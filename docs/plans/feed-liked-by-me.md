# LikedByMe on FeedEntryDto

## Source

The reference's `PostSummaryViewModel` has `LikedByCurrentReader` — a boolean indicating
whether the authenticated reader has liked each post. This is essential for the like
button to render in the correct toggled/untoggled state without a separate API call.

Our `FeedEntryDto` already exposes `LikeCount` (a running total denormalized from
`LikeAdded`/`LikeRemoved` events) but does not expose whether the *current* user
has liked a given entry. Clients must make a separate call to
`GET /api/posts/{id}/summary` (Engagement.Api) per post to determine this.

## What

Add per-user like tracking to `Feed.Api` via a new `feedLikes` MongoDB collection.
When processing `LikeAdded` events, insert a `FeedLikeDocument { UserId, PostId }`.
When processing `LikeRemoved` events, delete the matching document.
In `GET /api/feed`, look up which of the returned post IDs the current user has liked
and set `LikedByMe` accordingly on each `FeedEntryDto`.

## Rules

- `LikedByMe` defaults to `false` for unauthenticated callers and for the
  `GET /api/feed/users/{userId}` endpoint (reader identity is not required there).
- Only the `GET /api/feed` endpoint populates `LikedByMe` from the authenticated user.
- `FeedLikeDocument` deduplication is handled by `ReplaceOneAsync` with upsert on
  `(UserId, PostId)` so replayed events are idempotent.

## Affected Files

| File | Change |
|------|--------|
| `src/Feed.Api/Program.cs` | Add `FeedLikeDocument`; update `LikeAdded`/`LikeRemoved` handlers; update `GET /api/feed`; update `FeedEntryDto` |
| `tests/Integration.Tests/IntegrationFixture.cs` | Add `LikedByMe` to test `FeedEntryDto` |
| `tests/Integration.Tests/FeedApiTests.cs` | Tests for `LikedByMe` flag |
