using Microsoft.Playwright;

namespace Playwright.Tests;

// Requires the docker-compose stack to be running:
//   docker compose up -d
// After building, install browsers once:
//   pwsh tests/Playwright.Tests/bin/Debug/net8.0/playwright.ps1 install chromium
public sealed class LoginTests : IAsyncLifetime
{
    private static readonly string BffBaseUrl = "http://localhost:32123";
    private static readonly string IdentityApiBaseUrl = "http://localhost:5101";

    private IPlaywright? _playwright;
    private IBrowser? _browser;

    public async Task InitializeAsync()
    {
        _playwright = await Microsoft.Playwright.Playwright.CreateAsync();
        _browser = await _playwright.Chromium.LaunchAsync(new BrowserTypeLaunchOptions
        {
            Headless = true
        });
    }

    public async Task DisposeAsync()
    {
        if (_browser is not null) await _browser.DisposeAsync();
        _playwright?.Dispose();
    }

    [Fact]
    public async Task Login_with_valid_credentials_sets_session_and_shows_handle_on_home_page()
    {
        // Create a unique user directly via the Identity.Api register endpoint.
        // This adds the user to the `users` collection without email verification,
        // so the login endpoint can find them immediately.
        var suffix = Guid.NewGuid().ToString("N")[..8];
        var email = $"pw-{suffix}@test.local";
        var password = "Password1";
        var handle = $"pw{suffix}";
        using var http = new HttpClient();
        var reg = await http.PostAsJsonAsync($"{IdentityApiBaseUrl}/api/users/register",
            new { email, password, Handle = handle, DisplayName = "Playwright User" });
        Assert.True(reg.IsSuccessStatusCode,
            $"Register pre-condition failed ({(int)reg.StatusCode}): {await reg.Content.ReadAsStringAsync()}");

        var page = await _browser!.NewPageAsync();
        await page.GotoAsync($"{BffBaseUrl}/login");

        await page.FillAsync("input[type=email]", email);
        await page.FillAsync("input[type=password]", password);
        await page.ClickAsync("button:has-text('Log in')");

        // Force-load navigation happens after login; wait for the home page to stabilise.
        await page.WaitForURLAsync($"{BffBaseUrl}/");
        await page.WaitForLoadStateAsync(LoadState.NetworkIdle);

        // After AppSession.InitAsync() the home page renders "Signed in as @<handle>"
        var signedInText = await page.Locator($"text=Signed in as @{handle}").IsVisibleAsync();
        Assert.True(signedInText, $"Expected 'Signed in as @{handle}' to be visible after login.");
    }

    [Fact]
    public async Task Login_with_wrong_password_shows_error_and_stays_on_login_page()
    {
        var page = await _browser!.NewPageAsync();
        await page.GotoAsync($"{BffBaseUrl}/login");

        await page.FillAsync("input[type=email]", "nobody@test.local");
        await page.FillAsync("input[type=password]", "WrongPass1");
        await page.ClickAsync("button:has-text('Log in')");

        // No navigation; an error message appears
        await page.WaitForSelectorAsync(".field-message");
        var errorText = await page.TextContentAsync(".field-message");
        Assert.False(string.IsNullOrWhiteSpace(errorText));
        Assert.Contains("/login", page.Url);
    }
}
