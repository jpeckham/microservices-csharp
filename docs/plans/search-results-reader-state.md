# Search Results Include Reader State (LikedByMe)

## Source

The reference's `Search_posts_returns_matching_content_with_reader_state` test explicitly
verifies that `GET /api/posts/search` returns posts with `LikeCount` and
`LikedByCurrentReader` correctly set for the authenticated caller. The reference
treats this as a first-class requirement alongside matching content.

Our implementation already calls `LoadLikedIds` in the search endpoint (added as part
of the LikedByMe work), but no integration test verifies this end-to-end.

## What

Add integration tests that confirm search results carry accurate reader state:
- `LikedByMe` is `true` when the authenticated caller has liked a returned post.
- `LikedByMe` is `false` when the caller has not liked it.
- `LikeCount` reflects the correct total across all users.

## Rules

- `LikedByMe` reflects the authenticated caller (JWT principal), not any
  `readerHandle` or other parameter.
- Liking a post and then searching for it returns the updated state in the same call.

## Affected Files

| File | Change |
|------|--------|
| `tests/Integration.Tests/PostApiTests.cs` | Add `SearchPosts_LikedByMe_*` tests |
