# ADR-0007 — Retention via batched deletes; no partitioning in MVP

Status: Accepted · 2026-10-29

## Context

Logs are append-only and grow without bound. At tier S the table reaches roughly 300k rows per
30-day window (~150 MB). Retention must be enforced automatically and reliably.

## Decision

- Default retention: 30 days on `received_at`, configurable via `Logs__RetentionDays`.
- A nightly `IHostedService` deletes in batches of 5 000 with a short pause, looping until clean.
- Owner-initiated project purge uses the same batching.
- No table partitioning in the MVP.

## Alternatives

- **Monthly partitioning + `DROP PARTITION`** — cheaper deletes at high volume, but adds DDL
  automation and complexity that is unjustified at 10k logs/day.
- **Soft delete + archive table** — extra storage and query complexity for no MVP benefit.
- **No retention** — unbounded growth and a privacy liability.

## Consequences

- Deletes are cheap enough at tier S; the sweeper keeps transactions short to protect the log.
- Triggers for partitioning are documented (`docs/architecture.md` §7): > 3 GB, > 100k logs/day,
  or delete windows exceeding a few minutes.
- Purging is irreversible; the UI requires a typed confirmation.
