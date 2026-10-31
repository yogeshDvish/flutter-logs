# ADR-0004 — Project-scoped, hashed, write-only ingestion keys

Status: Accepted · 2026-10-29

## Context

A Flutter app distributed to users must send logs. Any credential embedded in a shipped binary can
be extracted. Using the project name as an identifier is unsafe (mutable, non-unique) and using it
as a secret is worse.

## Decision

- Configuration is `project_id` (immutable UUID, public) + `ingestion_key`.
- Key format: `flk_<env>_<8-char prefix>_<43-char base64url secret>`.
- Stored as `HMAC-SHA256(secret, server_pepper)` plus the non-secret prefix; plaintext shown once.
- Scope: write-only, exactly one project, accepted only by `POST /ingest`.
- At most two unrevoked keys per project; rotation optionally keeps the old key for ≤ 24 h.
- Revocation is immediate. Per-key limits: 60 req/min, 20k records/day.

## Alternatives

- **Project name + shared secret** — rejected: names are not identifiers and collide.
- **Same key for reads and writes** — rejected: an extracted key would expose all logs.
- **Per-device keys minted at runtime** — stronger attribution, but requires a provisioning
  service and an app-side auth flow that does not exist in the MVP.
- **JWT with embedded claims** — no benefit over a random opaque key for a single scope.

## Consequences

- Key leakage means log injection into one project, bounded by quotas and reversible by rotation.
  This is stated as a residual risk, not solved.
- The key is not device identity; `installation_id` carries that role.
- Rotation UX must be explicit because the plaintext cannot be recovered later.
