using System.Security.Claims;
using System.Text;
using System.Text.RegularExpressions;
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
    var databaseName = builder.Configuration["Mongo:DatabaseName"] ?? "PostDb";
    return new MongoClient(connectionString).GetDatabase(databaseName);
});
builder.Services.AddSingleton(sp => sp.GetRequiredService<IMongoDatabase>().GetCollection<PostDocument>("posts"));

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

app.MapPost("/api/posts", async (
    CreatePostRequest request,
    ClaimsPrincipal principal,
    IMongoCollection<PostDocument> posts,
    IEventPublisher events,
    CancellationToken ct) =>
{
    var userId = principal.GetUserId();
    if (userId is null) return Results.Unauthorized();
    if (string.IsNullOrWhiteSpace(request.Content) || request.Content.Length > 280)
        return Results.BadRequest(new { error = "Post content must be between 1 and 280 characters." });

    var content = request.Content.Trim();
    var post = new PostDocument
    {
        Id = Guid.NewGuid(),
        AuthorId = userId.Value,
        AuthorHandle = principal.FindFirstValue(AuthConstants.HandleClaim) ?? $"@{userId.Value.ToString()[..8]}",
        AuthorDisplayName = principal.FindFirstValue(AuthConstants.DisplayNameClaim) ?? "Unknown user",
        Content = content,
        Hashtags = HashtagExtractor.Extract(content),
        Mentions = MentionExtractor.Extract(content),
        PostedAt = DateTimeOffset.UtcNow,
        UpdatedAt = DateTimeOffset.UtcNow
    };

    await posts.InsertOneAsync(post, cancellationToken: ct);
    await events.PublishAsync(new PostCreated(post.Id, post.AuthorId, post.AuthorHandle, post.AuthorDisplayName, post.Content, DateTimeOffset.UtcNow), ct);
    return Results.Created($"/api/posts/{post.Id}", ToDto(post));
}).RequireAuthorization();

app.MapPut("/api/posts/{id:guid}", async (
    Guid id,
    UpdatePostRequest request,
    ClaimsPrincipal principal,
    IMongoCollection<PostDocument> posts,
    IEventPublisher events,
    CancellationToken ct) =>
{
    var userId = principal.GetUserId();
    if (userId is null) return Results.Unauthorized();
    if (string.IsNullOrWhiteSpace(request.Content) || request.Content.Length > 280)
        return Results.BadRequest(new { error = "Post content must be between 1 and 280 characters." });

    var existing = await posts.Find(p => p.Id == id).FirstOrDefaultAsync(ct);
    if (existing is null) return Results.NotFound(new { error = "Post not found." });
    if (existing.AuthorId != userId) return Results.Forbid();

    var updatedContent = request.Content.Trim();
    var update = Builders<PostDocument>.Update
        .Set(p => p.Content, updatedContent)
        .Set(p => p.Hashtags, HashtagExtractor.Extract(updatedContent))
        .Set(p => p.Mentions, MentionExtractor.Extract(updatedContent))
        .Set(p => p.UpdatedAt, DateTimeOffset.UtcNow);
    var post = await posts.FindOneAndUpdateAsync(
        p => p.Id == id,
        update,
        new FindOneAndUpdateOptions<PostDocument> { ReturnDocument = ReturnDocument.After },
        ct);
    if (post is null) return Results.NotFound(new { error = "Post not found." });

    await events.PublishAsync(new PostUpdated(post.Id, post.AuthorId, post.Content, DateTimeOffset.UtcNow), ct);
    return Results.Ok(ToDto(post));
}).RequireAuthorization();

app.MapDelete("/api/posts/{id:guid}", async (
    Guid id,
    ClaimsPrincipal principal,
    IMongoCollection<PostDocument> posts,
    IEventPublisher events,
    CancellationToken ct) =>
{
    var userId = principal.GetUserId();
    if (userId is null) return Results.Unauthorized();

    var toDelete = await posts.Find(p => p.Id == id).FirstOrDefaultAsync(ct);
    if (toDelete is null) return Results.NotFound(new { error = "Post not found." });
    if (toDelete.AuthorId != userId) return Results.Forbid();

    var post = await posts.FindOneAndDeleteAsync(p => p.Id == id, cancellationToken: ct);
    if (post is null) return Results.NotFound(new { error = "Post not found." });

    await events.PublishAsync(new PostDeleted(post.Id, post.AuthorId, DateTimeOffset.UtcNow), ct);
    return Results.NoContent();
}).RequireAuthorization();

app.MapPost("/api/posts/{postId:guid}/replies", async (
    Guid postId,
    CreatePostRequest request,
    ClaimsPrincipal principal,
    IMongoCollection<PostDocument> posts,
    IEventPublisher events,
    CancellationToken ct) =>
{
    var userId = principal.GetUserId();
    if (userId is null) return Results.Unauthorized();
    if (string.IsNullOrWhiteSpace(request.Content) || request.Content.Length > 280)
        return Results.BadRequest(new { error = "Post content must be between 1 and 280 characters." });

    var parent = await posts.Find(p => p.Id == postId).FirstOrDefaultAsync(ct);
    if (parent is null) return Results.NotFound(new { error = "Parent post not found." });

    var content = request.Content.Trim();
    var post = new PostDocument
    {
        Id = Guid.NewGuid(),
        AuthorId = userId.Value,
        AuthorHandle = principal.FindFirstValue(AuthConstants.HandleClaim) ?? $"@{userId.Value.ToString()[..8]}",
        AuthorDisplayName = principal.FindFirstValue(AuthConstants.DisplayNameClaim) ?? "Unknown user",
        Content = content,
        ParentPostId = postId,
        Hashtags = HashtagExtractor.Extract(content),
        Mentions = MentionExtractor.Extract(content),
        PostedAt = DateTimeOffset.UtcNow,
        UpdatedAt = DateTimeOffset.UtcNow
    };

    await posts.InsertOneAsync(post, cancellationToken: ct);
    await events.PublishAsync(new PostCreated(post.Id, post.AuthorId, post.AuthorHandle, post.AuthorDisplayName, post.Content, DateTimeOffset.UtcNow), ct);
    return Results.Created($"/api/posts/{post.Id}", ToDto(post));
}).RequireAuthorization();

app.MapGet("/api/posts/{id:guid}", async (Guid id, IMongoCollection<PostDocument> posts, CancellationToken ct) =>
{
    var post = await posts.Find(p => p.Id == id).FirstOrDefaultAsync(ct);
    return post is null ? Results.NotFound(new { error = "Post not found." }) : Results.Ok(ToDto(post));
}).RequireAuthorization();

app.MapGet("/api/posts/by-user/{userId:guid}", async (Guid userId, int limit, int offset, IMongoCollection<PostDocument> posts, CancellationToken ct) =>
{
    var result = await posts.Find(p => p.AuthorId == userId)
        .SortByDescending(p => p.PostedAt)
        .Skip(offset)
        .Limit(limit is <= 0 or > 100 ? 20 : limit)
        .ToListAsync(ct);
    return Results.Ok(result.Select(ToDto));
}).RequireAuthorization();

app.MapGet("/api/posts/search", async (string? q, int limit, int offset, IMongoCollection<PostDocument> posts, CancellationToken ct) =>
{
    if (string.IsNullOrWhiteSpace(q))
        return Results.BadRequest(new { error = "Query parameter 'q' is required." });

    var escaped = System.Text.RegularExpressions.Regex.Escape(q);
    var filter = Builders<PostDocument>.Filter.Regex(p => p.Content, new MongoDB.Bson.BsonRegularExpression(escaped, "i"));
    var result = await posts.Find(filter)
        .SortByDescending(p => p.PostedAt)
        .Skip(offset)
        .Limit(limit is <= 0 or > 100 ? 20 : limit)
        .ToListAsync(ct);
    return Results.Ok(new SearchResultsDto(result.Select(ToDto).ToList(), q ?? "", limit, offset));
}).RequireAuthorization();

app.MapGet("/api/posts/recent", async (int? limit, IMongoCollection<PostDocument> posts, CancellationToken ct) =>
{
    var take = Math.Clamp(limit ?? 20, 1, 100);
    var result = await posts.Find(_ => true)
        .SortByDescending(p => p.PostedAt)
        .Limit(take)
        .ToListAsync(ct);
    return Results.Ok(result.Select(ToDto));
}).RequireAuthorization();

app.MapHealthChecks("/health");
app.Run();

static PostDto ToDto(PostDocument post) =>
    new(post.Id, post.AuthorId, post.AuthorHandle, post.AuthorDisplayName, post.Content, post.PostedAt, post.UpdatedAt, post.Hashtags, post.Mentions, post.ParentPostId);

public sealed class PostDocument
{
    [BsonId]
    [BsonGuidRepresentation(GuidRepresentation.Standard)]
    public Guid Id { get; set; }
    [BsonGuidRepresentation(GuidRepresentation.Standard)]
    public Guid AuthorId { get; set; }
    public string AuthorHandle { get; set; } = "";
    public string AuthorDisplayName { get; set; } = "";
    public string Content { get; set; } = "";
    [BsonGuidRepresentation(GuidRepresentation.Standard)]
    public Guid? ParentPostId { get; set; }
    public List<string> Hashtags { get; set; } = [];
    public List<string> Mentions { get; set; } = [];
    public DateTimeOffset PostedAt { get; set; }
    public DateTimeOffset UpdatedAt { get; set; }
}

public sealed record CreatePostRequest(string Content);
public sealed record UpdatePostRequest(string Content);
public sealed record PostDto(Guid PostId, Guid AuthorId, string AuthorHandle, string AuthorDisplayName, string Content, DateTimeOffset PostedAt, DateTimeOffset UpdatedAt, List<string> Hashtags, List<string> Mentions, Guid? ParentPostId = null);
public sealed record SearchResultsDto(List<PostDto> Posts, string Query, int Limit, int Offset);

public static class HashtagExtractor
{
    private static readonly Regex Pattern = new(@"#([a-zA-Z][a-zA-Z0-9_]*)", RegexOptions.Compiled, TimeSpan.FromSeconds(1));

    public static List<string> Extract(string content)
    {
        try
        {
            return Pattern.Matches(content)
                .Select(m => m.Groups[1].Value.ToLowerInvariant())
                .Distinct()
                .ToList();
        }
        catch (RegexMatchTimeoutException)
        {
            return [];
        }
    }
}

public static class MentionExtractor
{
    private static readonly Regex Pattern = new(@"@([a-zA-Z][a-zA-Z0-9_]*)", RegexOptions.Compiled, TimeSpan.FromSeconds(1));

    public static List<string> Extract(string content)
    {
        try
        {
            return Pattern.Matches(content)
                .Select(m => m.Groups[1].Value.ToLowerInvariant())
                .Distinct()
                .ToList();
        }
        catch (RegexMatchTimeoutException)
        {
            return [];
        }
    }
}

public static class ClaimsPrincipalExtensions
{
    public static Guid? GetUserId(this ClaimsPrincipal principal)
    {
        var value = principal.FindFirstValue(ClaimTypes.NameIdentifier) ?? principal.FindFirstValue("sub");
        return Guid.TryParse(value, out var userId) ? userId : null;
    }
}

public partial class Program { }
