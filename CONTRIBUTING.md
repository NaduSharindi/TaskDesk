# Contributing & change management

## Branching
| Branch | Purpose |
|---|---|
| `main` | Always releasable. Protected: PR + 1 review + green CI required. |
| `feature/TD-<id>-short-name` | New work, e.g. `feature/TD-42-overdue-email` |
| `hotfix/TD-<id>-short-name` | Production fixes, merged to `main` and tagged immediately |

## Commits
[Conventional Commits](https://www.conventionalcommits.org/): `feat:`, `fix:`, `docs:`, `test:`, `refactor:`, `chore:`, `db:`.
Example: `fix(tasks): prevent editing cancelled tasks (TD-57)`.

## Definition of done
1. Code + tests (`dotnet test` green locally and in CI).
2. Security considered: input validation, authorization, no secrets in code, OWASP checklist in `SECURITY.md`.
3. Docs updated (README, RUNBOOK, UAT plan) and `CHANGELOG.md` entry added.
4. PR approved.

## Release & change management
1. Merge to `main` → CI produces `taskdesk-iis-package` artifact.
2. Tag the release: `git tag v1.1.0 && git push --tags`.
3. Deploy to **UAT**; business users execute `docs/UAT-TEST-PLAN.md` and sign off.
4. Raise a **change request** (CR) with: release notes, risk, test evidence, deployment window, rollback plan.
5. After CAB approval, deploy to production with `deploy/Deploy-TaskDesk.ps1` (automatic backup + rollback).
6. Post-deployment verification: `/health/ready`, smoke test, monitor logs for 30 minutes.
