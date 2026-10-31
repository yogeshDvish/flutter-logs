# SealedTrace Implementation Plan

Status: draft for review · Source spec: [`docs/code.txt`](code.txt) · Target: SealedTrace Server + Console · Spec-named alias: [`docs/IMPLEMENTATION.md`](IMPLEMENTATION.md)

> **Scope note:** `docs/code.txt` describes **SealedTrace**, a different product and stack from the existing Flutter Logs implementation. This plan supersedes the Flutter Logs roadmap. The previous plan is archived at [`docs/archive/implementation-plan-flutter-logs.md`](archive/implementation-plan-flutter-logs.md). The existing Flutter Logs code remains available as a reference for auth/EF/testing patterns, but it is not the target of this plan.

Execution rule: **one approved phase, one implementation cycle, one review checkpoint.** No code is to be written until this plan is approved.

---

## 1. Assumptions & Resolved Questions

The source spec is intentionally precise about the client-server contract and intentionally silent on several implementation details. The following decisions resolve those gaps. Each decision is binding for the first implementation pass unless the product owner overrides it.

| # | Question / ambiguity | Resolved decision | Rationale |
|---|---|---|---|
| A1 | The repo is Flutter Logs; `code.txt` describes SealedTrace | Treat SealedTrace as the new target. Archive the Flutter Logs plan. Start the server as `SealedTrace.sln` with `Arovyx.SealedTrace.*` namespaces and the console in `console/`. Reuse only patterns, not product assumptions. | The spec's contract, data model, and stack are incompatible with the existing plaintext Flutter Logs product. |
| A2 | .NET version | Use **.NET 8** exactly as specified. Add an ADR noting that .NET 8 reaches end of support in November 2026 and plan an upgrade to .NET 10 after the MVP. | The spec fixes .NET 8. The upgrade is a separate, low-risk platform change. |
| A3 | React version | Use **React 18** exactly as specified. Vite 5, TypeScript 5.x strict, React Router v6, TanStack Query v5, Tailwind 3.4 + shadcn/ui. | Matches the spec and the shadcn/ui ecosystem. |
| A4 | API-key encoding | Generate 32 random bytes with `RandomNumberGenerator`; encode as **base64url without padding** (43 chars). Display prefix = first 8 characters. Store only SHA-256 hash (`binary(32)`) and prefix. Send in `X-Api-Key`. | Base64url is header-safe and shorter than hex. 32 random bytes make SHA-256 sufficient; no pepper is required. |
| A5 | Project RSA key pair | One RSA-2048 key pair per project. Store public key as PEM. Encrypt the private-key PEM with AES-256-GCM using a 32-byte master key from `SealedTrace:MasterKey` (base64). Store `MasterKeyVersion` for rotation. | Matches the spec and supports future master-key rotation without re-encrypting every project at once. |
| A6 | Private-key ciphertext format | Store a versioned binary envelope: `[version:1][nonce:12][tag:16][ciphertext]`. Version 1 means AES-256-GCM with a 12-byte nonce and 16-byte tag. | Self-describing, compact, and easy to migrate. |
| A7 | Master-key rotation | Support a key ring: current version encrypts new keys; older versions remain available for decryption until all project keys are re-encrypted. `MasterKeyVersion` selects the key. | Rotation is required for a production secret. The spec only requires the column, but a key ring makes it useful. |
| A8 | Project RSA key rotation | Not in the MVP. A project key pair is created once. Document a manual re-key procedure for a future migration; do not add a console button yet. | The spec has no key-rotation endpoint or UI. Adding one would expand the contract. |
| A9 | Missing project fields | The spec's `Projects` DDL omits `Description` and `Environment`, but the console requires a description in the create wizard and an environment on dashboard cards. Add `Description nvarchar(500) NULL` and `Environment nvarchar(16) NOT NULL DEFAULT 'prod'`. This is the only intentional schema extension. | The UI contract cannot be satisfied otherwise. Record the extension for the SDK/product team. |
| A10 | `ctx` fields not in the Logs DDL | The fixed `Logs` table stores only `AppVersion` and `Platform`. To avoid losing `buildNumber`, `os`, `locale`, `env`, and `network`, store the full `ctx` object in `MetadataJson` under a reserved `_ctx` key, alongside the normal `metadata` object. The console renders both. | Preserves the fixed DDL and avoids silent data loss. If strict metadata-only semantics are required later, add explicit columns in a migration. |
| A11 | API-key lifecycle | First key is issued during project creation and returned exactly once. Settings may create up to **5 active keys** per project. Revocation is soft (`RevokedAtUtc`). There is no “rotate” operation; create a new key, deploy it, then revoke the old one. | Matches the spec's “returned exactly once” and soft-revoke requirement while allowing safe rotation. |
| A12 | Refresh-token transport | JWT access token: 15 minutes, held in memory in the console. Refresh token: 30 days, random 32 bytes, SHA-256 hash stored, sent as an `HttpOnly`, `Secure`, `SameSite=Lax` cookie scoped to `/api/v1/auth`. Rotate on every refresh. | Avoids exposing refresh tokens to JavaScript/localStorage and supports rotation. |
| A13 | Refresh-token reuse detection | The fixed `RefreshTokens` schema has no family ID. On reuse of an already-revoked token, revoke **all** refresh tokens for that user and return `401 token_reuse_detected`. | Conservative, safe response without changing the fixed schema. |
| A14 | Ingest request semantics | Authenticated, well-formed batches return **202** even when individual envelopes fail. Per-envelope failures go in `rejected[]`; duplicate batches go in `duplicate[]`; successful batches go in `accepted[]`. Malformed JSON or a missing `envelopes` array returns 400. | The spec defines a 202 response with per-batch arrays; partial success is expected. |
| A15 | Ingest limits | Maximum **50 envelopes** and **2 MB** request body. Rate limit **600 requests/minute per API key**, fixed window. Return 413 for a body over 2 MB or more than 50 envelopes, and 429 with `Retry-After` for rate limiting. | Directly from the spec; 413 is the only “too large” error in the contract. |
| A16 | Batch deduplication | `IngestBatches.BatchId` is a global primary key. A duplicate batch ID is reported in `duplicate[]` and is not decrypted again. A batch ID already owned by a different project is rejected as `batch_id_conflict`. A duplicate `Logs.Id` inside a new batch is rejected as `duplicate_log_id` to protect the primary key. | The spec only mentions batch deduplication, but the fixed primary keys make log-ID collisions possible. Handling them explicitly avoids 500s. |
| A17 | Envelope validation | Require `v = 1`, exact `alg = "RSA-OAEP-2048-SHA256+AES-256-GCM"`, `projectId` equal to the API key's project, `eventCount` equal to the decrypted array length, `ts <= now + 5 minutes`, valid level enum, `sessionId` as UUID or null, and `userId` at most 128 chars. Reject invalid envelopes with a stable reason code. | Prevents cross-project replay, malformed data, and future-dated logs. |
| A18 | `createdAt` and clock skew | Accept envelope `createdAt` as supplied; require it to be a valid UTC timestamp. Do not reject old batches. Store `ReceivedAtUtc` from the server for retention and display. | Clients may retry queued batches after long offline periods. |
| A19 | Search implementation | Use SQL Server full-text `CONTAINS` when `Search:UseFullText=true` and the catalog exists. Otherwise use a parameterized `LIKE` fallback. The full-text catalog/index is created by a raw migration; Testcontainers environments may use the fallback. | Full-text is required for production, but the fallback keeps tests and local setups working without full-text installation. |
| A20 | Query defaults | Default range: last 24 hours when `from`/`to` are omitted. Maximum range: 90 days. Default `limit`: 50; maximum: 200. Cursor = base64 of `"<ClientTs>|<Id>"`. `from`/`to` filter `ClientTs`; “today” means the UTC day. | Prevents unbounded scans and makes pagination deterministic. |
| A21 | Stats response | Return `totalEvents`, `levels[]`, `eventsPerDay[]`, `topTags[]`, and `topUserIds[]` (top 10 each). Default range: last 7 days; maximum: 90 days. Stats filter `ClientTs`; null `UserId` values are excluded from top users. | The spec lists the dimensions but not the JSON shape; this shape is stable and chart-friendly. |
| A22 | Retention defaults | If a project's retention fields are null, use `Retention:DefaultMaxAgeDays = 30` and `Retention:DefaultMaxEvents = 1,000,000`. The job runs nightly at 03:00 UTC and deletes at most 10,000 rows per loop. | Matches “nullable = defaults” and avoids long table locks. |
| A23 | Project deletion | Hard delete with cascade to API keys, project keys, batches, and logs. The console requires the project name to be typed. No archive/recycle bin in the MVP. | The spec's danger zone says “delete project”; no archive requirement exists. |
| A24 | Slug generation | Generate a lowercase URL-safe slug from the project name; ensure uniqueness with a numeric suffix (`-2`, `-3`, …). Slug is immutable after creation. | Slugs are stable identifiers and should not churn on rename. |
| A25 | Multi-tenancy | Every project-scoped query filters by `OwnerId == currentUserId`. A resource owned by another user returns 404, not 403. | Avoids leaking the existence of other tenants' resources. |
| A26 | Validation library | Use FluentValidation for request DTOs; use data annotations only for simple transport constraints. | The spec allows either; FluentValidation keeps validation out of controllers. |
| A27 | Error format | All errors use RFC 7807 `application/problem+json` with `type`, `title`, `status`, `detail`, `code`, `traceId`, and optional `errors[]`. | Stable client handling and correlation. |
| A28 | Observability | Use `System.Diagnostics.Metrics` counters for ingest accepted/duplicate/rejected and decrypt failures. Expose `/metrics` via prometheus-net only when `Metrics:Enabled=true`. | The spec marks prometheus as optional; metrics counters are always useful. |
| A29 | Test database | Integration tests use Testcontainers MSSQL when Docker is available. If `SEALEDTRACE_TEST_CONNECTION` is set, use that SQL Server instead. Unit tests never require Docker. | Keeps CI reproducible and local development possible without Docker. |
| A30 | Console testing | Vitest + React Testing Library + MSW for API mocks. TypeScript strict with no `any`; all API types live in `src/types/api.ts`. | The spec requires strict types and a 90% Lighthouse score; component tests protect the critical flows. |
| A31 | Deployment scope | `docker-compose.yml` for local API + SQL Server is in scope. Cloud hosting, TDE, Azure Key Vault provisioning, and CI/CD pipelines are deployment follow-ups, not phases in this plan. | The spec asks for docker-compose as optional and mentions Key Vault as an upgrade path. |
| A32 | Swagger exposure | Swagger is enabled in Development and optionally in Staging behind auth; disabled in Production by default. It documents both Bearer and `X-Api-Key` schemes. | Avoids exposing the API surface in production while keeping local development productive. |
| A33 | CORS | Allow only configured console origins (`Cors:AllowedOrigins`). Never combine wildcard origins with credentials. | Required for cookie-based refresh tokens. |
| A34 | Time handling | All persisted timestamps are UTC `datetime2(3)`. API responses use ISO-8601 with `Z`. | Avoids timezone ambiguity across SDK platforms. |
| A35 | No code in this deliverable | This document is the implementation plan only. No C# or TypeScript application code is written until the plan is approved. | Explicit instruction in the spec. |
| A36 | Batch metadata semantics | `IngestBatches.EventCount` is the sum of the accepted envelopes' `eventCount`; `IngestBatches.EnvelopeSize` is the sum of the UTF-8 JSON byte lengths of those envelopes. `ProjectKeys.Algorithm` stores `RSA-OAEP-2048-SHA256`. | Makes the audit columns and key metadata unambiguous. |
| A37 | Console runtime | Node.js 20 LTS for Vite 5 and the test toolchain. | Avoids Node-version surprises in local/CI builds. |

### Open items that need product confirmation

These do not block the plan, but should be confirmed before the relevant phase:

1. **Project `Environment` values** — assume `prod`, `staging`, `dev`, `test` (lowercase, max 16 chars). Confirm if the product wants free text.
2. **Description length** — assume 500 characters. Confirm if the UI needs a longer field.
3. **Active API-key cap** — assume 5 per project. Confirm if unlimited is acceptable.
4. **Retention defaults** — assume 30 days / 1,000,000 events. Confirm the commercial default.
5. **Search fallback** — confirm whether production SQL Server will always have full-text installed; if not, the fallback becomes the primary path and should be load-tested.

---

## 2. System Architecture

### 2.1 High-level flow

```text
┌──────────────────────────────┐
│ Flutter app / sealed_trace   │
│ SDK                          │
│                              │
│ 1. Build LogEvent[]          │
│ 2. Generate AES-256 key      │
│ 3. AES-GCM encrypt payload   │
│ 4. RSA-OAEP-SHA256 wrap key  │
│ 5. POST SealedEnvelope[]     │
└──────────────┬───────────────┘
               │  X-Api-Key
               ▼
┌──────────────────────────────────────────────────────────────┐
│ SealedTrace.Api                                              │
│                                                              │
│  ApiKeyAuthenticationHandler ──► Project + key status        │
│  Rate limiter (600/min/key)                                  │
│  Body limit (2 MB) + 50-envelope limit                       │
│  IngestController                                            │
│       │                                                      │
│       ├──► IngestBatches dedupe (BatchId PK)                 │
│       │                                                      │
│       ▼                                                      │
│  EnvelopeDecryptor                                           │
│       ├── Load project private key from IMemoryCache         │
│       ├── Master-key unwrap (AES-256-GCM)                    │
│       ├── RSA-OAEP-SHA256 unwrap AES key                     │
│       ├── AES-256-GCM decrypt payload                        │
│       └── Parse + validate LogEvent[]                        │
│       │                                                      │
│       ▼                                                      │
│  One EF Core transaction: IngestBatch + Logs AddRange        │
└──────────────┬───────────────────────────────────────────────┘
               │
               ▼
┌──────────────────────────────┐        ┌──────────────────────────────┐
│ SQL Server                   │◄───────│ SealedTrace.Jobs             │
│                              │        │ RetentionBackgroundService   │
│ Users / Projects / ApiKeys   │        │ nightly 03:00 UTC            │
│ ProjectKeys / IngestBatches  │        │ batched DELETE 10k/loop      │
│ Logs / RefreshTokens         │        └──────────────────────────────┘
│ FULLTEXT on Logs.Message     │
└──────────────┬───────────────┘
               │
               │ JWT + ownership check
               ▼
┌──────────────────────────────────────────────────────────────┐
│ SealedTrace.Api Query/Stats controllers                      │
│  keyset pagination on (ClientTs DESC, Id DESC)               │
│  filters: level, tag, from, to, userId, sessionId, search    │
│  stats: levels, events/day, top tags, top users              │
└──────────────┬───────────────────────────────────────────────┘
               │ HTTPS / JSON
               ▼
┌──────────────────────────────────────────────────────────────┐
│ SealedTrace Console (React 18)                               │
│ Auth · Dashboard · Create Wizard · Log Explorer · Stats      │
│ Settings · one-time API key + public key + Flutter snippet   │
└──────────────────────────────────────────────────────────────┘
```

### 2.2 Key issuance flow

```text
Console                API                         SQL Server
  │ POST /projects       │                              │
  ├─────────────────────►│                              │
  │                      │ 1. Create Project row        │
  │                      │ 2. RSA.Create(2048)          │
  │                      │ 3. Public PEM                │
  │                      │ 4. Private PEM -> AES-GCM    │
  │                      │    with master key vN        │
  │                      │ 5. Generate 32-byte API key  │
  │                      │ 6. SHA-256(api key)          │
  │                      │ 7. Insert ProjectKey +       │
  │                      │    ApiKey in transaction     │
  │                      ├─────────────────────────────►│
  │ 201 { project,       │                              │
  │   apiKey.plaintext,  │                              │
  │   publicKeyPem }     │                              │
  │◄─────────────────────┤                              │
  │                      │                              │
  │ Show once. Never      │                              │
  │ request plaintext     │                              │
  │ again.                │                              │
```

### 2.3 Ingest flow

```text
1. Authenticate X-Api-Key -> project, reject missing/revoked keys.
2. Enforce 2 MB body and <= 50 envelopes.
3. For each envelope:
   a. Validate v, alg, projectId, eventCount, timestamps.
   b. If BatchId already exists:
      - same project -> duplicate[]
      - other project -> rejected[] batch_id_conflict
   c. Decrypt private key from cache (master-key decrypt on miss).
   d. RSA-OAEP-SHA256 unwrap wrappedKey -> 32-byte AES key.
   e. AES-256-GCM decrypt ciphertext with iv + tag.
   f. Parse UTF-8 JSON array; require length == eventCount.
   g. Validate LogEvent fields; reject future ts > now + 5 min.
4. In one transaction:
   a. Insert IngestBatch.
   b. AddRange Logs.
   c. SaveChanges with batching.
5. Return 202 with accepted[], duplicate[], rejected[].
```

### 2.4 Query flow

```text
Console -> GET /api/v1/projects/{id}/logs
  -> JWT authentication
  -> project ownership check (404 if not owned)
  -> validate filters and cursor
  -> SQL Server keyset query:
       WHERE ProjectId = @id
         AND (ClientTs < @cursorTs OR (ClientTs = @cursorTs AND Id < @cursorId))
         AND optional filters
       ORDER BY ClientTs DESC, Id DESC
       LIMIT @limit + 1
  -> return items + nextCursor
```

### 2.5 Trust boundaries

| Boundary | Trusted? | Notes |
|---|---|---|
| Flutter device | No | Holds public key only. A compromised device sees ciphertext and its own plaintext before sealing. |
| Network | No | TLS is mandatory. API key and JWT are bearer credentials. |
| API process memory | Yes, transiently | Decrypted payload exists only during ingest. Never log it. |
| SQL Server | Yes, at rest | TDE is a deployment choice; note it in the operations guide. |
| Console browser | No | Access token in memory; refresh token in HttpOnly cookie. |
| Master key | Highest | Configuration/user-secrets/Key Vault. Never committed or logged. |

---

## 3. Server Solution Tree and Console Folder Tree

### 3.1 Server solution tree

```text
SealedTrace.sln
Directory.Build.props
.dockerignore
docker-compose.yml
src/
  SealedTrace.Api/
    Controllers/
      AuthController.cs
      ProjectsController.cs
      ApiKeysController.cs
      LogsController.cs
      StatsController.cs
      IngestController.cs
    Middleware/
      CorrelationIdMiddleware.cs
      ProblemDetailsExceptionMiddleware.cs
    Security/
      ApiKeyAuthenticationHandler.cs
      CurrentUser.cs
      JwtTokenService.cs
    Validation/
      AuthValidators.cs
      ProjectValidators.cs
      QueryValidators.cs
    Program.cs
    appsettings.json
    appsettings.Development.json
  SealedTrace.Core/
    Entities/
      User.cs
      RefreshToken.cs
      Project.cs
      ApiKey.cs
      ProjectKey.cs
      IngestBatch.cs
      LogEntry.cs
    Dtos/
      Auth/
      Projects/
      ApiKeys/
      Ingest/
      Logs/
      Stats/
    Interfaces/
      IAuthService.cs
      IProjectService.cs
      IApiKeyService.cs
      IIngestService.cs
      ILogQueryService.cs
      IStatsService.cs
      IEnvelopeDecryptor.cs
      IProjectKeyProtector.cs
      IRetentionService.cs
    Domain/
      LogLevels.cs
      ProjectSlug.cs
      IngestRejectionReason.cs
  SealedTrace.Infrastructure/
    Persistence/
      SealedTraceDbContext.cs
      Configurations/
        UserConfiguration.cs
        RefreshTokenConfiguration.cs
        ProjectConfiguration.cs
        ApiKeyConfiguration.cs
        ProjectKeyConfiguration.cs
        IngestBatchConfiguration.cs
        LogEntryConfiguration.cs
      Migrations/
    Repositories/
      ProjectRepository.cs
      LogQueryRepository.cs
      StatsRepository.cs
    Crypto/
      MasterKeyRing.cs
      ProjectKeyProtector.cs
      EnvelopeDecryptor.cs
      RsaKeyPairFactory.cs
    Auth/
      AuthService.cs
      PasswordHasher.cs
      RefreshTokenService.cs
    Caching/
      ProjectKeyCache.cs
  SealedTrace.Jobs/
    RetentionBackgroundService.cs
    RetentionOptions.cs
tests/
  SealedTrace.Tests/
    Api/
      AuthEndpointTests.cs
      ProjectEndpointTests.cs
      ApiKeyEndpointTests.cs
      IngestEndpointTests.cs
      QueryEndpointTests.cs
      StatsEndpointTests.cs
    Unit/
      EnvelopeDecryptorTests.cs
      ProjectKeyProtectorTests.cs
      CursorTests.cs
      RetentionPolicyTests.cs
    Infrastructure/
      SqlServerFixture.cs
      TestWebApplicationFactory.cs
    TestData/
      envelope_vectors.json
      test-private-key.pem
      test-public-key.pem
```

### 3.2 Console folder tree

```text
console/
  package.json
  vite.config.ts
  tsconfig.json
  tailwind.config.ts
  postcss.config.js
  components.json                 # shadcn/ui config
  index.html
  src/
    main.tsx
    app/
      App.tsx
      router.tsx
      providers.tsx
    components/
      layout/
        AppShell.tsx
        Sidebar.tsx
        Topbar.tsx
      ui/                         # shadcn/ui generated primitives
      common/
        EmptyState.tsx
        ErrorState.tsx
        LoadingSkeleton.tsx
        CopyButton.tsx
        ConfirmDialog.tsx
        JsonTree.tsx
      guards/
        ProtectedRoute.tsx
        GuestRoute.tsx
    features/
      auth/
        LoginPage.tsx
        RegisterPage.tsx
        useAuth.ts
      projects/
        DashboardPage.tsx
        ProjectCard.tsx
        CreateProjectWizard.tsx
        ProjectCreatedPanel.tsx
        FlutterSnippet.tsx
      logs/
        LogExplorerPage.tsx
        FilterBar.tsx
        LogTable.tsx
        LogDetailDrawer.tsx
      stats/
        StatsPage.tsx
        LevelDonut.tsx
        EventsAreaChart.tsx
        TopTagsBar.tsx
        TopUsersBar.tsx
      settings/
        SettingsPage.tsx
        ApiKeysTable.tsx
        CreateApiKeyDialog.tsx
        RetentionForm.tsx
        DangerZone.tsx
    lib/
      apiClient.ts
      authStore.ts
      queryClient.ts
      formatters.ts
      constants.ts
      errors.ts
    types/
      api.ts
    test/
      setup.ts
      msw/
        handlers.ts
        server.ts
      fixtures/
  e2e/                            # optional Playwright smoke tests
  lighthouserc.json
```

---

## 4. SQL Server DDL

### 4.1 EF Core migration list

| Order | Migration | Contents |
|---|---|---|
| 1 | `202610300001_InitialCreate` | Users, Projects, ApiKeys, ProjectKeys, IngestBatches, Logs, RefreshTokens, foreign keys, base indexes. |
| 2 | `202610300002_AddProjectDescriptionEnvironment` | `Projects.Description` and `Projects.Environment` (the console-required extension). |
| 3 | `202610300003_AddFullTextSearch` | Raw SQL: full-text catalog and index on `Logs.Message`; down migration drops them. |

The migrations are applied automatically only in Development. Production applies them through a deployment step with a backup and a reviewed script.

### 4.2 CREATE TABLE statements

```sql
CREATE TABLE dbo.Users (
    Id              uniqueidentifier NOT NULL CONSTRAINT PK_Users PRIMARY KEY,
    Email           nvarchar(320)    NOT NULL,
    PasswordHash    nvarchar(255)    NOT NULL,
    Name            nvarchar(120)    NULL,
    CreatedAtUtc    datetime2(3)     NOT NULL
);

CREATE UNIQUE INDEX UX_Users_Email
    ON dbo.Users (Email);

CREATE TABLE dbo.Projects (
    Id                  uniqueidentifier NOT NULL CONSTRAINT PK_Projects PRIMARY KEY,
    OwnerId             uniqueidentifier NOT NULL,
    Name                nvarchar(120)    NOT NULL,
    Slug                nvarchar(80)     NOT NULL,
    Description         nvarchar(500)    NULL,          -- extension: A9
    Environment         nvarchar(16)     NOT NULL
        CONSTRAINT DF_Projects_Environment DEFAULT 'prod', -- extension: A9
    CreatedAtUtc        datetime2(3)     NOT NULL,
    RetentionMaxAgeDays int              NULL,
    RetentionMaxEvents  bigint           NULL,
    CONSTRAINT FK_Projects_Users
        FOREIGN KEY (OwnerId) REFERENCES dbo.Users (Id) ON DELETE CASCADE
);

CREATE UNIQUE INDEX UX_Projects_Slug
    ON dbo.Projects (Slug);

CREATE INDEX IX_Projects_Owner_CreatedAt
    ON dbo.Projects (OwnerId, CreatedAtUtc DESC);

CREATE TABLE dbo.ApiKeys (
    Id              uniqueidentifier NOT NULL CONSTRAINT PK_ApiKeys PRIMARY KEY,
    ProjectId       uniqueidentifier NOT NULL,
    KeyHash         binary(32)       NOT NULL,
    KeyPrefix       nvarchar(16)     NOT NULL,
    CreatedAtUtc    datetime2(3)     NOT NULL,
    RevokedAtUtc    datetime2(3)     NULL,
    CONSTRAINT FK_ApiKeys_Projects
        FOREIGN KEY (ProjectId) REFERENCES dbo.Projects (Id) ON DELETE CASCADE
);

CREATE UNIQUE INDEX UX_ApiKeys_KeyHash
    ON dbo.ApiKeys (KeyHash);

CREATE INDEX IX_ApiKeys_Project_Revoked
    ON dbo.ApiKeys (ProjectId, RevokedAtUtc);

CREATE TABLE dbo.ProjectKeys (
    Id                  uniqueidentifier NOT NULL CONSTRAINT PK_ProjectKeys PRIMARY KEY,
    ProjectId           uniqueidentifier NOT NULL,
    Algorithm           nvarchar(64)     NOT NULL,
    PublicKeyPem        nvarchar(max)    NOT NULL,
    PrivateKeyEncrypted varbinary(max)   NOT NULL,
    MasterKeyVersion    int              NOT NULL,
    CreatedAtUtc        datetime2(3)     NOT NULL,
    CONSTRAINT FK_ProjectKeys_Projects
        FOREIGN KEY (ProjectId) REFERENCES dbo.Projects (Id) ON DELETE CASCADE
);

CREATE UNIQUE INDEX UX_ProjectKeys_Project
    ON dbo.ProjectKeys (ProjectId);

CREATE TABLE dbo.IngestBatches (
    BatchId         uniqueidentifier NOT NULL CONSTRAINT PK_IngestBatches PRIMARY KEY,
    ProjectId       uniqueidentifier NOT NULL,
    ReceivedAtUtc   datetime2(3)     NOT NULL,
    EventCount      int              NOT NULL,
    EnvelopeSize    int              NOT NULL,
    CONSTRAINT FK_IngestBatches_Projects
        FOREIGN KEY (ProjectId) REFERENCES dbo.Projects (Id) ON DELETE CASCADE
);

CREATE INDEX IX_IngestBatches_Project_ReceivedAt
    ON dbo.IngestBatches (ProjectId, ReceivedAtUtc DESC);

CREATE TABLE dbo.Logs (
    Id              uniqueidentifier NOT NULL CONSTRAINT PK_Logs PRIMARY KEY,
    ProjectId       uniqueidentifier NOT NULL,
    BatchId         uniqueidentifier NOT NULL,
    ClientTs        datetime2(3)     NOT NULL,
    ReceivedAtUtc   datetime2(3)     NOT NULL,
    Level           tinyint          NOT NULL,
    Tag             nvarchar(128)    NOT NULL,
    Message         nvarchar(max)    NOT NULL,
    Error           nvarchar(max)    NULL,
    StackTrace      nvarchar(max)    NULL,
    MetadataJson    nvarchar(max)    NULL,
    SessionId       uniqueidentifier NULL,
    UserId          nvarchar(128)    NULL,
    Seq             bigint           NOT NULL,
    AppVersion      nvarchar(32)     NULL,
    Platform        nvarchar(16)     NULL,
    CONSTRAINT FK_Logs_Projects
        FOREIGN KEY (ProjectId) REFERENCES dbo.Projects (Id) ON DELETE CASCADE,
    CONSTRAINT FK_Logs_IngestBatches
        FOREIGN KEY (BatchId) REFERENCES dbo.IngestBatches (BatchId) ON DELETE CASCADE
);

CREATE INDEX IX_Logs_Project_ClientTs_Id
    ON dbo.Logs (ProjectId, ClientTs DESC, Id DESC);

CREATE INDEX IX_Logs_Project_Level_ClientTs
    ON dbo.Logs (ProjectId, Level, ClientTs DESC);

CREATE INDEX IX_Logs_Project_Tag
    ON dbo.Logs (ProjectId, Tag);

CREATE INDEX IX_Logs_Project_Session_ClientTs
    ON dbo.Logs (ProjectId, SessionId, ClientTs DESC);

CREATE INDEX IX_Logs_Project_User_ClientTs
    ON dbo.Logs (ProjectId, UserId, ClientTs DESC);

CREATE INDEX IX_Logs_Project_ReceivedAt
    ON dbo.Logs (ProjectId, ReceivedAtUtc DESC);

CREATE TABLE dbo.RefreshTokens (
    Id                  uniqueidentifier NOT NULL CONSTRAINT PK_RefreshTokens PRIMARY KEY,
    UserId              uniqueidentifier NOT NULL,
    TokenHash           binary(32)       NOT NULL,
    ExpiresAtUtc        datetime2(3)     NOT NULL,
    RevokedAtUtc        datetime2(3)     NULL,
    ReplacedByTokenHash binary(32)       NULL,
    CONSTRAINT FK_RefreshTokens_Users
        FOREIGN KEY (UserId) REFERENCES dbo.Users (Id) ON DELETE CASCADE
);

CREATE UNIQUE INDEX UX_RefreshTokens_TokenHash
    ON dbo.RefreshTokens (TokenHash);

CREATE INDEX IX_RefreshTokens_User_ExpiresAt
    ON dbo.RefreshTokens (UserId, ExpiresAtUtc DESC);
```

### 4.3 Full-text index

```sql
-- Raw migration: EF Core does not model full-text indexes.
CREATE FULLTEXT CATALOG SealedTraceFullText AS DEFAULT;

CREATE FULLTEXT INDEX ON dbo.Logs (Message LANGUAGE 1033)
    KEY INDEX PK_Logs
    ON SealedTraceFullText
    WITH CHANGE_TRACKING AUTO;
```

Down migration:

```sql
DROP FULLTEXT INDEX ON dbo.Logs;
DROP FULLTEXT CATALOG SealedTraceFullText;
```

### 4.4 Index and column notes

- `IngestBatches.EventCount` is the sum of the accepted envelopes' `eventCount`; `IngestBatches.EnvelopeSize` is the sum of the UTF-8 JSON byte lengths of those envelopes.
- `ProjectKeys.Algorithm` stores `RSA-OAEP-2048-SHA256`.
- `IX_Logs_Project_ClientTs_Id` is the keyset-pagination index.
- `IX_Logs_Project_Level_ClientTs` supports level filters and stats.
- `IX_Logs_Project_Tag` supports tag filters and top-tag stats.
- `IX_Logs_Project_Session_ClientTs` and `IX_Logs_Project_User_ClientTs` support the console filters.
- `IX_Logs_Project_ReceivedAt` supports dashboard “today” counts and retention cleanup.
- Full-text is optional at runtime; the `LIKE` fallback must remain parameterized to avoid injection.

### 4.5 Retention query shape

The job selects candidate projects, then deletes in batches:

```sql
-- Age-based, batched
DELETE TOP (10000) FROM dbo.Logs
WHERE ProjectId = @projectId
  AND ClientTs < @cutoffUtc;

-- Event-count-based, batched
DELETE TOP (10000) FROM dbo.Logs
WHERE Id IN (
    SELECT TOP (10000) Id
    FROM dbo.Logs
    WHERE ProjectId = @projectId
    ORDER BY ClientTs ASC
);
```

After logs are removed, orphaned `IngestBatches` are deleted by the same job. The job runs in a loop until no rows remain or a per-run safety cap is reached.

---

## 5. REST API Reference

Base URL: `/api/v1`. Health endpoints are at the root. All timestamps are ISO-8601 UTC. All console endpoints require `Authorization: Bearer <jwt>` unless stated otherwise. Ingest requires `X-Api-Key`.

### 5.1 Endpoint summary

| Method | Path | Auth | Purpose |
|---|---|---|---|
| POST | `/auth/register` | None | Create an account. |
| POST | `/auth/login` | None | Issue access + refresh tokens. |
| POST | `/auth/refresh` | Refresh cookie | Rotate tokens. |
| POST | `/auth/logout` | Refresh cookie | Revoke the current refresh token. |
| GET | `/auth/me` | Bearer | Current user. |
| GET | `/projects` | Bearer | List owned projects with dashboard aggregates. |
| POST | `/projects` | Bearer | Create a project, RSA key pair, and first API key. |
| GET | `/projects/{id}` | Bearer | Project detail. |
| PATCH | `/projects/{id}` | Bearer | Rename, description, environment, retention. |
| DELETE | `/projects/{id}` | Bearer | Hard delete with cascade. |
| GET | `/projects/{id}/api-keys` | Bearer | List key prefixes and status. |
| POST | `/projects/{id}/api-keys` | Bearer | Create an additional key (max 5 active). |
| DELETE | `/projects/{id}/api-keys/{keyId}` | Bearer | Soft-revoke a key. |
| GET | `/projects/{id}/logs` | Bearer | Query logs with filters and cursor. |
| GET | `/projects/{id}/stats` | Bearer | Aggregated stats. |
| POST | `/ingest/logs` | `X-Api-Key` | Ingest sealed envelopes. |
| GET | `/health` | None | Liveness. |
| GET | `/health/ready` | None | Readiness + DB check. |

### 5.2 Common error format

```json
{
  "type": "https://sealedtrace.dev/errors/validation-failed",
  "title": "Validation failed",
  "status": 400,
  "detail": "One or more fields are invalid.",
  "code": "validation_failed",
  "traceId": "00-4bf92f3577b34da6a3ce929d0e0e4736-00f067aa0ba902b7-01",
  "errors": [
    { "field": "email", "message": "A valid email is required." }
  ]
}
```

Stable error codes:

| Code | HTTP | Meaning |
|---|---|---|
| `validation_failed` | 400 | Request body/query invalid. |
| `invalid_credentials` | 401 | Email/password mismatch. |
| `invalid_token` | 401 | Missing/expired/invalid JWT. |
| `invalid_refresh_token` | 401 | Missing/expired/unknown refresh token. |
| `token_reuse_detected` | 401 | Revoked refresh token reused; all user tokens revoked. |
| `invalid_key` | 401 | Missing/unknown API key. |
| `revoked_key` | 403 | API key exists but is revoked. |
| `not_found` | 404 | Resource does not exist or is not owned by the caller. |
| `email_taken` | 409 | Registration email already exists. |
| `key_limit_reached` | 409 | More than 5 active API keys. |
| `invalid_cursor` | 400 | Malformed pagination cursor. |
| `invalid_range` | 400 | `from`/`to` invalid or range > 90 days. |
| `payload_too_large` | 413 | Body exceeds 2 MB. |
| `rate_limited` | 429 | Rate limit exceeded; `Retry-After` included. |
| `internal_error` | 500 | Unexpected server error; no secrets in detail. |

### 5.3 Auth

#### `POST /auth/register`

Request:

```json
{
  "email": "owner@example.com",
  "password": "correct horse battery staple",
  "name": "Owner"
}
```

Response `201 Created`:

```json
{
  "id": "0f8fad5b-d9cb-469f-a165-70867728950e",
  "email": "owner@example.com",
  "name": "Owner",
  "createdAtUtc": "2026-10-30T10:00:00.000Z"
}
```

Errors: `400 validation_failed`, `409 email_taken`, `429 rate_limited`.

#### `POST /auth/login`

Request:

```json
{
  "email": "owner@example.com",
  "password": "correct horse battery staple"
}
```

Response `200 OK`:

```json
{
  "accessToken": "<jwt>",
  "tokenType": "Bearer",
  "expiresIn": 900,
  "user": {
    "id": "0f8fad5b-d9cb-469f-a165-70867728950e",
    "email": "owner@example.com",
    "name": "Owner"
  }
}
```

Also sets `st_refresh` cookie: `HttpOnly; Secure; SameSite=Lax; Path=/api/v1/auth; Max-Age=2592000`.

Errors: `401 invalid_credentials`, `429 rate_limited`.

#### `POST /auth/refresh`

Request: no body; requires the refresh cookie.

Response `200 OK`: same shape as login. The old refresh token is revoked and `ReplacedByTokenHash` is set.

Errors: `401 invalid_refresh_token`, `401 token_reuse_detected`.

#### `POST /auth/logout`

Request: no body; requires the refresh cookie. Response `204 No Content`. The current refresh token is revoked and the cookie is cleared.

#### `GET /auth/me`

Response `200 OK`:

```json
{
  "id": "0f8fad5b-d9cb-469f-a165-70867728950e",
  "email": "owner@example.com",
  "name": "Owner",
  "createdAtUtc": "2026-10-30T10:00:00.000Z"
}
```

Errors: `401 invalid_token`.

### 5.4 Projects

#### `GET /projects`

Response `200 OK`:

```json
[
  {
    "id": "3f2504e0-4f89-41d3-9a0c-0305e82c3301",
    "name": "Mobile App",
    "slug": "mobile-app",
    "description": "Production mobile client",
    "environment": "prod",
    "createdAtUtc": "2026-10-30T10:00:00.000Z",
    "retentionMaxAgeDays": 30,
    "retentionMaxEvents": 1000000,
    "todayEventCount": 1234,
    "lastReceivedAtUtc": "2026-10-30T11:59:59.000Z",
    "activeApiKeyCount": 1
  }
]
```

#### `POST /projects`

Request:

```json
{
  "name": "Mobile App",
  "description": "Production mobile client",
  "environment": "prod"
}
```

Response `201 Created`:

```json
{
  "project": {
    "id": "3f2504e0-4f89-41d3-9a0c-0305e82c3301",
    "name": "Mobile App",
    "slug": "mobile-app",
    "description": "Production mobile client",
    "environment": "prod",
    "createdAtUtc": "2026-10-30T10:00:00.000Z",
    "retentionMaxAgeDays": null,
    "retentionMaxEvents": null
  },
  "apiKey": {
    "id": "b7e6c1d2-3a4f-4b5c-8d9e-0f1a2b3c4d5e",
    "prefix": "AbCdEf12",
    "plaintext": "AbCdEf12...43-character-base64url-key",
    "createdAtUtc": "2026-10-30T10:00:00.000Z"
  },
  "publicKeyPem": "-----BEGIN PUBLIC KEY-----\n...\n-----END PUBLIC KEY-----\n"
}
```

The `plaintext` value is returned **exactly once**. It is never retrievable again.

Errors: `400 validation_failed`, `401 invalid_token`.

#### `GET /projects/{id}`

Response `200 OK`: the project object, without API key plaintext. Includes retention fields and `publicKeyPem`? The console does not need the public key after creation, so it is omitted by default. If the SDK team needs it later, add `GET /projects/{id}/public-key` as an additive endpoint.

Errors: `404 not_found`.

#### `PATCH /projects/{id}`

Request (all fields optional):

```json
{
  "name": "Mobile App (Prod)",
  "description": "Updated description",
  "environment": "prod",
  "retentionMaxAgeDays": 30,
  "retentionMaxEvents": 1000000
}
```

Response `200 OK`: updated project object.

Errors: `400 validation_failed`, `404 not_found`.

#### `DELETE /projects/{id}`

Response `204 No Content`. Cascades to keys, batches, and logs. The console requires typing the project name.

Errors: `404 not_found`.

### 5.5 API keys

#### `GET /projects/{id}/api-keys`

Response `200 OK`:

```json
[
  {
    "id": "b7e6c1d2-3a4f-4b5c-8d9e-0f1a2b3c4d5e",
    "prefix": "AbCdEf12",
    "createdAtUtc": "2026-10-30T10:00:00.000Z",
    "revokedAtUtc": null
  }
]
```

#### `POST /projects/{id}/api-keys`

Response `201 Created`:

```json
{
  "id": "c8f7d2e3-4b5a-4c6d-9e0f-1a2b3c4d5e6f",
  "prefix": "ZzYyXx98",
  "plaintext": "ZzYyXx98...43-character-base64url-key",
  "createdAtUtc": "2026-10-30T12:00:00.000Z"
}
```

Errors: `409 key_limit_reached`, `404 not_found`.

#### `DELETE /projects/{id}/api-keys/{keyId}`

Response `204 No Content`. Sets `RevokedAtUtc`; does not delete the row.

Errors: `404 not_found`.

### 5.6 Logs

#### `GET /projects/{id}/logs`

Query parameters:

| Parameter | Type | Notes |
|---|---|---|
| `level` | comma-separated | `verbose,debug,info,warning,error,fatal` |
| `tag` | string | Exact match. |
| `from` | ISO-8601 UTC | Inclusive. |
| `to` | ISO-8601 UTC | Exclusive. |
| `userId` | string | Exact match. |
| `sessionId` | UUID | Exact match. |
| `search` | string | Full-text on `Message`; fallback `LIKE`. |
| `cursor` | base64 | `base64("<ClientTs>|<Id>")` from `nextCursor`. |
| `limit` | int | 1–200; default 50. |

Response `200 OK`:

```json
{
  "items": [
    {
      "id": "9b2f7c1a-1d2e-4f3a-8b4c-5d6e7f8a9b0c",
      "seq": 1234,
      "ts": "2026-10-30T11:59:00.000Z",
      "receivedAtUtc": "2026-10-30T11:59:01.123Z",
      "level": "error",
      "tag": "auth",
      "message": "Login failed for user@example.com",
      "error": "InvalidCredentialsException",
      "stackTrace": "at AuthService...",
      "metadata": { "attempt": 3 },
      "ctx": {
        "appVersion": "1.2.3",
        "buildNumber": "45",
        "platform": "android",
        "os": "Android 15",
        "locale": "en_IN",
        "env": "prod",
        "network": "wifi"
      },
      "sessionId": "7c9e6679-7425-40de-944b-e07fc1f90ae7",
      "userId": "user-123"
    }
  ],
  "nextCursor": "MjAyNi0xMC0zMFQxMTo1OTowMC4wMDBafDliMmY3YzFhLTFkMmUtNGYzYS04YjRjLTVkNmU3ZjhhOWIwYw=="
}
```

Errors: `400 invalid_cursor`, `400 invalid_range`, `400 validation_failed`, `404 not_found`.

#### `GET /projects/{id}/stats`

Query parameters: `from`, `to` (optional; default last 7 days; max 90 days).

Response `200 OK`:

```json
{
  "from": "2026-10-23T00:00:00.000Z",
  "to": "2026-10-30T00:00:00.000Z",
  "totalEvents": 12345,
  "levels": [
    { "level": "verbose", "count": 10 },
    { "level": "debug", "count": 200 },
    { "level": "info", "count": 10000 },
    { "level": "warning", "count": 1500 },
    { "level": "error", "count": 600 },
    { "level": "fatal", "count": 35 }
  ],
  "eventsPerDay": [
    { "day": "2026-10-23", "count": 1000 },
    { "day": "2026-10-24", "count": 1200 }
  ],
  "topTags": [
    { "tag": "auth", "count": 3000 },
    { "tag": "network", "count": 2500 }
  ],
  "topUserIds": [
    { "userId": "user-123", "count": 500 },
    { "userId": "user-456", "count": 300 }
  ]
}
```

Errors: `400 invalid_range`, `404 not_found`.

### 5.7 Ingest

#### `POST /ingest/logs`

Headers:

```text
X-Api-Key: <api key>
Content-Type: application/json
```

Request:

```json
{
  "envelopes": [
    {
      "v": 1,
      "batchId": "5f0c5b7e-3a1d-4c9e-9f2a-1b3c4d5e6f70",
      "projectId": "3f2504e0-4f89-41d3-9a0c-0305e82c3301",
      "createdAt": "2026-10-30T10:00:00.000Z",
      "eventCount": 25,
      "alg": "RSA-OAEP-2048-SHA256+AES-256-GCM",
      "wrappedKey": "<base64>",
      "iv": "<base64, 12 bytes>",
      "tag": "<base64, 16 bytes>",
      "ciphertext": "<base64>"
    }
  ]
}
```

Response `202 Accepted`:

```json
{
  "accepted": ["5f0c5b7e-3a1d-4c9e-9f2a-1b3c4d5e6f70"],
  "duplicate": [],
  "rejected": []
}
```

Rejection reasons:

| Reason | Meaning |
|---|---|
| `invalid_envelope` | Shape/field validation failed. |
| `invalid_version` | `v` is not 1. |
| `invalid_algorithm` | `alg` is not the fixed value. |
| `project_mismatch` | Envelope `projectId` does not match the API key's project. |
| `decrypt_failed` | RSA/AES-GCM decryption failed. |
| `invalid_payload` | Decrypted bytes are not a UTF-8 JSON array. |
| `event_count_mismatch` | Decrypted array length differs from `eventCount`. |
| `invalid_event` | A `LogEvent` failed validation. |
| `timestamp_in_future` | `ts` is more than 5 minutes ahead of server time. |
| `batch_id_conflict` | `batchId` belongs to another project. |
| `duplicate_log_id` | A `Logs.Id` already exists. |
| `internal_error` | Unexpected per-envelope failure. |

Errors: `400 validation_failed`, `401 invalid_key`, `403 revoked_key`, `413 payload_too_large`, `429 rate_limited`, `500 internal_error`.

### 5.8 Health

#### `GET /health`

Response `200 OK`:

```json
{ "status": "Healthy" }
```

#### `GET /health/ready`

Response `200 OK` or `503 Service Unavailable`:

```json
{
  "status": "Healthy",
  "checks": {
    "db": "Healthy"
  }
}
```

### 5.9 Optional metrics

`GET /metrics` returns Prometheus text format only when `Metrics:Enabled=true`. It is not exposed in Production by default.

---

## 6. Contract Compliance Checklist vs Section 3

| # | Contract item | Implementation requirement | Verification |
|---|---|---|---|
| C1 | `POST /api/v1/ingest/logs` | Exact path, method, and `X-Api-Key` header. No alternate ingest route. | Integration test asserts route and 405/404 behavior. |
| C2 | `SealedEnvelope.v` | JSON property `v`, integer, must equal 1. | Unit test rejects 0 and 2. |
| C3 | `batchId` | JSON `batchId`, UUID string, used as `IngestBatches` PK. | Duplicate test. |
| C4 | `projectId` | JSON `projectId`, UUID string, must match API key project. | Cross-project rejection test. |
| C5 | `createdAt` | JSON `createdAt`, ISO-8601 UTC string, accepted as supplied. | Parse test. |
| C6 | `eventCount` | JSON `eventCount`, integer, must equal decrypted array length. | Mismatch test. |
| C7 | `alg` | Exact string `RSA-OAEP-2048-SHA256+AES-256-GCM`. | Rejection test for any other value. |
| C8 | `wrappedKey` | Base64 string; RSA-OAEP-SHA256 ciphertext of the 32-byte AES key. | Golden-vector test. |
| C9 | `iv` | Base64 string; exactly 12 bytes. | Length test. |
| C10 | `tag` | Base64 string; exactly 16 bytes. | Length test. |
| C11 | `ciphertext` | Base64 string; AES-256-GCM ciphertext. | Golden-vector test. |
| C12 | Decryption algorithm | `RSAEncryptionPadding.OaepSHA256` + `AesGcm` with 12-byte nonce and 16-byte tag. | Golden-vector test. |
| C13 | Decrypted payload | UTF-8 JSON array of `LogEvent`. | Invalid-payload test. |
| C14 | `LogEvent.id` | JSON `id`, UUID string, `Logs.Id` PK. | Insert/duplicate test. |
| C15 | `LogEvent.seq` | JSON `seq`, integer, stored as `bigint`. | Roundtrip test. |
| C16 | `LogEvent.ts` | JSON `ts`, ISO-8601 UTC, stored as `ClientTs`; reject > now + 5 min. | Future-timestamp test. |
| C17 | `LogEvent.level` | Exact enum: `verbose`, `debug`, `info`, `warning`, `error`, `fatal`. | Level mapping test. |
| C18 | `LogEvent.tag` | JSON `tag`, stored as `nvarchar(128)`. | Roundtrip test. |
| C19 | `LogEvent.message` | JSON `message`, stored as `nvarchar(max)`. | Roundtrip test. |
| C20 | `LogEvent.error` | JSON `error`, string or null. | Roundtrip test. |
| C21 | `LogEvent.stackTrace` | JSON `stackTrace`, string or null. | Roundtrip test. |
| C22 | `LogEvent.metadata` | JSON object, stored in `MetadataJson`; default `{}`. | Roundtrip test. |
| C23 | `LogEvent.sessionId` | JSON `sessionId`, UUID or null. | Roundtrip test. |
| C24 | `LogEvent.userId` | JSON `userId`, string or null, max 128. | Roundtrip test. |
| C25 | `LogEvent.ctx` | Full object accepted; `appVersion`/`platform` also stored in columns; full object stored under `_ctx` in `MetadataJson`. | Roundtrip test. |
| C26 | `ctx.platform` | Enum: `android`, `ios`, `web`, `windows`, `macos`, `linux`. | Validation test. |
| C27 | `ctx.network` | Enum: `wifi`, `mobile`, `none`, `unknown`. | Validation test. |
| C28 | 202 response | Exact keys `accepted`, `duplicate`, `rejected`; `rejected` items have `batchId`, `reason`. | Integration test. |
| C29 | Golden vectors | Same `tests/TestData/envelope_vectors.json` as the SDK team; fixed test key pair; known plaintext. | CI test fails if file/decryption drifts. |
| C30 | Field casing | Use explicit `[JsonPropertyName]` attributes or `PropertyNamingPolicy = null`; never rely on ambient naming policy. | Serialization test. |

### 6.1 Level mapping

| Contract level | `Logs.Level` |
|---|---|
| `verbose` | 0 |
| `debug` | 1 |
| `info` | 2 |
| `warning` | 3 |
| `error` | 4 |
| `fatal` | 5 |

### 6.2 `ctx` persistence

The fixed `Logs` table has no columns for `buildNumber`, `os`, `locale`, `env`, or `network`. The server writes:

```json
{
  "metadata": { "...": "client metadata" },
  "_ctx": {
    "appVersion": "1.2.3",
    "buildNumber": "45",
    "platform": "android",
    "os": "Android 15",
    "locale": "en_IN",
    "env": "prod",
    "network": "wifi"
  }
}
```

into `MetadataJson`. `AppVersion` and `Platform` are also copied to their dedicated columns for indexing/filtering. The console detail drawer renders `metadata` and `_ctx` separately.

---

## 7. Phase-Wise Plan

### 7.1 Backend phases

#### B1 — Solution scaffold + EF Core + SQL Server + Serilog + Swagger + health

**Goal:** A buildable, testable solution with the database, logging, API documentation, and health checks in place.

**Files touched:**
- `SealedTrace.sln`, `Directory.Build.props`
- `src/SealedTrace.Api/Program.cs`, `appsettings*.json`
- `src/SealedTrace.Core/Entities/*`, `src/SealedTrace.Core/Dtos/*`
- `src/SealedTrace.Infrastructure/Persistence/*`
- `src/SealedTrace.Jobs/*` (empty hosted-service shell)
- `tests/SealedTrace.Tests/Infrastructure/*`
- `docker-compose.yml` (initial SQL Server service; API service added in B6)

**Task checklist:**
- [ ] Create the four projects and the test project; enable nullable, implicit usings, and warnings-as-errors for nullable.
- [ ] Add EF Core SQL Server provider and design-time factory.
- [ ] Model all entities from §4; add configurations, keys, indexes, and cascade rules.
- [ ] Create the initial migration and apply it to a clean SQL Server database.
- [ ] Add Serilog console + rolling file sinks; redact `X-Api-Key`, `Authorization`, `SealedTrace:MasterKey`, and private-key fields.
- [ ] Add global exception middleware producing RFC 7807 problem+json with a correlation/trace ID.
- [ ] Add Swagger with Bearer and `X-Api-Key` security schemes; disable in Production.
- [ ] Add `/health` and `/health/ready` with a DbContext check.
- [ ] Add CORS policy from `Cors:AllowedOrigins`.
- [ ] Add the Testcontainers MSSQL fixture and `WebApplicationFactory` base class.
- [ ] Add a migration smoke test and health endpoint tests.

**Acceptance criteria:**
- `dotnet build SealedTrace.sln` succeeds with zero warnings.
- `dotnet test` runs the smoke tests.
- The initial migration applies to an empty database and rolls back cleanly.
- `/health` returns 200; `/health/ready` returns 200 with a reachable DB and 503 without it.
- Swagger loads in Development and shows both auth schemes.
- No secrets are committed.

**Tests:**
- Health liveness/readiness.
- Problem-details shape for an unhandled exception.
- Migration applies to a fresh Testcontainers database.
- CORS preflight from the allowed origin succeeds; an unknown origin is rejected.

---

#### B2 — Auth: register/login/refresh/me + tests

**Goal:** Secure account lifecycle with short-lived JWT access tokens and rotating refresh tokens.

**Files touched:**
- `src/SealedTrace.Core/Entities/User.cs`, `RefreshToken.cs`
- `src/SealedTrace.Core/Dtos/Auth/*`
- `src/SealedTrace.Core/Interfaces/IAuthService.cs`
- `src/SealedTrace.Infrastructure/Auth/*`
- `src/SealedTrace.Api/Controllers/AuthController.cs`
- `src/SealedTrace.Api/Validation/AuthValidators.cs`
- `tests/SealedTrace.Tests/Api/AuthEndpointTests.cs`

**Task checklist:**
- [ ] Implement registration with email normalization and a unique index; hash passwords with BCrypt (work factor 12).
- [ ] Issue JWTs with `sub`, `email`, `name`, `iat`, `exp`, and a 15-minute lifetime.
- [ ] Generate 32-byte refresh tokens; store SHA-256 hashes; set the `st_refresh` HttpOnly cookie.
- [ ] Implement rotation: revoke the old token, set `ReplacedByTokenHash`, issue a new token.
- [ ] Implement reuse detection: reuse of a revoked token revokes all user tokens and returns `token_reuse_detected`.
- [ ] Implement `logout` and `me`.
- [ ] Rate-limit login/register by IP or email.
- [ ] Add FluentValidation rules for email, password length, and name length.
- [ ] Add integration tests for the full auth matrix.

**Acceptance criteria:**
- Register returns 201 and never returns a password hash.
- Login returns a JWT and sets an HttpOnly refresh cookie.
- Refresh rotates the token and rejects the old one.
- Reusing a revoked refresh token returns 401 and revokes the user's other tokens.
- `/me` returns only the current user.
- Access tokens expire after 15 minutes; refresh tokens after 30 days.

**Tests:**
- Unit tests for password hashing and token hashing.
- Integration tests for register, duplicate email, login, wrong password, refresh, reuse detection, logout, and `/me`.
- Cookie flags asserted (`HttpOnly`, `Secure`, `SameSite=Lax`, path).

---

#### B3 — Projects CRUD + API keys + RSA key pair issuance + master-key encryption

**Goal:** Multi-tenant project management with one-time API keys and per-project RSA key pairs.

**Files touched:**
- `src/SealedTrace.Core/Entities/Project.cs`, `ApiKey.cs`, `ProjectKey.cs`
- `src/SealedTrace.Core/Dtos/Projects/*`, `Dtos/ApiKeys/*`
- `src/SealedTrace.Core/Interfaces/IProjectService.cs`, `IApiKeyService.cs`, `IProjectKeyProtector.cs`
- `src/SealedTrace.Infrastructure/Crypto/*`
- `src/SealedTrace.Infrastructure/Caching/ProjectKeyCache.cs`
- `src/SealedTrace.Api/Controllers/ProjectsController.cs`, `ApiKeysController.cs`
- `src/SealedTrace.Api/Validation/ProjectValidators.cs`
- `tests/SealedTrace.Tests/Api/ProjectEndpointTests.cs`, `ApiKeyEndpointTests.cs`
- `tests/SealedTrace.Tests/Unit/ProjectKeyProtectorTests.cs`

**Task checklist:**
- [ ] Implement project create/list/detail/update/delete with owner-only filtering.
- [ ] Generate slugs from names; enforce uniqueness with numeric suffixes; make slugs immutable.
- [ ] Add `Description` and `Environment` columns via migration.
- [ ] On create, generate an RSA-2048 key pair; store public PEM; encrypt private PEM with AES-256-GCM using the current master key version.
- [ ] Implement the master-key ring and `MasterKeyVersion` selection.
- [ ] Cache decrypted private keys in `IMemoryCache` with a bounded size and absolute expiration; never log them.
- [ ] Generate the first API key, return plaintext once, store SHA-256 hash + 8-char prefix.
- [ ] Implement API-key list/create/revoke; enforce max 5 active keys; soft-revoke.
- [ ] Implement dashboard aggregates (`todayEventCount`, `lastReceivedAtUtc`, `activeApiKeyCount`) with efficient queries.
- [ ] Add crypto round-trip and key-lifecycle tests.

**Acceptance criteria:**
- Project create returns the API key plaintext exactly once and a valid public PEM.
- The database never contains plaintext API keys or plaintext private keys.
- A user cannot read/update/delete another user's project (404).
- Revoked keys cannot authenticate.
- Creating a sixth active key returns `key_limit_reached`.
- Deleting a project cascades to keys, batches, and logs.

**Tests:**
- Project CRUD and slug uniqueness.
- RSA key pair generation; private-key encrypt/decrypt round trip; wrong master-key version fails.
- API-key hash lookup, prefix display, revocation, and limit.
- Cross-tenant access returns 404.

---

#### B4 — Ingest endpoint + decrypt service + idempotent batch storage + golden vectors

**Goal:** The fixed ingest contract works end-to-end with real encryption and idempotency.

**Files touched:**
- `src/SealedTrace.Api/Controllers/IngestController.cs`
- `src/SealedTrace.Api/Security/ApiKeyAuthenticationHandler.cs`
- `src/SealedTrace.Core/Dtos/Ingest/*`
- `src/SealedTrace.Core/Interfaces/IIngestService.cs`, `IEnvelopeDecryptor.cs`
- `src/SealedTrace.Infrastructure/Crypto/EnvelopeDecryptor.cs`
- `src/SealedTrace.Infrastructure/Services/IngestService.cs`
- `src/SealedTrace.Api/Validation/IngestValidators.cs`
- `tests/SealedTrace.Tests/Api/IngestEndpointTests.cs`
- `tests/SealedTrace.Tests/Unit/EnvelopeDecryptorTests.cs`
- `tests/SealedTrace.Tests/TestData/envelope_vectors.json`

**Task checklist:**
- [ ] Implement API-key authentication: hash the header value, look up the key, reject missing/unknown/revoked keys, and attach the project to the request.
- [ ] Enforce a 2 MB body limit and a maximum of 50 envelopes.
- [ ] Add the fixed-window rate limiter (600/min per key) and return 429 with `Retry-After`.
- [ ] Validate envelope fields before decryption; reject cross-project IDs and unsupported versions/algorithms.
- [ ] Implement batch dedupe by `BatchId`; return `duplicate[]` for same-project duplicates and `rejected[]` for cross-project conflicts.
- [ ] Load/decrypt the project private key from cache or the master-key-protected store.
- [ ] Implement RSA-OAEP-SHA256 unwrap + AES-256-GCM decrypt + UTF-8 JSON parse.
- [ ] Validate `LogEvent` fields, level enum, future timestamps, and event count.
- [ ] Persist `IngestBatch` + `Logs` in one transaction with `AddRange` and EF batching.
- [ ] Map `ctx` to `AppVersion`, `Platform`, and the `_ctx` key in `MetadataJson`.
- [ ] Return the exact 202 arrays and stable rejection reasons.
- [ ] Add the golden-vector integration test using the shared SDK file.

**Acceptance criteria:**
- Golden vectors decrypt successfully and produce the expected rows.
- A valid batch returns 202 with the batch ID in `accepted`.
- Replaying the same batch returns 202 with the ID in `duplicate` and inserts no rows.
- A tampered ciphertext, wrong key, wrong IV/tag, or wrong algorithm returns a per-envelope rejection.
- A future timestamp is rejected; an old timestamp is accepted.
- 51 envelopes or a body over 2 MB returns 413 `payload_too_large`.
- 601 requests in a minute from one key returns 429.
- A local benchmark records p95 < 300 ms for a 50-envelope request; the benchmark result is attached to the phase review (not a flaky CI gate).
- The transaction is atomic: a failure inserts neither the batch nor partial logs.

**Tests:**
- Unit tests for RSA/AES decryptor success and failure modes.
- Integration test: ingest → decrypt → query round trip.
- Duplicate batch, duplicate log ID, cross-project batch ID.
- Rate limit, body limit, and envelope count.
- Serialization test for exact field names/casing.

---

#### B5 — Query/stats APIs: keyset pagination + full-text search

**Goal:** The console can browse, filter, search, and chart logs efficiently.

**Files touched:**
- `src/SealedTrace.Core/Dtos/Logs/*`, `Dtos/Stats/*`
- `src/SealedTrace.Core/Interfaces/ILogQueryService.cs`, `IStatsService.cs`
- `src/SealedTrace.Infrastructure/Repositories/LogQueryRepository.cs`, `StatsRepository.cs`
- `src/SealedTrace.Api/Controllers/LogsController.cs`, `StatsController.cs`
- `src/SealedTrace.Api/Validation/QueryValidators.cs`
- `src/SealedTrace.Infrastructure/Persistence/Migrations/*_AddFullTextSearch.cs`
- `tests/SealedTrace.Tests/Api/QueryEndpointTests.cs`, `StatsEndpointTests.cs`
- `tests/SealedTrace.Tests/Unit/CursorTests.cs`

**Task checklist:**
- [ ] Implement cursor encode/decode with `base64("<ClientTs>|<Id>")`; reject malformed cursors.
- [ ] Implement keyset pagination on `(ClientTs DESC, Id DESC)` with `limit + 1` lookahead.
- [ ] Add filters: level list, tag, from/to, userId, sessionId, and search.
- [ ] Implement full-text `CONTAINS` via `FromSqlRaw` with parameterization; add the `LIKE` fallback behind `Search:UseFullText`.
- [ ] Enforce the 90-day maximum range and default ranges.
- [ ] Implement stats: total, level distribution, events/day, top 10 tags, top 10 users.
- [ ] Add the full-text raw migration and verify the down migration.
- [ ] Add query/stats integration tests with seeded logs.

**Acceptance criteria:**
- Pagination is gap-free and duplicate-free across pages, including identical timestamps.
- Every filter works independently and in combination.
- Search returns matching messages via full-text or fallback.
- Cross-tenant queries return 404.
- Stats match a known seed dataset.
- A range over 90 days returns `invalid_range`.
- `limit > 200` is rejected or clamped (choose rejection for clarity).

**Tests:**
- Cursor round trip and malformed cursor.
- Pagination with ties on `ClientTs`.
- Filter matrix.
- Full-text and fallback search.
- Stats aggregation and empty-result shape.
- Ownership isolation.

---

#### B6 — Retention job + rate limiting + body limits + docker-compose + hardening

**Goal:** Production hardening and operational readiness.

**Files touched:**
- `src/SealedTrace.Jobs/RetentionBackgroundService.cs`, `RetentionOptions.cs`
- `src/SealedTrace.Api/Program.cs` (rate limiting, body limits, CORS, headers)
- `src/SealedTrace.Infrastructure/Services/RetentionService.cs`
- `Dockerfile`, `docker-compose.yml`
- `tests/SealedTrace.Tests/Unit/RetentionPolicyTests.cs`
- `tests/SealedTrace.Tests/Api/HardeningTests.cs`

**Task checklist:**
- [ ] Implement retention policy resolution: project value or configured default.
- [ ] Run nightly at 03:00 UTC; delete in 10k-row batches; clean orphaned batches; purge expired/revoked refresh tokens; enforce a per-run safety cap.
- [ ] Add rate limiting to auth and ingest endpoints with appropriate partitions.
- [ ] Configure Kestrel `MaxRequestBodySize` and JSON limits.
- [ ] Add security headers (HSTS, `X-Content-Type-Options`, `Referrer-Policy`) and strict CORS.
- [ ] Add Serilog redaction tests for API keys, JWTs, and private keys.
- [ ] Add ingest/decrypt metrics counters; optional `/metrics`.
- [ ] Add the API Dockerfile and docker-compose API + SQL Server services with healthchecks.
- [ ] Add an operations note for TDE and Key Vault.
- [ ] Add hardening tests.

**Acceptance criteria:**
- Retention deletes old logs and excess events according to policy; it never deletes another project's logs.
- The job is idempotent and safe to re-run.
- Oversized bodies return 413; excessive requests return 429.
- The compose stack starts and `/health/ready` is green.
- Logs contain no API keys, JWTs, private keys, or plaintext PII.
- `/metrics` is absent unless enabled.

**Tests:**
- Retention unit tests for age/event-count/default policies.
- Integration test with seeded old/new rows.
- Rate limit and body limit tests.
- Compose smoke test (manual/CI job).
- Redaction test.

---

### 7.2 Frontend phases

#### F1 — Scaffold + auth pages + route guards

**Goal:** A strict TypeScript React 18 console with working login/register and protected routing.

**Files touched:**
- `console/package.json`, `vite.config.ts`, `tsconfig.json`, `tailwind.config.ts`
- `console/src/main.tsx`, `app/App.tsx`, `app/router.tsx`, `app/providers.tsx`
- `console/src/features/auth/*`
- `console/src/lib/apiClient.ts`, `authStore.ts`, `queryClient.ts`
- `console/src/components/guards/*`, `components/layout/*`
- `console/src/types/api.ts`
- `console/src/test/*`

**Task checklist:**
- [ ] Scaffold Vite + React 18 + TypeScript strict; add ESLint + Prettier.
- [ ] Add Tailwind + shadcn/ui; set dark theme as default.
- [ ] Build the API client with access token in memory and a 401 refresh interceptor that retries once.
- [ ] Build the zustand auth store; persist only user metadata, never tokens.
- [ ] Implement login/register forms with validation and error toasts.
- [ ] Implement `ProtectedRoute` and `GuestRoute`; redirect to the intended path after login.
- [ ] Add the app shell (sidebar, topbar, user menu, logout).
- [ ] Add MSW handlers and auth flow tests.

**Acceptance criteria:**
- Login/register work against the API.
- Refreshing the browser restores the session via the refresh cookie.
- Protected routes redirect unauthenticated users to `/login`.
- Tokens are never written to localStorage/sessionStorage.
- `npm run typecheck`, `npm run lint`, and `npm test` pass.

**Tests:**
- RTL tests for login success/failure, register, redirect, logout, and refresh retry.
- Type-level test that API types match the contract (compile-time).

---

#### F2 — Dashboard + create wizard + one-time key display + Flutter snippet

**Goal:** Users can see projects and create a fully connected project.

**Files touched:**
- `console/src/features/projects/*`
- `console/src/components/common/CopyButton.tsx`, `ConfirmDialog.tsx`
- `console/src/types/api.ts`

**Task checklist:**
- [ ] Build project cards with name, environment, today's event count, last received, and active key count.
- [ ] Build the create wizard: name, description, environment; validation and submit state.
- [ ] Build the one-time success panel: API key with copy + warning, public key PEM collapsible, Flutter snippet copyable.
- [ ] Generate the Flutter snippet with `baseUrl`, `projectId`, `apiKey`, and `publicKeyPem` placeholders.
- [ ] Ensure the API key is not refetched or stored after navigation.
- [ ] Add loading skeletons, empty state, and error state.
- [ ] Add MSW/RTL tests for creation and copy actions.

**Acceptance criteria:**
- Creating a project shows the success panel exactly once.
- The API key copy button works and the warning is prominent.
- The public key PEM is collapsible and copyable.
- The Flutter snippet is valid Dart-shaped text with all four placeholders.
- Navigating away and back never reveals the API key again.
- Dashboard reflects the new project without a full reload.

**Tests:**
- Create wizard validation and success.
- One-time key visibility and copy.
- Snippet content.
- Empty/error states.

---

#### F3 — Log explorer: filters + virtualized table + detail drawer

**Goal:** The main operator screen for finding and inspecting logs.

**Files touched:**
- `console/src/features/logs/*`
- `console/src/components/common/JsonTree.tsx`
- `console/src/lib/formatters.ts`

**Task checklist:**
- [ ] Build the filter bar: level chips, tag input, time presets (15m/1h/24h/7d/custom), userId, sessionId, debounced search.
- [ ] Use TanStack Query infinite query with the cursor; implement “Load more”.
- [ ] Build a virtualized table (TanStack Virtual) with time, level chip, tag, and message preview.
- [ ] Build the detail drawer with full message, error, stack trace, metadata/`_ctx` JSON tree, and copy-as-JSON.
- [ ] Sync filters to the URL query string for shareable views.
- [ ] Add `/` keyboard shortcut to focus search; support Escape to close the drawer.
- [ ] Add empty/loading/error states.
- [ ] Add tests for filters, pagination, and drawer.

**Acceptance criteria:**
- Filters update results and survive a page refresh via the URL.
- Pagination loads more rows without duplicates or gaps.
- The drawer shows all fields and copies valid JSON.
- The table remains responsive with 10k+ rows.
- Keyboard shortcuts work.
- Cross-project data is never shown.

**Tests:**
- RTL/MSW filter and pagination tests.
- Drawer rendering and copy.
- URL state round trip.
- Keyboard navigation.

---

#### F4 — Stats + settings: keys/retention/danger zone

**Goal:** Operators can understand usage and manage project security/lifecycle.

**Files touched:**
- `console/src/features/stats/*`
- `console/src/features/settings/*`

**Task checklist:**
- [ ] Build stats charts: level donut, events area chart, top tags bar, top users bar.
- [ ] Add date-range controls with 7d/30d/custom and loading/empty states.
- [ ] Build the API keys table with prefix, created date, status, create, and revoke confirm modal.
- [ ] Build the retention form (max age days, max events) with validation and save state.
- [ ] Build project rename/description/environment editing.
- [ ] Build the danger zone with typed project-name confirmation and delete.
- [ ] Add tests for settings mutations and chart rendering.

**Acceptance criteria:**
- Charts match the stats API and handle empty data.
- A new API key is shown once and then only its prefix appears.
- Revoking a key updates the table and blocks ingest immediately.
- Retention changes persist and are reflected on reload.
- Delete requires the exact project name and navigates to the dashboard.
- All mutations show success/error toasts.

**Tests:**
- RTL/MSW stats, key create/revoke, retention save, delete confirmation.
- Chart empty-state rendering.

---

#### F5 — Polish: dark theme, empty/error states, keyboard nav + Lighthouse pass

**Goal:** A polished, accessible, performant console.

**Files touched:**
- App-wide components, styles, routes, and assets.
- `console/lighthouserc.json`

**Task checklist:**
- [ ] Finalize dark theme tokens and contrast.
- [ ] Add skeletons, empty states, error boundaries, and toast consistency.
- [ ] Add keyboard navigation and focus management for dialogs/drawers.
- [ ] Make layouts responsive down to tablet.
- [ ] Code-split routes; optimize fonts and images.
- [ ] Add Lighthouse CI for the login page; target ≥90.
- [ ] Run an accessibility pass (labels, roles, focus traps).
- [ ] Add a Playwright smoke test for login → create project → view logs (optional but recommended).

**Acceptance criteria:**
- Lighthouse performance/accessibility/best-practices/SEO ≥90 on `/login`.
- No console errors in production build.
- All dialogs/drawers are keyboard accessible.
- Tablet layout has no horizontal scroll.
- `npm run build` succeeds and the bundle is within budget.

**Tests:**
- Lighthouse CI.
- RTL accessibility checks for dialogs and forms.
- Optional Playwright smoke flow.

---

### 7.3 Phase dependency graph

```text
B1 ──► B2 ──► B3 ──► B4 ──► B5 ──► B6
                 │
                 └──────────────► F2
B2 ──────────────────────────────► F1
F1 ──► F2 ──► F3 ──► F4 ──► F5
B5 ──────────────────────────────► F3/F4
B6 ──────────────────────────────► release readiness
```

Recommended execution order:

1. B1
2. B2
3. B3
4. F1 (can start once B2 is stable)
5. B4
6. F2
7. B5
8. F3
9. F4
10. B6
11. F5

This order lets the console start against real auth/projects while ingest/query work continues, without blocking either track.

### 7.4 Definition of done for every phase

- Code builds with zero warnings.
- New/changed behavior has tests.
- API changes are reflected in `docs/api-contract.md` (or a SealedTrace-specific contract file).
- No secrets in source, logs, or test output.
- The phase's acceptance criteria are demonstrated in the review checkpoint.
- The next phase is not started without approval.

---

## 8. Dependency List

### 8.1 NuGet packages

| Package | Version | Why |
|---|---|---|
| `Microsoft.EntityFrameworkCore.SqlServer` | 8.0.x (latest 8.0 patch) | SQL Server provider; matches .NET 8. |
| `Microsoft.EntityFrameworkCore.Design` | 8.0.x | Design-time migrations. |
| `Microsoft.AspNetCore.Authentication.JwtBearer` | 8.0.x | JWT validation. |
| `BCrypt.Net-Next` | 4.0.3 | Password hashing as specified. |
| `Serilog.AspNetCore` | 8.0.x | Structured request logging. |
| `Serilog.Sinks.Console` | 6.0.x | Console sink. |
| `Serilog.Sinks.File` | 6.0.x | Rolling file sink. |
| `Swashbuckle.AspNetCore` | 6.6.x | OpenAPI/Swagger with security schemes. |
| `FluentValidation` + `FluentValidation.DependencyInjectionExtensions` | 11.x | Request validation. Prefer the DI extension over the deprecated `FluentValidation.AspNetCore` package; the spec's expected package name is the legacy integration. |
| `Microsoft.Extensions.Diagnostics.HealthChecks.EntityFrameworkCore` | 8.0.x | DB readiness check. |
| `Testcontainers.MsSql` | 3.10.x | Disposable SQL Server for integration tests. |
| `xUnit` | 2.9.x | Test framework. |
| `FluentAssertions` | 6.12.x | Readable assertions; stay on 6.x to avoid the 7.x license change. |
| `NSubstitute` | 5.x | Test doubles. |
| `Microsoft.NET.Test.Sdk` | 17.x | Test host. |
| `coverlet.collector` | 6.x | Coverage collection. |
| `prometheus-net.AspNetCore` | 8.2.x | Optional `/metrics` endpoint. |
| `Microsoft.Extensions.Caching.Memory` | 8.0.x | Private-key cache. |

Built-in framework features used without extra packages:

- `System.Diagnostics.Metrics` for counters.
- `Microsoft.AspNetCore.RateLimiting` for the fixed-window limiter.
- `System.Security.Cryptography` for RSA/AES-GCM/RNG.
- `Microsoft.Extensions.Http` only if an outbound HTTP client is added later.

### 8.2 npm packages

| Package | Version | Why |
|---|---|---|
| `react` / `react-dom` | 18.3.x | Spec-mandated React 18. |
| `react-router-dom` | 6.30.x | Routing and guards. |
| `@tanstack/react-query` | 5.x | Server state, caching, infinite queries. |
| `zustand` | 5.x | Auth state. |
| `tailwindcss` | 3.4.x | Utility styling compatible with shadcn/ui. |
| `postcss` / `autoprefixer` | latest compatible | Tailwind pipeline. |
| `shadcn/ui` (Radix primitives) | CLI-generated | Accessible UI primitives. |
| `class-variance-authority` | 0.7.x | Variant styling. |
| `clsx` / `tailwind-merge` | latest | Class composition. |
| `lucide-react` | latest | Icons. |
| `recharts` | 2.15.x | Donut/area/bar charts. |
| `@tanstack/react-virtual` | 3.x | Virtualized log table. |
| `react-hook-form` | 7.x | Form state. |
| `zod` | 3.x | Form/schema validation. |
| `@hookform/resolvers` | 3.x | Zod + react-hook-form bridge. |
| `date-fns` | 4.x | Date formatting and ranges. |
| `sonner` | 1.x | Toasts. |
| `vite` | 5.x | Build tool. |
| `@vitejs/plugin-react` | 4.x | React fast refresh. |
| `typescript` | 5.x | Strict typing. |
| `eslint` / `prettier` | 9.x / 3.x | Lint/format. |
| `vitest` | 2.x | Unit/component tests. |
| `@testing-library/react` | 16.x | Component testing. |
| `@testing-library/jest-dom` | 6.x | DOM assertions. |
| `@testing-library/user-event` | 14.x | Realistic interactions. |
| `msw` | 2.x | API mocking. |
| `jsdom` | 25.x | Test DOM. |
| `@lhci/cli` | 0.14.x | Lighthouse CI. |
| `playwright` | optional | End-to-end smoke tests. |

Version policy: pin exact versions in `package-lock.json`; for NuGet, use the latest patch of the stated minor and commit `packages.lock.json` if central package management is enabled. The console runtime is **Node.js 20 LTS**.

### 8.3 Configuration keys

| Key | Development source | Production source |
|---|---|---|
| `ConnectionStrings:Default` | `appsettings.Development.json` / user-secrets | Environment variable / Key Vault |
| `Jwt:SigningKey` | User-secrets | Key Vault |
| `Jwt:Issuer` / `Jwt:Audience` | Config | Config |
| `SealedTrace:MasterKey` | User-secrets (base64 32 bytes) | Key Vault |
| `SealedTrace:MasterKeyVersion` | Config (default 1) | Config |
| `Cors:AllowedOrigins` | `http://localhost:5173` | Console origin |
| `Search:UseFullText` | `false` locally if needed | `true` in production |
| `Retention:DefaultMaxAgeDays` | 30 | 30 |
| `Retention:DefaultMaxEvents` | 1000000 | 1000000 |
| `Metrics:Enabled` | `false` | `false` unless scraped |

---

## 9. Risks & Mitigations

| Risk | Impact | Likelihood | Mitigation |
|---|---|---|---|
| Ingest p95 exceeds 300 ms for 50 envelopes | Poor SDK experience; dropped batches | Medium | Keep decryption in memory; use `AddRange` + EF batching; benchmark with 50-envelope payloads; add indexes; consider `SqlBulkCopy`/TVP if EF batching is insufficient. |
| EF Core insert overhead on large log batches | Slow ingest; lock contention | Medium | Use one transaction per batch, `AddRange`, `MaxBatchSize` tuning, and a staging-table/SqlBulkCopy fallback behind a feature flag. |
| Database growth outpaces retention | Disk exhaustion; slow queries | High over time | Enforce per-project max age/events; nightly batched deletes; monitor table size; document TDE/backup/archival. |
| Master key loss | All project private keys become undecryptable | Low but catastrophic | Store master key in Key Vault; back up with strict access control; support key-ring versions; document recovery and rotation drills. |
| Project RSA private-key exposure | Full log decryption for that project | Low | Encrypt at rest; cache only in memory; never log; restrict DB access; rotate project keys manually if compromised. |
| Full-text search cost/latency | Slow explorer queries | Medium | Full-text index; cap query range; parameterize; monitor; fall back to `LIKE` only when necessary; add query timeouts. |
| Clock skew between SDK and server | Future-dated logs rejected; pagination confusion | Medium | Reject only `ts > now + 5 min`; store both `ClientTs` and `ReceivedAtUtc`; document NTP requirement for devices. |
| Duplicate batch race | Double insert or 500 | Medium | `BatchId` PK + unique constraint; catch `DbUpdateException`; pre-check duplicates; transaction rollback on failure. |
| Cross-tenant data leak | Severe trust breach | Low | Every project query filters by owner; 404 for non-owned resources; integration tests for isolation; no global log queries. |
| Refresh-token theft | Account takeover | Medium | HttpOnly/Secure/SameSite cookie; 15-minute access token; rotation; reuse detection revokes all user tokens. |
| API-key leakage in logs | Unauthorized ingest | Medium | Serilog redaction; never log headers; secret scanning in CI; revoke/rotate keys from settings. |
| Oversized/malicious payloads | DoS/memory pressure | Medium | 2 MB Kestrel limit; 50-envelope cap; rate limiting; JSON depth/size limits; reject early. |
| .NET 8 end of support | Security patches stop | High after Nov 2026 | Track .NET 10 upgrade; keep dependencies patched; schedule upgrade immediately after MVP. |
| Testcontainers unavailable in CI | Integration tests skipped/failing | Medium | Fallback `SEALEDTRACE_TEST_CONNECTION`; separate unit/integration test categories; document Docker requirement. |
| Spec/repo mismatch causes rework | Wasted implementation effort | High | This plan is the single source of truth; archive the old plan; review the assumptions before B1; do not mix Flutter Logs behavior into SealedTrace. |
| Console performance with large log sets | Poor UX | Medium | Cursor pagination, virtualized table, query caching, debounced search, route code-splitting. |
| Lighthouse <90 on login | Missed non-functional target | Medium | Minimal login bundle, lazy-load authenticated routes, optimize fonts/assets, run Lighthouse CI in F5. |
| Full-text not installed in production | Search falls back to slow `LIKE` | Medium | Deployment check for the full-text catalog; readiness check or startup warning; load-test the fallback. |
| Secret committed to source | Credential compromise | Low | User-secrets locally; Key Vault in production; `.gitignore`; secret scanning; rotate immediately if leaked. |

---

## Review Gate

This plan is ready for review. No SealedTrace application code has been written; the existing Flutter Logs implementation is historical reference only. On approval, begin with **B1 only**. Do not start B2 until B1 is reviewed and accepted.

The immediate review questions are:

1. Confirm the scope pivot from Flutter Logs to SealedTrace.
2. Confirm the `Projects.Description` / `Projects.Environment` schema extension.
3. Confirm the API-key encoding (base64url) and the 5-active-key cap.
4. Confirm the retention defaults (30 days / 1,000,000 events).
5. Confirm whether production SQL Server will have full-text enabled.
