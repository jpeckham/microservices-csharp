# Comment Content Max Length (280 chars)

## What

Enforce a 280-character maximum on comment content in Engagement.Api. Currently only a non-empty check is applied — a 50 000-character comment would be accepted and stored.

Mirrors the constraint in the clean-architecture-csharp reference implementation where "replies" are modelled as posts (`SocialPost.ReplyTo`) and therefore inherit the same `Validate` guard that limits content to 280 characters.

## Rule

After trimming whitespace, comment content must be ≤ 280 characters. Returns `400 Bad Request` with `{ "error": "Comment content must be 280 characters or fewer." }` if exceeded.

## Affected Endpoints

| Endpoint | Change |
|----------|--------|
| `POST /api/posts/{postId}/comments` | Add max-length check after the empty check |

## Affected Files

| File | Change |
|------|--------|
| `src/Engagement.Api/Program.cs` | One validation guard |
| `tests/Integration.Tests/EngagementApiTests.cs` | 2 new tests |
