# Gambling Levy Suite

A secure, multi-org web application for DHSC to administer the gambling harm prevention levy — collecting data from ~153 Local Authorities and VCSE organisations and producing cross-org analytical reporting. Built with .NET 10 and React 18, hosted on Azure with Microsoft Entra External ID (CIAM) authentication.

## Table of Contents

- [Architecture](#architecture)
- [Prerequisites](#prerequisites)
- [Project Structure](#project-structure)
- [Getting Started](#getting-started)
- [Configuration](#configuration)
- [Testing](#testing)
- [Deployment](#deployment)
- [Documentation](#documentation)
- [Development in the open](#development-in-the-open)
- [Licence](#licence)

---

## Architecture

A full-stack application consisting of:

- **API**: .NET 10 REST API — Clean Architecture (`Domain`, `Application`, `Infrastructure`, `Api` layers), MediatR/CQRS for writes (`Either<TException, TResult>` folded via `ToObjectResult()`), a controller-service layer for read/DTO composition, FluentValidation, custom TOTP MFA (`Otp.NET`) enforced via middleware, and organisation-suspension gating
- **Client**: React 18 + TypeScript SPA — Vite, GOV.UK Design System (`govuk-frontend` + project `GovUKComponents` wrappers), MSAL v5 authentication, TanStack Query v5
- **Database**: PostgreSQL 15 — EF Core with snake_case naming convention (`EFCore.NamingConventions`); ~550 backend files across 16 controllers, ~70 MediatR commands and 49 migrations spanning Activities, AuditLogs, Categories, Collections, DataCollectionForms, Forms, Funding, KeyResults, Locations, LocationUsers, Organisations, OrganisationUsers, Submissions and Users
- **Authentication**: Microsoft Entra External ID (CIAM) — federated email-OTP sign-in via OIDC/OAuth2; MFA is a bespoke in-platform TOTP layer (not Entra Conditional Access)
- **Identity**: Microsoft Graph API — CIAM user provisioning
- **Storage**: Azure Blob Storage — file-upload answers and generated export documents (quarterly/yearly PDFs and CSVs)
- **Monitoring**: Application Insights (dashboards, metrics, distributed tracing) — the target/primary observability platform; Sentry remains wired in parallel during the transition (see [design_docs/Technical-Architecture-Spec.md](design_docs/Technical-Architecture-Spec.md))
- **Notifications**: GOV.UK Notify (email) — the decided target provider; SendGrid remains the only implemented integration until the Notify integration is built
- **Hosting**: Azure App Service — zip-deployed from Bitbucket Pipelines via the `atlassian/azure-web-apps-deploy` pipe
- **CI/CD**: Bitbucket Pipelines — parallel client/API builds plus the backend test suite on every pull request; automatic deploy to MVP on push to `mvp`, with manual promotion to UAT

For the full architecture diagrams and detailed technical specification see [design_docs/Technical-Architecture-Spec.md](design_docs/Technical-Architecture-Spec.md), [design_docs/docs/C4_Context.puml](design_docs/docs/C4_Context.puml) (system context) and [design_docs/docs/C4_Container.puml](design_docs/docs/C4_Container.puml) (container view).

---

## Prerequisites

### Required

- **.NET 10 SDK**: [Download](https://dotnet.microsoft.com/download/dotnet/10.0)
- **Node.js 24 (LTS)** — pinned to the 24.x line in `client/.nvmrc` and `client/package.json` `engines` (`cd client && nvm use`); CI and both Dockerfiles build on `node:24`, matching the Azure App Service runtime, which Microsoft only offers at major granularity (`24-lts`): [Download](https://nodejs.org/)
- **Docker Desktop**: [Download](https://www.docker.com/products/docker-desktop) — required both for the full local stack (`docker compose`) and for backend integration tests (one shared PostgreSQL Testcontainer per run, unless `TEST_POSTGRES_CONNECTION` points at a server)
- **Git**: [Download](https://git-scm.com/downloads)

### Azure Services (for full local dev)

Provisioned by the sibling infrastructure repo `gambling-levy-infrastructure` (Terraform) — values come from `terraform output`:

- Microsoft Entra External ID (CIAM) tenant + app registrations
- Microsoft Graph API application (user provisioning)
- Azure Blob Storage account
- SendGrid API key (optional locally — email sending no-ops without one)
- Sentry DSN / Application Insights connection string (optional locally)

---

## Project Structure

```
gambling-levy-suite/
├── api/                               # .NET 10 API
│   ├── src/
│   │   ├── Api/                       # Controllers, controller-services, middleware, endpoint modules
│   │   ├── Application/                # MediatR commands + validators, I*Queries interfaces, permissions
│   │   ├── Domain/                     # Entities, value objects, aggregate exception hierarchies
│   │   └── Infrastructure/             # EF Core, repositories, Graph/SendGrid/Blob/MFA/Sentry integrations
│   └── tests/
│       ├── Api.Tests.Integration/      # Integration tests (a PostgreSQL database per test class)
│       ├── Application.Tests.Unit/     # Pure calculation, value-object and seed-protocol tests
│       ├── Tests.Common/               # BaseIntegrationTest, TestFactory
│       └── Tests.Data/                 # Test data builders
├── client/                             # React 18 + TypeScript SPA
│   └── src/
│       ├── components/
│       │   ├── GovUKComponents/        # GOV.UK Design System wrappers — source of truth for UI primitives
│       │   └── Global/                 # Shared feature components
│       ├── views/                      # Admin / OrganisationAdmin / Activities / Reports / Funding / Public / Mfa
│       ├── layouts/                     # GeneralLayout, ManagementLayout, OrganisationManagementLayout
│       └── helpers/                     # Cache keys, route registration, test-id helpers
├── design_docs/                        # Technical architecture, data schema, ERD, C4 diagrams
├── docker-compose.yml                  # Local dev orchestration (Traefik, Postgres, API, client)
└── bitbucket-pipelines.yml             # CI/CD (Bitbucket Pipelines → Azure App Service)
```

Infrastructure (Terraform — Azure resource group, App Services, CIAM tenant, app registrations) lives in the sibling repo `gambling-levy-infrastructure`.

---

## Getting Started

### Option 1: Docker (recommended)

The full stack (Traefik, PostgreSQL, API, client) is orchestrated by `docker-compose.yml`. It expects Entra/Graph values from the infrastructure repo — there is no `.env.example` checked in, so create a local `.env` with at least:

```bash
APP_PREFIX=gambling-levy
POSTGRES_USER=your_user
POSTGRES_PASSWORD=your_password
POSTGRES_DB_NAME=gambling-levy-db
AD_INSTANCE=https://your-tenant.ciamlogin.com/
AD_TENANT_ID=your-tenant-id
AD_CLIENT_ID_API=your-api-client-id
AD_CLIENT_ID_CLIENT=your-spa-client-id
AD_CLIENT_SECRET=your-client-secret
AD_AUTHORITY=https://your-tenant.ciamlogin.com/
AD_REDIRECT_URI=/
AD_SCOPES=api://your-api-client-id/user_impersonation
GRAPH_APPLICATION_ID=your-graph-app-id
GRAPH_APPLICATION_OBJECT_ID=your-graph-app-object-id
GRAPH_ENTERPRISE_APPLICATION_OBJECT_ID=your-graph-enterprise-app-object-id
WORK_DIR=.
API_ROOT_DIR=./api
CLIENT_ROOT_DIR=./client
```

Then:

```bash
docker compose up --build
```

This starts:

- PostgreSQL on `localhost:5432`
- API at `http://${APP_PREFIX}-api.localtest.me` (via Traefik) / `localhost:5000` directly
- Client at `http://${APP_PREFIX}-client.localtest.me` (via Traefik) / `localhost:3000` directly
- Traefik dashboard at `http://localhost:8080`

### Option 2: Local Development (without Docker)

#### 1. Clone the Repository

```bash
git clone https://github.com/dhsc-govuk-infrastructure/analytics-gambling-levy-apps.git
cd analytics-gambling-levy-apps
```

#### 2. Start PostgreSQL

Any local PostgreSQL 15 instance works; create a database and user for the app to connect with.

#### 3. Configure the API

Create `api/src/Api/appsettings.Development.json` (copy real values from the infrastructure repo's `terraform output`):

```json
{
  "ConnectionStrings": {
    "DefaultConnection": "Host=localhost;Port=5432;Database=gambling-levy-db;Username=your_user;Password=your_password;"
  },
  "AzureAd": {
    "Instance": "https://your-tenant.ciamlogin.com/",
    "TenantId": "your-tenant-id",
    "ClientId": "your-api-client-id",
    "ClientSecret": "your-client-secret",
    "Domain": "your-tenant"
  },
  "Graph": {
    "TenantId": "your-tenant-id",
    "ApplicationId": "your-graph-app-id",
    "ApplicationObjectId": "your-graph-app-object-id",
    "EnterpriseApplicationObjectId": "your-graph-enterprise-app-object-id",
    "DefaultUserPassword": "",
    "Domain": "your-tenant"
  },
  "DefaultAdminUser": {
    "Email": "",
    "SubId": ""
  },
  "Sentry": { "Dsn": "" },
  "ApplicationInsights": { "ConnectionString": "" },
  "SendGrid": { "ClientUrl": "", "EmailApiKey": "", "SenderEmail": "" },
  "AzureBlob": { "ConnectionString": "" },
  "Cors": { "AllowedOrigins": ["http://localhost:3000"] }
}
```

#### 4. Start the API

```bash
dotnet run --project api/src/Api/Api.csproj
```

Migrations and seed data run automatically on startup (`app.InitializeDb()` / `app.SeedData()` in `Program.cs`) — no separate `dotnet ef database update` step is needed. The API is available at `https://localhost:5001`.

#### 5. Configure the Client

Update `client/src/config.json`:

```json
{
  "api_url": "https://localhost:5001/",
  "ad_tenant_id": "your-tenant-id",
  "ad_client_id": "your-spa-client-id",
  "ad_authority": "https://your-tenant.ciamlogin.com/",
  "ad_scope": "api://your-api-client-id/user_impersonation",
  "ad_redirect_uri": "/",
  "ad_domain_hint": "",
  "accessibility_testing_enabled": "true",
  "sentry_dsn": "",
  "app_insights_connection_string": ""
}
```

`client/.husky/pre-commit` **hard-blocks any staged change to `client/src/config.json`** — dev-only secrets stay local.

#### 6. Install and Start the Client

```bash
npm --prefix client install
npm --prefix client run dev
```

The client is available at `http://localhost:3000`.

---

## Configuration

### API Configuration Files

- **appsettings.json**: Base configuration
- **appsettings.Development.json**: Local development overrides (not committed)
- **appsettings.Test.json**: Used by `Api.Tests.Integration`

### Client Configuration

The client uses `client/src/config.json` (or matching `VITE_*` environment variables) for runtime configuration:

| Key | Description |
|-----|-------------|
| `api_url` | Backend API base URL |
| `ad_tenant_id` | Entra External ID tenant ID |
| `ad_authority` | Entra External ID authority URL (`https://<tenant>.ciamlogin.com/`) |
| `ad_client_id` | SPA application (client) ID |
| `ad_scope` | API scope for access token requests |
| `ad_redirect_uri` | OAuth redirect URI |
| `ad_domain_hint` | Optional domain hint for sign-in |
| `accessibility_testing_enabled` | Enables the `@axe-core/react` console checker |
| `sentry_dsn` | Sentry DSN for client-side error tracking |
| `app_insights_connection_string` | Application Insights connection string for client-side telemetry |

---

## Testing

### API Tests

```bash
# Build
dotnet build api/GamblingLevy.sln

# Run all tests (requires Docker — one shared PostgreSQL Testcontainer per run)
dotnet test api/GamblingLevy.sln

# ...or against a PostgreSQL server you already run (how CI runs them; no Docker needed)
TEST_POSTGRES_CONNECTION="Host=127.0.0.1;Port=5432;Username=postgres;Password=postgres;Database=postgres" dotnet test api/GamblingLevy.sln

# Run a single integration test
dotnet test api/tests/Api.Tests.Integration/Api.Tests.Integration.csproj --filter "FullyQualifiedName~TestMethodName"
```

`Api.Tests.Integration` inherits `BaseIntegrationTest`; each test class boots the real API against its own freshly created PostgreSQL database (`gl_test_{guid}`), dropped when the class finishes; `Application.Tests.Unit` covers pure calculation, value-object and seed-protocol logic without Docker.

### Client

Unit tests for pure client logic run on [Vitest](https://vitest.dev/) (`client/vitest.config.mts`); there is no component or end-to-end suite yet:

```bash
npm --prefix client run build   # "tsc && vite build" — the typecheck is the real gate
npm --prefix client run lint    # eslint (commit time adds --max-warnings 0 and prettier --check via lint-staged)
npm --prefix client test        # vitest run over client/src/**/*.test.ts ("run test:watch" to watch)
npm --prefix client run format  # prettier --write over client/src
```

---

## Deployment

### CI/CD (Bitbucket Pipelines)

Defined in [bitbucket-pipelines.yml](bitbucket-pipelines.yml):

- **Pull requests**: builds the client (running its Vitest unit tests first) and API in parallel, then runs the backend test suite (2x-size step, `xUnit.MaxParallelThreads=2`, against the native `postgres-tests` service container rather than Testcontainers). No separate lint step; the SonarQube analysis step exists but is currently commented out. The test step also measures backend code coverage (coverlet, configured in `api/coverlet.runsettings`): the line and branch totals are printed in the step log and the full HTML report is kept as the step's `coverage-report` artifact. Coverage is reported, not gated.
- **`mvp` branch**: build → test → deploy client and API to the **MVP** environment automatically, via the `atlassian/azure-web-apps-deploy` pipe (zip deploy, no Docker image involved in production). Promotion to **UAT** is a manual trigger on the same pipeline; a **PROD** stage is defined but currently commented out.
- **Publish to open source repo** runs automatically after every successful MVP deploy and publishes that commit to the public repository — see [Development in the open](#development-in-the-open).
- A manual "Upload to client github" stage mirrors the `client/` and `api/` folders to a separate GitHub repository's `develop` branch.

### Required Azure Configuration

1. **App Registration** in Entra External ID — redirect URIs per environment, "Customers only" account type, API scopes exposed to the SPA registration.
2. **Azure App Service** — one for the API, one for the client; application settings populated from Bitbucket pipeline variables (`AZURE_APP_ID`, `AZURE_PASSWORD`, `AZURE_TENANT_ID`, `AZURE_RESOURCE_GROUP`, `AZURE_HUB_APP_NAME`, `AZURE_API_APP_NAME`).
3. **Azure Blob Storage** — file-upload answers and generated export documents.
4. **PostgreSQL** — migrations and seed data apply automatically on API startup; no manual migration step is required post-deploy.

---

## Documentation

- [Technical Architecture Specification](design_docs/Technical-Architecture-Spec.md) — full architecture, data model and technical decisions
- [Data Schema Specification](design_docs/Data-Schema-Spec.md) — target domain schema (reference, not a to-do list)
- [C4 System Context](design_docs/docs/C4_Context.puml) / [C4 Container Diagram](design_docs/docs/C4_Container.puml) — render at [editor.plantuml.com](https://editor.plantuml.com) or via the checked-in `.png` renders in the same folder
- [GOV.UK Service Standard Alignment](design_docs/GOV-UK-Service-Standard-Alignment.md)
- [MFA Implementation](design_docs/MFA-Implementation.md) and [MFA / Azure AD Limitations](design_docs/MFA-Azure-AD-Limitations.md)

### Key Technologies

- [.NET 10](https://learn.microsoft.com/en-us/dotnet/core/whats-new/dotnet-10)
- [React 18](https://react.dev/)
- [GOV.UK Design System](https://design-system.service.gov.uk/)
- [MSAL React](https://github.com/AzureAD/microsoft-authentication-library-for-js)
- [MediatR](https://github.com/jbogard/MediatR)
- [FluentValidation](https://docs.fluentvalidation.net/)
- [Entity Framework Core](https://learn.microsoft.com/en-us/ef/core/)
- [Otp.NET](https://github.com/kspearrin/Otp.NET) — TOTP MFA
- [Microsoft Entra External ID](https://learn.microsoft.com/en-us/entra/external-id/)

### Common Issues

**`AADSTS50011: Redirect URI mismatch`**
Add the redirect URI to the SPA app registration in the Azure Portal under Authentication.

**Database connection fails locally**
Check the connection string in `appsettings.Development.json`, ensure PostgreSQL is running, and that the user has the correct grants.

**Integration tests cannot reach PostgreSQL**
Locally, make sure Docker is running (the suite starts one Testcontainer), or set `TEST_POSTGRES_CONNECTION` to a server-level connection string. In CI, check that the step lists the `postgres-tests` service and exports `TEST_POSTGRES_CONNECTION`.

**CORS errors**
Configure allowed origins in `appsettings.json`/`appsettings.Development.json` under `Cors.AllowedOrigins`.

**Commit rejected by pre-commit hook**
`client/.husky/pre-commit` runs `lint-staged` (`eslint --max-warnings 0` — any warning fails the commit — then `prettier --check`) and hard-blocks staged changes to `client/src/config.json`. Fix the lint warning, run `npm --prefix client run format`, or unstage the config file.

---

## Development in the open

The service is developed in a private Bitbucket repository and published continuously to the public repository [dhsc-govuk/gamblinglevy](https://github.com/dhsc-govuk/gamblinglevy).

- Every pull request is built and tested in Bitbucket Pipelines before it can merge.
- Every merge to the `mvp` branch is built, tested and deployed to the MVP environment, then the same commit is published to the public repository's `main` branch automatically. Each public commit names the source commit it came from.
- What is published is defined in one place, [scripts/open-source/stage.sh](scripts/open-source/stage.sh): the client and API source, the design documentation, `docker-compose.yml`, the pipeline configuration, this README and the licence. Files holding environment-specific credentials are excluded, and deployment secrets live in pipeline variables, never in the repository.
- Before anything is pushed, the publish step fails if a Markdown link points to a file that is not published ([scripts/open-source/check-links.sh](scripts/open-source/check-links.sh)) or if [gitleaks](https://github.com/gitleaks/gitleaks) finds anything that looks like a secret.

To report an issue or suggest a change, open an issue on the public repository.

## Licence

The source code is released under the [MIT licence](LICENSE). Documentation is © Crown copyright and available under the [Open Government Licence v3.0](https://www.nationalarchives.gov.uk/doc/open-government-licence/version/3/).
