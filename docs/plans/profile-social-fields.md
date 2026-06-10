# Feature: Populate FollowerCount, FollowingCount, IsFollowedByMe on User Profile

## Source
The reference's `ViewUserInteractor` populates `FollowerCount`, `FollowingCount`, and
`IsFollowedByCurrentReader` on every `PostSummaryResponse` user-profile sub-object. Our
`UserProfileDto` already declares these fields but both profile endpoints (`GET /api/users/{id}`
and `GET /api/users/by-handle/{handle}`) hardcode them to `0`, `0`, and `false`.

## What to add
Have Identity.Api call Social.Api's existing endpoints when serving a profile:
- `GET /api/users/{userId}/counts` → `FollowerCount`, `FollowingCount`
- `GET /api/users/{requesterId}/is-following/{userId}` → `IsFollowedByMe`

If `Social:ApiUrl` is not configured, or if the call fails, degrade gracefully to `0`/`false`.
The `{id:guid}` endpoint also gains a `ClaimsPrincipal` parameter to enable `IsOwnProfile`.

## Affected Files

| File | Change |
|------|--------|
| `src/Identity.Api/Program.cs` | Register named `HttpClient` for Social.Api; add `FetchSocialAsync` helper; update both profile endpoints |
| `tests/Integration.Tests/Factories/IdentityApiFactory.cs` | Accept optional `SocialApiFactory` and wire its test handler |
| `tests/Integration.Tests/IntegrationFixture.cs` | Start Social factory first, pass to Identity factory |
| `tests/Integration.Tests/IdentityApiTests.cs` | Tests: follower count, following count, IsFollowedByMe true/false |
