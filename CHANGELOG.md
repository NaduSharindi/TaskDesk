# Changelog

All notable changes to this project are documented here.
Format: [Keep a Changelog](https://keepachangelog.com/en/1.1.0/) · Versioning: [SemVer](https://semver.org/).

## [1.0.0] - 2026-10-30

### Added
- Office task management: create, edit, assign, comment, status workflow (To do → In progress → Blocked → Done / Cancelled).
- Role-based access: Admin, Manager (own department), Staff (own tasks).
- Dashboard with overdue / due-today counters; department workload report (SQL Server stored procedure).
- CSV export with formula-injection protection.
- Automatic audit trail of every change (old → new values) plus sign-in events; admin audit viewer.
- REST API for system integration (X-Api-Key), JSON and XML, idempotent task creation.
- SOAP integration with the corporate holiday calendar service (with caching and graceful degradation).
- Health endpoints (`/health/live`, `/health/ready`), correlation IDs, slow-request logging.
- IIS deployment: `web.config`, PowerShell deployment script with backup and automatic rollback.
- CI pipeline: build, tests, vulnerable package scan, deployable IIS package artifact.
