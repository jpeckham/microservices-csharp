# 403 Forbidden When Deleting or Updating Another User's Post

## What

Return `403 Forbidden` (not `404 Not Found`) when an authenticated user attempts to delete or update a post they do not own.

Currently both `DELETE /api/posts/{id}` and `PUT /api/posts/{id}` use a combined MongoDB filter `p.Id == id && p.AuthorId == userId`, so a missing post and a forbidden post produce an identical 404 response. A client cannot tell whether the post doesn't exist or whether it exists but the caller is not the author.

Mirrors the reference implementation (`DeletePost` handler in `SocialAppSliceEndpoints.cs`):

```csharp
catch (InvalidOperationException ex)
{
    return Results.Json(new { succeeded = false, message = ex.Message },
        statusCode: StatusCodes.Status403Forbidden);
}
return presenter.ViewModel is { Succeeded: true }
    ? Results.Ok(presenter.ViewModel)
    : Results.NotFound(presenter.ViewModel);
```

## Rule

For both `DELETE` and `PUT`:
1. Look up the post by ID alone.
2. If not found → `404 Not Found`.
3. If found but `AuthorId != userId` → `403 Forbidden`.
4. Otherwise proceed with the operation.

## Affected Files

| File | Change |
|------|--------|
| `src/Post.Api/Program.cs` | Two-step lookup in delete and update handlers |
| `tests/Integration.Tests/PostApiTests.cs` | 4 new tests (delete/update by wrong user → 403, delete/update non-existent → 404) |
