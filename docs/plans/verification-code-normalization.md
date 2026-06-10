# Verification Code Normalization

## What

Normalize the user-supplied verification code before comparing it to the stored code in `POST /api/registrations/verify`. Currently a raw equality check is used — typing `"1 2 3 4 5 6"` or `"123-456"` (both common when transcribing a code from an email or SMS) fails even though the digits are correct.

Mirrors the `NormalizeCode` helper in `InMemoryVerificationCodeGateway` from the clean-architecture-csharp reference:

```csharp
private static string NormalizeCode(string code)
{
    var digits = new string(code.Where(char.IsDigit).ToArray());
    return digits.Length == 6 ? digits : code.Trim();
}
```

## Rule

Before comparing, apply:
1. Strip all non-digit characters from the input.
2. If the result is exactly 6 digits, use that as the normalized form.
3. Otherwise fall back to `code.Trim()` (handles non-digit codes if the format ever changes).

## Affected Files

| File | Change |
|------|--------|
| `src/Identity.Api/Program.cs` | Add `NormalizeCode` static helper; apply before the equality check |
| `tests/Integration.Tests/IdentityApiTests.cs` | 3 new tests: spaced code succeeds, hyphenated code succeeds, wrong code still fails |
