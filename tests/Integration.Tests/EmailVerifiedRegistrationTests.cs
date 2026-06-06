using MongoDB.Bson;

namespace Integration.Tests;

[Collection(nameof(IntegrationCollection))]
public sealed class EmailVerifiedRegistrationTests(IntegrationFixture fx)
{
    [Fact]
    public async Task StartRegistration_valid_payload_returns_202_and_sends_email()
    {
        var id = Guid.NewGuid().ToString("N")[..10];
        var email = $"{id}@test.com";

        var response = await fx.Identity.PostAsJsonAsync("/api/registrations",
            new { email, password = "Pass123!", handle = id, displayName = $"User {id}" });

        Assert.Equal(HttpStatusCode.Accepted, response.StatusCode);
        var body = await response.Content.ReadFromJsonAsync<PendingRegistrationDto>();
        Assert.NotEqual(Guid.Empty, body!.PendingRegistrationId);

        var emails = await fx.GetDevEmailsAsync();
        Assert.Contains(emails, m => m.To == email && m.Subject == "Verify your registration");
    }

    [Fact]
    public async Task StartRegistration_missing_fields_returns_400()
    {
        var response = await fx.Identity.PostAsJsonAsync("/api/registrations",
            new { email = "", password = "Pass123!", handle = "noemail" });

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task StartRegistration_duplicate_email_in_users_returns_409()
    {
        var id = Guid.NewGuid().ToString("N")[..10];
        var email = $"{id}@test.com";
        await fx.Identity.PostAsJsonAsync("/api/users/register",
            new { email, password = "Pass123!", handle = id, displayName = $"User {id}" });

        var response = await fx.Identity.PostAsJsonAsync("/api/registrations",
            new { email, password = "Pass123!", handle = $"other{id}", displayName = "Other" });

        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
    }

    [Fact]
    public async Task StartRegistration_duplicate_handle_in_users_returns_409()
    {
        var id = Guid.NewGuid().ToString("N")[..10];
        var email = $"{id}@test.com";
        await fx.Identity.PostAsJsonAsync("/api/users/register",
            new { email, password = "Pass123!", handle = id, displayName = $"User {id}" });

        var response = await fx.Identity.PostAsJsonAsync("/api/registrations",
            new { email = $"other{email}", password = "Pass123!", handle = id, displayName = "Other" });

        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
    }

    [Fact]
    public async Task StartRegistration_duplicate_email_in_pending_returns_409()
    {
        var id = Guid.NewGuid().ToString("N")[..10];
        var email = $"{id}@test.com";
        await fx.Identity.PostAsJsonAsync("/api/registrations",
            new { email, password = "Pass123!", handle = id, displayName = $"User {id}" });

        var response = await fx.Identity.PostAsJsonAsync("/api/registrations",
            new { email, password = "Pass123!", handle = $"other{id}", displayName = "Other" });

        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
    }

    [Fact]
    public async Task VerifyRegistration_correct_code_creates_user_and_returns_jwt()
    {
        var id = Guid.NewGuid().ToString("N")[..10];
        var email = $"{id}@test.com";

        var startResponse = await fx.Identity.PostAsJsonAsync("/api/registrations",
            new { email, password = "Pass123!", handle = id, displayName = $"User {id}" });
        Assert.Equal(HttpStatusCode.Accepted, startResponse.StatusCode);
        var pending = await startResponse.Content.ReadFromJsonAsync<PendingRegistrationDto>();

        var emails = await fx.GetDevEmailsAsync();
        var code = ExtractCode(emails.First(m => m.To == email).Body);

        var verifyResponse = await fx.Identity.PostAsJsonAsync("/api/registrations/verify",
            new { pendingRegistrationId = pending!.PendingRegistrationId, code });

        Assert.Equal(HttpStatusCode.Created, verifyResponse.StatusCode);
        var token = await verifyResponse.Content.ReadFromJsonAsync<TokenDto>();
        Assert.NotNull(token);
        Assert.False(string.IsNullOrWhiteSpace(token.Token));
        Assert.NotEqual(Guid.Empty, token.UserId);
    }

    [Fact]
    public async Task VerifyRegistration_correct_code_allows_subsequent_login()
    {
        var id = Guid.NewGuid().ToString("N")[..10];
        var email = $"{id}@test.com";

        var startResponse = await fx.Identity.PostAsJsonAsync("/api/registrations",
            new { email, password = "Pass123!", handle = id, displayName = $"User {id}" });
        var pending = await startResponse.Content.ReadFromJsonAsync<PendingRegistrationDto>();

        var emails = await fx.GetDevEmailsAsync();
        var code = ExtractCode(emails.First(m => m.To == email).Body);

        await fx.Identity.PostAsJsonAsync("/api/registrations/verify",
            new { pendingRegistrationId = pending!.PendingRegistrationId, code });

        var loginResponse = await fx.Identity.PostAsJsonAsync("/api/users/login",
            new { email, password = "Pass123!" });
        Assert.Equal(HttpStatusCode.OK, loginResponse.StatusCode);
    }

    [Fact]
    public async Task VerifyRegistration_wrong_code_returns_400()
    {
        var id = Guid.NewGuid().ToString("N")[..10];
        var email = $"{id}@test.com";

        var startResponse = await fx.Identity.PostAsJsonAsync("/api/registrations",
            new { email, password = "Pass123!", handle = id, displayName = $"User {id}" });
        var pending = await startResponse.Content.ReadFromJsonAsync<PendingRegistrationDto>();

        var verifyResponse = await fx.Identity.PostAsJsonAsync("/api/registrations/verify",
            new { pendingRegistrationId = pending!.PendingRegistrationId, code = "000000" });

        Assert.Equal(HttpStatusCode.BadRequest, verifyResponse.StatusCode);
    }

    [Fact]
    public async Task VerifyRegistration_nonexistent_id_returns_400()
    {
        var verifyResponse = await fx.Identity.PostAsJsonAsync("/api/registrations/verify",
            new { pendingRegistrationId = Guid.NewGuid(), code = "123456" });

        Assert.Equal(HttpStatusCode.BadRequest, verifyResponse.StatusCode);
    }

    [Fact]
    public async Task VerifyRegistration_expired_registration_returns_400()
    {
        var id = Guid.NewGuid().ToString("N")[..10];
        var pendingId = Guid.NewGuid();
        var col = fx.IdentityDb.GetCollection<BsonDocument>("pendingRegistrations");
        await col.InsertOneAsync(new BsonDocument
        {
            { "_id", new BsonBinaryData(pendingId, GuidRepresentation.Standard) },
            { "Handle", $"@{id}" },
            { "Email", $"{id}@test.com" },
            { "DisplayName", $"User {id}" },
            { "PasswordHash", "fakehash" },
            { "VerificationCode", "123456" },
            { "ExpiresAt", new BsonDateTime(DateTime.UtcNow.AddHours(-1)) },
            { "CreatedAt", new BsonDateTime(DateTime.UtcNow.AddHours(-25)) }
        });

        var verifyResponse = await fx.Identity.PostAsJsonAsync("/api/registrations/verify",
            new { pendingRegistrationId = pendingId, code = "123456" });

        Assert.Equal(HttpStatusCode.BadRequest, verifyResponse.StatusCode);
    }

    [Fact]
    public async Task VerifyRegistration_pending_doc_deleted_after_successful_verify()
    {
        var id = Guid.NewGuid().ToString("N")[..10];
        var email = $"{id}@test.com";

        var startResponse = await fx.Identity.PostAsJsonAsync("/api/registrations",
            new { email, password = "Pass123!", handle = id, displayName = $"User {id}" });
        var pending = await startResponse.Content.ReadFromJsonAsync<PendingRegistrationDto>();

        var emails = await fx.GetDevEmailsAsync();
        var code = ExtractCode(emails.First(m => m.To == email).Body);

        await fx.Identity.PostAsJsonAsync("/api/registrations/verify",
            new { pendingRegistrationId = pending!.PendingRegistrationId, code });

        // Second verify attempt with the same (now deleted) pending registration fails
        var secondAttempt = await fx.Identity.PostAsJsonAsync("/api/registrations/verify",
            new { pendingRegistrationId = pending.PendingRegistrationId, code });
        Assert.Equal(HttpStatusCode.BadRequest, secondAttempt.StatusCode);
    }

    [Fact]
    public async Task DirectRegistration_still_works_alongside_verified_flow()
    {
        var id = Guid.NewGuid().ToString("N")[..10];
        var response = await fx.Identity.PostAsJsonAsync("/api/users/register",
            new { email = $"{id}@test.com", password = "Pass123!", handle = id, displayName = $"User {id}" });

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
    }

    private static string ExtractCode(string emailBody)
    {
        const string prefix = "Your verification code is: ";
        var idx = emailBody.IndexOf(prefix, StringComparison.Ordinal);
        return idx >= 0
            ? emailBody[(idx + prefix.Length)..].Trim()
            : throw new InvalidOperationException("Verification code not found in email body.");
    }
}

public sealed record PendingRegistrationDto(Guid PendingRegistrationId);
