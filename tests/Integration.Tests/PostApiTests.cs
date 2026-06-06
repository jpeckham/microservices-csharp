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
    public async Task GetPost_returns_post()
    {
        var session = await fx.RegisterAndLoginAsync();
        using var create = fx.AuthorizedRequest(HttpMethod.Post, "/api/posts", session.Token, new { content = "Fetch me" });
        var created = await (await fx.Post.SendAsync(create)).Content.ReadFromJsonAsync<PostDto>();

        var response = await fx.Post.GetAsync($"/api/posts/{created!.PostId}");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var fetched = await response.Content.ReadFromJsonAsync<PostDto>();
        Assert.Equal(created.PostId, fetched!.PostId);
    }

    [Fact]
    public async Task GetPost_unknown_id_returns_404()
    {
        var response = await fx.Post.GetAsync($"/api/posts/{Guid.NewGuid()}");

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

        var getResp = await fx.Post.GetAsync($"/api/posts/{post.PostId}");
        Assert.Equal(HttpStatusCode.NotFound, getResp.StatusCode);
    }

    [Fact]
    public async Task UpdatePost_by_non_author_returns_404()
    {
        var author = await fx.RegisterAndLoginAsync();
        var other = await fx.RegisterAndLoginAsync();

        using var create = fx.AuthorizedRequest(HttpMethod.Post, "/api/posts", author.Token, new { content = "Mine" });
        var post = await (await fx.Post.SendAsync(create)).Content.ReadFromJsonAsync<PostDto>();

        using var update = fx.AuthorizedRequest(HttpMethod.Put, $"/api/posts/{post!.PostId}", other.Token, new { content = "Stolen" });
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

        var response = await fx.Post.GetAsync($"/api/posts/search?q={unique}&limit=10&offset=0");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var results = await response.Content.ReadFromJsonAsync<SearchResultsDto>();
        Assert.NotEmpty(results!.Posts);
        Assert.Contains(results.Posts, p => p.Content.Contains(unique));
    }
}

file sealed record SearchResultsDto(List<PostDto> Posts, string Query, int Limit, int Offset);
