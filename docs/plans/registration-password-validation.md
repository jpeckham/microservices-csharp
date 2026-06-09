# Registration Password Strength Validation

## What

Enforce password strength on both registration endpoints in Identity.Api:

- `POST /api/users/register` — direct registration
- `POST /api/registrations` — start of email-verified registration

Currently both endpoints only check that a password is non-empty. The password-reset and change-password flows already enforce strength rules, but a weak password can slip in at registration time.

Mirrors the password validation present in the `CreateAccount` and `RegisterAccount` use cases of the clean-architecture-csharp reference implementation.

## Rule

Same rule already applied in `POST /api/password-resets` and `PUT /api/users/me/password`:

> Password must be at least 8 characters and contain at least one digit.

Returns `400 Bad Request` with `{ "error": "Password must be at least 8 characters and contain at least one digit." }` if the rule is violated.

## Affected Files

| File | Change |
|------|--------|
| `src/Identity.Api/Program.cs` | Add strength check after the blank-field guard in both registration handlers |
| `tests/Integration.Tests/IdentityApiTests.cs` | Add 4 tests (2 per endpoint) |
