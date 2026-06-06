# Prompt 4 — Email Verification Registration Flow

## Goal
Add an optional email-verified registration path to Identity.Api alongside the existing direct registration, mirroring the `RegisterAccountInteractor` / `VerifyRegistrationInteractor` flow in the clean-architecture reference.

## Prerequisites
- Prompt 3 (Password Reset) must be complete — this reuses `IEmailSender` and the in-memory dev stub.

## Context
Identity.Api's `POST /api/users/register` currently creates an account immediately. The clean-architecture reference offers a two-step path where a pending registration is created, a verification code is emailed, and the account is only finalised after the code is confirmed. Both paths should coexist.

## Changes Required

### `src/Identity.Api`

**New MongoDB collection: `pendingRegistrations`**
- Document:
  ```
  {
    _id: Guid,
    handle: string,
    email: string (lowercase, unique),
    displayName: string,
    passwordHash: string,
    verificationCode: string (6-digit numeric),
    expiresAt: DateTimeOffset,   // 24 hours
    createdAt: DateTimeOffset
  }
  ```

**New endpoint: `POST /api/registrations`**
- Body: `{ email, handle, displayName, password }`
- Validate that email and handle are not already taken (check both `users` and `pendingRegistrations`).
- Hash the password immediately.
- Generate a 6-digit numeric verification code.
- Store a `PendingRegistration` document.
- Email the code to the supplied address.
- Return `202 Accepted` with `{ pendingRegistrationId: Guid }`.

**New endpoint: `POST /api/registrations/verify`**
- Body: `{ pendingRegistrationId: Guid, code: string }`
- Look up the pending registration by ID.
- Validate it exists, is not expired, and the code matches.
- Create the `UserDocument` (same as existing direct registration).
- Delete the pending registration document.
- Return `201 Created` with a JWT token response (same shape as login) so the user is immediately signed in.
- Return `400` for invalid/expired/mismatched code.

**Cleanup**
- Expired pending registrations can be left for a TTL index on `expiresAt` to remove, or purged on the next registration attempt for the same email/handle.

**Social.Web**
- Update the Register page to use `POST /api/registrations` and then show a "check your email" screen.
- Add a verification code entry form that calls `POST /api/registrations/verify`.
- On success, store the returned JWT and redirect to the home feed (same as login).

## Acceptance Criteria
- `POST /api/registrations` with a valid payload returns `202` and sends an email (visible via `GET /dev/emails`).
- `POST /api/registrations/verify` with the correct code creates the user and returns a JWT.
- `POST /api/registrations/verify` with the wrong code returns `400`.
- `POST /api/registrations/verify` after expiry returns `400`.
- `POST /api/registrations` with an already-registered email or handle returns `409 Conflict`.
- Existing `POST /api/users/register` (direct path) continues to work unchanged.
