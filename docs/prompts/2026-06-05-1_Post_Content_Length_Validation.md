# Prompt 1 — Post Content Length Validation (280-char limit)

## Goal
Enforce a 280-character maximum on post content in Post.Api, matching the business rule in the clean-architecture reference.

## Context
`Post.Api` currently accepts any length of content. The clean-architecture reference enforces this at the domain-entity level. In the microservices version the appropriate place is a validation step inside the minimal-API endpoint handlers before the document is written to MongoDB.

## Changes Required

### `src/Post.Api`

**Endpoint: `POST /api/posts`**
- Validate `request.Content` is not null/whitespace and does not exceed 280 characters.
- Return `400 Bad Request` with body `{ "error": "Post content must be between 1 and 280 characters." }` on violation.

**Endpoint: `PUT /api/posts/{id}`**
- Apply the same validation to `request.Content`.

No schema changes to `PostDocument` are needed.

## Acceptance Criteria
- `POST /api/posts` with 281-character content returns `400`.
- `POST /api/posts` with exactly 280 characters returns `201`.
- `POST /api/posts` with empty/whitespace content returns `400`.
- `PUT /api/posts/{id}` with oversized content returns `400`.
- All existing post creation and update tests continue to pass.
