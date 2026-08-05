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

---

## Architecture

A full-stack application consisting of:

- **API**: .NET 10 REST API — Clean Architecture (`Domain`, `Application`, `Infrastructure`, `Api` layers), MediatR/CQRS for writes (`Either<TException, TResult>` folded via `ToObjectResult()`), a controller-service layer for read/DTO composition, FluentValidation, custom TOTP MFA (`Otp.NET`) enforced via middleware, and organisation-suspension gating
- **Client**: React 18 + TypeScript SPA — Vite, GOV.UK Design System (`govuk-frontend` + project `GovUKComponents` wrappers), MSAL React v3 authentication, React Query v3, MUI v5 (data grids and date pickers only)
- **Database**: PostgreSQL 15 — EF Core with snake_case naming convention (`EFCore.NamingConventions`); ~550 backend files across 16 controllers, ~70 MediatR commands and 49 migrations spanning Activities, AuditLogs, Categories, Collections, DataCollectionForms, Forms, Funding, KeyResults, Locations, LocationUsers, Organisations, OrganisationUsers, Submissions and Users
- **Authentication**: Microsoft Entra External ID (CIAM) — federated email-OTP sign-in via OIDC/OAuth2; MFA is a bespoke in-platform TOTP layer (not Entra Conditional Access)
- **Identity**: Microsoft Graph API — CIAM user provisioning
- **Storage**: Azure Blob Storage — file-upload answers and generated export documents (quarterly/yearly PDFs and CSVs)
- **Monitoring**: Sentry.io (client + API error/exception tracking) and Application Insights (dashboards, metrics, distributed tracing)
- **Notifications**: SendGrid (email) — the architecture diagrams below reflect a planned move to GOV.UK Notify
- **Hosting**: Azure App Service — zip-deployed from Bitbucket Pipelines via the `atlassian/azure-web-apps-deploy` pipe
- **CI/CD**: Bitbucket Pipelines — parallel client/API builds plus the backend test suite on every pull request; automatic deploy to MVP on push to `mvp`, with manual promotion to UAT

For the full architecture diagrams and detailed technical specification see [design_docs/Technical-Architecture-Spec.md](design_docs/Technical-Architecture-Spec.md), [design_docs/docs/C4_Context.puml](design_docs/docs/C4_Context.puml) (system context) and [design_docs/docs/C4_Container.puml](design_docs/docs/C4_Container.puml) (container view).

---

## Prerequisites

### Required

- **.NET 10 SDK**: [Download](https://dotnet.microsoft.com/download/dotnet/10.0)
- **Node.js 18+** (CI builds on Node 20): [Download](https://nodejs.org/)
- **Docker Desktop**: [Download](https://www.docker.com/products/docker-desktop) — required both for the full local stack (`docker compose`) and for backend integration tests (Testcontainers boots PostgreSQL)
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
│       ├── Api.Tests.Integration/      # Testcontainers-backed integration tests
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
NPM_ORGANISATION_TOKEN=your-npm-token
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
- **appsettings.Test.json**: Used by `Api.Tests.Integration` / Testcontainers

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

# Run all tests (requires Docker — Testcontainers boots PostgreSQL)
dotnet test api/GamblingLevy.sln

# Run a single integration test
dotnet test api/tests/Api.Tests.Integration/Api.Tests.Integration.csproj --filter "FullyQualifiedName~TestMethodName"
```

`Api.Tests.Integration` inherits `BaseIntegrationTest` and boots a real PostgreSQL Testcontainer per test class; `Application.Tests.Unit` covers pure calculation, value-object and seed-protocol logic without Docker.

### Client

There is no frontend test runner (no vitest/jest/RTL/Playwright) — `tsc` and review are the only automated checks:

```bash
npm --prefix client run build   # "tsc && vite build" — the typecheck is the real gate
npm --prefix client run lint    # eslint --max-warnings 0 (also enforced at commit time via lint-staged)
```

---

## Deployment

### CI/CD (Bitbucket Pipelines)

Defined in [bitbucket-pipelines.yml](bitbucket-pipelines.yml):

- **Pull requests**: builds the client and API in parallel, then runs the backend test suite (2x-size step, `TESTCONTAINERS_RYUK_DISABLED=true`, `xUnit.MaxParallelThreads=2` against a 5120 MB Docker sidecar). No lint step, no frontend tests; the SonarQube analysis step exists but is currently commented out.
- **`mvp` branch**: build → test → deploy client and API to the **MVP** environment automatically, via the `atlassian/azure-web-apps-deploy` pipe (zip deploy, no Docker image involved in production). Promotion to **UAT** is a manual trigger on the same pipeline; a **PROD** stage is defined but currently commented out.
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

**Testcontainers fails in CI or locally**
Ensure the Docker service is available and running before executing `dotnet test`.

**CORS errors**
Configure allowed origins in `appsettings.json`/`appsettings.Development.json` under `Cors.AllowedOrigins`.

**Commit rejected by pre-commit hook**
`client/.husky/pre-commit` runs `lint-staged` (`eslint --max-warnings 0` — any warning fails the commit) and hard-blocks staged changes to `client/src/config.json`. Fix the lint warning or unstage the config file.
