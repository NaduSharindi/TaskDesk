# TaskDesk – Requirements

| | |
|---|---|
| **Business owner** | Head of Branch Operations (sample) |
| **Users** | ~150 office staff across IT, Finance, HR and Operations |
| **Problem** | Work is tracked in e-mails and spreadsheets: tasks are lost, nobody sees what is overdue, and there is no record of who changed what. |
| **Goal** | One internal web application to create, assign and follow up office tasks, with management visibility and an audit trail. |

## Roles
| Role | Can do |
|---|---|
| **Staff** | Create tasks for themselves, update tasks they created or are assigned to, comment |
| **Manager** | Everything Staff can, plus see and manage all tasks of their department, assign to team members, reports, CSV export |
| **Admin** | All tasks in all departments, reports, audit log |
| **Integration client** | An external system (e.g. IT Helpdesk) creating tasks through the API |

## User stories & acceptance criteria

**US-01 Create a task** – *As a staff member I want to record a task with a due date so that I don't forget it.*
- Title is required (3–150 chars); description optional (≤ 2000).
- Priority: Low / Medium / High / Critical. Due date cannot be in the past.
- If the due date is a weekend or public holiday, the user is warned but may continue.
- Staff can only assign to themselves; managers only within their department.

**US-02 Work a task** – *As an assignee I want to move a task through its workflow.*
- Allowed transitions: To do → In progress / Blocked / Done / Cancelled; In progress → To do / Blocked / Done / Cancelled; Blocked → In progress / Cancelled; Done → In progress (reopen); Cancelled → To do (reopen).
- Completing a task records the completion time. Closed tasks are read-only until reopened.

**US-03 Find my work** – *As a user I want a list of my tasks, overdue first.*
- Filters: assigned to me / created by me / all visible, status, priority, overdue only, search by text or reference (TD-00012).
- Sort: open before closed, earliest due date first, then highest priority.

**US-04 Dashboard** – counts for assigned-to-me, overdue, due today, due in 7 days; my next five tasks.

**US-05 Team workload report** – *As a manager I want to see open / overdue / blocked work per department* and export tasks to CSV for Excel.

**US-06 Audit trail** – *As internal audit I need to know who changed what and when.*
- Every create / update of tasks, comments and departments stores user, time, IP, correlation ID and old → new values.
- Sign-in success, failure, lockout and sign-out are recorded. Audit rows are never edited by the application.

**US-07 Helpdesk integration** – *As the IT Helpdesk system I want to raise tasks automatically from tickets.*
- REST API, API-key authentication, JSON or XML.
- Re-sending the same ticket reference must not create a duplicate task.
- The client can read status updates of its own tasks only.

**US-08 Holiday calendar** – due-date warnings use the corporate holiday calendar SOAP service; if the service is down the application keeps working without warnings.

## Non-functional requirements
| Area | Requirement |
|---|---|
| Security | Company e-mail sign-in, password policy (≥10 chars, complexity), lockout after 5 failures for 15 minutes, HTTPS only, OWASP Top 10 addressed (see `SECURITY.md`) |
| Availability | Business hours 08:00–18:00 Sri Lanka time; planned maintenance outside these hours |
| Performance | Page responses < 1 s for 95 % of requests; slow requests logged |
| Audit | Audit data retained ≥ 1 year online, then archived (not deleted) |
| Platform | Windows Server 2022, IIS 10, SQL Server 2019+; Chrome / Edge |
| Operability | Health endpoints for monitoring, correlation ID on every error page and log line |
| Recovery | RPO 15 minutes, RTO 2 hours (see `RUNBOOK.md`) |

## Out of scope (v1)
E-mail notifications, file attachments, Active Directory single sign-on, mobile app.
