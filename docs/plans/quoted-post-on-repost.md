# Quoted Post Embedded in Repost Response

## What

The reference's `PostSummaryResponse` includes a `QuotedPost` field that carries the
original post's handle, content, and creation time inline. Our `PostDto` only has
`OriginalPostId`, forcing clients to make a second request to display the original.

## Rule

Add a nullable `QuotedPostDto` to `PostDto`. Populate it when the post has an
`OriginalPostId`:

- `GET /api/posts/{id}` — one extra query for the original.
- `GET /api/posts/recent`, `GET /api/posts/search`, `GET /api/posts/by-user/{userId}`,
  `GET /api/posts/{postId}/replies` — batch-load all distinct originals in one query,
  then join in memory. Zero extra round-trips per item.

`QuotedPostDto` carries: `PostId`, `AuthorHandle`, `AuthorDisplayName`, `Content`,
`PostedAt`. Deleted originals are omitted (null).

## Affected Files

| File | Change |
|------|--------|
| `src/Post.Api/Program.cs` | New `QuotedPostDto` record; update `ToDto`; add lookup logic in each endpoint |
| `tests/Integration.Tests/IntegrationFixture.cs` | Add `QuotedPostDto` and `QuotedPost` to fixture `PostDto` |
| `tests/Integration.Tests/PostApiTests.cs` | New tests for quoted post presence and content |
