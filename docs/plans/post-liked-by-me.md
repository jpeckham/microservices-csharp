# LikedByMe on PostDto

## Source

The reference's `PostSummaryResponse` includes `LikedByCurrentReader` — whether the
authenticated caller has liked a specific post. The reference computes this from the
post entity's embedded `_likedBy` HashSet. Our `PostDto.LikedByMe` currently always
returns `false`, so clients fetching `GET /api/posts/{id}` can't render like-button
state without a separate round-trip to Engagement.Api.

## What

Track per-user likes in Post.Api via a new `postLikes` MongoDB collection (same
pattern as `FeedLikeDocument` in Feed.Api). Populate it from the existing
`/events/LikeAdded` and `/events/LikeRemoved` endpoints already on Post.Api.
For authenticated requests, batch-query `postLikes` and set `LikedByMe` on
each `PostDto`.

## Rules

- `LikedByMe` defaults to `false` for unauthenticated callers.
- All authenticated post-returning endpoints populate `LikedByMe` for the caller.
- Upsert on `(UserId, PostId)` ensures replay-idempotency.

## Affected Files

| File | Change |
|------|--------|
| `src/Post.Api/Program.cs` | Add `PostLikeDocument`; register collection; update event handlers; update all post-returning endpoints; add `LikedByMe` to `PostDto`; update `ToDto` |
| `tests/Integration.Tests/IntegrationFixture.cs` | Add `LikedByMe` to test `PostDto` (already `int LikeCount = 0` so just add `bool LikedByMe = false`) |
| `tests/Integration.Tests/PostApiTests.cs` | Tests for `LikedByMe` on fetched posts |
