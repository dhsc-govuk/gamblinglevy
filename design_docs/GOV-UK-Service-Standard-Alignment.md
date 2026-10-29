# GOV.UK Service Standard Alignment Assessment

## Gambling Levy Portal – Standards 9-14

**Document Version:** 2.2 | **Date:** July 2026 (v2.2: replaced Sentry with Application Insights and SendGrid with GOV.UK Notify throughout §9, §10, §11 and §14)
**Assessment Date:** TBC
**Platform:** Gambling Levy Portal
**Domain:** DHSC gambling harm prevention levy administration; Local Authority and VCSE reporting

---

## Executive Summary

This document assesses the Gambling Levy Portal against GOV.UK Service Standard items 9-14, providing evidence of compliance, architectural justifications, and recommendations for continued alignment. The platform demonstrates strong alignment with government service standards through its cloud-native architecture, security-first design, and use of modern, widely-adopted technologies. Assessment evidence reflects the April 2026 data model and architecture — see [Data-Schema-Spec.md](Data-Schema-Spec.md) and [Technical-Architecture-Spec.md](Technical-Architecture-Spec.md).

---

## Standard 9: Create a Secure Service Which Protects Users' Privacy

### Assessment: STRONG ALIGNMENT

The Gambling Levy Portal embeds security throughout its architecture following Secure By Design principles.

### Evidence of Compliance

#### 9.1 Secure By Design Principles

| Principle | Implementation | Evidence |
|-----------|----------------|----------|
| **Defence in Depth** | Multiple security layers from network to application | Private VNET for API/DB, HTTPS everywhere, Entra JWT validation, custom TOTP MFA middleware, RBAC |
| **Least Privilege** | Role-based access with granular permissions | Two-tier role model (Org-membership + DHSC role claim); endpoint-level authorisation |
| **Separation of Duties** | Distinct user populations for data entry, admin, verification, and analysis | Reporting-org users (lead / contributor / finance / read-only per `UserOrgMembership`) vs DHSC Super Admin / Data Verifier / Data Analyst |
| **Fail Secure** | Default deny approach | Policy-based authorisation enforced per endpoint; MFA middleware rejects non-exempt calls without a valid MFA session |

#### 9.2 Authentication Architecture

**Identity Provider:** Azure Entra External ID (federated email-OTP sign-in)

| Component | Security Control | Justification |
|-----------|-----------------|---------------|
| **Protocol** | OIDC Authorization Code + PKCE | PKCE prevents authorisation code interception attacks; industry standard for SPAs |
| **Sign-in method** | Federated email identity with email OTP | Supports users from multiple external email domains without requiring them to register in the DHSC tenant |
| **Client Library** | MSAL (Microsoft Authentication Library) | Well-maintained, security-audited library with automatic token refresh |
| **Token Validation** | JWKS endpoint validation | Cryptographic verification of token signatures prevents token forgery |
| **Token Type** | JWT Bearer tokens | Stateless, tamper-evident tokens with expiry controls |

#### 9.3 Multi-Factor Authentication (MFA)

MFA is enforced via a **custom TOTP layer** built into the portal, layered on top of Entra authentication. Entra Conditional Access is **not** used for MFA — federated email identities from multiple external domains cannot reliably register authentication methods in the DHSC tenant. See [MFA-Azure-AD-Limitations.md](MFA-Azure-AD-Limitations.md) for full rationale, and [MFA-Implementation.md](MFA-Implementation.md) for the implementation plan.

| User Population | MFA Requirement | Factors Supported |
|-----------------|-----------------|-------------------|
| DHSC Super Admin | **Mandatory** | Any RFC 6238 TOTP authenticator (Microsoft Authenticator, Google Authenticator, Authy, etc.) |
| DHSC Data Verifier | **Mandatory** | As above |
| DHSC Data Analyst | **Mandatory** | As above |
| Reporting-org users (LA / VCSE) | **Mandatory** | As above |

**MFA security controls:**

| Control | Implementation |
|---------|----------------|
| **Secret storage** | Encrypted at rest; shown once at enrolment |
| **Recovery codes** | Generated at enrolment; hashed with bcrypt; shown once |
| **Rate limiting** | 5 attempts per minute; lockout after repeated failures |
| **Session binding** | MFA session cookie bound to IP + User-Agent; HttpOnly / Secure / SameSite=None (the API can be on a different site from the client), plus a `Partitioned` copy when the request is cross-site; 8-hour expiry |
| **Backend enforcement** | API middleware enforces MFA on every non-exempt endpoint — frontend bypass is not possible |

**Decision Justification:** Custom TOTP MFA works for any user regardless of their home email domain and sits independently of Entra's tenant-MFA model. This trades the convenience of a managed MFA service for guaranteed enforcement across all users — a worthwhile trade-off given the federated identity model.

#### 9.4 Authorisation Model

```
Authorisation Implementation:
├── Entra ID (Authentication level)
│   ├── Authenticates user via federated email OTP
│   └── Issues JWT with user identity claims
│
├── Application Roles (Authorisation level)
│   ├── DHSC role claim (Super Admin / Data Verifier / Data Analyst)
│   └── UserOrgMembership.role (lead / contributor / finance / read_only)
│       └── Per-Org, scoped by org_id — supports multi-Org membership
│
├── Custom MFA Middleware (Step-up level)
│   └── Requires valid MFA session cookie on every non-exempt endpoint
│
└── Endpoint Policies (Enforcement level)
    └── [Authorize(...)] per controller or action — explicit deny-by-default
```

**Policy-Based Authorisation:** Every API endpoint explicitly declares the required DHSC role claim and/or the required `UserOrgMembership.role` (scoped by `org_id`). Immutable resources (Submission, SubmissionValue post-submit) additionally reject mutating calls after `submitted_at`. See [Technical-Architecture-Spec.md](Technical-Architecture-Spec.md) §5.3 for the full permission matrix.

#### 9.5 Org Isolation (Multi-Organisation Security)

| Control | Implementation | Risk Mitigated |
|---------|----------------|----------------|
| **Request Scoping** | Every request from a reporting-org user is scoped to the `org_id`(s) from their active `UserOrgMembership` records | Cross-org data access |
| **Query Filtering** | Application-level filtering on all reads and writes | Data leakage |
| **Proxy Submission Attribution** | `Submission.proxy_org_id` records the acting Org; `org_id` remains the data-owning Org | Audit clarity for proxy flows |
| **Row-Level Security** | Optional PostgreSQL RLS | Database-level enforcement |
| **Cross-Org Access** | Only via DHSC Super Admin or Data Analyst roles | Privilege escalation |

**Decision Justification:** Application-level org isolation with optional database-level RLS provides defence in depth. The shared database model with `org_id` columns is a proven pattern for multi-organisation SaaS, balancing isolation with operational simplicity. Explicit `proxy_org_id` on `Submission` ensures audit records always capture who submitted the data (User) and which legal entity owns it (Org).

#### 9.6 Data Protection and Privacy

| Requirement | Implementation | Evidence |
|-------------|----------------|----------|
| **Data Minimisation** | No passwords stored; Entra handles credentials | `User` table contains metadata only (no password hashes) |
| **Secure Storage** | PostgreSQL within Private VNET | No public database endpoint |
| **Encryption at Rest** | Azure managed encryption | Azure Storage and PostgreSQL encryption |
| **Encryption in Transit** | HTTPS/TLS everywhere | All network communication encrypted |
| **Secure File Storage** | Azure Blob Storage with org-scoped containers / prefixes | Isolation pattern per Org |
| **Audit Trail** | Immutable `EventLog` table | All logins, data modifications, exports, and role changes logged |
| **Peer-View Redaction** | `Question.tag = 'narrative'` values redacted in peer directory views | Limits cross-org disclosure of qualitative data |

#### 9.7 Audit Logging

Two complementary mechanisms:

- **`EventLog`** — polymorphic field-level audit across every entity (`entity_type` + `entity_id` + `field_name` + `old_value` + `new_value` + `user_id` + `change_reason`). Captures diffs automatically.
- **`KeyResultReviewLog`** — human-readable intent trail for DHSC admin actions on KRs (`created` / `updated` / `archived` / `reinstated` / `scoping_changed` + notes).

```
Logged Events (via EventLog):
├── Authentication Events
│   ├── Successful logins (Entra + MFA)
│   ├── Failed authentication attempts
│   └── MFA challenges and recovery-code use
│
├── Data Events
│   ├── Create / Update / Delete / Status transitions
│   ├── Export generation (captured additionally in SubmissionExport)
│   └── File uploads / downloads
│
├── Administrative Events
│   ├── UserOrgMembership role changes
│   ├── Org onboarding status changes
│   └── Collection, FormVersion, and KeyResult configuration changes
│
└── Storage
    ├── Immutable audit tables (PostgreSQL)
    └── Structured logs also sent to Application Insights
```

**Submission audit:** Submitter IP and User-Agent are captured implicitly via the MFA session binding; `Submission.proxy_org_id` records proxy context when a user submits on behalf of another Org.

#### 9.8 Third-Party Risk Assessment

| Third Party | Risk Assessment | Mitigation |
|-------------|-----------------|------------|
| **Azure Entra External ID** | Low — Microsoft managed, SOC2 / ISO 27001 certified | Enterprise SLA; used for authentication only (MFA is in-platform) |
| **Azure Blob Storage** | Low — Private VNET, Microsoft managed | Soft-delete enabled; org-scoped isolation |
| **PostgreSQL (Managed)** | Low — Private VNET, automated backups | Azure managed; daily backups |
| **Application Insights** | Medium — External logging service | No PII in logs; correlation IDs only |
| **GOV.UK Notify** | Low — GDS-provided government email service | Transactional only; no sensitive data in emails |

### Recommendations for Standard 9

1. **Formalise Security Risk Register** — Document identified risks, owners, and review cycles.
2. **Penetration Testing** — Schedule regular third-party security assessments; prioritise the custom MFA path.
3. **Security Training** — Ensure team maintains security awareness.
4. **Incident Response Plan** — Document procedures for security incidents, including MFA-compromise scenarios.

---

## Standard 10: Define What Success Looks Like and Publish Performance Data

### Assessment: PARTIAL ALIGNMENT (Infrastructure in Place)

The platform has observability infrastructure but requires defined KPIs and public performance reporting.

### Evidence of Compliance

#### 10.1 Observability Architecture

| Layer | Tool | Metrics Captured |
|-------|------|------------------|
| **Application** | Application Insights | Exceptions, performance traces, correlation IDs |
| **Frontend** | Application Insights | Client-side errors, user sessions, page load times |
| **Backend** | Application Insights | API errors, slow queries, exceptions |

#### 10.2 Alerting Configuration

| Alert Type | Trigger | Purpose |
|------------|---------|---------|
| HTTP 5xx Spikes | Error rate threshold | Service availability |
| Authentication Failures | Failure rate threshold | Security monitoring (Entra + MFA) |
| Query Latency | Response time threshold | Performance degradation |
| Container Restarts | Restart count | Infrastructure stability |
| Worker Job Failures | Reminder / export job failure rate | Background-job health |

#### 10.3 Non-Functional Requirements (Defined Targets)

| Metric | Target | Measurement Method |
|--------|--------|-------------------|
| **Response Time** | < 2 seconds typical | Application Insights performance traces |
| **Availability** | 99.9% uptime | Azure monitoring + Application Insights |
| **Scalability** | Horizontal container scaling | Azure container metrics |

#### 10.4 Recommended Performance Indicators

| KPI Category | Suggested Metrics | User Problem Addressed |
|--------------|-------------------|----------------------|
| **Maturity Confirmation** | % of Orgs completing annual maturity confirmation before deadline | LAs can maintain an up-to-date strategic profile |
| **Quarterly Return Timeliness** | % of Orgs submitting quarterly returns before deadline | Financial and performance data is collected on schedule |
| **Verification Turnaround** | Time from `submitted` to `accepted` / `returned` | DHSC can verify data quality promptly |
| **Cross-Org Benchmarking** | Report generation time for peer directory / maturity benchmarking views | Analysts can produce policy reports efficiently |
| **Accessibility** | WCAG 2.2 AA compliance score | Service is accessible to all |
| **Reliability** | Service uptime percentage | Service is available when needed |

### Recommendations for Standard 10

1. **Define Service KPIs** — Establish metrics that reflect user outcomes, not just technical performance.
2. **Implement Performance Dashboard** — Create internal dashboard for monitoring defined KPIs.
3. **Plan Public Reporting** — Consider publishing performance data (response times, availability) on a public dashboard.
4. **User Feedback Mechanism** — The `Feedback` entity supports in-journey and post-submission capture; connect this to a weekly DHSC review cycle.

---

## Standard 11: Choose the Right Tools and Technology

### Assessment: STRONG ALIGNMENT

Technology choices demonstrate clear reasoning for quality, sustainability, and avoiding vendor lock-in.

### Evidence of Compliance

#### 11.1 Technology Stack Justification

| Component | Technology | Decision Rationale |
|-----------|------------|-------------------|
| **Backend Framework** | .NET 10 / ASP.NET Core | Current LTS; strong performance; extensive ecosystem; cross-platform |
| **Frontend Framework** | React 18 + TypeScript | Industry standard; large talent pool; strong typing for maintainability |
| **Database** | PostgreSQL 15+ | Open source; no licensing costs; excellent performance; Azure managed option |
| **Identity** | Azure Entra External ID | Federated email-OTP authentication; enterprise-grade; reduces custom auth code |
| **MFA** | Custom TOTP (RFC 6238) | Works across federated email domains where Entra Conditional Access cannot (see [MFA-Azure-AD-Limitations.md](MFA-Azure-AD-Limitations.md)) |
| **Containerisation** | Docker | Platform-agnostic deployment; consistent environments; industry standard |
| **CI/CD** | Bitbucket Pipelines | Integrated with repository; declarative configuration |

#### 11.2 Build vs Buy Decisions

| Capability | Decision | Justification |
|------------|----------|---------------|
| **Authentication (identity)** | BUY (Azure Entra) | Security-critical; managed service reduces risk; supports federated email identity |
| **MFA** | BUILD (custom TOTP) | Entra Conditional Access MFA cannot be reliably applied to federated email identities from external domains — see [MFA-Azure-AD-Limitations.md](MFA-Azure-AD-Limitations.md) |
| **Email Delivery** | BUY (GOV.UK Notify) | GDS-provided cross-government notification service; built for GOV.UK services; no per-vendor lock-in |
| **Monitoring** | BUY (Application Insights) | Proven platform; reduces operational overhead; rich features |
| **Database** | BUY (Managed PostgreSQL) | Automated backups; patching; scaling; reduces DBA overhead |
| **Statistical-neighbour data** | REUSE (external reference dataset, admin-loaded) | An external reference set already defines these groupings; reimplementing the derivation in-product would duplicate effort and risk drifting from the agreed methodology |
| **Business Logic** | BUILD (Custom API) | Domain-specific; competitive advantage; control over functionality |
| **Dynamic Forms + KR-driven form generation** | BUILD (Custom) | Highly domain-specific; no suitable off-the-shelf solution. The `KeyResultQuestion` join table lets DHSC admins shape quarterly returns without code deploys |

#### 11.3 Open Standards and Interoperability

| Standard | Usage | Lock-in Mitigation |
|----------|-------|-------------------|
| **OAuth 2.0 / OIDC** | Authentication protocol | Could migrate to any OIDC provider |
| **TOTP (RFC 6238)** | MFA codes | Works with any standard authenticator app |
| **REST/JSON** | API design | Platform-agnostic; widely supported |
| **JWT** | Token format | Industry standard; not Azure-specific |
| **PostgreSQL** | Database | Open source; portable to any PostgreSQL host |
| **Docker** | Containerisation | Runs on any container platform (Azure, AWS, GCP, on-prem) |
| **HTTPS/TLS** | Transport security | Universal standard |

#### 11.4 Total Cost of Ownership Considerations

| Cost Factor | Approach | Benefit |
|-------------|----------|---------|
| **Licensing** | PostgreSQL (open source) vs SQL Server | No per-core licensing fees |
| **Hosting** | Azure PaaS vs IaaS | Reduced operational overhead |
| **Talent** | .NET and React | Large talent pool; competitive salaries |
| **Maintenance** | Managed services where feasible | Automated patching; backups; scaling |
| **Migration** | Open standards | Reduces switching costs |

#### 11.5 Architecture Patterns

| Pattern | Implementation | Benefit |
|---------|----------------|---------|
| **Clean Architecture** | Domain, Application, Infrastructure, API layers | Testability; maintainability; separation of concerns |
| **CQRS** | MediatR for commands/queries | Clear separation of read/write operations |
| **Repository Pattern** | Interface-based data access | Database abstraction; testability |
| **Domain-Driven Design** | Aggregate roots; value objects; entities | Business logic encapsulation |
| **Event-sourced audit** | Polymorphic `EventLog` across all entities | Uniform regulatory traceability without per-entity drift |
| **Versioned configuration** | `FormVersion` + `KeyResultVersion` | Historic submissions render against the exact definitions in force at submission time |

**Decision Justification:** Clean Architecture ensures the domain logic is independent of frameworks, databases, and UI, enabling future technology migrations without rewriting business rules.

### Recommendations for Standard 11

1. **Technology Radar** — Maintain documented technology decisions and review annually.
2. **Deprecation Planning** — Monitor .NET LTS timelines and plan upgrade paths.
3. **Cost Monitoring** — Implement Azure cost alerts and regular spend reviews.

---

## Standard 12: Make New Source Code Open

### Assessment: REQUIRES REVIEW

Current repository is hosted on Bitbucket with no public visibility indicated.

### Evidence of Current State

| Aspect | Current State | Standard Requirement |
|--------|---------------|---------------------|
| **Repository** | Bitbucket (private) | Consider public repository |
| **Licence** | Proprietary | Open licence recommended |
| **Sensitive Data** | Secrets in environment variables | Correctly separated from code |
| **Documentation** | Technical specs at repository root | Good practice |

### Considerations for Open Source

| Code Component | Open Source Feasibility | Rationale |
|----------------|------------------------|-----------|
| **API / Domain Logic** | Possible | Generic compliance patterns could benefit others |
| **React Components** | Possible | UI patterns may be reusable |
| **Infrastructure Config** | Caution | May expose deployment details |
| **Business Rules** | Review Required | May contain policy-specific logic |
| **Custom TOTP MFA module** | Possible (with review) | Reusable pattern for services with federated identity constraints |
| **Dynamic forms + KR-driven form generation** | Possible | Generalisable pattern for no-code admin-editable surveys |

### Recommendations for Standard 12

1. **Open Source Review** — Assess which components could be open-sourced.
2. **Licence Selection** — Consider MIT or OGL (Open Government Licence) for public code.
3. **Secret Scanning** — Implement pre-commit hooks to prevent credential leaks.
4. **Documentation** — Ensure open code is well-documented for community use.
5. **Justification Document** — If remaining closed source, document the reasons (security, policy sensitivity).

---

## Standard 13: Use and Contribute to Open Standards, Common Components and Patterns

### Assessment: STRONG ALIGNMENT

The platform extensively uses open standards and common patterns.

### Evidence of Compliance

#### 13.1 Open Standards Adoption

| Standard | Usage | Compliance |
|----------|-------|------------|
| **OAuth 2.0** | Authorisation framework | Full compliance |
| **OpenID Connect** | Authentication layer | Full compliance |
| **TOTP (RFC 6238)** | MFA codes | Full compliance |
| **JWT (RFC 7519)** | Token format | Full compliance |
| **REST** | API architectural style | Full compliance |
| **JSON** | Data interchange format | Full compliance |
| **HTTPS/TLS** | Transport security | Full compliance |
| **SQL** | Database queries | Full compliance |

#### 13.2 Common Government Patterns

| Pattern | Implementation | Alignment |
|---------|----------------|-----------|
| **Role-Based Access Control** | Two-tier: DHSC role claim + per-Org `UserOrgMembership.role` | Standard RBAC pattern |
| **Multi-Organisation** | Shared database with `org_id` scoping; explicit proxy attribution via `proxy_org_id` | Common SaaS pattern with auditable proxy flow |
| **Audit Logging** | Polymorphic `EventLog` (field-level diffs) + `KeyResultReviewLog` (admin intent) | GDS recommended practice |
| **Form Submission Workflow** | Draft → Submitted → Accepted / Returned | Standard workflow pattern |
| **Versioned Configuration** | `FormVersion` + `KeyResultVersion`; submissions freeze the version at submit time | Historic-fidelity pattern |

#### 13.3 API Design Standards

```
RESTful API Conventions:
├── Resource-based URLs (/orgs/{orgId}/activities/{activityId}/key-results)
├── HTTP verbs (GET, POST, PUT, DELETE)
├── JSON request/response bodies
├── Consistent error model with correlation IDs
└── Bearer token authentication + custom MFA session cookie
```

**Error Response Standard:**
```json
{
  "error_code": "COLLECTION_CLOSED",
  "description": "Submission rejected: the Q2 2026/27 collection closed on 2026-10-15",
  "correlation_id": "abc-123-def"
}
```

#### 13.4 Data Interoperability

| Capability | Implementation | Standard |
|------------|----------------|----------|
| **Data Export** | CSV, Parquet, SQL Views | Open formats |
| **BI Integration** | Direct Query / Extract support | PowerBI compatible |
| **Reporting Schema** | Read-optimised PostgreSQL views | Standard SQL |

#### 13.5 Reusable Components Created

| Component | Description | Reusability |
|-----------|-------------|-------------|
| **Dynamic Forms System** | Schema-driven form builder with versioning — `Collection → FormVersion → Section → Question + Component → SubmissionValue` | High — extractable as a shared component |
| **KR-driven form generation** | `KeyResultQuestion` join table that dynamically shapes quarterly returns from org-registered KRs, with no code deploy required | High — generalisable to any domain needing admin-editable dynamic forms |
| **Custom TOTP MFA layer** | In-platform RFC 6238 MFA with rate limiting, recovery codes, and session binding — works across federated identities | High — reusable for any service facing Entra Conditional Access limitations |
| **Explicit row-per-rule conditional logic** | `QuestionDependency` table as an admin-editable alternative to JSON-blob conditions | Medium — common pattern |
| **Data Lists** | Reusable option sets with soft-deletion preserving historic submission labels | High — generic pattern |
| **Org Isolation Pattern** | Application-level multi-organisation with optional RLS and explicit proxy attribution | High — reusable architecture |

### Recommendations for Standard 13

1. **GOV.UK Design System** — Confirm frontend components align with GDS patterns.
2. **Share Patterns** — Consider publishing the dynamic-forms + KR-driven pattern to the cross-government community.
3. **API Documentation** — Publish OpenAPI/Swagger specification.
4. **Cross-Government Collaboration** — Engage with other departments using similar patterns.

---

## Standard 14: Operate a Reliable Service

### Assessment: STRONG ALIGNMENT

Architecture designed for high availability with comprehensive monitoring.

### Evidence of Compliance

#### 14.1 Availability Targets

| Metric | Target | Implementation |
|--------|--------|----------------|
| **Uptime** | 99.9% | Azure PaaS SLA; container orchestration |
| **Response Time** | < 2 seconds typical | Performance monitoring via Application Insights |
| **Recovery Time** | Minimised | Automated container restart; managed services |

#### 14.2 Deployment Architecture for Reliability

```
High Availability Design:
├── Azure Cloud Services
│   ├── Client (Azure App Service, zip deploy) — Horizontally scalable
│   ├── API (Azure App Service, zip deploy) — Horizontally scalable
│   ├── Worker (Docker Container) — unverified against this repo, see Technical-Architecture-Spec.md §3.3
│   ├── PostgreSQL (Managed Instance) — Azure SLA backed
│   └── Blob Storage (Storage Account) — Geo-redundant
│
├── Network Resilience
│   ├── Private VNET for backend services
│   ├── HTTPS termination
│   └── Load balancing via Azure
│
└── Data Protection
    ├── Automated daily DB backups
    └── Blob soft-delete enabled
```

#### 14.3 Deployment Strategy

| Aspect | Implementation | Benefit |
|--------|----------------|---------|
| **CI/CD Pipeline** | Bitbucket Pipelines | Automated, consistent deployments |
| **Container Deployment** | Docker images | Immutable deployments; rollback capability |
| **Environment Parity** | Same container across environments | Reduces environment-specific bugs |
| **Automated Testing** | Integration tests in pipeline | Catches issues before deployment |

**Pipeline Stages:**
```
Pull Request → Build (API + Client) → Run Tests → (merge)
Branch Deploy → Build → Test → Deploy to Environment
```

#### 14.4 Monitoring and Alerting

| Alert | Trigger | Response |
|-------|---------|----------|
| **HTTP 5xx Spikes** | Error rate threshold | Investigate API errors |
| **Authentication Failures** | Entra or MFA failure rate | Check for attacks; system issues |
| **Query Latency** | Slow response | Database optimisation; scaling |
| **Container Restarts** | Restart events | Infrastructure investigation |
| **Worker Job Failures** | Reminder / export worker failures | Background-job health triage |

#### 14.5 Quality Assurance

| QA Activity | Implementation | Coverage |
|-------------|----------------|----------|
| **Unit Tests** | xUnit framework | Domain logic |
| **Integration Tests** | Testcontainers + PostgreSQL | API endpoints; data access |
| **Code Analysis** | StyleCop; FluentValidation | Code quality; input validation |
| **Security Scanning** | Pipeline integration | Vulnerability detection |

#### 14.6 Performance Testing

Performance testing ensures the platform meets its non-functional requirements and can handle expected load patterns.

##### 14.6.1 Performance Testing Strategy

| Test Type | Purpose | Frequency |
|-----------|---------|-----------|
| **Load Testing** | Validate system behaviour under expected concurrent users | Pre-release; quarterly |
| **Stress Testing** | Identify breaking points and degradation patterns | Pre-release; major changes |
| **Soak Testing** | Detect memory leaks and performance degradation over time | Quarterly |
| **Spike Testing** | Validate system response to sudden traffic surges | Pre-release |
| **Baseline Testing** | Establish performance benchmarks for regression comparison | Each release |

##### 14.6.2 Performance Test Scenarios

| Scenario | Description | Success Criteria |
|----------|-------------|------------------|
| **Concurrent Quarterly Return Submissions** | Multiple Orgs submitting quarterly returns simultaneously at deadline | < 2s response; 0% error rate |
| **Submission Verification Workflow** | DHSC Verifiers processing submissions across multiple Orgs | < 2s response; correct Org isolation |
| **KR-driven Form Rendering** | Dynamic generation of quarterly forms from an Org's Activities + Key Results | < 2s for forms up to 100 questions |
| **Report Generation** | Large dataset exports and analytics queries (peer directory, maturity benchmarking) | < 30s for standard reports |
| **Authentication Flow** | Concurrent login requests through Entra + custom MFA verification | < 3s including MFA challenge |
| **API Endpoint Throughput** | High-volume API requests across all endpoints | Sustained 100 req/s per container |
| **File Upload/Download** | Concurrent document operations to Blob Storage | < 5s for files up to 10MB |

##### 14.6.3 Performance Testing Tools

| Tool | Purpose | Integration |
|------|---------|-------------|
| **k6 (Grafana)** | Load and stress testing | CI/CD pipeline integration; scriptable scenarios |
| **Azure Load Testing** | Cloud-native load testing | Azure-integrated; auto-scaling validation |
| **Lighthouse** | Frontend performance metrics | Core Web Vitals; accessibility scoring |
| **Application Insights** | Real-user monitoring (RUM) | Production performance traces; P95 latency |

##### 14.6.4 Performance Benchmarks

| Metric | Target | Measurement | Alert Threshold |
|--------|--------|-------------|-----------------|
| **API Response Time (P50)** | < 500ms | Application Insights | > 1s |
| **API Response Time (P95)** | < 2s | Application Insights | > 3s |
| **Page Load Time (FCP)** | < 1.5s | Lighthouse | > 2.5s |
| **Time to Interactive (TTI)** | < 3s | Lighthouse | > 5s |
| **Database Query Time (P95)** | < 100ms | Application Insights traces | > 500ms |
| **Error Rate** | < 0.1% | Application Insights errors | > 1% |
| **Throughput** | > 100 req/s | Load tests | < 50 req/s |

##### 14.6.5 Performance Testing in CI/CD

```
Pipeline Performance Gates:
├── Pull Request
│   ├── Lighthouse CI (Performance budget check)
│   └── Unit test performance assertions
│
├── Pre-Deployment (Staging)
│   ├── k6 smoke tests (quick validation)
│   ├── API response time checks
│   └── Database migration performance
│
├── Post-Deployment
│   ├── Synthetic monitoring (health checks)
│   └── Application Insights performance baseline comparison
│
└── Scheduled (Weekly/Quarterly)
    ├── Full load test suite
    ├── Soak testing (extended duration)
    └── Capacity planning review
```

##### 14.6.6 Performance Test Environment

| Aspect | Configuration | Rationale |
|--------|---------------|-----------|
| **Environment Parity** | Staging mirrors production configuration | Realistic performance results |
| **Data Volume** | Anonymised production-scale dataset | Accurate query performance |
| **Isolation** | Dedicated test windows; no shared load | Consistent, repeatable results |
| **Container Resources** | Production-equivalent CPU/memory limits | Accurate scaling behaviour |

##### 14.6.7 Performance Regression Prevention

| Practice | Implementation | Benefit |
|----------|----------------|---------|
| **Performance Budgets** | Lighthouse CI thresholds | Automated frontend regression detection |
| **Baseline Comparison** | k6 threshold assertions | API regression detection |
| **Query Plan Analysis** | EF Core query logging | Database performance visibility |
| **Bundle Size Monitoring** | Webpack Bundle Analyzer | Frontend payload control |
| **APM Alerting** | Application Insights performance alerts | Real-time production monitoring |

##### 14.6.8 Scalability Validation

| Test | Method | Expected Outcome |
|------|--------|------------------|
| **Horizontal Scaling** | Increase container replicas under load | Linear throughput increase |
| **Database Connection Pooling** | Connection pool saturation tests | Graceful handling; no connection leaks |
| **Blob Storage Throughput** | Concurrent file operations | No throttling under normal load |
| **Background Worker Scaling** | Queue depth monitoring (reminder / export jobs) | Processing keeps pace with submissions |

**Decision Justification:** A comprehensive performance testing strategy ensures the platform meets its 99.9% availability and < 2s response time targets. Integrating performance gates into CI/CD prevents regression, while scheduled load tests validate capacity planning.

#### 14.7 Backup and Recovery

| Component | Backup Strategy | Retention |
|-----------|-----------------|-----------|
| **PostgreSQL Database** | Automated daily backups | Azure managed retention |
| **Blob Storage** | Soft-delete enabled | Configurable retention |
| **Application Logs** | Application Insights retention | Platform retention policy |
| **Audit Logs** | Immutable `EventLog` table | Compliance-driven retention |

#### 14.8 Scalability

| Component | Scaling Approach | Trigger |
|-----------|------------------|---------|
| **API Containers** | Horizontal scaling | CPU / Memory thresholds |
| **Client Containers** | Horizontal scaling | Request volume |
| **Worker Containers** | Horizontal scaling | Queue depth |
| **Database** | Vertical scaling (managed) | Performance metrics |

### Recommendations for Standard 14

1. **Disaster Recovery Plan** — Document RTO/RPO and recovery procedures.
2. **Chaos Engineering** — Consider introducing failure testing (e.g., container kills).
3. **On-Call Procedures** — Document incident response and escalation.
4. **Capacity Planning** — Regular review of scaling thresholds.
5. **Status Page** — Consider public status page for transparency.

---

## Summary Assessment Matrix

| Standard | Rating | Key Strengths | Areas for Improvement |
|----------|--------|---------------|----------------------|
| **9. Security & Privacy** | Strong | Custom TOTP MFA with backend enforcement; org isolation with auditable proxy flow; polymorphic `EventLog` + admin-intent `KeyResultReviewLog` | Formalise security risk register; penetration test the custom MFA path |
| **10. Performance Data** | Partial | Monitoring infrastructure in place; `Feedback` entity ready for user-satisfaction capture | Define KPIs; public reporting |
| **11. Tools & Technology** | Strong | Open standards; clear build/buy decisions; managed services; KR-driven dynamic forms | Maintain technology radar |
| **12. Open Source Code** | Needs Review | Clean separation of secrets; MFA module and dynamic-forms pattern are strong candidates for public release | Assess open source feasibility |
| **13. Common Standards** | Strong | REST, OAuth, OIDC, TOTP, JSON; versioned configuration; reusable patterns | Share patterns with community |
| **14. Reliable Service** | Strong | HA architecture; CI/CD; monitoring; backups; worker-level scaling | DR planning; status page |

---

## Appendix A: Security Architecture Summary

```
┌─────────────────────────────────────────────────────────────────────┐
│                    SECURITY ARCHITECTURE                            │
├─────────────────────────────────────────────────────────────────────┤
│                                                                     │
│  ┌─────────────┐    HTTPS    ┌─────────────────────────────────┐   │
│  │   Browser   │────────────→│  Azure Entra External ID        │   │
│  │   (User)    │←────────────│  • OIDC/OAuth2 + PKCE           │   │
│  └──────┬──────┘   Tokens    │  • Federated email-OTP sign-in  │   │
│         │                    │    (MFA handled in-platform)    │   │
│         │ JWT                └─────────────────────────────────┘   │
│         │ Bearer +                                                  │
│         │ MFA cookie                                                │
│         ▼                                                           │
│  ┌─────────────┐                                                   │
│  │ React SPA   │  Application Insights (Client errors)             │
│  │ (MSAL)      │                                                   │
│  └──────┬──────┘                                                   │
│         │                                                           │
│         │ HTTPS + JWT + MFA session cookie                          │
│         ▼                                                           │
│  ┌─────────────────────────────────────────────────┐               │
│  │              PRIVATE VNET                        │               │
│  │  ┌─────────────────────────────────────────┐   │               │
│  │  │  .NET 10 API                             │   │               │
│  │  │  • JWT Validation (JWKS)                 │   │               │
│  │  │  • Custom TOTP MFA middleware            │   │               │
│  │  │  • RBAC per endpoint                     │   │               │
│  │  │    (DHSC role + UserOrgMembership.role)  │   │               │
│  │  │  • Org isolation (org_id scoping,        │   │               │
│  │  │    proxy_org_id for proxy flows)         │   │               │
│  │  │  • Audit logging (EventLog)              │   │               │
│  │  └──────────────┬──────────────────────────┘   │               │
│  │                 │                               │               │
│  │       ┌─────────┴─────────┐                    │               │
│  │       ▼                   ▼                    │               │
│  │  ┌─────────┐      ┌─────────────┐             │               │
│  │  │PostgreSQL│      │ Blob Storage │             │               │
│  │  │• RLS     │      │ • Org-scoped │             │               │
│  │  │• EventLog│      │   prefixes   │             │               │
│  │  │• Backups │      │ • Soft delete│             │               │
│  │  └─────────┘      └─────────────┘             │               │
│  └─────────────────────────────────────────────────┘               │
│                                                                     │
└─────────────────────────────────────────────────────────────────────┘
```

---

## Appendix B: Technology Decision Records

### TDR-001: Identity Provider Selection

**Decision:** Use Azure Entra External ID for authentication

**Context:** Service requires secure authentication for government users from multiple external email domains handling sensitive gambling levy data.

**Options Considered:**
1. Custom authentication implementation
2. Auth0
3. Azure Entra External ID
4. AWS Cognito

**Decision Rationale:**
- Federated email-OTP flow supports users from any domain without tenant registration
- Enterprise-grade service with strong SLAs
- Native integration with Azure hosting environment
- No custom credential storage required

**Consequences:**
- Dependency on Microsoft Azure ecosystem (mitigated by standard OIDC protocol — portable to other providers)
- Entra Conditional Access MFA is **not** applicable to federated email identities — MFA is instead implemented in-platform (see TDR-004)

---

### TDR-002: Database Selection

**Decision:** Use PostgreSQL over SQL Server

**Context:** Service requires relational database for structured data with complex queries.

**Options Considered:**
1. SQL Server
2. PostgreSQL
3. MySQL
4. CosmosDB

**Decision Rationale:**
- Open source with no licensing costs
- Excellent performance for OLTP workloads
- Rich feature set (JSONB, Row-Level Security)
- Azure managed service available
- Large community and ecosystem
- Avoids vendor lock-in

**Consequences:**
- Team may need PostgreSQL-specific knowledge
- Some SQL Server features not available
- Mitigated by standard SQL compatibility

---

### TDR-003: Frontend Framework Selection

**Decision:** Use React 18 with TypeScript

**Context:** Service requires modern, maintainable frontend for complex forms and workflows, including dynamically generated KR-driven quarterly returns.

**Options Considered:**
1. React
2. Angular
3. Vue.js
4. Server-rendered (Razor Pages)

**Decision Rationale:**
- Large talent pool for recruitment
- Strong TypeScript support for maintainability
- Extensive component ecosystem (MUI)
- Well-suited for complex SPA requirements, including dynamic form rendering
- Industry standard with long-term viability

**Consequences:**
- Requires JavaScript/TypeScript expertise
- SPA complexity vs server-rendered simplicity
- Mitigated by established patterns and tooling

---

### TDR-004: MFA Implementation — Custom TOTP over Entra Conditional Access

**Decision:** Build a custom RFC 6238 TOTP MFA layer in the portal rather than rely on Entra Conditional Access.

**Context:** Users authenticate via federated email identity (email OTP) against Entra External ID from multiple external domains. Entra's Conditional Access MFA typically requires users to be members of the DHSC tenant with registered authentication methods — which federated external identities do not have.

**Options Considered:**
1. Entra Conditional Access MFA
2. SMS-only second factor
3. Custom TOTP MFA (RFC 6238)
4. Hardware tokens

**Decision Rationale:**
- TOTP works with any standard authenticator app, regardless of user's email domain
- Backend-enforced middleware prevents frontend bypass
- Session binding to IP + User-Agent reduces session-theft risk
- Recovery-code model provides account-recovery path without relying on the identity provider
- Portable — not tied to Azure

**Consequences:**
- Greater implementation and maintenance burden than a managed MFA service
- Requires careful security testing of the custom code path (TOTP secret storage, rate limiting, session binding)
- Mitigated by small, focused surface area; extensive existing literature on TOTP security; and explicit test coverage

See [MFA-Azure-AD-Limitations.md](MFA-Azure-AD-Limitations.md) and [MFA-Implementation.md](MFA-Implementation.md).

---

### TDR-005: KR-driven Dynamic Form Generation via `KeyResultQuestion`

**Decision:** Model the mapping between Key Results and their injected quarterly-return questions as an explicit `KeyResultQuestion` join table.

**Context:** DHSC admins must be able to add, remove, or change the questions associated with a Key Result without a code deployment — the quarterly return form is dynamically assembled from the Activities and Key Results an Org has registered.

**Options Considered:**
1. Hardcoded KR-to-question logic in application code
2. JSON blob of question IDs on the `KeyResult` record
3. Explicit `KeyResultQuestion` join table

**Decision Rationale:**
- Data-driven rather than code-driven — no deployment needed for content changes
- Admins can list, reorder, add, and remove question mappings through a UI without a JSON editor
- `KeyResultVersion` + `ActivityKeyResult.kr_version_id` preserve historic fidelity when mappings change
- Referentially robust — FK constraints catch orphaned mappings

**Consequences:**
- One additional table in an already entity-rich schema
- Slight query complexity for form rendering (join `ActivityKeyRupdate architectureesult → KeyResultVersion → KeyResultQuestion → Question + Component`)
- Mitigated by a read-optimised reporting view that materialises the active form shape per Org

---

## Document History

| Version | Date | Author | Changes |
|---------|------|--------|---------|
| 2.1 | June 2026 | Architecture Team | Removed the Children's Services Benchmarking Tool from §9.x third-party risk register and §11.2 build/buy/reuse table. Statistical-neighbour data is now treated as a generic admin-loaded reference dataset. |
| 2.0 | April 2026 | Architecture Team | Realignment to the April 2026 ERD: reconciled §9.3 MFA (custom TOTP, not Conditional Access); rewrote §9.4 authorisation around the two-tier role model; renamed tenant → org throughout; pointed audit evidence at `EventLog` + `KeyResultReviewLog`; updated §10.4 KPIs to match the new collection vocabulary; bumped to .NET 10; updated §11.2 build/buy to reflect custom MFA and added KR-driven forms pattern; refreshed §14.6.2 performance scenarios; added TDR-004 (custom MFA) and TDR-005 (KR-driven forms); redrew Appendix A. |
| 1.0 | February 2026 | Architecture Team | Initial assessment (superseded). |

---
