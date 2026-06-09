# Change Password

## What

Add a `PUT /api/users/me/password` endpoint to Identity.Api that lets an authenticated user change their own password by supplying their current password and a new one. This is distinct from the existing email-based password-reset flow (which is for users who have forgotten their password); this endpoint is for users who are logged in and know their current password.

Mirrors the `ChangePassword` use case in the clean-architecture-csharp reference implementation.

## Rules

- Requires a valid JWT (401 if unauthenticated).
- `currentPassword` must match the stored hash (400 if wrong).
- `newPassword` must be at least 8 characters and contain at least one digit — matching the existing reset-password validation (400 if invalid).
- On success, updates the hash and returns 204 No Content.

## Endpoint

```
PUT /api/users/me/password
Authorization: Bearer {token}
Content-Type: application/json

{ "currentPassword": "OldP@ss1", "newPassword": "NewP@ss1" }
```

## Affected Files

| File | Change |
|------|--------|
| `src/Identity.Api/Program.cs` | Add `ChangePasswordRequest` record and `PUT /api/users/me/password` handler |
| `tests/Integration.Tests/IdentityApiTests.cs` | Add change-password integration tests |

## Tests

1. Valid credentials → 204, subsequent login with new password succeeds
2. Wrong current password → 400
3. Weak new password → 400
4. No auth → 401
