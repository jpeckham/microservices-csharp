# Comment Author Can Delete Their Own Comment

## What

`Engagement.Api` exposes `POST /api/posts/{postId}/comments` and
`GET /api/posts/{postId}/comments` but no delete endpoint. The reference's
`DeletePostInteractor` enforces that only the resource's author can remove it.
Comments are user-owned resources and should follow the same rule.

## Rule

- `DELETE /api/posts/{postId}/comments/{commentId}` is a new, authenticated endpoint.
- If the comment does not exist → `404 Not Found`.
- If the authenticated user is not the comment's author → `403 Forbidden`.
- Otherwise → delete the comment, publish `CommentDeleted`, return `204 No Content`.

## Affected Files

| File | Change |
|------|--------|
| `src/Shared.Contracts/Events/IntegrationEvents.cs` | Add `CommentDeleted` event |
| `src/Engagement.Api/Program.cs` | Add `DELETE /api/posts/{postId}/comments/{commentId}` |
| `tests/Integration.Tests/EngagementApiTests.cs` | Add four new tests |
