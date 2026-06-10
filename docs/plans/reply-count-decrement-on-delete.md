# Feature: Decrement ReplyCount When a Reply Is Deleted

## Source
The reference decrements engagement counts symmetrically — every increment has a matching decrement.
`POST /api/posts/{postId}/replies` increments the parent's `ReplyCount` by 1, but
`DELETE /api/posts/{id}` only sets `IsDeleted = true` without decrementing `ReplyCount` on the
parent. Deleting a reply leaves the parent's reply count permanently inflated.

## What to add
In `DELETE /api/posts/{id}`, after soft-deleting the post, check if the deleted post has a
`ParentPostId`. If it does, decrement the parent's `ReplyCount` by 1 (floored at 0 via a
conditional update, matching the pattern used for `LikeRemoved` in Feed.Api).

## Affected Files

| File | Change |
|------|--------|
| `src/Post.Api/Program.cs` | After soft-delete, if `toDelete.ParentPostId.HasValue`, run a conditional `Inc(p.ReplyCount, -1)` on the parent |
| `tests/Integration.Tests/PostApiTests.cs` | New tests: reply count decrements when reply deleted, does not go below zero |
