/* =========================================================
   ECCO CORRESPONDENCE PDF DOCUMENTS
   Tracks the PDF rendered from each correspondence request's payload and stored in Blob Storage
   (see ProcessRequestEndPoint/Pdf). One row per request; the worker claims rows with a lease,
   so several API instances can run the worker without generating the same PDF twice.
   Re-runnable. Requires dbo.CorrespondenceRequest.
   ========================================================= */
SET ANSI_NULLS ON;
SET QUOTED_IDENTIFIER ON;
GO

IF OBJECT_ID(N'dbo.CorrespondencePdfDocument', N'U') IS NULL
BEGIN
    CREATE TABLE dbo.CorrespondencePdfDocument (
        CorrespondenceId UNIQUEIDENTIFIER NOT NULL,
        Status           VARCHAR(20)      NOT NULL,   -- Pending | Processing | Generated | Failed
        BlobName         NVARCHAR(400)    NULL,
        ContentLength    BIGINT           NULL,
        ContentSha256    VARBINARY(32)    NULL,
        Attempts         INT              NOT NULL CONSTRAINT DF_CorrespondencePdfDocument_Attempts DEFAULT 0,
        LastError        NVARCHAR(1000)   NULL,
        LeaseUntilUtc    DATETIME2        NULL,       -- set while a worker is generating it
        NextAttemptUtc   DATETIME2        NULL,       -- retry backoff after a failure
        GeneratedUtc     DATETIME2        NULL,
        CreatedUtc       DATETIME2        NOT NULL CONSTRAINT DF_CorrespondencePdfDocument_CreatedUtc DEFAULT SYSUTCDATETIME(),
        UpdatedUtc       DATETIME2        NOT NULL CONSTRAINT DF_CorrespondencePdfDocument_UpdatedUtc DEFAULT SYSUTCDATETIME(),
        CONSTRAINT PK_CorrespondencePdfDocument PRIMARY KEY (CorrespondenceId),
        -- Deleting a correspondence request removes its PDF row too (the API deletes the blob).
        CONSTRAINT FK_CorrespondencePdfDocument_CorrespondenceRequest FOREIGN KEY (CorrespondenceId)
            REFERENCES dbo.CorrespondenceRequest (CorrespondenceId) ON DELETE CASCADE,
        CONSTRAINT CK_CorrespondencePdfDocument_Status CHECK (Status IN ('Pending', 'Processing', 'Generated', 'Failed'))
    );
    CREATE INDEX IX_CorrespondencePdfDocument_Work ON dbo.CorrespondencePdfDocument (Status, NextAttemptUtc) INCLUDE (Attempts, LeaseUntilUtc);
    PRINT 'Created table: dbo.CorrespondencePdfDocument';
END
GO

/* The worker looks for recent requests that have no PDF row yet. */
IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = N'IX_CorrespondenceRequest_RequestCreationTS' AND object_id = OBJECT_ID(N'dbo.CorrespondenceRequest'))
    CREATE INDEX IX_CorrespondenceRequest_RequestCreationTS ON dbo.CorrespondenceRequest (RequestCreationTS);
GO

PRINT 'Correspondence PDF schema complete.';
GO
