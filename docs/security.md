# Security & Privacy

Status: approved baseline (Phase 0) · Last updated: 2026-10-29

## 1. Assets

| Asset | Confidentiality | Integrity | Availability |
|---|---|---|---|
| User credentials / refresh tokens | Critical | Critical | High |
| Ingestion keys | High (write capability) | Critical | High |
| Log content (may contain PII/secrets) | High | High | Medium |
| Cross-tenant isolation | Critical | Critical | — |
| Audit trail | Medium | Critical | Medium |

## 2. Threat model

| # | Threat | Vector | Impact | Mitigation (MVP) | Residual |
|---|---|---|---|---|---|
| T1 | Ingestion key extracted from a shipped app | APK/IPA decompilation, string scanning | Fabricated logs, quota exhaustion | Write-only scope, per-key rate + daily quota, rotation, instant revoke, anomaly audit | **Accepted** — cannot be prevented for any embedded credential |
| T2 | Key brute force | Automated guessing | Unauthorized writes | 32-byte secret, indexed prefix, 60 req/min limit, uniform 401 | Low |
| T3 | Refresh token theft | XSS, malware | Account takeover | `httpOnly`/`SameSite=Strict` cookie, 7-day TTL, rotation + reuse detection (family revoke) | Low |
| T4 | Access token theft via XSS | Malicious script | 15-minute takeover | Token in memory only, strict CSP, no `innerHTML` rendering of logs | Low |
| T5 | CSRF on refresh | Cross-site form | Session continuation | `SameSite=Strict`, `Origin` validation on `/auth/refresh` | Low |
| T6 | IDOR / cross-tenant read | Guessing or swapping UUIDs | Data breach | Owner-scoped repository, 404 for non-owned, per-endpoint negative tests, SSE auth on connect | Low with tests |
| T7 | Credential leakage into app logs | Logging request bodies/headers | Full compromise | Serilog configured without bodies/headers; keys logged as prefix only; review gate | Low |
| T8 | Log-body secret leakage | App logs tokens/PII | Privacy incident | Client redaction hooks, documented warning, 30-day retention, owner purge, no server-side indexing of metadata | Medium — inherent to logging |
| T9 | Ingest flooding | Scripted batches | Cost/DoS, storage exhaustion | 1 MB body, 500 records/batch, 60 req/min, 20k records/day/key, 413/429, request timeouts | Medium |
| T10 | Log injection / terminal escapes | Control characters, ANSI | UI spoofing, log forgery | Render as text, strip control chars for display, JSON-encoded transport | Low |
| T11 | SQL injection | Crafted filters | DB compromise | EF Core parameterization only, no dynamic SQL | Low |
| T12 | Enumeration / timing | Response differences | Information disclosure | Uniform 404, constant-time digest compare | Low |
| T13 | Malicious project name in UI | Stored XSS | Token theft | React escaping, no `dangerouslySetInnerHTML`, CSP | Low |
| T14 | Supply chain | Compromised package | RCE | Pinned versions, audit in CI, no auto-major upgrades | Medium |
| T15 | Backup exposure | Unprotected dump | Full breach | Phase 8: encrypted backups, restricted access, restore drill | Tracked |

## 3. Authentication design

- **Passwords:** Argon2id, m=64 MB, t=3, p=1, 16-byte salt, 32-byte hash, PHC encoding.
  Minimum 12 characters; checked against a small common-password list; no composition rules.
- **Access JWT:** HS256, 15 min, claims `sub`, `jti`, `iat`, `exp`, `iss`, `aud`. Signing key
  ≥ 32 bytes from `Jwt__SigningKey`, rotated by restarting with a new key (accepting a short
  forced re-login; documented).
- **Refresh token:** 32 random bytes, base64url, stored as SHA-256; scoped cookie
  `fl_refresh; HttpOnly; Secure; SameSite=Strict; Path=/api/v1/auth`; 7-day TTL; rotated on every
  use; reuse revokes the family.
- **Verification/reset:** single-use hashed tokens, 24 h / 1 h expiry, uniform responses to avoid
  account enumeration.
- **Password reset** invalidates all refresh families for the user.

## 4. Authorization

Two authentication schemes, never interchangeable:

| Scheme | Accepted by | Grants |
|---|---|---|
| `UserJwt` | all `/api/v1/*` except `/ingest` | Owner-scoped read/write |
| `ProjectKey` | `/ingest` **only** | Append logs to exactly one project |

- Every project-scoped handler resolves the project via `OwnedBy(userId)`, so ownership is a
  single code path rather than a per-handler convention.
- A non-owned project returns `404`, never `403`, to avoid confirming existence.
- The SSE endpoint validates project ownership before subscribing.
- Ingestion key authentication: lookup by `prefix`, then `HMAC-SHA256(secret, pepper)` compared in
  constant time; revoked/expired keys rejected; `last_used_at` updated at most once a minute.
- Defense in depth (deferred, documented): SQL Server row-level security is possible later; the
  MVP guarantee rests on the repository layer plus the negative-test matrix.

## 5. Secrets and configuration

- Nothing sensitive in git. `.gitignore` excludes `appsettings.*.local.json`, `.env*`, `*.pfx`.
- Development: `dotnet user-secrets` for `Jwt:SigningKey` and `Ingest:Pepper`
  (environment variables use `Jwt__SigningKey` / `Ingest__Pepper`).
- Production (Phase 8): environment variables or a secret store; no defaults, startup fails fast
  if `Jwt__SigningKey` is missing or shorter than 32 bytes.
- Dev-only connection strings use Windows integrated auth (no password material).

## 6. Transport and browser hardening

- TLS required outside localhost (Phase 8). HSTS enabled then.
- Headers: `Content-Security-Policy: default-src 'self'; connect-src 'self'; img-src 'self' data:;
  script-src 'self'; style-src 'self' 'unsafe-inline'; frame-ancestors 'none'`,
  `X-Content-Type-Options: nosniff`, `Referrer-Policy: no-referrer`,
  `Permissions-Policy: geolocation=(), microphone=(), camera=()`.
- CORS only for the configured origin; credentials allowed only for `/auth/*`.
- Rate limiting via ASP.NET Core middleware, partitioned by IP for auth and by key for ingest.

## 7. Logging hygiene and privacy

- **Our** application logs never contain: passwords, JWTs, refresh tokens, ingestion keys, request
  bodies, or log-record payloads. Keys appear as `flk_live_Ab3dE9f1…`.
- Correlation IDs are attached to every request and echoed to the client for support.
- Audit table records security-relevant actions without sensitive values.
- **Customer** log content is untrusted input: stored as text, rendered as text, size-capped,
  never executed, never used in redirects or SQL.
- Retention: 30 days by default; owner purge; project archive purges after 7 days.
- No third-party analytics or trackers. No hardware identifiers. Installation IDs are pseudonymous
  and project-scoped.

## 8. Privacy notice content (for the UI)

The app should state, in plain language: logs may contain personal data; the owner controls
retention up to 30 days; installation IDs are random and can be blocked; the platform does not
read logs except as needed to operate the service.

## 9. Reliability and abuse posture

- A log is durable only after `COMMIT`; the API never acknowledges earlier.
- Ingestion is idempotent; retries cannot duplicate records.
- Per-subscriber bounded buffers prevent a slow browser from exhausting server memory.
- Accepted limitations for the MVP: single instance, no WAF, no CAPTCHA on registration (rate
  limits only), no server-side PII detection, no per-device keys.

## 10. Security review checklist (per phase)

- [ ] New endpoint has an owner-scope test and a cross-tenant negative test
- [ ] New input has an explicit size cap
- [ ] New error path leaks no stack trace or existence information
- [ ] No secret added to git, config defaults, logs, or the bundle
- [ ] New dependency reviewed and audited
