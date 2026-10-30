# TaskDesk – Operations Runbook

## Monitoring
| What | Where | Alert when |
|---|---|---|
| Liveness | `GET /health/live` | not 200 for 2 minutes |
| Readiness (incl. database) | `GET /health/ready` (JSON with per-check status and duration) | `Unhealthy` / not 200 |
| Slow requests | Log warning `Slow request: {Method} {Path} returned {Status} in {ms} ms` | > 20 per hour |
| Failed sign-ins | Audit log action `LoginFailed` / `LoginLockedOut` | spike from one IP |
| Holiday service | Log warning `Holiday service call failed` | continuously for 1 hour |

## Where to look
| Source | Location |
|---|---|
| Application log | Event Viewer → Windows Logs → Application (source *.NET Runtime* / *IIS AspNetCore Module V2*) |
| Startup failures | Temporarily set `stdoutLogEnabled="true"` in `web.config` → `D:\Sites\TaskDesk\logs\stdout_*.log`. **Switch off afterwards.** |
| IIS request log | `C:\inetpub\logs\LogFiles\W3SVC<id>` (status, time-taken, client IP) |
| Who did what | Admin → Audit log, or `EXEC dbo.usp_GetTaskAuditTrail @TaskId = 42` |

Every error page shows a **reference** (correlation ID). It is also in the `X-Correlation-ID` response header,
in log scopes and in `AuditEntries.CorrelationId` – search for it first.

## Common incidents
| Symptom | Likely cause | Action |
|---|---|---|
| HTTP **500.30** "ASP.NET Core app failed to start" | Bad/missing connection string, DB unreachable, invalid config | Enable stdout log, read the exception, fix `appsettings.Production.json`, recycle app pool |
| HTTP **500.19** / **500.21** | Hosting Bundle not installed or module missing | Install the ASP.NET Core 10 Hosting Bundle, `iisreset` |
| HTTP **502.5** / process crash | Wrong runtime version, app pool identity cannot read files | Check Event Viewer; verify NTFS rights for `IIS AppPool\TaskDeskPool` |
| `/health/ready` Unhealthy, `database` check failing | SQL Server down, login/permission issue, firewall | `sqlcmd -S SQL01 -E -Q "SELECT 1"` from web server; check SQL error log |
| User: "my account is locked" | 5 failed sign-ins → 15-minute lockout | Wait 15 minutes, or (with ticket) `UPDATE dbo.AspNetUsers SET LockoutEnd = NULL, AccessFailedCount = 0 WHERE NormalizedEmail = UPPER(@email)` |
| User: "task was changed by someone else" | Normal optimistic-concurrency protection | Ask the user to reload and re-apply; check task History for the other change |
| Pages slow | Missing statistics / index fragmentation, blocking | Check slow-request log entries; run index usage query in `database/03_SampleQueries.sql`; update statistics |
| No holiday warnings | Holiday SOAP service down or URL wrong | App keeps working by design. Check log warning details; contact the HR systems team (vendor) |
| Helpdesk gets 401 | API key rotated or client disabled | Verify `Integration:Clients` hash/Enabled; coordinate key rotation with the Helpdesk team |

## Routine maintenance
| Task | Frequency | How |
|---|---|---|
| Archive audit entries > 1 year | Monthly (SQL Agent) | `EXEC dbo.usp_ArchiveAuditEntries @RetentionDays = 365` |
| Index maintenance / statistics | Weekly | DBA standard maintenance plan |
| Patch Windows / IIS / .NET runtime | Monthly | Change request; patch UAT first |
| Review vulnerable NuGet packages | Every CI run | `dotnet list package --vulnerable` step |
| Rotate API keys | Every 12 months | Generate new key, add second client entry, switch caller, disable old |

## Backup & business continuity
| Item | Policy |
|---|---|
| Database | Full backup nightly, differential every 6 h, transaction log every 15 min (**RPO 15 min**) |
| Application | Release packages kept as CI artifacts (30 days) + last 5 releases on the server (`D:\Backups\TaskDesk`) |
| Configuration | `appsettings.Production.json` backed up with the server backup (contains secrets – encrypted backup) |
| Recovery target | **RTO 2 hours**: restore DB to standby SQL Server, deploy last package to standby web server, switch DNS |

### Database restore (point in time)
```sql
RESTORE DATABASE TaskDeskDb FROM DISK = N'\\backup\sql\TaskDeskDb_full.bak' WITH NORECOVERY, REPLACE;
RESTORE LOG TaskDeskDb FROM DISK = N'\\backup\sql\TaskDeskDb_log_1015.trn' WITH STOPAT = '2026-10-30T10:10:00', RECOVERY;
```
After restore: recycle the app pool, check `/health/ready`, verify the latest tasks with the business owner.

## Escalation
1. Application support (first line) – this runbook.
2. Developer on call – code defects, data fixes (always via change request and with an audit note).
3. DBA / Infrastructure / Information Security – as relevant; vendor contacts for the Holiday service and Helpdesk system.
