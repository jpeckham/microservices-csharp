# Recent Replies Embedded on GET /api/posts/{id}

## Source

`PostSummaryProjection.ToSummary` in the reference always includes up to 3 recent
replies (`RecentReplyLimit = 3`) embedded directly in the post response.
`ToFocusedConversationSummary` (used by `DisplayPostInteractor`) uses `replyLimit: 100`
and `replyDepth: 3`. Our `GET /api/posts/{id}` returns the post itself only; replies
must be fetched separately via `GET /api/posts/{id}/replies`.

## What

Add `List<PostDto>? RecentReplies = null` to `PostDto`. Populate it in
`GET /api/posts/{id}` with up to 3 most recent direct replies (by `PostedAt` desc),
each mapped to a flat `PostDto` (no nested sub-replies).

## Rules

- At most 3 replies embedded (matching the reference's `RecentReplyLimit`).
- Replies are ordered newest-first to match `RecentReplies` in the reference.
- Embedded reply DTOs do NOT include their own `RecentReplies` (no recursion).
- Embedded reply DTOs DO include their `ReplyTarget` (which is the current post).
- All other endpoints (`by-user`, `search`, `recent`, feed) are unchanged — they
  don't embed replies (keeping scope small).

## Affected Files

| File | Change |
|------|--------|
| `src/Post.Api/Program.cs` | Add `RecentReplies` to `PostDto`; populate in `GET /api/posts/{id}` |
| `tests/Integration.Tests/IntegrationFixture.cs` | Add `RecentReplies` to test `PostDto` |
| `tests/Integration.Tests/PostApiTests.cs` | Tests for embedded replies |
