# Direct Registration Returns JWT Token

## What

`POST /api/users/register` currently returns a `UserProfileDto` with no session
token. The caller must make a separate `POST /api/users/login` request to get a
token before using any authenticated endpoint.

The reference's `CreateAccountInteractor` immediately creates a session after
registration and returns the token alongside the account info:
```
output.Present(new CreateAccountResponse(true, "ACCOUNT_CREATED", user.Handle, token));
```

Our email-verified flow (`POST /api/registrations/verify`) already returns a token
on success. The direct registration endpoint should be consistent.

## Rule

- `POST /api/users/register` returns `201 Created` with a `TokenResponse` body
  (same shape as the login response: token, userId, username, handle, displayName).
- The fixture's `RegisterAndLoginAsync()` can then skip the separate login call.

## Affected Files

| File | Change |
|------|--------|
| `src/Identity.Api/Program.cs` | Return `TokenResponse` (with JWT) instead of `UserProfileDto` from register endpoint |
| `tests/Integration.Tests/IntegrationFixture.cs` | Remove separate login call in `RegisterAndLoginAsync()` |
| `tests/Integration.Tests/IdentityApiTests.cs` | Update `Register_creates_user` to assert token is returned |
