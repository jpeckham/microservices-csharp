# Mention Extraction

## What

When a post is created or updated in Post.Api, extract `@mention` tokens from the content and persist them alongside the post. The extracted mentions are returned in the post DTO, enabling clients to display linked profiles and notify mentioned users without re-parsing content.

This mirrors the mention extraction already present in the clean-architecture-csharp reference implementation (`SocialPost` entity), and is the natural complement to the hashtag extraction added in the previous iteration.

## Rules

- A mention matches `@([a-zA-Z][a-zA-Z0-9_]*)` — must start with a letter after the @, followed by alphanumeric or underscore characters.
- Extracted handles are stored **without** the `@` prefix, **lowercase**, **deduplicated**.
- Extraction runs on create and on every update (mentions reflect current content).
- Posts with no mentions return an empty list.
- The post author's own handle is not excluded — self-mentions are valid.

## Affected Files

| File | Change |
|------|--------|
| `src/Post.Api/Program.cs` | Add `Mentions` to `PostDocument`, extract on create/update, include in `PostDto` |
| `tests/Integration.Tests/IntegrationFixture.cs` | Add `Mentions` to the shared `PostDto` record |
| `tests/Integration.Tests/PostApiTests.cs` | Add mention extraction tests |
| `src/Social.Web.Client/Services/ApiClient.cs` | Add `Mentions` to client-side `PostDto` |

## Design

```
Content: "Hey @Alice and @bob, check this out! @Alice rocks"
Mentions: ["alice", "bob"]   ← lowercase, deduplicated, no @
```

`PostDto` gains a `List<string> Mentions` field alongside the existing `Hashtags` field.
