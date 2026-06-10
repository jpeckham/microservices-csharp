using System.Security.Claims;
using System.Text;
using Microsoft.AspNetCore.Authentication.JwtBearer;
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
    var databaseName = builder.Configuration["Mongo:DatabaseName"] ?? "EngagementDb";
    return new MongoClient(connectionString).GetDatabase(databaseName);
});
builder.Services.AddSingleton(sp => sp.GetRequiredService<IMongoDatabase>().GetCollection<LikeDocument>("likes"));
builder.Services.AddSingleton(sp => sp.GetRequiredService<IMongoDatabase>().GetCollection<CommentDocument>("comments"));

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

app.MapPost("/api/posts/{postId:guid}/likes", async (
    Guid postId,
    ClaimsPrincipal principal,
    IMongoCollection<LikeDocument> likes,
    IEventPublisher events,
    CancellationToken ct) =>
{
    var userId = principal.GetUserId();
    if (userId is null) return Results.Unauthorized();

    var existing = await likes.Find(l => l.PostId == postId && l.UserId == userId).FirstOrDefaultAsync(ct);
    if (existing is null)
    {
        existing = new LikeDocument { Id = Guid.NewGuid(), PostId = postId, UserId = userId.Value, CreatedAt = DateTimeOffset.UtcNow };
        await likes.InsertOneAsync(existing, cancellationToken: ct);
        await events.PublishAsync(new LikeAdded(existing.Id, postId, userId.Value, DateTimeOffset.UtcNow), ct);
    }

    var count = await likes.CountDocumentsAsync(l => l.PostId == postId, cancellationToken: ct);
    return Results.Ok(new { likeCount = (int)count });
}).RequireAuthorization();

app.MapDelete("/api/posts/{postId:guid}/likes", async (
    Guid postId,
    ClaimsPrincipal principal,
    IMongoCollection<LikeDocument> likes,
    IEventPublisher events,
    CancellationToken ct) =>
{
    var userId = principal.GetUserId();
    if (userId is null) return Results.Unauthorized();

    var existing = await likes.Find(l => l.PostId == postId && l.UserId == userId).FirstOrDefaultAsync(ct);
    if (existing is null)
        return Results.BadRequest(new { error = "You have not liked this post." });

    await likes.DeleteOneAsync(l => l.Id == existing.Id, ct);
    await events.PublishAsync(new LikeRemoved(existing.Id, postId, userId.Value, DateTimeOffset.UtcNow), ct);

    var count = await likes.CountDocumentsAsync(l => l.PostId == postId, cancellationToken: ct);
    return Results.Ok(new { likeCount = (int)count });
}).RequireAuthorization();

app.MapPost("/api/posts/{postId:guid}/comments", async (
    Guid postId,
    CreateCommentRequest request,
    ClaimsPrincipal principal,
    IMongoCollection<CommentDocument> comments,
    IEventPublisher events,
    CancellationToken ct) =>
{
    var userId = principal.GetUserId();
    if (userId is null) return Results.Unauthorized();
    if (string.IsNullOrWhiteSpace(request.Content)) return Results.BadRequest(new { error = "Comment content is required." });
    if (request.Content.Length > 280) return Results.BadRequest(new { error = "Comment content must be 280 characters or fewer." });

    var comment = new CommentDocument
    {
        Id = Guid.NewGuid(),
        PostId = postId,
        AuthorId = userId.Value,
        AuthorHandle = principal.FindFirstValue(AuthConstants.HandleClaim) ?? $"@{userId.Value.ToString()[..8]}",
        AuthorDisplayName = principal.FindFirstValue(AuthConstants.DisplayNameClaim) ?? "Unknown user",
        Content = request.Content.Trim(),
        CreatedAt = DateTimeOffset.UtcNow
    };
    await comments.InsertOneAsync(comment, cancellationToken: ct);
    await events.PublishAsync(new CommentAdded(comment.Id, postId, comment.AuthorId, comment.AuthorHandle, comment.AuthorDisplayName, comment.Content, DateTimeOffset.UtcNow), ct);
    return Results.Created($"/api/posts/{postId}/comments/{comment.Id}", ToDto(comment));
}).RequireAuthorization();

app.MapGet("/api/posts/{postId:guid}/comments", async (Guid postId, IMongoCollection<CommentDocument> comments, CancellationToken ct) =>
{
    var result = await comments.Find(c => c.PostId == postId).SortBy(c => c.CreatedAt).ToListAsync(ct);
    return Results.Ok(result.Select(ToDto));
}).RequireAuthorization();

app.MapDelete("/api/posts/{postId:guid}/comments/{commentId:guid}", async (
    Guid postId,
    Guid commentId,
    ClaimsPrincipal principal,
    IMongoCollection<CommentDocument> comments,
    IEventPublisher events,
    CancellationToken ct) =>
{
    var userId = principal.GetUserId();
    if (userId is null) return Results.Unauthorized();

    var comment = await comments.Find(c => c.Id == commentId && c.PostId == postId).FirstOrDefaultAsync(ct);
    if (comment is null) return Results.NotFound(new { error = "Comment not found." });
    if (comment.AuthorId != userId) return Results.Forbid();

    await comments.DeleteOneAsync(c => c.Id == commentId, ct);
    await events.PublishAsync(new CommentDeleted(comment.Id, comment.PostId, comment.AuthorId, DateTimeOffset.UtcNow), ct);
    return Results.NoContent();
}).RequireAuthorization();

app.MapGet("/api/posts/{postId:guid}/summary", async (
    Guid postId,
    ClaimsPrincipal principal,
    IMongoCollection<LikeDocument> likes,
    IMongoCollection<CommentDocument> comments,
    CancellationToken ct) =>
{
    var userId = principal.GetUserId();
    var likeCount = await likes.CountDocumentsAsync(l => l.PostId == postId, cancellationToken: ct);
    var commentCount = await comments.CountDocumentsAsync(c => c.PostId == postId, cancellationToken: ct);
    var likedByMe = userId.HasValue && await likes.Find(l => l.PostId == postId && l.UserId == userId).AnyAsync(ct);
    return Results.Ok(new EngagementSummaryDto((int)likeCount, (int)commentCount, likedByMe));
}).RequireAuthorization();

app.MapHealthChecks("/health");
app.Run();

static CommentDto ToDto(CommentDocument comment) =>
    new(comment.Id, comment.PostId, comment.AuthorId, comment.AuthorHandle, comment.AuthorDisplayName, comment.Content, comment.CreatedAt);

public sealed class LikeDocument
{
    [BsonId]
    [BsonGuidRepresentation(GuidRepresentation.Standard)]
    public Guid Id { get; set; }
    [BsonGuidRepresentation(GuidRepresentation.Standard)]
    public Guid PostId { get; set; }
    [BsonGuidRepresentation(GuidRepresentation.Standard)]
    public Guid UserId { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
}

public sealed class CommentDocument
{
    [BsonId]
    [BsonGuidRepresentation(GuidRepresentation.Standard)]
    public Guid Id { get; set; }
    [BsonGuidRepresentation(GuidRepresentation.Standard)]
    public Guid PostId { get; set; }
    [BsonGuidRepresentation(GuidRepresentation.Standard)]
    public Guid AuthorId { get; set; }
    public string AuthorHandle { get; set; } = "";
    public string AuthorDisplayName { get; set; } = "";
    public string Content { get; set; } = "";
    public DateTimeOffset CreatedAt { get; set; }
}

public sealed record CreateCommentRequest(string Content);
public sealed record CommentDto(Guid CommentId, Guid PostId, Guid AuthorId, string AuthorHandle, string AuthorDisplayName, string Content, DateTimeOffset CreatedAt);
public sealed record EngagementSummaryDto(int LikeCount, int CommentCount, bool LikedByMe);

public static class ClaimsPrincipalExtensions
{
    public static Guid? GetUserId(this ClaimsPrincipal principal)
    {
        var value = principal.FindFirstValue(ClaimTypes.NameIdentifier) ?? principal.FindFirstValue("sub");
        return Guid.TryParse(value, out var userId) ? userId : null;
    }
}

public partial class Program { }
