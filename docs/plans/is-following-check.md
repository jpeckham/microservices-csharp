# Feature: GET /api/users/{followerId}/is-following/{targetId}

## Source
clean-architecture-csharp's `ViewUserInteractor` receives a `readerHandle` and can determine
whether the reader follows the viewed user. microservices-csharp has no endpoint to check
whether a specific user follows another — clients must fetch the full follower list and scan it.

## What to add
Add `GET /api/users/{followerId}/is-following/{targetId}` to **Social.Api**.
Returns `{ isFollowing: bool }`. Requires authentication.

## Implementation
1. Map a new `GET` endpoint on Social.Api
2. Query: `follows.Find(f => f.FollowerId == followerId && f.FollowingId == targetId).AnyAsync()`
3. Return `{ isFollowing: bool }`

## Tests
- `IsFollowing_returns_false_before_following`
- `IsFollowing_returns_true_after_following`
- `IsFollowing_returns_false_after_unfollowing`
- `IsFollowing_without_auth_returns_401`
- `IsFollowing_returns_false_for_self`
