# User Profile Lookup Requires Authentication

## What

`GET /api/users/by-handle/{handle}` and `GET /api/users/{id:guid}` are currently
public. The reference repo's `ViewUser` handler calls `ReadAuthenticatedUser` and
returns `401 Unauthorized` for unauthenticated callers, enforcing the principle that
profile data is only visible to logged-in users.

## Rule

- `GET /api/users/by-handle/{handle}` requires a valid JWT.
- `GET /api/users/{id:guid}` requires a valid JWT.
- Unauthenticated requests → `401 Unauthorized`.
- All other behavior (404 for unknown handle/id, profile data returned on 200) is unchanged.

## Affected Files

| File | Change |
|------|--------|
| `src/Identity.Api/Program.cs` | Add `.RequireAuthorization()` to both GET lookup endpoints |
| `tests/Integration.Tests/IdentityApiTests.cs` | Update 1 existing test to send bearer token; add 2 new 401 tests |
