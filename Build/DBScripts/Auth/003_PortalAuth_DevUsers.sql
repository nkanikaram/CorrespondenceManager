/* =========================================================
   ECCO PORTAL AUTH - DEVELOPMENT / TEST USERS ONLY
   !!! Never run this against Demo or Production. !!!
   Every account below has the password  EccoDev!2026  (PBKDF2-HMAC-SHA256, 600,000 iterations).
   For other environments, create the first administrator with the API's
   Auth:BootstrapAdmin settings instead (see README), then add users in the portal.

     admin     Administrator  all customers, PII clearance
     operator  Operator       Life Services + Claims System, PII clearance, export limit 500
     analyst   Analyst        all customers, no PII clearance, export limit 100
     viewer    Viewer         Annuity Correspondence by Email only
   ========================================================= */
SET ANSI_NULLS ON;
SET QUOTED_IDENTIFIER ON;
GO
SET NOCOUNT ON;
SET XACT_ABORT ON;

DECLARE @DatabaseName SYSNAME = DB_NAME();
IF @DatabaseName LIKE '%-P0%' OR @DatabaseName LIKE '%PROD%' OR @DatabaseName LIKE '%Demo%'
BEGIN
    RAISERROR ('Refusing to seed development users into database %s.', 16, 1, @DatabaseName);
    RETURN;
END

BEGIN TRANSACTION;

DECLARE @Users TABLE (UserName NVARCHAR(100), DisplayName NVARCHAR(200), Email NVARCHAR(256),
                      PasswordHash VARBINARY(64), PasswordSalt VARBINARY(32), RoleName NVARCHAR(100));
INSERT INTO @Users VALUES
    (N'admin',    N'Avery Admin',    N'admin@ecco.local',
     0x26BCD51604D3248DF6A1866DC96DC4A1FD155429AEA112875CFEDC876C8C0CE9, 0x1277086C287803A151F419166F5729E6, N'Administrator'),
    (N'operator', N'Olivia Operator', N'operator@ecco.local',
     0x4EC4431E0876C7C0E66DBF1C5D16E05A78C155BA9F3BA9DB587AD095F4348EAC, 0xFD9E855773C6D4F985903CC5A20B506D, N'Operator'),
    (N'analyst',  N'Ari Analyst',    N'analyst@ecco.local',
     0x075815E2B0E5E9BA27948298546C781851FFF900C905967ECF0D40B5FDAB21E8, 0x30BB8A106BC0FED9D0FB888B7FC52AD6, N'Analyst'),
    (N'viewer',   N'Val Viewer',     N'viewer@ecco.local',
     0x728F572D5386AE6F46870DC1F0CA2A8D21E6EE70FA1DACEC9B72BA2F65E777A9, 0xBEB071CBD861BEFDBD791075816AE219, N'Viewer');

INSERT INTO auth.AppUser (UserName, DisplayName, Email, PasswordHash, PasswordSalt, PasswordIterations)
SELECT u.UserName, u.DisplayName, u.Email, u.PasswordHash, u.PasswordSalt, 600000
FROM @Users u
WHERE NOT EXISTS (SELECT 1 FROM auth.AppUser x WHERE x.NormalizedUserName = UPPER(u.UserName));

INSERT INTO auth.UserRole (UserId, RoleId)
SELECT a.UserId, r.RoleId
FROM @Users u
JOIN auth.AppUser a ON a.NormalizedUserName = UPPER(u.UserName)
JOIN auth.Role r ON r.RoleName = u.RoleName
WHERE NOT EXISTS (SELECT 1 FROM auth.UserRole x WHERE x.UserId = a.UserId AND x.RoleId = r.RoleId);

DECLARE @Attrs TABLE (UserName NVARCHAR(100), AttributeKey VARCHAR(100), AttributeValue NVARCHAR(200));
INSERT INTO @Attrs VALUES
    (N'admin',    'scope.customer', N'*'), (N'admin', 'scope.deliveryMethod', N'*'), (N'admin', 'piiClearance', N'true'),
    (N'operator', 'scope.customer', N'Life Services'), (N'operator', 'scope.customer', N'Claims System'),
    (N'operator', 'scope.deliveryMethod', N'*'), (N'operator', 'piiClearance', N'true'), (N'operator', 'maxExportRows', N'500'),
    (N'analyst',  'scope.customer', N'*'), (N'analyst', 'scope.deliveryMethod', N'*'), (N'analyst', 'maxExportRows', N'100'),
    (N'viewer',   'scope.customer', N'Annuity Correspondence'), (N'viewer', 'scope.deliveryMethod', N'Email');

INSERT INTO auth.UserAttribute (UserId, AttributeKey, AttributeValue)
SELECT a.UserId, t.AttributeKey, t.AttributeValue
FROM @Attrs t
JOIN auth.AppUser a ON a.NormalizedUserName = UPPER(t.UserName)
WHERE NOT EXISTS (SELECT 1 FROM auth.UserAttribute x
                  WHERE x.UserId = a.UserId AND x.AttributeKey = t.AttributeKey AND x.AttributeValue = t.AttributeValue);

COMMIT TRANSACTION;
PRINT 'Development users seeded (password: EccoDev!2026).';
GO
