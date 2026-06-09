using System.Net.Http.Json;

namespace Social.Web.Client.Services;

public sealed class ApiClient(HttpClient http)
{
    public async Task<(UserInfoDto? User, string? Error)> LoginAsync(string email, string password)
    {
        var response = await http.PostAsJsonAsync("/bff/login", new { email, password });
        if (!response.IsSuccessStatusCode) return (null, "Invalid email or password.");
        var user = await response.Content.ReadFromJsonAsync<UserInfoDto>();
        return (user, null);
    }

    public Task LogoutAsync() => http.PostAsync("/bff/logout", null);

    public async Task<(Guid? PendingRegistrationId, string? Error)> StartRegistrationAsync(
        string email, string password, string handle, string displayName)
    {
        var response = await http.PostAsJsonAsync("/bff/register", new { email, password, handle, displayName });
        if (!response.IsSuccessStatusCode) return (null, await ReadErrorAsync(response, "Registration failed."));
        var result = await response.Content.ReadFromJsonAsync<PendingRegistrationResponse>();
        return (result?.PendingRegistrationId, null);
    }

    public async Task<(UserInfoDto? User, string? Error)> VerifyRegistrationAsync(
        Guid pendingRegistrationId, string code)
    {
        var response = await http.PostAsJsonAsync("/bff/register/verify", new { pendingRegistrationId, code });
        if (!response.IsSuccessStatusCode) return (null, await ReadErrorAsync(response, "Verification failed."));
        var user = await response.Content.ReadFromJsonAsync<UserInfoDto>();
        return (user, null);
    }

    public async Task<string?> RequestPasswordResetAsync(string email)
    {
        await http.PostAsJsonAsync("/bff/password-reset-request", new { email });
        return null;
    }

    public async Task<string?> ResetPasswordAsync(string token, string newPassword)
    {
        var response = await http.PostAsJsonAsync("/bff/password-reset", new { token, newPassword });
        return response.IsSuccessStatusCode ? null : await ReadErrorAsync(response, "Failed to reset password.");
    }

    public async Task<UserProfileDto?> GetProfileAsync(string handle)
    {
        var normalized = handle.TrimStart('@');
        var response = await http.GetAsync($"/api/users/by-handle/{Uri.EscapeDataString(normalized)}");
        return response.IsSuccessStatusCode
            ? await response.Content.ReadFromJsonAsync<UserProfileDto>()
            : null;
    }

    public async Task<List<UserSearchResultDto>> SearchUsersAsync(string q, int limit = 20, int offset = 0)
    {
        var response = await http.GetAsync($"/api/users/search?q={Uri.EscapeDataString(q)}&limit={limit}&offset={offset}");
        return response.IsSuccessStatusCode
            ? await response.Content.ReadFromJsonAsync<List<UserSearchResultDto>>() ?? []
            : [];
    }

    public async Task<bool> UpdateDisplayNameAsync(string displayName)
    {
        var response = await http.PutAsJsonAsync("/api/users/me/display-name", new { displayName });
        return response.IsSuccessStatusCode;
    }

    public async Task<List<FeedEntryDto>> GetFeedAsync(bool followingOnly) =>
        await http.GetFromJsonAsync<List<FeedEntryDto>>(
            $"/api/feed?limit=20&offset=0&followingOnly={followingOnly}") ?? [];

    public async Task<List<FeedEntryDto>> GetUserFeedAsync(Guid userId) =>
        await http.GetFromJsonAsync<List<FeedEntryDto>>(
            $"/api/feed/users/{userId}?limit=20&offset=0") ?? [];

    public async Task<PostDto?> CreatePostAsync(string content)
    {
        var response = await http.PostAsJsonAsync("/api/posts", new { content });
        return response.IsSuccessStatusCode
            ? await response.Content.ReadFromJsonAsync<PostDto>()
            : null;
    }

    public async Task<PostDto?> EditPostAsync(Guid postId, string content)
    {
        var response = await http.PutAsJsonAsync($"/api/posts/{postId}", new { content });
        return response.IsSuccessStatusCode
            ? await response.Content.ReadFromJsonAsync<PostDto>()
            : null;
    }

    public async Task<bool> DeletePostAsync(Guid postId)
    {
        var response = await http.DeleteAsync($"/api/posts/{postId}");
        return response.IsSuccessStatusCode;
    }

    public Task<PostDto?> GetPostAsync(Guid postId) =>
        http.GetFromJsonAsync<PostDto>($"/api/posts/{postId}");

    public async Task<List<PostDto>> SearchPostsAsync(string q) =>
        (await http.GetFromJsonAsync<SearchResultsDto>(
            $"/api/posts/search?q={Uri.EscapeDataString(q)}&limit=20&offset=0"))?.Posts ?? [];

    public async Task<List<PostDto>> GetPostsByUserAsync(Guid userId) =>
        await http.GetFromJsonAsync<List<PostDto>>(
            $"/api/posts/by-user/{userId}?limit=20&offset=0") ?? [];

    public async Task<bool> LikeAsync(Guid postId)
    {
        var response = await http.PostAsync($"/api/posts/{postId}/likes", null);
        return response.IsSuccessStatusCode;
    }

    public async Task<bool> UnlikeAsync(Guid postId)
    {
        var response = await http.DeleteAsync($"/api/posts/{postId}/likes");
        return response.IsSuccessStatusCode;
    }

    public async Task<CommentDto?> CommentAsync(Guid postId, string content)
    {
        var response = await http.PostAsJsonAsync($"/api/posts/{postId}/comments", new { content });
        return response.IsSuccessStatusCode
            ? await response.Content.ReadFromJsonAsync<CommentDto>()
            : null;
    }

    public async Task<List<CommentDto>> GetCommentsAsync(Guid postId) =>
        await http.GetFromJsonAsync<List<CommentDto>>($"/api/posts/{postId}/comments") ?? [];

    public async Task<bool> FollowAsync(Guid userId)
    {
        var response = await http.PostAsync($"/api/users/{userId}/follows", null);
        return response.IsSuccessStatusCode;
    }

    public async Task<bool> UnfollowAsync(Guid userId)
    {
        var response = await http.DeleteAsync($"/api/users/{userId}/follows");
        return response.IsSuccessStatusCode;
    }

    public async Task<FollowCountsDto> GetFollowCountsAsync(Guid userId) =>
        await http.GetFromJsonAsync<FollowCountsDto>($"/api/users/{userId}/counts")
            ?? new FollowCountsDto(0, 0);

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

public sealed record UserInfoDto(Guid UserId, string Handle, string DisplayName);
public sealed record UserProfileDto(Guid UserId, string Username, string Handle, string DisplayName, DateTimeOffset RegisteredAt, int FollowerCount, int FollowingCount, bool IsOwnProfile, bool IsFollowedByMe);
public sealed record FollowCountsDto(int FollowerCount, int FollowingCount);
public sealed record PostDto(Guid PostId, Guid AuthorId, string AuthorHandle, string AuthorDisplayName, string Content, DateTimeOffset PostedAt, DateTimeOffset UpdatedAt, List<string>? Hashtags = null, List<string>? Mentions = null);
public sealed record FeedEntryDto(Guid PostId, Guid AuthorId, string AuthorHandle, string AuthorDisplayName, string Content, DateTimeOffset PostedAt, int LikeCount, int CommentCount);
public sealed record SearchResultsDto(List<PostDto> Posts, string Query, int Limit, int Offset);
public sealed record CommentDto(Guid CommentId, Guid PostId, Guid AuthorId, string AuthorHandle, string AuthorDisplayName, string Content, DateTimeOffset CreatedAt);
public sealed record UserSearchResultDto(Guid UserId, string Handle, string DisplayName);
public sealed record PendingRegistrationResponse(Guid PendingRegistrationId);
public sealed record ErrorResponse(string Error);
