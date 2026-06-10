using System.Security.Claims;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using Azure.Messaging.ServiceBus;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.IdentityModel.Tokens;
using MongoDB.Bson;
using MongoDB.Bson.Serialization.Attributes;
using MongoDB.Driver;
using Shared.Contracts.Events;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen();
builder.Services.AddSingleton(_ =>
{
    var connectionString = builder.Configuration["Mongo:ConnectionString"] ?? "mongodb://localhost:27017";
    var databaseName = builder.Configuration["Mongo:DatabaseName"] ?? "FeedDb";
    return new MongoClient(connectionString).GetDatabase(databaseName);
});
builder.Services.AddSingleton(sp => sp.GetRequiredService<IMongoDatabase>().GetCollection<FeedEntryDocument>("feedEntries"));
builder.Services.AddSingleton(sp => sp.GetRequiredService<IMongoDatabase>().GetCollection<FeedFollowDocument>("feedFollows"));
builder.Services.AddSingleton(sp => sp.GetRequiredService<IMongoDatabase>().GetCollection<FeedBlockDocument>("feedBlocks"));
builder.Services.AddSingleton(sp => sp.GetRequiredService<IMongoDatabase>().GetCollection<FeedLikeDocument>("feedLikes"));
builder.Services.AddHostedService<ServiceBusFeedConsumer>();

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

app.MapGet("/api/feed", async (
    int limit,
    int offset,
    bool followingOnly,
    ClaimsPrincipal principal,
    IMongoCollection<FeedEntryDocument> entries,
    IMongoCollection<FeedFollowDocument> follows,
    IMongoCollection<FeedBlockDocument> blocks,
    IMongoCollection<FeedLikeDocument> feedLikes,
    CancellationToken ct) =>
{
    var userId = principal.GetUserId();
    var filter = Builders<FeedEntryDocument>.Filter.Empty;
    if (followingOnly && userId.HasValue)
    {
        var following = await follows.Find(f => f.FollowerId == userId).Project(f => f.FollowingId).ToListAsync(ct);
        filter = Builders<FeedEntryDocument>.Filter.In(e => e.AuthorId, following.Append(userId.Value));
    }

    if (userId.HasValue)
    {
        var blockedByMe = await blocks.Find(b => b.BlockerId == userId.Value).Project(b => b.BlockedId).ToListAsync(ct);
        var blockedMe = await blocks.Find(b => b.BlockedId == userId.Value).Project(b => b.BlockerId).ToListAsync(ct);
        var excluded = blockedByMe.Concat(blockedMe).Distinct().ToList();
        if (excluded.Count > 0)
            filter &= Builders<FeedEntryDocument>.Filter.Nin(e => e.AuthorId, excluded);
    }

    var result = await entries.Find(filter)
        .SortByDescending(e => e.LastActivityAt)
        .Skip(offset)
        .Limit(limit is <= 0 or > 100 ? 20 : limit)
        .ToListAsync(ct);

    HashSet<Guid> likedPostIds = [];
    if (userId.HasValue && result.Count > 0)
    {
        var postIds = result.Select(e => e.PostId).ToList();
        likedPostIds = (await feedLikes.Find(l => l.UserId == userId.Value && postIds.Contains(l.PostId))
            .Project(l => l.PostId)
            .ToListAsync(ct)).ToHashSet();
    }

    return Results.Ok(result.Select(e => ToDto(e, likedPostIds.Contains(e.PostId))));
}).RequireAuthorization();

app.MapGet("/api/feed/users/{userId:guid}", async (Guid userId, int limit, int offset, IMongoCollection<FeedEntryDocument> entries, CancellationToken ct) =>
{
    var result = await entries.Find(e => e.AuthorId == userId)
        .SortByDescending(e => e.PostedAt)
        .Skip(offset)
        .Limit(limit is <= 0 or > 100 ? 20 : limit)
        .ToListAsync(ct);
    return Results.Ok(result.Select(e => ToDto(e)));
}).RequireAuthorization();

app.MapPost("/events/PostCreated", async (PostCreated integrationEvent, IMongoCollection<FeedEntryDocument> entries, CancellationToken ct) =>
{
    if (integrationEvent.ParentPostId.HasValue)
    {
        await entries.UpdateOneAsync(
            e => e.PostId == integrationEvent.ParentPostId.Value && e.LastActivityAt < integrationEvent.OccurredAt,
            Builders<FeedEntryDocument>.Update.Set(e => e.LastActivityAt, integrationEvent.OccurredAt),
            cancellationToken: ct);
    }
    else
    {
        var entry = new FeedEntryDocument
        {
            Id = integrationEvent.PostId,
            PostId = integrationEvent.PostId,
            AuthorId = integrationEvent.AuthorId,
            AuthorHandle = integrationEvent.AuthorHandle,
            AuthorDisplayName = integrationEvent.AuthorDisplayName,
            Content = integrationEvent.Content,
            PostedAt = integrationEvent.OccurredAt,
            UpdatedAt = integrationEvent.OccurredAt,
            LastActivityAt = integrationEvent.OccurredAt
        };
        await entries.ReplaceOneAsync(e => e.PostId == entry.PostId, entry, new ReplaceOptions { IsUpsert = true }, ct);
    }
    return Results.Accepted();
});

app.MapPost("/events/PostUpdated", async (PostUpdated integrationEvent, IMongoCollection<FeedEntryDocument> entries, CancellationToken ct) =>
{
    var update = Builders<FeedEntryDocument>.Update
        .Set(e => e.Content, integrationEvent.Content)
        .Set(e => e.UpdatedAt, integrationEvent.OccurredAt);
    await entries.UpdateOneAsync(e => e.PostId == integrationEvent.PostId, update, cancellationToken: ct);
    return Results.Accepted();
});

app.MapPost("/events/PostDeleted", async (PostDeleted integrationEvent, IMongoCollection<FeedEntryDocument> entries, CancellationToken ct) =>
{
    await entries.DeleteOneAsync(e => e.PostId == integrationEvent.PostId, ct);
    return Results.Accepted();
});

app.MapPost("/events/UserProfileUpdated", async (UserProfileUpdated integrationEvent, IMongoCollection<FeedEntryDocument> entries, CancellationToken ct) =>
{
    var update = Builders<FeedEntryDocument>.Update
        .Set(e => e.AuthorHandle, integrationEvent.Handle)
        .Set(e => e.AuthorDisplayName, integrationEvent.DisplayName);
    await entries.UpdateManyAsync(e => e.AuthorId == integrationEvent.UserId, update, cancellationToken: ct);
    return Results.Accepted();
});

app.MapPost("/events/UserFollowed", async (UserFollowed integrationEvent, IMongoCollection<FeedFollowDocument> follows, CancellationToken ct) =>
{
    var follow = new FeedFollowDocument
    {
        Id = integrationEvent.RelationshipId,
        FollowerId = integrationEvent.FollowerId,
        FollowingId = integrationEvent.FollowingId,
        CreatedAt = integrationEvent.OccurredAt
    };
    await follows.ReplaceOneAsync(f => f.FollowerId == follow.FollowerId && f.FollowingId == follow.FollowingId, follow, new ReplaceOptions { IsUpsert = true }, ct);
    return Results.Accepted();
});

app.MapPost("/events/UserUnfollowed", async (UserUnfollowed integrationEvent, IMongoCollection<FeedFollowDocument> follows, CancellationToken ct) =>
{
    await follows.DeleteOneAsync(f => f.FollowerId == integrationEvent.FollowerId && f.FollowingId == integrationEvent.FollowingId, ct);
    return Results.Accepted();
});

app.MapPost("/events/LikeAdded", async (LikeAdded integrationEvent, IMongoCollection<FeedEntryDocument> entries, IMongoCollection<FeedLikeDocument> feedLikes, CancellationToken ct) =>
{
    await entries.UpdateOneAsync(e => e.PostId == integrationEvent.PostId, Builders<FeedEntryDocument>.Update.Inc(e => e.LikeCount, 1), cancellationToken: ct);
    var like = new FeedLikeDocument { UserId = integrationEvent.UserId, PostId = integrationEvent.PostId };
    await feedLikes.ReplaceOneAsync(l => l.UserId == like.UserId && l.PostId == like.PostId, like, new ReplaceOptions { IsUpsert = true }, ct);
    return Results.Accepted();
});

app.MapPost("/events/LikeRemoved", async (LikeRemoved integrationEvent, IMongoCollection<FeedEntryDocument> entries, IMongoCollection<FeedLikeDocument> feedLikes, CancellationToken ct) =>
{
    await entries.UpdateOneAsync(e => e.PostId == integrationEvent.PostId && e.LikeCount > 0, Builders<FeedEntryDocument>.Update.Inc(e => e.LikeCount, -1), cancellationToken: ct);
    await feedLikes.DeleteOneAsync(l => l.UserId == integrationEvent.UserId && l.PostId == integrationEvent.PostId, ct);
    return Results.Accepted();
});

app.MapPost("/events/CommentAdded", async (CommentAdded integrationEvent, IMongoCollection<FeedEntryDocument> entries, CancellationToken ct) =>
{
    await entries.UpdateOneAsync(e => e.PostId == integrationEvent.PostId, Builders<FeedEntryDocument>.Update.Inc(e => e.CommentCount, 1), cancellationToken: ct);
    return Results.Accepted();
});

app.MapPost("/events/CommentDeleted", async (CommentDeleted integrationEvent, IMongoCollection<FeedEntryDocument> entries, CancellationToken ct) =>
{
    await entries.UpdateOneAsync(
        e => e.PostId == integrationEvent.PostId && e.CommentCount > 0,
        Builders<FeedEntryDocument>.Update.Inc(e => e.CommentCount, -1),
        cancellationToken: ct);
    return Results.Accepted();
});

app.MapPost("/events/UserBlocked", async (UserBlocked integrationEvent, IMongoCollection<FeedBlockDocument> blocks, CancellationToken ct) =>
{
    var block = new FeedBlockDocument
    {
        Id = integrationEvent.BlockId,
        BlockerId = integrationEvent.BlockerId,
        BlockedId = integrationEvent.BlockedId,
        CreatedAt = integrationEvent.OccurredAt
    };
    await blocks.ReplaceOneAsync(b => b.BlockerId == block.BlockerId && b.BlockedId == block.BlockedId, block, new ReplaceOptions { IsUpsert = true }, ct);
    return Results.Accepted();
});

app.MapPost("/events/UserUnblocked", async (UserUnblocked integrationEvent, IMongoCollection<FeedBlockDocument> blocks, CancellationToken ct) =>
{
    await blocks.DeleteOneAsync(b => b.BlockerId == integrationEvent.BlockerId && b.BlockedId == integrationEvent.BlockedId, ct);
    return Results.Accepted();
});

app.MapHealthChecks("/health");
app.Run();

static FeedEntryDto ToDto(FeedEntryDocument entry, bool likedByMe = false) =>
    new(entry.PostId, entry.AuthorId, entry.AuthorHandle, entry.AuthorDisplayName, entry.Content, entry.PostedAt, entry.LikeCount, entry.CommentCount, likedByMe, FeedContentSegmentParser.Parse(entry.Content));

public sealed class FeedEntryDocument
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
    public DateTimeOffset PostedAt { get; set; }
    public DateTimeOffset UpdatedAt { get; set; }
    public DateTimeOffset LastActivityAt { get; set; }
    public int LikeCount { get; set; }
    public int CommentCount { get; set; }
}

public sealed class FeedFollowDocument
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

public sealed class FeedBlockDocument
{
    [BsonId]
    [BsonGuidRepresentation(GuidRepresentation.Standard)]
    public Guid Id { get; set; }
    [BsonGuidRepresentation(GuidRepresentation.Standard)]
    public Guid BlockerId { get; set; }
    [BsonGuidRepresentation(GuidRepresentation.Standard)]
    public Guid BlockedId { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
}

public sealed class FeedLikeDocument
{
    [BsonId]
    [BsonGuidRepresentation(GuidRepresentation.Standard)]
    public Guid UserId { get; set; }
    [BsonGuidRepresentation(GuidRepresentation.Standard)]
    public Guid PostId { get; set; }
}

public sealed record FeedEntryDto(Guid PostId, Guid AuthorId, string AuthorHandle, string AuthorDisplayName, string Content, DateTimeOffset PostedAt, int LikeCount, int CommentCount, bool LikedByMe = false, List<FeedContentSegmentDto>? ContentSegments = null);

public sealed record FeedContentSegmentDto(int Sequence, string Text, string? MentionHandle = null, string? HashtagText = null);

public static class FeedContentSegmentParser
{
    private static readonly Regex TokenPattern = new(@"(@[a-zA-Z][a-zA-Z0-9_]*|#[a-zA-Z][a-zA-Z0-9_]*)", RegexOptions.Compiled, TimeSpan.FromSeconds(1));

    public static List<FeedContentSegmentDto> Parse(string content)
    {
        var segments = new List<FeedContentSegmentDto>();
        MatchCollection matches;
        try { matches = TokenPattern.Matches(content); }
        catch (RegexMatchTimeoutException) { return [new FeedContentSegmentDto(0, content)]; }

        int pos = 0, seq = 0;
        foreach (Match m in matches)
        {
            if (m.Index > pos)
                segments.Add(new FeedContentSegmentDto(seq++, content[pos..m.Index]));
            if (m.Value[0] == '@')
                segments.Add(new FeedContentSegmentDto(seq++, m.Value, MentionHandle: m.Value[1..].ToLowerInvariant()));
            else
                segments.Add(new FeedContentSegmentDto(seq++, m.Value, HashtagText: m.Value[1..].ToLowerInvariant()));
            pos = m.Index + m.Length;
        }
        if (pos < content.Length)
            segments.Add(new FeedContentSegmentDto(seq++, content[pos..]));
        if (segments.Count == 0)
            segments.Add(new FeedContentSegmentDto(0, content));
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

public sealed class ServiceBusFeedConsumer(
    IConfiguration configuration,
    IMongoCollection<FeedEntryDocument> entries,
    IMongoCollection<FeedFollowDocument> follows,
    IMongoCollection<FeedBlockDocument> blocks,
    IMongoCollection<FeedLikeDocument> feedLikes,
    ILogger<ServiceBusFeedConsumer> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var connectionString = configuration["ServiceBus:ConnectionString"];
        if (string.IsNullOrWhiteSpace(connectionString))
        {
            logger.LogInformation("Service Bus feed consumer disabled because no connection string is configured.");
            return;
        }

        var topicName = configuration["ServiceBus:TopicName"] ?? "social-events";
        var subscriptionName = configuration["ServiceBus:SubscriptionName"] ?? "feed";

        await using var client = new ServiceBusClient(connectionString);
        await using var processor = client.CreateProcessor(topicName, subscriptionName);
        processor.ProcessMessageAsync += HandleMessageAsync;
        processor.ProcessErrorAsync += args =>
        {
            logger.LogError(args.Exception, "Service Bus feed consumer error from {ErrorSource}.", args.ErrorSource);
            return Task.CompletedTask;
        };

        await processor.StartProcessingAsync(stoppingToken);
        try
        {
            await Task.Delay(Timeout.InfiniteTimeSpan, stoppingToken);
        }
        catch (OperationCanceledException)
        {
            await processor.StopProcessingAsync(CancellationToken.None);
        }
    }

    private async Task HandleMessageAsync(ProcessMessageEventArgs args)
    {
        var json = args.Message.Body.ToString();
        switch (args.Message.Subject)
        {
            case nameof(PostCreated):
                var created = JsonSerializer.Deserialize<PostCreated>(json);
                if (created is not null) await ApplyAsync(created, args.CancellationToken);
                break;
            case nameof(PostUpdated):
                var updated = JsonSerializer.Deserialize<PostUpdated>(json);
                if (updated is not null) await ApplyAsync(updated, args.CancellationToken);
                break;
            case nameof(PostDeleted):
                var deleted = JsonSerializer.Deserialize<PostDeleted>(json);
                if (deleted is not null) await entries.DeleteOneAsync(e => e.PostId == deleted.PostId, args.CancellationToken);
                break;
            case nameof(UserProfileUpdated):
                var profile = JsonSerializer.Deserialize<UserProfileUpdated>(json);
                if (profile is not null)
                {
                    var update = Builders<FeedEntryDocument>.Update
                        .Set(e => e.AuthorHandle, profile.Handle)
                        .Set(e => e.AuthorDisplayName, profile.DisplayName);
                    await entries.UpdateManyAsync(e => e.AuthorId == profile.UserId, update, cancellationToken: args.CancellationToken);
                }
                break;
            case nameof(UserFollowed):
                var followed = JsonSerializer.Deserialize<UserFollowed>(json);
                if (followed is not null) await ApplyAsync(followed, args.CancellationToken);
                break;
            case nameof(UserUnfollowed):
                var unfollowed = JsonSerializer.Deserialize<UserUnfollowed>(json);
                if (unfollowed is not null)
                {
                    await follows.DeleteOneAsync(f => f.FollowerId == unfollowed.FollowerId && f.FollowingId == unfollowed.FollowingId, args.CancellationToken);
                }
                break;
            case nameof(LikeAdded):
                var likeAdded = JsonSerializer.Deserialize<LikeAdded>(json);
                if (likeAdded is not null)
                {
                    await entries.UpdateOneAsync(e => e.PostId == likeAdded.PostId, Builders<FeedEntryDocument>.Update.Inc(e => e.LikeCount, 1), cancellationToken: args.CancellationToken);
                    var like = new FeedLikeDocument { UserId = likeAdded.UserId, PostId = likeAdded.PostId };
                    await feedLikes.ReplaceOneAsync(l => l.UserId == like.UserId && l.PostId == like.PostId, like, new ReplaceOptions { IsUpsert = true }, args.CancellationToken);
                }
                break;
            case nameof(LikeRemoved):
                var likeRemoved = JsonSerializer.Deserialize<LikeRemoved>(json);
                if (likeRemoved is not null)
                {
                    await entries.UpdateOneAsync(e => e.PostId == likeRemoved.PostId && e.LikeCount > 0, Builders<FeedEntryDocument>.Update.Inc(e => e.LikeCount, -1), cancellationToken: args.CancellationToken);
                    await feedLikes.DeleteOneAsync(l => l.UserId == likeRemoved.UserId && l.PostId == likeRemoved.PostId, args.CancellationToken);
                }
                break;
            case nameof(CommentAdded):
                var commentAdded = JsonSerializer.Deserialize<CommentAdded>(json);
                if (commentAdded is not null)
                {
                    await entries.UpdateOneAsync(e => e.PostId == commentAdded.PostId, Builders<FeedEntryDocument>.Update.Inc(e => e.CommentCount, 1), cancellationToken: args.CancellationToken);
                }
                break;
            case nameof(CommentDeleted):
                var commentDeleted = JsonSerializer.Deserialize<CommentDeleted>(json);
                if (commentDeleted is not null)
                {
                    await entries.UpdateOneAsync(
                        e => e.PostId == commentDeleted.PostId && e.CommentCount > 0,
                        Builders<FeedEntryDocument>.Update.Inc(e => e.CommentCount, -1),
                        cancellationToken: args.CancellationToken);
                }
                break;
            case nameof(UserBlocked):
                var userBlocked = JsonSerializer.Deserialize<UserBlocked>(json);
                if (userBlocked is not null)
                {
                    var block = new FeedBlockDocument
                    {
                        Id = userBlocked.BlockId,
                        BlockerId = userBlocked.BlockerId,
                        BlockedId = userBlocked.BlockedId,
                        CreatedAt = userBlocked.OccurredAt
                    };
                    await blocks.ReplaceOneAsync(b => b.BlockerId == block.BlockerId && b.BlockedId == block.BlockedId, block, new ReplaceOptions { IsUpsert = true }, args.CancellationToken);
                }
                break;
            case nameof(UserUnblocked):
                var userUnblocked = JsonSerializer.Deserialize<UserUnblocked>(json);
                if (userUnblocked is not null)
                    await blocks.DeleteOneAsync(b => b.BlockerId == userUnblocked.BlockerId && b.BlockedId == userUnblocked.BlockedId, args.CancellationToken);
                break;
        }

        await args.CompleteMessageAsync(args.Message);
    }

    private async Task ApplyAsync(PostCreated integrationEvent, CancellationToken ct)
    {
        if (integrationEvent.ParentPostId.HasValue)
        {
            await entries.UpdateOneAsync(
                e => e.PostId == integrationEvent.ParentPostId.Value && e.LastActivityAt < integrationEvent.OccurredAt,
                Builders<FeedEntryDocument>.Update.Set(e => e.LastActivityAt, integrationEvent.OccurredAt),
                cancellationToken: ct);
        }
        else
        {
            var entry = new FeedEntryDocument
            {
                Id = integrationEvent.PostId,
                PostId = integrationEvent.PostId,
                AuthorId = integrationEvent.AuthorId,
                AuthorHandle = integrationEvent.AuthorHandle,
                AuthorDisplayName = integrationEvent.AuthorDisplayName,
                Content = integrationEvent.Content,
                PostedAt = integrationEvent.OccurredAt,
                UpdatedAt = integrationEvent.OccurredAt,
                LastActivityAt = integrationEvent.OccurredAt
            };
            await entries.ReplaceOneAsync(e => e.PostId == entry.PostId, entry, new ReplaceOptions { IsUpsert = true }, ct);
        }
    }

    private async Task ApplyAsync(PostUpdated integrationEvent, CancellationToken ct)
    {
        var update = Builders<FeedEntryDocument>.Update
            .Set(e => e.Content, integrationEvent.Content)
            .Set(e => e.UpdatedAt, integrationEvent.OccurredAt);
        await entries.UpdateOneAsync(e => e.PostId == integrationEvent.PostId, update, cancellationToken: ct);
    }

    private async Task ApplyAsync(UserFollowed integrationEvent, CancellationToken ct)
    {
        var follow = new FeedFollowDocument
        {
            Id = integrationEvent.RelationshipId,
            FollowerId = integrationEvent.FollowerId,
            FollowingId = integrationEvent.FollowingId,
            CreatedAt = integrationEvent.OccurredAt
        };
        await follows.ReplaceOneAsync(f => f.FollowerId == follow.FollowerId && f.FollowingId == follow.FollowingId, follow, new ReplaceOptions { IsUpsert = true }, ct);
    }
}
