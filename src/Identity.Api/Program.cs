using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Text;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Identity;
using Microsoft.IdentityModel.Tokens;
using MongoDB.Bson;
using MongoDB.Bson.Serialization.Attributes;
using MongoDB.Driver;
using Shared.Contracts.Auth;
using Shared.Contracts.Events;
using Shared.Contracts.Messaging;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen();
builder.Services.AddHttpClient();
builder.Services.AddSingleton<IEventPublisher, ConfiguredEventPublisher>();
builder.Services.AddSingleton(_ =>
{
    var connectionString = builder.Configuration["Mongo:ConnectionString"] ?? "mongodb://localhost:27017";
    var databaseName = builder.Configuration["Mongo:DatabaseName"] ?? "IdentityDb";
    return new MongoClient(connectionString).GetDatabase(databaseName);
});
builder.Services.AddSingleton(sp => sp.GetRequiredService<IMongoDatabase>().GetCollection<UserDocument>("users"));
builder.Services.AddSingleton(sp => sp.GetRequiredService<IMongoDatabase>().GetCollection<PasswordResetTokenDocument>("passwordResetTokens"));
builder.Services.AddSingleton(sp => sp.GetRequiredService<IMongoDatabase>().GetCollection<PendingRegistrationDocument>("pendingRegistrations"));
builder.Services.AddSingleton<PasswordHasher<UserDocument>>();
builder.Services.AddSingleton<InMemoryEmailSender>();
builder.Services.AddSingleton<IEmailSender>(sp => sp.GetRequiredService<InMemoryEmailSender>());

var jwtKey = builder.Configuration["Jwt:Key"] ?? "local-development-secret-change-me-32";
var signingKey = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(jwtKey));

builder.Services.AddAuthentication(JwtBearerDefaults.AuthenticationScheme)
    .AddJwtBearer(options =>
    {
        options.TokenValidationParameters = new TokenValidationParameters
        {
            ValidateIssuer = false,
            ValidateAudience = false,
            ValidateLifetime = true,
            ValidateIssuerSigningKey = true,
            IssuerSigningKey = signingKey
        };
    });
builder.Services.AddAuthorization();
builder.Services.AddHealthChecks();

var app = builder.Build();

if (app.Environment.IsDevelopment())
{
    app.UseSwagger();
    app.UseSwaggerUI();
}

app.UseAuthentication();
app.UseAuthorization();

app.MapPost("/api/users/register", async (
    RegisterRequest request,
    IMongoCollection<UserDocument> users,
    PasswordHasher<UserDocument> hasher,
    IEventPublisher events,
    IConfiguration configuration,
    CancellationToken ct) =>
{
    if (string.IsNullOrWhiteSpace(request.Email) ||
        string.IsNullOrWhiteSpace(request.Password) ||
        string.IsNullOrWhiteSpace(request.Handle) ||
        string.IsNullOrWhiteSpace(request.DisplayName))
    {
        return Results.BadRequest(new { error = "Email, password, handle, and display name are required." });
    }

    if (!IsValidEmail(request.Email))
        return Results.BadRequest(new { error = "A valid email address is required." });

    if (request.Password.Length < 8 || !request.Password.Any(char.IsDigit))
        return Results.BadRequest(new { error = "Password must be at least 8 characters and contain at least one digit." });

    if (!IsValidHandle(request.Handle))
        return Results.BadRequest(new { error = "Handle must contain only letters, digits, and underscores." });

    if (request.DisplayName.Trim().Length > 50)
        return Results.BadRequest(new { error = "Display name must be 50 characters or fewer." });

    var handle = NormalizeHandle(request.Handle);
    var existing = await users.Find(u => u.Email == request.Email || u.Handle == handle).AnyAsync(ct);
    if (existing)
    {
        return Results.Conflict(new { error = "A user with that email or handle already exists." });
    }

    var user = new UserDocument
    {
        Id = Guid.NewGuid(),
        Email = request.Email.Trim().ToLowerInvariant(),
        Username = handle.TrimStart('@'),
        Handle = handle,
        DisplayName = request.DisplayName.Trim(),
        RegisteredAt = DateTimeOffset.UtcNow
    };
    user.PasswordHash = hasher.HashPassword(user, request.Password);

    await users.InsertOneAsync(user, cancellationToken: ct);
    await events.PublishAsync(new UserCreated(user.Id, user.Username, user.Handle, user.DisplayName, DateTimeOffset.UtcNow), ct);

    return Results.Created($"/api/users/{user.Id}", new TokenResponse(CreateToken(user, configuration), user.Id, user.Username, user.Handle, user.DisplayName));
});

app.MapPost("/api/users/login", async (
    LoginRequest request,
    IMongoCollection<UserDocument> users,
    PasswordHasher<UserDocument> hasher,
    IConfiguration configuration,
    CancellationToken ct) =>
{
    var email = request.Email.Trim().ToLowerInvariant();
    var user = await users.Find(u => u.Email == email).FirstOrDefaultAsync(ct);
    if (user is null ||
        hasher.VerifyHashedPassword(user, user.PasswordHash, request.Password) == PasswordVerificationResult.Failed)
    {
        return Results.Unauthorized();
    }

    return Results.Ok(new TokenResponse(CreateToken(user, configuration), user.Id, user.Username, user.Handle, user.DisplayName));
});

app.MapGet("/api/users/me", async (ClaimsPrincipal principal, IMongoCollection<UserDocument> users, CancellationToken ct) =>
{
    var userId = principal.GetUserId();
    if (userId is null) return Results.Unauthorized();

    var user = await users.Find(u => u.Id == userId).FirstOrDefaultAsync(ct);
    return user is null ? Results.NotFound(new { error = "User not found." }) : Results.Ok(ToProfile(user, userId, 0, 0));
}).RequireAuthorization();

app.MapGet("/api/users/{id:guid}", async (Guid id, IMongoCollection<UserDocument> users, CancellationToken ct) =>
{
    var user = await users.Find(u => u.Id == id).FirstOrDefaultAsync(ct);
    return user is null ? Results.NotFound(new { error = "User not found." }) : Results.Ok(ToProfile(user, null, 0, 0));
}).RequireAuthorization();

app.MapGet("/api/users/by-handle/{handle}", async (string handle, ClaimsPrincipal principal, IMongoCollection<UserDocument> users, CancellationToken ct) =>
{
    var normalized = NormalizeHandle(handle);
    var user = await users.Find(u => u.Handle == normalized).FirstOrDefaultAsync(ct);
    return user is null
        ? Results.NotFound(new { error = "Profile not found." })
        : Results.Ok(ToProfile(user, principal.GetUserId(), 0, 0));
}).RequireAuthorization();

app.MapGet("/api/users/search", async (string? q, int limit, int offset, IMongoCollection<UserDocument> users, CancellationToken ct) =>
{
    if (string.IsNullOrWhiteSpace(q))
        return Results.BadRequest(new { error = "Query parameter 'q' is required." });

    var clampedLimit = limit is <= 0 or > 50 ? 20 : limit;
    var regex = new MongoDB.Bson.BsonRegularExpression(System.Text.RegularExpressions.Regex.Escape(q), "i");
    var filter = Builders<UserDocument>.Filter.Or(
        Builders<UserDocument>.Filter.Regex(u => u.Handle, regex),
        Builders<UserDocument>.Filter.Regex(u => u.DisplayName, regex));
    var result = await users.Find(filter)
        .Skip(offset)
        .Limit(clampedLimit)
        .ToListAsync(ct);
    return Results.Ok(result.Select(u => new UserSearchResultDto(u.Id, u.Handle, u.DisplayName)));
}).RequireAuthorization();

app.MapPut("/api/users/me/display-name", async (
    UpdateDisplayNameRequest request,
    ClaimsPrincipal principal,
    IMongoCollection<UserDocument> users,
    IEventPublisher events,
    CancellationToken ct) =>
{
    var userId = principal.GetUserId();
    if (userId is null) return Results.Unauthorized();
    if (string.IsNullOrWhiteSpace(request.DisplayName)) return Results.BadRequest(new { error = "Display name is required." });
    if (request.DisplayName.Trim().Length > 50) return Results.BadRequest(new { error = "Display name must be 50 characters or fewer." });

    var update = Builders<UserDocument>.Update.Set(u => u.DisplayName, request.DisplayName.Trim());
    var user = await users.FindOneAndUpdateAsync(
        u => u.Id == userId,
        update,
        new FindOneAndUpdateOptions<UserDocument> { ReturnDocument = ReturnDocument.After },
        ct);
    if (user is null) return Results.NotFound(new { error = "User not found." });

    await events.PublishAsync(new UserProfileUpdated(user.Id, user.Handle, user.DisplayName, DateTimeOffset.UtcNow), ct);
    return Results.NoContent();
}).RequireAuthorization();

app.MapPost("/api/password-reset-requests", async (
    PasswordResetRequest request,
    IMongoCollection<UserDocument> users,
    IMongoCollection<PasswordResetTokenDocument> tokens,
    IEmailSender emailSender,
    IConfiguration configuration,
    CancellationToken ct) =>
{
    var email = request.Email?.Trim().ToLowerInvariant();
    if (!string.IsNullOrWhiteSpace(email))
    {
        var user = await users.Find(u => u.Email == email).FirstOrDefaultAsync(ct);
        if (user is not null)
        {
            var tokenDoc = new PasswordResetTokenDocument
            {
                Id = Guid.NewGuid(),
                UserId = user.Id,
                Token = Guid.NewGuid().ToString("N"),
                ExpiresAt = DateTimeOffset.UtcNow.AddMinutes(5),
                Consumed = false
            };
            await tokens.InsertOneAsync(tokenDoc, cancellationToken: ct);
            var baseUrl = configuration["PasswordReset:BaseUrl"] ?? "http://localhost:5106";
            var resetLink = $"{baseUrl}/reset-password?token={tokenDoc.Token}";
            await emailSender.SendAsync(user.Email, "Reset your password",
                $"Use this one-time link within 5 minutes: {resetLink}");
        }
    }
    return Results.NoContent();
});

app.MapPost("/api/password-resets", async (
    ResetPasswordRequest request,
    IMongoCollection<UserDocument> users,
    IMongoCollection<PasswordResetTokenDocument> tokens,
    PasswordHasher<UserDocument> hasher,
    CancellationToken ct) =>
{
    if (string.IsNullOrWhiteSpace(request.Token) || string.IsNullOrWhiteSpace(request.NewPassword))
        return Results.BadRequest(new { error = "Token and new password are required." });

    if (request.NewPassword.Length < 8 || !request.NewPassword.Any(char.IsDigit))
        return Results.BadRequest(new { error = "Password must be at least 8 characters and contain at least one digit." });

    var tokenDoc = await tokens.Find(t => t.Token == request.Token).FirstOrDefaultAsync(ct);
    if (tokenDoc is null || tokenDoc.Consumed || tokenDoc.ExpiresAt < DateTimeOffset.UtcNow)
        return Results.BadRequest(new { error = "Token is invalid or has expired." });

    var user = await users.Find(u => u.Id == tokenDoc.UserId).FirstOrDefaultAsync(ct);
    if (user is null)
        return Results.BadRequest(new { error = "Token is invalid or has expired." });

    var newHash = hasher.HashPassword(user, request.NewPassword);
    await users.UpdateOneAsync(u => u.Id == user.Id,
        Builders<UserDocument>.Update.Set(u => u.PasswordHash, newHash), cancellationToken: ct);
    await tokens.UpdateOneAsync(t => t.Id == tokenDoc.Id,
        Builders<PasswordResetTokenDocument>.Update.Set(t => t.Consumed, true), cancellationToken: ct);

    return Results.NoContent();
});

app.MapPost("/api/registrations", async (
    StartRegistrationRequest request,
    IMongoCollection<UserDocument> users,
    IMongoCollection<PendingRegistrationDocument> pendingRegistrations,
    PasswordHasher<UserDocument> hasher,
    IEmailSender emailSender,
    CancellationToken ct) =>
{
    if (string.IsNullOrWhiteSpace(request.Email) ||
        string.IsNullOrWhiteSpace(request.Password) ||
        string.IsNullOrWhiteSpace(request.Handle) ||
        string.IsNullOrWhiteSpace(request.DisplayName))
    {
        return Results.BadRequest(new { error = "Email, password, handle, and display name are required." });
    }

    if (!IsValidEmail(request.Email))
        return Results.BadRequest(new { error = "A valid email address is required." });

    if (request.Password.Length < 8 || !request.Password.Any(char.IsDigit))
        return Results.BadRequest(new { error = "Password must be at least 8 characters and contain at least one digit." });

    if (!IsValidHandle(request.Handle))
        return Results.BadRequest(new { error = "Handle must contain only letters, digits, and underscores." });

    if (request.DisplayName.Trim().Length > 50)
        return Results.BadRequest(new { error = "Display name must be 50 characters or fewer." });

    var email = request.Email.Trim().ToLowerInvariant();
    var handle = NormalizeHandle(request.Handle);

    var existingUser = await users.Find(u => u.Email == email || u.Handle == handle).AnyAsync(ct);
    if (existingUser)
        return Results.Conflict(new { error = "A user with that email or handle already exists." });

    var existingPending = await pendingRegistrations.Find(p => p.Email == email || p.Handle == handle).AnyAsync(ct);
    if (existingPending)
        return Results.Conflict(new { error = "A user with that email or handle already exists." });

    var code = Random.Shared.Next(100000, 999999).ToString();
    var tempUser = new UserDocument { Id = Guid.NewGuid() };
    var passwordHash = hasher.HashPassword(tempUser, request.Password);

    var pending = new PendingRegistrationDocument
    {
        Id = Guid.NewGuid(),
        Handle = handle,
        Email = email,
        DisplayName = request.DisplayName.Trim(),
        PasswordHash = passwordHash,
        VerificationCode = code,
        ExpiresAt = DateTimeOffset.UtcNow.AddHours(24),
        CreatedAt = DateTimeOffset.UtcNow
    };

    await pendingRegistrations.InsertOneAsync(pending, cancellationToken: ct);
    await emailSender.SendAsync(email, "Verify your registration",
        $"Your verification code is: {code}");

    return Results.Accepted(null, new { pendingRegistrationId = pending.Id });
});

app.MapPost("/api/registrations/verify", async (
    VerifyRegistrationRequest request,
    IMongoCollection<UserDocument> users,
    IMongoCollection<PendingRegistrationDocument> pendingRegistrations,
    IEventPublisher events,
    IConfiguration configuration,
    CancellationToken ct) =>
{
    if (string.IsNullOrWhiteSpace(request.Code))
        return Results.BadRequest(new { error = "Verification code is required." });

    var pending = await pendingRegistrations.Find(p => p.Id == request.PendingRegistrationId).FirstOrDefaultAsync(ct);
    if (pending is null || pending.ExpiresAt < DateTimeOffset.UtcNow)
        return Results.BadRequest(new { error = "Registration not found or has expired." });

    if (pending.VerificationCode != NormalizeCode(request.Code))
        return Results.BadRequest(new { error = "Invalid verification code." });

    var user = new UserDocument
    {
        Id = Guid.NewGuid(),
        Email = pending.Email,
        Username = pending.Handle.TrimStart('@'),
        Handle = pending.Handle,
        DisplayName = pending.DisplayName,
        PasswordHash = pending.PasswordHash,
        RegisteredAt = DateTimeOffset.UtcNow
    };

    await users.InsertOneAsync(user, cancellationToken: ct);
    await pendingRegistrations.DeleteOneAsync(p => p.Id == pending.Id, ct);
    await events.PublishAsync(new UserCreated(user.Id, user.Username, user.Handle, user.DisplayName, DateTimeOffset.UtcNow), ct);

    return Results.Created($"/api/users/{user.Id}",
        new TokenResponse(CreateToken(user, configuration), user.Id, user.Username, user.Handle, user.DisplayName));
});

app.MapPut("/api/users/me/password", async (
    ChangePasswordRequest request,
    ClaimsPrincipal principal,
    IMongoCollection<UserDocument> users,
    PasswordHasher<UserDocument> hasher,
    CancellationToken ct) =>
{
    var userId = principal.GetUserId();
    if (userId is null) return Results.Unauthorized();

    if (string.IsNullOrWhiteSpace(request.NewPassword) ||
        request.NewPassword.Length < 8 ||
        !request.NewPassword.Any(char.IsDigit))
        return Results.BadRequest(new { error = "New password must be at least 8 characters and contain at least one digit." });

    var user = await users.Find(u => u.Id == userId).FirstOrDefaultAsync(ct);
    if (user is null) return Results.Unauthorized();

    if (hasher.VerifyHashedPassword(user, user.PasswordHash, request.CurrentPassword) == PasswordVerificationResult.Failed)
        return Results.BadRequest(new { error = "Current password is incorrect." });

    var newHash = hasher.HashPassword(user, request.NewPassword);
    await users.UpdateOneAsync(u => u.Id == userId,
        Builders<UserDocument>.Update.Set(u => u.PasswordHash, newHash), cancellationToken: ct);

    return Results.NoContent();
}).RequireAuthorization();

if (app.Environment.IsDevelopment())
{
    app.MapGet("/dev/emails", (InMemoryEmailSender sender) => Results.Ok(sender.Messages));
}

app.MapHealthChecks("/health");
app.Run();

static string NormalizeHandle(string handle)
{
    var normalized = handle.Trim().TrimStart('@').ToLowerInvariant();
    return $"@{normalized}";
}

static bool IsValidHandle(string handle)
{
    var raw = handle.Trim().TrimStart('@');
    return raw.Length > 0 && raw.All(c => char.IsLetterOrDigit(c) || c == '_');
}

static bool IsValidEmail(string email)
{
    try { _ = new System.Net.Mail.MailAddress(email); return true; }
    catch { return false; }
}

static string NormalizeCode(string code)
{
    var digits = new string(code.Where(char.IsDigit).ToArray());
    return digits.Length == 6 ? digits : code.Trim();
}

static UserProfileDto ToProfile(UserDocument user, Guid? requesterId, int followerCount, int followingCount) =>
    new(user.Id, user.Username, user.Handle, user.DisplayName, user.RegisteredAt, followerCount, followingCount, requesterId == user.Id, false);

static string CreateToken(UserDocument user, IConfiguration configuration)
{
    var jwtKey = configuration["Jwt:Key"] ?? "local-development-secret-change-me-32";
    var claims = new[]
    {
        new Claim(ClaimTypes.NameIdentifier, user.Id.ToString()),
        new Claim(ClaimTypes.Name, user.Username),
        new Claim(AuthConstants.HandleClaim, user.Handle),
        new Claim(AuthConstants.DisplayNameClaim, user.DisplayName)
    };
    var token = new JwtSecurityToken(
        claims: claims,
        expires: DateTime.UtcNow.AddDays(7),
        signingCredentials: new SigningCredentials(new SymmetricSecurityKey(Encoding.UTF8.GetBytes(jwtKey)), SecurityAlgorithms.HmacSha256));
    return new JwtSecurityTokenHandler().WriteToken(token);
}

public sealed class UserDocument
{
    [BsonId]
    [BsonGuidRepresentation(GuidRepresentation.Standard)]
    public Guid Id { get; set; }
    public string Email { get; set; } = "";
    public string Username { get; set; } = "";
    public string Handle { get; set; } = "";
    public string DisplayName { get; set; } = "";
    public string PasswordHash { get; set; } = "";
    public DateTimeOffset RegisteredAt { get; set; }
}

public sealed record RegisterRequest(string Email, string Password, string Handle, string? DisplayName);
public sealed record LoginRequest(string Email, string Password);
public sealed record UpdateDisplayNameRequest(string DisplayName);
public sealed record TokenResponse(string Token, Guid UserId, string Username, string Handle, string DisplayName);
public sealed record UserProfileDto(Guid UserId, string Username, string Handle, string DisplayName, DateTimeOffset RegisteredAt, int FollowerCount, int FollowingCount, bool IsOwnProfile, bool IsFollowedByMe);
public sealed record UserSearchResultDto(Guid UserId, string Handle, string DisplayName);
public sealed record PasswordResetRequest(string Email);
public sealed record ResetPasswordRequest(string Token, string NewPassword);
public sealed record ChangePasswordRequest(string CurrentPassword, string NewPassword);
public sealed record StartRegistrationRequest(string Email, string Password, string Handle, string? DisplayName);
public sealed record VerifyRegistrationRequest(Guid PendingRegistrationId, string Code);

public sealed class PendingRegistrationDocument
{
    [BsonId]
    [BsonGuidRepresentation(GuidRepresentation.Standard)]
    public Guid Id { get; set; }
    public string Handle { get; set; } = "";
    public string Email { get; set; } = "";
    public string DisplayName { get; set; } = "";
    public string PasswordHash { get; set; } = "";
    public string VerificationCode { get; set; } = "";
    public DateTimeOffset ExpiresAt { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
}

public sealed class PasswordResetTokenDocument
{
    [BsonId]
    [BsonGuidRepresentation(GuidRepresentation.Standard)]
    public Guid Id { get; set; }
    [BsonGuidRepresentation(GuidRepresentation.Standard)]
    public Guid UserId { get; set; }
    public string Token { get; set; } = "";
    public DateTimeOffset ExpiresAt { get; set; }
    public bool Consumed { get; set; }
}

public interface IEmailSender
{
    Task SendAsync(string to, string subject, string body);
}

public sealed class InMemoryEmailSender : IEmailSender
{
    private readonly List<EmailMessage> _messages = [];

    public Task SendAsync(string to, string subject, string body)
    {
        _messages.Add(new EmailMessage(to, subject, body, DateTimeOffset.UtcNow));
        return Task.CompletedTask;
    }

    public IReadOnlyList<EmailMessage> Messages => _messages.AsReadOnly();
}

public sealed record EmailMessage(string To, string Subject, string Body, DateTimeOffset SentAt);

public static class ClaimsPrincipalExtensions
{
    public static Guid? GetUserId(this ClaimsPrincipal principal)
    {
        var value = principal.FindFirstValue(ClaimTypes.NameIdentifier) ?? principal.FindFirstValue("sub");
        return Guid.TryParse(value, out var userId) ? userId : null;
    }
}

public partial class Program { }
