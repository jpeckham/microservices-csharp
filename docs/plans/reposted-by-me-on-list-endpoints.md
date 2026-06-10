# RepostedByMe on List Endpoints

## Source

The reference's `Authenticated_user_can_create_and_delete_quote_repost_through_api`
test calls `GET /api/posts/recent` and asserts `RepostedByCurrentReader == true`
for a post the caller has reposted. Our single-post endpoint (`GET /api/posts/{id}`)
correctly computes `RepostedByMe`, but all list endpoints always return `false`.

## What

Add a `LoadRepostedIds` helper (same pattern as the existing `LoadLikedIds`) and
wire it into all list endpoints that return `PostDto`:

- `GET /api/posts/recent`
- `GET /api/posts/by-user/{userId}`
- `GET /api/posts/search`
- `GET /api/posts/{postId}/replies`

## Rules

- `RepostedByMe` is `false` for unauthenticated callers (defaults via missing JWT).
- The helper batch-queries the `posts` collection for reposts owned by the caller
  with `OriginalPostId` in the requested set.
- Repost documents have `IsDeleted = false` (a deleted repost is not active).

## Affected Files

| File | Change |
|------|--------|
| `src/Post.Api/Program.cs` | Add `LoadRepostedIds`; update 4 list endpoints |
| `tests/Integration.Tests/PostApiTests.cs` | Tests for `RepostedByMe` on each list endpoint |
