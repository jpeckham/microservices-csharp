# Prompt 2 — User Search

## Goal
Add a user search endpoint to Identity.Api so users can be found by handle or display name, mirroring `SearchUserInteractor` in the clean-architecture reference.

## Context
Identity.Api currently exposes lookup by ID and by exact handle. There is no search capability. Post.Api already has full-text post search as a reference pattern.

## Changes Required

### `src/Identity.Api`

**New endpoint: `GET /api/users/search?q={term}&limit={n}&offset={n}`**
- `q` (required): search term, minimum 1 character.
- `limit` (optional, default 20, max 50).
- `offset` (optional, default 0).
- Case-insensitive regex or `$text` search against `handle` and `displayName` fields.
- Returns array of `UserSummaryDto { UserId, Handle, DisplayName }`.
- Returns `400` if `q` is missing or empty.

**MongoDB index**
- Add a text index on `{ handle: "text", displayName: "text" }` in the database initialisation/startup code, or use a regex query if a text index is not preferred (consistent with how Post.Api does it).

**Response DTO**
```csharp
record UserSearchResultDto(Guid UserId, string Handle, string DisplayName);
```

**Wire-up in Social.Web**
- Add a search box to the navigation or a dedicated `/search` page.
- Call `GET /api/users/search?q={term}` against Identity.Api and display results with links to each user's profile.

## Acceptance Criteria
- `GET /api/users/search?q=alice` returns users whose handle or display name contains "alice" (case-insensitive).
- `GET /api/users/search` (no `q`) returns `400`.
- Results respect `limit` and `offset` for pagination.
- Social.Web search UI navigates to the correct profile page on result click.
