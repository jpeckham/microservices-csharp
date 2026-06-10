# Social Graph Read Endpoints Require Authentication

## What

`GET /api/users/{userId}/followers`, `GET /api/users/{userId}/following`, and
`GET /api/users/{userId}/counts` are currently public. The reference requires
authentication for all user-data reads (the `ViewUser` handler calls
`ReadAuthenticatedUser` and returns `401` for unauthenticated callers).

Our write endpoints (`POST`/`DELETE` follows) already require auth. The read
endpoints should follow the same rule.

## Rule

- All three GET social-graph endpoints require a valid JWT.
- Unauthenticated requests → `401 Unauthorized`.
- Behavior for authenticated requests is unchanged.

## Affected Files

| File | Change |
|------|--------|
| `src/Social.Api/Program.cs` | Add `.RequireAuthorization()` to all three GET endpoints |
| `tests/Integration.Tests/SocialApiTests.cs` | Update `Follow_increments_follower_count` to pass auth token; add 401 tests |
