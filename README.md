# Flutter Logs

Application logging and monitoring for Flutter apps: projects, write-only ingestion keys,
structured log ingestion, and a live dashboard.

**Status: Phase 3 — projects & ingestion keys.** Project CRUD, write-only ingestion keys with
one-time plaintext display, rotation with a grace window, revocation, activity stats, and audit
events are implemented. Log ingestion arrives in Phase 4.

## Repository layout

```
api/                     ASP.NET Core 9 solution
  src/FlutterLogs.Api/   Minimal API: health, auth, projects, keys, problem details, correlation IDs
  tests/…Tests/          xUnit + WebApplicationFactory integration tests
web/                     React 19 + Vite 8 + TypeScript + Tailwind v4 dashboard shell
docs/                    Requirements, architecture, data model, API contract, security
docs/decisions/          Architecture decision records (ADR-0001…0008)
```

## Prerequisites

| Tool | Version | Notes |
|---|---|---|
| .NET SDK | 9.0.2xx | Pinned in `global.json`; see ADR-0008 for the .NET 10 upgrade plan |
| Node.js | 22.x | `npm` 10+ |
| SQL Server | 2022 Express | Instance `.\SQLEXPRESS`, Windows authentication |
| dotnet-ef | 9.0.22 | Installed as a local tool in `api/.config` |

## Setup

```powershell
# 1. Restore the EF tool (once)
dotnet tool restore

# 2. Create/migrate the development database
dotnet ef database update --project api/src/FlutterLogs.Api --startup-project api/src/FlutterLogs.Api

# 3. Configure the required secrets (once per machine)
dotnet user-secrets set "Jwt:SigningKey" "<random 32+ byte string>" --project api/src/FlutterLogs.Api
dotnet user-secrets set "Ingest:Pepper" "<random 32+ byte string>" --project api/src/FlutterLogs.Api

# 4. Install frontend dependencies (once)
cd web; npm install; cd ..
```

## Run

```powershell
# Terminal 1 — API on http://localhost:5080
dotnet run --project api/src/FlutterLogs.Api --launch-profile http

# Terminal 2 — dashboard on http://localhost:5173
cd web
npm run dev
```

The Vite dev server proxies `/api` to the API, so the dashboard at
`http://localhost:5173` verifies the whole chain: React → proxy → ASP.NET → SQL Server.

Dashboard routes: `/login`, `/register`, `/verify-email`, `/forgot-password`,
`/reset-password`, the protected `/` projects list, and `/projects/:projectId` for project
detail, keys, settings, and integration instructions.

In Development, `Auth:DevEmailMode` prints verification and password-reset links to the API
console (prefixed `[DEV EMAIL]`) instead of sending email. No SMTP server is required.

Useful endpoints in Development:

| URL | Purpose |
|---|---|
| `GET http://localhost:5080/api/v1/health` | API + database health |
| `http://localhost:5080/scalar/v1` | Interactive API reference (Scalar) |
| `http://localhost:5080/openapi/v1.json` | OpenAPI document |

## Test and verify

```powershell
dotnet build api/FlutterLogs.sln
dotnet test api/FlutterLogs.sln

cd web
npm run typecheck
npm run lint
npm test
npm run build
```

Integration tests create and migrate `FlutterLogs_Test` on the local SQL Server Express instance.
Override the target with `FLUTTERLOGS_TEST_CONNECTION`. Docker is not required.

## Configuration

`api/src/FlutterLogs.Api/appsettings.json` holds non-secret development defaults, including a
Windows-authenticated connection string (no password). Anything sensitive must go to user-secrets
or environment variables:

```powershell
dotnet user-secrets set "Jwt:SigningKey" "<32+ char random string>" --project api/src/FlutterLogs.Api
dotnet user-secrets set "Ingest:Pepper" "<32+ char random string>" --project api/src/FlutterLogs.Api
```

User-secrets use `:` separators. In environment variables (for example in deployment) the same
keys are written with double underscores, such as `Jwt__SigningKey`.

| Key | Purpose | Secret |
|---|---|---|
| `ConnectionStrings__Default` | SQL Server connection | No in dev, yes in production |
| `Jwt__SigningKey` | Access-token signing key (Phase 2) | **Yes** |
| `Jwt__Issuer`, `Jwt__Audience` | Token validation (Phase 2) | No |
| `Ingest__Pepper` | HMAC pepper for ingestion keys (Phase 3) | **Yes** |
| `Logs__RetentionDays` | Log retention window | No |
| `Ingest__MaxRecordsPerBatch` | Batch size cap | No |
| `Auth__CookieSecure` | `Secure` flag on the refresh cookie (false only for local HTTP) | No |
| `Auth__DevEmailMode` | Print verification/reset links to the console | No |
| `Auth__PublicBaseUrl` | Base URL used in emailed links | No |

## Documentation

- [`docs/requirements.md`](docs/requirements.md) — scope, functional and non-functional requirements
- [`docs/architecture.md`](docs/architecture.md) — components, flows, scaling triggers
- [`docs/data-model.md`](docs/data-model.md) — tables, indexes, retention
- [`docs/api-contract.md`](docs/api-contract.md) — endpoints and the proposed ingestion contract
- [`docs/security.md`](docs/security.md) — threat model and controls
- [`docs/implementation-plan.md`](docs/implementation-plan.md) — phased roadmap
- [`docs/decisions/`](docs/decisions) — ADRs

## Security notes

- Logs are acknowledged only after the database transaction commits.
- Ingestion keys are write-only, scoped to one project, and revocable; they are not secrets that
  can be kept confidential inside a shipped app. The plaintext is shown once at creation or
  rotation; only a prefix and HMAC-SHA256 hash are stored.
- A project name is a label, never an identifier or a credential.
- Website users, ingestion keys, and application installations are distinct identities.

## Roadmap

Phase 1 foundation → 2 auth → 3 projects & keys → 4 ingestion → 5 live viewer → 6 installations →
7 hardening → 8 deployment → 9 Flutter plugin. One phase per review, no automatic progression.
