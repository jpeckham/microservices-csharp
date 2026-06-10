# Display Name Max Length Validation

## What

Enforce a 50-character maximum on display names in Identity.Api. Currently the display name field is validated only for emptiness — there is no upper-bound check. A display name of 10 000 characters would be accepted and stored.

Mirrors the display-name length constraint enforced in the `UserAccount` entity of the clean-architecture-csharp reference implementation.

## Rule

After trimming whitespace, the display name must be ≤ 50 characters. Returns `400 Bad Request` with `{ "error": "Display name must be 50 characters or fewer." }` if exceeded.

## Affected Endpoints

| Endpoint | Change |
|----------|--------|
| `POST /api/users/register` | Validate `DisplayName` length if the field is provided |
| `POST /api/registrations` | Same |
| `PUT /api/users/me/display-name` | Add max-length check after the empty check |

## Affected Files

| File | Change |
|------|--------|
| `src/Identity.Api/Program.cs` | Add length check in all three handlers |
| `tests/Integration.Tests/IdentityApiTests.cs` | Add 3 tests |
