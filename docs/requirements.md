# Requirements — Flutter Logs Platform

Status: approved baseline (Phase 0) · Last updated: 2026-10-29

## 1. Purpose

A web platform where a developer registers, creates projects, obtains a project-scoped
credential, and receives application logs from Flutter apps. Logs are persisted, searchable,
and streamed live to the owner's dashboard.

Three identities are kept strictly separate:

| Identity | Authenticates | Can do |
|---|---|---|
| Website user | Email + password → JWT | Everything, but only inside projects they own |
| Project ingestion key | Bearer token at `/ingest` | Write logs to **one** project. No reads. No admin |
| Application installation | `installation_id` in the payload | Nothing. It is a grouping label, not a principal |

## 2. Personas

- **Owner / developer** — registers, creates projects, copies the ingestion key into a Flutter
  app, watches logs arrive, filters them, inspects failures.
- **Application installation** — a running copy of the owner's app; emits logs; never logs in.

## 3. Glossary

| Term | Meaning |
|---|---|
| Project | A container for one application's logs, owned by exactly one user |
| Ingestion key | High-entropy, write-only, revocable credential scoped to one project |
| Installation | One app installation, identified by a client-generated UUID (`installation_id`) |
| Commit | A successful database transaction; only then is a log considered stored |
| Live stream | SSE connection delivering newly committed logs to an authorized browser |

## 4. Functional requirements (MVP)

### 4.1 Accounts — FR-A

- FR-A1 Register with email + password. Email stored lowercase; uniqueness enforced.
- FR-A2 Log in, obtaining a 15-minute access JWT (response body) and a 7-day refresh token
  (`httpOnly`, `SameSite=Strict`, `Path=/api/v1/auth` cookie).
- FR-A3 Refresh rotates the token. Reuse of a consumed token revokes the whole token family.
- FR-A4 Log out revokes the family and clears the cookie.
- FR-A5 Verification and password-reset tokens are single-use and expire (24 h / 1 h).
  In development the link is printed to the server console (`[DEV EMAIL]`); no SMTP in MVP.
- FR-A6 Passwords are hashed with Argon2id; no plaintext or reversible storage.
- FR-A7 A user can only ever see their own projects, logs, keys, and installations.

### 4.2 Projects — FR-P

- FR-P1 Create a project with a name (1–80 chars) and optional description (≤ 500 chars).
- FR-P2 List own projects with log counts and last-log timestamp.
- FR-P3 Rename, edit description, toggle ingestion on/off, archive.
- FR-P4 Project names are labels. Routing uses the immutable project UUID.

### 4.3 Ingestion keys — FR-K

- FR-K1 Creating a project issues one ingestion key; plaintext is shown **once**.
- FR-K2 At most two unrevoked keys per project (rotation window).
- FR-K3 Rotation creates a new key; the old one may keep a grace window (`grace_until`, max 24 h)
  before automatic revocation.
- FR-K4 Revocation takes effect on the next request.
- FR-K5 Only a prefix (8 chars) and metadata are ever readable again.
- FR-K6 Keys are stored as HMAC-SHA256 digests with a server-side pepper.
- FR-K7 The settings UI shows integration instructions (ID + key placement, endpoint).

### 4.4 Ingestion — FR-I

- FR-I1 `POST /api/v1/ingest` accepts a batch of 1–500 log records.
- FR-I2 Envelope carries `schema_version`, `installation_id`, `client` metadata, optional
  `session_id`, and `logs[]`.
- FR-I3 Each record carries `log_id`, `timestamp`, `level`, `message`, optional `stack_trace`,
  optional `metadata`, optional `session_id`.
- FR-I4 Levels: `debug`, `info`, `warning`, `error`, `fatal`.
- FR-I5 `(project_id, log_id)` is unique. Re-submitting a record is a duplicate, not an error.
- FR-I6 The installation row is upserted (`last_seen_at`, platform, OS, app version).
- FR-I7 The response reports per-batch `accepted`, `duplicates`, and `rejected[]` with codes.
- FR-I8 A log is acknowledged only after the transaction commits.
- FR-I9 Revoked key → 401; ingestion disabled → 403; blocked installation → 403;
  oversized payload → 413; rate limited → 429 with `Retry-After`.
- FR-I10 Per-key limits: 60 requests/min, 20 000 records/day (configurable).

### 4.5 Viewing logs — FR-V

- FR-V1 Log list is ordered by server receive time, newest first, keyset paginated (cursor).
- FR-V2 Filters: level(s), time range, installation, free-text substring on message.
- FR-V3 Selecting a row shows full detail: message, stack trace, metadata, timestamps, device,
  app version, session ID.
- FR-V4 Dashboard shows per-project counts (24 h / 7 d), last log time, error count.
- FR-V5 Empty, loading, error, and stream-disconnected states are explicit in the UI.
- FR-V6 Owner can purge all logs of a project (typed confirmation).

### 4.6 Live updates — FR-R

- FR-R1 `GET /api/v1/projects/{id}/stream` delivers committed logs as SSE.
- FR-R2 Authorization is evaluated on connect; a user cannot subscribe to another's project.
- FR-R3 Heartbeats every 15 s. Bounded per-subscriber buffer; overflow emits `event: overflow`.
- FR-R4 Client reconnects with backoff and backfills gaps from the REST API using its cursor.
- FR-R5 Target latency: p95 ≤ 250 ms from commit to browser render (single instance, localhost/LAN).

### 4.7 Installations — FR-D

- FR-D1 Installations are listed per project with label, platform, OS, app version, last seen.
- FR-D2 Labels are owner-editable.
- FR-D3 Blocking an installation rejects its future batches; history is retained.
- FR-D4 Reinstall produces a new installation. No hardware identifiers are collected.

## 5. Non-functional requirements

| ID | Requirement | Target |
|---|---|---|
| NFR-1 | Ingest latency (p95) | ≤ 100 ms per batch, 500 records |
| NFR-2 | Live latency (p95) | ≤ 250 ms commit → render, single instance |
| NFR-3 | Log query (p95) | ≤ 300 ms over a 30-day, 300k-row project |
| NFR-4 | Durability | Acknowledged only after commit |
| NFR-5 | Isolation | 100 % of endpoints covered by cross-tenant negative tests |
| NFR-6 | Secret hygiene | No credential material in git, app logs, or client-readable storage |
| NFR-7 | Retention | 30 days default, env-configurable |
| NFR-8 | Scale (MVP) | ≤ 50 users, ≤ 10k logs/day, ≤ 5 installs/project |
| NFR-9 | Operability | One command to run API, one to run web; `/api/v1/health` reports DB state |

## 6. MVP scope

**In:** accounts, auth, projects, keys, ingestion, dedupe, quotas, installations, log list with
filters + cursor paging, log detail, SSE stream, retention sweeper, project purge, audit records
for sensitive actions, docs.

**Out:** teams/roles, OAuth/SSO, alerts/notifications, analytics, exports, public read API, saved
searches, metadata-field filtering, full-text index, per-device keys, device attestation,
server-side PII redaction, multi-region, billing.

## 7. Assumptions

| ID | Assumption | Impact if wrong |
|---|---|---|
| A1 | Scale tier S: ≤ 50 users, ≤ 10k logs/day, ~500 B/record (~150 MB/month) | Schema and indexes are growth-oriented; upgrade triggers documented |
| A2 | Single API instance is acceptable for MVP | Multi-instance needs Redis pub/sub for fan-out |
| A3 | Logs may contain PII or secrets | Redaction is the client's responsibility; retention and purge are provided |
| A4 | Windows is the development platform | Linux hosting is planned, not implemented |
| A5 | Project keys are extractable from shipped apps | Key is a write-only capability, not a secret; limits and rotation are the mitigation |
| A6 | SQL Server Express (10 GB) is sufficient for years at tier S | Revisit at 3 GB; Postgres migration path documented |

## 8. Open questions (deferred, non-blocking)

1. Real email provider (Resend/SES/SMTP) — interface is in place, provider is not.
2. Whether device blocking should also be available per installation *per key*.
3. Export format and delivery (CSV/JSON, signed URL) — post-MVP.
4. Whether archived projects should auto-purge logs after 7 days.
5. Hosting platform, TLS termination, backup window (Phase 8).

## 9. Reference package

`ql_logger_flutter` 0.0.6 (MIT-0) is a **reference only**; no compatibility is required and no
source is copied. Useful patterns: two-line init, one-call logging, periodic flush, upload
callbacks, Dio interceptor. Rejected patterns: text-blob payloads, project-name routing,
whole-file upload + clear, `androidInfo.id` as device identity, permission inventory upload.
See `docs/architecture.md` §8.
