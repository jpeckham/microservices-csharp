# Feed Reply Awareness via ParentPostId in PostCreated

## Source

The reference's `ScrollFor` excludes `p.ParentPostId is null` posts and sorts by
`LatestConversationActivityAt` (latest reply time bubbles the post up). Our Feed.Api has
no awareness of whether a `PostCreated` event is a reply or a root post, so:
1. Replies appear as top-level feed entries (incorrect)
2. Feed order is purely by `PostedAt` — replies don't bubble their parent up

## Changes

### `PostCreated` event
Add `Guid? ParentPostId = null` (non-breaking, null = root post).

### `Post.Api`
Pass `ParentPostId = postId` when publishing `PostCreated` for replies.

### `Feed.Api`
- Add `LastActivityAt` to `FeedEntryDocument` (initialized from `PostedAt`)
- `/events/PostCreated` handler:
  - `ParentPostId == null`: create/upsert feed entry with `LastActivityAt = OccurredAt`
  - `ParentPostId != null`: do NOT create an entry; update parent entry's
    `LastActivityAt = OccurredAt` if parent exists in feed and new time is later
- `GET /api/feed`: sort by `LastActivityAt` descending instead of `PostedAt`
- Same logic in `ServiceBusFeedConsumer`

## Affected Files

| File | Change |
|------|--------|
| `src/Shared.Contracts/Events/IntegrationEvents.cs` | Add `ParentPostId` to `PostCreated` |
| `src/Post.Api/Program.cs` | Pass `ParentPostId` when publishing reply events |
| `src/Feed.Api/Program.cs` | `LastActivityAt`, reply-exclusion, activity-sort |
| `tests/Integration.Tests/FeedApiTests.cs` | Tests for exclusion and bubble-up |
