# Hashtag Extraction

## What

When a post is created or updated in Post.Api, extract `#hashtag` tokens from the content and persist them alongside the post. The extracted hashtags are returned in the post DTO, enabling clients to display, link, and filter by hashtag without re-parsing content on the frontend.

This mirrors the hashtag extraction already present in the clean-architecture-csharp reference implementation (`SocialPost` entity).

## Rules

- A hashtag matches `#([a-zA-Z][a-zA-Z0-9_]*)` — must start with a letter, followed by alphanumeric or underscore characters.
- Extracted tags are stored **without** the `#` prefix, **lowercase**, **deduplicated**.
- Extraction runs on create and on every update (tags reflect current content).
- Posts with no hashtags return an empty list.

## Affected Files

| File | Change |
|------|--------|
| `src/Post.Api/Program.cs` | Add `Hashtags` to `PostDocument`, extract on create/update, include in `PostDto` |
| `tests/Integration.Tests/IntegrationFixture.cs` | Add `Hashtags` to the shared `PostDto` record |
| `tests/Integration.Tests/PostApiTests.cs` | Add hashtag extraction tests |

## Design

```
Content: "Loving #Blazor and #dotnet today! #Blazor rocks"
Hashtags: ["blazor", "dotnet"]   ← lowercase, deduplicated, no #
```

`PostDto` gains a `List<string> Hashtags` field (non-breaking addition; existing deserialization of records without the field will just default to null/empty).
