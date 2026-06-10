# Password Reset Token 5-Minute Expiry

## What

`POST /api/password-reset-requests` creates a reset token with a 15-minute expiry.
The reference repo uses `TimeSpan.FromMinutes(5)` and the email body explicitly tells
the user "Use this one-time link within 5 minutes". A shorter window is better security
practice for single-use password reset tokens.

The existing test suite already covers expired and consumed token rejection
(`ResetPassword_with_expired_token_returns_400`, `ResetPassword_token_cannot_be_reused`).
The only code change is tightening the expiry window to match the reference.

## Rule

- Password reset tokens expire after 5 minutes (down from 15).
- Expired token → `400 Bad Request` (already validated and tested).
- Consumed token → `400 Bad Request` (already validated and tested).

## Affected Files

| File | Change |
|------|--------|
| `src/Identity.Api/Program.cs` | Change `AddMinutes(15)` → `AddMinutes(5)` for reset tokens |
