# Delete My Repost

## What

`POST /api/posts/{postId}/reposts` was added to create reposts but there is no
way to undo one. The reference exposes `DELETE /api/posts/{postId}/reposts/mine`
which lets the caller remove their own repost of a given post.

The reference resolves the target to the canonical root post (same as on create),
then finds and soft-deletes the caller's repost. If no repost exists, it returns
404.

## Rule

- `DELETE /api/posts/{postId}/reposts/mine` removes the authenticated user's
  repost of the canonical post identified by `postId`.
- `postId` may be the original post or an existing repost — both resolve to the
  root `OriginalPostId ?? Id`.
- If the caller has no active repost of that root post → `404 Not Found`.
- Unauthenticated → `401 Unauthorized`.
- On success → `204 No Content` + publishes `PostDeleted` event.

## Affected Files

| File | Change |
|------|--------|
| `src/Post.Api/Program.cs` | Add `DELETE /api/posts/{postId}/reposts/mine` |
| `tests/Integration.Tests/PostApiTests.cs` | Add tests |
