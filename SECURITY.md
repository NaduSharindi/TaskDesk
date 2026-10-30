# Security

## OWASP Top 10 (2021) – how TaskDesk addresses each risk

| Risk | Controls |
|---|---|
| **A01 Broken Access Control** | Fallback policy: every page requires sign-in unless `[AllowAnonymous]`. Role policies (Admin / Manager). Row-level rules in `TaskService.VisibleTo` (staff → own tasks, manager → department). Unauthorized records return 404 to avoid information leaks. API clients only see tasks they created. |
| **A02 Cryptographic Failures** | HTTPS + HSTS in production, secure / HttpOnly / SameSite=Strict auth cookie. Passwords hashed by ASP.NET Core Identity (PBKDF2). API keys stored only as SHA-256 hashes, compared in constant time. No secrets in source control. |
| **A03 Injection** | EF Core parameterised queries; `SqlQuery` uses interpolated (parameterised) SQL; stored procedures with typed parameters. Razor HTML-encodes all output. CSV export neutralises formula injection (`=`, `+`, `-`, `@`). |
| **A04 Insecure Design** | Workflow rules in the domain model, optimistic concurrency, idempotent API, audit by default, least-privilege DB login. |
| **A05 Security Misconfiguration** | Strict Content-Security-Policy (no inline script), X-Frame-Options DENY, nosniff, Referrer-Policy, Permissions-Policy; `Server` and `X-Powered-By` headers removed; request size limit 1 MB; friendly error pages without stack traces. |
| **A06 Vulnerable Components** | Current LTS .NET; CI runs `dotnet list package --vulnerable --include-transitive`. |
| **A07 Identification & Authentication Failures** | Password policy (≥10, complexity), lockout after 5 failures, login rate limiting per IP, identical error message for every failure, 30-minute sliding session, open-redirect protection on `returnUrl`. |
| **A08 Software & Data Integrity Failures** | CI-built release artifacts, deployment script with health check and rollback, XML parsed with DTDs disabled (no XXE). |
| **A09 Logging & Monitoring Failures** | Automatic audit trail (old → new values, user, IP, correlation ID), sign-in auditing, slow-request warnings, health endpoints. |
| **A10 SSRF** | The only outbound call goes to a configured holiday service URL; it is never taken from user input. |

CSRF: `AutoValidateAntiforgeryToken` on every MVC POST. The API uses a header API key (not cookies), so CSRF does not apply there.

## Reporting a vulnerability
Please do not open a public issue. E-mail the maintainer (see profile) with steps to reproduce.
