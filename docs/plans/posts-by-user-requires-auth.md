# Posts By User Requires Authentication

## What

`GET /api/posts/by-user/{userId}` is currently public. In the reference repo the equivalent functionality (viewing a user's posts) is part of `GET /api/users/{handle}` (the `ViewUser` endpoint), which calls `ReadAuthenticatedUser` and returns `401 Unauthorized` for unauthenticated requests.

Our endpoint exposes all posts for any user without requiring a login. Making it auth-required aligns with the reference's principle that reading post content requires an authenticated session.

## Rule

- `GET /api/posts/by-user/{userId}` must require a valid JWT.
- Unauthenticated requests return `401 Unauthorized`.
- All other behaviour (paging, sorting) is unchanged.

## Affected Files

| File | Change |
|------|--------|
| `src/Post.Api/Program.cs` | Add `.RequireAuthorization()` to the `by-user` endpoint |
| `tests/Integration.Tests/PostApiTests.cs` | Add `GetPostsByUser_without_auth_returns_401` and `GetPostsByUser_with_auth_returns_posts` |
