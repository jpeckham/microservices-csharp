# LikeCount on PostDto

## Source

The reference's `PostSummaryResponse` includes `LikeCount` as a first-class field on
every post response. Our `PostDto` (Post.Api) exposes `ReplyCount` and `RepostCount`
but has no `LikeCount` — like counts only exist in `FeedEntryDocument` (Feed.Api).
This means `GET /api/posts/{id}` and `GET /api/posts` never tell clients how many
likes a post has received.

## What

Track `LikeCount` on `PostDocument` in Post.Api by consuming `LikeAdded` and
`LikeRemoved` events via the existing `/events/*` HTTP endpoints.  Expose the
count through `PostDto`.

## Rules

- `LikeAdded` event increments `PostDocument.LikeCount` for the matching post.
- `LikeRemoved` event decrements only when `LikeCount > 0` (floor at 0, idempotent).
- `PostDto.LikeCount` defaults to `0`.

## Affected Files

| File | Change |
|------|--------|
| `src/Post.Api/Program.cs` | Add `LikeCount` to `PostDocument`; update `PostDto`; add `/events/LikeAdded` and `/events/LikeRemoved` endpoints; update `ToDto` |
| `tests/Integration.Tests/IntegrationFixture.cs` | Add `LikeCount` to test `PostDto` |
| `tests/Integration.Tests/PostApiTests.cs` | Tests for LikeCount on posts |
