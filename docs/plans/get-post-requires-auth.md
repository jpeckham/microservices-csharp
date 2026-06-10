# GET /api/posts/{id} Requires Authentication

## What

`GET /api/posts/{id:guid}` is currently public. In the reference repo the `GetPost` handler calls `ReadAuthenticatedUser` first and returns `401 Unauthorized` for unauthenticated callers.

Our endpoint lets anyone read any post without a login. Making it auth-required aligns with the reference's principle that all post-reading operations require an authenticated session.

## Rule

- `GET /api/posts/{id:guid}` must require a valid JWT.
- Unauthenticated requests return `401 Unauthorized`.
- All other behaviour (404 for unknown ID, post content returned on 200) is unchanged.

## Affected Files

| File | Change |
|------|--------|
| `src/Post.Api/Program.cs` | Add `.RequireAuthorization()` to `GET /api/posts/{id:guid}` |
| `tests/Integration.Tests/PostApiTests.cs` | Update 5 tests that call the endpoint without auth; add `GetPost_without_auth_returns_401` |
