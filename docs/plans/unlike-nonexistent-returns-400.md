# Unlike a Post You Haven't Liked Returns 400

## What

`DELETE /api/posts/{postId}/likes` currently returns `200 OK` (with the unchanged
like count) when the caller has not previously liked that post. The reference repo's
`DeleteLikeFromPostInteractor` throws `InvalidOperationException("Cannot delete a like
that does not exist.")` which the endpoint catches and maps to `400 Bad Request`.

## Rule

- If the authenticated caller has not liked the post, `DELETE /api/posts/{postId}/likes`
  returns `400 Bad Request` with `{ "error": "You have not liked this post." }`.
- If the caller has liked the post, behavior is unchanged: delete the like, publish the
  event, return `200 OK` with the updated like count.

## Affected Files

| File | Change |
|------|--------|
| `src/Engagement.Api/Program.cs` | Check for existing like before deleting; return 400 if none found |
| `tests/Integration.Tests/EngagementApiTests.cs` | Add `UnlikePost_not_liked_returns_400` test |
