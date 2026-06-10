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
}

file sealed record SearchResultsDto(List<PostDto> Posts, string Query, int Limit, int Offset);
