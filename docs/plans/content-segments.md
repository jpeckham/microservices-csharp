# Content Segments on PostDto

## Source

The reference's `PostSummaryHttpResult` exposes
`IReadOnlyList<PostContentSegmentHttpResult> ContentSegments` alongside the raw
`Content` string. Each segment is a typed span of the post text:

```csharp
public sealed record PostContentSegment(int Sequence, string Text,
    string? MentionHandle = null, string? HashtagText = null);
```

Plain-text spans have `MentionHandle == null && HashtagText == null`. Mention spans
have `MentionHandle` set to the handle (without `@`). Hashtag spans have
`HashtagText` set to the tag (without `#`).

Our `PostDto` only provides the raw `Content` string plus separate `Hashtags` /
`Mentions` lists. Clients that want to render `@alice` as a profile link or `#cats`
as a search link must reparse the content themselves.

## What

Add `ContentSegmentParser.Parse(string content)` that tokenises the content string
into ordered segments using the same regex patterns as `MentionExtractor` and
`HashtagExtractor`. Add `List<ContentSegmentDto> ContentSegments` to `PostDto` and
populate it unconditionally in `ToDto`.

## Rules

- Segment `Sequence` values start at 0 and are contiguous.
- Plain text between tokens is emitted as a segment; leading/trailing plain text is
  always included if non-empty.
- If the entire content has no tokens, a single plain-text segment covering the whole
  string is returned.
- `MentionHandle` stores the lowercased handle without the `@` prefix.
- `HashtagText` stores the lowercased tag without the `#` prefix.
- A segment is either plain text (both nullable fields null), a mention, or a
  hashtag — never both.

## Affected Files

| File | Change |
|------|--------|
| `src/Post.Api/Program.cs` | Add `ContentSegmentDto`, `ContentSegmentParser`; populate in `ToDto` |
| `tests/Integration.Tests/IntegrationFixture.cs` | Mirror `ContentSegmentDto` |
| `tests/Integration.Tests/PostApiTests.cs` | Tests for content segment parsing via API |
