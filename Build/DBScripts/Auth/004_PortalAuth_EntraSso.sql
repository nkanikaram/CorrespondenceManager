/* =========================================================
   ECCO PORTAL AUTH - MICROSOFT ENTRA ID SINGLE SIGN-ON
   Run after 001-003. Re-runnable.

   - AppUser gains an external identity (Entra object id); passwords become optional
     (SSO users have none).
   - UserRole / UserAttribute rows get a Source: 'Manual' (set by an administrator in the portal)
     or 'Group' (synced from Entra groups / app roles at every sign-in). Effective access is the union.
   - ExternalGroupMapping maps an Entra group object id or app role value to a portal role and/or
     an ABAC attribute value.
   - AuthAuditLog gains ResourceId so record-level events can be listed per record.
   ========================================================= */
SET ANSI_NULLS ON;
SET QUOTED_IDENTIFIER ON;
GO

IF COL_LENGTH('auth.AppUser', 'ExternalProvider') IS NULL
BEGIN
    ALTER TABLE auth.AppUser ADD ExternalProvider VARCHAR(20) NULL, ExternalObjectId NVARCHAR(100) NULL, ExternalTenantId NVARCHAR(100) NULL;
    PRINT 'Added external identity columns to auth.AppUser';
END
GO

IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = 'UX_AppUser_External' AND object_id = OBJECT_ID('auth.AppUser'))
    CREATE UNIQUE INDEX UX_AppUser_External ON auth.AppUser (ExternalProvider, ExternalObjectId) WHERE ExternalObjectId IS NOT NULL;
GO

/* SSO accounts have no password. */
IF EXISTS (SELECT 1 FROM sys.columns WHERE object_id = OBJECT_ID('auth.AppUser') AND name = 'PasswordHash' AND is_nullable = 0)
BEGIN
    ALTER TABLE auth.AppUser ALTER COLUMN PasswordHash VARBINARY(64) NULL;
    ALTER TABLE auth.AppUser ALTER COLUMN PasswordSalt VARBINARY(32) NULL;
    ALTER TABLE auth.AppUser ALTER COLUMN PasswordIterations INT NULL;
    PRINT 'Passwords are now optional (SSO accounts)';
END
GO

IF COL_LENGTH('auth.UserRole', 'Source') IS NULL
    ALTER TABLE auth.UserRole ADD Source VARCHAR(10) NOT NULL CONSTRAINT DF_UserRole_Source DEFAULT 'Manual';
GO
IF COL_LENGTH('auth.UserAttribute', 'Source') IS NULL
    ALTER TABLE auth.UserAttribute ADD Source VARCHAR(10) NOT NULL CONSTRAINT DF_UserAttribute_Source DEFAULT 'Manual';
GO

IF OBJECT_ID(N'auth.ExternalGroupMapping', N'U') IS NULL
BEGIN
    CREATE TABLE auth.ExternalGroupMapping (
        MappingId       INT IDENTITY(1,1) NOT NULL,
        ClaimType       VARCHAR(10)       NOT NULL,   -- 'group' (Entra group object id) | 'appRole' (app role value)
        ClaimValue      NVARCHAR(200)     NOT NULL,
        DisplayName     NVARCHAR(200)     NULL,       -- e.g. the group's name, for administrators
        RoleId          INT               NULL,
        AttributeKey    VARCHAR(100)      NULL,
        AttributeValue  NVARCHAR(200)     NULL,
        CreatedUtc      DATETIME2         NOT NULL CONSTRAINT DF_ExternalGroupMapping_CreatedUtc DEFAULT SYSUTCDATETIME(),
        CONSTRAINT PK_ExternalGroupMapping PRIMARY KEY (MappingId),
        CONSTRAINT FK_ExternalGroupMapping_Role FOREIGN KEY (RoleId) REFERENCES auth.Role (RoleId) ON DELETE CASCADE,
        CONSTRAINT FK_ExternalGroupMapping_Attribute FOREIGN KEY (AttributeKey) REFERENCES auth.AttributeDefinition (AttributeKey),
        CONSTRAINT CK_ExternalGroupMapping_ClaimType CHECK (ClaimType IN ('group', 'appRole')),
        CONSTRAINT CK_ExternalGroupMapping_Target CHECK (RoleId IS NOT NULL OR (AttributeKey IS NOT NULL AND AttributeValue IS NOT NULL))
    );
    CREATE INDEX IX_ExternalGroupMapping_Claim ON auth.ExternalGroupMapping (ClaimType, ClaimValue);
    PRINT 'Created table: auth.ExternalGroupMapping';
END
GO

IF COL_LENGTH('auth.AuthAuditLog', 'ResourceId') IS NULL
BEGIN
    ALTER TABLE auth.AuthAuditLog ADD ResourceId UNIQUEIDENTIFIER NULL;
    PRINT 'Added auth.AuthAuditLog.ResourceId';
END
GO
IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = 'IX_AuthAuditLog_ResourceId' AND object_id = OBJECT_ID('auth.AuthAuditLog'))
    CREATE INDEX IX_AuthAuditLog_ResourceId ON auth.AuthAuditLog (ResourceId, OccurredUtc DESC) WHERE ResourceId IS NOT NULL;
GO

PRINT 'Entra SSO schema complete.';
GO
