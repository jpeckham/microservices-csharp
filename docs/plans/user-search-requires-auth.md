# User Search Requires Authentication

## What

`GET /api/users/search` is currently public. The reference repo's `SearchUserInteractor`
is only ever invoked from authenticated contexts — the reference exposes all user-data
operations behind auth, treating user search as profile-data access rather than a
public discovery feature.

Our `GET /api/posts/search` already requires auth (added earlier). User search should
follow the same rule for consistency: any query that returns user profile data requires
a valid JWT.

## Rule

- `GET /api/users/search` requires a valid JWT.
- Unauthenticated requests → `401 Unauthorized`.
- All other behavior (400 for missing `q`, results on 200) is unchanged.

## Affected Files

| File | Change |
|------|--------|
| `src/Identity.Api/Program.cs` | Add `.RequireAuthorization()` to `GET /api/users/search` |
| `tests/Integration.Tests/IdentityApiTests.cs` | Add `UserSearch_without_auth_returns_401` and `UserSearch_with_auth_returns_results` tests |
