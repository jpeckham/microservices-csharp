using System.Net.Http.Headers;
using System.Net.Http.Json;
using Microsoft.AspNetCore.Components.Server.ProtectedBrowserStorage;

namespace Social.Web.Services;

public sealed class SessionState(ProtectedLocalStorage storage)
{
    private const string TokenKey = "social_token";
    private const string UserIdKey = "social_user_id";
    private const string HandleKey = "social_handle";
    private const string DisplayNameKey = "social_display_name";

    public async Task SaveAsync(TokenResponse token)
    {
        await storage.SetAsync(TokenKey, token.Token);
        await storage.SetAsync(UserIdKey, token.UserId);
        await storage.SetAsync(HandleKey, token.Handle);
        await storage.SetAsync(DisplayNameKey, token.DisplayName);
    }

    public async Task ClearAsync()
    {
        await storage.DeleteAsync(TokenKey);
        await storage.DeleteAsync(UserIdKey);
        await storage.DeleteAsync(HandleKey);
        await storage.DeleteAsync(DisplayNameKey);
    }

    public async Task<string?> GetTokenAsync() => (await storage.GetAsync<string>(TokenKey)).Value;
    public async Task<Guid?> GetUserIdAsync() => (await storage.GetAsync<Guid>(UserIdKey)).Value;
    public async Task<string?> GetHandleAsync() => (await storage.GetAsync<string>(HandleKey)).Value;
    public async Task<string?> GetDisplayNameAsync() => (await storage.GetAsync<string>(DisplayNameKey)).Value;
}

public sealed class AuthApi(HttpClient identity, SessionState session)
{
    public async Task<string?> RegisterAsync(string email, string password, string handle, string displayName)
    {
        var response = await identity.PostAsJsonAsync("api/users/register", new { email, password, handle, displayName });
        return response.IsSuccessStatusCode ? null : await ReadErrorAsync(response, "Registration failed.");
    }

    public async Task<string?> LoginAsync(string email, string password)
    {
        var response = await identity.PostAsJsonAsync("api/users/login", new { email, password });
        if (!response.IsSuccessStatusCode) return "Invalid email or password.";
        var token = await response.Content.ReadFromJsonAsync<TokenResponse>();
        if (token is null) return "Login failed.";
        await session.SaveAsync(token);
        return null;
    }

    public Task LogoutAsync() => session.ClearAsync();

    public async Task<UserProfileDto?> GetMeAsync()
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, "api/users/me");
        await AuthorizeAsync(request);
        var response = await identity.SendAsync(request);
        return response.IsSuccessStatusCode ? await response.Content.ReadFromJsonAsync<UserProfileDto>() : null;
    }

    public async Task<UserProfileDto?> GetProfileAsync(string handle)
    {
        var normalized = handle.TrimStart('@');
        using var request = new HttpRequestMessage(HttpMethod.Get, $"api/users/by-handle/{Uri.EscapeDataString(normalized)}");
        await AuthorizeAsync(request);
        var response = await identity.SendAsync(request);
        return response.IsSuccessStatusCode ? await response.Content.ReadFromJsonAsync<UserProfileDto>() : null;
    }

    public async Task<bool> UpdateDisplayNameAsync(string displayName)
    {
        using var request = new HttpRequestMessage(HttpMethod.Put, "api/users/me/display-name")
        {
            Content = JsonContent.Create(new { displayName })
        };
        await AuthorizeAsync(request);
        return (await identity.SendAsync(request)).IsSuccessStatusCode;
    }

    private async Task AuthorizeAsync(HttpRequestMessage request)
    {
        var token = await session.GetTokenAsync();
        if (!string.IsNullOrWhiteSpace(token))
        {
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        }
    }

    private static async Task<string> ReadErrorAsync(HttpResponseMessage response, string fallback)
    {
        try
        {
            var error = await response.Content.ReadFromJsonAsync<ErrorResponse>();
            return string.IsNullOrWhiteSpace(error?.Error) ? fallback : error.Error;
        }
        catch
        {
            return fallback;
        }
    }
}

public sealed class SocialApi(HttpClient social, SessionState session)
{
    public async Task<bool> FollowAsync(Guid userId) => await SendAsync(HttpMethod.Post, $"api/users/{userId}/follows");
    public async Task<bool> UnfollowAsync(Guid userId) => await SendAsync(HttpMethod.Delete, $"api/users/{userId}/follows");
    public async Task<FollowCountsDto> CountsAsync(Guid userId) =>
        await social.GetFromJsonAsync<FollowCountsDto>($"api/users/{userId}/counts") ?? new FollowCountsDto(0, 0);

    private async Task<bool> SendAsync(HttpMethod method, string url)
    {
        using var request = new HttpRequestMessage(method, url);
        var token = await session.GetTokenAsync();
        if (!string.IsNullOrWhiteSpace(token)) request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        return (await social.SendAsync(request)).IsSuccessStatusCode;
    }
}

public sealed class PostApi(HttpClient post, SessionState session)
{
    public async Task<PostDto?> CreateAsync(string content)
    {
        using var request = new HttpRequestMessage(HttpMethod.Post, "api/posts") { Content = JsonContent.Create(new { content }) };
        await AuthorizeAsync(request);
        var response = await post.SendAsync(request);
        return response.IsSuccessStatusCode ? await response.Content.ReadFromJsonAsync<PostDto>() : null;
    }

    public async Task<PostDto?> EditAsync(Guid postId, string content)
    {
        using var request = new HttpRequestMessage(HttpMethod.Put, $"api/posts/{postId}") { Content = JsonContent.Create(new { content }) };
        await AuthorizeAsync(request);
        var response = await post.SendAsync(request);
        return response.IsSuccessStatusCode ? await response.Content.ReadFromJsonAsync<PostDto>() : null;
    }

    public async Task<bool> DeleteAsync(Guid postId)
    {
        using var request = new HttpRequestMessage(HttpMethod.Delete, $"api/posts/{postId}");
        await AuthorizeAsync(request);
        return (await post.SendAsync(request)).IsSuccessStatusCode;
    }

    public async Task<PostDto?> GetAsync(Guid postId) => await post.GetFromJsonAsync<PostDto>($"api/posts/{postId}");
    public async Task<List<PostDto>> SearchAsync(string q) => (await post.GetFromJsonAsync<SearchResultsDto>($"api/posts/search?q={Uri.EscapeDataString(q)}&limit=20&offset=0"))?.Posts ?? [];
    public async Task<List<PostDto>> ByUserAsync(Guid userId) => await post.GetFromJsonAsync<List<PostDto>>($"api/posts/by-user/{userId}?limit=20&offset=0") ?? [];

    private async Task AuthorizeAsync(HttpRequestMessage request)
    {
        var token = await session.GetTokenAsync();
        if (!string.IsNullOrWhiteSpace(token)) request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
    }
}

public sealed class FeedApi(HttpClient feed, SessionState session)
{
    public async Task<List<FeedEntryDto>> GetFeedAsync(bool followingOnly)
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, $"api/feed?limit=20&offset=0&followingOnly={followingOnly}");
        var token = await session.GetTokenAsync();
        if (!string.IsNullOrWhiteSpace(token)) request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        var response = await feed.SendAsync(request);
        return response.IsSuccessStatusCode ? await response.Content.ReadFromJsonAsync<List<FeedEntryDto>>() ?? [] : [];
    }

    public async Task<List<FeedEntryDto>> ByUserAsync(Guid userId) =>
        await feed.GetFromJsonAsync<List<FeedEntryDto>>($"api/feed/users/{userId}?limit=20&offset=0") ?? [];
}

public sealed class EngagementApi(HttpClient engagement, SessionState session)
{
    public async Task<bool> LikeAsync(Guid postId) => await SendAsync(HttpMethod.Post, $"api/posts/{postId}/likes");
    public async Task<bool> UnlikeAsync(Guid postId) => await SendAsync(HttpMethod.Delete, $"api/posts/{postId}/likes");

    public async Task<CommentDto?> CommentAsync(Guid postId, string content)
    {
        using var request = new HttpRequestMessage(HttpMethod.Post, $"api/posts/{postId}/comments") { Content = JsonContent.Create(new { content }) };
        await AuthorizeAsync(request);
        var response = await engagement.SendAsync(request);
        return response.IsSuccessStatusCode ? await response.Content.ReadFromJsonAsync<CommentDto>() : null;
    }

    public async Task<List<CommentDto>> CommentsAsync(Guid postId) =>
        await engagement.GetFromJsonAsync<List<CommentDto>>($"api/posts/{postId}/comments") ?? [];

    private async Task<bool> SendAsync(HttpMethod method, string url)
    {
        using var request = new HttpRequestMessage(method, url);
        await AuthorizeAsync(request);
        return (await engagement.SendAsync(request)).IsSuccessStatusCode;
    }

    private async Task AuthorizeAsync(HttpRequestMessage request)
    {
        var token = await session.GetTokenAsync();
        if (!string.IsNullOrWhiteSpace(token)) request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
    }
}

public sealed record TokenResponse(string Token, Guid UserId, string Username, string Handle, string DisplayName);
public sealed record UserProfileDto(Guid UserId, string Username, string Handle, string DisplayName, DateTimeOffset RegisteredAt, int FollowerCount, int FollowingCount, bool IsOwnProfile, bool IsFollowedByMe);
public sealed record FollowCountsDto(int FollowerCount, int FollowingCount);
public sealed record PostDto(Guid PostId, Guid AuthorId, string AuthorHandle, string AuthorDisplayName, string Content, DateTimeOffset PostedAt, DateTimeOffset UpdatedAt);
public sealed record FeedEntryDto(Guid PostId, Guid AuthorId, string AuthorHandle, string AuthorDisplayName, string Content, DateTimeOffset PostedAt, int LikeCount, int CommentCount);
public sealed record SearchResultsDto(List<PostDto> Posts, string Query, int Limit, int Offset);
public sealed record CommentDto(Guid CommentId, Guid PostId, Guid AuthorId, string AuthorHandle, string AuthorDisplayName, string Content, DateTimeOffset CreatedAt);
public sealed record ErrorResponse(string Error);
