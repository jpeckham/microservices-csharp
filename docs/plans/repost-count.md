# Repost Count on Post

## What

The reference's `PostSummaryResponse` includes `RepostCount` (computed via
`posts.CountActiveReposts(repostTargetId)`). Our `PostDto` has no such field,
so callers cannot tell how many times a post has been reposted without a
separate query.

## Rule

- `PostDocument` gains an `int RepostCount { get; set; }` field (default 0).
- When `POST /api/posts/{postId}/reposts` succeeds, atomically increment the
  root post's `RepostCount` via MongoDB `$inc`.
- When `DELETE /api/posts/{postId}/reposts/mine` succeeds, atomically decrement
  the root post's `RepostCount` (floor 0 via `$max` or conditional update).
- `PostDto` exposes `int RepostCount` so the value flows to callers.

## Affected Files

| File | Change |
|------|--------|
| `src/Post.Api/Program.cs` | Add `RepostCount` to `PostDocument` and `PostDto`; inc/dec on repost create/delete |
| `tests/Integration.Tests/PostApiTests.cs` | Add tests verifying count increments/decrements |
