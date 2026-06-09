namespace Integration.Tests;

[Collection(nameof(IntegrationCollection))]
public sealed class SocialApiTests(IntegrationFixture fx)
{
    [Fact]
    public async Task Follow_user_returns_204()
    {
        var follower = await fx.RegisterAndLoginAsync();
        var target = await fx.RegisterAndLoginAsync();

        using var request = fx.AuthorizedRequest(HttpMethod.Post, $"/api/users/{target.UserId}/follows", follower.Token);
        var response = await fx.Social.SendAsync(request);

        Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);
    }

    [Fact]
    public async Task Follow_self_returns_400()
    {
        var session = await fx.RegisterAndLoginAsync();

        using var request = fx.AuthorizedRequest(HttpMethod.Post, $"/api/users/{session.UserId}/follows", session.Token);
        var response = await fx.Social.SendAsync(request);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task Follow_twice_is_idempotent()
    {
        var follower = await fx.RegisterAndLoginAsync();
        var target = await fx.RegisterAndLoginAsync();

        using var req1 = fx.AuthorizedRequest(HttpMethod.Post, $"/api/users/{target.UserId}/follows", follower.Token);
        using var req2 = fx.AuthorizedRequest(HttpMethod.Post, $"/api/users/{target.UserId}/follows", follower.Token);
        await fx.Social.SendAsync(req1);
        var response = await fx.Social.SendAsync(req2);

        Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);
    }

    [Fact]
    public async Task Unfollow_user_returns_204()
    {
        var follower = await fx.RegisterAndLoginAsync();
        var target = await fx.RegisterAndLoginAsync();

        using var follow = fx.AuthorizedRequest(HttpMethod.Post, $"/api/users/{target.UserId}/follows", follower.Token);
        await fx.Social.SendAsync(follow);

        using var unfollow = fx.AuthorizedRequest(HttpMethod.Delete, $"/api/users/{target.UserId}/follows", follower.Token);
        var response = await fx.Social.SendAsync(unfollow);

        Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);
    }

    [Fact]
    public async Task Follow_increments_follower_count()
    {
        var follower = await fx.RegisterAndLoginAsync();
        var target = await fx.RegisterAndLoginAsync();

        var before = await fx.Social.GetFromJsonAsync<FollowCountsDto>($"/api/users/{target.UserId}/counts");

        using var follow = fx.AuthorizedRequest(HttpMethod.Post, $"/api/users/{target.UserId}/follows", follower.Token);
        await fx.Social.SendAsync(follow);

        var after = await fx.Social.GetFromJsonAsync<FollowCountsDto>($"/api/users/{target.UserId}/counts");

        Assert.Equal(before!.FollowerCount + 1, after!.FollowerCount);
    }

    [Fact]
    public async Task Unfollow_without_auth_returns_401()
    {
        var target = await fx.RegisterAndLoginAsync();
        var response = await fx.Social.DeleteAsync($"/api/users/{target.UserId}/follows");

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task HealthCheck_returns_healthy()
    {
        var response = await fx.Social.GetAsync("/health");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var body = await response.Content.ReadAsStringAsync();
        Assert.Equal("Healthy", body);
    }
}
