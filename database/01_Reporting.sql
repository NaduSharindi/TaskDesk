/* =============================================================================
   TaskDesk - SQL Server reporting objects (T-SQL)
   Deployed automatically on startup (CREATE OR ALTER = idempotent) and can also be
   run manually by a DBA through SSMS / sqlcmd as part of a change request.
   ============================================================================= */

/* -----------------------------------------------------------------------------
   Department workload as of a business date. Used by the Reports > Workload page.
   One pass over Tasks with conditional aggregation (no cursors, no per-row queries).
   ----------------------------------------------------------------------------- */
CREATE OR ALTER PROCEDURE dbo.usp_GetDepartmentWorkload
    @AsOfDate DATE
AS
BEGIN
    SET NOCOUNT ON;

    DECLARE @CompletedSince DATETIME2(7) = DATEADD(DAY, -7, CAST(@AsOfDate AS DATETIME2(7)));

    SELECT
        DepartmentCode     = d.Code,
        DepartmentName     = d.Name,
        OpenTasks          = COUNT(CASE WHEN t.[Status] IN (N'ToDo', N'InProgress', N'Blocked') THEN 1 END),
        InProgressTasks    = COUNT(CASE WHEN t.[Status] = N'InProgress' THEN 1 END),
        BlockedTasks       = COUNT(CASE WHEN t.[Status] = N'Blocked' THEN 1 END),
        OverdueTasks       = COUNT(CASE WHEN t.[Status] IN (N'ToDo', N'InProgress', N'Blocked') AND t.DueDate < @AsOfDate THEN 1 END),
        DueToday           = COUNT(CASE WHEN t.[Status] IN (N'ToDo', N'InProgress', N'Blocked') AND t.DueDate = @AsOfDate THEN 1 END),
        CompletedLast7Days = COUNT(CASE WHEN t.[Status] = N'Done' AND t.CompletedAtUtc >= @CompletedSince THEN 1 END)
    FROM dbo.Departments AS d
    LEFT JOIN dbo.Tasks AS t ON t.DepartmentId = d.Id
    GROUP BY d.Code, d.Name
    ORDER BY d.Name;
END
GO

/* -----------------------------------------------------------------------------
   Overdue tasks with owner details - for the daily overdue e-mail / escalation job.
   @DepartmentCode NULL = all departments. Parameter types match column types so
   the (DepartmentId, Status, DueDate) index can be used.
   ----------------------------------------------------------------------------- */
CREATE OR ALTER PROCEDURE dbo.usp_GetOverdueTasks
    @AsOfDate       DATE,
    @DepartmentCode NVARCHAR(10) = NULL
AS
BEGIN
    SET NOCOUNT ON;

    SELECT
        Reference     = CONCAT(N'TD-', RIGHT(CONCAT(N'00000', t.Id), 5)),
        t.Title,
        Priority      = CASE t.Priority WHEN 1 THEN N'Low' WHEN 2 THEN N'Medium' WHEN 3 THEN N'High' ELSE N'Critical' END,
        t.[Status],
        t.DueDate,
        DaysOverdue   = DATEDIFF(DAY, t.DueDate, @AsOfDate),
        Department    = d.Code,
        AssignedTo    = u.FullName,
        AssigneeEmail = u.Email
    FROM dbo.Tasks AS t
    INNER JOIN dbo.Departments AS d ON d.Id = t.DepartmentId
    LEFT JOIN dbo.AspNetUsers AS u ON u.Id = t.AssignedToId
    WHERE t.[Status] IN (N'ToDo', N'InProgress', N'Blocked')
      AND t.DueDate < @AsOfDate
      AND (@DepartmentCode IS NULL OR d.Code = @DepartmentCode)
    ORDER BY t.Priority DESC, t.DueDate;
END
GO

/* -----------------------------------------------------------------------------
   Full audit trail of one task - for internal audit / incident investigations.
   ----------------------------------------------------------------------------- */
CREATE OR ALTER PROCEDURE dbo.usp_GetTaskAuditTrail
    @TaskId INT
AS
BEGIN
    SET NOCOUNT ON;

    DECLARE @EntityId NVARCHAR(100) = CAST(@TaskId AS NVARCHAR(100));

    SELECT a.Id, a.TimestampUtc, a.UserName, a.[Action], a.Changes, a.CorrelationId, a.IpAddress
    FROM dbo.AuditEntries AS a
    WHERE a.EntityName = N'TaskItem'
      AND a.EntityId = @EntityId
    ORDER BY a.Id;
END
GO
