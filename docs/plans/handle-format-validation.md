# Handle Format Validation

## What

Reject handles that contain characters outside `[a-zA-Z0-9_]` on both registration endpoints in Identity.Api. Currently the endpoints normalize handles (strip `@`, lowercase) but never reject invalid characters — a handle like `"hello world"` would normalize to `@hello world` and be persisted.

Mirrors the handle validation present in the `UserAccount` entity of the clean-architecture-csharp reference implementation.

## Rule

After stripping the leading `@` and surrounding whitespace, the remaining characters must match `^[a-zA-Z0-9_]+$`:
- Only letters, digits, and underscores
- At least 1 character (the existing blank-field guard already catches empty handles)

Returns `400 Bad Request` with `{ "error": "Handle must contain only letters, digits, and underscores." }` if invalid.

## Affected Files

| File | Change |
|------|--------|
| `src/Identity.Api/Program.cs` | Add `HandlePattern` static regex and validation guard in both registration handlers |
| `tests/Integration.Tests/IdentityApiTests.cs` | Add 4 tests (2 per endpoint) |
