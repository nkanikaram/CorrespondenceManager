/* =========================================================
   SERVER-SIDE ALERTS
   PortalAlert: alerts raised by the scheduled checks (ProcessRequestEndPoint/Alerts), with
   acknowledgement and which channels were notified. PortalAlertMetric: each check's measured
   value per run, used as the baseline for "unusually high" rules.
   Re-runnable.
   ========================================================= */
SET ANSI_NULLS ON;
SET QUOTED_IDENTIFIER ON;
GO

IF OBJECT_ID(N'dbo.PortalAlert', N'U') IS NULL
BEGIN
    CREATE TABLE dbo.PortalAlert (
        AlertId          BIGINT IDENTITY(1,1) NOT NULL,
        RuleKey          VARCHAR(50)    NOT NULL,     -- FailedRate | ProcessingBacklog | PdfFailures
        Severity         VARCHAR(10)    NOT NULL,     -- Warning | Critical
        Title            NVARCHAR(200)  NOT NULL,
        Message          NVARCHAR(2000) NOT NULL,
        MetricValue      FLOAT          NULL,
        BaselineValue    FLOAT          NULL,
        RaisedUtc        DATETIME2      NOT NULL CONSTRAINT DF_PortalAlert_RaisedUtc DEFAULT SYSUTCDATETIME(),
        AcknowledgedUtc  DATETIME2      NULL,
        AcknowledgedBy   NVARCHAR(100)  NULL,
        NotifiedTeams    BIT            NOT NULL CONSTRAINT DF_PortalAlert_NotifiedTeams DEFAULT 0,
        NotifiedEmail    BIT            NOT NULL CONSTRAINT DF_PortalAlert_NotifiedEmail DEFAULT 0,
        NotifyError      NVARCHAR(1000) NULL,
        CONSTRAINT PK_PortalAlert PRIMARY KEY (AlertId),
        CONSTRAINT CK_PortalAlert_Severity CHECK (Severity IN ('Warning', 'Critical'))
    );
    CREATE INDEX IX_PortalAlert_Rule ON dbo.PortalAlert (RuleKey, RaisedUtc DESC);
    CREATE INDEX IX_PortalAlert_Open ON dbo.PortalAlert (RaisedUtc DESC) WHERE AcknowledgedUtc IS NULL;
    PRINT 'Created table: dbo.PortalAlert';
END
GO

IF OBJECT_ID(N'dbo.PortalAlertMetric', N'U') IS NULL
BEGIN
    CREATE TABLE dbo.PortalAlertMetric (
        MetricId     BIGINT IDENTITY(1,1) NOT NULL,
        RuleKey      VARCHAR(50) NOT NULL,
        CapturedUtc  DATETIME2   NOT NULL CONSTRAINT DF_PortalAlertMetric_CapturedUtc DEFAULT SYSUTCDATETIME(),
        Value        FLOAT       NOT NULL,
        SampleSize   INT         NOT NULL,
        CONSTRAINT PK_PortalAlertMetric PRIMARY KEY (MetricId)
    );
    CREATE INDEX IX_PortalAlertMetric_Rule ON dbo.PortalAlertMetric (RuleKey, CapturedUtc DESC);
    PRINT 'Created table: dbo.PortalAlertMetric';
END
GO

PRINT 'Alerts schema ready.';
GO
