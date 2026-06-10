# Nested Reply Tree on GET /api/posts/{id}

## Source

The reference's `Display_post_limits_conversation_tree_to_depth_three_from_selected_post`
test calls `DisplayPostInteractor` and asserts that the resulting post has three levels
of nested `RecentReplies`:

```
post.RecentReplies[level1]
  └── level1.RecentReplies[level2]
        └── level2.RecentReplies[level3]
              └── level3.RecentReplies = [] (depth limit)
```

Our `GET /api/posts/{id}` fetches the immediate replies but does not populate
`RecentReplies` on those replies — the nesting depth is always 0.

The companion test `Display_post_includes_parent_context_for_reply_and_nested_replies_from_selected_post`
also checks that a reply's `ReplyTarget` and its own `RecentReplies` are populated.

## What

After fetching immediate (level-1) replies in `GET /api/posts/{id}`:
1. Batch-fetch level-2 replies with a single `$in` query against all level-1 IDs.
2. Batch-fetch level-3 replies with a single `$in` query against all level-2 IDs.
3. Build `PostDto` tree bottom-up: level-3 ← level-2 ← level-1.
4. Level-3 posts get empty `RecentReplies` (depth limit enforced by not querying level-4).

Two extra DB round-trips total regardless of how wide the reply tree is.

## Rules

- Each level is sorted descending by `PostedAt` and limited to 3.
- Deleted posts are excluded at every level.
- The feature only applies to `GET /api/posts/{id}`; list endpoints are unaffected.

## Affected Files

| File | Change |
|------|--------|
| `src/Post.Api/Program.cs` | Update `GET /api/posts/{id}` to fetch and nest replies 3 levels deep |
| `tests/Integration.Tests/PostApiTests.cs` | Tests for nested reply tree and depth limit |
