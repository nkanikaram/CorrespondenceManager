/* =========================================================
   ECCO PORTAL AUTH - USER PREFERENCES
   Run after 001-005. Re-runnable.

   Display preferences saved with the account, so they follow the user to any browser:
   theme (light | dark | system), time zone for dates (local | utc), default page size and row density.
   Stored as a small JSON document; the API validates every value (Controllers/AccountController.cs).
   ========================================================= */
SET ANSI_NULLS ON;
SET QUOTED_IDENTIFIER ON;
GO

IF OBJECT_ID(N'auth.UserPreference', N'U') IS NULL
BEGIN
    CREATE TABLE auth.UserPreference (
        UserId          UNIQUEIDENTIFIER     NOT NULL,
        PreferencesJson NVARCHAR(2000)       NOT NULL,
        UpdatedUtc      DATETIME2            NOT NULL CONSTRAINT DF_UserPreference_UpdatedUtc DEFAULT SYSUTCDATETIME(),
        CONSTRAINT PK_UserPreference PRIMARY KEY (UserId),
        CONSTRAINT FK_UserPreference_AppUser FOREIGN KEY (UserId) REFERENCES auth.AppUser (UserId) ON DELETE CASCADE,
        CONSTRAINT CK_UserPreference_Json CHECK (ISJSON(PreferencesJson) = 1)
    );
    PRINT 'Created table: auth.UserPreference';
END
GO
