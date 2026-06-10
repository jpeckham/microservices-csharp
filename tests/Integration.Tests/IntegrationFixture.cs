using MongoDB.Bson;
using MongoDB.Driver;
using Testcontainers.MongoDb;

namespace Integration.Tests;

public sealed class IntegrationFixture : IAsyncLifetime
{
    private readonly MongoDbContainer _mongo = new MongoDbBuilder()
        .WithImage("mongo:7")
        .Build();

    private MongoClient _mongoClient = null!;
    public IMongoDatabase IdentityDb => _mongoClient.GetDatabase("test_identity");

    private IdentityApiFactory _identityFactory = null!;
    private PostApiFactory _postFactory = null!;
    private SocialApiFactory _socialFactory = null!;
    private EngagementApiFactory _engagementFactory = null!;
    private FeedApiFactory _feedFactory = null!;

    public HttpClient Identity { get; private set; } = null!;
    public HttpClient Post { get; private set; } = null!;
    public HttpClient Social { get; private set; } = null!;
    public HttpClient Engagement { get; private set; } = null!;
    public HttpClient Feed { get; private set; } = null!;

    public async Task InitializeAsync()
    {
        await _mongo.StartAsync();
        var cs = _mongo.GetConnectionString();

        _mongoClient = new MongoClient(cs);
        _identityFactory = new IdentityApiFactory(cs);
        _postFactory = new PostApiFactory(cs);
        _socialFactory = new SocialApiFactory(cs);
        _engagementFactory = new EngagementApiFactory(cs);
        _feedFactory = new FeedApiFactory(cs);

        Identity = _identityFactory.CreateClient();
        Post = _postFactory.CreateClient();
        Social = _socialFactory.CreateClient();
        Engagement = _engagementFactory.CreateClient();
        Feed = _feedFactory.CreateClient();
    }

    public async Task DisposeAsync()
    {
        _identityFactory?.Dispose();
        _postFactory?.Dispose();
        _socialFactory?.Dispose();
        _engagementFactory?.Dispose();
        _feedFactory?.Dispose();
        await _mongo.DisposeAsync();
    }

    public async Task<AuthSession> RegisterAndLoginAsync()
    {
        var id = Guid.NewGuid().ToString("N")[..10];
        var email = $"{id}@test.com";
        var handle = id;

        var reg = await Identity.PostAsJsonAsync("/api/users/register",
            new { email, password = "Pass123!", handle, displayName = $"User {id}" });
        reg.EnsureSuccessStatusCode();

        var token = await reg.Content.ReadFromJsonAsync<TokenDto>()
            ?? throw new InvalidOperationException("Register returned no token.");

        return new AuthSession(token.UserId, token.Token, handle, $"User {id}");
    }

    public HttpRequestMessage AuthorizedRequest(HttpMethod method, string url, string token, object? body = null)
    {
        var request = new HttpRequestMessage(method, url);
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        if (body is not null)
            request.Content = JsonContent.Create(body);
        return request;
    }

    public async Task<List<EmailMessageDto>> GetDevEmailsAsync()
    {
        var response = await Identity.GetAsync("/dev/emails");
        response.EnsureSuccessStatusCode();
        return await response.Content.ReadFromJsonAsync<List<EmailMessageDto>>() ?? [];
    }
}

[CollectionDefinition(nameof(IntegrationCollection))]
public sealed class IntegrationCollection : ICollectionFixture<IntegrationFixture> { }

public sealed record AuthSession(Guid UserId, string Token, string Handle, string DisplayName);
public sealed record TokenDto(string Token, Guid UserId, string Username, string Handle, string DisplayName);
public sealed record UserProfileDto(Guid UserId, string Username, string Handle, string DisplayName, int FollowerCount, int FollowingCount, bool IsOwnProfile, bool IsFollowedByMe);
public sealed record QuotedPostDto(Guid PostId, string AuthorHandle, string AuthorDisplayName, string Content, DateTimeOffset PostedAt);
public sealed record PostDto(Guid PostId, Guid AuthorId, string AuthorHandle, string AuthorDisplayName, string Content, DateTimeOffset PostedAt, DateTimeOffset UpdatedAt, List<string>? Hashtags = null, List<string>? Mentions = null, Guid? ParentPostId = null, Guid? OriginalPostId = null, int ReplyCount = 0, int RepostCount = 0, bool RepostedByMe = false, QuotedPostDto? QuotedPost = null);
public sealed record FeedEntryDto(Guid PostId, Guid AuthorId, string AuthorHandle, string Content, DateTimeOffset PostedAt, int LikeCount, int CommentCount);
public sealed record CommentDto(Guid CommentId, Guid PostId, Guid AuthorId, string AuthorHandle, string Content, DateTimeOffset CreatedAt);
public sealed record FollowCountsDto(int FollowerCount, int FollowingCount);
public sealed record EmailMessageDto(string To, string Subject, string Body, DateTimeOffset SentAt);
