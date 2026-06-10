using System.Security.Claims;
using System.Text;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.IdentityModel.Tokens;
using MongoDB.Bson;
using MongoDB.Bson.Serialization.Attributes;
using MongoDB.Driver;
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
    var databaseName = builder.Configuration["Mongo:DatabaseName"] ?? "SocialDb";
    return new MongoClient(connectionString).GetDatabase(databaseName);
});
builder.Services.AddSingleton(sp => sp.GetRequiredService<IMongoDatabase>().GetCollection<FollowDocument>("follows"));

var jwtKey = builder.Configuration["Jwt:Key"] ?? "local-development-secret-change-me-32";
builder.Services.AddAuthentication(JwtBearerDefaults.AuthenticationScheme)
    .AddJwtBearer(options =>
    {
        options.TokenValidationParameters = new TokenValidationParameters
        {
            ValidateIssuer = false,
            ValidateAudience = false,
            ValidateLifetime = true,
            ValidateIssuerSigningKey = true,
            IssuerSigningKey = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(jwtKey))
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

app.MapPost("/api/users/{followingId:guid}/follows", async (
    Guid followingId,
    ClaimsPrincipal principal,
    IMongoCollection<FollowDocument> follows,
    IEventPublisher events,
    CancellationToken ct) =>
{
    var followerId = principal.GetUserId();
    if (followerId is null) return Results.Unauthorized();
    if (followerId == followingId) return Results.BadRequest(new { error = "Users cannot follow themselves." });

    var existing = await follows.Find(f => f.FollowerId == followerId && f.FollowingId == followingId).FirstOrDefaultAsync(ct);
    if (existing is not null) return Results.NoContent();

    var follow = new FollowDocument
    {
        Id = Guid.NewGuid(),
        FollowerId = followerId.Value,
        FollowingId = followingId,
        CreatedAt = DateTimeOffset.UtcNow
    };
    await follows.InsertOneAsync(follow, cancellationToken: ct);
    await events.PublishAsync(new UserFollowed(follow.Id, follow.FollowerId, follow.FollowingId, DateTimeOffset.UtcNow), ct);
    return Results.NoContent();
}).RequireAuthorization();

app.MapDelete("/api/users/{followingId:guid}/follows", async (
    Guid followingId,
    ClaimsPrincipal principal,
    IMongoCollection<FollowDocument> follows,
    IEventPublisher events,
    CancellationToken ct) =>
{
    var followerId = principal.GetUserId();
    if (followerId is null) return Results.Unauthorized();
    var follow = await follows.FindOneAndDeleteAsync(f => f.FollowerId == followerId && f.FollowingId == followingId, cancellationToken: ct);
    if (follow is not null)
    {
        await events.PublishAsync(new UserUnfollowed(follow.Id, follow.FollowerId, follow.FollowingId, DateTimeOffset.UtcNow), ct);
    }
    return Results.NoContent();
}).RequireAuthorization();

app.MapGet("/api/users/{userId:guid}/followers", async (Guid userId, IMongoCollection<FollowDocument> follows, CancellationToken ct) =>
{
    var result = await follows.Find(f => f.FollowingId == userId).ToListAsync(ct);
    return Results.Ok(result.Select(f => f.FollowerId));
}).RequireAuthorization();

app.MapGet("/api/users/{userId:guid}/following", async (Guid userId, IMongoCollection<FollowDocument> follows, CancellationToken ct) =>
{
    var result = await follows.Find(f => f.FollowerId == userId).ToListAsync(ct);
    return Results.Ok(result.Select(f => f.FollowingId));
}).RequireAuthorization();

app.MapGet("/api/users/{userId:guid}/counts", async (Guid userId, IMongoCollection<FollowDocument> follows, CancellationToken ct) =>
{
    var followers = await follows.CountDocumentsAsync(f => f.FollowingId == userId, cancellationToken: ct);
    var following = await follows.CountDocumentsAsync(f => f.FollowerId == userId, cancellationToken: ct);
    return Results.Ok(new FollowCountsDto((int)followers, (int)following));
}).RequireAuthorization();

app.MapHealthChecks("/health");
app.Run();

public sealed class FollowDocument
{
    [BsonId]
    [BsonGuidRepresentation(GuidRepresentation.Standard)]
    public Guid Id { get; set; }
    [BsonGuidRepresentation(GuidRepresentation.Standard)]
    public Guid FollowerId { get; set; }
    [BsonGuidRepresentation(GuidRepresentation.Standard)]
    public Guid FollowingId { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
}

public sealed record FollowCountsDto(int FollowerCount, int FollowingCount);

public static class ClaimsPrincipalExtensions
{
    public static Guid? GetUserId(this ClaimsPrincipal principal)
    {
        var value = principal.FindFirstValue(ClaimTypes.NameIdentifier) ?? principal.FindFirstValue("sub");
        return Guid.TryParse(value, out var userId) ? userId : null;
    }
}

public partial class Program { }
