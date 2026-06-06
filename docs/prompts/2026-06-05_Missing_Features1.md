# Missing Features: clean-architecture-csharp → microservices-csharp

Functionality present in `clean-architecture-csharp` that has not yet been brought into the microservices version.

---

## 1. Email Verification Registration Flow

**Clean-arch:** Two signup paths — a direct `CreateAccountInteractor` (instant) and a verified `RegisterAccountInteractor` that sends a time-limited verification code via email, requiring `VerifyRegistrationInteractor` to complete signup.

**Microservices gap:** Identity.Api only supports direct registration with no email verification step, no pending-registration state, and no OTP/code concept.

**Needs:**
- Pending registration storage (handle, email, hashed password, verification code, expiry)
- Email dispatch on registration
- `POST /api/registrations` + `POST /api/registrations/verify` endpoints in Identity.Api
- Email infrastructure integration (equivalent of ACS Email or in-memory dev stub)

---

## 2. Device-Based Login with OTP

**Clean-arch:** `LoginWithDeviceInteractor` accepts a device ID alongside credentials. If the device is unrecognized, it sends a one-time passcode via email instead of issuing a session token. `VerifyDeviceOtpInteractor` validates the OTP and optionally remembers the device for future logins.

**Microservices gap:** Identity.Api issues a JWT on every successful password match with no device awareness or step-up challenge.

**Needs:**
- Remembered-device storage (userId + deviceId)
- Verification-code storage (userId, code, expiry, one-time-use flag)
- `POST /api/sessions/device` and `POST /api/sessions/device/verify` endpoints
- Email dispatch for OTP

---

## 3. Password Reset

**Clean-arch:** `RequestPasswordResetInteractor` generates a short-lived token (5 min expiry, single-use) and emails a reset link. `ResetPasswordInteractor` validates the token and re-hashes the password.

**Microservices gap:** Identity.Api has no password-reset capability at all.

**Needs:**
- Password-reset token storage (userId, token, expiry, consumed flag)
- `POST /api/password-reset-requests` + `POST /api/password-resets` endpoints in Identity.Api
- Email dispatch for reset link

---

## 4. User Profile Images

**Clean-arch:** Three-step upload flow — `BeginProfileImageUploadInteractor` reserves an asset slot and returns an upload URL; the client uploads directly; `CompleteProfileImageUploadInteractor` finalizes and associates the image with the user. `RemoveProfileImageInteractor` deletes it. Stored as a `ProfileImage` value object on `UserAccount` (assetId, storageKey, contentType, dimensions, byteLength).

**Microservices gap:** Identity.Api `UserDocument` has no profile image field; no upload endpoints exist anywhere.

**Needs:**
- Profile image fields on `UserDocument`
- Begin/complete upload endpoints in Identity.Api (or a dedicated Media service)
- `DELETE /api/users/me/profile-image` endpoint
- Blob/file storage backend (Azure Blob or local file system)
- `UserProfileUpdated` integration event extended with profile image URL so Feed.Api can denormalize it

---

## 5. Post Media Attachments (Images & Video)

**Clean-arch:** Posts support up to 4 images **or** 1 video. `BeginPostMediaUploadInteractor` reserves a `PostMediaItem` slot (assetId, kind, sortOrder, contentType, dimensions, durationMs, thumbnailKey, altText). `CompletePostMediaUploadInteractor` finalizes it. Business rules: image vs. video content-type validation, max 4 images or 1 video enforced at the domain level.

**Microservices gap:** `PostDocument` stores only text content; Post.Api has no media upload endpoints.

**Needs:**
- `Media` list on `PostDocument` (assetId, kind, storageKey, contentType, dimensions, etc.)
- Begin/complete media upload endpoints in Post.Api (or shared Media service)
- Blob/file storage backend
- `PostCreated` / `PostUpdated` contracts extended with media for Feed.Api denormalization

---

## 6. Replies & Threaded Conversations

**Clean-arch:** `ReplyToPostInteractor` creates a new post with a `ParentPostId` reference. `DisplayPostInteractor` returns a single post together with its full conversation thread (ancestors + replies). The post feed and search are aware of `ParentPostId` (replies vs. root posts).

**Microservices gap:** The Engagement.Api comments model is flat (not structured as posts); Post.Api has no `ParentPostId` concept and no conversation-thread query.

**Needs:**
- `ParentPostId` (nullable Guid) on `PostDocument` in Post.Api
- `GET /api/posts/{id}` extended to return conversation thread
- `POST /api/posts/{postId}/replies` endpoint in Post.Api
- Feed filtering to optionally exclude replies from the main timeline
- `PostCreated` event carries `ParentPostId` so Feed.Api can handle threading

---

## 7. Reposts (Shares)

**Clean-arch:** `RepostInteractor` creates a new post with `OriginalPostId` set (and blank content), effectively sharing another user's post. `DeleteRepostInteractor` removes the repost. The feed differentiates reposts from original posts.

**Microservices gap:** No repost concept exists in any service.

**Needs:**
- `OriginalPostId` (nullable Guid) on `PostDocument`
- `POST /api/posts/{postId}/reposts` and `DELETE /api/posts/{postId}/reposts/mine` in Post.Api
- `PostCreated` event carries `OriginalPostId` for Feed.Api display
- Feed.Api `FeedEntryDocument` should surface repost metadata (original author, original content)

---

## 8. Block Users

**Clean-arch:** `BlockUserPostsInteractor` records a block relationship (symmetrical to follow). The `ScrollPostsInteractor` filters both followed-only and blocked-user posts from the feed.

**Microservices gap:** Social.Api tracks follows only; no block relationship exists and Feed.Api has no block-filtering logic.

**Needs:**
- Block relationship storage in Social.Api (`BlockDocument`: blockerId, blockedId)
- `POST /api/users/{id}/blocks` and `DELETE /api/users/{id}/blocks` in Social.Api
- `UserBlocked` / `UserUnblocked` integration events in Shared.Contracts
- Feed.Api consumes block events and excludes blocked users from feed results

---

## 9. Mention Extraction (@handle)

**Clean-arch:** `SocialPost.ExtractMentionHandles()` parses content with a regex and stores a `Mentions` HashSet on the post. ViewModels segment content into plain text and `@handle` spans for UI rendering.

**Microservices gap:** Post.Api stores raw content string only; no mention parsing, no structured content segments.

**Needs:**
- `Mentions` list on `PostDocument` (populated on create/edit)
- Content segmentation in Social.Web when rendering post content
- (Optional) `GET /api/posts/by-mention/{handle}` query endpoint

---

## 10. Hashtag Extraction (#tag)

**Clean-arch:** `SocialPost.ExtractHashtags()` similarly parses `#tag` tokens and stores a `Hashtags` HashSet. ViewModels render hashtags as clickable spans.

**Microservices gap:** No hashtag parsing or storage exists.

**Needs:**
- `Hashtags` list on `PostDocument`
- Content segmentation for hashtag rendering in Social.Web
- (Optional) `GET /api/posts/by-hashtag/{tag}` query endpoint

---

## 11. User Search

**Clean-arch:** `SearchUserInteractor` searches users by handle prefix or display name substring.

**Microservices gap:** Post.Api has full-text post search, but Identity.Api has no user search endpoint.

**Needs:**
- `GET /api/users/search?q={term}` in Identity.Api
- Text index on handle + displayName in MongoDB
- Search results wired into Social.Web UI

---

## 12. Post Content Business Rules (280-char limit)

**Clean-arch:** The `SocialPost` domain entity enforces a hard 280-character content limit at construction time.

**Microservices gap:** Post.Api stores arbitrary-length content with no domain-level length enforcement (only whatever the client sends).

**Needs:**
- 280-character validation on `POST /api/posts` and `PUT /api/posts/{id}` in Post.Api
- Return `400 Bad Request` with a descriptive message on violation

---

## Summary Table

| Feature | Target Service(s) | Effort |
|---|---|---|
| Email verification registration | Identity.Api | Medium |
| Device-based login + OTP | Identity.Api | Medium |
| Password reset | Identity.Api | Small |
| Profile images | Identity.Api + Media/Blob | Medium |
| Post media attachments | Post.Api + Media/Blob | Large |
| Replies + conversation threading | Post.Api, Feed.Api | Medium |
| Reposts | Post.Api, Feed.Api | Small |
| Block users | Social.Api, Feed.Api | Small |
| Mention extraction | Post.Api, Social.Web | Small |
| Hashtag extraction | Post.Api, Social.Web | Small |
| User search | Identity.Api | Small |
| 280-char content limit | Post.Api | Trivial |
