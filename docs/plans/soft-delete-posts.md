# Soft Delete Posts

## What

The reference uses soft delete for posts: `DELETE /api/posts/{id}` marks the document
with `IsDeleted = true` rather than removing it from the collection.  All read queries
filter to `IsDeleted != true`, and `GET /api/posts/{id}` returns 404 for deleted posts.

Our implementation uses `FindOneAndDeleteAsync`, which permanently removes the document.
This means:

- A client that holds a post ID has no way to know whether the post existed and was deleted
  vs. never existed.
- A reply to a parent post that has since been deleted succeeds silently (we just look up
  the parent; if it's gone we return 404, but only because the parent isn't there at all).

## Rule

1. Add `IsDeleted` (bool, default `false`) to `PostDocument`.
2. `DELETE /api/posts/{id}` → `UpdateOne` sets `IsDeleted = true` (still requires caller
   to be the author; still returns 403 if not the author).
3. All read queries add `p.IsDeleted != true`:
   - `GET /api/posts/{id}` — return 404 if deleted.
   - `GET /api/posts/recent` — exclude deleted posts.
   - `GET /api/posts/search` — exclude deleted posts.
   - `GET /api/posts/{postId}/replies` — exclude deleted replies.
   - `POST /api/posts/{postId}/replies` — return 404 if parent is deleted.
   - `POST /api/posts/{postId}/reposts` — return 404 if original is deleted.
   - `DELETE /api/posts/{postId}/reposts/mine` — look for active (non-deleted) repost.

## Affected Files

| File | Change |
|------|--------|
| `src/Post.Api/Program.cs` | Add `IsDeleted`, change delete, filter all reads |
| `tests/Integration.Tests/PostApiTests.cs` | Tests for soft-delete behaviour |
