# Email Format Validation on Registration

## What

Reject syntactically invalid email addresses on both registration endpoints in Identity.Api. Currently only a non-empty check is applied — an input like `"notanemail"` or `"missing@"` is accepted and stored.

Mirrors the email validation present in the `UserAccount` entity of the clean-architecture-csharp reference implementation.

## Implementation

Use `System.Net.Mail.MailAddress` (built into .NET, no extra packages) — its constructor throws `FormatException` for invalid addresses and handles the RFC 2822 edge cases cleanly.

```csharp
static bool IsValidEmail(string email)
{
    try { _ = new System.Net.Mail.MailAddress(email); return true; }
    catch { return false; }
}
```

Returns `400 Bad Request` with `{ "error": "A valid email address is required." }` if the format is invalid.

## Affected Files

| File | Change |
|------|--------|
| `src/Identity.Api/Program.cs` | Add `IsValidEmail` helper and validation guard in both registration handlers |
| `tests/Integration.Tests/IdentityApiTests.cs` | Add 4 tests (2 per endpoint) |
