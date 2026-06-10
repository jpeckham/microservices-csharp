# User Profile: Embedded Recent Posts

## Source

The reference's `GET /api/users/{handle}` returns a `UserProfileResult` with an
embedded `Posts` list — the user's recent posts, each with `LikeCount`,
`LikedByCurrentReader`, `RepostCount`, and `RepostedByCurrentReader`. Our
`UserProfileDto` has none of these fields, so callers viewing a profile must make
a separate round-trip to Post.Api.

## What

Add `List<PostDto>? RecentPosts` to `UserProfileDto` in Identity.Api. The profile
endpoints (`/api/users/me`, `/api/users/{id}`, `/api/users/by-handle/{handle}`)
call Post.Api's existing `GET /api/posts/by-user/{userId}` and embed the results.

The cross-service call follows the same pattern as Social.Api: a named `PostApi`
HttpClient is registered in DI; `IdentityApiFactory` wires the test server handler.

## Rules

- Populated only when the `PostApi` client has a non-null `BaseAddress` (graceful
  degradation when Post.Api is not deployed alongside Identity.Api).
- The bearer token from the inbound request is forwarded so `LikedByMe` and
  `RepostedByMe` reflect the requesting user.
- `RecentPosts` defaults to `null` / empty list when Post.Api is unavailable.

## Affected Files

| File | Change |
|------|--------|
| `src/Identity.Api/Program.cs` | Register `PostApi` HttpClient; add `FetchPostsAsync`; update `UserProfileDto`; update `ToProfile`; update 3 profile endpoints |
| `tests/Integration.Tests/Factories/IdentityApiFactory.cs` | Accept `PostApiFactory?` parameter and wire handler |
| `tests/Integration.Tests/IntegrationFixture.cs` | Pass `_postFactory` to `IdentityApiFactory`; add `RecentPosts` to test `UserProfileDto` |
| `tests/Integration.Tests/IdentityApiTests.cs` | Tests verifying embedded posts appear on profile |
