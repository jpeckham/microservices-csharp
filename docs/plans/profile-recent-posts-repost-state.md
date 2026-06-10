# Profile Recent Posts Include RepostedByMe State

## Source

The reference's `View_user_returns_display_name_handle_and_recent_posts_for_authenticated_reader`
test (SocialAppApiSliceTests.cs, line 338–373) calls `GET /api/users/@ada` as user Grace after
Grace has both liked and reposted Ada's post, then asserts:

```
p.LikeCount == 1 && p.LikedByCurrentReader &&
p.RepostCount == 1 && p.RepostedByCurrentReader
```

Our existing `GetProfile_recent_posts_include_like_count_and_liked_by_me` (IdentityApiTests.cs)
covers `LikedByMe` but has no counterpart verifying `RepostedByMe` on profile posts.

## What

The implementation already plumbs `RepostedByMe` through the full pipeline:
- `PostSummaryDto` has `RepostedByMe = false` as a default
- `FetchPostsAsync` passes the caller's bearer token to Post.Api
- Post.Api's `GET /api/posts/by-user/{userId}` calls `LoadRepostedIds`

The gap is purely a missing integration test.

## Test to Add

`GetProfile_recent_posts_include_repost_count_and_reposted_by_me`:
1. `author` creates a post.
2. `reposter` reposts it via `POST /api/posts/{id}/reposts`.
3. `reposter` calls Identity.Api `GET /api/users/by-handle/{author.Handle}`.
4. Assert: the post appears in `RecentPosts` with `RepostCount == 1` and `RepostedByMe == true`.

## Affected Files

| File | Change |
|------|--------|
| `tests/Integration.Tests/IdentityApiTests.cs` | Add one test |
