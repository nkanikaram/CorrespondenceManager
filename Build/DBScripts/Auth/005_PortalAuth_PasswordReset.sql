/* =========================================================
   ECCO PORTAL AUTH - SELF-SERVICE PASSWORD RESET
   Run after 001-004. Re-runnable.

   "Forgot password?" emails a one-time link. Only a SHA-256 hash of the link's token is stored, so a
   copy of this table can't be used to reset anyone's password. A token works once, expires (30 minutes
   by default), and dies as soon as the account's security stamp changes (any password change, admin
   reset, deactivation or role change), so an older link can't undo a newer change.
   ========================================================= */
SET ANSI_NULLS ON;
SET QUOTED_IDENTIFIER ON;
GO

IF OBJECT_ID(N'auth.PasswordResetToken', N'U') IS NULL
BEGIN
    CREATE TABLE auth.PasswordResetToken (
        TokenId         BIGINT IDENTITY(1,1) NOT NULL,
        UserId          UNIQUEIDENTIFIER     NOT NULL,
        TokenHash       BINARY(32)           NOT NULL,
        SecurityStamp   UNIQUEIDENTIFIER     NOT NULL,   -- the account's stamp when the link was issued
        CreatedUtc      DATETIME2            NOT NULL CONSTRAINT DF_PasswordResetToken_CreatedUtc DEFAULT SYSUTCDATETIME(),
        ExpiresUtc      DATETIME2            NOT NULL,
        UsedUtc         DATETIME2            NULL,       -- set when used, or when a newer link replaces it
        RequestIp       VARCHAR(45)          NULL,
        CONSTRAINT PK_PasswordResetToken PRIMARY KEY (TokenId),
        CONSTRAINT FK_PasswordResetToken_AppUser FOREIGN KEY (UserId) REFERENCES auth.AppUser (UserId) ON DELETE CASCADE
    );
    CREATE UNIQUE INDEX UX_PasswordResetToken_TokenHash ON auth.PasswordResetToken (TokenHash);
    CREATE INDEX IX_PasswordResetToken_User ON auth.PasswordResetToken (UserId, CreatedUtc DESC);
    PRINT 'Created table: auth.PasswordResetToken';
END
GO
