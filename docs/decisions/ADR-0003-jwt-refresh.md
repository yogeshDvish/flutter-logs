# ADR-0003 — JWT access token in memory + rotating refresh cookie

Status: Accepted · 2026-10-29

## Context

Email + password authentication with JWT was requested. Two constraints collide with common JWT
practice: tokens in `localStorage` are readable by any XSS, and the browser `EventSource` API
cannot send an `Authorization` header for the live stream.

## Decision

- 15-minute HS256 access JWT returned in the login/refresh response body, held in React memory
  only (never `localStorage`/`sessionStorage`/cookies).
- 7-day opaque refresh token in `fl_refresh` — `HttpOnly`, `Secure`, `SameSite=Strict`,
  `Path=/api/v1/auth`.
- Refresh rotates on every use; reuse of a consumed token revokes the entire family.
- The SSE endpoint is consumed with `fetch()` + `ReadableStream` so the access token travels in
  the `Authorization` header.

## Alternatives

- **JWT in `localStorage`** — simplest, rejected: XSS turns into persistent account takeover.
- **JWT in a cookie only** — avoids the memory plumbing, but makes CSRF the primary defense and
  complicates revocation.
- **Server-side sessions** — simpler revocation, but conflicts with the stated JWT requirement.

## Consequences

- Page reload requires one `/auth/refresh` round trip before data loads; the app shows a boot
  state.
- Refresh endpoint must validate `Origin` to complement `SameSite`.
- Streaming requires a small SSE parser in the client rather than `EventSource`.
- Token revocation is bounded by the 15-minute access-token lifetime, except for refresh reuse.
