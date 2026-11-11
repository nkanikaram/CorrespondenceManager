/* =========================================================
   CORRESPONDENCE RECYCLE BIN
   Deleting a record in the portal moves it here instead of destroying it: the CorrespondenceRequest
   row and its status history are kept as JSON snapshots (every column, so later schema additions
   are captured too), with its PDF reference. Restore puts them back exactly; the retention job
   purges entries after their PurgeAfterUtc. Other systems see a recycled record as gone, the same
   as a deletion. Re-runnable. Requires dbo.CorrespondenceRequest (and Pdf/001 for PDFs).
   ========================================================= */
SET ANSI_NULLS ON;
SET QUOTED_IDENTIFIER ON;
GO

IF OBJECT_ID(N'dbo.CorrespondenceRecycleBin', N'U') IS NULL
BEGIN
    CREATE TABLE dbo.CorrespondenceRecycleBin (
        CorrespondenceId     UNIQUEIDENTIFIER NOT NULL,
        DocId                VARCHAR(50)      NULL,
        ClaimNumber          VARCHAR(50)      NULL,
        RequestingDomainName VARCHAR(100)     NULL,
        DeliveryMethodCode   VARCHAR(50)      NULL,
        RequestCreationTS    DATETIME         NULL,
        LatestStatus         VARCHAR(255)     NULL,
        DeletedUtc           DATETIME2        NOT NULL CONSTRAINT DF_CorrespondenceRecycleBin_DeletedUtc DEFAULT SYSUTCDATETIME(),
        DeletedByUserId      UNIQUEIDENTIFIER NULL,
        DeletedByUserName    NVARCHAR(100)    NULL,
        DeleteReason         NVARCHAR(200)    NULL,       -- 'Deleted in portal' | 'Retention period ended'
        PurgeAfterUtc        DATETIME2        NOT NULL,
        PdfBlobName          NVARCHAR(400)    NULL,
        RequestJson          NVARCHAR(MAX)    NOT NULL,   -- the CorrespondenceRequest row (FOR JSON; varbinary as base64)
        HistoryJson          NVARCHAR(MAX)    NULL,       -- its CorrespondenceRequestStatusHistory rows
        PdfJson              NVARCHAR(MAX)    NULL,       -- its CorrespondencePdfDocument row
        CONSTRAINT PK_CorrespondenceRecycleBin PRIMARY KEY (CorrespondenceId)
    );
    CREATE INDEX IX_CorrespondenceRecycleBin_PurgeAfterUtc ON dbo.CorrespondenceRecycleBin (PurgeAfterUtc);
    PRINT 'Created table: dbo.CorrespondenceRecycleBin';
END
GO

/* ---------------------------------------------------------
   usp_RecycleCorrespondence: moves the given records (JSON array of GUID strings) to the bin.
   Returns the ids that were moved.
   --------------------------------------------------------- */
CREATE OR ALTER PROCEDURE dbo.usp_RecycleCorrespondence
    @IdsJson NVARCHAR(MAX),
    @DeletedByUserId UNIQUEIDENTIFIER = NULL,
    @DeletedByUserName NVARCHAR(100) = NULL,
    @Reason NVARCHAR(200) = NULL,
    @RetentionDays INT = 30
AS
BEGIN
    SET NOCOUNT ON;
    SET XACT_ABORT ON;

    DECLARE @ids TABLE (CorrespondenceId UNIQUEIDENTIFIER PRIMARY KEY);
    INSERT INTO @ids SELECT DISTINCT TRY_CONVERT(UNIQUEIDENTIFIER, value) FROM OPENJSON(@IdsJson) WHERE TRY_CONVERT(UNIQUEIDENTIFIER, value) IS NOT NULL;

    DECLARE @hasPdf BIT = CASE WHEN OBJECT_ID('dbo.CorrespondencePdfDocument') IS NULL THEN 0 ELSE 1 END;

    BEGIN TRANSACTION;

    INSERT INTO dbo.CorrespondenceRecycleBin
        (CorrespondenceId, DocId, ClaimNumber, RequestingDomainName, DeliveryMethodCode, RequestCreationTS, LatestStatus,
         DeletedByUserId, DeletedByUserName, DeleteReason, PurgeAfterUtc, RequestJson, HistoryJson)
    SELECT cr.CorrespondenceId, cr.DocId, cr.ClaimNumber, cr.RequestingDomainName, cr.DeliveryMethodCode, cr.RequestCreationTS,
           (SELECT TOP (1) COALESCE(sc.StatusDescription, h.StatusCode)
            FROM dbo.CorrespondenceRequestStatusHistory h
            LEFT JOIN dbo.CorrespondenceStatusCode sc ON sc.StatusCode = h.StatusCode
            WHERE h.CorrespondenceId = cr.CorrespondenceId ORDER BY h.HistorySequenceNum DESC, h.StatusUpdateTS DESC),
           @DeletedByUserId, @DeletedByUserName, @Reason, DATEADD(DAY, @RetentionDays, SYSUTCDATETIME()),
           (SELECT r.* FROM dbo.CorrespondenceRequest r WHERE r.CorrespondenceId = cr.CorrespondenceId FOR JSON PATH, WITHOUT_ARRAY_WRAPPER, INCLUDE_NULL_VALUES),
           (SELECT h.* FROM dbo.CorrespondenceRequestStatusHistory h WHERE h.CorrespondenceId = cr.CorrespondenceId FOR JSON PATH, INCLUDE_NULL_VALUES)
    FROM dbo.CorrespondenceRequest cr
    JOIN @ids i ON i.CorrespondenceId = cr.CorrespondenceId
    WHERE NOT EXISTS (SELECT 1 FROM dbo.CorrespondenceRecycleBin b WHERE b.CorrespondenceId = cr.CorrespondenceId);

    -- Keep the PDF reference (the row cascades away with the record; the blob stays until purge).
    -- Deferred name resolution: this only runs when the PDF table exists.
    IF @hasPdf = 1
        UPDATE b SET PdfBlobName = p.BlobName,
                     PdfJson = (SELECT p2.* FROM dbo.CorrespondencePdfDocument p2 WHERE p2.CorrespondenceId = p.CorrespondenceId
                                FOR JSON PATH, WITHOUT_ARRAY_WRAPPER, INCLUDE_NULL_VALUES)
        FROM dbo.CorrespondenceRecycleBin b
        JOIN dbo.CorrespondencePdfDocument p ON p.CorrespondenceId = b.CorrespondenceId
        JOIN @ids i ON i.CorrespondenceId = b.CorrespondenceId;

    DELETE h FROM dbo.CorrespondenceRequestStatusHistory h JOIN @ids i ON i.CorrespondenceId = h.CorrespondenceId;
    DELETE cr OUTPUT deleted.CorrespondenceId
    FROM dbo.CorrespondenceRequest cr JOIN @ids i ON i.CorrespondenceId = cr.CorrespondenceId;  -- PDF row cascades

    COMMIT TRANSACTION;
END
GO

/* ---------------------------------------------------------
   fn_JsonColumnList: "col type '$.\"col\"', ..." for OPENJSON ... WITH, from the table's current
   columns, so restore keeps working when columns are added later.
   --------------------------------------------------------- */
CREATE OR ALTER FUNCTION dbo.fn_RecycleBinColumns (@Table SYSNAME, @ForWith BIT)
RETURNS NVARCHAR(MAX)
AS
BEGIN
    RETURN (
        SELECT STRING_AGG(CAST(
            CASE WHEN @ForWith = 0 THEN QUOTENAME(c.name)
            ELSE QUOTENAME(c.name) + N' ' +
                CASE
                    WHEN t.name IN ('varchar', 'char', 'varbinary', 'binary')
                        THEN t.name + '(' + IIF(c.max_length = -1, 'max', CAST(c.max_length AS VARCHAR(10))) + ')'
                    WHEN t.name IN ('nvarchar', 'nchar')
                        THEN t.name + '(' + IIF(c.max_length = -1, 'max', CAST(c.max_length / 2 AS VARCHAR(10))) + ')'
                    WHEN t.name IN ('decimal', 'numeric')
                        THEN t.name + '(' + CAST(c.precision AS VARCHAR(10)) + ',' + CAST(c.scale AS VARCHAR(10)) + ')'
                    WHEN t.name IN ('datetime2', 'time', 'datetimeoffset')
                        THEN t.name + '(' + CAST(c.scale AS VARCHAR(10)) + ')'
                    ELSE t.name
                END + N' ''$."' + REPLACE(c.name, '"', '\"') + N'"'''
            END AS NVARCHAR(MAX)), N', ') WITHIN GROUP (ORDER BY c.column_id)
        FROM sys.columns c
        JOIN sys.types t ON t.user_type_id = c.user_type_id
        WHERE c.object_id = OBJECT_ID(@Table) AND c.is_computed = 0 AND t.name NOT IN ('timestamp', 'rowversion')
    );
END
GO

/* ---------------------------------------------------------
   usp_RestoreCorrespondence: puts a recycled record, its history and its PDF row back.
   --------------------------------------------------------- */
CREATE OR ALTER PROCEDURE dbo.usp_RestoreCorrespondence
    @CorrespondenceId UNIQUEIDENTIFIER
AS
BEGIN
    SET NOCOUNT ON;
    SET XACT_ABORT ON;

    DECLARE @request NVARCHAR(MAX), @history NVARCHAR(MAX), @pdf NVARCHAR(MAX);
    SELECT @request = RequestJson, @history = HistoryJson, @pdf = PdfJson
    FROM dbo.CorrespondenceRecycleBin WHERE CorrespondenceId = @CorrespondenceId;

    IF @request IS NULL THROW 50001, 'That record is not in the recycle bin.', 1;
    IF EXISTS (SELECT 1 FROM dbo.CorrespondenceRequest WHERE CorrespondenceId = @CorrespondenceId)
        THROW 50002, 'A record with this id already exists.', 1;

    DECLARE @sql NVARCHAR(MAX);
    BEGIN TRANSACTION;

    -- Foreign keys added WITH NOCHECK ("untrusted") were never checked against the existing rows, and existing rows
    -- may break them (e.g. DeliveryMethodCode 'MAIL' when the lookup holds '01'-'04'). Putting such a row back is
    -- an INSERT, which those keys would refuse. So, for this transaction only, switch off the keys that are already
    -- untrusted on the two tables, then switch them back on (still untrusted, exactly as before). Trusted keys stay
    -- enforced. Needs ALTER permission on the two tables; on any error XACT_ABORT rolls this back too.
    DECLARE @relax NVARCHAR(MAX), @recheck NVARCHAR(MAX);
    SELECT @relax = STRING_AGG(CAST(N'ALTER TABLE ' + QUOTENAME(OBJECT_SCHEMA_NAME(parent_object_id)) + N'.' + QUOTENAME(OBJECT_NAME(parent_object_id))
                        + N' NOCHECK CONSTRAINT ' + QUOTENAME(name) AS NVARCHAR(MAX)), N'; '),
           @recheck = STRING_AGG(CAST(N'ALTER TABLE ' + QUOTENAME(OBJECT_SCHEMA_NAME(parent_object_id)) + N'.' + QUOTENAME(OBJECT_NAME(parent_object_id))
                        + N' CHECK CONSTRAINT ' + QUOTENAME(name) AS NVARCHAR(MAX)), N'; ')
    FROM sys.foreign_keys
    WHERE is_not_trusted = 1 AND is_disabled = 0
      AND parent_object_id IN (OBJECT_ID('dbo.CorrespondenceRequest'), OBJECT_ID('dbo.CorrespondenceRequestStatusHistory'));
    IF @relax IS NOT NULL EXEC sp_executesql @relax;

    SET @sql = N'INSERT INTO dbo.CorrespondenceRequest (' + dbo.fn_RecycleBinColumns('dbo.CorrespondenceRequest', 0) + N') SELECT '
             + dbo.fn_RecycleBinColumns('dbo.CorrespondenceRequest', 0) + N' FROM OPENJSON(@j) WITH ('
             + dbo.fn_RecycleBinColumns('dbo.CorrespondenceRequest', 1) + N')';
    IF OBJECTPROPERTY(OBJECT_ID('dbo.CorrespondenceRequest'), 'TableHasIdentity') = 1
        SET @sql = N'SET IDENTITY_INSERT dbo.CorrespondenceRequest ON; ' + @sql + N'; SET IDENTITY_INSERT dbo.CorrespondenceRequest OFF;';
    EXEC sp_executesql @sql, N'@j NVARCHAR(MAX)', @j = @request;

    IF @history IS NOT NULL
    BEGIN
        SET @sql = N'INSERT INTO dbo.CorrespondenceRequestStatusHistory (' + dbo.fn_RecycleBinColumns('dbo.CorrespondenceRequestStatusHistory', 0)
                 + N') SELECT ' + dbo.fn_RecycleBinColumns('dbo.CorrespondenceRequestStatusHistory', 0) + N' FROM OPENJSON(@j) WITH ('
                 + dbo.fn_RecycleBinColumns('dbo.CorrespondenceRequestStatusHistory', 1) + N')';
        IF OBJECTPROPERTY(OBJECT_ID('dbo.CorrespondenceRequestStatusHistory'), 'TableHasIdentity') = 1
        SET @sql = N'SET IDENTITY_INSERT dbo.CorrespondenceRequestStatusHistory ON; ' + @sql + N'; SET IDENTITY_INSERT dbo.CorrespondenceRequestStatusHistory OFF;';
    EXEC sp_executesql @sql, N'@j NVARCHAR(MAX)', @j = @history;
    END

    IF @pdf IS NOT NULL AND OBJECT_ID('dbo.CorrespondencePdfDocument') IS NOT NULL
    BEGIN
        SET @sql = N'INSERT INTO dbo.CorrespondencePdfDocument (' + dbo.fn_RecycleBinColumns('dbo.CorrespondencePdfDocument', 0)
                 + N') SELECT ' + dbo.fn_RecycleBinColumns('dbo.CorrespondencePdfDocument', 0) + N' FROM OPENJSON(@j) WITH ('
                 + dbo.fn_RecycleBinColumns('dbo.CorrespondencePdfDocument', 1) + N')';
        IF OBJECTPROPERTY(OBJECT_ID('dbo.CorrespondencePdfDocument'), 'TableHasIdentity') = 1
        SET @sql = N'SET IDENTITY_INSERT dbo.CorrespondencePdfDocument ON; ' + @sql + N'; SET IDENTITY_INSERT dbo.CorrespondencePdfDocument OFF;';
    EXEC sp_executesql @sql, N'@j NVARCHAR(MAX)', @j = @pdf;
    END

    IF @recheck IS NOT NULL EXEC sp_executesql @recheck;

    DELETE FROM dbo.CorrespondenceRecycleBin WHERE CorrespondenceId = @CorrespondenceId;
    COMMIT TRANSACTION;
END
GO

PRINT 'Correspondence recycle bin ready.';
GO
