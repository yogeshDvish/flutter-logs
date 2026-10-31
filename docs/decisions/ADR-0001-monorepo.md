# ADR-0001 — Monorepo with separate React and ASP.NET Core deployables

Status: Accepted · 2026-10-29

## Context

The workspace was a standalone Vite + React scaffold with no backend, no database, and no git
repository. A dashboard, an HTTP ingestion API, and a relational store are required.

## Decision

One repository with three top-level areas: `web/` (React 19 + Vite + TypeScript), `api/`
(ASP.NET Core solution), `docs/`. Two deployables, one source of truth. No shared code package:
the API contract in `docs/api-contract.md` is the interface.

## Alternatives

- **Separate repositories** — more isolation, more coordination overhead for a single developer.
- **Single ASP.NET app serving a compiled SPA** — one deployable, but couples frontend build and
  backend release, and loses the Vite DX.
- **Next.js full-stack** — would discard the existing React choice and mix API concerns into the
  web tier.

## Consequences

- Independent deploys and clear boundaries.
- Two toolchains in one repo (npm + dotnet); both are documented in the README.
- Duplicate type definitions for API DTOs; accepted for now, revisit only if drift causes bugs.
