using Shared.Contracts.Auth;
using Shared.Contracts.Events;
using Shared.Contracts.Messaging;

namespace Architecture.Tests;

public sealed class ContractShapeTests
{
    [Fact]
    public void Integration_events_expose_names_and_primary_ids()
    {
        var userId = Guid.NewGuid();
        var postId = Guid.NewGuid();

        IntegrationEvent[] events =
        [
            new UserCreated(userId, "ada", "@ada", "Ada Lovelace", DateTimeOffset.UtcNow),
            new UserProfileUpdated(userId, "@ada", "Countess Ada", DateTimeOffset.UtcNow),
            new PostCreated(postId, userId, "@ada", "Ada Lovelace", "First post", DateTimeOffset.UtcNow),
            new PostUpdated(postId, userId, "Edited post", DateTimeOffset.UtcNow),
            new PostDeleted(postId, userId, DateTimeOffset.UtcNow),
            new UserFollowed(Guid.NewGuid(), userId, Guid.NewGuid(), DateTimeOffset.UtcNow),
            new UserUnfollowed(Guid.NewGuid(), userId, Guid.NewGuid(), DateTimeOffset.UtcNow),
            new LikeAdded(Guid.NewGuid(), postId, userId, DateTimeOffset.UtcNow),
            new LikeRemoved(Guid.NewGuid(), postId, userId, DateTimeOffset.UtcNow),
            new CommentAdded(Guid.NewGuid(), postId, userId, "@ada", "Ada Lovelace", "Nice", DateTimeOffset.UtcNow)
        ];

        Assert.All(events, e =>
        {
            Assert.False(string.IsNullOrWhiteSpace(e.EventName));
            Assert.NotEqual(Guid.Empty, e.EventId);
            Assert.NotEqual(default, e.OccurredAt);
        });
    }

    [Fact]
    public void Auth_constants_define_shared_jwt_claims()
    {
        Assert.Equal("handle", AuthConstants.HandleClaim);
        Assert.Equal("display_name", AuthConstants.DisplayNameClaim);
    }

    [Fact]
    public async Task Event_publisher_accepts_any_integration_event()
    {
        IEventPublisher publisher = new CapturingPublisher();
        var integrationEvent = new PostDeleted(Guid.NewGuid(), Guid.NewGuid(), DateTimeOffset.UtcNow);

        await publisher.PublishAsync(integrationEvent, CancellationToken.None);

        Assert.Equal(integrationEvent, Assert.Single(((CapturingPublisher)publisher).Published));
    }

    private sealed class CapturingPublisher : IEventPublisher
    {
        public List<IntegrationEvent> Published { get; } = [];

        public Task PublishAsync(IntegrationEvent integrationEvent, CancellationToken cancellationToken = default)
        {
            Published.Add(integrationEvent);
            return Task.CompletedTask;
        }
    }
}
