namespace Integration.Tests;

[Collection(nameof(IntegrationCollection))]
public sealed class PostApiTests(IntegrationFixture fx)
{
    [Fact]
    public async Task CreatePost_without_auth_returns_401()
    {
        var response = await fx.Post.PostAsJsonAsync("/api/posts", new { content = "Hello" });

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task CreatePost_empty_content_returns_400()
    {
        var session = await fx.RegisterAndLoginAsync();
        using var request = fx.AuthorizedRequest(HttpMethod.Post, "/api/posts", session.Token, new { content = "" });

        var response = await fx.Post.SendAsync(request);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task CreatePost_content_over_280_chars_returns_400()
    {
        var session = await fx.RegisterAndLoginAsync();
        var longContent = new string('x', 281);
        using var request = fx.AuthorizedRequest(HttpMethod.Post, "/api/posts", session.Token, new { content = longContent });

        var response = await fx.Post.SendAsync(request);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task CreatePost_valid_content_returns_201_with_post()
    {
        var session = await fx.RegisterAndLoginAsync();
        using var request = fx.AuthorizedRequest(HttpMethod.Post, "/api/posts", session.Token, new { content = "Hello world!" });

        var response = await fx.Post.SendAsync(request);

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        var post = await response.Content.ReadFromJsonAsync<PostDto>();
        Assert.Equal("Hello world!", post!.Content);
        Assert.Equal(session.UserId, post.AuthorId);
    }

    [Fact]
    public async Task GetPost_without_auth_returns_401()
    {
        var response = await fx.Post.GetAsync($"/api/posts/{Guid.NewGuid()}");

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task GetPost_returns_post()
    {
        var session = await fx.RegisterAndLoginAsync();
        using var create = fx.AuthorizedRequest(HttpMethod.Post, "/api/posts", session.Token, new { content = "Fetch me" });
        var created = await (await fx.Post.SendAsync(create)).Content.ReadFromJsonAsync<PostDto>();

        using var get = fx.AuthorizedRequest(HttpMethod.Get, $"/api/posts/{created!.PostId}", session.Token);
        var response = await fx.Post.SendAsync(get);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var fetched = await response.Content.ReadFromJsonAsync<PostDto>();
        Assert.Equal(created.PostId, fetched!.PostId);
    }

    [Fact]
    public async Task GetPost_unknown_id_returns_404()
    {
        var session = await fx.RegisterAndLoginAsync();
        using var get = fx.AuthorizedRequest(HttpMethod.Get, $"/api/posts/{Guid.NewGuid()}", session.Token);
        var response = await fx.Post.SendAsync(get);

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task UpdatePost_changes_content()
    {
        var session = await fx.RegisterAndLoginAsync();
        using var create = fx.AuthorizedRequest(HttpMethod.Post, "/api/posts", session.Token, new { content = "Original" });
        var post = await (await fx.Post.SendAsync(create)).Content.ReadFromJsonAsync<PostDto>();

        using var update = fx.AuthorizedRequest(HttpMethod.Put, $"/api/posts/{post!.PostId}", session.Token, new { content = "Updated" });
        var response = await fx.Post.SendAsync(update);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var updated = await response.Content.ReadFromJsonAsync<PostDto>();
        Assert.Equal("Updated", updated!.Content);
    }

    [Fact]
    public async Task DeletePost_removes_post()
    {
        var session = await fx.RegisterAndLoginAsync();
        using var create = fx.AuthorizedRequest(HttpMethod.Post, "/api/posts", session.Token, new { content = "Delete me" });
        var post = await (await fx.Post.SendAsync(create)).Content.ReadFromJsonAsync<PostDto>();

        using var delete = fx.AuthorizedRequest(HttpMethod.Delete, $"/api/posts/{post!.PostId}", session.Token);
        var deleteResp = await fx.Post.SendAsync(delete);
        Assert.Equal(HttpStatusCode.NoContent, deleteResp.StatusCode);

        using var getReq = fx.AuthorizedRequest(HttpMethod.Get, $"/api/posts/{post.PostId}", session.Token);
        var getResp = await fx.Post.SendAsync(getReq);
        Assert.Equal(HttpStatusCode.NotFound, getResp.StatusCode);
    }

    [Fact]
    public async Task UpdatePost_by_non_author_returns_403()
    {
        var author = await fx.RegisterAndLoginAsync();
        var other = await fx.RegisterAndLoginAsync();

        using var create = fx.AuthorizedRequest(HttpMethod.Post, "/api/posts", author.Token, new { content = "Mine" });
        var post = await (await fx.Post.SendAsync(create)).Content.ReadFromJsonAsync<PostDto>();

        using var update = fx.AuthorizedRequest(HttpMethod.Put, $"/api/posts/{post!.PostId}", other.Token, new { content = "Stolen" });
        var response = await fx.Post.SendAsync(update);

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task DeletePost_by_non_author_returns_403()
    {
        var author = await fx.RegisterAndLoginAsync();
        var other = await fx.RegisterAndLoginAsync();

        using var create = fx.AuthorizedRequest(HttpMethod.Post, "/api/posts", author.Token, new { content = "My post" });
        var post = await (await fx.Post.SendAsync(create)).Content.ReadFromJsonAsync<PostDto>();

        using var delete = fx.AuthorizedRequest(HttpMethod.Delete, $"/api/posts/{post!.PostId}", other.Token);
        var response = await fx.Post.SendAsync(delete);

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task DeletePost_nonexistent_returns_404()
    {
        var session = await fx.RegisterAndLoginAsync();
        using var delete = fx.AuthorizedRequest(HttpMethod.Delete, $"/api/posts/{Guid.NewGuid()}", session.Token);
        var response = await fx.Post.SendAsync(delete);

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task UpdatePost_nonexistent_returns_404()
    {
        var session = await fx.RegisterAndLoginAsync();
        using var update = fx.AuthorizedRequest(HttpMethod.Put, $"/api/posts/{Guid.NewGuid()}", session.Token, new { content = "Updated" });
        var response = await fx.Post.SendAsync(update);

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task SearchPosts_returns_matching_results()
    {
        var session = await fx.RegisterAndLoginAsync();
        var unique = Guid.NewGuid().ToString("N")[..8];
        using var create = fx.AuthorizedRequest(HttpMethod.Post, "/api/posts", session.Token, new { content = $"SearchToken {unique}" });
        await fx.Post.SendAsync(create);

        using var search = fx.AuthorizedRequest(HttpMethod.Get, $"/api/posts/search?q={unique}&limit=10&offset=0", session.Token);
        var response = await fx.Post.SendAsync(search);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var results = await response.Content.ReadFromJsonAsync<SearchResultsDto>();
        Assert.NotEmpty(results!.Posts);
        Assert.Contains(results.Posts, p => p.Content.Contains(unique));
    }

    [Fact]
    public async Task SearchPosts_includes_repost_when_original_content_matches()
    {
        var author = await fx.RegisterAndLoginAsync();
        var reposter = await fx.RegisterAndLoginAsync();
        var unique = Guid.NewGuid().ToString("N")[..8];

        using var createReq = fx.AuthorizedRequest(HttpMethod.Post, "/api/posts", author.Token, new { content = $"Original post {unique}" });
        var original = await (await fx.Post.SendAsync(createReq)).Content.ReadFromJsonAsync<PostDto>();

        // Repost with empty quote — own content does not contain unique token
        using var repostReq = fx.AuthorizedRequest(HttpMethod.Post, $"/api/posts/{original!.PostId}/reposts", reposter.Token, new { content = "" });
        var repost = await (await fx.Post.SendAsync(repostReq)).Content.ReadFromJsonAsync<PostDto>();

        using var search = fx.AuthorizedRequest(HttpMethod.Get, $"/api/posts/search?q={unique}&limit=50&offset=0", author.Token);
        var results = await (await fx.Post.SendAsync(search)).Content.ReadFromJsonAsync<SearchResultsDto>();

        Assert.Contains(results!.Posts, p => p.PostId == original.PostId);
        Assert.Contains(results.Posts, p => p.PostId == repost!.PostId);
    }

    [Fact]
    public async Task SearchPosts_LikedByMe_is_false_before_liking()
    {
        var session = await fx.RegisterAndLoginAsync();
        var unique = Guid.NewGuid().ToString("N")[..8];

        using var create = fx.AuthorizedRequest(HttpMethod.Post, "/api/posts", session.Token, new { content = $"Post before like {unique}" });
        var post = await (await fx.Post.SendAsync(create)).Content.ReadFromJsonAsync<PostDto>();

        using var search = fx.AuthorizedRequest(HttpMethod.Get, $"/api/posts/search?q={unique}&limit=10&offset=0", session.Token);
        var results = await (await fx.Post.SendAsync(search)).Content.ReadFromJsonAsync<SearchResultsDto>();

        Assert.Contains(results!.Posts, p => p.PostId == post!.PostId && !p.LikedByMe);
    }

    [Fact]
    public async Task SearchPosts_LikedByMe_is_true_after_caller_likes_post()
    {
        var author = await fx.RegisterAndLoginAsync();
        var reader = await fx.RegisterAndLoginAsync();
        var unique = Guid.NewGuid().ToString("N")[..8];

        using var create = fx.AuthorizedRequest(HttpMethod.Post, "/api/posts", author.Token, new { content = $"Liked search result {unique}" });
        var post = await (await fx.Post.SendAsync(create)).Content.ReadFromJsonAsync<PostDto>();

        await fx.Engagement.SendAsync(fx.AuthorizedRequest(HttpMethod.Post, $"/api/posts/{post!.PostId}/likes", reader.Token));

        await fx.Post.PostAsJsonAsync("/events/LikeAdded",
            new { PostId = post.PostId, UserId = reader.UserId, OccurredAt = DateTimeOffset.UtcNow });

        using var search = fx.AuthorizedRequest(HttpMethod.Get, $"/api/posts/search?q={unique}&limit=10&offset=0", reader.Token);
        var results = await (await fx.Post.SendAsync(search)).Content.ReadFromJsonAsync<SearchResultsDto>();

        var found = Assert.Single(results!.Posts, p => p.PostId == post.PostId);
        Assert.True(found.LikedByMe);
        Assert.Equal(1, found.LikeCount);
    }

    [Fact]
    public async Task SearchPosts_LikedByMe_only_reflects_calling_user_not_other_likers()
    {
        var author = await fx.RegisterAndLoginAsync();
        var liker = await fx.RegisterAndLoginAsync();
        var reader = await fx.RegisterAndLoginAsync();
        var unique = Guid.NewGuid().ToString("N")[..8];

        using var create = fx.AuthorizedRequest(HttpMethod.Post, "/api/posts", author.Token, new { content = $"Multi liker search {unique}" });
        var post = await (await fx.Post.SendAsync(create)).Content.ReadFromJsonAsync<PostDto>();

        await fx.Post.PostAsJsonAsync("/events/LikeAdded",
            new { PostId = post!.PostId, UserId = liker.UserId, OccurredAt = DateTimeOffset.UtcNow });

        // reader has NOT liked the post, liker has
        using var search = fx.AuthorizedRequest(HttpMethod.Get, $"/api/posts/search?q={unique}&limit=10&offset=0", reader.Token);
        var results = await (await fx.Post.SendAsync(search)).Content.ReadFromJsonAsync<SearchResultsDto>();

        var found = Assert.Single(results!.Posts, p => p.PostId == post.PostId);
        Assert.False(found.LikedByMe);
        Assert.Equal(1, found.LikeCount);
    }

    [Fact]
    public async Task CreatePost_with_hashtags_returns_extracted_hashtags()
    {
        var session = await fx.RegisterAndLoginAsync();
        using var request = fx.AuthorizedRequest(HttpMethod.Post, "/api/posts", session.Token, new { content = "Loving #Blazor and #dotnet today!" });

        var response = await fx.Post.SendAsync(request);

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        var post = await response.Content.ReadFromJsonAsync<PostDto>();
        Assert.NotNull(post!.Hashtags);
        Assert.Contains("blazor", post.Hashtags);
        Assert.Contains("dotnet", post.Hashtags);
    }

    [Fact]
    public async Task CreatePost_without_hashtags_returns_empty_hashtag_list()
    {
        var session = await fx.RegisterAndLoginAsync();
        using var request = fx.AuthorizedRequest(HttpMethod.Post, "/api/posts", session.Token, new { content = "No tags here at all." });

        var response = await fx.Post.SendAsync(request);

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        var post = await response.Content.ReadFromJsonAsync<PostDto>();
        Assert.NotNull(post!.Hashtags);
        Assert.Empty(post.Hashtags);
    }

    [Fact]
    public async Task CreatePost_with_duplicate_hashtags_returns_distinct_tags()
    {
        var session = await fx.RegisterAndLoginAsync();
        using var request = fx.AuthorizedRequest(HttpMethod.Post, "/api/posts", session.Token, new { content = "#Blazor is great! #blazor rocks!" });

        var response = await fx.Post.SendAsync(request);

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        var post = await response.Content.ReadFromJsonAsync<PostDto>();
        Assert.NotNull(post!.Hashtags);
        Assert.Single(post.Hashtags);
        Assert.Equal("blazor", post.Hashtags![0]);
    }

    [Fact]
    public async Task UpdatePost_replaces_hashtags_with_new_content()
    {
        var session = await fx.RegisterAndLoginAsync();
        using var create = fx.AuthorizedRequest(HttpMethod.Post, "/api/posts", session.Token, new { content = "Hello #csharp world" });
        var created = await (await fx.Post.SendAsync(create)).Content.ReadFromJsonAsync<PostDto>();

        using var update = fx.AuthorizedRequest(HttpMethod.Put, $"/api/posts/{created!.PostId}", session.Token, new { content = "Now talking about #dotnet instead" });
        var response = await fx.Post.SendAsync(update);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var updated = await response.Content.ReadFromJsonAsync<PostDto>();
        Assert.DoesNotContain("csharp", updated!.Hashtags ?? []);
        Assert.Contains("dotnet", updated.Hashtags!);
    }

    [Fact]
    public async Task GetPost_returns_hashtags()
    {
        var session = await fx.RegisterAndLoginAsync();
        using var create = fx.AuthorizedRequest(HttpMethod.Post, "/api/posts", session.Token, new { content = "Check out #xunit for testing!" });
        var created = await (await fx.Post.SendAsync(create)).Content.ReadFromJsonAsync<PostDto>();

        using var get = fx.AuthorizedRequest(HttpMethod.Get, $"/api/posts/{created!.PostId}", session.Token);
        var response = await fx.Post.SendAsync(get);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var fetched = await response.Content.ReadFromJsonAsync<PostDto>();
        Assert.NotNull(fetched!.Hashtags);
        Assert.Contains("xunit", fetched.Hashtags);
    }

    [Fact]
    public async Task CreatePost_with_mentions_returns_extracted_mentions()
    {
        var session = await fx.RegisterAndLoginAsync();
        using var request = fx.AuthorizedRequest(HttpMethod.Post, "/api/posts", session.Token, new { content = "Hey @Alice and @Bob, check this out!" });

        var response = await fx.Post.SendAsync(request);

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        var post = await response.Content.ReadFromJsonAsync<PostDto>();
        Assert.NotNull(post!.Mentions);
        Assert.Contains("alice", post.Mentions);
        Assert.Contains("bob", post.Mentions);
    }

    [Fact]
    public async Task CreatePost_without_mentions_returns_empty_mention_list()
    {
        var session = await fx.RegisterAndLoginAsync();
        using var request = fx.AuthorizedRequest(HttpMethod.Post, "/api/posts", session.Token, new { content = "Just a plain post." });

        var response = await fx.Post.SendAsync(request);

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        var post = await response.Content.ReadFromJsonAsync<PostDto>();
        Assert.NotNull(post!.Mentions);
        Assert.Empty(post.Mentions);
    }

    [Fact]
    public async Task CreatePost_with_duplicate_mentions_returns_distinct_mentions()
    {
        var session = await fx.RegisterAndLoginAsync();
        using var request = fx.AuthorizedRequest(HttpMethod.Post, "/api/posts", session.Token, new { content = "@Carol is great! @carol rocks!" });

        var response = await fx.Post.SendAsync(request);

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        var post = await response.Content.ReadFromJsonAsync<PostDto>();
        Assert.NotNull(post!.Mentions);
        Assert.Single(post.Mentions);
        Assert.Equal("carol", post.Mentions![0]);
    }

    [Fact]
    public async Task UpdatePost_replaces_mentions_with_new_content()
    {
        var session = await fx.RegisterAndLoginAsync();
        using var create = fx.AuthorizedRequest(HttpMethod.Post, "/api/posts", session.Token, new { content = "Hello @dave!" });
        var created = await (await fx.Post.SendAsync(create)).Content.ReadFromJsonAsync<PostDto>();

        using var update = fx.AuthorizedRequest(HttpMethod.Put, $"/api/posts/{created!.PostId}", session.Token, new { content = "Now mentioning @eve instead" });
        var response = await fx.Post.SendAsync(update);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var updated = await response.Content.ReadFromJsonAsync<PostDto>();
        Assert.DoesNotContain("dave", updated!.Mentions ?? []);
        Assert.Contains("eve", updated.Mentions!);
    }

    [Fact]
    public async Task GetPost_returns_mentions()
    {
        var session = await fx.RegisterAndLoginAsync();
        using var create = fx.AuthorizedRequest(HttpMethod.Post, "/api/posts", session.Token, new { content = "Shoutout to @frank today!" });
        var created = await (await fx.Post.SendAsync(create)).Content.ReadFromJsonAsync<PostDto>();

        using var get = fx.AuthorizedRequest(HttpMethod.Get, $"/api/posts/{created!.PostId}", session.Token);
        var response = await fx.Post.SendAsync(get);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var fetched = await response.Content.ReadFromJsonAsync<PostDto>();
        Assert.NotNull(fetched!.Mentions);
        Assert.Contains("frank", fetched.Mentions);
    }

    [Fact]
    public async Task CreatePost_with_both_hashtags_and_mentions_extracts_both()
    {
        var session = await fx.RegisterAndLoginAsync();
        using var request = fx.AuthorizedRequest(HttpMethod.Post, "/api/posts", session.Token, new { content = "Hey @Grace, check out #dotnet!" });

        var response = await fx.Post.SendAsync(request);

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        var post = await response.Content.ReadFromJsonAsync<PostDto>();
        Assert.Contains("grace", post!.Mentions!);
        Assert.Contains("dotnet", post.Hashtags!);
    }

    [Fact]
    public async Task GetPostsByUser_without_auth_returns_401()
    {
        var response = await fx.Post.GetAsync($"/api/posts/by-user/{Guid.NewGuid()}?limit=10&offset=0");

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task GetPostsByUser_with_auth_returns_posts()
    {
        var session = await fx.RegisterAndLoginAsync();
        using var create = fx.AuthorizedRequest(HttpMethod.Post, "/api/posts", session.Token, new { content = "My authored post" });
        await fx.Post.SendAsync(create);

        using var req = fx.AuthorizedRequest(HttpMethod.Get, $"/api/posts/by-user/{session.UserId}?limit=10&offset=0", session.Token);
        var response = await fx.Post.SendAsync(req);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var posts = await response.Content.ReadFromJsonAsync<List<PostDto>>();
        Assert.NotNull(posts);
        Assert.Contains(posts, p => p.Content == "My authored post");
    }

    [Fact]
    public async Task GetRecentPosts_without_auth_returns_401()
    {
        var response = await fx.Post.GetAsync("/api/posts/recent");

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task GetRecentPosts_returns_posts_sorted_newest_first()
    {
        var session = await fx.RegisterAndLoginAsync();
        var unique = Guid.NewGuid().ToString("N")[..8];

        using var first = fx.AuthorizedRequest(HttpMethod.Post, "/api/posts", session.Token, new { content = $"First post {unique}" });
        await fx.Post.SendAsync(first);
        using var second = fx.AuthorizedRequest(HttpMethod.Post, "/api/posts", session.Token, new { content = $"Second post {unique}" });
        await fx.Post.SendAsync(second);

        using var req = fx.AuthorizedRequest(HttpMethod.Get, "/api/posts/recent?limit=50", session.Token);
        var response = await fx.Post.SendAsync(req);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var posts = await response.Content.ReadFromJsonAsync<List<PostDto>>();
        Assert.NotNull(posts);
        var myPosts = posts.Where(p => p.Content.Contains(unique)).ToList();
        Assert.Equal(2, myPosts.Count);
        Assert.True(myPosts[0].PostedAt >= myPosts[1].PostedAt, "Posts should be sorted newest first.");
    }

    [Fact]
    public async Task GetRecentPosts_limit_is_clamped_to_100()
    {
        var session = await fx.RegisterAndLoginAsync();

        using var req = fx.AuthorizedRequest(HttpMethod.Get, "/api/posts/recent?limit=999", session.Token);
        var response = await fx.Post.SendAsync(req);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var posts = await response.Content.ReadFromJsonAsync<List<PostDto>>();
        Assert.NotNull(posts);
        Assert.True(posts.Count <= 100, "Limit should be clamped to 100.");
    }

    [Fact]
    public async Task GetRecentPosts_default_limit_is_20()
    {
        var session = await fx.RegisterAndLoginAsync();
        for (var i = 0; i < 25; i++)
        {
            using var create = fx.AuthorizedRequest(HttpMethod.Post, "/api/posts", session.Token, new { content = $"Filler post {i} {Guid.NewGuid():N}" });
            await fx.Post.SendAsync(create);
        }

        using var req = fx.AuthorizedRequest(HttpMethod.Get, "/api/posts/recent", session.Token);
        var response = await fx.Post.SendAsync(req);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var posts = await response.Content.ReadFromJsonAsync<List<PostDto>>();
        Assert.NotNull(posts);
        Assert.True(posts.Count <= 20, "Default limit should be 20.");
    }

    [Fact]
    public async Task SearchPosts_excludes_replies()
    {
        var session = await fx.RegisterAndLoginAsync();
        var unique = Guid.NewGuid().ToString("N")[..8];

        using var createReq = fx.AuthorizedRequest(HttpMethod.Post, "/api/posts", session.Token, new { content = $"Root post {unique}" });
        var parent = await (await fx.Post.SendAsync(createReq)).Content.ReadFromJsonAsync<PostDto>();

        using var replyReq = fx.AuthorizedRequest(HttpMethod.Post, $"/api/posts/{parent!.PostId}/replies", session.Token, new { content = $"Reply with {unique}" });
        await fx.Post.SendAsync(replyReq);

        using var search = fx.AuthorizedRequest(HttpMethod.Get, $"/api/posts/search?q={unique}&limit=50&offset=0", session.Token);
        var results = await (await fx.Post.SendAsync(search)).Content.ReadFromJsonAsync<SearchResultsDto>();

        Assert.All(results!.Posts, p => Assert.Null(p.ParentPostId));
        Assert.Contains(results.Posts, p => p.PostId == parent.PostId);
    }

    [Fact]
    public async Task GetRecentPosts_excludes_replies()
    {
        var session = await fx.RegisterAndLoginAsync();
        var unique = Guid.NewGuid().ToString("N")[..8];

        using var createReq = fx.AuthorizedRequest(HttpMethod.Post, "/api/posts", session.Token, new { content = $"Root post {unique}" });
        var parent = await (await fx.Post.SendAsync(createReq)).Content.ReadFromJsonAsync<PostDto>();

        using var replyReq = fx.AuthorizedRequest(HttpMethod.Post, $"/api/posts/{parent!.PostId}/replies", session.Token, new { content = $"Reply with {unique}" });
        await fx.Post.SendAsync(replyReq);

        using var req = fx.AuthorizedRequest(HttpMethod.Get, "/api/posts/recent?limit=100", session.Token);
        var posts = await (await fx.Post.SendAsync(req)).Content.ReadFromJsonAsync<List<PostDto>>();

        Assert.All(posts!, p => Assert.Null(p.ParentPostId));
    }

    [Fact]
    public async Task HealthCheck_returns_healthy()
    {
        var response = await fx.Post.GetAsync("/health");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var body = await response.Content.ReadAsStringAsync();
        Assert.Equal("Healthy", body);
    }

    [Fact]
    public async Task SearchPosts_without_auth_returns_401()
    {
        var response = await fx.Post.GetAsync("/api/posts/search?q=hello&limit=10&offset=0");

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task SearchPosts_with_empty_query_returns_400()
    {
        var session = await fx.RegisterAndLoginAsync();
        using var request = fx.AuthorizedRequest(HttpMethod.Get, "/api/posts/search?q=&limit=10&offset=0", session.Token);
        var response = await fx.Post.SendAsync(request);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task SearchPosts_with_regex_special_chars_does_not_error()
    {
        var session = await fx.RegisterAndLoginAsync();
        using var create = fx.AuthorizedRequest(HttpMethod.Post, "/api/posts", session.Token, new { content = "price is $5.00 today" });
        await fx.Post.SendAsync(create);

        using var search = fx.AuthorizedRequest(HttpMethod.Get, "/api/posts/search?q=%245.00&limit=10&offset=0", session.Token);
        var response = await fx.Post.SendAsync(search);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    [Fact]
    public async Task SearchPosts_with_dot_query_matches_only_literal_dot()
    {
        var session = await fx.RegisterAndLoginAsync();
        var unique = Guid.NewGuid().ToString("N")[..8];
        using var exact = fx.AuthorizedRequest(HttpMethod.Post, "/api/posts", session.Token, new { content = $"hello.world {unique}" });
        using var noMatch = fx.AuthorizedRequest(HttpMethod.Post, "/api/posts", session.Token, new { content = $"helloXworld {unique}" });
        await fx.Post.SendAsync(exact);
        await fx.Post.SendAsync(noMatch);

        using var search = fx.AuthorizedRequest(HttpMethod.Get, $"/api/posts/search?q=hello.world+{unique}&limit=10&offset=0", session.Token);
        var response = await fx.Post.SendAsync(search);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var results = await response.Content.ReadFromJsonAsync<SearchResultsDto>();
        Assert.All(results!.Posts, p => Assert.Contains(".", p.Content));
    }

    [Fact]
    public async Task CreatePost_with_very_long_content_does_not_throw_and_extracts_tags()
    {
        var session = await fx.RegisterAndLoginAsync();
        var repeated = string.Concat(Enumerable.Repeat("#dotnet @alice ", 200));
        var content = repeated.TrimEnd()[..280];
        using var request = fx.AuthorizedRequest(HttpMethod.Post, "/api/posts", session.Token, new { content });

        var response = await fx.Post.SendAsync(request);

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        var post = await response.Content.ReadFromJsonAsync<PostDto>();
        Assert.NotNull(post);
    }

    [Fact]
    public async Task CreatePost_content_at_exactly_280_chars_with_tags_extracts_correctly()
    {
        var session = await fx.RegisterAndLoginAsync();
        var content = "#csharp " + new string('x', 272);
        using var request = fx.AuthorizedRequest(HttpMethod.Post, "/api/posts", session.Token, new { content });

        var response = await fx.Post.SendAsync(request);

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        var post = await response.Content.ReadFromJsonAsync<PostDto>();
        Assert.Contains("csharp", post!.Hashtags!);
    }

    [Fact]
    public async Task ReplyToPost_returns_201_with_parent_id_and_prefixed_content()
    {
        var author = await fx.RegisterAndLoginAsync();
        var replier = await fx.RegisterAndLoginAsync();

        using var createReq = fx.AuthorizedRequest(HttpMethod.Post, "/api/posts", author.Token, new { content = "Original post" });
        var createResp = await fx.Post.SendAsync(createReq);
        var parent = await createResp.Content.ReadFromJsonAsync<PostDto>();

        using var replyReq = fx.AuthorizedRequest(HttpMethod.Post, $"/api/posts/{parent!.PostId}/replies", replier.Token, new { content = "My reply" });
        var replyResp = await fx.Post.SendAsync(replyReq);

        Assert.Equal(HttpStatusCode.Created, replyResp.StatusCode);
        var reply = await replyResp.Content.ReadFromJsonAsync<PostDto>();
        Assert.Equal(parent.PostId, reply!.ParentPostId);
        var expectedHandle = author.Handle.TrimStart('@').ToLowerInvariant();
        Assert.StartsWith($"@{expectedHandle} ", reply.Content, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("My reply", reply.Content);
    }

    [Fact]
    public async Task ReplyToPost_already_prefixed_content_is_not_doubled()
    {
        var author = await fx.RegisterAndLoginAsync();
        var replier = await fx.RegisterAndLoginAsync();

        using var createReq = fx.AuthorizedRequest(HttpMethod.Post, "/api/posts", author.Token, new { content = "Post" });
        var parent = await (await fx.Post.SendAsync(createReq)).Content.ReadFromJsonAsync<PostDto>();

        var expectedHandle = author.Handle.TrimStart('@').ToLowerInvariant();
        var alreadyPrefixed = $"@{expectedHandle} got it";
        using var replyReq = fx.AuthorizedRequest(HttpMethod.Post, $"/api/posts/{parent!.PostId}/replies", replier.Token, new { content = alreadyPrefixed });
        var replyResp = await fx.Post.SendAsync(replyReq);

        Assert.Equal(HttpStatusCode.Created, replyResp.StatusCode);
        var reply = await replyResp.Content.ReadFromJsonAsync<PostDto>();
        Assert.Equal(alreadyPrefixed, reply!.Content);
    }

    [Fact]
    public async Task ReplyToPost_nonexistent_parent_returns_404()
    {
        var session = await fx.RegisterAndLoginAsync();

        using var replyReq = fx.AuthorizedRequest(HttpMethod.Post, $"/api/posts/{Guid.NewGuid()}/replies", session.Token, new { content = "Reply to nothing" });
        var response = await fx.Post.SendAsync(replyReq);

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task ReplyToPost_without_auth_returns_401()
    {
        var response = await fx.Post.PostAsJsonAsync($"/api/posts/{Guid.NewGuid()}/replies", new { content = "Reply" });

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task ReplyToPost_increments_parent_reply_count()
    {
        var session = await fx.RegisterAndLoginAsync();

        using var createReq = fx.AuthorizedRequest(HttpMethod.Post, "/api/posts", session.Token, new { content = "Post with replies" });
        var parent = await (await fx.Post.SendAsync(createReq)).Content.ReadFromJsonAsync<PostDto>();
        Assert.Equal(0, parent!.ReplyCount);

        using var reply1Req = fx.AuthorizedRequest(HttpMethod.Post, $"/api/posts/{parent.PostId}/replies", session.Token, new { content = "First reply" });
        await fx.Post.SendAsync(reply1Req);

        using var getReq1 = fx.AuthorizedRequest(HttpMethod.Get, $"/api/posts/{parent.PostId}", session.Token);
        var afterFirst = await (await fx.Post.SendAsync(getReq1)).Content.ReadFromJsonAsync<PostDto>();
        Assert.Equal(1, afterFirst!.ReplyCount);

        using var reply2Req = fx.AuthorizedRequest(HttpMethod.Post, $"/api/posts/{parent.PostId}/replies", session.Token, new { content = "Second reply" });
        await fx.Post.SendAsync(reply2Req);

        using var getReq2 = fx.AuthorizedRequest(HttpMethod.Get, $"/api/posts/{parent.PostId}", session.Token);
        var afterSecond = await (await fx.Post.SendAsync(getReq2)).Content.ReadFromJsonAsync<PostDto>();
        Assert.Equal(2, afterSecond!.ReplyCount);
    }

    [Fact]
    public async Task NewPost_has_zero_reply_count()
    {
        var session = await fx.RegisterAndLoginAsync();

        using var createReq = fx.AuthorizedRequest(HttpMethod.Post, "/api/posts", session.Token, new { content = "Fresh post" });
        var post = await (await fx.Post.SendAsync(createReq)).Content.ReadFromJsonAsync<PostDto>();

        Assert.Equal(0, post!.ReplyCount);
    }

    [Fact]
    public async Task DeleteReply_decrements_parent_reply_count()
    {
        var session = await fx.RegisterAndLoginAsync();

        using var createReq = fx.AuthorizedRequest(HttpMethod.Post, "/api/posts", session.Token, new { content = "Parent post" });
        var parent = await (await fx.Post.SendAsync(createReq)).Content.ReadFromJsonAsync<PostDto>();

        using var replyReq = fx.AuthorizedRequest(HttpMethod.Post, $"/api/posts/{parent!.PostId}/replies", session.Token, new { content = "A reply" });
        var reply = await (await fx.Post.SendAsync(replyReq)).Content.ReadFromJsonAsync<PostDto>();

        using var deleteReq = fx.AuthorizedRequest(HttpMethod.Delete, $"/api/posts/{reply!.PostId}", session.Token);
        var deleteResp = await fx.Post.SendAsync(deleteReq);
        Assert.Equal(HttpStatusCode.NoContent, deleteResp.StatusCode);

        using var getReq = fx.AuthorizedRequest(HttpMethod.Get, $"/api/posts/{parent.PostId}", session.Token);
        var updated = await (await fx.Post.SendAsync(getReq)).Content.ReadFromJsonAsync<PostDto>();
        Assert.Equal(0, updated!.ReplyCount);
    }

    [Fact]
    public async Task DeletePost_top_level_does_not_change_reply_count_on_any_parent()
    {
        var session = await fx.RegisterAndLoginAsync();

        using var createReq = fx.AuthorizedRequest(HttpMethod.Post, "/api/posts", session.Token, new { content = "Top-level post to delete" });
        var post = await (await fx.Post.SendAsync(createReq)).Content.ReadFromJsonAsync<PostDto>();

        using var deleteReq = fx.AuthorizedRequest(HttpMethod.Delete, $"/api/posts/{post!.PostId}", session.Token);
        var deleteResp = await fx.Post.SendAsync(deleteReq);

        Assert.Equal(HttpStatusCode.NoContent, deleteResp.StatusCode);
    }

    [Fact]
    public async Task DeleteReply_reply_count_does_not_go_below_zero()
    {
        var session = await fx.RegisterAndLoginAsync();

        using var createReq = fx.AuthorizedRequest(HttpMethod.Post, "/api/posts", session.Token, new { content = "Parent post for floor test" });
        var parent = await (await fx.Post.SendAsync(createReq)).Content.ReadFromJsonAsync<PostDto>();

        // Create and delete reply once — count should be 0 after
        using var replyReq = fx.AuthorizedRequest(HttpMethod.Post, $"/api/posts/{parent!.PostId}/replies", session.Token, new { content = "Reply to delete" });
        var reply = await (await fx.Post.SendAsync(replyReq)).Content.ReadFromJsonAsync<PostDto>();

        using var deleteReq = fx.AuthorizedRequest(HttpMethod.Delete, $"/api/posts/{reply!.PostId}", session.Token);
        await fx.Post.SendAsync(deleteReq);

        using var getReq = fx.AuthorizedRequest(HttpMethod.Get, $"/api/posts/{parent.PostId}", session.Token);
        var updated = await (await fx.Post.SendAsync(getReq)).Content.ReadFromJsonAsync<PostDto>();
        Assert.Equal(0, updated!.ReplyCount);
    }

    [Fact]
    public async Task ReplyToPost_empty_content_returns_400()
    {
        var session = await fx.RegisterAndLoginAsync();

        using var createReq = fx.AuthorizedRequest(HttpMethod.Post, "/api/posts", session.Token, new { content = "Original" });
        var createResp = await fx.Post.SendAsync(createReq);
        var parent = await createResp.Content.ReadFromJsonAsync<PostDto>();

        using var replyReq = fx.AuthorizedRequest(HttpMethod.Post, $"/api/posts/{parent!.PostId}/replies", session.Token, new { content = "" });
        var response = await fx.Post.SendAsync(replyReq);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task GetReplies_returns_replies_in_chronological_order()
    {
        var session = await fx.RegisterAndLoginAsync();

        using var createReq = fx.AuthorizedRequest(HttpMethod.Post, "/api/posts", session.Token, new { content = "Original post" });
        var createResp = await fx.Post.SendAsync(createReq);
        var parent = await createResp.Content.ReadFromJsonAsync<PostDto>();

        using var r1 = fx.AuthorizedRequest(HttpMethod.Post, $"/api/posts/{parent!.PostId}/replies", session.Token, new { content = "First reply" });
        using var r2 = fx.AuthorizedRequest(HttpMethod.Post, $"/api/posts/{parent.PostId}/replies", session.Token, new { content = "Second reply" });
        await fx.Post.SendAsync(r1);
        await fx.Post.SendAsync(r2);

        using var listReq = fx.AuthorizedRequest(HttpMethod.Get, $"/api/posts/{parent.PostId}/replies", session.Token);
        var listResp = await fx.Post.SendAsync(listReq);

        Assert.Equal(HttpStatusCode.OK, listResp.StatusCode);
        var replies = await listResp.Content.ReadFromJsonAsync<List<PostDto>>();
        Assert.Equal(2, replies!.Count);
        Assert.All(replies, r => Assert.Equal(parent.PostId, r.ParentPostId));
        Assert.Contains("First reply", replies[0].Content);
        Assert.Contains("Second reply", replies[1].Content);
    }

    [Fact]
    public async Task GetReplies_returns_empty_list_when_no_replies()
    {
        var session = await fx.RegisterAndLoginAsync();

        using var createReq = fx.AuthorizedRequest(HttpMethod.Post, "/api/posts", session.Token, new { content = "No replies yet" });
        var createResp = await fx.Post.SendAsync(createReq);
        var parent = await createResp.Content.ReadFromJsonAsync<PostDto>();

        using var listReq = fx.AuthorizedRequest(HttpMethod.Get, $"/api/posts/{parent!.PostId}/replies", session.Token);
        var listResp = await fx.Post.SendAsync(listReq);

        Assert.Equal(HttpStatusCode.OK, listResp.StatusCode);
        var replies = await listResp.Content.ReadFromJsonAsync<List<PostDto>>();
        Assert.Empty(replies!);
    }

    [Fact]
    public async Task GetReplies_without_auth_returns_401()
    {
        var response = await fx.Post.GetAsync($"/api/posts/{Guid.NewGuid()}/replies");

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task Repost_returns_201_with_original_id()
    {
        var author = await fx.RegisterAndLoginAsync();
        var reposter = await fx.RegisterAndLoginAsync();

        using var createReq = fx.AuthorizedRequest(HttpMethod.Post, "/api/posts", author.Token, new { content = "Original post" });
        var createResp = await fx.Post.SendAsync(createReq);
        var original = await createResp.Content.ReadFromJsonAsync<PostDto>();

        using var repostReq = fx.AuthorizedRequest(HttpMethod.Post, $"/api/posts/{original!.PostId}/reposts", reposter.Token, new { content = "Great post!" });
        var repostResp = await fx.Post.SendAsync(repostReq);

        Assert.Equal(HttpStatusCode.Created, repostResp.StatusCode);
        var repost = await repostResp.Content.ReadFromJsonAsync<PostDto>();
        Assert.Equal(original.PostId, repost!.OriginalPostId);
        Assert.Equal(reposter.UserId, repost.AuthorId);
    }

    [Fact]
    public async Task Repost_own_post_returns_409()
    {
        var session = await fx.RegisterAndLoginAsync();

        using var createReq = fx.AuthorizedRequest(HttpMethod.Post, "/api/posts", session.Token, new { content = "My post" });
        var createResp = await fx.Post.SendAsync(createReq);
        var original = await createResp.Content.ReadFromJsonAsync<PostDto>();

        using var repostReq = fx.AuthorizedRequest(HttpMethod.Post, $"/api/posts/{original!.PostId}/reposts", session.Token, new { content = "" });
        var response = await fx.Post.SendAsync(repostReq);

        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
    }

    [Fact]
    public async Task Repost_same_post_twice_returns_409()
    {
        var author = await fx.RegisterAndLoginAsync();
        var reposter = await fx.RegisterAndLoginAsync();

        using var createReq = fx.AuthorizedRequest(HttpMethod.Post, "/api/posts", author.Token, new { content = "Original" });
        var createResp = await fx.Post.SendAsync(createReq);
        var original = await createResp.Content.ReadFromJsonAsync<PostDto>();

        using var r1 = fx.AuthorizedRequest(HttpMethod.Post, $"/api/posts/{original!.PostId}/reposts", reposter.Token, new { content = "" });
        await fx.Post.SendAsync(r1);

        using var r2 = fx.AuthorizedRequest(HttpMethod.Post, $"/api/posts/{original.PostId}/reposts", reposter.Token, new { content = "" });
        var response = await fx.Post.SendAsync(r2);

        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
    }

    [Fact]
    public async Task Repost_nonexistent_post_returns_404()
    {
        var session = await fx.RegisterAndLoginAsync();

        using var req = fx.AuthorizedRequest(HttpMethod.Post, $"/api/posts/{Guid.NewGuid()}/reposts", session.Token, new { content = "" });
        var response = await fx.Post.SendAsync(req);

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task Repost_without_auth_returns_401()
    {
        var response = await fx.Post.PostAsJsonAsync($"/api/posts/{Guid.NewGuid()}/reposts", new { content = "" });

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task DeleteMyRepost_returns_204()
    {
        var author = await fx.RegisterAndLoginAsync();
        var reposter = await fx.RegisterAndLoginAsync();

        using var createReq = fx.AuthorizedRequest(HttpMethod.Post, "/api/posts", author.Token, new { content = "Original" });
        var original = await (await fx.Post.SendAsync(createReq)).Content.ReadFromJsonAsync<PostDto>();

        using var repostReq = fx.AuthorizedRequest(HttpMethod.Post, $"/api/posts/{original!.PostId}/reposts", reposter.Token, new { content = "" });
        await fx.Post.SendAsync(repostReq);

        using var deleteReq = fx.AuthorizedRequest(HttpMethod.Delete, $"/api/posts/{original.PostId}/reposts/mine", reposter.Token);
        var response = await fx.Post.SendAsync(deleteReq);

        Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);
    }

    [Fact]
    public async Task DeleteMyRepost_when_not_reposted_returns_404()
    {
        var author = await fx.RegisterAndLoginAsync();
        var other = await fx.RegisterAndLoginAsync();

        using var createReq = fx.AuthorizedRequest(HttpMethod.Post, "/api/posts", author.Token, new { content = "Post" });
        var original = await (await fx.Post.SendAsync(createReq)).Content.ReadFromJsonAsync<PostDto>();

        using var deleteReq = fx.AuthorizedRequest(HttpMethod.Delete, $"/api/posts/{original!.PostId}/reposts/mine", other.Token);
        var response = await fx.Post.SendAsync(deleteReq);

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task DeleteMyRepost_after_delete_allows_repost_again()
    {
        var author = await fx.RegisterAndLoginAsync();
        var reposter = await fx.RegisterAndLoginAsync();

        using var createReq = fx.AuthorizedRequest(HttpMethod.Post, "/api/posts", author.Token, new { content = "Original" });
        var original = await (await fx.Post.SendAsync(createReq)).Content.ReadFromJsonAsync<PostDto>();

        using var r1 = fx.AuthorizedRequest(HttpMethod.Post, $"/api/posts/{original!.PostId}/reposts", reposter.Token, new { content = "" });
        await fx.Post.SendAsync(r1);

        using var del = fx.AuthorizedRequest(HttpMethod.Delete, $"/api/posts/{original.PostId}/reposts/mine", reposter.Token);
        await fx.Post.SendAsync(del);

        using var r2 = fx.AuthorizedRequest(HttpMethod.Post, $"/api/posts/{original.PostId}/reposts", reposter.Token, new { content = "" });
        var resp = await fx.Post.SendAsync(r2);

        Assert.Equal(HttpStatusCode.Created, resp.StatusCode);
    }

    [Fact]
    public async Task DeleteMyRepost_without_auth_returns_401()
    {
        var response = await fx.Post.DeleteAsync($"/api/posts/{Guid.NewGuid()}/reposts/mine");

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task Repost_increments_original_post_repost_count()
    {
        var author = await fx.RegisterAndLoginAsync();
        var reposter1 = await fx.RegisterAndLoginAsync();
        var reposter2 = await fx.RegisterAndLoginAsync();

        using var createReq = fx.AuthorizedRequest(HttpMethod.Post, "/api/posts", author.Token, new { content = "Original post" });
        var original = await (await fx.Post.SendAsync(createReq)).Content.ReadFromJsonAsync<PostDto>();
        Assert.Equal(0, original!.RepostCount);

        using var r1 = fx.AuthorizedRequest(HttpMethod.Post, $"/api/posts/{original.PostId}/reposts", reposter1.Token, new { content = "" });
        await fx.Post.SendAsync(r1);

        using var getReq1 = fx.AuthorizedRequest(HttpMethod.Get, $"/api/posts/{original.PostId}", author.Token);
        var after1 = await (await fx.Post.SendAsync(getReq1)).Content.ReadFromJsonAsync<PostDto>();
        Assert.Equal(1, after1!.RepostCount);

        using var r2 = fx.AuthorizedRequest(HttpMethod.Post, $"/api/posts/{original.PostId}/reposts", reposter2.Token, new { content = "" });
        await fx.Post.SendAsync(r2);

        using var getReq2 = fx.AuthorizedRequest(HttpMethod.Get, $"/api/posts/{original.PostId}", author.Token);
        var after2 = await (await fx.Post.SendAsync(getReq2)).Content.ReadFromJsonAsync<PostDto>();
        Assert.Equal(2, after2!.RepostCount);
    }

    [Fact]
    public async Task DeleteMyRepost_decrements_original_post_repost_count()
    {
        var author = await fx.RegisterAndLoginAsync();
        var reposter = await fx.RegisterAndLoginAsync();

        using var createReq = fx.AuthorizedRequest(HttpMethod.Post, "/api/posts", author.Token, new { content = "Post to repost" });
        var original = await (await fx.Post.SendAsync(createReq)).Content.ReadFromJsonAsync<PostDto>();

        using var repostReq = fx.AuthorizedRequest(HttpMethod.Post, $"/api/posts/{original!.PostId}/reposts", reposter.Token, new { content = "" });
        await fx.Post.SendAsync(repostReq);

        using var deleteReq = fx.AuthorizedRequest(HttpMethod.Delete, $"/api/posts/{original.PostId}/reposts/mine", reposter.Token);
        await fx.Post.SendAsync(deleteReq);

        using var getReq = fx.AuthorizedRequest(HttpMethod.Get, $"/api/posts/{original.PostId}", author.Token);
        var after = await (await fx.Post.SendAsync(getReq)).Content.ReadFromJsonAsync<PostDto>();
        Assert.Equal(0, after!.RepostCount);
    }

    [Fact]
    public async Task DeletePost_when_post_is_repost_decrements_original_repost_count()
    {
        var author = await fx.RegisterAndLoginAsync();
        var reposter = await fx.RegisterAndLoginAsync();

        using var createReq = fx.AuthorizedRequest(HttpMethod.Post, "/api/posts", author.Token, new { content = "Original to count reposts of" });
        var original = await (await fx.Post.SendAsync(createReq)).Content.ReadFromJsonAsync<PostDto>();

        using var repostReq = fx.AuthorizedRequest(HttpMethod.Post, $"/api/posts/{original!.PostId}/reposts", reposter.Token, new { content = "" });
        var repost = await (await fx.Post.SendAsync(repostReq)).Content.ReadFromJsonAsync<PostDto>();

        // Delete the repost via generic delete (not via reposts/mine)
        using var deleteReq = fx.AuthorizedRequest(HttpMethod.Delete, $"/api/posts/{repost!.PostId}", reposter.Token);
        var deleteResp = await fx.Post.SendAsync(deleteReq);
        Assert.Equal(HttpStatusCode.NoContent, deleteResp.StatusCode);

        using var getReq = fx.AuthorizedRequest(HttpMethod.Get, $"/api/posts/{original.PostId}", author.Token);
        var after = await (await fx.Post.SendAsync(getReq)).Content.ReadFromJsonAsync<PostDto>();
        Assert.Equal(0, after!.RepostCount);
    }

    [Fact]
    public async Task GetPost_returns_reposted_by_me_false_when_not_reposted()
    {
        var author = await fx.RegisterAndLoginAsync();
        var reader = await fx.RegisterAndLoginAsync();

        using var create = fx.AuthorizedRequest(HttpMethod.Post, "/api/posts", author.Token, new { content = "Post to check" });
        var post = await (await fx.Post.SendAsync(create)).Content.ReadFromJsonAsync<PostDto>();

        using var get = fx.AuthorizedRequest(HttpMethod.Get, $"/api/posts/{post!.PostId}", reader.Token);
        var fetched = await (await fx.Post.SendAsync(get)).Content.ReadFromJsonAsync<PostDto>();

        Assert.False(fetched!.RepostedByMe);
    }

    [Fact]
    public async Task GetPost_returns_reposted_by_me_true_after_reposting()
    {
        var author = await fx.RegisterAndLoginAsync();
        var reposter = await fx.RegisterAndLoginAsync();

        using var create = fx.AuthorizedRequest(HttpMethod.Post, "/api/posts", author.Token, new { content = "Repostable" });
        var original = await (await fx.Post.SendAsync(create)).Content.ReadFromJsonAsync<PostDto>();

        using var repost = fx.AuthorizedRequest(HttpMethod.Post, $"/api/posts/{original!.PostId}/reposts", reposter.Token, new { content = "" });
        await fx.Post.SendAsync(repost);

        using var get = fx.AuthorizedRequest(HttpMethod.Get, $"/api/posts/{original.PostId}", reposter.Token);
        var fetched = await (await fx.Post.SendAsync(get)).Content.ReadFromJsonAsync<PostDto>();

        Assert.True(fetched!.RepostedByMe);
    }

    [Fact]
    public async Task GetPost_returns_reposted_by_me_false_after_deleting_repost()
    {
        var author = await fx.RegisterAndLoginAsync();
        var reposter = await fx.RegisterAndLoginAsync();

        using var create = fx.AuthorizedRequest(HttpMethod.Post, "/api/posts", author.Token, new { content = "Repostable" });
        var original = await (await fx.Post.SendAsync(create)).Content.ReadFromJsonAsync<PostDto>();

        using var repost = fx.AuthorizedRequest(HttpMethod.Post, $"/api/posts/{original!.PostId}/reposts", reposter.Token, new { content = "" });
        await fx.Post.SendAsync(repost);

        using var del = fx.AuthorizedRequest(HttpMethod.Delete, $"/api/posts/{original.PostId}/reposts/mine", reposter.Token);
        await fx.Post.SendAsync(del);

        using var get = fx.AuthorizedRequest(HttpMethod.Get, $"/api/posts/{original.PostId}", reposter.Token);
        var fetched = await (await fx.Post.SendAsync(get)).Content.ReadFromJsonAsync<PostDto>();

        Assert.False(fetched!.RepostedByMe);
    }

    [Fact]
    public async Task GetPost_reposted_by_me_is_false_for_original_author()
    {
        var author = await fx.RegisterAndLoginAsync();

        using var create = fx.AuthorizedRequest(HttpMethod.Post, "/api/posts", author.Token, new { content = "My post" });
        var post = await (await fx.Post.SendAsync(create)).Content.ReadFromJsonAsync<PostDto>();

        using var get = fx.AuthorizedRequest(HttpMethod.Get, $"/api/posts/{post!.PostId}", author.Token);
        var fetched = await (await fx.Post.SendAsync(get)).Content.ReadFromJsonAsync<PostDto>();

        Assert.False(fetched!.RepostedByMe);
    }

    [Fact]
    public async Task GetPost_reposted_by_me_is_false_for_another_users_repost()
    {
        var author = await fx.RegisterAndLoginAsync();
        var reposter1 = await fx.RegisterAndLoginAsync();
        var reposter2 = await fx.RegisterAndLoginAsync();

        using var create = fx.AuthorizedRequest(HttpMethod.Post, "/api/posts", author.Token, new { content = "Shared post" });
        var original = await (await fx.Post.SendAsync(create)).Content.ReadFromJsonAsync<PostDto>();

        using var repost = fx.AuthorizedRequest(HttpMethod.Post, $"/api/posts/{original!.PostId}/reposts", reposter1.Token, new { content = "" });
        await fx.Post.SendAsync(repost);

        using var get = fx.AuthorizedRequest(HttpMethod.Get, $"/api/posts/{original.PostId}", reposter2.Token);
        var fetched = await (await fx.Post.SendAsync(get)).Content.ReadFromJsonAsync<PostDto>();

        Assert.False(fetched!.RepostedByMe);
    }

    [Fact]
    public async Task RecentPosts_RepostedByMe_is_true_after_reposting()
    {
        var author = await fx.RegisterAndLoginAsync();
        var reposter = await fx.RegisterAndLoginAsync();

        using var create = fx.AuthorizedRequest(HttpMethod.Post, "/api/posts", author.Token, new { content = "Post to repost in feed" });
        var original = await (await fx.Post.SendAsync(create)).Content.ReadFromJsonAsync<PostDto>();

        using var repost = fx.AuthorizedRequest(HttpMethod.Post, $"/api/posts/{original!.PostId}/reposts", reposter.Token, new { content = "" });
        await fx.Post.SendAsync(repost);

        using var get = fx.AuthorizedRequest(HttpMethod.Get, "/api/posts/recent?limit=50", reposter.Token);
        var result = await (await fx.Post.SendAsync(get)).Content.ReadFromJsonAsync<List<PostDto>>();

        var found = result!.Single(p => p.PostId == original.PostId);
        Assert.True(found.RepostedByMe);
    }

    [Fact]
    public async Task RecentPosts_RepostedByMe_is_false_for_non_reposter()
    {
        var author = await fx.RegisterAndLoginAsync();
        var reposter = await fx.RegisterAndLoginAsync();
        var reader = await fx.RegisterAndLoginAsync();

        using var create = fx.AuthorizedRequest(HttpMethod.Post, "/api/posts", author.Token, new { content = "Post for reader feed check" });
        var original = await (await fx.Post.SendAsync(create)).Content.ReadFromJsonAsync<PostDto>();

        using var repost = fx.AuthorizedRequest(HttpMethod.Post, $"/api/posts/{original!.PostId}/reposts", reposter.Token, new { content = "" });
        await fx.Post.SendAsync(repost);

        using var get = fx.AuthorizedRequest(HttpMethod.Get, "/api/posts/recent?limit=50", reader.Token);
        var result = await (await fx.Post.SendAsync(get)).Content.ReadFromJsonAsync<List<PostDto>>();

        var found = result!.Single(p => p.PostId == original.PostId);
        Assert.False(found.RepostedByMe);
    }

    [Fact]
    public async Task ByUser_RepostedByMe_is_true_after_reposting()
    {
        var author = await fx.RegisterAndLoginAsync();
        var reposter = await fx.RegisterAndLoginAsync();

        using var create = fx.AuthorizedRequest(HttpMethod.Post, "/api/posts", author.Token, new { content = "Post visible on author profile" });
        var original = await (await fx.Post.SendAsync(create)).Content.ReadFromJsonAsync<PostDto>();

        using var repost = fx.AuthorizedRequest(HttpMethod.Post, $"/api/posts/{original!.PostId}/reposts", reposter.Token, new { content = "" });
        await fx.Post.SendAsync(repost);

        using var get = fx.AuthorizedRequest(HttpMethod.Get, $"/api/posts/by-user/{author.UserId}?limit=20&offset=0", reposter.Token);
        var result = await (await fx.Post.SendAsync(get)).Content.ReadFromJsonAsync<List<PostDto>>();

        var found = result!.Single(p => p.PostId == original.PostId);
        Assert.True(found.RepostedByMe);
    }

    [Fact]
    public async Task SearchPosts_RepostedByMe_is_true_after_reposting()
    {
        var author = await fx.RegisterAndLoginAsync();
        var reposter = await fx.RegisterAndLoginAsync();
        var unique = Guid.NewGuid().ToString("N")[..8];

        using var create = fx.AuthorizedRequest(HttpMethod.Post, "/api/posts", author.Token, new { content = $"Searchable repost target {unique}" });
        var original = await (await fx.Post.SendAsync(create)).Content.ReadFromJsonAsync<PostDto>();

        using var repost = fx.AuthorizedRequest(HttpMethod.Post, $"/api/posts/{original!.PostId}/reposts", reposter.Token, new { content = "" });
        await fx.Post.SendAsync(repost);

        using var search = fx.AuthorizedRequest(HttpMethod.Get, $"/api/posts/search?q={unique}&limit=20&offset=0", reposter.Token);
        var results = await (await fx.Post.SendAsync(search)).Content.ReadFromJsonAsync<SearchResultsDto>();

        var found = results!.Posts.Single(p => p.PostId == original.PostId);
        Assert.True(found.RepostedByMe);
    }

    [Fact]
    public async Task DeletePost_soft_deletes_so_GET_returns_404()
    {
        var session = await fx.RegisterAndLoginAsync();
        using var create = fx.AuthorizedRequest(HttpMethod.Post, "/api/posts", session.Token, new { content = "To be soft-deleted" });
        var post = await (await fx.Post.SendAsync(create)).Content.ReadFromJsonAsync<PostDto>();

        using var delete = fx.AuthorizedRequest(HttpMethod.Delete, $"/api/posts/{post!.PostId}", session.Token);
        var deleteResp = await fx.Post.SendAsync(delete);
        Assert.Equal(HttpStatusCode.NoContent, deleteResp.StatusCode);

        using var get = fx.AuthorizedRequest(HttpMethod.Get, $"/api/posts/{post.PostId}", session.Token);
        var getResp = await fx.Post.SendAsync(get);
        Assert.Equal(HttpStatusCode.NotFound, getResp.StatusCode);
    }

    [Fact]
    public async Task DeletedPost_does_not_appear_in_recent_posts()
    {
        var session = await fx.RegisterAndLoginAsync();
        var unique = Guid.NewGuid().ToString("N")[..8];

        using var create = fx.AuthorizedRequest(HttpMethod.Post, "/api/posts", session.Token, new { content = $"Post to delete {unique}" });
        var post = await (await fx.Post.SendAsync(create)).Content.ReadFromJsonAsync<PostDto>();

        using var delete = fx.AuthorizedRequest(HttpMethod.Delete, $"/api/posts/{post!.PostId}", session.Token);
        await fx.Post.SendAsync(delete);

        using var recent = fx.AuthorizedRequest(HttpMethod.Get, "/api/posts/recent?limit=100", session.Token);
        var posts = await (await fx.Post.SendAsync(recent)).Content.ReadFromJsonAsync<List<PostDto>>();

        Assert.DoesNotContain(posts!, p => p.PostId == post.PostId);
    }

    [Fact]
    public async Task DeletedPost_does_not_appear_in_search_results()
    {
        var session = await fx.RegisterAndLoginAsync();
        var unique = Guid.NewGuid().ToString("N")[..8];

        using var create = fx.AuthorizedRequest(HttpMethod.Post, "/api/posts", session.Token, new { content = $"Deleted searchable post {unique}" });
        var post = await (await fx.Post.SendAsync(create)).Content.ReadFromJsonAsync<PostDto>();

        using var delete = fx.AuthorizedRequest(HttpMethod.Delete, $"/api/posts/{post!.PostId}", session.Token);
        await fx.Post.SendAsync(delete);

        using var search = fx.AuthorizedRequest(HttpMethod.Get, $"/api/posts/search?q={unique}&limit=50&offset=0", session.Token);
        var results = await (await fx.Post.SendAsync(search)).Content.ReadFromJsonAsync<SearchResultsDto>();

        Assert.DoesNotContain(results!.Posts, p => p.PostId == post.PostId);
    }

    [Fact]
    public async Task ReplyToDeletedPost_returns_404()
    {
        var session = await fx.RegisterAndLoginAsync();

        using var create = fx.AuthorizedRequest(HttpMethod.Post, "/api/posts", session.Token, new { content = "Will be deleted" });
        var post = await (await fx.Post.SendAsync(create)).Content.ReadFromJsonAsync<PostDto>();

        using var delete = fx.AuthorizedRequest(HttpMethod.Delete, $"/api/posts/{post!.PostId}", session.Token);
        await fx.Post.SendAsync(delete);

        using var reply = fx.AuthorizedRequest(HttpMethod.Post, $"/api/posts/{post.PostId}/replies", session.Token, new { content = "Reply to deleted post" });
        var response = await fx.Post.SendAsync(reply);

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task DeletedReply_does_not_appear_in_reply_list()
    {
        var session = await fx.RegisterAndLoginAsync();

        using var create = fx.AuthorizedRequest(HttpMethod.Post, "/api/posts", session.Token, new { content = "Parent post" });
        var parent = await (await fx.Post.SendAsync(create)).Content.ReadFromJsonAsync<PostDto>();

        using var r1 = fx.AuthorizedRequest(HttpMethod.Post, $"/api/posts/{parent!.PostId}/replies", session.Token, new { content = "Kept reply" });
        var kept = await (await fx.Post.SendAsync(r1)).Content.ReadFromJsonAsync<PostDto>();

        using var r2 = fx.AuthorizedRequest(HttpMethod.Post, $"/api/posts/{parent.PostId}/replies", session.Token, new { content = "Deleted reply" });
        var toDelete = await (await fx.Post.SendAsync(r2)).Content.ReadFromJsonAsync<PostDto>();

        using var delete = fx.AuthorizedRequest(HttpMethod.Delete, $"/api/posts/{toDelete!.PostId}", session.Token);
        await fx.Post.SendAsync(delete);

        using var list = fx.AuthorizedRequest(HttpMethod.Get, $"/api/posts/{parent.PostId}/replies", session.Token);
        var replies = await (await fx.Post.SendAsync(list)).Content.ReadFromJsonAsync<List<PostDto>>();

        Assert.Contains(replies!, r => r.PostId == kept!.PostId);
        Assert.DoesNotContain(replies!, r => r.PostId == toDelete.PostId);
    }

    [Fact]
    public async Task GetPost_repost_includes_quoted_post_with_original_content()
    {
        var author = await fx.RegisterAndLoginAsync();
        var reposter = await fx.RegisterAndLoginAsync();

        using var create = fx.AuthorizedRequest(HttpMethod.Post, "/api/posts", author.Token, new { content = "Original content here" });
        var original = await (await fx.Post.SendAsync(create)).Content.ReadFromJsonAsync<PostDto>();

        using var repostReq = fx.AuthorizedRequest(HttpMethod.Post, $"/api/posts/{original!.PostId}/reposts", reposter.Token, new { content = "My quote" });
        var repost = await (await fx.Post.SendAsync(repostReq)).Content.ReadFromJsonAsync<PostDto>();

        using var get = fx.AuthorizedRequest(HttpMethod.Get, $"/api/posts/{repost!.PostId}", reposter.Token);
        var fetched = await (await fx.Post.SendAsync(get)).Content.ReadFromJsonAsync<PostDto>();

        Assert.NotNull(fetched!.QuotedPost);
        Assert.Equal(original.PostId, fetched.QuotedPost!.PostId);
        Assert.Equal("Original content here", fetched.QuotedPost.Content);
        Assert.Contains(author.Handle, fetched.QuotedPost.AuthorHandle);
    }

    [Fact]
    public async Task GetPost_regular_post_has_null_quoted_post()
    {
        var session = await fx.RegisterAndLoginAsync();

        using var create = fx.AuthorizedRequest(HttpMethod.Post, "/api/posts", session.Token, new { content = "Just a post" });
        var post = await (await fx.Post.SendAsync(create)).Content.ReadFromJsonAsync<PostDto>();

        using var get = fx.AuthorizedRequest(HttpMethod.Get, $"/api/posts/{post!.PostId}", session.Token);
        var fetched = await (await fx.Post.SendAsync(get)).Content.ReadFromJsonAsync<PostDto>();

        Assert.Null(fetched!.QuotedPost);
    }

    [Fact]
    public async Task GetRecentPosts_reposts_include_quoted_post()
    {
        var author = await fx.RegisterAndLoginAsync();
        var reposter = await fx.RegisterAndLoginAsync();
        var unique = Guid.NewGuid().ToString("N")[..8];

        using var create = fx.AuthorizedRequest(HttpMethod.Post, "/api/posts", author.Token, new { content = $"Original {unique}" });
        var original = await (await fx.Post.SendAsync(create)).Content.ReadFromJsonAsync<PostDto>();

        using var repostReq = fx.AuthorizedRequest(HttpMethod.Post, $"/api/posts/{original!.PostId}/reposts", reposter.Token, new { content = "" });
        await fx.Post.SendAsync(repostReq);

        using var recent = fx.AuthorizedRequest(HttpMethod.Get, "/api/posts/recent?limit=100", reposter.Token);
        var posts = await (await fx.Post.SendAsync(recent)).Content.ReadFromJsonAsync<List<PostDto>>();

        var repostInFeed = posts!.FirstOrDefault(p => p.OriginalPostId == original.PostId);
        Assert.NotNull(repostInFeed);
        Assert.NotNull(repostInFeed!.QuotedPost);
        Assert.Equal($"Original {unique}", repostInFeed.QuotedPost!.Content);
    }

    [Fact]
    public async Task SearchPosts_reposts_include_quoted_post()
    {
        var author = await fx.RegisterAndLoginAsync();
        var reposter = await fx.RegisterAndLoginAsync();
        var unique = Guid.NewGuid().ToString("N")[..8];

        using var create = fx.AuthorizedRequest(HttpMethod.Post, "/api/posts", author.Token, new { content = $"Searchable original {unique}" });
        var original = await (await fx.Post.SendAsync(create)).Content.ReadFromJsonAsync<PostDto>();

        using var repostReq = fx.AuthorizedRequest(HttpMethod.Post, $"/api/posts/{original!.PostId}/reposts", reposter.Token, new { content = "" });
        await fx.Post.SendAsync(repostReq);

        using var search = fx.AuthorizedRequest(HttpMethod.Get, $"/api/posts/search?q={unique}&limit=50&offset=0", reposter.Token);
        var results = await (await fx.Post.SendAsync(search)).Content.ReadFromJsonAsync<SearchResultsDto>();

        var repostResult = results!.Posts.FirstOrDefault(p => p.OriginalPostId == original.PostId);
        Assert.NotNull(repostResult);
        Assert.NotNull(repostResult!.QuotedPost);
        Assert.Equal($"Searchable original {unique}", repostResult.QuotedPost!.Content);
    }

    [Fact]
    public async Task GetPost_reply_includes_reply_target_with_parent_content()
    {
        var author = await fx.RegisterAndLoginAsync();
        var replier = await fx.RegisterAndLoginAsync();

        using var createReq = fx.AuthorizedRequest(HttpMethod.Post, "/api/posts", author.Token, new { content = "Parent post content" });
        var parent = await (await fx.Post.SendAsync(createReq)).Content.ReadFromJsonAsync<PostDto>();

        using var replyReq = fx.AuthorizedRequest(HttpMethod.Post, $"/api/posts/{parent!.PostId}/replies", replier.Token, new { content = "Replying here" });
        var reply = await (await fx.Post.SendAsync(replyReq)).Content.ReadFromJsonAsync<PostDto>();

        using var getReq = fx.AuthorizedRequest(HttpMethod.Get, $"/api/posts/{reply!.PostId}", replier.Token);
        var fetched = await (await fx.Post.SendAsync(getReq)).Content.ReadFromJsonAsync<PostDto>();

        Assert.NotNull(fetched!.ReplyTarget);
        Assert.Equal(parent.PostId, fetched.ReplyTarget!.PostId);
        Assert.Equal("Parent post content", fetched.ReplyTarget.Content);
        Assert.Contains(author.Handle, fetched.ReplyTarget.AuthorHandle);
    }

    [Fact]
    public async Task GetPost_non_reply_has_null_reply_target()
    {
        var session = await fx.RegisterAndLoginAsync();

        using var createReq = fx.AuthorizedRequest(HttpMethod.Post, "/api/posts", session.Token, new { content = "Top-level post" });
        var post = await (await fx.Post.SendAsync(createReq)).Content.ReadFromJsonAsync<PostDto>();

        using var getReq = fx.AuthorizedRequest(HttpMethod.Get, $"/api/posts/{post!.PostId}", session.Token);
        var fetched = await (await fx.Post.SendAsync(getReq)).Content.ReadFromJsonAsync<PostDto>();

        Assert.Null(fetched!.ReplyTarget);
    }

    [Fact]
    public async Task GetPostsByUser_replies_include_reply_target()
    {
        var author = await fx.RegisterAndLoginAsync();
        var replier = await fx.RegisterAndLoginAsync();

        using var createReq = fx.AuthorizedRequest(HttpMethod.Post, "/api/posts", author.Token, new { content = "The parent post" });
        var parent = await (await fx.Post.SendAsync(createReq)).Content.ReadFromJsonAsync<PostDto>();

        using var replyReq = fx.AuthorizedRequest(HttpMethod.Post, $"/api/posts/{parent!.PostId}/replies", replier.Token, new { content = "My reply" });
        var reply = await (await fx.Post.SendAsync(replyReq)).Content.ReadFromJsonAsync<PostDto>();

        using var listReq = fx.AuthorizedRequest(HttpMethod.Get, $"/api/posts/by-user/{replier.UserId}?limit=10&offset=0", replier.Token);
        var posts = await (await fx.Post.SendAsync(listReq)).Content.ReadFromJsonAsync<List<PostDto>>();

        var replyInList = posts!.First(p => p.PostId == reply!.PostId);
        Assert.NotNull(replyInList.ReplyTarget);
        Assert.Equal(parent.PostId, replyInList.ReplyTarget!.PostId);
        Assert.Equal("The parent post", replyInList.ReplyTarget.Content);
    }

    [Fact]
    public async Task GetPost_reply_target_is_null_when_parent_is_deleted()
    {
        var author = await fx.RegisterAndLoginAsync();
        var replier = await fx.RegisterAndLoginAsync();

        using var createReq = fx.AuthorizedRequest(HttpMethod.Post, "/api/posts", author.Token, new { content = "Parent to delete" });
        var parent = await (await fx.Post.SendAsync(createReq)).Content.ReadFromJsonAsync<PostDto>();

        using var replyReq = fx.AuthorizedRequest(HttpMethod.Post, $"/api/posts/{parent!.PostId}/replies", replier.Token, new { content = "A reply" });
        var reply = await (await fx.Post.SendAsync(replyReq)).Content.ReadFromJsonAsync<PostDto>();

        using var deleteReq = fx.AuthorizedRequest(HttpMethod.Delete, $"/api/posts/{parent.PostId}", author.Token);
        await fx.Post.SendAsync(deleteReq);

        using var getReq = fx.AuthorizedRequest(HttpMethod.Get, $"/api/posts/{reply!.PostId}", replier.Token);
        var fetched = await (await fx.Post.SendAsync(getReq)).Content.ReadFromJsonAsync<PostDto>();

        Assert.Null(fetched!.ReplyTarget);
    }

    [Fact]
    public async Task GetPost_with_no_replies_has_empty_recent_replies()
    {
        var session = await fx.RegisterAndLoginAsync();

        using var createReq = fx.AuthorizedRequest(HttpMethod.Post, "/api/posts", session.Token, new { content = "No replies yet" });
        var post = await (await fx.Post.SendAsync(createReq)).Content.ReadFromJsonAsync<PostDto>();

        using var getReq = fx.AuthorizedRequest(HttpMethod.Get, $"/api/posts/{post!.PostId}", session.Token);
        var fetched = await (await fx.Post.SendAsync(getReq)).Content.ReadFromJsonAsync<PostDto>();

        Assert.NotNull(fetched!.RecentReplies);
        Assert.Empty(fetched.RecentReplies!);
    }

    [Fact]
    public async Task GetPost_returns_recent_replies_embedded_in_response()
    {
        var author = await fx.RegisterAndLoginAsync();
        var replier = await fx.RegisterAndLoginAsync();

        using var createReq = fx.AuthorizedRequest(HttpMethod.Post, "/api/posts", author.Token, new { content = "Post with replies" });
        var post = await (await fx.Post.SendAsync(createReq)).Content.ReadFromJsonAsync<PostDto>();

        using var r1 = fx.AuthorizedRequest(HttpMethod.Post, $"/api/posts/{post!.PostId}/replies", replier.Token, new { content = "First reply" });
        using var r2 = fx.AuthorizedRequest(HttpMethod.Post, $"/api/posts/{post.PostId}/replies", replier.Token, new { content = "Second reply" });
        await fx.Post.SendAsync(r1);
        await fx.Post.SendAsync(r2);

        using var getReq = fx.AuthorizedRequest(HttpMethod.Get, $"/api/posts/{post.PostId}", author.Token);
        var fetched = await (await fx.Post.SendAsync(getReq)).Content.ReadFromJsonAsync<PostDto>();

        Assert.NotNull(fetched!.RecentReplies);
        Assert.Equal(2, fetched.RecentReplies!.Count);
        Assert.All(fetched.RecentReplies, r => Assert.Equal(post.PostId, r.ParentPostId));
    }

    [Fact]
    public async Task GetPost_recent_replies_capped_at_3()
    {
        var author = await fx.RegisterAndLoginAsync();
        var replier = await fx.RegisterAndLoginAsync();

        using var createReq = fx.AuthorizedRequest(HttpMethod.Post, "/api/posts", author.Token, new { content = "Post with many replies" });
        var post = await (await fx.Post.SendAsync(createReq)).Content.ReadFromJsonAsync<PostDto>();

        for (var i = 1; i <= 5; i++)
        {
            using var r = fx.AuthorizedRequest(HttpMethod.Post, $"/api/posts/{post!.PostId}/replies", replier.Token, new { content = $"Reply {i}" });
            await fx.Post.SendAsync(r);
        }

        using var getReq = fx.AuthorizedRequest(HttpMethod.Get, $"/api/posts/{post!.PostId}", author.Token);
        var fetched = await (await fx.Post.SendAsync(getReq)).Content.ReadFromJsonAsync<PostDto>();

        Assert.NotNull(fetched!.RecentReplies);
        Assert.Equal(3, fetched.RecentReplies!.Count);
    }

    [Fact]
    public async Task GetPost_recent_replies_are_ordered_newest_first()
    {
        var author = await fx.RegisterAndLoginAsync();
        var replier = await fx.RegisterAndLoginAsync();

        using var createReq = fx.AuthorizedRequest(HttpMethod.Post, "/api/posts", author.Token, new { content = "Post to reply to" });
        var post = await (await fx.Post.SendAsync(createReq)).Content.ReadFromJsonAsync<PostDto>();

        using var r1 = fx.AuthorizedRequest(HttpMethod.Post, $"/api/posts/{post!.PostId}/replies", replier.Token, new { content = "Older reply" });
        using var r2 = fx.AuthorizedRequest(HttpMethod.Post, $"/api/posts/{post.PostId}/replies", replier.Token, new { content = "Newer reply" });
        await fx.Post.SendAsync(r1);
        await fx.Post.SendAsync(r2);

        using var getReq = fx.AuthorizedRequest(HttpMethod.Get, $"/api/posts/{post.PostId}", author.Token);
        var fetched = await (await fx.Post.SendAsync(getReq)).Content.ReadFromJsonAsync<PostDto>();

        Assert.NotNull(fetched!.RecentReplies);
        Assert.Equal(2, fetched.RecentReplies!.Count);
        Assert.True(fetched.RecentReplies[0].PostedAt >= fetched.RecentReplies[1].PostedAt,
            "Replies should be ordered newest first.");
    }

    [Fact]
    public async Task GetPost_recent_replies_each_have_reply_target_pointing_to_current_post()
    {
        var author = await fx.RegisterAndLoginAsync();
        var replier = await fx.RegisterAndLoginAsync();

        using var createReq = fx.AuthorizedRequest(HttpMethod.Post, "/api/posts", author.Token, new { content = "Parent post" });
        var post = await (await fx.Post.SendAsync(createReq)).Content.ReadFromJsonAsync<PostDto>();

        using var replyReq = fx.AuthorizedRequest(HttpMethod.Post, $"/api/posts/{post!.PostId}/replies", replier.Token, new { content = "Reply with target" });
        await fx.Post.SendAsync(replyReq);

        using var getReq = fx.AuthorizedRequest(HttpMethod.Get, $"/api/posts/{post.PostId}", author.Token);
        var fetched = await (await fx.Post.SendAsync(getReq)).Content.ReadFromJsonAsync<PostDto>();

        var embeddedReply = fetched!.RecentReplies!.Single();
        Assert.NotNull(embeddedReply.ReplyTarget);
        Assert.Equal(post.PostId, embeddedReply.ReplyTarget!.PostId);
        Assert.Equal("Parent post", embeddedReply.ReplyTarget.Content);
    }

    [Fact]
    public async Task GetPost_deleted_reply_not_included_in_recent_replies()
    {
        var author = await fx.RegisterAndLoginAsync();
        var replier = await fx.RegisterAndLoginAsync();

        using var createReq = fx.AuthorizedRequest(HttpMethod.Post, "/api/posts", author.Token, new { content = "Post with a deleted reply" });
        var post = await (await fx.Post.SendAsync(createReq)).Content.ReadFromJsonAsync<PostDto>();

        using var keepReq = fx.AuthorizedRequest(HttpMethod.Post, $"/api/posts/{post!.PostId}/replies", replier.Token, new { content = "Kept reply" });
        var kept = await (await fx.Post.SendAsync(keepReq)).Content.ReadFromJsonAsync<PostDto>();

        using var delReq = fx.AuthorizedRequest(HttpMethod.Post, $"/api/posts/{post.PostId}/replies", replier.Token, new { content = "Deleted reply" });
        var toDelete = await (await fx.Post.SendAsync(delReq)).Content.ReadFromJsonAsync<PostDto>();

        using var deleteReq = fx.AuthorizedRequest(HttpMethod.Delete, $"/api/posts/{toDelete!.PostId}", replier.Token);
        await fx.Post.SendAsync(deleteReq);

        using var getReq = fx.AuthorizedRequest(HttpMethod.Get, $"/api/posts/{post.PostId}", author.Token);
        var fetched = await (await fx.Post.SendAsync(getReq)).Content.ReadFromJsonAsync<PostDto>();

        Assert.Contains(fetched!.RecentReplies!, r => r.PostId == kept!.PostId);
        Assert.DoesNotContain(fetched.RecentReplies!, r => r.PostId == toDelete.PostId);
    }

    [Fact]
    public async Task GetPost_reply_has_nested_recent_replies_for_its_own_replies()
    {
        var author = await fx.RegisterAndLoginAsync();
        var replier = await fx.RegisterAndLoginAsync();

        using var rootReq = fx.AuthorizedRequest(HttpMethod.Post, "/api/posts", author.Token, new { content = "Root post" });
        var root = await (await fx.Post.SendAsync(rootReq)).Content.ReadFromJsonAsync<PostDto>();

        using var level1Req = fx.AuthorizedRequest(HttpMethod.Post, $"/api/posts/{root!.PostId}/replies", replier.Token, new { content = "Level 1 reply" });
        var level1 = await (await fx.Post.SendAsync(level1Req)).Content.ReadFromJsonAsync<PostDto>();

        using var level2Req = fx.AuthorizedRequest(HttpMethod.Post, $"/api/posts/{level1!.PostId}/replies", author.Token, new { content = "Level 2 reply" });
        var level2 = await (await fx.Post.SendAsync(level2Req)).Content.ReadFromJsonAsync<PostDto>();

        using var getReq = fx.AuthorizedRequest(HttpMethod.Get, $"/api/posts/{root.PostId}", author.Token);
        var fetched = await (await fx.Post.SendAsync(getReq)).Content.ReadFromJsonAsync<PostDto>();

        var embeddedLevel1 = Assert.Single(fetched!.RecentReplies!, r => r.PostId == level1.PostId);
        Assert.NotNull(embeddedLevel1.RecentReplies);
        var embeddedLevel2 = Assert.Single(embeddedLevel1.RecentReplies!, r => r.PostId == level2!.PostId);
        Assert.NotNull(embeddedLevel2.RecentReplies);
        Assert.Empty(embeddedLevel2.RecentReplies!);
    }

    [Fact]
    public async Task GetPost_conversation_tree_limited_to_depth_three_from_selected_post()
    {
        var author = await fx.RegisterAndLoginAsync();
        var replier = await fx.RegisterAndLoginAsync();

        using var rootReq = fx.AuthorizedRequest(HttpMethod.Post, "/api/posts", author.Token, new { content = "Root" });
        var root = await (await fx.Post.SendAsync(rootReq)).Content.ReadFromJsonAsync<PostDto>();

        using var l1Req = fx.AuthorizedRequest(HttpMethod.Post, $"/api/posts/{root!.PostId}/replies", replier.Token, new { content = "Level 1" });
        var level1 = await (await fx.Post.SendAsync(l1Req)).Content.ReadFromJsonAsync<PostDto>();

        using var l2Req = fx.AuthorizedRequest(HttpMethod.Post, $"/api/posts/{level1!.PostId}/replies", author.Token, new { content = "Level 2" });
        var level2 = await (await fx.Post.SendAsync(l2Req)).Content.ReadFromJsonAsync<PostDto>();

        using var l3Req = fx.AuthorizedRequest(HttpMethod.Post, $"/api/posts/{level2!.PostId}/replies", replier.Token, new { content = "Level 3" });
        var level3 = await (await fx.Post.SendAsync(l3Req)).Content.ReadFromJsonAsync<PostDto>();

        using var l4Req = fx.AuthorizedRequest(HttpMethod.Post, $"/api/posts/{level3!.PostId}/replies", author.Token, new { content = "Level 4 — should not appear" });
        await fx.Post.SendAsync(l4Req);

        using var getReq = fx.AuthorizedRequest(HttpMethod.Get, $"/api/posts/{root.PostId}", author.Token);
        var fetched = await (await fx.Post.SendAsync(getReq)).Content.ReadFromJsonAsync<PostDto>();

        var l1Embedded = Assert.Single(fetched!.RecentReplies!, r => r.PostId == level1.PostId);
        var l2Embedded = Assert.Single(l1Embedded.RecentReplies!, r => r.PostId == level2.PostId);
        var l3Embedded = Assert.Single(l2Embedded.RecentReplies!, r => r.PostId == level3.PostId);
        // Level 3 should have empty RecentReplies (depth limit)
        Assert.True(l3Embedded.RecentReplies is null || l3Embedded.RecentReplies.Count == 0);
    }

    [Fact]
    public async Task CreatePost_plain_text_has_single_segment_with_no_mention_or_hashtag()
    {
        var session = await fx.RegisterAndLoginAsync();
        using var req = fx.AuthorizedRequest(HttpMethod.Post, "/api/posts", session.Token, new { content = "Hello world" });

        var response = await fx.Post.SendAsync(req);

        var post = await response.Content.ReadFromJsonAsync<PostDto>();
        Assert.NotNull(post!.ContentSegments);
        Assert.Single(post.ContentSegments!);
        Assert.Equal("Hello world", post.ContentSegments![0].Text);
        Assert.Null(post.ContentSegments[0].MentionHandle);
        Assert.Null(post.ContentSegments[0].HashtagText);
    }

    [Fact]
    public async Task CreatePost_with_mention_produces_mention_segment()
    {
        var session = await fx.RegisterAndLoginAsync();
        using var req = fx.AuthorizedRequest(HttpMethod.Post, "/api/posts", session.Token, new { content = "Hello @alice how are you" });

        var response = await fx.Post.SendAsync(req);

        var post = await response.Content.ReadFromJsonAsync<PostDto>();
        Assert.NotNull(post!.ContentSegments);
        var mention = post.ContentSegments!.Single(s => s.MentionHandle is not null);
        Assert.Equal("alice", mention.MentionHandle);
        Assert.Equal("@alice", mention.Text);
        Assert.Null(mention.HashtagText);
    }

    [Fact]
    public async Task CreatePost_with_hashtag_produces_hashtag_segment()
    {
        var session = await fx.RegisterAndLoginAsync();
        using var req = fx.AuthorizedRequest(HttpMethod.Post, "/api/posts", session.Token, new { content = "Loving #dotnet today" });

        var response = await fx.Post.SendAsync(req);

        var post = await response.Content.ReadFromJsonAsync<PostDto>();
        Assert.NotNull(post!.ContentSegments);
        var hashtag = post.ContentSegments!.Single(s => s.HashtagText is not null);
        Assert.Equal("dotnet", hashtag.HashtagText);
        Assert.Equal("#dotnet", hashtag.Text);
        Assert.Null(hashtag.MentionHandle);
    }

    [Fact]
    public async Task CreatePost_segments_are_in_order_and_cover_full_content()
    {
        var session = await fx.RegisterAndLoginAsync();
        using var req = fx.AuthorizedRequest(HttpMethod.Post, "/api/posts", session.Token, new { content = "Hi @bob check out #csharp rocks" });

        var response = await fx.Post.SendAsync(req);

        var post = await response.Content.ReadFromJsonAsync<PostDto>();
        var segments = post!.ContentSegments!;
        Assert.Equal("Hi @bob check out #csharp rocks", string.Concat(segments.Select(s => s.Text)));
        Assert.Equal(segments.Select((s, i) => i), segments.Select(s => s.Sequence));
    }

    [Fact]
    public async Task GetPost_returns_content_segments_on_fetched_post()
    {
        var session = await fx.RegisterAndLoginAsync();
        using var createReq = fx.AuthorizedRequest(HttpMethod.Post, "/api/posts", session.Token, new { content = "Hey @world #test" });
        var created = await (await fx.Post.SendAsync(createReq)).Content.ReadFromJsonAsync<PostDto>();

        using var getReq = fx.AuthorizedRequest(HttpMethod.Get, $"/api/posts/{created!.PostId}", session.Token);
        var fetched = await (await fx.Post.SendAsync(getReq)).Content.ReadFromJsonAsync<PostDto>();

        Assert.NotNull(fetched!.ContentSegments);
        Assert.Contains(fetched.ContentSegments!, s => s.MentionHandle == "world");
        Assert.Contains(fetched.ContentSegments!, s => s.HashtagText == "test");
    }

    [Fact]
    public async Task CreatePost_segments_sequence_numbers_start_at_zero_and_are_contiguous()
    {
        var session = await fx.RegisterAndLoginAsync();
        using var req = fx.AuthorizedRequest(HttpMethod.Post, "/api/posts", session.Token, new { content = "one @alice two #dev three" });

        var response = await fx.Post.SendAsync(req);

        var post = await response.Content.ReadFromJsonAsync<PostDto>();
        var seqs = post!.ContentSegments!.Select(s => s.Sequence).ToList();
        Assert.Equal(0, seqs[0]);
        for (int i = 1; i < seqs.Count; i++)
            Assert.Equal(seqs[i - 1] + 1, seqs[i]);
    }

    [Fact]
    public async Task GetPost_LikeCount_is_zero_before_any_likes()
    {
        var session = await fx.RegisterAndLoginAsync();
        using var createReq = fx.AuthorizedRequest(HttpMethod.Post, "/api/posts", session.Token, new { content = "No likes yet" });
        var created = await (await fx.Post.SendAsync(createReq)).Content.ReadFromJsonAsync<PostDto>();

        using var getReq = fx.AuthorizedRequest(HttpMethod.Get, $"/api/posts/{created!.PostId}", session.Token);
        var fetched = await (await fx.Post.SendAsync(getReq)).Content.ReadFromJsonAsync<PostDto>();

        Assert.Equal(0, fetched!.LikeCount);
    }

    [Fact]
    public async Task GetPost_LikeCount_increments_after_LikeAdded_event()
    {
        var session = await fx.RegisterAndLoginAsync();
        using var createReq = fx.AuthorizedRequest(HttpMethod.Post, "/api/posts", session.Token, new { content = "Like me" });
        var created = await (await fx.Post.SendAsync(createReq)).Content.ReadFromJsonAsync<PostDto>();

        var likeResponse = await fx.Post.PostAsJsonAsync("/events/LikeAdded", new
        {
            LikeId = Guid.NewGuid(),
            PostId = created!.PostId,
            UserId = Guid.NewGuid(),
            OccurredAt = DateTimeOffset.UtcNow
        });
        Assert.Equal(HttpStatusCode.Accepted, likeResponse.StatusCode);

        using var getReq = fx.AuthorizedRequest(HttpMethod.Get, $"/api/posts/{created.PostId}", session.Token);
        var fetched = await (await fx.Post.SendAsync(getReq)).Content.ReadFromJsonAsync<PostDto>();

        Assert.Equal(1, fetched!.LikeCount);
    }

    [Fact]
    public async Task GetPost_LikeCount_decrements_after_LikeRemoved_event()
    {
        var session = await fx.RegisterAndLoginAsync();
        using var createReq = fx.AuthorizedRequest(HttpMethod.Post, "/api/posts", session.Token, new { content = "Like and unlike" });
        var created = await (await fx.Post.SendAsync(createReq)).Content.ReadFromJsonAsync<PostDto>();
        var likerId = Guid.NewGuid();

        await fx.Post.PostAsJsonAsync("/events/LikeAdded", new
        {
            LikeId = Guid.NewGuid(),
            PostId = created!.PostId,
            UserId = likerId,
            OccurredAt = DateTimeOffset.UtcNow
        });

        var removeResponse = await fx.Post.PostAsJsonAsync("/events/LikeRemoved", new
        {
            LikeId = Guid.NewGuid(),
            PostId = created.PostId,
            UserId = likerId,
            OccurredAt = DateTimeOffset.UtcNow
        });
        Assert.Equal(HttpStatusCode.Accepted, removeResponse.StatusCode);

        using var getReq = fx.AuthorizedRequest(HttpMethod.Get, $"/api/posts/{created.PostId}", session.Token);
        var fetched = await (await fx.Post.SendAsync(getReq)).Content.ReadFromJsonAsync<PostDto>();

        Assert.Equal(0, fetched!.LikeCount);
    }

    [Fact]
    public async Task GetPost_LikeCount_does_not_go_below_zero()
    {
        var session = await fx.RegisterAndLoginAsync();
        using var createReq = fx.AuthorizedRequest(HttpMethod.Post, "/api/posts", session.Token, new { content = "Already at zero likes" });
        var created = await (await fx.Post.SendAsync(createReq)).Content.ReadFromJsonAsync<PostDto>();

        await fx.Post.PostAsJsonAsync("/events/LikeRemoved", new
        {
            LikeId = Guid.NewGuid(),
            PostId = created!.PostId,
            UserId = Guid.NewGuid(),
            OccurredAt = DateTimeOffset.UtcNow
        });

        using var getReq = fx.AuthorizedRequest(HttpMethod.Get, $"/api/posts/{created.PostId}", session.Token);
        var fetched = await (await fx.Post.SendAsync(getReq)).Content.ReadFromJsonAsync<PostDto>();

        Assert.Equal(0, fetched!.LikeCount);
    }

    [Fact]
    public async Task GetPost_LikeCount_accumulates_from_multiple_users()
    {
        var session = await fx.RegisterAndLoginAsync();
        using var createReq = fx.AuthorizedRequest(HttpMethod.Post, "/api/posts", session.Token, new { content = "Popular post" });
        var created = await (await fx.Post.SendAsync(createReq)).Content.ReadFromJsonAsync<PostDto>();

        await fx.Post.PostAsJsonAsync("/events/LikeAdded", new { LikeId = Guid.NewGuid(), PostId = created!.PostId, UserId = Guid.NewGuid(), OccurredAt = DateTimeOffset.UtcNow });
        await fx.Post.PostAsJsonAsync("/events/LikeAdded", new { LikeId = Guid.NewGuid(), PostId = created.PostId, UserId = Guid.NewGuid(), OccurredAt = DateTimeOffset.UtcNow });
        await fx.Post.PostAsJsonAsync("/events/LikeAdded", new { LikeId = Guid.NewGuid(), PostId = created.PostId, UserId = Guid.NewGuid(), OccurredAt = DateTimeOffset.UtcNow });

        using var getReq = fx.AuthorizedRequest(HttpMethod.Get, $"/api/posts/{created.PostId}", session.Token);
        var fetched = await (await fx.Post.SendAsync(getReq)).Content.ReadFromJsonAsync<PostDto>();

        Assert.Equal(3, fetched!.LikeCount);
    }

    [Fact]
    public async Task GetPost_LikedByMe_is_false_before_user_likes_it()
    {
        var session = await fx.RegisterAndLoginAsync();
        using var createReq = fx.AuthorizedRequest(HttpMethod.Post, "/api/posts", session.Token, new { content = "Not liked yet" });
        var created = await (await fx.Post.SendAsync(createReq)).Content.ReadFromJsonAsync<PostDto>();

        using var getReq = fx.AuthorizedRequest(HttpMethod.Get, $"/api/posts/{created!.PostId}", session.Token);
        var fetched = await (await fx.Post.SendAsync(getReq)).Content.ReadFromJsonAsync<PostDto>();

        Assert.False(fetched!.LikedByMe);
    }

    [Fact]
    public async Task GetPost_LikedByMe_is_true_after_LikeAdded_event()
    {
        var session = await fx.RegisterAndLoginAsync();
        using var createReq = fx.AuthorizedRequest(HttpMethod.Post, "/api/posts", session.Token, new { content = "About to be liked" });
        var created = await (await fx.Post.SendAsync(createReq)).Content.ReadFromJsonAsync<PostDto>();

        await fx.Post.PostAsJsonAsync("/events/LikeAdded", new
        {
            LikeId = Guid.NewGuid(),
            PostId = created!.PostId,
            UserId = session.UserId,
            OccurredAt = DateTimeOffset.UtcNow
        });

        using var getReq = fx.AuthorizedRequest(HttpMethod.Get, $"/api/posts/{created.PostId}", session.Token);
        var fetched = await (await fx.Post.SendAsync(getReq)).Content.ReadFromJsonAsync<PostDto>();

        Assert.True(fetched!.LikedByMe);
    }

    [Fact]
    public async Task GetPost_LikedByMe_is_false_after_LikeRemoved_event()
    {
        var session = await fx.RegisterAndLoginAsync();
        using var createReq = fx.AuthorizedRequest(HttpMethod.Post, "/api/posts", session.Token, new { content = "Like then unlike" });
        var created = await (await fx.Post.SendAsync(createReq)).Content.ReadFromJsonAsync<PostDto>();

        await fx.Post.PostAsJsonAsync("/events/LikeAdded", new { LikeId = Guid.NewGuid(), PostId = created!.PostId, UserId = session.UserId, OccurredAt = DateTimeOffset.UtcNow });
        await fx.Post.PostAsJsonAsync("/events/LikeRemoved", new { LikeId = Guid.NewGuid(), PostId = created.PostId, UserId = session.UserId, OccurredAt = DateTimeOffset.UtcNow });

        using var getReq = fx.AuthorizedRequest(HttpMethod.Get, $"/api/posts/{created.PostId}", session.Token);
        var fetched = await (await fx.Post.SendAsync(getReq)).Content.ReadFromJsonAsync<PostDto>();

        Assert.False(fetched!.LikedByMe);
    }

    [Fact]
    public async Task GetPost_LikedByMe_only_reflects_current_user_not_other_users()
    {
        var alice = await fx.RegisterAndLoginAsync();
        var bob = await fx.RegisterAndLoginAsync();
        using var createReq = fx.AuthorizedRequest(HttpMethod.Post, "/api/posts", alice.Token, new { content = "Shared post" });
        var created = await (await fx.Post.SendAsync(createReq)).Content.ReadFromJsonAsync<PostDto>();

        await fx.Post.PostAsJsonAsync("/events/LikeAdded", new { LikeId = Guid.NewGuid(), PostId = created!.PostId, UserId = alice.UserId, OccurredAt = DateTimeOffset.UtcNow });

        using var aliceReq = fx.AuthorizedRequest(HttpMethod.Get, $"/api/posts/{created.PostId}", alice.Token);
        var aliceFetch = await (await fx.Post.SendAsync(aliceReq)).Content.ReadFromJsonAsync<PostDto>();
        Assert.True(aliceFetch!.LikedByMe);

        using var bobReq = fx.AuthorizedRequest(HttpMethod.Get, $"/api/posts/{created.PostId}", bob.Token);
        var bobFetch = await (await fx.Post.SendAsync(bobReq)).Content.ReadFromJsonAsync<PostDto>();
        Assert.False(bobFetch!.LikedByMe);
    }
}

file sealed record SearchResultsDto(List<PostDto> Posts, string Query, int Limit, int Offset);
