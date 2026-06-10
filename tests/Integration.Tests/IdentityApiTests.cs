using MongoDB.Bson;

namespace Integration.Tests;

[Collection(nameof(IntegrationCollection))]
public sealed class IdentityApiTests(IntegrationFixture fx)
{
    [Fact]
    public async Task Register_new_user_returns_201_with_token()
    {
        var id = Guid.NewGuid().ToString("N")[..10];
        var response = await fx.Identity.PostAsJsonAsync("/api/users/register",
            new { email = $"{id}@test.com", password = "Pass123!", handle = id, displayName = $"User {id}" });

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        var body = await response.Content.ReadFromJsonAsync<TokenDto>();
        Assert.NotNull(body);
        Assert.False(string.IsNullOrWhiteSpace(body.Token));
        Assert.NotEqual(Guid.Empty, body.UserId);
        Assert.Equal($"@{id}", body.Handle);
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

        using var request = fx.AuthorizedRequest(HttpMethod.Get, $"/api/users/by-handle/{session.Handle}", session.Token);
        var response = await fx.Identity.SendAsync(request);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var profile = await response.Content.ReadFromJsonAsync<UserProfileDto>();
        Assert.Equal(session.Handle, profile!.Handle.TrimStart('@'));
    }

    [Fact]
    public async Task GetByHandle_without_auth_returns_401()
    {
        var session = await fx.RegisterAndLoginAsync();

        var response = await fx.Identity.GetAsync($"/api/users/by-handle/{session.Handle}");

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task GetById_without_auth_returns_401()
    {
        var response = await fx.Identity.GetAsync($"/api/users/{Guid.NewGuid()}");

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task GetByHandle_is_followed_by_me_false_before_following()
    {
        var viewer = await fx.RegisterAndLoginAsync();
        var target = await fx.RegisterAndLoginAsync();

        using var req = fx.AuthorizedRequest(HttpMethod.Get, $"/api/users/by-handle/{target.Handle}", viewer.Token);
        var profile = await (await fx.Identity.SendAsync(req)).Content.ReadFromJsonAsync<UserProfileDto>();

        Assert.False(profile!.IsFollowedByMe);
    }

    [Fact]
    public async Task GetByHandle_is_followed_by_me_true_after_following()
    {
        var viewer = await fx.RegisterAndLoginAsync();
        var target = await fx.RegisterAndLoginAsync();

        using var follow = fx.AuthorizedRequest(HttpMethod.Post, $"/api/users/{target.UserId}/follows", viewer.Token);
        await fx.Social.SendAsync(follow);

        using var req = fx.AuthorizedRequest(HttpMethod.Get, $"/api/users/by-handle/{target.Handle}", viewer.Token);
        var profile = await (await fx.Identity.SendAsync(req)).Content.ReadFromJsonAsync<UserProfileDto>();

        Assert.True(profile!.IsFollowedByMe);
    }

    [Fact]
    public async Task GetByHandle_follower_count_increments_after_follow()
    {
        var follower = await fx.RegisterAndLoginAsync();
        var target = await fx.RegisterAndLoginAsync();

        using var follow = fx.AuthorizedRequest(HttpMethod.Post, $"/api/users/{target.UserId}/follows", follower.Token);
        await fx.Social.SendAsync(follow);

        using var req = fx.AuthorizedRequest(HttpMethod.Get, $"/api/users/by-handle/{target.Handle}", follower.Token);
        var profile = await (await fx.Identity.SendAsync(req)).Content.ReadFromJsonAsync<UserProfileDto>();

        Assert.Equal(1, profile!.FollowerCount);
    }

    [Fact]
    public async Task GetByHandle_own_profile_is_followed_by_me_false()
    {
        var session = await fx.RegisterAndLoginAsync();

        using var req = fx.AuthorizedRequest(HttpMethod.Get, $"/api/users/by-handle/{session.Handle}", session.Token);
        var profile = await (await fx.Identity.SendAsync(req)).Content.ReadFromJsonAsync<UserProfileDto>();

        Assert.True(profile!.IsOwnProfile);
        Assert.False(profile.IsFollowedByMe);
    }

    // --- Password reset ---

    [Fact]
    public async Task RequestReset_with_registered_email_returns_204_and_sends_email()
    {
        var id = Guid.NewGuid().ToString("N")[..10];
        var email = $"{id}@test.com";
        await fx.Identity.PostAsJsonAsync("/api/users/register",
            new { email, password = "Pass123!", handle = id, displayName = $"User {id}" });

        var response = await fx.Identity.PostAsJsonAsync("/api/password-reset-requests", new { email });

        Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);
        var emails = await fx.GetDevEmailsAsync();
        Assert.Contains(emails, m => m.To == email && m.Body.Contains("?token="));
    }

    [Fact]
    public async Task RequestReset_with_unknown_email_returns_204()
    {
        var response = await fx.Identity.PostAsJsonAsync("/api/password-reset-requests",
            new { email = "nobody@nowhere.com" });

        Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);
    }

    [Fact]
    public async Task RequestReset_email_body_mentions_5_minute_expiry()
    {
        var id = Guid.NewGuid().ToString("N")[..10];
        var email = $"{id}@test.com";
        await fx.Identity.PostAsJsonAsync("/api/users/register",
            new { email, password = "Pass123!", handle = id, displayName = $"User {id}" });

        await fx.Identity.PostAsJsonAsync("/api/password-reset-requests", new { email });

        var emails = await fx.GetDevEmailsAsync();
        var resetEmail = emails.First(m => m.To == email && m.Body.Contains("?token="));
        Assert.Contains("5 minutes", resetEmail.Body);
    }

    [Fact]
    public async Task ResetPassword_with_valid_token_updates_password_and_allows_login()
    {
        var id = Guid.NewGuid().ToString("N")[..10];
        var email = $"{id}@test.com";
        await fx.Identity.PostAsJsonAsync("/api/users/register",
            new { email, password = "OldPass123!", handle = id, displayName = $"User {id}" });
        await fx.Identity.PostAsJsonAsync("/api/password-reset-requests", new { email });

        var emails = await fx.GetDevEmailsAsync();
        var token = ExtractToken(emails.First(m => m.To == email).Body);

        var resetResponse = await fx.Identity.PostAsJsonAsync("/api/password-resets",
            new { token, newPassword = "NewPass456!" });
        Assert.Equal(HttpStatusCode.NoContent, resetResponse.StatusCode);

        var loginResponse = await fx.Identity.PostAsJsonAsync("/api/users/login",
            new { email, password = "NewPass456!" });
        Assert.Equal(HttpStatusCode.OK, loginResponse.StatusCode);
    }

    [Fact]
    public async Task ResetPassword_token_cannot_be_reused()
    {
        var id = Guid.NewGuid().ToString("N")[..10];
        var email = $"{id}@test.com";
        await fx.Identity.PostAsJsonAsync("/api/users/register",
            new { email, password = "Pass123!", handle = id, displayName = $"User {id}" });
        await fx.Identity.PostAsJsonAsync("/api/password-reset-requests", new { email });

        var emails = await fx.GetDevEmailsAsync();
        var token = ExtractToken(emails.First(m => m.To == email).Body);

        await fx.Identity.PostAsJsonAsync("/api/password-resets", new { token, newPassword = "NewPass456!" });
        var second = await fx.Identity.PostAsJsonAsync("/api/password-resets", new { token, newPassword = "AnotherPass789!" });

        Assert.Equal(HttpStatusCode.BadRequest, second.StatusCode);
    }

    [Fact]
    public async Task ResetPassword_with_invalid_token_returns_400()
    {
        var response = await fx.Identity.PostAsJsonAsync("/api/password-resets",
            new { token = "doesnotexist", newPassword = "NewPass123!" });

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task ResetPassword_with_weak_password_returns_400()
    {
        var id = Guid.NewGuid().ToString("N")[..10];
        var email = $"{id}@test.com";
        await fx.Identity.PostAsJsonAsync("/api/users/register",
            new { email, password = "Pass123!", handle = id, displayName = $"User {id}" });
        await fx.Identity.PostAsJsonAsync("/api/password-reset-requests", new { email });

        var emails = await fx.GetDevEmailsAsync();
        var token = ExtractToken(emails.First(m => m.To == email).Body);

        var noDigit = await fx.Identity.PostAsJsonAsync("/api/password-resets",
            new { token, newPassword = "NoDigitsHere" });
        Assert.Equal(HttpStatusCode.BadRequest, noDigit.StatusCode);

        var tooShort = await fx.Identity.PostAsJsonAsync("/api/password-resets",
            new { token, newPassword = "Ab1!" });
        Assert.Equal(HttpStatusCode.BadRequest, tooShort.StatusCode);
    }

    [Fact]
    public async Task ResetPassword_with_expired_token_returns_400()
    {
        var token = Guid.NewGuid().ToString("N");
        var col = fx.IdentityDb.GetCollection<BsonDocument>("passwordResetTokens");
        await col.InsertOneAsync(new BsonDocument
        {
            { "_id", new BsonBinaryData(Guid.NewGuid(), GuidRepresentation.Standard) },
            { "UserId", new BsonBinaryData(Guid.NewGuid(), GuidRepresentation.Standard) },
            { "Token", token },
            { "ExpiresAt", new BsonDateTime(DateTime.UtcNow.AddMinutes(-1)) },
            { "Consumed", false }
        });

        var response = await fx.Identity.PostAsJsonAsync("/api/password-resets",
            new { token, newPassword = "NewPass123!" });

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    private static string ExtractToken(string emailBody)
    {
        var idx = emailBody.IndexOf("?token=", StringComparison.Ordinal);
        return idx >= 0 ? emailBody[(idx + 7)..] : throw new InvalidOperationException("Token not found in email body.");
    }

    private static string ExtractVerificationCode(string emailBody)
    {
        var prefix = "Your verification code is: ";
        var idx = emailBody.IndexOf(prefix, StringComparison.Ordinal);
        return idx >= 0 ? emailBody[(idx + prefix.Length)..].Trim() : throw new InvalidOperationException("Verification code not found in email body.");
    }

    [Fact]
    public async Task HealthCheck_returns_healthy()
    {
        var response = await fx.Identity.GetAsync("/health");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var body = await response.Content.ReadAsStringAsync();
        Assert.Equal("Healthy", body);
    }

    [Fact]
    public async Task ChangePassword_without_auth_returns_401()
    {
        var response = await fx.Identity.PutAsJsonAsync("/api/users/me/password",
            new { currentPassword = "Pass123!", newPassword = "NewPass1!" });

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task ChangePassword_with_valid_credentials_returns_204()
    {
        var session = await fx.RegisterAndLoginAsync();
        using var request = fx.AuthorizedRequest(HttpMethod.Put, "/api/users/me/password", session.Token,
            new { currentPassword = "Pass123!", newPassword = "NewPass1!" });

        var response = await fx.Identity.SendAsync(request);

        Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);
    }

    [Fact]
    public async Task ChangePassword_allows_login_with_new_password()
    {
        var session = await fx.RegisterAndLoginAsync();
        using var changeReq = fx.AuthorizedRequest(HttpMethod.Put, "/api/users/me/password", session.Token,
            new { currentPassword = "Pass123!", newPassword = "Changed9!" });
        (await fx.Identity.SendAsync(changeReq)).EnsureSuccessStatusCode();

        var login = await fx.Identity.PostAsJsonAsync("/api/users/login",
            new { email = $"{session.Handle}@test.com", password = "Changed9!" });

        Assert.Equal(HttpStatusCode.OK, login.StatusCode);
    }

    [Fact]
    public async Task ChangePassword_wrong_current_password_returns_400()
    {
        var session = await fx.RegisterAndLoginAsync();
        using var request = fx.AuthorizedRequest(HttpMethod.Put, "/api/users/me/password", session.Token,
            new { currentPassword = "WrongPass1!", newPassword = "NewPass1!" });

        var response = await fx.Identity.SendAsync(request);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task ChangePassword_weak_new_password_returns_400()
    {
        var session = await fx.RegisterAndLoginAsync();
        using var request = fx.AuthorizedRequest(HttpMethod.Put, "/api/users/me/password", session.Token,
            new { currentPassword = "Pass123!", newPassword = "short" });

        var response = await fx.Identity.SendAsync(request);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task ChangePassword_new_password_without_digit_returns_400()
    {
        var session = await fx.RegisterAndLoginAsync();
        using var request = fx.AuthorizedRequest(HttpMethod.Put, "/api/users/me/password", session.Token,
            new { currentPassword = "Pass123!", newPassword = "NoDigitsHere!" });

        var response = await fx.Identity.SendAsync(request);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task Register_with_short_password_returns_400()
    {
        var id = Guid.NewGuid().ToString("N")[..10];
        var response = await fx.Identity.PostAsJsonAsync("/api/users/register",
            new { email = $"{id}@test.com", password = "short1", handle = id, displayName = id });

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task Register_with_password_without_digit_returns_400()
    {
        var id = Guid.NewGuid().ToString("N")[..10];
        var response = await fx.Identity.PostAsJsonAsync("/api/users/register",
            new { email = $"{id}@test.com", password = "NoDigitsHere!", handle = id, displayName = id });

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task StartRegistration_with_short_password_returns_400()
    {
        var id = Guid.NewGuid().ToString("N")[..10];
        var response = await fx.Identity.PostAsJsonAsync("/api/registrations",
            new { email = $"{id}@test.com", password = "short1", handle = id, displayName = id });

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task StartRegistration_with_password_without_digit_returns_400()
    {
        var id = Guid.NewGuid().ToString("N")[..10];
        var response = await fx.Identity.PostAsJsonAsync("/api/registrations",
            new { email = $"{id}@test.com", password = "NoDigitsAtAll!", handle = id, displayName = id });

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task Register_with_handle_containing_spaces_returns_400()
    {
        var id = Guid.NewGuid().ToString("N")[..10];
        var response = await fx.Identity.PostAsJsonAsync("/api/users/register",
            new { email = $"{id}@test.com", password = "Pass123!", handle = "hello world", displayName = id });

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task Register_with_handle_containing_special_chars_returns_400()
    {
        var id = Guid.NewGuid().ToString("N")[..10];
        var response = await fx.Identity.PostAsJsonAsync("/api/users/register",
            new { email = $"{id}@test.com", password = "Pass123!", handle = "bad-handle!", displayName = id });

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task StartRegistration_with_handle_containing_spaces_returns_400()
    {
        var id = Guid.NewGuid().ToString("N")[..10];
        var response = await fx.Identity.PostAsJsonAsync("/api/registrations",
            new { email = $"{id}@test.com", password = "Pass123!", handle = "bad handle", displayName = id });

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task StartRegistration_with_handle_containing_special_chars_returns_400()
    {
        var id = Guid.NewGuid().ToString("N")[..10];
        var response = await fx.Identity.PostAsJsonAsync("/api/registrations",
            new { email = $"{id}@test.com", password = "Pass123!", handle = "bad@handle#", displayName = id });

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task Register_with_invalid_email_format_returns_400()
    {
        var id = Guid.NewGuid().ToString("N")[..10];
        var response = await fx.Identity.PostAsJsonAsync("/api/users/register",
            new { email = "notanemail", password = "Pass123!", handle = id, displayName = id });

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task Register_with_email_missing_domain_returns_400()
    {
        var id = Guid.NewGuid().ToString("N")[..10];
        var response = await fx.Identity.PostAsJsonAsync("/api/users/register",
            new { email = "user@", password = "Pass123!", handle = id, displayName = id });

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task StartRegistration_with_invalid_email_format_returns_400()
    {
        var id = Guid.NewGuid().ToString("N")[..10];
        var response = await fx.Identity.PostAsJsonAsync("/api/registrations",
            new { email = "notanemail", password = "Pass123!", handle = id, displayName = id });

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task StartRegistration_with_email_missing_domain_returns_400()
    {
        var id = Guid.NewGuid().ToString("N")[..10];
        var response = await fx.Identity.PostAsJsonAsync("/api/registrations",
            new { email = "missing@", password = "Pass123!", handle = id, displayName = id });

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task Register_with_display_name_over_50_chars_returns_400()
    {
        var id = Guid.NewGuid().ToString("N")[..10];
        var longName = new string('A', 51);
        var response = await fx.Identity.PostAsJsonAsync("/api/users/register",
            new { email = $"{id}@test.com", password = "Pass123!", handle = id, displayName = longName });

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task StartRegistration_with_display_name_over_50_chars_returns_400()
    {
        var id = Guid.NewGuid().ToString("N")[..10];
        var longName = new string('B', 51);
        var response = await fx.Identity.PostAsJsonAsync("/api/registrations",
            new { email = $"{id}@test.com", password = "Pass123!", handle = id, displayName = longName });

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task UpdateDisplayName_over_50_chars_returns_400()
    {
        var session = await fx.RegisterAndLoginAsync();
        var longName = new string('C', 51);
        using var request = fx.AuthorizedRequest(HttpMethod.Put, "/api/users/me/display-name", session.Token,
            new { displayName = longName });

        var response = await fx.Identity.SendAsync(request);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task UpdateDisplayName_exactly_50_chars_returns_204()
    {
        var session = await fx.RegisterAndLoginAsync();
        var maxName = new string('D', 50);
        using var request = fx.AuthorizedRequest(HttpMethod.Put, "/api/users/me/display-name", session.Token,
            new { displayName = maxName });

        var response = await fx.Identity.SendAsync(request);

        Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);
    }

    [Fact]
    public async Task VerifyRegistration_with_code_with_spaces_succeeds()
    {
        var id = Guid.NewGuid().ToString("N")[..10];
        var email = $"{id}@test.com";
        var startResp = await fx.Identity.PostAsJsonAsync("/api/registrations",
            new { email, password = "Pass123!", handle = id, displayName = id });
        Assert.Equal(HttpStatusCode.Accepted, startResp.StatusCode);
        var body = await startResp.Content.ReadFromJsonAsync<PendingRegistrationDto>();

        var emails = await fx.GetDevEmailsAsync();
        var code = ExtractVerificationCode(emails.First(m => m.To == email).Body);
        var spacedCode = string.Join(" ", code.ToCharArray());

        var verifyResp = await fx.Identity.PostAsJsonAsync("/api/registrations/verify",
            new { pendingRegistrationId = body!.PendingRegistrationId, code = spacedCode });

        Assert.Equal(HttpStatusCode.Created, verifyResp.StatusCode);
    }

    [Fact]
    public async Task VerifyRegistration_with_hyphenated_code_succeeds()
    {
        var id = Guid.NewGuid().ToString("N")[..10];
        var email = $"{id}@test.com";
        var startResp = await fx.Identity.PostAsJsonAsync("/api/registrations",
            new { email, password = "Pass123!", handle = id, displayName = id });
        Assert.Equal(HttpStatusCode.Accepted, startResp.StatusCode);
        var body = await startResp.Content.ReadFromJsonAsync<PendingRegistrationDto>();

        var emails = await fx.GetDevEmailsAsync();
        var code = ExtractVerificationCode(emails.First(m => m.To == email).Body);
        var hyphenatedCode = $"{code[..3]}-{code[3..]}";

        var verifyResp = await fx.Identity.PostAsJsonAsync("/api/registrations/verify",
            new { pendingRegistrationId = body!.PendingRegistrationId, code = hyphenatedCode });

        Assert.Equal(HttpStatusCode.Created, verifyResp.StatusCode);
    }

    [Fact]
    public async Task Register_without_display_name_returns_400()
    {
        var id = Guid.NewGuid().ToString("N")[..10];
        var response = await fx.Identity.PostAsJsonAsync("/api/users/register",
            new { email = $"{id}@test.com", password = "Pass123!", handle = id });

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task Register_with_empty_display_name_returns_400()
    {
        var id = Guid.NewGuid().ToString("N")[..10];
        var response = await fx.Identity.PostAsJsonAsync("/api/users/register",
            new { email = $"{id}@test.com", password = "Pass123!", handle = id, displayName = "" });

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task StartRegistration_without_display_name_returns_400()
    {
        var id = Guid.NewGuid().ToString("N")[..10];
        var response = await fx.Identity.PostAsJsonAsync("/api/registrations",
            new { email = $"{id}@test.com", password = "Pass123!", handle = id });

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task StartRegistration_with_empty_display_name_returns_400()
    {
        var id = Guid.NewGuid().ToString("N")[..10];
        var response = await fx.Identity.PostAsJsonAsync("/api/registrations",
            new { email = $"{id}@test.com", password = "Pass123!", handle = id, displayName = "   " });

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task VerifyRegistration_with_wrong_code_returns_400()
    {
        var id = Guid.NewGuid().ToString("N")[..10];
        var email = $"{id}@test.com";
        var startResp = await fx.Identity.PostAsJsonAsync("/api/registrations",
            new { email, password = "Pass123!", handle = id, displayName = id });
        var body = await startResp.Content.ReadFromJsonAsync<PendingRegistrationDto>();

        var verifyResp = await fx.Identity.PostAsJsonAsync("/api/registrations/verify",
            new { pendingRegistrationId = body!.PendingRegistrationId, code = "000000" });

        Assert.Equal(HttpStatusCode.BadRequest, verifyResp.StatusCode);
    }
    [Fact]
    public async Task UserSearch_without_auth_returns_401()
    {
        var response = await fx.Identity.GetAsync("/api/users/search?q=test");

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task UserSearch_with_auth_returns_results()
    {
        var session = await fx.RegisterAndLoginAsync();

        using var request = fx.AuthorizedRequest(HttpMethod.Get, $"/api/users/search?q={session.Handle}&limit=20&offset=0", session.Token);
        var response = await fx.Identity.SendAsync(request);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var results = await response.Content.ReadFromJsonAsync<List<UserSearchResultDto>>();
        Assert.Contains(results!, r => r.Handle == $"@{session.Handle}");
    }
}

file sealed record PendingRegistrationDto(Guid PendingRegistrationId);
file sealed record UserSearchResultDto(Guid UserId, string Handle, string DisplayName);
