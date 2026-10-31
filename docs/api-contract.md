# API Contract

Status: approved baseline (Phase 0), Phase 3 endpoints implemented · Last updated: 2026-10-30
Base path: `/api/v1` · Media type: `application/json` (except the stream)

## 1. Conventions

- **User auth:** `Authorization: Bearer <access JWT>` (15 min). Refresh token lives only in the
  `fl_refresh` `httpOnly` cookie, path `/api/v1/auth`.
- **Ingestion auth:** `Authorization: Bearer flk_<env>_<8-char-prefix>_<43-char-secret>`.
  Ingestion keys are accepted **only** by `POST /ingest`.
- IDs are UUIDs. `log_id` is client-generated; internal bigint ids are never exposed.
- Errors use RFC 7807 `ProblemDetails` plus `code` and `X-Correlation-Id`.
- Pagination is keyset-based: `?limit=50&cursor=<opaque>`; responses return `nextCursor`.
- All timestamps are ISO-8601 UTC.

### Error codes

| HTTP | `code` examples | Meaning |
|---|---|---|
| 400 | `invalid_payload`, `validation_failed` | Malformed or invalid request |
| 401 | `invalid_credentials`, `token_expired`, `invalid_key` | Auth failed |
| 403 | `ingest_disabled`, `installation_blocked`, `invalid_origin` | Authenticated but not permitted |
| 404 | `not_found` | Absent **or** not owned (no existence oracle) |
| 409 | `project_limit_reached`, `key_limit_reached`, `project_archived` | Project or key rules violated |
| 413 | `payload_too_large` | Body or field cap exceeded |
| 429 | `rate_limited` | Includes `Retry-After` seconds |
| 500 | `internal_error` | Retryable with backoff; no stack traces leaked |

## 2. Dashboard endpoints

| Method | Path | Purpose | Access |
|---|---|---|---|
| POST | `/auth/register` | Create account | public, 5/h/IP |
| POST | `/auth/login` | Issue tokens | public, 10/min/IP |
| POST | `/auth/refresh` | Rotate refresh token | refresh cookie |
| POST | `/auth/logout` | Revoke family | refresh cookie |
| GET | `/auth/me` | Current user | user |
| POST | `/auth/verify-email` | Consume verification token | public |
| POST | `/auth/resend-verification` | Re-issue verification | user |
| POST | `/auth/forgot-password` | Issue reset token | public, 5/h/IP |
| POST | `/auth/reset-password` | Consume reset token | public |
| GET | `/projects` | List owned projects + stats | user |
| POST | `/projects` | Create project | user, 20/user cap |
| GET | `/projects/{projectId}` | Project detail | owner |
| PATCH | `/projects/{projectId}` | Rename / describe / toggle ingest | owner |
| DELETE | `/projects/{projectId}` | Archive (or `?purge=true`) | owner |
| GET | `/projects/{projectId}/keys` | Key metadata (prefix + dates) | owner |
| POST | `/projects/{projectId}/keys` | Create or rotate (plaintext once) | owner |
| DELETE | `/projects/{projectId}/keys/{keyId}` | Revoke | owner |
| GET | `/projects/{projectId}/logs` | Filtered, cursor-paged list | owner |
| GET | `/projects/{projectId}/logs/{logId}` | Single log | owner |
| DELETE | `/projects/{projectId}/logs` | Purge all logs | owner |
| GET | `/projects/{projectId}/stats` | Counts by level, 24 h / 7 d | owner |
| GET | `/projects/{projectId}/installations` | Installations + last seen | owner |
| PATCH | `/projects/{projectId}/installations/{installationId}` | Set label | owner |
| POST | `/projects/{projectId}/installations/{installationId}/block` | Block ingestion | owner |
| POST | `/projects/{projectId}/installations/{installationId}/unblock` | Unblock | owner |
| GET | `/projects/{projectId}/stream` | SSE log stream | owner |
| POST | `/ingest` | Batch ingestion | **project key** |
| GET | `/health` | Liveness + DB probe (served at `/api/v1/health`) | public |

## 3. Auth examples

`POST /auth/login`
```json
{ "email": "dev@example.com", "password": "correct horse battery staple" }
```
`200` — body contains the access token; the refresh token is set as a cookie:
```json
{
  "accessToken": "eyJ...",
  "tokenType": "Bearer",
  "expiresIn": 900,
  "user": {
    "id": "…",
    "email": "dev@example.com",
    "displayName": "Dev",
    "emailVerified": true,
    "createdAt": "2026-10-29T00:00:00Z"
  }
}
```

`POST /auth/register` → `201` with the same `user` object. Duplicate address → `409 email_taken`;
invalid input → `400 validation_failed` with an `errors` map keyed by field name.

`POST /auth/refresh` → `200` with a rotated access token, the same `tokenType`/`expiresIn`/`user`
shape, and a replacement `fl_refresh` cookie.
A reused refresh token → `401 token_reuse_detected` and the whole family is revoked.

## 4. Projects and ingestion keys

All project and key endpoints require a user access token. Cross-tenant ids return
`404 not_found` (never `403`) so the API does not reveal whether another account owns a project.

`GET /projects?includeArchived=false` → `200`:
```json
[
  {
    "id": "…",
    "name": "Mobile app",
    "description": "Production Flutter app",
    "ingestEnabled": true,
    "createdAt": "2026-10-01T10:00:00Z",
    "updatedAt": "2026-10-01T10:00:00Z",
    "archivedAt": null,
    "activeKeyCount": 1,
    "stats": {
      "last24Hours": { "total": 120, "errors": 3 },
      "last7Days": { "total": 840, "errors": 12 },
      "lastLogAt": null
    }
  }
]
```

`POST /projects` with `{ "name": "Mobile app", "description": "Production Flutter app" }` →
`201`. The response contains the project and the **only** plaintext copy of its first key:
```json
{
  "project": { "…": "same shape as the list item" },
  "key": {
    "id": "…",
    "prefix": "Ab3dE9f1",
    "label": null,
    "createdAt": "2026-10-01T10:00:00Z",
    "graceUntil": null,
    "plaintext": "flk_live_Ab3dE9f1_<43-char base64url secret>"
  }
}
```

`PATCH /projects/{projectId}` accepts any subset of `name`, `description`, and `ingestEnabled`.
Send an empty `description` to clear it. `DELETE /projects/{projectId}` archives the project
(soft delete, ingestion disabled) and returns `204`; add `?purge=true` to delete it permanently.
`GET /projects/{projectId}/stats` returns `projectId`, `last24Hours`, `last7Days`, and
`lastLogAt`. Until Phase 4 ingests logs, both buckets are zero and `lastLogAt` is `null`.

### Keys

`GET /projects/{projectId}/keys` returns metadata only; plaintext is never included:
```json
[
  {
    "id": "…",
    "prefix": "Ab3dE9f1",
    "label": "Primary",
    "status": "active",
    "createdAt": "2026-10-01T10:00:00Z",
    "lastUsedAt": null,
    "graceUntil": null,
    "revokedAt": null
  }
]
```
`status` is `active`, `grace`, `expired`, or `revoked`.

`POST /projects/{projectId}/keys` issues a key and returns its plaintext once:
```json
{ "label": "Secondary", "graceHours": 24 }
```
The response has the same shape as the `key` object in the project-creation example. When the
project already has an active key this is a rotation: existing keys enter the grace window.
`graceHours` accepts `0`–`24` (default `24`); `0` revokes existing keys immediately. A project
may have at most two effective active keys, otherwise the API returns `409 key_limit_reached`.
`DELETE /projects/{projectId}/keys/{keyId}` revokes a key immediately and returns `204`.

Key format: `flk_<env>_<8-char prefix>_<43-char base64url secret>`. Only the prefix and an
HMAC-SHA256 hash are stored. The plaintext is shown once at creation or rotation and cannot be
recovered.

## 5. Log query

`GET /projects/{id}/logs?level=error,fatal&from=2026-10-28T00:00:00Z&to=2026-10-29T00:00:00Z&installationId=<uuid>&q=timeout&limit=50&cursor=...`

```json
{
  "items": [
    {
      "logId": "018f2ca0-11a1-7d10-8f00-5b6c7d8e9f01",
      "level": "error",
      "message": "Checkout failed",
      "stackTrace": "#0 ...",
      "metadata": { "order_id": "A-1029" },
      "clientTimestamp": "2026-10-29T10:11:12.345Z",
      "receivedAt": "2026-10-29T10:11:12.401Z",
      "appVersion": "1.2.3+45",
      "platform": "android",
      "osVersion": "14",
      "sessionId": "018f2c9e-8a10-7b3c-9e55-1c2d3e4f5a6b",
      "installation": { "id": "…", "label": "Pixel 8", "installationId": "…" }
    }
  ],
  "nextCursor": "eyJyIjoiMjAyNi0xMC0yOVQxMDoxMToxMi40MDFaIiwiaSI6MTIzNH0"
}
```

`q` is a case-insensitive substring match on `message` in the MVP. `level` accepts a
comma-separated set. Maximum window without `from`/`to` is 7 days.

## 6. Ingestion contract (proposed — Phase 9 client must conform)

`POST /api/v1/ingest`
```
Authorization: Bearer flk_live_Ab3dE9f1_<43 chars base64url>
Content-Type: application/json
Content-Encoding: gzip            (optional)
```

```json
{
  "schema_version": 1,
  "installation_id": "018f2c9e-7f3a-7c2b-9d41-6a0f5e2b8c11",
  "client": {
    "platform": "android",
    "os_version": "14",
    "app_version": "1.2.3+45",
    "app_build": "45",
    "label": null
  },
  "session_id": "018f2c9e-8a10-7b3c-9e55-1c2d3e4f5a6b",
  "logs": [
    {
      "log_id": "018f2ca0-11a1-7d10-8f00-5b6c7d8e9f01",
      "timestamp": "2026-10-29T10:11:12.345Z",
      "level": "error",
      "message": "Checkout failed",
      "stack_trace": "#0 CheckoutService.pay (package:app/checkout.dart:42)",
      "metadata": { "order_id": "A-1029", "retry": 2 },
      "session_id": null
    }
  ]
}
```

### Limits

| Item | Limit |
|---|---|
| Body | 1 MB (after decompression) |
| Records per batch | 500 |
| `message` | 2 000 chars |
| `stack_trace` | 64 KB |
| `metadata` (serialized) | 16 KB |
| `session_id` | 64 chars |
| Requests per key | 60/min |
| Records per key per day | 20 000 |

### Responses

`200` — committed:
```json
{
  "accepted": 12,
  "duplicates": 1,
  "rejected": [{ "log_id": "018f2ca0-…", "code": "message_too_long" }],
  "server_time": "2026-10-29T10:11:14Z"
}
```

| Status | When | Client action |
|---|---|---|
| 200 | Some or all records stored; inspect `rejected` | Drop accepted/duplicates, re-queue nothing |
| 400 | Envelope invalid | Do not retry; surface a configuration error |
| 401 | Unknown/revoked key | Stop retrying; surface a setup error |
| 403 | Ingest disabled or installation blocked | Stop retrying |
| 413 | Batch too large | Split and retry |
| 429 | Rate limited | Wait `Retry-After`, then retry |
| 5xx / timeout | Server or network failure | Retry with exponential backoff |

Duplicate `log_id`s are counted, never rejected. Ingestion is idempotent per record.

## 7. Live stream

`GET /projects/{id}/stream?since=<cursor>` → `text/event-stream`

```
event: log
id: 2026-10-29T10:11:12.401Z-1234
data: {"logId":"…","level":"error","message":"Checkout failed","receivedAt":"…"}

event: overflow
data: {"reason":"subscriber_slow","cursor":"2026-10-29T10:11:12.401Z-1234"}

: heartbeat
```

- `since` triggers replay of up to 500 committed records, then live delivery.
- On `overflow`, the client refetches via REST from its cursor.
- Heartbeat comment every 15 s.
- The stream is ephemeral: it is never the source of truth for durability.

## 8. Versioning

`schema_version` is an integer in the ingestion envelope and in the URL path (`/api/v1`).
Additive fields are backward compatible; removals or semantic changes require `/api/v2`.
The server rejects unknown `schema_version` values with `400 unsupported_schema_version`.
