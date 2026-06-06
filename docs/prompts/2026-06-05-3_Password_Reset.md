# Prompt 3 — Password Reset

## Goal
Add a two-step password reset flow to Identity.Api: request a reset link via email, then consume the link to set a new password.

## Context
The clean-architecture reference implements `RequestPasswordResetInteractor` (generates a short-lived single-use token and emails a reset link) and `ResetPasswordInteractor` (validates the token and re-hashes the password). Identity.Api currently has no password reset capability.

This prompt assumes an email abstraction already exists or will be introduced as a thin interface with an in-memory stub for development (no real email service required to complete this work).

## Changes Required

### `src/Identity.Api`

**New MongoDB collection: `passwordResetTokens`**
- Document: `{ _id: Guid, userId: Guid, token: string, expiresAt: DateTimeOffset, consumed: bool }`
- Token is a cryptographically random string (e.g. `Guid.NewGuid().ToString("N")`).
- Expiry: 15 minutes from creation.
- Single-use: mark `consumed = true` on first successful use.

**New endpoint: `POST /api/password-reset-requests`**
- Body: `{ email: string }`
- Look up the user by email. If found, create a token document and "send" the reset link.
- Always return `204 No Content` regardless of whether the email exists (prevents user enumeration).
- Reset link format: `{baseUrl}/reset-password?token={token}` (baseUrl from config).

**New endpoint: `POST /api/password-resets`**
- Body: `{ token: string, newPassword: string }`
- Validate token exists, is not expired, and is not consumed.
- Validate `newPassword` meets minimum requirements (≥ 8 characters, at least 1 digit).
- Re-hash password and update `UserDocument.PasswordHash`.
- Mark token as consumed.
- Return `204` on success, `400` for invalid/expired token or bad password.

**Email abstraction**
- Define `IEmailSender` with a single method: `Task SendAsync(string to, string subject, string body)`.
- Provide `InMemoryEmailSender` for development (stores messages in a list, optionally exposes `GET /dev/emails`).
- Register via DI; swap in a real sender (e.g. Azure Communication Services) via configuration.

**Social.Web**
- Add a "Forgot password?" link on the Login page that calls `POST /api/password-reset-requests`.
- Add a `/reset-password` page that reads `?token=` from the query string and calls `POST /api/password-resets`.

## Acceptance Criteria
- `POST /api/password-reset-requests` with a valid email triggers token creation (visible via `GET /dev/emails` in dev).
- `POST /api/password-reset-requests` with an unknown email still returns `204`.
- `POST /api/password-resets` with a valid token updates the password and the user can log in with the new password.
- `POST /api/password-resets` with an expired or already-consumed token returns `400`.
- Token cannot be reused after a successful reset.
