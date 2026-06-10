# Block Users

## Source

`BlockUserPostsInteractor` in the clean-architecture reference. Social.Api already tracks follows; blocks follow the same pattern. Feed.Api already consumes follow events to filter the feed; it will consume block events the same way.

## What

- `POST /api/users/{blockedId}/blocks` — block a user (auth required)
- `DELETE /api/users/{blockedId}/blocks` — unblock (auth required, idempotent)
- `GET /api/users/{userId}/blocks` — list own blocks (auth required, own userId only)
- `UserBlocked` / `UserUnblocked` integration events flow to Feed.Api
- Feed.Api excludes blocked-user posts in both directions (I blocked them OR they blocked me)

## Affected Files

| File | Change |
|------|--------|
| `src/Shared.Contracts/Events/IntegrationEvents.cs` | Add `UserBlocked`, `UserUnblocked` events |
| `src/Social.Api/Program.cs` | `blocks` collection, 3 new endpoints, `BlockDocument` type |
| `src/Feed.Api/Program.cs` | `feedBlocks` collection, event handlers, feed query block filter |
| `tests/Integration.Tests/IntegrationFixture.cs` | Add `BlockedUserDto` record |
| `tests/Integration.Tests/SocialApiTests.cs` | Block CRUD + auth tests |
| `tests/Integration.Tests/FeedApiTests.cs` | Feed block-filter tests |

## Rules

- Self-block → `400`
- Duplicate block → idempotent `204`
- Unblock nonexistent → idempotent `204`
- Feed exclusion is mutual: A blocks B → B's posts disappear from A's feed AND A's posts disappear from B's feed
