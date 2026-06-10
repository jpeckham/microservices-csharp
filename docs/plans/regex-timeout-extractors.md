# Regex Timeout on Hashtag and Mention Extractors

## What

Add a 1-second evaluation timeout to the compiled `Regex` instances in `HashtagExtractor` and `MentionExtractor` in Post.Api, and catch `RegexMatchTimeoutException` to return an empty list rather than propagating a 500.

Mirrors the defensive pattern in the clean-architecture-csharp reference implementation:

```csharp
// Reference (SocialApp.Post/Entities/SocialPost.cs)
private static readonly Regex MentionPattern = new(@"@([a-zA-Z0-9_]+)", RegexOptions.Compiled, TimeSpan.FromSeconds(1));
private static readonly Regex HashtagPattern = new(@"#(\S+)", RegexOptions.Compiled, TimeSpan.FromSeconds(1));
```

## Why

`Regex.Matches` can hang indefinitely on pathological inputs with certain patterns. A per-evaluation timeout bounds worst-case latency at the cost of silently returning no tags — which is safe: tags are informational and their absence does not break core functionality.

## Affected Files

| File | Change |
|------|--------|
| `src/Post.Api/Program.cs` | Add `TimeSpan` to both `Regex` constructors; wrap `Pattern.Matches` in try/catch |
| `tests/Integration.Tests/PostApiTests.cs` | 2 tests: long content does not throw and still returns tags |
