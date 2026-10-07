# Claims Management System — FNOL Intake & Reserve Management

A greenfield vertical slice of an insurance claims module: first notice of loss (FNOL) intake, a controlled claim
lifecycle, three-tier reserve authority with approvals, simulated general-ledger posting, document storage, and an
append-only audit log. Built with **.NET 9 (Clean Architecture, CQRS)** and **Angular 22**, deployed on **Azure**.

| | |
|---|---|
| **Live app** | https://delightful-tree-018e99310.4.azurestaticapps.net |
| **API / Swagger** | https://claims-api-dmitriy.azurewebsites.net/swagger |
| **Health** | https://claims-api-dmitriy.azurewebsites.net/health (reports the deployed commit) |
| **Hangfire dashboard** | `/hangfire` on the API (open locally; restricted on Azure) |

**Test accounts** (mock identity provider; the login page also has one-click buttons and a role switcher in the header):

| User | Password | Role | Can |
|---|---|---|---|
| `handler` / `handler2` | `Handler#2026` | handler | create claims, manage parties, upload documents, open reserves ≤ $10,000 |
| `supervisor` | `Supervisor#2026` | supervisor | + approve/reject reserves up to $100,000, change status, reopen claims |
| `manager` | `Manager#2026` | manager | + approve up to $10,000,000, set the reserve override flag |

> These are demo-only credentials for a mock identity provider. Do not reuse them anywhere.

---

## Contents

1. [Specifications this implements](#1-specifications-this-implements)
2. [Features](#2-features)
3. [Technology stack](#3-technology-stack)
4. [Repository layout](#4-repository-layout)
5. [Run it locally](#5-run-it-locally)
6. [Configuration reference](#6-configuration-reference)
7. [Database, migrations and seed data](#7-database-migrations-and-seed-data)
8. [Tests](#8-tests)
9. [Deploy to Azure](#9-deploy-to-azure)
10. [Demo walkthrough](#10-demo-walkthrough)
11. [Specification traceability](#11-specification-traceability)
12. [Deviations from the specifications](#12-deviations-from-the-specifications)
13. [Known limitations](#13-known-limitations)
14. [Deliverables status](#14-deliverables-status)

---

## 1. Specifications this implements

The work follows two documents. Throughout this README and in code comments they are cited by these short names:

| Short name | Document | Used for |
|---|---|---|
| **FRS** | *Claims Management System: FNOL Intake & Reserve Management, Functional Specification, v1.0 (May 2026)* | Business rules (`BR-*`), closure conditions (`CC-*`), validation messages, entities, endpoints, UI behaviour, jobs, conventions |
| **Brief** | *DICEUS Fullstack Technical Assessment: Claims Module, v1.0 (May 2026)* | Required stack, Clean Architecture layout, deliverables, evaluation criteria |

Both documents are marked confidential for candidate use, so they are **not included in this repository**. Instead,
[section 11](#11-specification-traceability) maps every requirement, by section number and rule ID, to the code that
implements it and the test that proves it. Where the two documents disagree, the choice made is listed in
[section 12](#12-deviations-from-the-specifications). In source files, comments such as `FRS BR-R-03` mark the exact
line that enforces a rule.

## 2. Features

- **FNOL intake**: three-step wizard (policy and loss, parties and risk objects, reserve and review) with policy
  typeahead, in-force indicator, live authority preview and a server-side dry-run validation.
- **Claim lifecycle**: Draft → Open → Under investigation → Pending payment → Closed / Withdrawn / Reopened, enforced by a
  state machine with role checks and a closure pre-flight checklist.
- **Reserves**: Indemnity, Expense, ALAE and Subrogation components; ≤ $10k auto-approved, ≤ $100k supervisor, above that
  manager; no self-approval; reject, retract and re-submit; $10M aggregate cap with manager override.
- **GL posting simulation**: Hangfire job, idempotent and re-entrant, with retry and a manual retry from the UI.
- **SLA monitoring**: recurring job (every 15 minutes) that flags Draft/Open claims idle for 48 hours.
- **Documents**: Azure Blob Storage with short-lived SAS links; automatic local-file fallback for development.
- **Audit log**: immutable, append-only, with correlation ids; every significant action is recorded.
- **Quality**: structured error bodies, idempotency keys, optimistic concurrency, soft delete, tenant isolation, health
  endpoint reporting the deployed build.

## 3. Technology stack

| Concern | Required (Brief §2.3) | Used |
|---|---|---|
| Runtime / Web API | .NET 9, ASP.NET Core | .NET 9, C# 13, ASP.NET Core controllers |
| CQRS | MediatR 12+ | MediatR 12.5 (pinned: 13+ is commercially licensed) |
| ORM / DB | EF Core 9, SQL Server 2022 | EF Core 9, SQL Server 2022 (Azure SQL in the cloud) |
| Validation | FluentValidation | FluentValidation 11, run as a MediatR pipeline behaviour |
| Mapping | AutoMapper | AutoMapper 14 (profiles in the Application layer) |
| Jobs | Hangfire | Hangfire with SQL Server storage |
| Files | Azure Blob Storage | `IStorageService`: Azure Blob and local file system |
| Frontend | Angular 18+, Material, Reactive Forms | Angular 22 (standalone, zoneless, signals), Angular Material (custom palette), Reactive Forms |
| Architecture | Clean Architecture | Domain / Application / Persistence / Infrastructure / API |
| Hosting | Azure App Service or Container Apps | App Service (API), Static Web Apps (UI), Azure SQL, Blob Storage |
| CI/CD | GitHub Actions or Azure DevOps | GitHub Actions |

## 4. Repository layout

```
backend/
  ClaimsModule.sln, Directory.Build.props, .config/dotnet-tools.json   (dotnet-ef is pinned here)
  src/
    ClaimsModule.Domain          entities, rules (state machine, authority policy), domain events, enums
    ClaimsModule.Application     MediatR commands/queries, validators, DTOs, AutoMapper, pipeline behaviours, interfaces
    ClaimsModule.Persistence     EF Core DbContext, configurations, migrations, repositories, Unit of Work, seed data
    ClaimsModule.Infrastructure  Azure Blob / local storage, Hangfire jobs, mock JWT auth
    ClaimsModule.API             controllers, middleware, Swagger, DI composition root
  tests/ClaimsModule.Tests       xUnit: Domain, Persistence, Application, Api (real SQL Server)
frontend/                        Angular app (core / shared / layout / features)
tests/postman/                   Newman API test collection (generated from build-collection.mjs)
infra/                           main.bicep, deploy.sh, sql/ (standalone schema + seed script)
.github/workflows/               ci.yml (every push) and deploy.yml (manual)
docker-compose.yml               SQL Server 2022 + Azurite for local development
```

Dependency rule: `API → Infrastructure, Persistence → Application → Domain`. Domain depends on nothing; Application
defines the interfaces (repositories, storage, audit) that Persistence and Infrastructure implement.

## 5. Run it locally

**Prerequisites:** .NET 9 SDK (`global.json` pins it), Node.js 22+, Docker (for SQL Server and Azurite).

```bash
# 1. SQL Server 2022 + Azurite (first start of SQL Server takes up to ~30 s; on Apple Silicon enable
#    "Use Rosetta for x86_64/amd64 emulation" in Docker Desktop)
cp .env.example .env
docker compose up -d

# 2. API at http://localhost:5080 (applies migrations and seeds reference data on start-up in Development)
cd backend/src/ClaimsModule.API && dotnet run --launch-profile http

# 3. Frontend at http://localhost:4200 (new terminal)
cd frontend && npm ci && npm start
```

Open http://localhost:4200 and sign in with a test account. Swagger is at http://localhost:5080/swagger.
Local document storage defaults to the file system (`backend/src/ClaimsModule.API/uploads/`, git-ignored); to use the
Azurite emulator instead set `Storage__Provider=AzureBlob` and `Storage__AzureBlob__ConnectionString="UseDevelopmentStorage=true"`.

## 6. Configuration reference

Backend settings come from `appsettings.json`, `appsettings.{Environment}.json`, then environment variables
(`Section__Key`). The base file ships with **empty secrets**, so a non-Development deployment refuses to start until
they are set; `appsettings.Development.json` holds local-only defaults.

| Key | Example | Purpose |
|---|---|---|
| `ConnectionStrings__ClaimsDb` | `Server=localhost,1433;Database=ClaimsDb;User Id=sa;Password=…;TrustServerCertificate=True` | SQL Server (Azure SQL: `Encrypt=True`) |
| `Tenant__OrganisationId` | `0b1f6e3a-5a2d-4a53-9c2e-0e1d6f6a1001` | Single seeded organisation (must match the seed data) |
| `Jwt__SigningKey` | 32+ random characters | Signs the mock JWTs (required outside Development) |
| `Jwt__Issuer`, `Jwt__Audience`, `Jwt__ExpiryMinutes` | `claims-module`, `claims-module-ui`, `480` | Token settings |
| `Storage__Provider` | `AzureBlob` or `LocalFileSystem` | Document storage (AzureBlob without a connection string falls back to local) |
| `Storage__AzureBlob__ConnectionString` / `__ContainerName` | `…` / `claim-documents` | Azure Blob account |
| `Storage__LocalFileSystem__RootPath` / `__PublicBaseUrl` / `__SigningKey` | `uploads` / `http://localhost:5080` / 16+ chars | Local fallback and its signed download links |
| `Cors__AllowedOrigins__0` | `http://localhost:4200` | Frontend origin |
| `Hangfire__Enabled` | `true` | Run the Hangfire server and register the recurring job |
| `Hangfire__AllowAnonymousDashboard` | `false` | Opens `/hangfire` to anyone (demo only) |
| `Auth__EnableRoleSwitcher` | `true` | Role switcher and password-less test tokens (testing only) |
| `MockUsers__Users__N__…` | see `appsettings.json` | The hard-coded users |
| `Database__MigrateOnStartup` | `false` | Apply migrations when the API starts (on in Development only) |

Frontend runtime config is `frontend/public/config.json` → `{ "apiBaseUrl": "http://localhost:5080" }`. It is read at
start-up, so the same build can be pointed at any API without rebuilding.

## 7. Database, migrations and seed data

- The schema is created only by **EF Core migrations** (never by startup code). Reference data (5 simulated policies, 10
  cause-of-loss codes, 12 status transitions) is seeded inside the migrations with `HasData`.
- Apply migrations manually: `cd backend && dotnet tool restore && dotnet ef database update --project src/ClaimsModule.Persistence`
  (reads `ConnectionStrings__ClaimsDb`; defaults to the local Docker SQL Server).
- A standalone, idempotent SQL script of the schema and seed data is in
  [`infra/sql/001-claims-schema-and-seed.sql`](infra/sql/001-claims-schema-and-seed.sql); regenerate with
  `dotnet ef migrations script --idempotent`.
- Hangfire creates its own `HangFire` schema in the same database on first start; it is not part of the EF migrations.

## 8. Tests

| Suite | Command | Count | Notes |
|---|---|---|---|
| Backend (xUnit) | `cd backend && dotnet test` | 209 | Domain rules; persistence, pipeline, jobs and HTTP against a **real SQL Server** (a throw-away database per run, so Docker must be running; tests are skipped if SQL Server is unreachable) |
| Frontend (Vitest) | `cd frontend && npx ng test --no-watch` | 101 | Interceptors, auth, API client, domain helpers, components |
| API (Newman) | `cd tests/postman && npm ci && npm test` | 74 requests, 168 assertions | Drives a running API; also runs against Azure (`baseUrl` override). See [`tests/postman/README.md`](tests/postman/README.md) |

CI ([`.github/workflows/ci.yml`](.github/workflows/ci.yml)) runs all three on every push: it starts a SQL Server service
container, runs the backend tests, boots the API and runs Newman, then runs the frontend tests and production build.

## 9. Deploy to Azure

Resources (all defined in [`infra/main.bicep`](infra/main.bicep)): Azure SQL (Basic), Storage account with the
`claim-documents` container, Linux App Service plan + web app for the API, Static Web App for the frontend. CORS and
every app setting are wired by the template.

```bash
brew install azure-cli && az login
./infra/deploy.sh all        # resources → migrations → API → frontend → Newman smoke test → prints the URLs
./infra/deploy.sh info       # URLs and the values for the GitHub Actions secrets
./infra/deploy.sh destroy    # delete everything
```

Other sub-commands: `infra`, `migrate`, `api`, `web`, `smoke`. The script generates secrets once (stored in the
git-ignored `infra/.secrets.env`), registers resource providers on a fresh subscription, and falls back across regions
when a new free subscription is refused (`LOCATION`, `SWA_LOCATION`, `PLAN_SKU` and `API_NAME` override its defaults).

**CI/CD** ([`.github/workflows/deploy.yml`](.github/workflows/deploy.yml), manual trigger): build → apply migrations →
deploy the API → wait until `/health` reports *this* commit → build and deploy the frontend → Newman smoke test. It needs
variables `AZURE_WEBAPP_NAME`, `API_URL` and secrets `AZURE_WEBAPP_PUBLISH_PROFILE`,
`AZURE_STATIC_WEB_APPS_API_TOKEN`, `SQL_CONNECTION_STRING` (printed by `./infra/deploy.sh info`).

Operational notes: the API needs an App Service plan with **Always On** (B1 or higher) because the Hangfire server runs
inside it; run a single instance (the idempotency store is in memory); `az webapp log tail -g <rg> -n <app>` streams
application logs (container logging is enabled by the template).

## 10. Demo walkthrough

1. Sign in as `handler` → **Log New Claim**. Pick a policy (try `POL-2023-000099`, expired, to see the amber cover
   indicator and the acknowledgeable warning), add a claimant, and set an initial reserve above $10,000 to see "Supervisor
   approval required".
2. Create the claim, then open **Reserves**. Use the user menu to switch to `supervisor` and **Approve**. The GL badge goes
   from *Pending* to *Posted* as the Hangfire job runs.
3. Open the **Audit log** tab: every step is there, the GL posting is attributed to *System*, and entries link back to the
   reserve.
4. **Change status → Closed** shows the pre-flight checklist (open reserves need a justification). Reopening requires a
   supervisor and a reason.
5. **Documents**: upload a file and download it through the short-lived link.

## 11. Specification traceability

Every row names the rule in the specification, where it is enforced, and the test that proves it. Paths are relative to
the repository root. *Newman n* = folder *n* of the Newman collection.

Abbreviations: **D** = `backend/src/ClaimsModule.Domain`, **A** = `…Application`, **P** = `…Persistence`,
**I** = `…Infrastructure`, **API** = `…API`, **T** = `backend/tests/ClaimsModule.Tests`.

### 11.1 FRS: claim lifecycle and closure (§4, §7.3)

| Requirement | Implementation | Verified by |
|---|---|---|
| §4.2 valid transitions; anything else is 422 (**BR-ST-01**) | [`D/Rules/ClaimStateMachine.cs`](backend/src/ClaimsModule.Domain/Rules/ClaimStateMachine.cs), [`Claim.TransitionTo`](backend/src/ClaimsModule.Domain/Entities/Claim.cs); seeded to `ClaimStatusTransitions` | [`T/Domain/ClaimStateMachineTests.cs`](backend/tests/ClaimsModule.Tests/Domain/ClaimStateMachineTests.cs), [`ClaimTransitionTests.cs`](backend/tests/ClaimsModule.Tests/Domain/ClaimTransitionTests.cs); Newman 6 |
| Draft → Open needs no Critical issue and a Claimant (**BR-ST-02**, **BR-C-03**) | `Claim.TransitionTo` (Open case), `Claim.SyncStructuralIssues` | `ClaimTransitionTests`; [`T/Application/ClaimFlowTests.cs`](backend/tests/ClaimsModule.Tests/Application/ClaimFlowTests.cs) |
| Loss date outside the policy period is a Warning that must be cleared or acknowledged (**BR-C-02**) | `Claim.Create`, `acknowledgeWarnings` on `PUT /api/claims/{id}/status` | `ClaimTransitionTests`, `ClaimFlowTests`; Newman 4 |
| §4.3 closure conditions CC-01…CC-04 (**BR-ST-03**) | `Claim.ClosureBlockers`, `GetClosurePreflightQuery` in [`A/Features/Claims/Queries/ClaimQueries.cs`](backend/src/ClaimsModule.Application/Features/Claims/Queries/ClaimQueries.cs) | `ClaimTransitionTests`, `ClaimFlowTests`; Newman 10 |
| Reopen needs a supervisor and a reason, then goes straight to Open (**BR-ST-04**) | `Claim.TransitionTo` (Reopened case) | `ClaimTransitionTests`; Newman 10 |
| Withdrawal needs a reason | `Claim.TransitionTo` (Withdrawn case) | `ClaimTransitionTests`; Newman 4 |

### 11.2 FRS: FNOL intake and validation (§5, §7.1, §8)

| Requirement | Implementation | Verified by |
|---|---|---|
| §5.3 claim number `CLM-YYYY-0000000`, atomic, no gaps, never reused (**BR-C-04**) | [`D/Rules/ClaimNumberFormatter.cs`](backend/src/ClaimsModule.Domain/Rules/ClaimNumberFormatter.cs), [`P/ClaimNumberGenerator.cs`](backend/src/ClaimsModule.Persistence/ClaimNumberGenerator.cs) (single `MERGE` inside the command transaction) | [`T/Domain/ClaimNumberFormatterTests.cs`](backend/tests/ClaimsModule.Tests/Domain/ClaimNumberFormatterTests.cs), [`T/Persistence/PersistenceTests.cs`](backend/tests/ClaimsModule.Tests/Persistence/PersistenceTests.cs) (40 concurrent calls, rollback gives the number back) |
| Loss date required and not in the future (**BR-C-01**) | validator in [`A/Features/Claims/Commands/CreateClaim.cs`](backend/src/ClaimsModule.Application/Features/Claims/Commands/CreateClaim.cs); guard in [`D/Entities/LossEvent.cs`](backend/src/ClaimsModule.Domain/Entities/LossEvent.cs) | `ClaimFlowTests`; Newman 3 |
| Cause code must exist and be active (**BR-C-05**) | `CreateClaimCommandValidator` | `ClaimFlowTests`; Newman 3 |
| Description ≥ 20 characters (**BR-C-07**) | `CreateClaimCommandValidator`, `LossEvent` | `ClaimFlowTests`; Newman 3 |
| No policy → Warning; reserves blocked until a policy is linked (**BR-C-06**) | `Claim.Create`, `Claim.SubmitReserve` | `ReserveWorkflowTests`; Newman 5 |
| §5.4 Critical vs Warning issues; claim stays in Draft | [`D/Entities/ClaimValidationIssue.cs`](backend/src/ClaimsModule.Domain/Entities/ClaimValidationIssue.cs); dry-run `POST /api/claims/validate` | `ClaimFlowTests`; Newman 3 |
| §8 exact validation messages | [`D/Rules/ValidationMessages.cs`](backend/src/ClaimsModule.Domain/Rules/ValidationMessages.cs) (shared by domain, validators and UI) | `ClaimFlowTests`, [`T/Api/ApiTests.cs`](backend/tests/ClaimsModule.Tests/Api/ApiTests.cs) |
| §5.5, §5.6 seeded policies and cause codes | [`P/Seed/SeedData.cs`](backend/src/ClaimsModule.Persistence/Seed/SeedData.cs) via `HasData` | `PersistenceTests`; Newman 2 |
| Parties: roles, several of one role, last Claimant cannot be removed (**BR-P-01**, **BR-P-02**) | [`D/Entities/ClaimParty.cs`](backend/src/ClaimsModule.Domain/Entities/ClaimParty.cs), `Claim.RemoveParty` | `ClaimCreationTests`; Newman 6 |

### 11.3 FRS: reserves (§6, §7.2)

| Requirement | Implementation | Verified by |
|---|---|---|
| Amount > 0, except Subrogation which may be negative (**BR-R-01**) | `ReserveRules` in `CreateClaim.cs`, `Claim.SubmitReserve` | [`T/Domain/ReserveWorkflowTests.cs`](backend/tests/ClaimsModule.Tests/Domain/ReserveWorkflowTests.cs); Newman 7 |
| Three authority tiers on the transaction amount (**BR-R-02**) | [`D/Rules/ReserveAuthorityPolicy.cs`](backend/src/ClaimsModule.Domain/Rules/ReserveAuthorityPolicy.cs) | [`T/Domain/ReserveAuthorityPolicyTests.cs`](backend/tests/ClaimsModule.Tests/Domain/ReserveAuthorityPolicyTests.cs) (boundaries); [`T/Application/ReserveFlowTests.cs`](backend/tests/ClaimsModule.Tests/Application/ReserveFlowTests.cs); Newman 7 |
| No self-approval, 422 "Self-approval is not permitted." (**BR-R-03**) | `Claim.ApproveReserve` | `ReserveWorkflowTests`, `ReserveFlowTests`; Newman 7 |
| Rejected reserve is kept; resubmission creates a new record (**BR-R-04**) | `Claim.RejectReserve`, event-sourced [`ReserveHistory`](backend/src/ClaimsModule.Domain/Entities/ReserveHistory.cs) | `ReserveFlowTests`; Newman 7 |
| Pending reserve cannot be edited; retract → Cancelled (§6.4) | `Claim.RetractReserve` | `ReserveFlowTests`; Newman 7 |
| Total approved reserves ≤ $10,000,000 without a Manager override (**BR-R-05**; Brief **BR-R-07**) | `Claim.SubmitReserve`, `Claim.ApproveReserve`, `Claim.SetManagerOverride` | `ReserveWorkflowTests`, `ReserveFlowTests` |
| GL job idempotency key `Reserve:{id}:Change:{seq}`, re-entrant (**BR-R-06**) | [`I/Jobs/PostGlReserveChangeJob.cs`](backend/src/ClaimsModule.Infrastructure/Jobs/PostGlReserveChangeJob.cs); concurrency tokens in `EntityConfigurations.cs` | [`T/Application/JobTests.cs`](backend/tests/ClaimsModule.Tests/Application/JobTests.cs) (twice, 6-way concurrent, failure and retry); Newman 8 |
| §6.6 history: previous/new balance, who, when, GL status | `ReserveHistory`; `GET /api/claims/{id}/reserves` | `ReserveFlowTests`; Newman 7 |
| §3 roles enforced in the API and hidden in the UI | `[Authorize(Roles=…)]` in [`API/Controllers/ReservesController.cs`](backend/src/ClaimsModule.API/Controllers/ReservesController.cs); [`reserves-tab.ts`](frontend/src/app/features/claim-detail/tabs/reserves-tab.ts) | `ApiTests`; Newman 7 |

### 11.4 FRS: jobs, documents, audit (§12, §13, §14, §7.4, §7.6)

| Requirement | Implementation | Verified by |
|---|---|---|
| §12.1 GL posting job: audit `GL_POSTING_SIMULATED`, `PostingStatus = Posted`, `GL_POSTING_FAILED` after retries | `PostGlReserveChangeJob` (3 retries, final-attempt handling) | `JobTests`; Newman 8 |
| §12.2 SLA job every 15 min, Draft/Open idle 48 h, at most one entry per claim per 24 h, no status change | [`I/Jobs/SlaMonitoringJob.cs`](backend/src/ClaimsModule.Infrastructure/Jobs/SlaMonitoringJob.cs), registered in [`API/Program.cs`](backend/src/ClaimsModule.API/Program.cs) | `JobTests` |
| §13 Azure Blob, SAS URL with 1 h TTL, bytes never proxied (**BR-D-02**) | [`I/Storage/AzureBlobStorageService.cs`](backend/src/ClaimsModule.Infrastructure/Storage/AzureBlobStorageService.cs) | [`T/Persistence/AzureStorageTests.cs`](backend/tests/ClaimsModule.Tests/Persistence/AzureStorageTests.cs); Newman 9 (run against Azure) |
| Path `{org}/{claim}/{file}` with sanitised file name (**BR-D-01**); MIME allowlist; 50 MB limit | `DocumentRules` in [`UploadDocument.cs`](backend/src/ClaimsModule.Application/Features/Claims/Commands/UploadDocument.cs) | [`T/Application/DocumentTests.cs`](backend/tests/ClaimsModule.Tests/Application/DocumentTests.cs) |
| Local file-system fallback behind `IStorageService`, chosen in config (**BR-D-03**) | [`I/Storage/LocalFileSystemStorageService.cs`](backend/src/ClaimsModule.Infrastructure/Storage/LocalFileSystemStorageService.cs), selection in `Infrastructure/DependencyInjection.cs` | `ApiTests` (upload, signed link, tampered link) |
| §14 audit events, append-only through one service (**BR-A-01**, **BR-A-02**) | [`D/Entities/ClaimAuditLog.cs`](backend/src/ClaimsModule.Domain/Entities/ClaimAuditLog.cs) (`AuditEventTypes`), [`A/Services/AuditLogService.cs`](backend/src/ClaimsModule.Application/Services/AuditLogService.cs), [`A/DomainEvents/ClaimAuditEventHandlers.cs`](backend/src/ClaimsModule.Application/DomainEvents/ClaimAuditEventHandlers.cs); update/delete rejected in [`P/ClaimsDbContext.cs`](backend/src/ClaimsModule.Persistence/ClaimsDbContext.cs) | `PersistenceTests`, `ClaimFlowTests`; Newman 8 |
| Correlation id carried on every audit entry of a request | [`API/Infrastructure/CorrelationMiddleware.cs`](backend/src/ClaimsModule.API/Infrastructure/CorrelationMiddleware.cs) | `ApiTests`, `ClaimFlowTests` |

### 11.5 FRS: API, entities, conventions (§9, §10, §15)

| Requirement | Implementation | Verified by |
|---|---|---|
| §10.1–10.3 endpoints | [`API/Controllers/`](backend/src/ClaimsModule.API/Controllers) (`ClaimsController`, `ReservesController`, `ReferenceController`, plus `AuthController`, `FilesController`) | `ApiTests`; Newman 1–10; Swagger |
| §10.4 error body, 422 for validation | [`API/Infrastructure/ExceptionHandlingMiddleware.cs`](backend/src/ClaimsModule.API/Infrastructure/ExceptionHandlingMiddleware.cs), `ApiErrors.cs` | `ApiTests`; every Newman error check |
| Writes idempotent with an `Idempotency-Key` header (§10) | [`API/Infrastructure/IdempotencyMiddleware.cs`](backend/src/ClaimsModule.API/Infrastructure/IdempotencyMiddleware.cs) | `ApiTests`; Newman 3 |
| §9 the ten entities (+ `ClaimStatusTransitions`, see Brief §3.2) | [`D/Entities/`](backend/src/ClaimsModule.Domain/Entities), [`P/Configurations/EntityConfigurations.cs`](backend/src/ClaimsModule.Persistence/Configurations/EntityConfigurations.cs) | `PersistenceTests` |
| §15.1 sequential GUID keys, `DECIMAL(19,4)`, `DATETIMEOFFSET`, soft delete, audit columns, tenant, `ROWVERSION` | [`D/Common/BaseEntity.cs`](backend/src/ClaimsModule.Domain/Common/BaseEntity.cs), `BaseEntityConfiguration.cs`, `ClaimsDbContext.cs` | `PersistenceTests` (soft delete, tenant isolation, rowversion conflict) |
| §15.2 Fluent API only, global soft-delete filter | `P/Configurations/*`, `ClaimsDbContext.OnModelCreating` | `PersistenceTests` |
| §15.4 seed through migrations, reproducible from scratch | [`P/Migrations/`](backend/src/ClaimsModule.Persistence/Migrations) | every SQL-backed test (each run migrates a fresh database); CI |
| §15.3 naming (`CreateClaimCommand`, `GetClaimDetailQuery`, kebab-case routes) | `A/Features/**` | code review |

### 11.6 FRS: frontend (§11)

| Requirement | Implementation |
|---|---|
| §11.1 claims list: paginated table, colour-coded status badges, filters (status, date range, handler, cause), clickable rows, empty state | [`features/claims-list/`](frontend/src/app/features/claims-list), [`shared/badge.ts`](frontend/src/app/shared/badge.ts) |
| §11.2 three-step FNOL form: typeahead, in-force badge, unknown-policy toggle, live authority indicator, review, warning confirmation | [`features/fnol-intake/`](frontend/src/app/features/fnol-intake); logic tested in [`fnol-payload.spec.ts`](frontend/src/app/features/fnol-intake/fnol-payload.spec.ts), [`domain.spec.ts`](frontend/src/app/core/util/domain.spec.ts) |
| §11.3 claim detail: header, transition dialog with closure checklist, five tabs, slide-in reserve panel, approve/reject/retract, GL badge and retry | [`features/claim-detail/`](frontend/src/app/features/claim-detail) |
| §11.4 lazy routes, mock auth + interceptor + role switcher, Material theme, snackbar errors, loading states | [`app.routes.ts`](frontend/src/app/app.routes.ts), [`core/auth/`](frontend/src/app/core/auth), [`core/http/interceptors.ts`](frontend/src/app/core/http/interceptors.ts) (tested in `interceptors.spec.ts`, `auth.spec.ts`), [`src/theme/`](frontend/src/theme) |
| All API access through a typed service layer | [`core/api/`](frontend/src/app/core/api); components never inject `HttpClient` |

### 11.7 Brief: stack, architecture and what reviewers check (§2.3, §2.4, §6.1)

| Brief requirement | Where |
|---|---|
| Five Clean Architecture projects with the named responsibilities | [`backend/src/`](backend/src) (section 4) |
| Every state change is a Command, every read a Query, through MediatR | `A/Features/**`, [`A/Common/Messaging.cs`](backend/src/ClaimsModule.Application/Common/Messaging.cs) (`ICommand`/`IQuery`) |
| FluentValidation wired at the MediatR pipeline, not just the controller | [`A/Behaviors/PipelineBehaviors.cs`](backend/src/ClaimsModule.Application/Behaviors/PipelineBehaviors.cs) (`ValidationBehavior`) |
| Domain events raised and handled (e.g. `ClaimCreated` → audit entry) | [`D/Events/DomainEvents.cs`](backend/src/ClaimsModule.Domain/Events/DomainEvents.cs); dispatched by `TransactionBehavior`; handlers in `A/DomainEvents/` |
| Unit of Work coordinating the transaction | [`P/Repositories/UnitOfWork.cs`](backend/src/ClaimsModule.Persistence/Repositories/UnitOfWork.cs), `TransactionBehavior` |
| AutoMapper profiles in the Application layer | [`A/Mapping/MappingProfile.cs`](backend/src/ClaimsModule.Application/Mapping/MappingProfile.cs) |
| EF Core: value conversions, global filters (soft delete and tenant) | `ClaimsDbContext.cs`, `EntityConfigurations.cs` |
| Hangfire idempotency key strategy | `PostGlReserveChangeJob.cs` (section 11.3) |
| Azure Blob behind an interface for testability | `IStorageService` in [`A/Abstractions/Services.cs`](backend/src/ClaimsModule.Application/Abstractions/Services.cs) |
| Middleware error pipeline, structured logging | `ExceptionHandlingMiddleware.cs`, Serilog in `Program.cs` |

### 11.8 Brief: database, endpoints and rules where it differs from the FRS (§3)

| Brief requirement | Where / how |
|---|---|
| §3.2 `ClaimStatusTransitions` table, `ClaimType` on claims | seeded from the state machine; `ClaimType` = the cause code's peril category |
| §3.3.2 `GET /api/policies/{id}/coverage` | `ReferenceController` |
| §3.3.3 `PUT /api/claims/{id}/reserves/{reserveId}` adjusts a reserve | `AdjustReserveCommand` (the id is the reserve **component**); also `POST` with `transactionType` as in the FRS |
| §3.4 BR-R-02…BR-R-04 tiers; BR-R-06 resubmission; BR-R-07 total cap | same implementation as the FRS rules in 11.3 |
| §3.4 BR-C-06 a Draft claim may not go straight to Closed | state machine: Draft → Open only; Open → Closed allowed |
| §3.5 SLA job "flags with a SlaBreached status" | **audit entry only**, claim status unchanged (the FRS says so explicitly); see section 12 |
| §3.6 Azure Blob, SAS 1 h, local fallback | section 11.4 |
| §3.7 frontend screens and general requirements | section 11.6 |
| §3.8 Azure deployment and basic CI/CD | section 9; [`infra/`](infra), [`.github/workflows/`](.github/workflows) |

## 12. Deviations from the specifications

Choices made where the specifications are silent, ambiguous, or disagree. Each is deliberate and covered by tests.

| # | Topic | Decision | Why |
|---|---|---|---|
| 1 | FRS vs Brief: tenant column | `OrganisationId` (FRS) rather than `OrganizationEntityId` | The FRS has the detailed schema |
| 2 | FRS vs Brief: reserve components | Indemnity, Expense, ALAE, SubrogationRecoverable (FRS), not Brief Appendix A's Recovery/Litigation | The FRS defines the rules per component |
| 3 | FRS vs Brief: SLA breach | Audit entry only, no status change | FRS §12.2: "Do not change claim status" |
| 4 | Warnings that must be acknowledged (FRS §4.2, BR-C-02) | `acknowledgeWarnings` flag on the status endpoint; no separate endpoint | The FRS requires acknowledgement but defines no endpoint |
| 5 | Validation issues | New `ClaimValidationIssues` table (Critical/Warning, resolved/acknowledged) | The FRS refers to issues "recorded against the claim" and audits them but lists no table |
| 6 | Reserve direction | Request amount is a magnitude; `transactionType` (Add, Adjust, Reverse) sets the sign; only Subrogation accepts negative | Resolves "amount > 0" vs "Reverse" |
| 7 | Reserves on Closed/Withdrawn claims | Rejected with 422 | Sensible domain rule not stated in the specs |
| 8 | Idempotency key | `Reserve:{ReserveComponentId}:Change:{seq}` | The specs' "ReserveId" is ambiguous |
| 9 | Audit log columns | No soft-delete columns | It is append-only; updates and deletes are blocked in code |
| 10 | GUIDs | Assigned in the domain with a SQL Server-ordered generator; columns still default to `NEWSEQUENTIALID()` | Aggregates reference each other (audit, idempotency keys) before saving |
| 11 | GL journal for decreases | Mirror entry (DR Outstanding Loss Reserves / CR Change in Outstanding Reserves), positive amount | Increases book the entry the FRS defines |
| 12 | Document blob names | `{8-hex}-{clean name}`; downloads are saved with the clean name | Two uploads with the same name must not overwrite each other |
| 13 | Storage setting name | `Storage:Provider` (Brief wording) | FRS says `StorageProvider` |
| 14 | Extra endpoints | `POST /claims/validate`, `GET /claims/{id}/closure-preflight`, `PUT /claims/{id}/manager-override`, `PUT /claims/{id}/notes`, `POST …/retry-posting`, `/api/auth/*` | Needed by the UI behaviours the FRS describes |
| 15 | Wizard vs API on the claimant | The UI requires a Claimant to continue; the API accepts a Draft without one (Critical issue) | FRS §5.2/§11.2 (UI) vs §5.4 (API) |
| 16 | Dependency pins | MediatR 12.5 (13+ licensed); AutoMapper 14 with advisory GHSA-rvv3-g6hj-g44x suppressed with a documented reason (15+ needs a license key; only flat entity→DTO maps, no cyclic graphs) | Licensing |

## 13. Known limitations

- **No outbox for GL jobs.** The job is queued after the transaction commits; if the process dies in between, the
  reserve is approved but unposted. The UI's retry button covers the failure case; an outbox table is the production fix.
- **Idempotency store is in memory**, so it is per instance. Run a single API instance or swap in a shared store.
- **Hangfire runs inside the API process**, which needs Always On (B1 or higher on Azure).
- **Deployments interrupt in-flight requests** on the old container (no deployment slots on this plan).
- **Mock identity provider.** Passwords are in configuration; it is not real authentication.
- **Local file storage is for development only**; it is not suitable on App Service.
- No email or push notifications (explicitly out of scope: SLA breaches only write audit entries).

## 14. Deliverables status

| Brief §4 deliverable | Status |
|---|---|
| Source repository (backend, frontend, migrations, seed script, docker-compose, CI/CD definition) | Done |
| `README.md` with setup, configuration, migrations, Azure deployment, walkthrough | Done (this file) |
| Deployed backend (Swagger) and frontend, test credentials | Done (top of this file) |
| CI/CD pipeline | Done (`.github/workflows`) |
| `ARCHITECTURE.md` | To do |
| `AI-WORKFLOW.md` and AI interaction history export | To do |
