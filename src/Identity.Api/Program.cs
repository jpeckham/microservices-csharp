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
builder.Services.AddSingleton<PasswordHasher<UserDocument>>();

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
    CancellationToken ct) =>
{
    if (string.IsNullOrWhiteSpace(request.Email) ||
        string.IsNullOrWhiteSpace(request.Password) ||
        string.IsNullOrWhiteSpace(request.Handle))
    {
        return Results.BadRequest(new { error = "Email, password, and handle are required." });
    }

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
        DisplayName = string.IsNullOrWhiteSpace(request.DisplayName) ? handle.TrimStart('@') : request.DisplayName.Trim(),
        RegisteredAt = DateTimeOffset.UtcNow
    };
    user.PasswordHash = hasher.HashPassword(user, request.Password);

    await users.InsertOneAsync(user, cancellationToken: ct);
    await events.PublishAsync(new UserCreated(user.Id, user.Username, user.Handle, user.DisplayName, DateTimeOffset.UtcNow), ct);

    return Results.Created($"/api/users/{user.Id}", ToProfile(user, null, 0, 0));
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
});

app.MapGet("/api/users/by-handle/{handle}", async (string handle, ClaimsPrincipal principal, IMongoCollection<UserDocument> users, CancellationToken ct) =>
{
    var normalized = NormalizeHandle(handle);
    var user = await users.Find(u => u.Handle == normalized).FirstOrDefaultAsync(ct);
    return user is null
        ? Results.NotFound(new { error = "Profile not found." })
        : Results.Ok(ToProfile(user, principal.GetUserId(), 0, 0));
});

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

app.Run();

static string NormalizeHandle(string handle)
{
    var normalized = handle.Trim().TrimStart('@').ToLowerInvariant();
    return $"@{normalized}";
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

public static class ClaimsPrincipalExtensions
{
    public static Guid? GetUserId(this ClaimsPrincipal principal)
    {
        var value = principal.FindFirstValue(ClaimTypes.NameIdentifier) ?? principal.FindFirstValue("sub");
        return Guid.TryParse(value, out var userId) ? userId : null;
    }
}
