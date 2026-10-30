# TaskDesk – Office To-Do Manager

An internal web application for an office to create, assign and track tasks, built with
**ASP.NET Core MVC, Web API, Entity Framework Core and SQL Server**, deployed on **IIS / Windows Server**.
It is deliberately small in features but complete in the things a production system in a regulated
organisation needs: role-based security, audit trail, system integration (REST/JSON/XML and SOAP),
stored procedures, monitoring, documentation, CI/CD and an IIS deployment with rollback.

> Portfolio project 2 of 5.

---

## Features
- **Tasks:** create, edit, assign, comment, priorities, due dates, workflow
  *To do → In progress → Blocked → Done / Cancelled* with reopen.
- **Roles:** *Staff* (own tasks), *Manager* (whole department, reports), *Admin* (everything, audit log).
- **Dashboard:** assigned to me, overdue, due today, due within 7 days.
- **Reports:** department workload (stored procedure), CSV export for Excel.
- **Audit trail:** every change is recorded automatically with old → new values, user, IP and reference ID;
  history shown on each task, full log for admins.
- **Integration API:** the IT Helpdesk can raise tasks automatically – API key, JSON *or* XML, idempotent.
- **SOAP integration:** due dates on public holidays are flagged using a corporate holiday SOAP service.
- **Operations:** health checks, correlation IDs, slow-request logging, friendly error pages.

## How the job requirements are covered

| Requirement | Where to look in this repo |
|---|---|
| C#, .NET, ASP.NET Core MVC, Web API | `src/TaskDesk.Web` – MVC controllers + Razor views, `Controllers/Api/TasksApiController.cs` |
| SQL Server, T-SQL, Entity Framework | `Data/AppDbContext.cs`, `Data/Configurations.cs` (Fluent API, indexes, concurrency); `database/*.sql` stored procedures; `ReportService` calls `usp_GetDepartmentWorkload` |
| Develop & optimise DB objects, queries, SPs | Set-based SPs with conditional aggregation, sargable parameters, batched archiving (`usp_ArchiveAuditEntries`), index usage query |
| REST / SOAP, JSON / XML, system integration | REST API with JSON + XML content negotiation; SOAP 1.1 client `Integration/HolidayCalendar.cs` (+ mock SOAP service); idempotent integration design |
| Git & CI/CD | `.github/workflows/ci.yml` (build, test, vulnerability scan, IIS package artifact); `CONTRIBUTING.md` branching & commit rules; PR template; `CHANGELOG.md` |
| IIS, Windows Server, troubleshooting | `web.config`, `deploy/Deploy-TaskDesk.ps1` (app pool, backup, app_offline, health check, rollback), `docs/DEPLOYMENT-IIS.md`, `docs/RUNBOOK.md` (500.30/500.19/502.5 …) |
| Analyse & resolve incidents | Correlation ID on every error page/log/audit row, health endpoint JSON, slow-request warnings, runbook |
| Requirements, design, testing, UAT, deployment | `docs/REQUIREMENTS.md`, `docs/TECHNICAL-DESIGN.md`, `tests/`, `docs/UAT-TEST-PLAN.md`, `docs/DEPLOYMENT-IIS.md` |
| Secure coding, OWASP | `SECURITY.md` (Top 10 mapping): Identity + lockout, CSRF tokens, strict CSP, XXE-safe XML, CSV-injection guard, hashed API keys, open-redirect protection |
| Change management, version control, documentation | `CONTRIBUTING.md` release/CR process, PR checklist, changelog, SemVer |
| Performance monitoring, audit, business continuity | `/health/*`, `RequestTimingMiddleware`, automatic audit, backup/restore + RPO/RTO in runbook, graceful degradation when the SOAP service is down |
| Work with business users, security teams, vendors | UAT plan with sign-off table, escalation section in runbook, API key rotation procedure with the Helpdesk vendor |

## Tech stack
.NET 10 · ASP.NET Core MVC + Web API · Razor · ASP.NET Core Identity · EF Core 10 · SQL Server (SQLite for tests) ·
xUnit + WebApplicationFactory · GitHub Actions · IIS 10 / Windows Server 2022

## Getting started

### Run locally (Windows, SQL Server LocalDB)
```bash
git clone https://github.com/YOUR-GITHUB-USERNAME/TaskDesk.git
cd TaskDesk
dotnet run --project src/TaskDesk.Web --launch-profile http
```
Open **http://localhost:5090**. The database `TaskDeskDb`, stored procedures and demo data are created automatically.

### Run without SQL Server (any OS)
```bash
dotnet run --project src/TaskDesk.Web --launch-profile http -- \
  --Database:Provider=Sqlite --ConnectionStrings:TaskDesk="Data Source=taskdesk.db"
```
(The workload report then uses the LINQ fallback instead of the stored procedure.)

### Demo users
Password for all: **`Demo@Pass123!`** (development only)

| E-mail | Role | Department |
|---|---|---|
| `admin@taskdesk.local` | Admin | IT |
| `manager.it@taskdesk.local` | Manager | IT |
| `staff.it@taskdesk.local` | Staff | IT |
| `manager.fin@taskdesk.local` | Manager | Finance |
| `staff.fin@taskdesk.local` | Staff | Finance |

### Try the API
Development API key: `dev-helpdesk-key-0001-change-me` (only its SHA-256 hash is in config).
Use `src/TaskDesk.Web/TaskDesk.Web.http`, or:
```bash
curl -X POST http://localhost:5090/api/v1/tasks \
  -H "X-Api-Key: dev-helpdesk-key-0001-change-me" -H "Content-Type: application/json" \
  -d '{"externalReference":"HD-1001","title":"Replace keyboard","departmentCode":"IT","priority":"Low"}'

curl http://localhost:5090/api/v1/tasks -H "X-Api-Key: dev-helpdesk-key-0001-change-me" -H "Accept: application/xml"
```

| Method | Endpoint | Description |
|---|---|---|
| GET | `/api/v1/tasks?status=&updatedSinceUtc=&page=&pageSize=` | Tasks created by this client |
| GET | `/api/v1/tasks/{id}` | One task |
| GET | `/api/v1/tasks/by-reference/{externalReference}` | Look up by the caller's own ID |
| POST | `/api/v1/tasks` | Create (201) – or return the existing task (200) for a repeated `externalReference` |
| POST | `/api/v1/tasks/{id}/comments` | Add a comment |
| GET | `/health/live`, `/health/ready` | Monitoring (anonymous) |

## Tests
```bash
dotnet test
```
| Suite | Covers |
|---|---|
| `Domain` | Workflow transitions, read-only closed tasks, overdue logic, CSV injection, XXE rejection |
| `Services` (real SQLite DB) | Visibility per role, assignment rules, optimistic concurrency, audit old/new values, holiday warnings, API idempotency, SOAP client (envelope, parsing, caching, faults, outages) |
| `Integration` (full app in memory) | Login with anti-forgery, generic login errors, CSRF rejection, role-based access to reports, form submission, security headers, health checks, API key auth, JSON + XML API, validation 400 vs business 422, mock SOAP service |

## Project structure
```
TaskDesk/
├── src/TaskDesk.Web/
│   ├── Domain/          entities & business rules
│   ├── Data/            EF Core context (automatic audit), mappings, initializer
│   ├── Services/        use cases + authorization rules
│   ├── Integration/     SOAP holiday client, API use cases, mock SOAP service
│   ├── Security/        API-key auth, claims, policies
│   ├── Infrastructure/  middleware, error handling, health output
│   ├── Controllers/     MVC + Api/
│   ├── Views/ wwwroot/  Razor UI, plain CSS (no inline JS → strict CSP)
│   └── web.config       IIS hosting
├── database/            T-SQL stored procedures & maintenance scripts
├── deploy/              Deploy-TaskDesk.ps1 (IIS, backup, rollback)
├── docs/                requirements, design, UAT plan, IIS deployment, runbook
├── tests/TaskDesk.Tests unit, SQLite-backed and full integration tests
└── .github/             CI workflow, PR template
```

## License
MIT – see [LICENSE](LICENSE). Demo data is fictitious.
