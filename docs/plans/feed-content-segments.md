# ContentSegments on FeedEntryDto

## Source

The reference's `PostSummaryViewModel` uses `IReadOnlyList<PostContentSegment> ContentSegments`
everywhere — including in the scroll/feed response. This lets feed clients render
`@mentions` as profile links and `#hashtags` as search links without reparsing
the raw content string on the client side.

We added `ContentSegments` to `PostDto` in `Post.Api`. `FeedEntryDto` in `Feed.Api`
still only exposes raw `Content`, requiring clients to parse the content themselves.

## What

Add `ContentSegmentParser` to `Feed.Api` (same tokenization logic as `Post.Api` —
microservice duplication is acceptable) and populate `ContentSegments` on
`FeedEntryDto` from `ToDto`.

## Rules

- Each segment has `Sequence` (0-based), `Text`, and optionally `MentionHandle`
  (lowercased, no `@`) or `HashtagText` (lowercased, no `#`).
- Plain text between tokens emits a plain segment; empty content emits one empty segment.
- `ContentSegments` is always populated (never null) for every `FeedEntryDto`.

## Affected Files

| File | Change |
|------|--------|
| `src/Feed.Api/Program.cs` | Add `ContentSegmentDto`, `ContentSegmentParser`; add to `FeedEntryDto`; update `ToDto` |
| `tests/Integration.Tests/IntegrationFixture.cs` | Add `ContentSegments` to test `FeedEntryDto` |
| `tests/Integration.Tests/FeedApiTests.cs` | Tests for content segments in feed |
