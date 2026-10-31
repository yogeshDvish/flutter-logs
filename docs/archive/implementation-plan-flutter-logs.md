# Implementation Plan — Flutter Logs (Archived)

> **SUPERSEDED:** This roadmap targets the original Flutter Logs product. It was superseded by the SealedTrace plan in [`../implementation-plan.md`](../implementation-plan.md), derived from [`../code.txt`](../code.txt). This file is retained for historical reference only. Do not implement new work from this plan.

Status: archived · originally approved (Phase 0) · Phase 3 complete · Phase 4 was not started
Execution rule: **one approved phase, one implementation cycle, one review checkpoint.**

## 1. Fixed decisions

| # | Decision |
|---|---|
| 1 | React + Vite + TypeScript dashboard; Flutter/Dart plugin in Phase 9 |
| 2 | ASP.NET Core 9 backend (upgrade to 10 tracked in ADR-0008) |
| 3 | SQL Server 2022 Express (SQL Server provider) |
| 4 | JWT access token in memory + rotating refresh cookie |
| 5 | Owner-only tenancy |
| 6 | p95 ≤ 250 ms live latency, single instance, LAN |
| 7 | Installation-ID device model, no hardware identifiers |
| 8 | 30-day retention, owner purge, no export/alerts/public API in MVP |
| 9 | Local-first; hosting in Phase 8 |
| 10 | Dev-mode email (console links); provider later |
| 11 | Open registration with quotas |
| 12 | Scale tier S: ≤ 50 users, ≤ 10k logs/day |

## 2. Phases

| Phase | Scope | Key deliverables | Acceptance criteria | Effort |
|---|---|---|---|---|
| 0 | Discovery & plan | Requirements, architecture, data model, API contract, security, this plan, ADRs | Plan approved | done |
| 1 | Foundation | Monorepo (`web/`, `api/`, `docs/`), TS + Tailwind, ASP.NET solution, EF Core + SQL Server, `/health`, correlation IDs, ProblemDetails, Serilog, test harnesses, `.gitignore`, README | Builds and tests green; `/health` reports DB; migration applies; no secrets tracked | 2–3 d |
| 2 | Auth & accounts | Register/login/refresh/logout/me, Argon2id, refresh families, dev email tokens, protected shell, login/register UI, route guard | Auth matrix green; no token in JS-readable storage; cross-user access denied | 4–6 d |
| 3 | Projects & keys | Project CRUD, key create/rotate/revoke, show-once UI, integration instructions, stats, audit | Plaintext returned once only; 2-key rotation; cross-tenant 404 | 3–4 d |
| 4 | Ingestion | `/ingest`, key auth handler, validation, dedupe, installation upsert, quotas, daily stats, simulated client | Golden payloads; duplicates counted; 401/403/413/429 verified | 4–6 d |
| 5 | Real-time & viewer | SSE stream, fan-out, overflow, catch-up, log table, filters, cursor paging, detail drawer, states | p95 < 250 ms locally; reconnect gap-free; no cross-project events | 4–6 d |
| 6 | Installations | List, labels, block/unblock, device filter, last seen | Blocked install gets 403; filters isolate; labels persist | 2–3 d |
| 7 | Hardening | Audit surface, CSP/headers, rate-limit tuning, retention sweeper, purge, Serilog/correlation, load test, authz matrix | k6 targets met; authz matrix 100 %; retention deletes; audit complete | 3–5 d |
| 8 | Deployment & docs | Hosting, TLS, migrations as release step, backups + restore drill, runbook, README | HTTPS instance; restore verified; docs accurate | 2–4 d |
| 9 | Flutter plugin | Dart package: buffer, retry, redaction, levels, tests, example app, E2E | Real device logs arrive; offline buffer survives restart; revoked key fails safely | 5–8 d |

Total ≈ 30–45 dev-days.

## 3. Phase 1 detail

**In:** monorepo restructure; JS→TS migration; Tailwind v4; app shell; ASP.NET Core solution;
EF Core + `InitialCreate` migration; `/api/v1/health` with DB probe; correlation IDs;
ProblemDetails for 404/405/500; Serilog; xUnit + WebApplicationFactory smoke tests;
Vitest + RTL smoke test; docs; `.gitignore`; README.

**Out:** auth, domain tables, ingestion, SSE, dashboard screens, routing, TanStack Query, CI,
hosting, Docker.

**Prerequisites:** .NET 9 SDK ✅, Node 22 ✅, SQL Server Express ✅ (`.\SQLEXPRESS`, Windows auth),
`dotnet-ef` local tool.

**Verification commands**
```
dotnet build api/FlutterLogs.sln
dotnet test api/FlutterLogs.sln
dotnet ef database update --project api/src/FlutterLogs.Api
dotnet run --project api/src/FlutterLogs.Api   # GET /health
cd web; npm run lint; npm test; npm run build; npm run dev
```

## 4. Dependencies

| Phase | Requires |
|---|---|
| 2 | Phase 1; `Jwt__SigningKey` secret; Argon2id package |
| 3 | Phase 2; `Ingest__Pepper` secret |
| 4 | Phase 3; key auth handler |
| 5 | Phase 4; SSE infrastructure |
| 6 | Phase 4 |
| 7 | Phases 4–6; k6 binary for load tests |
| 8 | All previous; hosting decision and budget |
| 9 | Phases 4–5; Flutter SDK ✅ |

## 5. Risks

| Risk | Likelihood | Impact | Mitigation |
|---|---|---|---|
| .NET 9 is out of support | High | Medium | ADR-0008: upgrade to .NET 10 in a dedicated change; pinned SDK via `global.json` |
| Scale assumption wrong | Medium | Medium | Growth-oriented indexes; explicit upgrade triggers |
| SQL Server → Postgres need | Low | Medium | No provider-specific SQL; regeneration of migrations is contained |
| Text search slow without FTS | Medium | Low | Filtered `LIKE` at tier S; measure in Phase 7 |
| Single-instance assumption | Low | Medium | `ILogStream` seam allows Redis pub/sub without endpoint changes |
| 10 GB Express cap | Low | Low | Tracked at 3 GB; retention enforced |
| Key extraction from app | High | Medium | Write-only capability, quotas, rotation, revoke |
| Solo-maintainer scope creep | Medium | High | One phase per approval; MVP exclusions documented |

## 6. Deferred decisions

OAuth/SSO · teams & roles · alerts/notifications · exports · public API · analytics ·
saved searches · metadata-field filters · full-text index · Redis / multi-instance ·
partitioning · SQL Server RLS · per-device keys · device attestation · real email provider ·
dark mode · CSP enforcement mode · billing.

## 7. Change control

An approved phase may not expand silently. If implementation reveals a decision that affects
security, data integrity, compatibility, cost, or scope, work stops and the question is raised.
Approval of the roadmap is not approval to execute any phase; each phase starts only on an
explicit instruction.

## 8. Status

| Phase | Status |
|---|---|
| 0 Discovery & plan | done |
| 1 Foundation | done |
| 2 Auth & accounts | done |
| 3 Projects & keys | done |
| 4 Ingestion | next |
| 5 Real-time & viewer | planned |
| 6 Installations | planned |
| 7 Hardening | planned |
| 8 Deployment & docs | planned |
| 9 Flutter plugin | planned |

Phase 3 verification: `dotnet test api/FlutterLogs.sln` (27 passing), `npm run typecheck`,
`npm run lint`, `npm test` (17 passing), and `npm run build` all green. The acceptance checks are
covered by integration tests: plaintext is returned once, rotation keeps at most two effective
active keys with a configurable grace window, and cross-tenant project/key ids return `404`.
