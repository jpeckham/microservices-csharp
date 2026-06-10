using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using Yarp.ReverseProxy.Transforms;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddReverseProxy()
    .LoadFromConfig(builder.Configuration.GetSection("ReverseProxy"))
    .AddTransforms(ctx =>
    {
        ctx.AddRequestTransform(requestContext =>
        {
            var token = requestContext.HttpContext.Request.Cookies["bff_token"];
            if (!string.IsNullOrEmpty(token))
            {
                requestContext.ProxyRequest.Headers.Authorization =
                    new AuthenticationHeaderValue("Bearer", token);
            }
            return ValueTask.CompletedTask;
        });
    });

builder.Services.AddHttpClient("identity", client =>
    client.BaseAddress = new Uri(builder.Configuration["Services:Identity"] ?? "http://localhost:5101/"));

var app = builder.Build();

if (!app.Environment.IsDevelopment())
{
    app.UseHsts();
    app.UseHttpsRedirection();
}

app.UseBlazorFrameworkFiles();
app.UseStaticFiles();

app.MapPost("/bff/login", async (LoginRequest req, IHttpClientFactory httpFactory, HttpContext ctx) =>
{
    var client = httpFactory.CreateClient("identity");
    var response = await client.PostAsJsonAsync("api/users/login", new { req.Email, req.Password });
    if (!response.IsSuccessStatusCode) return Results.Unauthorized();

    var token = await response.Content.ReadFromJsonAsync<TokenResponse>();
    if (token is null) return Results.Unauthorized();

    SetAuthCookie(ctx, token.Token);
    return Results.Ok(new UserInfoDto(token.UserId, token.Handle, token.DisplayName));
});

app.MapPost("/bff/logout", (HttpContext ctx) =>
{
    ctx.Response.Cookies.Delete("bff_token");
    return Results.Ok();
});

app.MapGet("/bff/user", (HttpContext ctx) =>
{
    var raw = ctx.Request.Cookies["bff_token"];
    if (string.IsNullOrEmpty(raw)) return Results.Unauthorized();

    var info = ParseJwtClaims(raw);
    if (info is null) return Results.Unauthorized();

    return Results.Ok(info);
});

app.MapPost("/bff/register", async (RegisterRequest req, IHttpClientFactory httpFactory) =>
{
    var client = httpFactory.CreateClient("identity");
    var response = await client.PostAsJsonAsync("api/registrations",
        new { req.Email, req.Password, req.Handle, req.DisplayName });

    if (!response.IsSuccessStatusCode)
        return Results.BadRequest(await ReadErrorAsync(response));

    var result = await response.Content.ReadFromJsonAsync<PendingRegistrationResponse>();
    return Results.Ok(result);
});

app.MapPost("/bff/register/verify", async (VerifyRequest req, IHttpClientFactory httpFactory, HttpContext ctx) =>
{
    var client = httpFactory.CreateClient("identity");
    var response = await client.PostAsJsonAsync("api/registrations/verify",
        new { req.PendingRegistrationId, req.Code });

    if (!response.IsSuccessStatusCode)
        return Results.BadRequest(await ReadErrorAsync(response));

    var token = await response.Content.ReadFromJsonAsync<TokenResponse>();
    if (token is null) return Results.Problem("Verification failed.");

    SetAuthCookie(ctx, token.Token);
    return Results.Ok(new UserInfoDto(token.UserId, token.Handle, token.DisplayName));
});

app.MapPost("/bff/password-reset-request", async (PasswordResetRequestDto req, IHttpClientFactory httpFactory) =>
{
    var client = httpFactory.CreateClient("identity");
    await client.PostAsJsonAsync("api/password-reset-requests", new { req.Email });
    return Results.Ok();
});

app.MapPost("/bff/password-reset", async (PasswordResetDto req, IHttpClientFactory httpFactory) =>
{
    var client = httpFactory.CreateClient("identity");
    var response = await client.PostAsJsonAsync("api/password-resets",
        new { req.Token, req.NewPassword });

    if (!response.IsSuccessStatusCode)
        return Results.BadRequest(await ReadErrorAsync(response));

    return Results.Ok();
});

app.MapReverseProxy();
app.MapFallbackToFile("index.html");

app.Run();

static void SetAuthCookie(HttpContext ctx, string token)
{
    ctx.Response.Cookies.Append("bff_token", token, new CookieOptions
    {
        HttpOnly = true,
        Secure = ctx.Request.IsHttps,
        SameSite = SameSiteMode.Strict,
        Path = "/",
        MaxAge = TimeSpan.FromDays(7)
    });
}

static UserInfoDto? ParseJwtClaims(string token)
{
    var parts = token.Split('.');
    if (parts.Length != 3) return null;

    try
    {
        var padding = (4 - parts[1].Length % 4) % 4;
        var base64 = parts[1].Replace('-', '+').Replace('_', '/') + new string('=', padding);
        var json = Encoding.UTF8.GetString(Convert.FromBase64String(base64));
        using var doc = JsonDocument.Parse(json);
        var root = doc.RootElement;

        var userId = root.TryGetProperty("nameid", out var n) ? n.GetString() : null;
        var handle = root.TryGetProperty("handle", out var h) ? h.GetString() : null;
        var displayName = root.TryGetProperty("display_name", out var d) ? d.GetString() : null;

        if (string.IsNullOrEmpty(userId) || string.IsNullOrEmpty(handle)) return null;

        return new UserInfoDto(Guid.Parse(userId), handle, displayName ?? handle);
    }
    catch
    {
        return null;
    }
}

static async Task<object> ReadErrorAsync(HttpResponseMessage response)
{
    try
    {
        var error = await response.Content.ReadFromJsonAsync<ErrorResponse>();
        return new { error = string.IsNullOrWhiteSpace(error?.Error) ? "An error occurred." : error.Error };
    }
    catch
    {
        return new { error = "An error occurred." };
    }
}

record LoginRequest(string Email, string Password);
record RegisterRequest(string Email, string Password, string Handle, string DisplayName);
record VerifyRequest(Guid PendingRegistrationId, string Code);
record PasswordResetRequestDto(string Email);
record PasswordResetDto(string Token, string NewPassword);
record TokenResponse(string Token, Guid UserId, string Username, string Handle, string DisplayName);
record UserInfoDto(Guid UserId, string Handle, string DisplayName);
record PendingRegistrationResponse(Guid PendingRegistrationId);
record ErrorResponse(string Error);
