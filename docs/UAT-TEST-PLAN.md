# TaskDesk – User Acceptance Test Plan (v1.0.0)

**Environment:** UAT (`https://taskdesk-uat.office.local`) · **Data:** seeded demo users (password from the UAT release note)
**Entry criteria:** CI green, deployed with `Deploy-TaskDesk.ps1`, `/health/ready` = Healthy.
**Exit criteria:** all *High* cases passed; no open Severity 1/2 defects; business owner sign-off.

| ID | Req | Steps | Expected result | Priority | Result | Tester |
|---|---|---|---|---|---|---|
| UAT-01 | US-01 | Sign in as `staff.it`. New task: title, High, due in 3 days. | Task TD-xxxxx created, assigned to me, appears on dashboard | High | | |
| UAT-02 | US-01 | Create a task with due date yesterday | Error "Due date cannot be in the past" | High | | |
| UAT-03 | US-01/08 | Create a task due on 25 Dec | Task created + warning that the date is a public holiday | Medium | | |
| UAT-04 | US-01 | As `staff.it`, open "Assign to" list | Only my own name is listed | High | | |
| UAT-05 | US-01 | As `manager.it`, assign a task to `staff.it` | Saved; staff sees it under "Assigned to me" | High | | |
| UAT-06 | US-02 | Move a task To do → In progress → Done | Status badges update; completion time shown; Edit button disappears | High | | |
| UAT-07 | US-02 | Reopen the Done task | Status In progress, editable again | Medium | | |
| UAT-08 | US-02 | Two browsers edit the same task, save both | Second save shows "changed by someone else"; no data lost | High | | |
| UAT-09 | US-03 | Tasks → "Overdue only" | Only open tasks with past due dates, highlighted red | High | | |
| UAT-10 | US-03 | Search "TD-00001" | Exactly that task | Medium | | |
| UAT-11 | Security | As `staff.fin`, open an IT task URL directly | "Not found" page (no data shown) | High | | |
| UAT-12 | US-05 | As `manager.it` open Reports → Workload | Counts per department match the task list | High | | |
| UAT-13 | US-05 | Export CSV and open in Excel | All visible tasks, correct Sinhala/Tamil characters, no formulas executed | Medium | | |
| UAT-14 | US-05 | As `staff.it` open `/Reports/Workload` | Access denied page | High | | |
| UAT-15 | US-06 | Change a task's priority, then open its History | Entry shows user, time and `Priority: Medium → High` | High | | |
| UAT-16 | US-06 | As `admin` open Audit log, filter LoginFailed | Failed sign-ins listed with IP and time | High | | |
| UAT-17 | Security | Enter wrong password 5 times | Account locked 15 minutes; message does not reveal which part was wrong | High | | |
| UAT-18 | US-07 | Helpdesk test harness: POST same ticket twice | First 201, second 200 with the same task ID | High | | |
| UAT-19 | US-07 | POST with Accept/Content-Type `application/xml` | XML request accepted, XML response returned | Medium | | |
| UAT-20 | US-08 | Stop the holiday service, create a task | Task saved normally; no holiday warning; warning in logs | Medium | | |
| UAT-21 | Ops | Trigger an error page | Friendly page with reference; same reference found in the server log | Medium | | |

## Defect severity
| Sev | Definition | Fix before go-live? |
|---|---|---|
| 1 | Data loss, security breach, app unusable | Yes |
| 2 | Major function broken, no workaround | Yes |
| 3 | Function broken, workaround exists | Agreed case by case |
| 4 | Cosmetic | No |

## Sign-off
| Role | Name | Decision | Date | Signature |
|---|---|---|---|---|
| Business owner | | Approved / Rejected | | |
| IT Application Support | | | | |
| Information Security | | | | |
