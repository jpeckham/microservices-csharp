namespace Integration.Tests;

[Collection(nameof(IntegrationCollection))]
public sealed class EngagementApiTests(IntegrationFixture fx)
{
    [Fact]
    public async Task LikePost_without_auth_returns_401()
    {
        var response = await fx.Engagement.PostAsJsonAsync($"/api/posts/{Guid.NewGuid()}/likes", new { });

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task LikePost_returns_like_count()
    {
        var session = await fx.RegisterAndLoginAsync();
        var postId = Guid.NewGuid();

        using var request = fx.AuthorizedRequest(HttpMethod.Post, $"/api/posts/{postId}/likes", session.Token);
        var response = await fx.Engagement.SendAsync(request);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var body = await response.Content.ReadFromJsonAsync<LikeCountDto>();
        Assert.Equal(1, body!.LikeCount);
    }

    [Fact]
    public async Task LikePost_twice_is_idempotent()
    {
        var session = await fx.RegisterAndLoginAsync();
        var postId = Guid.NewGuid();

        using var req1 = fx.AuthorizedRequest(HttpMethod.Post, $"/api/posts/{postId}/likes", session.Token);
        using var req2 = fx.AuthorizedRequest(HttpMethod.Post, $"/api/posts/{postId}/likes", session.Token);
        await fx.Engagement.SendAsync(req1);
        var response = await fx.Engagement.SendAsync(req2);

        var body = await response.Content.ReadFromJsonAsync<LikeCountDto>();
        Assert.Equal(1, body!.LikeCount);
    }

    [Fact]
    public async Task UnlikePost_decrements_count()
    {
        var session = await fx.RegisterAndLoginAsync();
        var postId = Guid.NewGuid();

        using var like = fx.AuthorizedRequest(HttpMethod.Post, $"/api/posts/{postId}/likes", session.Token);
        await fx.Engagement.SendAsync(like);

        using var unlike = fx.AuthorizedRequest(HttpMethod.Delete, $"/api/posts/{postId}/likes", session.Token);
        var response = await fx.Engagement.SendAsync(unlike);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var body = await response.Content.ReadFromJsonAsync<LikeCountDto>();
        Assert.Equal(0, body!.LikeCount);
    }

    [Fact]
    public async Task AddComment_returns_201_with_comment()
    {
        var session = await fx.RegisterAndLoginAsync();
        var postId = Guid.NewGuid();

        using var request = fx.AuthorizedRequest(HttpMethod.Post, $"/api/posts/{postId}/comments", session.Token, new { content = "Great post!" });
        var response = await fx.Engagement.SendAsync(request);

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        var comment = await response.Content.ReadFromJsonAsync<CommentDto>();
        Assert.Equal("Great post!", comment!.Content);
        Assert.Equal(postId, comment.PostId);
    }

    [Fact]
    public async Task AddComment_empty_content_returns_400()
    {
        var session = await fx.RegisterAndLoginAsync();

        using var request = fx.AuthorizedRequest(HttpMethod.Post, $"/api/posts/{Guid.NewGuid()}/comments", session.Token, new { content = "" });
        var response = await fx.Engagement.SendAsync(request);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task GetComments_returns_list()
    {
        var session = await fx.RegisterAndLoginAsync();
        var postId = Guid.NewGuid();

        using var c1 = fx.AuthorizedRequest(HttpMethod.Post, $"/api/posts/{postId}/comments", session.Token, new { content = "First" });
        using var c2 = fx.AuthorizedRequest(HttpMethod.Post, $"/api/posts/{postId}/comments", session.Token, new { content = "Second" });
        await fx.Engagement.SendAsync(c1);
        await fx.Engagement.SendAsync(c2);

        var response = await fx.Engagement.GetAsync($"/api/posts/{postId}/comments");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var comments = await response.Content.ReadFromJsonAsync<List<CommentDto>>();
        Assert.Equal(2, comments!.Count);
    }

    [Fact]
    public async Task GetEngagementSummary_returns_counts()
    {
        var session = await fx.RegisterAndLoginAsync();
        var postId = Guid.NewGuid();

        using var like = fx.AuthorizedRequest(HttpMethod.Post, $"/api/posts/{postId}/likes", session.Token);
        using var comment = fx.AuthorizedRequest(HttpMethod.Post, $"/api/posts/{postId}/comments", session.Token, new { content = "Nice" });
        await fx.Engagement.SendAsync(like);
        await fx.Engagement.SendAsync(comment);

        var response = await fx.Engagement.GetAsync($"/api/posts/{postId}/summary");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var summary = await response.Content.ReadFromJsonAsync<EngagementSummaryDto>();
        Assert.Equal(1, summary!.LikeCount);
        Assert.Equal(1, summary.CommentCount);
    }
}

file sealed record LikeCountDto(int LikeCount);
file sealed record EngagementSummaryDto(int LikeCount, int CommentCount, bool LikedByMe);
