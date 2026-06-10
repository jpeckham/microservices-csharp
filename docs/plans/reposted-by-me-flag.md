# Feature: RepostedByMe on GET /api/posts/{id}

## Source
`clean-architecture-csharp` exposes `RepostedByCurrentReader: bool` on every `PostSummaryResponse`.  
`microservices-csharp` has no equivalent — callers cannot tell whether the authenticated user has already reposted a given post without a separate round-trip or client-side state.

## What to add
Add a `RepostedByMe` bool to `PostDto` in **Post.Api**.  
Populate it on `GET /api/posts/{id}` by querying whether a non-deleted post exists with `OriginalPostId == id` and `AuthorId == requestingUserId`.

List endpoints (`/recent`, `/search`, `/by-user`) keep `RepostedByMe = false` — the extra per-item queries are not worth it for browse views.

## Why the single-post endpoint matters
The primary consumer is the post-detail view: before showing a "Repost" or "Undo Repost" button the client needs this bit.  
The repost/delete-repost endpoints already return 409/404 to guard against double-repost, but the UI needs the flag up front.

## Implementation
1. Add `bool RepostedByMe = false` to `PostDto` record in `Post.Api/Program.cs`
2. Update `ToDto` to accept and forward `repostedByMe`
3. In `GET /api/posts/{id}`: after loading the post, run one extra `AnyAsync` query scoped to the requesting user
4. Update `PostDto` in `IntegrationFixture.cs` to match

## Tests
- `GetPost_returns_reposted_by_me_false_when_not_reposted`
- `GetPost_returns_reposted_by_me_true_after_reposting`
- `GetPost_returns_reposted_by_me_false_after_deleting_repost`
- `GetPost_reposted_by_me_is_false_for_creator` (can never repost own post)
