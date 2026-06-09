namespace Integration.Tests;

[Collection(nameof(IntegrationCollection))]
public sealed class FeedApiTests(IntegrationFixture fx)
{
    [Fact]
    public async Task GetFeed_without_auth_returns_200()
    {
        var response = await fx.Feed.GetAsync("/api/feed?limit=10&offset=0&followingOnly=false");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var entries = await response.Content.ReadFromJsonAsync<List<FeedEntryDto>>();
        Assert.NotNull(entries);
    }

    [Fact]
    public async Task PostCreated_event_adds_entry_to_feed()
    {
        var postId = Guid.NewGuid();
        var authorId = Guid.NewGuid();
        var evt = new
        {
            PostId = postId,
            AuthorId = authorId,
            AuthorHandle = "@testuser",
            AuthorDisplayName = "Test User",
            Content = "Event-driven post",
            OccurredAt = DateTimeOffset.UtcNow
        };

        var evtResponse = await fx.Feed.PostAsJsonAsync("/events/PostCreated", evt);
        Assert.Equal(HttpStatusCode.Accepted, evtResponse.StatusCode);

        var feedResponse = await fx.Feed.GetAsync("/api/feed?limit=50&offset=0&followingOnly=false");
        var entries = await feedResponse.Content.ReadFromJsonAsync<List<FeedEntryDto>>();
        Assert.Contains(entries!, e => e.PostId == postId);
    }

    [Fact]
    public async Task PostUpdated_event_changes_content_in_feed()
    {
        var postId = Guid.NewGuid();
        var authorId = Guid.NewGuid();

        await fx.Feed.PostAsJsonAsync("/events/PostCreated", new
        {
            PostId = postId,
            AuthorId = authorId,
            AuthorHandle = "@updater",
            AuthorDisplayName = "Updater",
            Content = "Before update",
            OccurredAt = DateTimeOffset.UtcNow
        });

        await fx.Feed.PostAsJsonAsync("/events/PostUpdated", new
        {
            PostId = postId,
            AuthorId = authorId,
            Content = "After update",
            OccurredAt = DateTimeOffset.UtcNow
        });

        var feedResponse = await fx.Feed.GetAsync("/api/feed?limit=100&offset=0&followingOnly=false");
        var entries = await feedResponse.Content.ReadFromJsonAsync<List<FeedEntryDto>>();
        var entry = Assert.Single(entries!, e => e.PostId == postId);
        Assert.Equal("After update", entry.Content);
    }

    [Fact]
    public async Task PostDeleted_event_removes_entry_from_feed()
    {
        var postId = Guid.NewGuid();
        var authorId = Guid.NewGuid();

        await fx.Feed.PostAsJsonAsync("/events/PostCreated", new
        {
            PostId = postId,
            AuthorId = authorId,
            AuthorHandle = "@deleter",
            AuthorDisplayName = "Deleter",
            Content = "Going away",
            OccurredAt = DateTimeOffset.UtcNow
        });

        await fx.Feed.PostAsJsonAsync("/events/PostDeleted", new
        {
            PostId = postId,
            AuthorId = authorId,
            OccurredAt = DateTimeOffset.UtcNow
        });

        var feedResponse = await fx.Feed.GetAsync("/api/feed?limit=100&offset=0&followingOnly=false");
        var entries = await feedResponse.Content.ReadFromJsonAsync<List<FeedEntryDto>>();
        Assert.DoesNotContain(entries!, e => e.PostId == postId);
    }

    [Fact]
    public async Task LikeAdded_event_increments_like_count_in_feed()
    {
        var postId = Guid.NewGuid();
        var authorId = Guid.NewGuid();

        await fx.Feed.PostAsJsonAsync("/events/PostCreated", new
        {
            PostId = postId,
            AuthorId = authorId,
            AuthorHandle = "@liker",
            AuthorDisplayName = "Liker",
            Content = "Likeable post",
            OccurredAt = DateTimeOffset.UtcNow
        });

        await fx.Feed.PostAsJsonAsync("/events/LikeAdded", new
        {
            LikeId = Guid.NewGuid(),
            PostId = postId,
            UserId = Guid.NewGuid(),
            OccurredAt = DateTimeOffset.UtcNow
        });

        var feedResponse = await fx.Feed.GetAsync("/api/feed?limit=100&offset=0&followingOnly=false");
        var entries = await feedResponse.Content.ReadFromJsonAsync<List<FeedEntryDto>>();
        var entry = Assert.Single(entries!, e => e.PostId == postId);
        Assert.Equal(1, entry.LikeCount);
    }

    [Fact]
    public async Task GetFeedByUser_returns_only_that_users_posts()
    {
        var userId = Guid.NewGuid();
        var otherUserId = Guid.NewGuid();

        await fx.Feed.PostAsJsonAsync("/events/PostCreated", new
        {
            PostId = Guid.NewGuid(),
            AuthorId = userId,
            AuthorHandle = "@myuser",
            AuthorDisplayName = "My User",
            Content = "My post",
            OccurredAt = DateTimeOffset.UtcNow
        });

        await fx.Feed.PostAsJsonAsync("/events/PostCreated", new
        {
            PostId = Guid.NewGuid(),
            AuthorId = otherUserId,
            AuthorHandle = "@other",
            AuthorDisplayName = "Other",
            Content = "Other post",
            OccurredAt = DateTimeOffset.UtcNow
        });

        var response = await fx.Feed.GetAsync($"/api/feed/users/{userId}?limit=50&offset=0");
        var entries = await response.Content.ReadFromJsonAsync<List<FeedEntryDto>>();
        Assert.All(entries!, e => Assert.Equal(userId, e.AuthorId));
    }

    [Fact]
    public async Task HealthCheck_returns_healthy()
    {
        var response = await fx.Feed.GetAsync("/health");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var body = await response.Content.ReadAsStringAsync();
        Assert.Equal("Healthy", body);
    }
}
