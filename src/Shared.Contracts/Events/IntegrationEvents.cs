namespace Shared.Contracts.Events;

public abstract record IntegrationEvent(Guid EventId, DateTimeOffset OccurredAt)
{
    public abstract string EventName { get; }
}

public sealed record UserCreated(
    Guid UserId,
    string Username,
    string Handle,
    string DisplayName,
    DateTimeOffset OccurredAt)
    : IntegrationEvent(Guid.NewGuid(), OccurredAt)
{
    public override string EventName => nameof(UserCreated);
}

public sealed record UserProfileUpdated(
    Guid UserId,
    string Handle,
    string DisplayName,
    DateTimeOffset OccurredAt)
    : IntegrationEvent(Guid.NewGuid(), OccurredAt)
{
    public override string EventName => nameof(UserProfileUpdated);
}

public sealed record PostCreated(
    Guid PostId,
    Guid AuthorId,
    string AuthorHandle,
    string AuthorDisplayName,
    string Content,
    DateTimeOffset OccurredAt)
    : IntegrationEvent(Guid.NewGuid(), OccurredAt)
{
    public override string EventName => nameof(PostCreated);
}

public sealed record PostUpdated(
    Guid PostId,
    Guid AuthorId,
    string Content,
    DateTimeOffset OccurredAt)
    : IntegrationEvent(Guid.NewGuid(), OccurredAt)
{
    public override string EventName => nameof(PostUpdated);
}

public sealed record PostDeleted(
    Guid PostId,
    Guid AuthorId,
    DateTimeOffset OccurredAt)
    : IntegrationEvent(Guid.NewGuid(), OccurredAt)
{
    public override string EventName => nameof(PostDeleted);
}

public sealed record UserFollowed(
    Guid RelationshipId,
    Guid FollowerId,
    Guid FollowingId,
    DateTimeOffset OccurredAt)
    : IntegrationEvent(Guid.NewGuid(), OccurredAt)
{
    public override string EventName => nameof(UserFollowed);
}

public sealed record UserUnfollowed(
    Guid RelationshipId,
    Guid FollowerId,
    Guid FollowingId,
    DateTimeOffset OccurredAt)
    : IntegrationEvent(Guid.NewGuid(), OccurredAt)
{
    public override string EventName => nameof(UserUnfollowed);
}

public sealed record LikeAdded(
    Guid LikeId,
    Guid PostId,
    Guid UserId,
    DateTimeOffset OccurredAt)
    : IntegrationEvent(Guid.NewGuid(), OccurredAt)
{
    public override string EventName => nameof(LikeAdded);
}

public sealed record LikeRemoved(
    Guid LikeId,
    Guid PostId,
    Guid UserId,
    DateTimeOffset OccurredAt)
    : IntegrationEvent(Guid.NewGuid(), OccurredAt)
{
    public override string EventName => nameof(LikeRemoved);
}

public sealed record CommentAdded(
    Guid CommentId,
    Guid PostId,
    Guid AuthorId,
    string AuthorHandle,
    string AuthorDisplayName,
    string Content,
    DateTimeOffset OccurredAt)
    : IntegrationEvent(Guid.NewGuid(), OccurredAt)
{
    public override string EventName => nameof(CommentAdded);
}
