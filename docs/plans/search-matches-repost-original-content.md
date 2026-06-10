# Search Also Matches Repost Original Content

## What

The reference's `MatchesContent` helper in `CosmosMongoPostGateway.Search` returns true if:
- The post's own content contains the query, **OR**
- The post is a repost (`OriginalPostId != null`) whose original post's content contains the query.

Our `GET /api/posts/search` only checks the post's own content. A repost with empty
or unrelated quote text will not be found even when the original post matches the query.

## Rule

When searching:
1. Find the IDs of all root posts (no `ParentPostId`, no `OriginalPostId`) whose content
   matches the query regex.
2. Build a combined filter: root posts where content matches **OR** `OriginalPostId` is in
   the set found in step 1.
3. Apply the combined filter, sort, skip/limit as before.

Implemented as two MongoDB queries to avoid loading all posts into memory.

## Affected Files

| File | Change |
|------|--------|
| `src/Post.Api/Program.cs` | Two-phase search in `GET /api/posts/search` |
| `tests/Integration.Tests/PostApiTests.cs` | Test that repost of matching original appears in results |
