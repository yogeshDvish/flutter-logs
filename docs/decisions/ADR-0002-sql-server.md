# ADR-0002 — SQL Server Express as the MVP database

Status: Accepted · 2026-10-29

## Context

At tier S (≤ 50 users, ≤ 10k logs/day, ~500 B/record) the workload is ~5 MB/day. The development
machine has SQL Server 2022 Express running (`MSSQL$SQLEXPRESS`) and no PostgreSQL installation or
Docker. A relational store with strong uniqueness and range indexes is sufficient; no search
engine or time-series database is justified.

## Decision

Use the existing SQL Server 2022 Express instance with EF Core 9 (`Microsoft.EntityFrameworkCore.SqlServer`).
Develop against `FlutterLogs`; tests use `FlutterLogs_Test`.

## Alternatives

- **PostgreSQL** — better long-term portability, `LISTEN/NOTIFY`, trigram search, cheaper cloud
  hosting. Requires an install now and offers no MVP feature that SQL Server lacks at this scale.
- **SQLite** — zero install, but no concurrent writer story, no server-side jobs, and a painful
  migration later.
- **Elasticsearch / ClickHouse** — premature; both add infrastructure with no measured need.

## Consequences

- Zero setup cost today; Express's 10 GB cap covers years at tier S.
- No `LISTEN/NOTIFY`; live fan-out is in-process (ADR-0005).
- Text search uses filtered `LIKE` until a measured trigger justifies SQL Server full-text.
- EF migrations are provider-specific: moving to Postgres later means regenerating migrations and
  re-validating SQL. Avoid provider-specific SQL in the meantime.
- Production SQL Server licensing would be expensive; Phase 8 must evaluate managed Postgres or
  Azure SQL Basic before committing to a paid host.
