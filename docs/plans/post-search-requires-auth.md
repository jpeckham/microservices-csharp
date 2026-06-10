# Post Search Requires Authentication

## What

`GET /api/posts/search` currently allows unauthenticated access. The reference repo's `SearchPosts` handler calls `ReadAuthenticatedUser` first and returns `401 Unauthorized` if no valid session is found. Our endpoint is missing the `.RequireAuthorization()` call.

## Rule

- `GET /api/posts/search` must require a valid JWT.
- Unauthenticated requests return `401 Unauthorized`.
- All other behavior (empty query → 400, regex escaping, paging) is unchanged.

## Affected Files

| File | Change |
|------|--------|
| `src/Post.Api/Program.cs` | Add `.RequireAuthorization()` to `GET /api/posts/search` |
| `tests/Integration.Tests/PostApiTests.cs` | Update 4 existing search tests to pass auth token; add `SearchPosts_without_auth_returns_401` |
