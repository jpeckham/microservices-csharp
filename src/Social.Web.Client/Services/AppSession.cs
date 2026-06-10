using System.Net.Http.Json;

namespace Social.Web.Client.Services;

public sealed class AppSession(HttpClient http)
{
    private Task? _initTask;

    public Guid? UserId { get; private set; }
    public string? Handle { get; private set; }
    public string? DisplayName { get; private set; }
    public bool IsLoggedIn => !string.IsNullOrWhiteSpace(Handle);

    public Task InitAsync() => _initTask ??= FetchUserAsync();

    private async Task FetchUserAsync()
    {
        try
        {
            var response = await http.GetAsync("/bff/user");
            if (response.IsSuccessStatusCode)
            {
                var info = await response.Content.ReadFromJsonAsync<UserInfoDto>();
                if (info is not null) Apply(info);
            }
        }
        catch { }
    }

    public void Apply(UserInfoDto info)
    {
        UserId = info.UserId;
        Handle = info.Handle;
        DisplayName = info.DisplayName;
    }

    public void Clear()
    {
        UserId = null;
        Handle = null;
        DisplayName = null;
    }
}
