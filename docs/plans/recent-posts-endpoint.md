# Recent Posts Endpoint

## What

Add `GET /api/posts/recent?limit=N` to Post.Api — a global feed of the most recent posts, sorted newest-first, requiring authentication.

The reference repo exposes this as `GET /api/posts/recent` (see `SocialAppSliceEndpoints.RecentPosts`). Our Post.Api currently only has `GET /api/posts/by-user/{userId}` for reading posts, so there is no way to retrieve a global feed.

## Rule

- Requires a valid JWT (`RequireAuthorization`).
- `limit` query parameter: integer, default 20, clamped to 1–100.
- Returns posts sorted by `PostedAt` descending.
- Response body reuses the existing `PostDto` list shape.

## Affected Files

| File | Change |
|------|--------|
| `src/Post.Api/Program.cs` | New `GET /api/posts/recent` endpoint |
| `tests/Integration.Tests/PostApiTests.cs` | Tests: returns 401 unauthenticated, returns posts sorted newest-first, limit is clamped |
