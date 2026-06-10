# Reply to Post

## What

The reference's `ReplyToPostInteractor` creates a new post with a `parentPostId`
linking it to the original. Our Post.Api has no reply concept — `PostDocument`
has no `ParentPostId` field and there is no reply endpoint.

The reference validates:
- Parent post must exist (and not be soft-deleted) → 404 otherwise.
- Reply content follows the same 1–280 character rule as normal posts.

## Rule

- `POST /api/posts/{postId}/replies` creates a new post with `ParentPostId` set to `postId`.
- If the parent post does not exist → `404 Not Found`.
- Content validation is identical to normal post creation (1–280 chars).
- Returns `201 Created` with the reply's `PostDto` (which now includes `ParentPostId`).
- Unauthenticated requests → `401 Unauthorized`.

## Affected Files

| File | Change |
|------|--------|
| `src/Post.Api/Program.cs` | Add `ParentPostId` to `PostDocument`; extend `PostDto`; add reply endpoint |
| `tests/Integration.Tests/IntegrationFixture.cs` | Add `ParentPostId` to test `PostDto` record |
| `tests/Integration.Tests/PostApiTests.cs` | Add reply tests |
