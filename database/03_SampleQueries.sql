/* Ad-hoc examples for support / DBAs. NOT embedded in the application
   (only 01_Reporting.sql and 02_Maintenance.sql are deployed on startup). */

-- Workload as of today (Sri Lanka date)
DECLARE @Today DATE = CAST(DATEADD(MINUTE, 330, SYSUTCDATETIME()) AS DATE);
EXEC dbo.usp_GetDepartmentWorkload @AsOfDate = @Today;

-- Overdue tasks for IT
EXEC dbo.usp_GetOverdueTasks @AsOfDate = @Today, @DepartmentCode = N'IT';

-- Audit trail for task TD-00001
EXEC dbo.usp_GetTaskAuditTrail @TaskId = 1;

-- Who changed what in the last 24 hours
SELECT TOP (100) TimestampUtc, UserName, [Action], EntityName, EntityId, Changes
FROM dbo.AuditEntries
WHERE TimestampUtc >= DATEADD(HOUR, -24, SYSUTCDATETIME())
ORDER BY Id DESC;

-- Index usage check (performance tuning)
SELECT OBJECT_NAME(s.object_id) AS TableName, i.name AS IndexName, s.user_seeks, s.user_scans, s.user_lookups, s.user_updates
FROM sys.dm_db_index_usage_stats AS s
JOIN sys.indexes AS i ON i.object_id = s.object_id AND i.index_id = s.index_id
WHERE s.database_id = DB_ID() AND OBJECT_NAME(s.object_id) IN (N'Tasks', N'AuditEntries')
ORDER BY s.user_seeks + s.user_scans DESC;
