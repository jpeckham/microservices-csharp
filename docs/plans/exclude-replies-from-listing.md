# Exclude Replies from Search and Recent Posts

## What

The reference's `Search` and `ScrollFor` (recent posts) both filter to
`p.ParentPostId is null`, so replies never appear in listing or search
results. Our `GET /api/posts/search` and `GET /api/posts/recent` currently
return all post types including replies.

## Rule

- `GET /api/posts/search`: add `p.ParentPostId == null` to the filter so only
  root posts (including reposts) are returned.
- `GET /api/posts/recent`: add `p.ParentPostId == null` to the filter so only
  root posts (including reposts) are returned.

## Affected Files

| File | Change |
|------|--------|
| `src/Post.Api/Program.cs` | Add `ParentPostId == null` filter to both endpoints |
| `tests/Integration.Tests/PostApiTests.cs` | Add tests verifying replies are excluded |
