# Feature: Populate FollowerCount and FollowingCount on GET /api/users/me

## Source
The previous iteration updated `GET /api/users/{id}` and `GET /api/users/by-handle/{handle}`
to call Social.Api and return real follower/following counts and `IsFollowedByMe`. But
`GET /api/users/me` was missed — it still hardcodes `followerCount=0, followingCount=0`.
The reference always returns live counts on every profile endpoint.

## What to add
Update `GET /api/users/me` to inject `HttpContext` and `IHttpClientFactory`, extract the
bearer token, and call `FetchSocialAsync` just like the other two profile endpoints.
`IsFollowedByMe` is correctly false (user can't follow themselves — `FetchSocialAsync`
already skips the is-following check when `requesterId == userId`).

## Affected Files

| File | Change |
|------|--------|
| `src/Identity.Api/Program.cs` | Inject `HttpContext` + `IHttpClientFactory` into `/api/users/me`; call `FetchSocialAsync` |
| `tests/Integration.Tests/IdentityApiTests.cs` | Test that `/api/users/me` returns correct FollowerCount after being followed |
