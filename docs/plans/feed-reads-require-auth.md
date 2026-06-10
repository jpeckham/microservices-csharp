# Feed Read Endpoints Require Authentication

## What

`GET /api/feed` and `GET /api/feed/users/{userId}` in Feed.Api have no
`.RequireAuthorization()`. Every other GET endpoint across all services was
secured in earlier iterations; these two were missed.

The reference requires an authenticated session for all user-data reads. Without
auth on the feed, any anonymous caller can read the entire public feed.

## Rule

- `GET /api/feed` → requires JWT.
- `GET /api/feed/users/{userId}` → requires JWT.
- Unauthenticated requests → `401 Unauthorized`.
- Authenticated behavior is unchanged.

## Affected Files

| File | Change |
|------|--------|
| `src/Feed.Api/Program.cs` | Add `.RequireAuthorization()` to both GET endpoints |
| `tests/Integration.Tests/FeedApiTests.cs` | Update existing tests to pass auth token; add 401 tests |
