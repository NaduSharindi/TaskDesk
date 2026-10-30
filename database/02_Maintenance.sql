/* =============================================================================
   TaskDesk - maintenance procedures (run from a SQL Agent job, not by the app)
   ============================================================================= */

/* -----------------------------------------------------------------------------
   Moves audit rows older than @RetentionDays into dbo.AuditEntriesArchive in small
   batches, so the live table stays fast without long blocking locks.
   Nothing is deleted without first being copied (audit requirement).
   ----------------------------------------------------------------------------- */
CREATE OR ALTER PROCEDURE dbo.usp_ArchiveAuditEntries
    @RetentionDays INT = 365,
    @BatchSize     INT = 5000
AS
BEGIN
    SET NOCOUNT ON;
    SET XACT_ABORT ON;

    IF OBJECT_ID(N'dbo.AuditEntriesArchive', N'U') IS NULL
    BEGIN
        CREATE TABLE dbo.AuditEntriesArchive
        (
            Id            BIGINT         NOT NULL PRIMARY KEY,
            TimestampUtc  DATETIME2(7)   NOT NULL,
            UserName      NVARCHAR(256)  NOT NULL,
            [Action]      NVARCHAR(50)   NOT NULL,
            EntityName    NVARCHAR(100)  NOT NULL,
            EntityId      NVARCHAR(100)  NOT NULL,
            Changes       NVARCHAR(MAX)  NULL,
            CorrelationId NVARCHAR(64)   NULL,
            IpAddress     NVARCHAR(45)   NULL,
            ArchivedAtUtc DATETIME2(7)   NOT NULL DEFAULT SYSUTCDATETIME()
        );
    END;

    DECLARE @Cutoff DATETIME2(7) = DATEADD(DAY, -@RetentionDays, SYSUTCDATETIME());
    DECLARE @Moved INT = 1, @Total INT = 0;

    WHILE @Moved > 0
    BEGIN
        BEGIN TRANSACTION;

        DELETE TOP (@BatchSize) a
        OUTPUT deleted.Id, deleted.TimestampUtc, deleted.UserName, deleted.[Action], deleted.EntityName,
               deleted.EntityId, deleted.Changes, deleted.CorrelationId, deleted.IpAddress
        INTO dbo.AuditEntriesArchive (Id, TimestampUtc, UserName, [Action], EntityName, EntityId, Changes, CorrelationId, IpAddress)
        FROM dbo.AuditEntries AS a
        WHERE a.TimestampUtc < @Cutoff;

        SET @Moved = @@ROWCOUNT;
        SET @Total += @Moved;

        COMMIT TRANSACTION;
    END;

    SELECT ArchivedRows = @Total;
END
GO
