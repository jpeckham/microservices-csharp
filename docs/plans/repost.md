# Repost (Quote/Boost) a Post

## What

The reference's `RepostInteractor` lets a user reshare an existing post with an
optional comment. Two business rules make it distinct from a plain post:

1. **Cannot repost own post** — throws `InvalidOperationException` → `409 Conflict`.
2. **Cannot repost the same post twice** — throws `InvalidOperationException` → `409 Conflict`.
3. **Reposting a repost resolves to the root original post** — so the chain
   never goes deeper than one level.

Our Post.Api has no repost endpoint and `PostDocument` has no `OriginalPostId` field.

## Rule

- `POST /api/posts/{postId}/reposts` creates a new post with `OriginalPostId` set.
- If the target post is itself a repost, `OriginalPostId` is set to the root
  (`targetPost.OriginalPostId ?? targetPost.Id`).
- If the caller is the root post's author → `409 Conflict`.
- If the caller already has an active repost of the root post → `409 Conflict`.
- Target post not found → `404 Not Found`.
- Returns `201 Created` with the new repost's `PostDto`.

## Affected Files

| File | Change |
|------|--------|
| `src/Post.Api/Program.cs` | Add `OriginalPostId` to `PostDocument`; extend `PostDto`; add repost endpoint |
| `tests/Integration.Tests/IntegrationFixture.cs` | Add `OriginalPostId` to test `PostDto` record |
| `tests/Integration.Tests/PostApiTests.cs` | Add repost tests |
