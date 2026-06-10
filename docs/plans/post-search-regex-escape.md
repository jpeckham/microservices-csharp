# Post Search Regex Escaping

## What

Escape the query string before using it as a MongoDB regex in `GET /api/posts/search`, and reject empty queries with `400 Bad Request`.

The clean-architecture-csharp reference implementation uses `string.Contains(query, StringComparison.OrdinalIgnoreCase)` which treats the query as a plain literal. This project uses a MongoDB `BsonRegularExpression` but currently passes the raw user input directly as the pattern — a query like `a.b` matches `aXb` (`.` matches any char) and `a[b` causes a MongoDB regex error.

Identity.Api's user search in this same codebase already does the right thing:
```csharp
var regex = new MongoDB.Bson.BsonRegularExpression(System.Text.RegularExpressions.Regex.Escape(q), "i");
```

Post.Api should do the same.

## Changes

| File | Change |
|------|--------|
| `src/Post.Api/Program.cs` | Add `Regex.Escape(q)` call; add 400 guard when `q` is null/whitespace |
| `tests/Integration.Tests/PostApiTests.cs` | 3 new tests: empty query returns 400, special-char query returns results safely, dot query matches literal dot |
