# List Replies for a Post

## What

`POST /api/posts/{postId}/replies` was added to create replies, but there is no
read endpoint. The reference's `DisplayPostInteractor` fetches recent replies and
embeds them in the focused-conversation view. Without a query path, the stored
`ParentPostId` is write-only and clients cannot render threads.

## Rule

- `GET /api/posts/{postId}/replies` returns all posts with `ParentPostId == postId`
  sorted ascending by `PostedAt` (oldest first, conversation order).
- Requires authentication.
- Returns an empty array (not 404) when the parent has no replies.
- Paginates via optional `limit` (default 20, max 100) and `offset` (default 0).

## Affected Files

| File | Change |
|------|--------|
| `src/Post.Api/Program.cs` | Add `GET /api/posts/{postId}/replies` endpoint |
| `tests/Integration.Tests/PostApiTests.cs` | Add tests |
