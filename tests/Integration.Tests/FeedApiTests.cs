namespace Integration.Tests;

[Collection(nameof(IntegrationCollection))]
public sealed class FeedApiTests(IntegrationFixture fx)
{
    [Fact]
    public async Task GetFeed_without_auth_returns_401()
    {
        var response = await fx.Feed.GetAsync("/api/feed?limit=10&offset=0&followingOnly=false");

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task GetFeedByUser_without_auth_returns_401()
    {
        var response = await fx.Feed.GetAsync($"/api/feed/users/{Guid.NewGuid()}?limit=10&offset=0");

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task PostCreated_event_adds_entry_to_feed()
    {
        var session = await fx.RegisterAndLoginAsync();
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

        using var req = fx.AuthorizedRequest(HttpMethod.Get, "/api/feed?limit=50&offset=0&followingOnly=false", session.Token);
        var feedResponse = await fx.Feed.SendAsync(req);
        var entries = await feedResponse.Content.ReadFromJsonAsync<List<FeedEntryDto>>();
        Assert.Contains(entries!, e => e.PostId == postId);
    }

    [Fact]
    public async Task PostUpdated_event_changes_content_in_feed()
    {
        var session = await fx.RegisterAndLoginAsync();
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

        using var req = fx.AuthorizedRequest(HttpMethod.Get, "/api/feed?limit=100&offset=0&followingOnly=false", session.Token);
        var feedResponse = await fx.Feed.SendAsync(req);
        var entries = await feedResponse.Content.ReadFromJsonAsync<List<FeedEntryDto>>();
        var entry = Assert.Single(entries!, e => e.PostId == postId);
        Assert.Equal("After update", entry.Content);
    }

    [Fact]
    public async Task PostDeleted_event_removes_entry_from_feed()
    {
        var session = await fx.RegisterAndLoginAsync();
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

        using var req = fx.AuthorizedRequest(HttpMethod.Get, "/api/feed?limit=100&offset=0&followingOnly=false", session.Token);
        var feedResponse = await fx.Feed.SendAsync(req);
        var entries = await feedResponse.Content.ReadFromJsonAsync<List<FeedEntryDto>>();
        Assert.DoesNotContain(entries!, e => e.PostId == postId);
    }

    [Fact]
    public async Task LikeAdded_event_increments_like_count_in_feed()
    {
        var session = await fx.RegisterAndLoginAsync();
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

        using var req = fx.AuthorizedRequest(HttpMethod.Get, "/api/feed?limit=100&offset=0&followingOnly=false", session.Token);
        var feedResponse = await fx.Feed.SendAsync(req);
        var entries = await feedResponse.Content.ReadFromJsonAsync<List<FeedEntryDto>>();
        var entry = Assert.Single(entries!, e => e.PostId == postId);
        Assert.Equal(1, entry.LikeCount);
    }

    [Fact]
    public async Task GetFeedByUser_returns_only_that_users_posts()
    {
        var session = await fx.RegisterAndLoginAsync();
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

        using var req = fx.AuthorizedRequest(HttpMethod.Get, $"/api/feed/users/{userId}?limit=50&offset=0", session.Token);
        var response = await fx.Feed.SendAsync(req);
        var entries = await response.Content.ReadFromJsonAsync<List<FeedEntryDto>>();
        Assert.All(entries!, e => Assert.Equal(userId, e.AuthorId));
    }

    [Fact]
    public async Task CommentAdded_event_increments_comment_count_in_feed()
    {
        var session = await fx.RegisterAndLoginAsync();
        var postId = Guid.NewGuid();
        var authorId = Guid.NewGuid();

        await fx.Feed.PostAsJsonAsync("/events/PostCreated", new
        {
            PostId = postId,
            AuthorId = authorId,
            AuthorHandle = "@commenter",
            AuthorDisplayName = "Commenter",
            Content = "Post with comments",
            OccurredAt = DateTimeOffset.UtcNow
        });

        await fx.Feed.PostAsJsonAsync("/events/CommentAdded", new
        {
            CommentId = Guid.NewGuid(),
            PostId = postId,
            AuthorId = Guid.NewGuid(),
            AuthorHandle = "@replier",
            AuthorDisplayName = "Replier",
            Content = "Nice post!",
            OccurredAt = DateTimeOffset.UtcNow
        });

        using var req = fx.AuthorizedRequest(HttpMethod.Get, "/api/feed?limit=100&offset=0&followingOnly=false", session.Token);
        var feedResponse = await fx.Feed.SendAsync(req);
        var entries = await feedResponse.Content.ReadFromJsonAsync<List<FeedEntryDto>>();
        var entry = Assert.Single(entries!, e => e.PostId == postId);
        Assert.Equal(1, entry.CommentCount);
    }

    [Fact]
    public async Task CommentDeleted_event_decrements_comment_count_in_feed()
    {
        var session = await fx.RegisterAndLoginAsync();
        var postId = Guid.NewGuid();
        var authorId = Guid.NewGuid();
        var commentId = Guid.NewGuid();
        var commentAuthorId = Guid.NewGuid();

        await fx.Feed.PostAsJsonAsync("/events/PostCreated", new
        {
            PostId = postId,
            AuthorId = authorId,
            AuthorHandle = "@author",
            AuthorDisplayName = "Author",
            Content = "Post for comment delete test",
            OccurredAt = DateTimeOffset.UtcNow
        });

        await fx.Feed.PostAsJsonAsync("/events/CommentAdded", new
        {
            CommentId = commentId,
            PostId = postId,
            AuthorId = commentAuthorId,
            AuthorHandle = "@replier",
            AuthorDisplayName = "Replier",
            Content = "A comment",
            OccurredAt = DateTimeOffset.UtcNow
        });

        await fx.Feed.PostAsJsonAsync("/events/CommentDeleted", new
        {
            CommentId = commentId,
            PostId = postId,
            AuthorId = commentAuthorId,
            OccurredAt = DateTimeOffset.UtcNow
        });

        using var req = fx.AuthorizedRequest(HttpMethod.Get, "/api/feed?limit=100&offset=0&followingOnly=false", session.Token);
        var feedResponse = await fx.Feed.SendAsync(req);
        var entries = await feedResponse.Content.ReadFromJsonAsync<List<FeedEntryDto>>();
        var entry = Assert.Single(entries!, e => e.PostId == postId);
        Assert.Equal(0, entry.CommentCount);
    }

    [Fact]
    public async Task CommentDeleted_event_does_not_decrement_comment_count_below_zero()
    {
        var session = await fx.RegisterAndLoginAsync();
        var postId = Guid.NewGuid();
        var authorId = Guid.NewGuid();

        await fx.Feed.PostAsJsonAsync("/events/PostCreated", new
        {
            PostId = postId,
            AuthorId = authorId,
            AuthorHandle = "@authorzero",
            AuthorDisplayName = "Author Zero",
            Content = "Post with no comments",
            OccurredAt = DateTimeOffset.UtcNow
        });

        await fx.Feed.PostAsJsonAsync("/events/CommentDeleted", new
        {
            CommentId = Guid.NewGuid(),
            PostId = postId,
            AuthorId = Guid.NewGuid(),
            OccurredAt = DateTimeOffset.UtcNow
        });

        using var req = fx.AuthorizedRequest(HttpMethod.Get, "/api/feed?limit=100&offset=0&followingOnly=false", session.Token);
        var feedResponse = await fx.Feed.SendAsync(req);
        var entries = await feedResponse.Content.ReadFromJsonAsync<List<FeedEntryDto>>();
        var entry = Assert.Single(entries!, e => e.PostId == postId);
        Assert.Equal(0, entry.CommentCount);
    }

    [Fact]
    public async Task HealthCheck_returns_healthy()
    {
        var response = await fx.Feed.GetAsync("/health");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var body = await response.Content.ReadAsStringAsync();
        Assert.Equal("Healthy", body);
    }

    [Fact]
    public async Task PostCreated_reply_does_not_add_separate_feed_entry()
    {
        var session = await fx.RegisterAndLoginAsync();
        var parentId = Guid.NewGuid();
        var replyId = Guid.NewGuid();
        var authorId = Guid.NewGuid();

        await fx.Feed.PostAsJsonAsync("/events/PostCreated", new
        {
            PostId = parentId,
            AuthorId = authorId,
            AuthorHandle = "@parent",
            AuthorDisplayName = "Parent Author",
            Content = "Parent post",
            OccurredAt = DateTimeOffset.UtcNow
        });

        await fx.Feed.PostAsJsonAsync("/events/PostCreated", new
        {
            PostId = replyId,
            AuthorId = Guid.NewGuid(),
            AuthorHandle = "@replier",
            AuthorDisplayName = "Replier",
            Content = "Reply content",
            OccurredAt = DateTimeOffset.UtcNow.AddSeconds(1),
            ParentPostId = parentId
        });

        using var req = fx.AuthorizedRequest(HttpMethod.Get, "/api/feed?limit=100&offset=0&followingOnly=false", session.Token);
        var feedResponse = await fx.Feed.SendAsync(req);
        var entries = await feedResponse.Content.ReadFromJsonAsync<List<FeedEntryDto>>();

        Assert.DoesNotContain(entries!, e => e.PostId == replyId);
        Assert.Contains(entries!, e => e.PostId == parentId);
    }

    [Fact]
    public async Task PostCreated_reply_bubbles_parent_to_top_of_feed()
    {
        var session = await fx.RegisterAndLoginAsync();
        var olderPostId = Guid.NewGuid();
        var newerPostId = Guid.NewGuid();
        var replyToOlderPostId = Guid.NewGuid();
        var authorId = Guid.NewGuid();
        var baseTime = DateTimeOffset.UtcNow;

        await fx.Feed.PostAsJsonAsync("/events/PostCreated", new
        {
            PostId = olderPostId,
            AuthorId = authorId,
            AuthorHandle = "@older",
            AuthorDisplayName = "Older Author",
            Content = "Older post",
            OccurredAt = baseTime
        });

        await fx.Feed.PostAsJsonAsync("/events/PostCreated", new
        {
            PostId = newerPostId,
            AuthorId = authorId,
            AuthorHandle = "@newer",
            AuthorDisplayName = "Newer Author",
            Content = "Newer post",
            OccurredAt = baseTime.AddSeconds(1)
        });

        // Reply to older post — should bubble it to top
        await fx.Feed.PostAsJsonAsync("/events/PostCreated", new
        {
            PostId = replyToOlderPostId,
            AuthorId = Guid.NewGuid(),
            AuthorHandle = "@replier",
            AuthorDisplayName = "Replier",
            Content = "Reply to old post",
            OccurredAt = baseTime.AddSeconds(2),
            ParentPostId = olderPostId
        });

        using var req = fx.AuthorizedRequest(HttpMethod.Get, "/api/feed?limit=100&offset=0&followingOnly=false", session.Token);
        var feedResponse = await fx.Feed.SendAsync(req);
        var entries = await feedResponse.Content.ReadFromJsonAsync<List<FeedEntryDto>>();

        var olderIdx = entries!.FindIndex(e => e.PostId == olderPostId);
        var newerIdx = entries.FindIndex(e => e.PostId == newerPostId);

        Assert.True(olderIdx < newerIdx, "Older post (with recent reply) should appear before newer post in feed");
    }

    [Fact]
    public async Task UserBlocked_event_stores_block_in_feed()
    {
        var blockerId = Guid.NewGuid();
        var blockedId = Guid.NewGuid();
        var blockId = Guid.NewGuid();

        var response = await fx.Feed.PostAsJsonAsync("/events/UserBlocked", new
        {
            BlockId = blockId,
            BlockerId = blockerId,
            BlockedId = blockedId,
            OccurredAt = DateTimeOffset.UtcNow
        });

        Assert.Equal(HttpStatusCode.Accepted, response.StatusCode);
    }

    [Fact]
    public async Task UserUnblocked_event_returns_accepted()
    {
        var blockerId = Guid.NewGuid();
        var blockedId = Guid.NewGuid();

        var response = await fx.Feed.PostAsJsonAsync("/events/UserUnblocked", new
        {
            BlockerId = blockerId,
            BlockedId = blockedId,
            OccurredAt = DateTimeOffset.UtcNow
        });

        Assert.Equal(HttpStatusCode.Accepted, response.StatusCode);
    }

    [Fact]
    public async Task GetFeed_does_not_show_posts_from_blocked_user()
    {
        var viewer = await fx.RegisterAndLoginAsync();
        var blockedAuthorId = Guid.NewGuid();
        var otherAuthorId = Guid.NewGuid();
        var blockedPostId = Guid.NewGuid();
        var visiblePostId = Guid.NewGuid();

        await fx.Feed.PostAsJsonAsync("/events/PostCreated", new
        {
            PostId = blockedPostId,
            AuthorId = blockedAuthorId,
            AuthorHandle = "@blocked",
            AuthorDisplayName = "Blocked",
            Content = "You should not see this",
            OccurredAt = DateTimeOffset.UtcNow
        });
        await fx.Feed.PostAsJsonAsync("/events/PostCreated", new
        {
            PostId = visiblePostId,
            AuthorId = otherAuthorId,
            AuthorHandle = "@visible",
            AuthorDisplayName = "Visible",
            Content = "You should see this",
            OccurredAt = DateTimeOffset.UtcNow
        });

        await fx.Feed.PostAsJsonAsync("/events/UserBlocked", new
        {
            BlockId = Guid.NewGuid(),
            BlockerId = viewer.UserId,
            BlockedId = blockedAuthorId,
            OccurredAt = DateTimeOffset.UtcNow
        });

        using var req = fx.AuthorizedRequest(HttpMethod.Get, "/api/feed?limit=100&offset=0&followingOnly=false", viewer.Token);
        var feedResponse = await fx.Feed.SendAsync(req);
        var entries = await feedResponse.Content.ReadFromJsonAsync<List<FeedEntryDto>>();

        Assert.DoesNotContain(entries!, e => e.PostId == blockedPostId);
        Assert.Contains(entries!, e => e.PostId == visiblePostId);
    }

    [Fact]
    public async Task GetFeed_does_not_show_posts_from_user_who_blocked_me()
    {
        var viewer = await fx.RegisterAndLoginAsync();
        var authorWhoBlockedMe = Guid.NewGuid();
        var postId = Guid.NewGuid();

        await fx.Feed.PostAsJsonAsync("/events/PostCreated", new
        {
            PostId = postId,
            AuthorId = authorWhoBlockedMe,
            AuthorHandle = "@blocker",
            AuthorDisplayName = "Blocker",
            Content = "Author blocked the viewer",
            OccurredAt = DateTimeOffset.UtcNow
        });

        await fx.Feed.PostAsJsonAsync("/events/UserBlocked", new
        {
            BlockId = Guid.NewGuid(),
            BlockerId = authorWhoBlockedMe,
            BlockedId = viewer.UserId,
            OccurredAt = DateTimeOffset.UtcNow
        });

        using var req = fx.AuthorizedRequest(HttpMethod.Get, "/api/feed?limit=100&offset=0&followingOnly=false", viewer.Token);
        var feedResponse = await fx.Feed.SendAsync(req);
        var entries = await feedResponse.Content.ReadFromJsonAsync<List<FeedEntryDto>>();

        Assert.DoesNotContain(entries!, e => e.PostId == postId);
    }

    [Fact]
    public async Task GetFeed_shows_posts_again_after_unblock()
    {
        var viewer = await fx.RegisterAndLoginAsync();
        var authorId = Guid.NewGuid();
        var postId = Guid.NewGuid();

        await fx.Feed.PostAsJsonAsync("/events/PostCreated", new
        {
            PostId = postId,
            AuthorId = authorId,
            AuthorHandle = "@reappearing",
            AuthorDisplayName = "Reappearing",
            Content = "Should reappear after unblock",
            OccurredAt = DateTimeOffset.UtcNow
        });

        await fx.Feed.PostAsJsonAsync("/events/UserBlocked", new
        {
            BlockId = Guid.NewGuid(),
            BlockerId = viewer.UserId,
            BlockedId = authorId,
            OccurredAt = DateTimeOffset.UtcNow
        });

        await fx.Feed.PostAsJsonAsync("/events/UserUnblocked", new
        {
            BlockerId = viewer.UserId,
            BlockedId = authorId,
            OccurredAt = DateTimeOffset.UtcNow
        });

        using var req = fx.AuthorizedRequest(HttpMethod.Get, "/api/feed?limit=100&offset=0&followingOnly=false", viewer.Token);
        var feedResponse = await fx.Feed.SendAsync(req);
        var entries = await feedResponse.Content.ReadFromJsonAsync<List<FeedEntryDto>>();

        Assert.Contains(entries!, e => e.PostId == postId);
    }
}
