# Reply Count on Post

## What

The reference's `PostSummaryResponse` includes `ReplyCount` (int). Our `PostDto` has no such field,
so callers cannot tell how many replies a post has without fetching the full reply list.

## Rule

- `PostDocument` gains an `int ReplyCount { get; set; }` field (default 0).
- When `POST /api/posts/{postId}/replies` succeeds, atomically increment the parent's `ReplyCount`
  using a MongoDB `$inc` update alongside the reply insert.
- `PostDto` exposes `int ReplyCount` so the value flows to callers.

## Affected Files

| File | Change |
|------|--------|
| `src/Post.Api/Program.cs` | Add `ReplyCount` to `PostDocument` and `PostDto`; increment parent on reply creation |
| `tests/Integration.Tests/PostApiTests.cs` | Add tests verifying count increments |
