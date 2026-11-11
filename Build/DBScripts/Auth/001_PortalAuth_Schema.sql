/* =========================================================
   ECCO CORRESPONDENCE MANAGER PORTAL - AUTHENTICATION, RBAC AND ABAC
   Re-runnable: every object is created only when missing.

   RBAC  : AppUser -> UserRole -> Role -> RolePermission -> Permission
   ABAC  : UserAttribute holds subject attributes (data scope, clearances, limits).
           AccessPolicy holds deny rules evaluated against subject, resource and
           environment attributes after RBAC has granted the permission.
   Audit : AuthAuditLog records sign-ins, denials and administrative changes.
   ========================================================= */
SET ANSI_NULLS ON;
SET QUOTED_IDENTIFIER ON;
GO

PRINT 'ECCO Portal Auth schema - ' + CONVERT(VARCHAR(19), GETDATE(), 120);
GO

IF NOT EXISTS (SELECT 1 FROM sys.schemas WHERE name = N'auth')
    EXEC (N'CREATE SCHEMA auth AUTHORIZATION dbo');
GO

/* ---------------------------------------------------------
   AppUser: local portal accounts. Passwords are PBKDF2-HMAC-SHA256
   (salt and iteration count stored per user so the cost can be raised
   later without invalidating existing hashes).
   SecurityStamp changes whenever access-relevant data changes
   (password, roles, attributes, deactivation); issued tokens carry it
   and are rejected once it no longer matches.
   --------------------------------------------------------- */
IF OBJECT_ID(N'auth.AppUser', N'U') IS NULL
BEGIN
    CREATE TABLE auth.AppUser (
        UserId              UNIQUEIDENTIFIER NOT NULL CONSTRAINT DF_AppUser_UserId DEFAULT NEWSEQUENTIALID(),
        UserName            NVARCHAR(100)    NOT NULL,
        NormalizedUserName  AS UPPER(UserName) PERSISTED,
        DisplayName         NVARCHAR(200)    NOT NULL,
        Email               NVARCHAR(256)    NULL,
        PasswordHash        VARBINARY(64)    NOT NULL,
        PasswordSalt        VARBINARY(32)    NOT NULL,
        PasswordIterations  INT              NOT NULL,
        MustChangePassword  BIT              NOT NULL CONSTRAINT DF_AppUser_MustChangePassword DEFAULT 0,
        IsActive            BIT              NOT NULL CONSTRAINT DF_AppUser_IsActive DEFAULT 1,
        FailedLoginCount    INT              NOT NULL CONSTRAINT DF_AppUser_FailedLoginCount DEFAULT 0,
        LockoutEndUtc       DATETIME2        NULL,
        LastLoginUtc        DATETIME2        NULL,
        SecurityStamp       UNIQUEIDENTIFIER NOT NULL CONSTRAINT DF_AppUser_SecurityStamp DEFAULT NEWID(),
        CreatedUtc          DATETIME2        NOT NULL CONSTRAINT DF_AppUser_CreatedUtc DEFAULT SYSUTCDATETIME(),
        UpdatedUtc          DATETIME2        NOT NULL CONSTRAINT DF_AppUser_UpdatedUtc DEFAULT SYSUTCDATETIME(),
        CONSTRAINT PK_AppUser PRIMARY KEY (UserId)
    );
    CREATE UNIQUE INDEX UX_AppUser_NormalizedUserName ON auth.AppUser (NormalizedUserName);
    PRINT 'Created table: auth.AppUser';
END
GO

/* ---------------------------------------------------------
   Role / Permission / RolePermission / UserRole (RBAC)
   --------------------------------------------------------- */
IF OBJECT_ID(N'auth.Role', N'U') IS NULL
BEGIN
    CREATE TABLE auth.Role (
        RoleId          INT IDENTITY(1,1) NOT NULL,
        RoleName        NVARCHAR(100)     NOT NULL,
        Description     NVARCHAR(400)     NULL,
        IsSystem        BIT               NOT NULL CONSTRAINT DF_Role_IsSystem DEFAULT 0,
        CreatedUtc      DATETIME2         NOT NULL CONSTRAINT DF_Role_CreatedUtc DEFAULT SYSUTCDATETIME(),
        CONSTRAINT PK_Role PRIMARY KEY (RoleId),
        CONSTRAINT UQ_Role_RoleName UNIQUE (RoleName)
    );
    PRINT 'Created table: auth.Role';
END
GO

IF OBJECT_ID(N'auth.Permission', N'U') IS NULL
BEGIN
    CREATE TABLE auth.Permission (
        PermissionId    INT IDENTITY(1,1) NOT NULL,
        PermissionKey   VARCHAR(100)      NOT NULL,   -- e.g. 'correspondence.read'
        Category        NVARCHAR(100)     NOT NULL,
        Description     NVARCHAR(400)     NULL,
        CONSTRAINT PK_Permission PRIMARY KEY (PermissionId),
        CONSTRAINT UQ_Permission_PermissionKey UNIQUE (PermissionKey)
    );
    PRINT 'Created table: auth.Permission';
END
GO

IF OBJECT_ID(N'auth.RolePermission', N'U') IS NULL
BEGIN
    CREATE TABLE auth.RolePermission (
        RoleId          INT NOT NULL,
        PermissionId    INT NOT NULL,
        CONSTRAINT PK_RolePermission PRIMARY KEY (RoleId, PermissionId),
        CONSTRAINT FK_RolePermission_Role FOREIGN KEY (RoleId) REFERENCES auth.Role (RoleId) ON DELETE CASCADE,
        CONSTRAINT FK_RolePermission_Permission FOREIGN KEY (PermissionId) REFERENCES auth.Permission (PermissionId) ON DELETE CASCADE
    );
    PRINT 'Created table: auth.RolePermission';
END
GO

IF OBJECT_ID(N'auth.UserRole', N'U') IS NULL
BEGIN
    CREATE TABLE auth.UserRole (
        UserId          UNIQUEIDENTIFIER NOT NULL,
        RoleId          INT              NOT NULL,
        AssignedUtc     DATETIME2        NOT NULL CONSTRAINT DF_UserRole_AssignedUtc DEFAULT SYSUTCDATETIME(),
        CONSTRAINT PK_UserRole PRIMARY KEY (UserId, RoleId),
        CONSTRAINT FK_UserRole_AppUser FOREIGN KEY (UserId) REFERENCES auth.AppUser (UserId) ON DELETE CASCADE,
        CONSTRAINT FK_UserRole_Role FOREIGN KEY (RoleId) REFERENCES auth.Role (RoleId) ON DELETE CASCADE
    );
    CREATE INDEX IX_UserRole_RoleId ON auth.UserRole (RoleId);
    PRINT 'Created table: auth.UserRole';
END
GO

/* ---------------------------------------------------------
   UserAttribute (ABAC subject attributes). Multi-valued: one row per value.
   Known keys (see AttributeDefinition):
     scope.customer        RequestingDomainName values the user may see ('*' = all)
     scope.deliveryMethod  delivery methods the user may see ('*' = all)
     piiClearance          'true' allows viewing request payloads (personal data)
     maxExportRows         upper bound for "export all matching"
   --------------------------------------------------------- */
IF OBJECT_ID(N'auth.AttributeDefinition', N'U') IS NULL
BEGIN
    CREATE TABLE auth.AttributeDefinition (
        AttributeKey    VARCHAR(100)   NOT NULL,
        DisplayName     NVARCHAR(200)  NOT NULL,
        DataType        VARCHAR(20)    NOT NULL,   -- 'list' | 'boolean' | 'integer' | 'string'
        Description     NVARCHAR(400)  NULL,
        CONSTRAINT PK_AttributeDefinition PRIMARY KEY (AttributeKey),
        CONSTRAINT CK_AttributeDefinition_DataType CHECK (DataType IN ('list', 'boolean', 'integer', 'string'))
    );
    PRINT 'Created table: auth.AttributeDefinition';
END
GO

IF OBJECT_ID(N'auth.UserAttribute', N'U') IS NULL
BEGIN
    CREATE TABLE auth.UserAttribute (
        UserAttributeId BIGINT IDENTITY(1,1) NOT NULL,
        UserId          UNIQUEIDENTIFIER     NOT NULL,
        AttributeKey    VARCHAR(100)         NOT NULL,
        AttributeValue  NVARCHAR(200)        NOT NULL,
        CONSTRAINT PK_UserAttribute PRIMARY KEY (UserAttributeId),
        CONSTRAINT FK_UserAttribute_AppUser FOREIGN KEY (UserId) REFERENCES auth.AppUser (UserId) ON DELETE CASCADE,
        CONSTRAINT FK_UserAttribute_AttributeDefinition FOREIGN KEY (AttributeKey) REFERENCES auth.AttributeDefinition (AttributeKey),
        CONSTRAINT UQ_UserAttribute UNIQUE (UserId, AttributeKey, AttributeValue)
    );
    PRINT 'Created table: auth.UserAttribute';
END
GO

/* ---------------------------------------------------------
   AccessPolicy (ABAC rules). Evaluated after RBAC grants PermissionKey.
   Effect 'Deny' rules whose condition matches block the action (deny overrides).
   ConditionJson grammar (evaluated by AccessPolicyEvaluator):
     { "all": [ <clause>, ... ] }   or   { "any": [ <clause>, ... ] }
     <clause> = { "attr": "resource.status", "op": "notIn", "values": ["Failed"] }
              | { "attr": "resource.customer", "op": "notIn", "valuesFrom": "subject.scope.customer" }
     attr prefixes: subject.*, resource.*, env.* (env.hourUtc, env.dayOfWeek)
     ops: eq, neq, in, notIn, exists, notExists, gt, gte, lt, lte
     A '*' in a valuesFrom list matches every value.
   --------------------------------------------------------- */
IF OBJECT_ID(N'auth.AccessPolicy', N'U') IS NULL
BEGIN
    CREATE TABLE auth.AccessPolicy (
        PolicyId        INT IDENTITY(1,1) NOT NULL,
        PolicyName      NVARCHAR(200)     NOT NULL,
        Description     NVARCHAR(1000)    NULL,
        PermissionKey   VARCHAR(100)      NOT NULL,
        Effect          VARCHAR(10)       NOT NULL CONSTRAINT DF_AccessPolicy_Effect DEFAULT 'Deny',
        ConditionJson   NVARCHAR(MAX)     NOT NULL,
        DenyMessage     NVARCHAR(400)     NULL,
        Priority        INT               NOT NULL CONSTRAINT DF_AccessPolicy_Priority DEFAULT 100,
        IsActive        BIT               NOT NULL CONSTRAINT DF_AccessPolicy_IsActive DEFAULT 1,
        UpdatedUtc      DATETIME2         NOT NULL CONSTRAINT DF_AccessPolicy_UpdatedUtc DEFAULT SYSUTCDATETIME(),
        CONSTRAINT PK_AccessPolicy PRIMARY KEY (PolicyId),
        CONSTRAINT UQ_AccessPolicy_PolicyName UNIQUE (PolicyName),
        CONSTRAINT FK_AccessPolicy_Permission FOREIGN KEY (PermissionKey) REFERENCES auth.Permission (PermissionKey),
        CONSTRAINT CK_AccessPolicy_Effect CHECK (Effect IN ('Deny')),
        CONSTRAINT CK_AccessPolicy_ConditionJson CHECK (ISJSON(ConditionJson) = 1)
    );
    CREATE INDEX IX_AccessPolicy_PermissionKey ON auth.AccessPolicy (PermissionKey) WHERE IsActive = 1;
    PRINT 'Created table: auth.AccessPolicy';
END
GO

/* ---------------------------------------------------------
   AuthAuditLog
   --------------------------------------------------------- */
IF OBJECT_ID(N'auth.AuthAuditLog', N'U') IS NULL
BEGIN
    CREATE TABLE auth.AuthAuditLog (
        AuditId         BIGINT IDENTITY(1,1) NOT NULL,
        OccurredUtc     DATETIME2            NOT NULL CONSTRAINT DF_AuthAuditLog_OccurredUtc DEFAULT SYSUTCDATETIME(),
        UserId          UNIQUEIDENTIFIER     NULL,
        UserName        NVARCHAR(100)        NULL,
        EventType       VARCHAR(50)          NOT NULL,   -- LoginSucceeded, LoginFailed, LockedOut, AccessDenied, UserCreated, ...
        Outcome         VARCHAR(20)          NOT NULL,   -- Success | Failure | Denied
        Detail          NVARCHAR(1000)       NULL,
        IpAddress       VARCHAR(64)          NULL,
        CONSTRAINT PK_AuthAuditLog PRIMARY KEY (AuditId)
    );
    CREATE INDEX IX_AuthAuditLog_OccurredUtc ON auth.AuthAuditLog (OccurredUtc DESC);
    CREATE INDEX IX_AuthAuditLog_UserId ON auth.AuthAuditLog (UserId, OccurredUtc DESC);
    PRINT 'Created table: auth.AuthAuditLog';
END
GO

/* Chat history ownership: conversations are keyed by the signed-in user's id from now on. */
IF OBJECT_ID(N'dbo.AiChatConversation', N'U') IS NOT NULL
   AND NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = N'IX_AiChatConversation_UserId' AND object_id = OBJECT_ID(N'dbo.AiChatConversation'))
    CREATE INDEX IX_AiChatConversation_UserId ON dbo.AiChatConversation (UserId);
GO

/* Server-side grid filtering on customer and delivery method (ABAC data scope uses the same columns). */
IF OBJECT_ID(N'dbo.CorrespondenceRequest', N'U') IS NOT NULL
   AND NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = N'IX_CorrespondenceRequest_Domain_Delivery' AND object_id = OBJECT_ID(N'dbo.CorrespondenceRequest'))
    CREATE INDEX IX_CorrespondenceRequest_Domain_Delivery
        ON dbo.CorrespondenceRequest (RequestingDomainName, DeliveryMethodCode) INCLUDE (RequestCreationTS);
GO

PRINT 'ECCO Portal Auth schema complete.';
GO
