# Reply Target Embedded on Post Response

## Source

`PostSummaryProjection.ToSummary` in the clean-architecture reference. When a post has a
`ParentPostId`, the projection fetches the parent and includes it as a `replyTarget`
(`QuotedPostSummaryResponse`) alongside the post. Our API returns only the bare `ParentPostId`.

## What

Add `ReplyTarget` (optional, same shape as `QuotedPostDto`) to `PostDto`. When a post is a
reply (`ParentPostId` is set), fetch and embed the parent post's basic fields so clients can
display conversation context without a second round-trip.

Applies to:
- `GET /api/posts/{id}` — single post, always embed reply target when applicable
- `GET /api/posts/by-user/{userId}` — batch-load parents for all replies in the listing

## Affected Files

| File | Change |
|------|--------|
| `src/Post.Api/Program.cs` | Add `ReplyTarget` to `PostDto`; update `ToDto`; add `LoadParents` helper; update two endpoints |
| `tests/Integration.Tests/IntegrationFixture.cs` | Add `ReplyTarget` to test `PostDto` record |
| `tests/Integration.Tests/PostApiTests.cs` | Tests that reply target is populated correctly |
