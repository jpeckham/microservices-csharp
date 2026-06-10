# Engagement Read Endpoints Require Authentication

## What

`GET /api/posts/{postId}/comments` and `GET /api/posts/{postId}/summary` are
currently public. In the reference repo both pieces of data (replies and engagement
counts) are returned as part of `GET /api/posts/{postId}`, which calls
`ReadAuthenticatedUser` and returns `401` for unauthenticated callers.

Additionally, the summary endpoint already accepts an optional `ClaimsPrincipal`
to compute `LikedByMe`, but because no auth is enforced, `LikedByMe` is always
`false` for any unauthenticated client. Requiring auth makes `LikedByMe` accurate.

## Rule

- Both read endpoints require a valid JWT.
- Unauthenticated requests → `401 Unauthorized`.
- `LikedByMe` in the summary now reflects the authenticated caller's actual like state.

## Affected Files

| File | Change |
|------|--------|
| `src/Engagement.Api/Program.cs` | Add `.RequireAuthorization()` to both GET endpoints |
| `tests/Integration.Tests/EngagementApiTests.cs` | Update 2 existing tests to send bearer token; add 3 new tests |
