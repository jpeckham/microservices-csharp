# Display Name Required on Registration

## What

The reference's `UserAccount.CreateWithCredentials` throws `ArgumentException` ("Display name is required.") when `DisplayName` is null or whitespace — meaning both `POST /api/accounts` and `POST /api/registrations` require a non-empty display name.

Our Identity.Api currently allows `DisplayName` to be omitted on both `POST /api/users/register` and `POST /api/registrations`. When absent it silently falls back to the handle string, so a user could register without ever providing a human-readable name.

## Rule

- `DisplayName` must be non-null and non-whitespace in both registration endpoints.
- If missing or blank → `400 Bad Request`.
- The existing 50-character max-length check is unchanged.

## Affected Files

| File | Change |
|------|--------|
| `src/Identity.Api/Program.cs` | Add `DisplayName` to the required-fields guard in both `/api/users/register` and `/api/registrations`; remove the now-unreachable fallback default |
| `tests/Integration.Tests/IdentityApiTests.cs` | Add tests: register without display name → 400 (both endpoints) |
