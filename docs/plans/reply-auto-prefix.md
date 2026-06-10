# Reply Content Auto-Prefixed with @parentAuthorHandle

## What

The reference's `ReplyToPostInteractor` calls `PrefixedReplyContent` to ensure
replies always begin with `@parentAuthorHandle`. This makes threads readable on
their own: every reply visually mentions who it is directed at.

```csharp
private static string PrefixedReplyContent(string parentAuthorHandle, string content)
{
    var prefix = $"@{SocialPost.NormalizeHandle(parentAuthorHandle)}";
    var body = content.Trim();
    return body.Equals(prefix, StringComparison.OrdinalIgnoreCase) ||
        body.StartsWith($"{prefix} ", StringComparison.OrdinalIgnoreCase)
        ? body
        : string.IsNullOrWhiteSpace(body) ? prefix : $"{prefix} {body}";
}
```

Our `POST /api/posts/{postId}/replies` stores the caller-supplied content
verbatim. It already fetches the parent post (needed for 404 check), so the
parent's `AuthorHandle` is available at no extra cost.

## Rule

When saving a reply:
- Strip `@` prefix from the parent's handle, lowercase it → `normalizedHandle`
- `prefix = "@" + normalizedHandle`
- If body is blank → `content = prefix`
- If body already starts with `prefix` (case-insensitive, whole word) → unchanged
- Otherwise → `content = prefix + " " + body`

Content-length validation (max 280) is applied to the raw caller-supplied body
**before** prefixing, matching the reference behaviour (prefix is not counted
against the caller's budget).

## Affected Files

| File | Change |
|------|--------|
| `src/Post.Api/Program.cs` | Apply prefix logic in reply endpoint |
| `tests/Integration.Tests/PostApiTests.cs` | Add prefix tests |
