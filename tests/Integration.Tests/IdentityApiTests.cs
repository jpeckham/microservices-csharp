namespace Integration.Tests;

[Collection(nameof(IntegrationCollection))]
public sealed class IdentityApiTests(IntegrationFixture fx)
{
    [Fact]
    public async Task Register_new_user_returns_201()
    {
        var id = Guid.NewGuid().ToString("N")[..10];
        var response = await fx.Identity.PostAsJsonAsync("/api/users/register",
            new { email = $"{id}@test.com", password = "Pass123!", handle = id, displayName = $"User {id}" });

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
    }

    [Fact]
    public async Task Register_duplicate_email_returns_409()
    {
        var id = Guid.NewGuid().ToString("N")[..10];
        var body = new { email = $"{id}@test.com", password = "Pass123!", handle = id, displayName = "Dup" };

        await fx.Identity.PostAsJsonAsync("/api/users/register", body);
        var response = await fx.Identity.PostAsJsonAsync("/api/users/register", body);

        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
    }

    [Fact]
    public async Task Register_missing_fields_returns_400()
    {
        var response = await fx.Identity.PostAsJsonAsync("/api/users/register",
            new { email = "", password = "Pass123!", handle = "noemail" });

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task Login_valid_credentials_returns_token()
    {
        var session = await fx.RegisterAndLoginAsync();

        Assert.False(string.IsNullOrWhiteSpace(session.Token));
        Assert.NotEqual(Guid.Empty, session.UserId);
    }

    [Fact]
    public async Task Login_wrong_password_returns_401()
    {
        var id = Guid.NewGuid().ToString("N")[..10];
        await fx.Identity.PostAsJsonAsync("/api/users/register",
            new { email = $"{id}@test.com", password = "Pass123!", handle = id });

        var response = await fx.Identity.PostAsJsonAsync("/api/users/login",
            new { email = $"{id}@test.com", password = "WrongPassword!" });

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task GetMe_with_valid_token_returns_profile()
    {
        var session = await fx.RegisterAndLoginAsync();

        using var request = fx.AuthorizedRequest(HttpMethod.Get, "/api/users/me", session.Token);
        var response = await fx.Identity.SendAsync(request);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var profile = await response.Content.ReadFromJsonAsync<UserProfileDto>();
        Assert.Equal(session.UserId, profile!.UserId);
    }

    [Fact]
    public async Task GetMe_without_token_returns_401()
    {
        var response = await fx.Identity.GetAsync("/api/users/me");

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task GetByHandle_returns_profile()
    {
        var session = await fx.RegisterAndLoginAsync();

        var response = await fx.Identity.GetAsync($"/api/users/by-handle/{session.Handle}");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var profile = await response.Content.ReadFromJsonAsync<UserProfileDto>();
        Assert.Equal(session.Handle, profile!.Handle.TrimStart('@'));
    }
}
