/* =========================================================
   SERVICE LEVEL AGREEMENT (SLA) SETTINGS
   Run after Auth/001-005 and Portal/001-003. Re-runnable.

   SlaSetting (one row): how the SLA clock works.
     - BusinessDays: count Monday-Friday only (1) or every day (0).
     - AtRiskPercent: a request is "at risk" once it has used this much of its SLA.
     - StopStatuses: statuses (as shown in the portal) that finish a request and stop its clock.
       Failed / Error don't stop it: those requests still need action.
   SlaTarget: the SLA in days. The row with CustomerName NULL is the default; other rows override it
   for one customer (requesting domain).
   Edited in the portal under Administration > SLA (permission sla.manage).
   ========================================================= */
SET ANSI_NULLS ON;
SET QUOTED_IDENTIFIER ON;
GO

IF OBJECT_ID(N'dbo.SlaSetting', N'U') IS NULL
BEGIN
    CREATE TABLE dbo.SlaSetting (
        SlaSettingId   INT            NOT NULL CONSTRAINT DF_SlaSetting_Id DEFAULT 1,
        BusinessDays   BIT            NOT NULL CONSTRAINT DF_SlaSetting_BusinessDays DEFAULT 1,
        AtRiskPercent  INT            NOT NULL CONSTRAINT DF_SlaSetting_AtRiskPercent DEFAULT 80,
        StopStatuses   NVARCHAR(1000) NOT NULL,
        UpdatedUtc     DATETIME2      NOT NULL CONSTRAINT DF_SlaSetting_UpdatedUtc DEFAULT SYSUTCDATETIME(),
        UpdatedBy      NVARCHAR(100)  NULL,
        CONSTRAINT PK_SlaSetting PRIMARY KEY (SlaSettingId),
        CONSTRAINT CK_SlaSetting_Single CHECK (SlaSettingId = 1),
        CONSTRAINT CK_SlaSetting_AtRisk CHECK (AtRiskPercent BETWEEN 1 AND 99)
    );
    INSERT INTO dbo.SlaSetting (StopStatuses) VALUES (N'Delivered,EDeliveryConfirmed,ImagingConfirmed,Archived,Completed');
    PRINT 'Created table: dbo.SlaSetting';
END
GO

IF OBJECT_ID(N'dbo.SlaTarget', N'U') IS NULL
BEGIN
    CREATE TABLE dbo.SlaTarget (
        SlaTargetId    INT IDENTITY(1,1) NOT NULL,
        CustomerName   NVARCHAR(200)  NULL,          -- NULL = the default for every customer
        TargetDays     DECIMAL(6,2)   NOT NULL,
        UpdatedUtc     DATETIME2      NOT NULL CONSTRAINT DF_SlaTarget_UpdatedUtc DEFAULT SYSUTCDATETIME(),
        UpdatedBy      NVARCHAR(100)  NULL,
        CONSTRAINT PK_SlaTarget PRIMARY KEY (SlaTargetId),
        CONSTRAINT CK_SlaTarget_Days CHECK (TargetDays > 0 AND TargetDays <= 365)
    );
    CREATE UNIQUE INDEX UX_SlaTarget_Customer ON dbo.SlaTarget (CustomerName);  -- one row per customer, and (NULLs count as equal) one default
    INSERT INTO dbo.SlaTarget (CustomerName, TargetDays) VALUES (NULL, 5);
    PRINT 'Created table: dbo.SlaTarget (default SLA: 5 days)';
END
GO

/* Permission to change the SLA settings; Administrator gets it. */
IF NOT EXISTS (SELECT 1 FROM auth.Permission WHERE PermissionKey = 'sla.manage')
    INSERT INTO auth.Permission (PermissionKey, Category, Description)
    VALUES ('sla.manage', N'Administration', N'Change the SLA targets and how the SLA clock is counted');

INSERT INTO auth.RolePermission (RoleId, PermissionId)
SELECT r.RoleId, p.PermissionId
FROM auth.Role r JOIN auth.Permission p ON p.PermissionKey = 'sla.manage'
WHERE r.RoleName = N'Administrator'
  AND NOT EXISTS (SELECT 1 FROM auth.RolePermission x WHERE x.RoleId = r.RoleId AND x.PermissionId = p.PermissionId);
GO
