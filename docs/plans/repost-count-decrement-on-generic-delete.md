# Feature: Decrement RepostCount When a Repost Is Deleted via Generic Delete

## Source
`DELETE /api/posts/{postId}/reposts/mine` correctly decrements the original post's `RepostCount`.
But `DELETE /api/posts/{id}` (the generic post delete) does not. A user who deletes their repost
post directly leaves the original's `RepostCount` permanently inflated.

## What to add
In `DELETE /api/posts/{id}`, after soft-deleting and handling the `ParentPostId` (reply) case,
also check `OriginalPostId`. If present, decrement the original's `RepostCount` by 1 floored at 0,
matching the pattern used by `DELETE /api/posts/{postId}/reposts/mine`.

## Affected Files

| File | Change |
|------|--------|
| `src/Post.Api/Program.cs` | After ParentPostId guard, add OriginalPostId guard to decrement original's RepostCount |
| `tests/Integration.Tests/PostApiTests.cs` | New test: deleting a repost via generic delete decrements original's RepostCount |
