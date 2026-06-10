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

        using var beforeReq = fx.AuthorizedRequest(HttpMethod.Get, $"/api/users/{target.UserId}/counts", follower.Token);
        var beforeResp = await fx.Social.SendAsync(beforeReq);
        var before = await beforeResp.Content.ReadFromJsonAsync<FollowCountsDto>();

        using var follow = fx.AuthorizedRequest(HttpMethod.Post, $"/api/users/{target.UserId}/follows", follower.Token);
        await fx.Social.SendAsync(follow);

        using var afterReq = fx.AuthorizedRequest(HttpMethod.Get, $"/api/users/{target.UserId}/counts", follower.Token);
        var afterResp = await fx.Social.SendAsync(afterReq);
        var after = await afterResp.Content.ReadFromJsonAsync<FollowCountsDto>();

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
    public async Task GetFollowers_without_auth_returns_401()
    {
        var target = await fx.RegisterAndLoginAsync();
        var response = await fx.Social.GetAsync($"/api/users/{target.UserId}/followers");

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task GetFollowing_without_auth_returns_401()
    {
        var target = await fx.RegisterAndLoginAsync();
        var response = await fx.Social.GetAsync($"/api/users/{target.UserId}/following");

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task GetCounts_without_auth_returns_401()
    {
        var target = await fx.RegisterAndLoginAsync();
        var response = await fx.Social.GetAsync($"/api/users/{target.UserId}/counts");

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task IsFollowing_returns_false_before_following()
    {
        var follower = await fx.RegisterAndLoginAsync();
        var target = await fx.RegisterAndLoginAsync();

        using var req = fx.AuthorizedRequest(HttpMethod.Get, $"/api/users/{follower.UserId}/is-following/{target.UserId}", follower.Token);
        var response = await fx.Social.SendAsync(req);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var result = await response.Content.ReadFromJsonAsync<IsFollowingDto>();
        Assert.False(result!.IsFollowing);
    }

    [Fact]
    public async Task IsFollowing_returns_true_after_following()
    {
        var follower = await fx.RegisterAndLoginAsync();
        var target = await fx.RegisterAndLoginAsync();

        using var follow = fx.AuthorizedRequest(HttpMethod.Post, $"/api/users/{target.UserId}/follows", follower.Token);
        await fx.Social.SendAsync(follow);

        using var req = fx.AuthorizedRequest(HttpMethod.Get, $"/api/users/{follower.UserId}/is-following/{target.UserId}", follower.Token);
        var response = await fx.Social.SendAsync(req);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var result = await response.Content.ReadFromJsonAsync<IsFollowingDto>();
        Assert.True(result!.IsFollowing);
    }

    [Fact]
    public async Task IsFollowing_returns_false_after_unfollowing()
    {
        var follower = await fx.RegisterAndLoginAsync();
        var target = await fx.RegisterAndLoginAsync();

        using var follow = fx.AuthorizedRequest(HttpMethod.Post, $"/api/users/{target.UserId}/follows", follower.Token);
        await fx.Social.SendAsync(follow);

        using var unfollow = fx.AuthorizedRequest(HttpMethod.Delete, $"/api/users/{target.UserId}/follows", follower.Token);
        await fx.Social.SendAsync(unfollow);

        using var req = fx.AuthorizedRequest(HttpMethod.Get, $"/api/users/{follower.UserId}/is-following/{target.UserId}", follower.Token);
        var response = await fx.Social.SendAsync(req);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var result = await response.Content.ReadFromJsonAsync<IsFollowingDto>();
        Assert.False(result!.IsFollowing);
    }

    [Fact]
    public async Task IsFollowing_without_auth_returns_401()
    {
        var response = await fx.Social.GetAsync($"/api/users/{Guid.NewGuid()}/is-following/{Guid.NewGuid()}");

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task IsFollowing_returns_false_for_self()
    {
        var session = await fx.RegisterAndLoginAsync();

        using var req = fx.AuthorizedRequest(HttpMethod.Get, $"/api/users/{session.UserId}/is-following/{session.UserId}", session.Token);
        var response = await fx.Social.SendAsync(req);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var result = await response.Content.ReadFromJsonAsync<IsFollowingDto>();
        Assert.False(result!.IsFollowing);
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

file sealed record IsFollowingDto(bool IsFollowing);
