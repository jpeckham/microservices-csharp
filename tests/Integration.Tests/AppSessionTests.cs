extern alias WebClient;
using WebClient::Social.Web.Client.Services;

namespace Integration.Tests;

// Reproduces: after login, feed page shows "not logged in" / cannot post.
//
// Root cause: AppSession.InitAsync() set _initialized = true before awaiting the HTTP call.
// When MainLayout and Home both called InitAsync() "at the same time", the second caller
// saw _initialized = true and returned an already-completed Task — before the HTTP call
// finished and before the session was populated. Home then rendered with IsLoggedIn = false.
public sealed class AppSessionTests
{
    [Fact]
    public async Task InitAsync_WhenCalledConcurrently_BothCallersWaitForSessionToBePopulated()
    {
        // Arrange: gate that holds the /bff/user response until we release it
        var gate = new SemaphoreSlim(0, 1);
        var http = new HttpClient(new GatedUserInfoHandler(gate))
        {
            BaseAddress = new Uri("http://localhost/")
        };
        var session = new AppSession(http);

        // Act: simulate MainLayout.OnInitializedAsync and Home.OnInitializedAsync
        // calling InitAsync() before the first HTTP call has returned
        var mainLayoutTask = session.InitAsync();   // starts HTTP call, now blocked at gate
        var homeTask       = session.InitAsync();   // must return the SAME task, not a completed one

        // Assert BEFORE releasing: the second task must still be pending.
        // With the buggy code (_initialized flag set before await) homeTask was already
        // Task.CompletedTask here, so Home rendered immediately with IsLoggedIn = false.
        Assert.False(homeTask.IsCompleted,
            "InitAsync() returned a completed task before the HTTP call finished — " +
            "Home would render as logged-out even though the user just logged in.");

        // Release the HTTP response and wait for both tasks
        gate.Release();
        await Task.WhenAll(mainLayoutTask, homeTask);

        // After both tasks finish the session must be populated
        Assert.True(session.IsLoggedIn,
            "Session should be logged in after InitAsync() completes.");
        Assert.Equal("@test", session.Handle);
    }

    [Fact]
    public async Task InitAsync_SubsequentCall_ReturnsSameCompletedTask()
    {
        var http = new HttpClient(new ImmediateUserInfoHandler())
        {
            BaseAddress = new Uri("http://localhost/")
        };
        var session = new AppSession(http);

        await session.InitAsync();

        // A later call (e.g. navigating to another page) must not fire a second HTTP call
        var second = session.InitAsync();
        Assert.True(second.IsCompleted);
        Assert.True(session.IsLoggedIn);
    }

    // ── fake handlers ────────────────────────────────────────────────────────

    private sealed class GatedUserInfoHandler(SemaphoreSlim gate) : HttpMessageHandler
    {
        protected override async Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request, CancellationToken cancellationToken)
        {
            await gate.WaitAsync(cancellationToken);
            return UserInfoResponse();
        }
    }

    private sealed class ImmediateUserInfoHandler : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request, CancellationToken cancellationToken)
            => Task.FromResult(UserInfoResponse());
    }

    private static HttpResponseMessage UserInfoResponse()
    {
        var json = """{"userId":"11111111-1111-1111-1111-111111111111","handle":"@test","displayName":"Test User"}""";
        return new HttpResponseMessage(System.Net.HttpStatusCode.OK)
        {
            Content = new StringContent(json, System.Text.Encoding.UTF8, "application/json")
        };
    }
}
