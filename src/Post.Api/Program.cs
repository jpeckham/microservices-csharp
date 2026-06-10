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
builder.Services.AddSingleton(sp => sp.GetRequiredService<IMongoDatabase>().GetCollection<PostLikeDocument>("postLikes"));

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

    if (toDelete.ParentPostId.HasValue)
        await posts.UpdateOneAsync(
            p => p.Id == toDelete.ParentPostId.Value && p.ReplyCount > 0,
            Builders<PostDocument>.Update.Inc(p => p.ReplyCount, -1),
            cancellationToken: ct);

    if (toDelete.OriginalPostId.HasValue)
        await posts.UpdateOneAsync(
            p => p.Id == toDelete.OriginalPostId.Value && p.RepostCount > 0,
            Builders<PostDocument>.Update.Inc(p => p.RepostCount, -1),
            cancellationToken: ct);

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
    await events.PublishAsync(new PostCreated(post.Id, post.AuthorId, post.AuthorHandle, post.AuthorDisplayName, post.Content, DateTimeOffset.UtcNow, ParentPostId: postId), ct);
    return Results.Created($"/api/posts/{post.Id}", ToDto(post));
}).RequireAuthorization();

app.MapGet("/api/posts/{id:guid}", async (Guid id, ClaimsPrincipal principal, IMongoCollection<PostDocument> posts, IMongoCollection<PostLikeDocument> postLikes, CancellationToken ct) =>
{
    var post = await posts.Find(p => p.Id == id && !p.IsDeleted).FirstOrDefaultAsync(ct);
    if (post is null) return Results.NotFound(new { error = "Post not found." });
    var userId = principal.GetUserId();
    var repostedByMe = userId.HasValue && await posts.Find(p => p.OriginalPostId == id && p.AuthorId == userId && !p.IsDeleted).AnyAsync(ct);
    var likedByMe = userId.HasValue && await postLikes.Find(l => l.PostId == id && l.UserId == userId.Value).AnyAsync(ct);
    QuotedPostDto? quotedPost = null;
    if (post.OriginalPostId.HasValue)
    {
        var original = await posts.Find(p => p.Id == post.OriginalPostId.Value && !p.IsDeleted).FirstOrDefaultAsync(ct);
        if (original is not null) quotedPost = ToQuotedDto(original);
    }
    QuotedPostDto? replyTarget = null;
    if (post.ParentPostId.HasValue)
    {
        var parent = await posts.Find(p => p.Id == post.ParentPostId.Value && !p.IsDeleted).FirstOrDefaultAsync(ct);
        if (parent is not null) replyTarget = ToQuotedDto(parent);
    }
    var level1 = await posts.Find(p => p.ParentPostId == id && !p.IsDeleted)
        .SortByDescending(p => p.PostedAt)
        .Limit(3)
        .ToListAsync(ct);

    var level1Ids = level1.Select(r => (Guid?)r.Id).ToList();
    var level2All = level1Ids.Count > 0
        ? await posts.Find(p => level1Ids.Contains(p.ParentPostId) && !p.IsDeleted)
            .SortByDescending(p => p.PostedAt)
            .ToListAsync(ct)
        : [];

    var level2Ids = level2All.Select(r => (Guid?)r.Id).ToList();
    var level3All = level2Ids.Count > 0
        ? await posts.Find(p => level2Ids.Contains(p.ParentPostId) && !p.IsDeleted)
            .SortByDescending(p => p.PostedAt)
            .ToListAsync(ct)
        : [];

    var level3ByParent = level3All
        .GroupBy(p => p.ParentPostId!.Value)
        .ToDictionary(g => g.Key, g => g.Take(3).Select(r => ToDto(r, recentReplies: [])).ToList());

    var level2ByParent = level2All
        .GroupBy(p => p.ParentPostId!.Value)
        .ToDictionary(g => g.Key, g => g.Take(3)
            .Select(r => ToDto(r, recentReplies: level3ByParent.TryGetValue(r.Id, out var l3) ? l3 : [])).ToList());

    var currentAsQuoted = ToQuotedDto(post);
    var recentReplies = level1.Select(r => ToDto(r, replyTarget: currentAsQuoted,
        recentReplies: level2ByParent.TryGetValue(r.Id, out var l2) ? l2 : null)).ToList();

    return Results.Ok(ToDto(post, repostedByMe, likedByMe, quotedPost, replyTarget, recentReplies));
}).RequireAuthorization();

app.MapGet("/api/posts/{postId:guid}/replies", async (Guid postId, int? limit, int? offset, ClaimsPrincipal principal, IMongoCollection<PostDocument> posts, IMongoCollection<PostLikeDocument> postLikes, CancellationToken ct) =>
{
    var take = Math.Clamp(limit ?? 20, 1, 100);
    var skip = Math.Max(offset ?? 0, 0);
    var result = await posts.Find(p => p.ParentPostId == postId && !p.IsDeleted)
        .SortBy(p => p.PostedAt)
        .Skip(skip)
        .Limit(take)
        .ToListAsync(ct);
    var originals = await LoadOriginals(result, posts, ct);
    var likedIds = await LoadLikedIds(result.Select(p => p.Id), principal, postLikes, ct);
    var repostedIds = await LoadRepostedIds(result.Select(p => p.Id), principal, posts, ct);
    return Results.Ok(result.Select(p => ToDto(p, repostedByMe: repostedIds.Contains(p.Id), likedByMe: likedIds.Contains(p.Id), quotedPost: p.OriginalPostId.HasValue && originals.TryGetValue(p.OriginalPostId.Value, out var o) ? ToQuotedDto(o) : null)));
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

app.MapGet("/api/posts/by-user/{userId:guid}", async (Guid userId, int limit, int offset, ClaimsPrincipal principal, IMongoCollection<PostDocument> posts, IMongoCollection<PostLikeDocument> postLikes, CancellationToken ct) =>
{
    var result = await posts.Find(p => p.AuthorId == userId && !p.IsDeleted)
        .SortByDescending(p => p.PostedAt)
        .Skip(offset)
        .Limit(limit is <= 0 or > 100 ? 20 : limit)
        .ToListAsync(ct);
    var originals = await LoadOriginals(result, posts, ct);
    var parents = await LoadParents(result, posts, ct);
    var likedIds = await LoadLikedIds(result.Select(p => p.Id), principal, postLikes, ct);
    var repostedIds = await LoadRepostedIds(result.Select(p => p.Id), principal, posts, ct);
    return Results.Ok(result.Select(p => ToDto(
        p,
        repostedByMe: repostedIds.Contains(p.Id),
        likedByMe: likedIds.Contains(p.Id),
        quotedPost: p.OriginalPostId.HasValue && originals.TryGetValue(p.OriginalPostId.Value, out var o) ? ToQuotedDto(o) : null,
        replyTarget: p.ParentPostId.HasValue && parents.TryGetValue(p.ParentPostId.Value, out var par) ? ToQuotedDto(par) : null)));
}).RequireAuthorization();

app.MapGet("/api/posts/search", async (string? q, int limit, int offset, ClaimsPrincipal principal, IMongoCollection<PostDocument> posts, IMongoCollection<PostLikeDocument> postLikes, CancellationToken ct) =>
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
    var likedIds = await LoadLikedIds(result.Select(p => p.Id), principal, postLikes, ct);
    var repostedIds = await LoadRepostedIds(result.Select(p => p.Id), principal, posts, ct);
    var dtos = result.Select(p => ToDto(p, repostedByMe: repostedIds.Contains(p.Id), likedByMe: likedIds.Contains(p.Id), quotedPost: p.OriginalPostId.HasValue && originals.TryGetValue(p.OriginalPostId.Value, out var o) ? ToQuotedDto(o) : null)).ToList();
    return Results.Ok(new SearchResultsDto(dtos, q ?? "", limit, offset));
}).RequireAuthorization();

app.MapGet("/api/posts/recent", async (int? limit, ClaimsPrincipal principal, IMongoCollection<PostDocument> posts, IMongoCollection<PostLikeDocument> postLikes, CancellationToken ct) =>
{
    var take = Math.Clamp(limit ?? 20, 1, 100);
    var result = await posts.Find(p => p.ParentPostId == null && !p.IsDeleted)
        .SortByDescending(p => p.PostedAt)
        .Limit(take)
        .ToListAsync(ct);
    var originals = await LoadOriginals(result, posts, ct);
    var likedIds = await LoadLikedIds(result.Select(p => p.Id), principal, postLikes, ct);
    var repostedIds = await LoadRepostedIds(result.Select(p => p.Id), principal, posts, ct);
    return Results.Ok(result.Select(p => ToDto(p, repostedByMe: repostedIds.Contains(p.Id), likedByMe: likedIds.Contains(p.Id), quotedPost: p.OriginalPostId.HasValue && originals.TryGetValue(p.OriginalPostId.Value, out var o) ? ToQuotedDto(o) : null)));
}).RequireAuthorization();

app.MapPost("/events/LikeAdded", async (LikeAdded integrationEvent, IMongoCollection<PostDocument> posts, IMongoCollection<PostLikeDocument> postLikes, CancellationToken ct) =>
{
    await posts.UpdateOneAsync(
        p => p.Id == integrationEvent.PostId,
        Builders<PostDocument>.Update.Inc(p => p.LikeCount, 1),
        cancellationToken: ct);
    var like = new PostLikeDocument { PostId = integrationEvent.PostId, UserId = integrationEvent.UserId };
    await postLikes.ReplaceOneAsync(l => l.PostId == like.PostId && l.UserId == like.UserId, like, new ReplaceOptions { IsUpsert = true }, ct);
    return Results.Accepted();
});

app.MapPost("/events/LikeRemoved", async (LikeRemoved integrationEvent, IMongoCollection<PostDocument> posts, IMongoCollection<PostLikeDocument> postLikes, CancellationToken ct) =>
{
    await posts.UpdateOneAsync(
        p => p.Id == integrationEvent.PostId && p.LikeCount > 0,
        Builders<PostDocument>.Update.Inc(p => p.LikeCount, -1),
        cancellationToken: ct);
    await postLikes.DeleteOneAsync(l => l.PostId == integrationEvent.PostId && l.UserId == integrationEvent.UserId, ct);
    return Results.Accepted();
});

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

static PostDto ToDto(PostDocument post, bool repostedByMe = false, bool likedByMe = false, QuotedPostDto? quotedPost = null, QuotedPostDto? replyTarget = null, List<PostDto>? recentReplies = null) =>
    new(post.Id, post.AuthorId, post.AuthorHandle, post.AuthorDisplayName, post.Content, post.PostedAt, post.UpdatedAt, post.Hashtags, post.Mentions, ContentSegmentParser.Parse(post.Content), post.ParentPostId, post.OriginalPostId, post.ReplyCount, post.RepostCount, post.LikeCount, repostedByMe, likedByMe, quotedPost, replyTarget, recentReplies);

static QuotedPostDto ToQuotedDto(PostDocument post) =>
    new(post.Id, post.AuthorHandle, post.AuthorDisplayName, post.Content, post.PostedAt);

static async Task<Dictionary<Guid, PostDocument>> LoadOriginals(List<PostDocument> posts, IMongoCollection<PostDocument> collection, CancellationToken ct)
{
    var ids = posts.Where(p => p.OriginalPostId.HasValue).Select(p => p.OriginalPostId!.Value).Distinct().ToList();
    if (ids.Count == 0) return [];
    var originals = await collection.Find(p => ids.Contains(p.Id) && !p.IsDeleted).ToListAsync(ct);
    return originals.ToDictionary(p => p.Id);
}

static async Task<Dictionary<Guid, PostDocument>> LoadParents(List<PostDocument> posts, IMongoCollection<PostDocument> collection, CancellationToken ct)
{
    var ids = posts.Where(p => p.ParentPostId.HasValue).Select(p => p.ParentPostId!.Value).Distinct().ToList();
    if (ids.Count == 0) return [];
    var parents = await collection.Find(p => ids.Contains(p.Id) && !p.IsDeleted).ToListAsync(ct);
    return parents.ToDictionary(p => p.Id);
}

static async Task<HashSet<Guid>> LoadLikedIds(IEnumerable<Guid> postIds, ClaimsPrincipal principal, IMongoCollection<PostLikeDocument> postLikes, CancellationToken ct)
{
    var userId = principal.GetUserId();
    if (!userId.HasValue) return [];
    var ids = postIds.ToList();
    if (ids.Count == 0) return [];
    return (await postLikes.Find(l => l.UserId == userId.Value && ids.Contains(l.PostId))
        .Project(l => l.PostId)
        .ToListAsync(ct)).ToHashSet();
}

static async Task<HashSet<Guid>> LoadRepostedIds(IEnumerable<Guid> postIds, ClaimsPrincipal principal, IMongoCollection<PostDocument> posts, CancellationToken ct)
{
    var userId = principal.GetUserId();
    if (!userId.HasValue) return [];
    var ids = postIds.ToList();
    if (ids.Count == 0) return [];
    var nullableIds = ids.Select(id => (Guid?)id).ToList();
    return (await posts.Find(p => p.AuthorId == userId.Value && nullableIds.Contains(p.OriginalPostId) && !p.IsDeleted)
        .Project(p => p.OriginalPostId!.Value)
        .ToListAsync(ct)).ToHashSet();
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
    public int LikeCount { get; set; }
    public bool IsDeleted { get; set; }
    public DateTimeOffset PostedAt { get; set; }
    public DateTimeOffset UpdatedAt { get; set; }
}

public sealed class PostLikeDocument
{
    [BsonId]
    [BsonGuidRepresentation(GuidRepresentation.Standard)]
    public Guid PostId { get; set; }
    [BsonGuidRepresentation(GuidRepresentation.Standard)]
    public Guid UserId { get; set; }
}

public sealed record CreatePostRequest(string Content);
public sealed record UpdatePostRequest(string Content);
public sealed record QuotedPostDto(Guid PostId, string AuthorHandle, string AuthorDisplayName, string Content, DateTimeOffset PostedAt);
public sealed record PostDto(Guid PostId, Guid AuthorId, string AuthorHandle, string AuthorDisplayName, string Content, DateTimeOffset PostedAt, DateTimeOffset UpdatedAt, List<string> Hashtags, List<string> Mentions, List<ContentSegmentDto> ContentSegments, Guid? ParentPostId = null, Guid? OriginalPostId = null, int ReplyCount = 0, int RepostCount = 0, int LikeCount = 0, bool RepostedByMe = false, bool LikedByMe = false, QuotedPostDto? QuotedPost = null, QuotedPostDto? ReplyTarget = null, List<PostDto>? RecentReplies = null);
public sealed record ContentSegmentDto(int Sequence, string Text, string? MentionHandle = null, string? HashtagText = null);
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

public static class ContentSegmentParser
{
    private static readonly Regex TokenPattern = new(@"(@[a-zA-Z][a-zA-Z0-9_]*|#[a-zA-Z][a-zA-Z0-9_]*)", RegexOptions.Compiled, TimeSpan.FromSeconds(1));

    public static List<ContentSegmentDto> Parse(string content)
    {
        var segments = new List<ContentSegmentDto>();
        MatchCollection matches;
        try { matches = TokenPattern.Matches(content); }
        catch (RegexMatchTimeoutException) { return [new ContentSegmentDto(0, content)]; }

        int pos = 0, seq = 0;
        foreach (Match m in matches)
        {
            if (m.Index > pos)
                segments.Add(new ContentSegmentDto(seq++, content[pos..m.Index]));
            if (m.Value[0] == '@')
                segments.Add(new ContentSegmentDto(seq++, m.Value, MentionHandle: m.Value[1..].ToLowerInvariant()));
            else
                segments.Add(new ContentSegmentDto(seq++, m.Value, HashtagText: m.Value[1..].ToLowerInvariant()));
            pos = m.Index + m.Length;
        }
        if (pos < content.Length)
            segments.Add(new ContentSegmentDto(seq++, content[pos..]));
        if (segments.Count == 0)
            segments.Add(new ContentSegmentDto(0, content));
        return segments;
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
