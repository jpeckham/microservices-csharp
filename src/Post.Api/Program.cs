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

    var existing = await posts.Find(p => p.Id == id && !p.IsDeleted).FirstOrDefaultAsync(ct);
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

    var toDelete = await posts.Find(p => p.Id == id && !p.IsDeleted).FirstOrDefaultAsync(ct);
    if (toDelete is null) return Results.NotFound(new { error = "Post not found." });
    if (toDelete.AuthorId != userId) return Results.Forbid();

    await posts.UpdateOneAsync(p => p.Id == id,
        Builders<PostDocument>.Update.Set(p => p.IsDeleted, true), cancellationToken: ct);
    await events.PublishAsync(new PostDeleted(toDelete.Id, toDelete.AuthorId, DateTimeOffset.UtcNow), ct);
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

    var parent = await posts.Find(p => p.Id == postId && !p.IsDeleted).FirstOrDefaultAsync(ct);
    if (parent is null) return Results.NotFound(new { error = "Parent post not found." });

    var content = PrefixReplyContent(parent.AuthorHandle, request.Content.Trim());
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
    await posts.UpdateOneAsync(
        p => p.Id == postId,
        Builders<PostDocument>.Update.Inc(p => p.ReplyCount, 1),
        cancellationToken: ct);
    await events.PublishAsync(new PostCreated(post.Id, post.AuthorId, post.AuthorHandle, post.AuthorDisplayName, post.Content, DateTimeOffset.UtcNow), ct);
    return Results.Created($"/api/posts/{post.Id}", ToDto(post));
}).RequireAuthorization();

app.MapGet("/api/posts/{id:guid}", async (Guid id, ClaimsPrincipal principal, IMongoCollection<PostDocument> posts, CancellationToken ct) =>
{
    var post = await posts.Find(p => p.Id == id && !p.IsDeleted).FirstOrDefaultAsync(ct);
    if (post is null) return Results.NotFound(new { error = "Post not found." });
    var userId = principal.GetUserId();
    var repostedByMe = userId.HasValue && await posts.Find(p => p.OriginalPostId == id && p.AuthorId == userId && !p.IsDeleted).AnyAsync(ct);
    QuotedPostDto? quotedPost = null;
    if (post.OriginalPostId.HasValue)
    {
        var original = await posts.Find(p => p.Id == post.OriginalPostId.Value && !p.IsDeleted).FirstOrDefaultAsync(ct);
        if (original is not null) quotedPost = ToQuotedDto(original);
    }
    return Results.Ok(ToDto(post, repostedByMe, quotedPost));
}).RequireAuthorization();

app.MapGet("/api/posts/{postId:guid}/replies", async (Guid postId, int? limit, int? offset, IMongoCollection<PostDocument> posts, CancellationToken ct) =>
{
    var take = Math.Clamp(limit ?? 20, 1, 100);
    var skip = Math.Max(offset ?? 0, 0);
    var result = await posts.Find(p => p.ParentPostId == postId && !p.IsDeleted)
        .SortBy(p => p.PostedAt)
        .Skip(skip)
        .Limit(take)
        .ToListAsync(ct);
    var originals = await LoadOriginals(result, posts, ct);
    return Results.Ok(result.Select(p => ToDto(p, quotedPost: p.OriginalPostId.HasValue && originals.TryGetValue(p.OriginalPostId.Value, out var o) ? ToQuotedDto(o) : null)));
}).RequireAuthorization();

app.MapPost("/api/posts/{postId:guid}/reposts", async (
    Guid postId,
    CreatePostRequest request,
    ClaimsPrincipal principal,
    IMongoCollection<PostDocument> posts,
    IEventPublisher events,
    CancellationToken ct) =>
{
    var userId = principal.GetUserId();
    if (userId is null) return Results.Unauthorized();
    if (request.Content.Length > 280)
        return Results.BadRequest(new { error = "Post content must be 280 characters or fewer." });

    var target = await posts.Find(p => p.Id == postId && !p.IsDeleted).FirstOrDefaultAsync(ct);
    if (target is null) return Results.NotFound(new { error = "Post not found." });

    // Resolve to the root original post (no repost chains deeper than one)
    var rootId = target.OriginalPostId ?? target.Id;
    var root = target.OriginalPostId.HasValue
        ? await posts.Find(p => p.Id == rootId && !p.IsDeleted).FirstOrDefaultAsync(ct)
        : target;
    if (root is null) return Results.NotFound(new { error = "Original post not found." });

    if (root.AuthorId == userId)
        return Results.Conflict(new { error = "Users cannot repost their own posts." });

    var existingRepost = await posts.Find(p => p.OriginalPostId == rootId && p.AuthorId == userId && !p.IsDeleted).FirstOrDefaultAsync(ct);
    if (existingRepost is not null)
        return Results.Conflict(new { error = "Users can repost a post only once." });

    var content = request.Content.Trim();
    var repost = new PostDocument
    {
        Id = Guid.NewGuid(),
        AuthorId = userId.Value,
        AuthorHandle = principal.FindFirstValue(AuthConstants.HandleClaim) ?? $"@{userId.Value.ToString()[..8]}",
        AuthorDisplayName = principal.FindFirstValue(AuthConstants.DisplayNameClaim) ?? "Unknown user",
        Content = content,
        OriginalPostId = rootId,
        Hashtags = HashtagExtractor.Extract(content),
        Mentions = MentionExtractor.Extract(content),
        PostedAt = DateTimeOffset.UtcNow,
        UpdatedAt = DateTimeOffset.UtcNow
    };

    await posts.InsertOneAsync(repost, cancellationToken: ct);
    await posts.UpdateOneAsync(
        p => p.Id == rootId,
        Builders<PostDocument>.Update.Inc(p => p.RepostCount, 1),
        cancellationToken: ct);
    await events.PublishAsync(new PostCreated(repost.Id, repost.AuthorId, repost.AuthorHandle, repost.AuthorDisplayName, repost.Content, DateTimeOffset.UtcNow), ct);
    return Results.Created($"/api/posts/{repost.Id}", ToDto(repost));
}).RequireAuthorization();

app.MapDelete("/api/posts/{postId:guid}/reposts/mine", async (
    Guid postId,
    ClaimsPrincipal principal,
    IMongoCollection<PostDocument> posts,
    IEventPublisher events,
    CancellationToken ct) =>
{
    var userId = principal.GetUserId();
    if (userId is null) return Results.Unauthorized();

    var target = await posts.Find(p => p.Id == postId && !p.IsDeleted).FirstOrDefaultAsync(ct);
    if (target is null) return Results.NotFound(new { error = "Post not found." });

    var rootId = target.OriginalPostId ?? target.Id;
    var myRepost = await posts.Find(p => p.OriginalPostId == rootId && p.AuthorId == userId && !p.IsDeleted).FirstOrDefaultAsync(ct);
    if (myRepost is null) return Results.NotFound(new { error = "You have not reposted this post." });

    await posts.UpdateOneAsync(p => p.Id == myRepost.Id,
        Builders<PostDocument>.Update.Set(p => p.IsDeleted, true), cancellationToken: ct);
    await posts.UpdateOneAsync(
        p => p.Id == rootId && p.RepostCount > 0,
        Builders<PostDocument>.Update.Inc(p => p.RepostCount, -1),
        cancellationToken: ct);
    await events.PublishAsync(new PostDeleted(myRepost.Id, myRepost.AuthorId, DateTimeOffset.UtcNow), ct);
    return Results.NoContent();
}).RequireAuthorization();

app.MapGet("/api/posts/by-user/{userId:guid}", async (Guid userId, int limit, int offset, IMongoCollection<PostDocument> posts, CancellationToken ct) =>
{
    var result = await posts.Find(p => p.AuthorId == userId && !p.IsDeleted)
        .SortByDescending(p => p.PostedAt)
        .Skip(offset)
        .Limit(limit is <= 0 or > 100 ? 20 : limit)
        .ToListAsync(ct);
    var originals = await LoadOriginals(result, posts, ct);
    return Results.Ok(result.Select(p => ToDto(p, quotedPost: p.OriginalPostId.HasValue && originals.TryGetValue(p.OriginalPostId.Value, out var o) ? ToQuotedDto(o) : null)));
}).RequireAuthorization();

app.MapGet("/api/posts/search", async (string? q, int limit, int offset, IMongoCollection<PostDocument> posts, CancellationToken ct) =>
{
    if (string.IsNullOrWhiteSpace(q))
        return Results.BadRequest(new { error = "Query parameter 'q' is required." });

    var escaped = System.Text.RegularExpressions.Regex.Escape(q);
    var regex = new MongoDB.Bson.BsonRegularExpression(escaped, "i");
    var contentFilter = Builders<PostDocument>.Filter.Regex(p => p.Content, regex);
    var rootOnlyFilter = Builders<PostDocument>.Filter.Eq(p => p.ParentPostId, (Guid?)null);
    var notDeletedFilter = Builders<PostDocument>.Filter.Eq(p => p.IsDeleted, false);

    // Phase 1: IDs of original posts (not reposts) whose content matches
    var matchingOriginalIds = await posts
        .Find(Builders<PostDocument>.Filter.And(
            notDeletedFilter,
            rootOnlyFilter,
            Builders<PostDocument>.Filter.Eq(p => p.OriginalPostId, (Guid?)null),
            contentFilter))
        .Project(p => p.Id)
        .ToListAsync(ct);

    // Phase 2: root posts where content matches OR they repost a matching original
    FilterDefinition<PostDocument> combinedFilter;
    if (matchingOriginalIds.Count > 0)
    {
        var repostOfMatchFilter = Builders<PostDocument>.Filter.In(
            p => p.OriginalPostId,
            matchingOriginalIds.Select(id => (Guid?)id));
        combinedFilter = Builders<PostDocument>.Filter.And(
            notDeletedFilter,
            rootOnlyFilter,
            Builders<PostDocument>.Filter.Or(contentFilter, repostOfMatchFilter));
    }
    else
    {
        combinedFilter = Builders<PostDocument>.Filter.And(notDeletedFilter, rootOnlyFilter, contentFilter);
    }

    var result = await posts.Find(combinedFilter)
        .SortByDescending(p => p.PostedAt)
        .Skip(offset)
        .Limit(limit is <= 0 or > 100 ? 20 : limit)
        .ToListAsync(ct);
    var originals = await LoadOriginals(result, posts, ct);
    var dtos = result.Select(p => ToDto(p, quotedPost: p.OriginalPostId.HasValue && originals.TryGetValue(p.OriginalPostId.Value, out var o) ? ToQuotedDto(o) : null)).ToList();
    return Results.Ok(new SearchResultsDto(dtos, q ?? "", limit, offset));
}).RequireAuthorization();

app.MapGet("/api/posts/recent", async (int? limit, IMongoCollection<PostDocument> posts, CancellationToken ct) =>
{
    var take = Math.Clamp(limit ?? 20, 1, 100);
    var result = await posts.Find(p => p.ParentPostId == null && !p.IsDeleted)
        .SortByDescending(p => p.PostedAt)
        .Limit(take)
        .ToListAsync(ct);
    var originals = await LoadOriginals(result, posts, ct);
    return Results.Ok(result.Select(p => ToDto(p, quotedPost: p.OriginalPostId.HasValue && originals.TryGetValue(p.OriginalPostId.Value, out var o) ? ToQuotedDto(o) : null)));
}).RequireAuthorization();

app.MapHealthChecks("/health");
app.Run();

static string PrefixReplyContent(string parentAuthorHandle, string body)
{
    var normalized = parentAuthorHandle.TrimStart('@').Trim().ToLowerInvariant();
    var prefix = $"@{normalized}";
    if (string.IsNullOrWhiteSpace(body)) return prefix;
    if (body.Equals(prefix, StringComparison.OrdinalIgnoreCase) ||
        body.StartsWith(prefix + " ", StringComparison.OrdinalIgnoreCase))
        return body;
    return $"{prefix} {body}";
}

static PostDto ToDto(PostDocument post, bool repostedByMe = false, QuotedPostDto? quotedPost = null) =>
    new(post.Id, post.AuthorId, post.AuthorHandle, post.AuthorDisplayName, post.Content, post.PostedAt, post.UpdatedAt, post.Hashtags, post.Mentions, post.ParentPostId, post.OriginalPostId, post.ReplyCount, post.RepostCount, repostedByMe, quotedPost);

static QuotedPostDto ToQuotedDto(PostDocument post) =>
    new(post.Id, post.AuthorHandle, post.AuthorDisplayName, post.Content, post.PostedAt);

static async Task<Dictionary<Guid, PostDocument>> LoadOriginals(List<PostDocument> posts, IMongoCollection<PostDocument> collection, CancellationToken ct)
{
    var ids = posts.Where(p => p.OriginalPostId.HasValue).Select(p => p.OriginalPostId!.Value).Distinct().ToList();
    if (ids.Count == 0) return [];
    var originals = await collection.Find(p => ids.Contains(p.Id) && !p.IsDeleted).ToListAsync(ct);
    return originals.ToDictionary(p => p.Id);
}

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
    [BsonGuidRepresentation(GuidRepresentation.Standard)]
    public Guid? OriginalPostId { get; set; }
    public List<string> Hashtags { get; set; } = [];
    public List<string> Mentions { get; set; } = [];
    public int ReplyCount { get; set; }
    public int RepostCount { get; set; }
    public bool IsDeleted { get; set; }
    public DateTimeOffset PostedAt { get; set; }
    public DateTimeOffset UpdatedAt { get; set; }
}

public sealed record CreatePostRequest(string Content);
public sealed record UpdatePostRequest(string Content);
public sealed record QuotedPostDto(Guid PostId, string AuthorHandle, string AuthorDisplayName, string Content, DateTimeOffset PostedAt);
public sealed record PostDto(Guid PostId, Guid AuthorId, string AuthorHandle, string AuthorDisplayName, string Content, DateTimeOffset PostedAt, DateTimeOffset UpdatedAt, List<string> Hashtags, List<string> Mentions, Guid? ParentPostId = null, Guid? OriginalPostId = null, int ReplyCount = 0, int RepostCount = 0, bool RepostedByMe = false, QuotedPostDto? QuotedPost = null);
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
