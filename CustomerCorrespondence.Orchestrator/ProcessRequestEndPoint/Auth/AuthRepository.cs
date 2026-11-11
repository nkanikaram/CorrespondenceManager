using System.Data;
using Microsoft.Data.SqlClient;
using Microsoft.Extensions.Options;

namespace ProcessRequestEndPoint.Auth
{
    /// <summary>
    /// Data access for the auth schema (see Build/DBScripts/Auth). Plain parameterized ADO.NET,
    /// matching CorrespondenceSearchController, against the same AZURE_SQL_CONNECTION database.
    /// </summary>
    public class AuthRepository
    {
        private readonly string _connectionString;

        public AuthRepository(IConfiguration configuration, IOptions<PortalAuthOptions> options)
        {
            var name = options.Value.ConnectionStringName;
            _connectionString = configuration.GetConnectionString(name)
                ?? configuration[name]
                ?? throw new InvalidOperationException($"{name} is not configured.");
        }

        private async Task<SqlConnection> OpenAsync(CancellationToken ct = default)
        {
            var conn = new SqlConnection(_connectionString);
            await conn.OpenAsync(ct);
            return conn;
        }

        private static SqlCommand Command(SqlConnection conn, string sql, SqlTransaction? tx = null, params SqlParameter[] parameters)
        {
            var cmd = new SqlCommand(sql, conn, tx);
            cmd.Parameters.AddRange(parameters);
            return cmd;
        }

        private static SqlParameter P(string name, SqlDbType type, object? value, int size = 0)
        {
            var p = new SqlParameter(name, type) { Value = value ?? DBNull.Value };
            if (size != 0) p.Size = size;
            return p;
        }

        private static string? Str(SqlDataReader r, string col) => r.IsDBNull(r.GetOrdinal(col)) ? null : r.GetString(r.GetOrdinal(col));
        private static DateTime? Date(SqlDataReader r, string col) => r.IsDBNull(r.GetOrdinal(col)) ? null : r.GetDateTime(r.GetOrdinal(col));

        // ================= Sign-in =================

        public async Task<AppUserCredentials?> FindCredentialsAsync(string userName, CancellationToken ct = default)
        {
            const string sql = @"
SELECT UserId, UserName, PasswordHash, PasswordSalt, PasswordIterations, IsActive, FailedLoginCount, LockoutEndUtc, ExternalProvider
FROM auth.AppUser WHERE NormalizedUserName = UPPER(@userName)";
            await using var conn = await OpenAsync(ct);
            await using var cmd = Command(conn, sql, null, P("@userName", SqlDbType.NVarChar, userName, 100));
            await using var r = await cmd.ExecuteReaderAsync(ct);
            if (!await r.ReadAsync(ct)) return null;
            return new AppUserCredentials
            {
                UserId = r.GetGuid(0),
                UserName = r.GetString(1),
                PasswordHash = r.IsDBNull(2) ? Array.Empty<byte>() : (byte[])r[2],
                PasswordSalt = r.IsDBNull(3) ? Array.Empty<byte>() : (byte[])r[3],
                PasswordIterations = r.IsDBNull(4) ? 0 : r.GetInt32(4),
                IsActive = r.GetBoolean(5),
                FailedLoginCount = r.GetInt32(6),
                LockoutEndUtc = r.IsDBNull(7) ? null : r.GetDateTime(7),
                ExternalProvider = r.IsDBNull(8) ? null : r.GetString(8)
            };
        }

        /// <summary>Counts a failed attempt; locks the account once the threshold is reached. Returns true when now locked.</summary>
        public async Task<bool> RecordFailedLoginAsync(Guid userId, int threshold, int lockoutMinutes, CancellationToken ct = default)
        {
            const string sql = @"
UPDATE auth.AppUser
SET FailedLoginCount = CASE WHEN FailedLoginCount + 1 >= @threshold THEN 0 ELSE FailedLoginCount + 1 END,
    LockoutEndUtc    = CASE WHEN FailedLoginCount + 1 >= @threshold THEN DATEADD(MINUTE, @minutes, SYSUTCDATETIME()) ELSE LockoutEndUtc END,
    UpdatedUtc       = SYSUTCDATETIME()
OUTPUT CASE WHEN inserted.LockoutEndUtc > SYSUTCDATETIME() THEN 1 ELSE 0 END
WHERE UserId = @id";
            await using var conn = await OpenAsync(ct);
            await using var cmd = Command(conn, sql, null,
                P("@id", SqlDbType.UniqueIdentifier, userId), P("@threshold", SqlDbType.Int, threshold), P("@minutes", SqlDbType.Int, lockoutMinutes));
            var locked = await cmd.ExecuteScalarAsync(ct);
            return Convert.ToInt32(locked ?? 0) == 1;
        }

        public async Task RecordSuccessfulLoginAsync(Guid userId, CancellationToken ct = default)
        {
            const string sql = @"
UPDATE auth.AppUser SET FailedLoginCount = 0, LockoutEndUtc = NULL, LastLoginUtc = SYSUTCDATETIME() WHERE UserId = @id";
            await using var conn = await OpenAsync(ct);
            await using var cmd = Command(conn, sql, null, P("@id", SqlDbType.UniqueIdentifier, userId));
            await cmd.ExecuteNonQueryAsync(ct);
        }

        /// <summary>Sets a new password and rotates the security stamp, which invalidates every issued token.</summary>
        public async Task SetPasswordAsync(Guid userId, PasswordHash hash, bool mustChange, CancellationToken ct = default)
        {
            const string sql = @"
UPDATE auth.AppUser
SET PasswordHash = @hash, PasswordSalt = @salt, PasswordIterations = @iterations, MustChangePassword = @mustChange,
    FailedLoginCount = 0, LockoutEndUtc = NULL, SecurityStamp = NEWID(), UpdatedUtc = SYSUTCDATETIME()
WHERE UserId = @id";
            await using var conn = await OpenAsync(ct);
            await using var cmd = Command(conn, sql, null,
                P("@id", SqlDbType.UniqueIdentifier, userId),
                P("@hash", SqlDbType.VarBinary, hash.Hash, 64),
                P("@salt", SqlDbType.VarBinary, hash.Salt, 32),
                P("@iterations", SqlDbType.Int, hash.Iterations),
                P("@mustChange", SqlDbType.Bit, mustChange));
            await cmd.ExecuteNonQueryAsync(ct);
        }

        // ================= Self-service password reset =================

        /// <summary>
        /// The account a "forgot password" request refers to: a user name match first, otherwise the one account with
        /// that email address (none when several accounts share it). Null when there's no match.
        /// </summary>
        public async Task<PasswordResetCandidate?> FindResetCandidateAsync(string userNameOrEmail, CancellationToken ct = default)
        {
            const string sql = @"
SELECT TOP (2) UserId, UserName, DisplayName, Email, IsActive, ExternalProvider, SecurityStamp, MatchedOn
FROM (
    SELECT UserId, UserName, DisplayName, Email, IsActive, ExternalProvider, SecurityStamp, 0 AS MatchedOn
    FROM auth.AppUser WHERE NormalizedUserName = UPPER(@value)
    UNION ALL
    SELECT UserId, UserName, DisplayName, Email, IsActive, ExternalProvider, SecurityStamp, 1
    FROM auth.AppUser WHERE Email = @value AND NormalizedUserName <> UPPER(@value)
) m
ORDER BY MatchedOn";
            await using var conn = await OpenAsync(ct);
            await using var cmd = Command(conn, sql, null, P("@value", SqlDbType.NVarChar, userNameOrEmail, 256));
            await using var r = await cmd.ExecuteReaderAsync(ct);
            var found = new List<(PasswordResetCandidate User, int MatchedOn)>();
            while (await r.ReadAsync(ct))
                found.Add((ReadResetCandidate(r, tokenId: null), r.GetInt32(7)));
            if (found.Count == 0) return null;
            if (found[0].MatchedOn == 0) return found[0].User;
            return found.Count == 1 ? found[0].User : null; // an email shared by several accounts is ambiguous
        }

        private static PasswordResetCandidate ReadResetCandidate(SqlDataReader r, long? tokenId) => new()
        {
            TokenId = tokenId,
            UserId = r.GetGuid(r.GetOrdinal("UserId")),
            UserName = r.GetString(r.GetOrdinal("UserName")),
            DisplayName = r.GetString(r.GetOrdinal("DisplayName")),
            Email = Str(r, "Email"),
            IsActive = r.GetBoolean(r.GetOrdinal("IsActive")),
            ExternalProvider = Str(r, "ExternalProvider"),
            SecurityStamp = r.GetGuid(r.GetOrdinal("SecurityStamp"))
        };

        public async Task<int> CountRecentResetRequestsAsync(Guid userId, TimeSpan window, CancellationToken ct = default)
        {
            const string sql = "SELECT COUNT(*) FROM auth.PasswordResetToken WHERE UserId = @id AND CreatedUtc > DATEADD(SECOND, -@seconds, SYSUTCDATETIME())";
            await using var conn = await OpenAsync(ct);
            await using var cmd = Command(conn, sql, null,
                P("@id", SqlDbType.UniqueIdentifier, userId), P("@seconds", SqlDbType.Int, (int)window.TotalSeconds));
            return Convert.ToInt32(await cmd.ExecuteScalarAsync(ct));
        }

        /// <summary>Stores a new reset token (its hash) and retires the user's earlier unused ones, so only the newest link works.</summary>
        public async Task CreateResetTokenAsync(Guid userId, byte[] tokenHash, Guid securityStamp, DateTime expiresUtc, string? ip, CancellationToken ct = default)
        {
            const string sql = @"
UPDATE auth.PasswordResetToken SET UsedUtc = SYSUTCDATETIME() WHERE UserId = @id AND UsedUtc IS NULL;
INSERT INTO auth.PasswordResetToken (UserId, TokenHash, SecurityStamp, ExpiresUtc, RequestIp) VALUES (@id, @hash, @stamp, @expires, @ip);
DELETE FROM auth.PasswordResetToken WHERE CreatedUtc < DATEADD(DAY, -30, SYSUTCDATETIME());";
            await using var conn = await OpenAsync(ct);
            await using var cmd = Command(conn, sql, null,
                P("@id", SqlDbType.UniqueIdentifier, userId),
                P("@hash", SqlDbType.Binary, tokenHash, 32),
                P("@stamp", SqlDbType.UniqueIdentifier, securityStamp),
                P("@expires", SqlDbType.DateTime2, expiresUtc),
                P("@ip", SqlDbType.VarChar, ip, 45));
            await cmd.ExecuteNonQueryAsync(ct);
        }

        /// <summary>
        /// The account a reset token belongs to, when the token is unused, unexpired, issued for the account's current
        /// security stamp, and the account is an active local one. Null otherwise.
        /// </summary>
        public async Task<PasswordResetCandidate?> FindValidResetTokenAsync(byte[] tokenHash, CancellationToken ct = default)
        {
            const string sql = @"
SELECT t.TokenId, u.UserId, u.UserName, u.DisplayName, u.Email, u.IsActive, u.ExternalProvider, u.SecurityStamp
FROM auth.PasswordResetToken t
JOIN auth.AppUser u ON u.UserId = t.UserId
WHERE t.TokenHash = @hash AND t.UsedUtc IS NULL AND t.ExpiresUtc > SYSUTCDATETIME()
  AND t.SecurityStamp = u.SecurityStamp AND u.IsActive = 1 AND u.ExternalProvider IS NULL";
            await using var conn = await OpenAsync(ct);
            await using var cmd = Command(conn, sql, null, P("@hash", SqlDbType.Binary, tokenHash, 32));
            await using var r = await cmd.ExecuteReaderAsync(ct);
            if (!await r.ReadAsync(ct)) return null;
            return ReadResetCandidate(r, r.GetInt64(0));
        }

        /// <summary>
        /// Uses a reset token: sets the new password (which also unlocks the account and rotates the stamp) and marks the
        /// token used, in one transaction. False when the token was already used or the account changed in the meantime.
        /// </summary>
        public async Task<bool> CompletePasswordResetAsync(long tokenId, Guid userId, Guid expectedStamp, PasswordHash hash, CancellationToken ct = default)
        {
            const string sql = @"
UPDATE auth.PasswordResetToken SET UsedUtc = SYSUTCDATETIME() WHERE TokenId = @tokenId AND UserId = @id AND UsedUtc IS NULL;
IF @@ROWCOUNT = 0 BEGIN SELECT 0; RETURN; END
UPDATE auth.AppUser
SET PasswordHash = @hash, PasswordSalt = @salt, PasswordIterations = @iterations, MustChangePassword = 0,
    FailedLoginCount = 0, LockoutEndUtc = NULL, SecurityStamp = NEWID(), UpdatedUtc = SYSUTCDATETIME()
WHERE UserId = @id AND SecurityStamp = @stamp AND IsActive = 1 AND ExternalProvider IS NULL;
SELECT @@ROWCOUNT;";
            await using var conn = await OpenAsync(ct);
            await using var tx = (SqlTransaction)await conn.BeginTransactionAsync(IsolationLevel.Serializable, ct);
            await using var cmd = Command(conn, sql, tx,
                P("@tokenId", SqlDbType.BigInt, tokenId),
                P("@id", SqlDbType.UniqueIdentifier, userId),
                P("@stamp", SqlDbType.UniqueIdentifier, expectedStamp),
                P("@hash", SqlDbType.VarBinary, hash.Hash, 64),
                P("@salt", SqlDbType.VarBinary, hash.Salt, 32),
                P("@iterations", SqlDbType.Int, hash.Iterations));
            var updated = Convert.ToInt32(await cmd.ExecuteScalarAsync(ct)) == 1;
            if (updated) await tx.CommitAsync(ct); else await tx.RollbackAsync(ct);
            return updated;
        }

        // ================= Access profile =================

        public async Task<UserAccessProfile?> GetAccessProfileAsync(Guid userId, CancellationToken ct = default)
        {
            const string sql = @"
SELECT UserId, UserName, DisplayName, Email, IsActive, MustChangePassword, SecurityStamp, ExternalProvider FROM auth.AppUser WHERE UserId = @id;

SELECT r.RoleName FROM auth.UserRole ur JOIN auth.Role r ON r.RoleId = ur.RoleId WHERE ur.UserId = @id ORDER BY r.RoleName;

SELECT DISTINCT p.PermissionKey
FROM auth.UserRole ur
JOIN auth.RolePermission rp ON rp.RoleId = ur.RoleId
JOIN auth.Permission p ON p.PermissionId = rp.PermissionId
WHERE ur.UserId = @id;

SELECT AttributeKey, AttributeValue FROM auth.UserAttribute WHERE UserId = @id ORDER BY AttributeKey, AttributeValue;";

            await using var conn = await OpenAsync(ct);
            await using var cmd = Command(conn, sql, null, P("@id", SqlDbType.UniqueIdentifier, userId));
            await using var r = await cmd.ExecuteReaderAsync(ct);
            if (!await r.ReadAsync(ct)) return null;

            var id = r.GetGuid(0);
            var userName = r.GetString(1);
            var displayName = r.GetString(2);
            var email = r.IsDBNull(3) ? null : r.GetString(3);
            var isActive = r.GetBoolean(4);
            var mustChange = r.GetBoolean(5);
            var stamp = r.GetGuid(6);
            var provider = r.IsDBNull(7) ? null : r.GetString(7);

            var roles = new List<string>();
            await r.NextResultAsync(ct);
            while (await r.ReadAsync(ct)) roles.Add(r.GetString(0));

            var permissions = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            await r.NextResultAsync(ct);
            while (await r.ReadAsync(ct)) permissions.Add(r.GetString(0));

            var attributes = new Dictionary<string, List<string>>(StringComparer.OrdinalIgnoreCase);
            await r.NextResultAsync(ct);
            while (await r.ReadAsync(ct))
            {
                var key = r.GetString(0);
                if (!attributes.TryGetValue(key, out var list)) attributes[key] = list = new List<string>();
                list.Add(r.GetString(1));
            }

            return new UserAccessProfile
            {
                UserId = id,
                UserName = userName,
                DisplayName = displayName,
                Email = email,
                IsActive = isActive,
                MustChangePassword = mustChange,
                SecurityStamp = stamp,
                ExternalProvider = provider,
                Roles = roles,
                Permissions = permissions,
                Attributes = attributes.ToDictionary(kv => kv.Key, kv => (IReadOnlyList<string>)kv.Value, StringComparer.OrdinalIgnoreCase)
            };
        }

        public async Task<List<AccessPolicyRecord>> GetPoliciesAsync(bool activeOnly, CancellationToken ct = default)
        {
            var sql = @"
SELECT PolicyId, PolicyName, Description, PermissionKey, Effect, ConditionJson, DenyMessage, Priority, IsActive, UpdatedUtc
FROM auth.AccessPolicy" + (activeOnly ? " WHERE IsActive = 1" : "") + " ORDER BY PermissionKey, Priority, PolicyName";
            var list = new List<AccessPolicyRecord>();
            await using var conn = await OpenAsync(ct);
            await using var cmd = Command(conn, sql);
            await using var r = await cmd.ExecuteReaderAsync(ct);
            while (await r.ReadAsync(ct))
            {
                list.Add(new AccessPolicyRecord
                {
                    PolicyId = r.GetInt32(0),
                    PolicyName = r.GetString(1),
                    Description = r.IsDBNull(2) ? null : r.GetString(2),
                    PermissionKey = r.GetString(3),
                    Effect = r.GetString(4),
                    ConditionJson = r.GetString(5),
                    DenyMessage = r.IsDBNull(6) ? null : r.GetString(6),
                    Priority = r.GetInt32(7),
                    IsActive = r.GetBoolean(8),
                    UpdatedUtc = r.GetDateTime(9)
                });
            }
            return list;
        }

        // ================= Administration =================

        public async Task<int> CountUsersAsync(CancellationToken ct = default)
        {
            await using var conn = await OpenAsync(ct);
            await using var cmd = Command(conn, "SELECT COUNT(1) FROM auth.AppUser");
            return Convert.ToInt32(await cmd.ExecuteScalarAsync(ct) ?? 0);
        }

        public async Task<List<UserSummaryDto>> ListUsersAsync(CancellationToken ct = default)
        {
            const string sql = @"
SELECT UserId, UserName, DisplayName, Email, IsActive, MustChangePassword,
       CAST(CASE WHEN LockoutEndUtc > SYSUTCDATETIME() THEN 1 ELSE 0 END AS bit) AS IsLockedOut, LastLoginUtc, ExternalProvider
FROM auth.AppUser ORDER BY UserName;

SELECT ur.UserId, r.RoleName, ur.Source FROM auth.UserRole ur JOIN auth.Role r ON r.RoleId = ur.RoleId ORDER BY r.RoleName;

SELECT UserId, AttributeKey, AttributeValue, Source FROM auth.UserAttribute ORDER BY AttributeKey, AttributeValue;";

            var users = new List<(Guid Id, string UserName, string DisplayName, string? Email, bool Active, bool MustChange, bool Locked, DateTime? LastLogin, string? Provider)>();
            var roles = new Dictionary<Guid, List<string>>();
            var groupRoles = new Dictionary<Guid, List<string>>();
            var attrs = new Dictionary<Guid, Dictionary<string, List<string>>>();
            var groupAttrs = new Dictionary<Guid, Dictionary<string, List<string>>>();

            await using (var conn = await OpenAsync(ct))
            await using (var cmd = Command(conn, sql))
            await using (var r = await cmd.ExecuteReaderAsync(ct))
            {
                while (await r.ReadAsync(ct))
                    users.Add((r.GetGuid(0), r.GetString(1), r.GetString(2), Str(r, "Email"), r.GetBoolean(4), r.GetBoolean(5), r.GetBoolean(6),
                        Date(r, "LastLoginUtc"), Str(r, "ExternalProvider")));

                await r.NextResultAsync(ct);
                while (await r.ReadAsync(ct))
                {
                    var id = r.GetGuid(0);
                    if (!roles.TryGetValue(id, out var list)) roles[id] = list = new List<string>();
                    if (!list.Contains(r.GetString(1))) list.Add(r.GetString(1));
                    if (r.GetString(2) == GroupSource)
                    {
                        if (!groupRoles.TryGetValue(id, out var g)) groupRoles[id] = g = new List<string>();
                        g.Add(r.GetString(1));
                    }
                }

                await r.NextResultAsync(ct);
                while (await r.ReadAsync(ct))
                {
                    var id = r.GetGuid(0);
                    var target = r.GetString(3) == GroupSource ? groupAttrs : attrs;
                    if (!target.TryGetValue(id, out var map)) target[id] = map = new Dictionary<string, List<string>>(StringComparer.OrdinalIgnoreCase);
                    var key = r.GetString(1);
                    if (!map.TryGetValue(key, out var values)) map[key] = values = new List<string>();
                    values.Add(r.GetString(2));
                }
            }

            static IReadOnlyDictionary<string, IReadOnlyList<string>> Freeze(Dictionary<Guid, Dictionary<string, List<string>>> source, Guid id) =>
                source.TryGetValue(id, out var m)
                    ? m.ToDictionary(kv => kv.Key, kv => (IReadOnlyList<string>)kv.Value)
                    : new Dictionary<string, IReadOnlyList<string>>();

            return users.Select(u => new UserSummaryDto
            {
                UserId = u.Id,
                UserName = u.UserName,
                DisplayName = u.DisplayName,
                Email = u.Email,
                IsActive = u.Active,
                MustChangePassword = u.MustChange,
                IsLockedOut = u.Locked,
                LastLoginUtc = u.LastLogin,
                Roles = roles.TryGetValue(u.Id, out var rl) ? rl : new List<string>(),
                Attributes = Freeze(attrs, u.Id),        // manually assigned (editable)
                ExternalProvider = u.Provider,
                GroupRoles = groupRoles.TryGetValue(u.Id, out var gr) ? gr : new List<string>(),
                GroupAttributes = Freeze(groupAttrs, u.Id)
            }).ToList();
        }

        public async Task<bool> UserNameExistsAsync(string userName, CancellationToken ct = default)
        {
            await using var conn = await OpenAsync(ct);
            await using var cmd = Command(conn, "SELECT COUNT(1) FROM auth.AppUser WHERE NormalizedUserName = UPPER(@u)", null,
                P("@u", SqlDbType.NVarChar, userName, 100));
            return Convert.ToInt32(await cmd.ExecuteScalarAsync(ct) ?? 0) > 0;
        }

        /// <summary>Creates a user with roles and attributes in one transaction. Returns the new id.</summary>
        public async Task<Guid> CreateUserAsync(string userName, string displayName, string? email, bool isActive, PasswordHash hash,
            bool mustChangePassword, IEnumerable<string> roles, IDictionary<string, List<string>> attributes, CancellationToken ct = default)
        {
            await using var conn = await OpenAsync(ct);
            await using var tx = (SqlTransaction)await conn.BeginTransactionAsync(ct);
            Guid id;
            await using (var cmd = Command(conn, @"
INSERT INTO auth.AppUser (UserName, DisplayName, Email, PasswordHash, PasswordSalt, PasswordIterations, MustChangePassword, IsActive)
OUTPUT inserted.UserId
VALUES (@u, @d, @e, @hash, @salt, @iterations, @mustChange, @active)", tx,
                P("@u", SqlDbType.NVarChar, userName, 100), P("@d", SqlDbType.NVarChar, displayName, 200), P("@e", SqlDbType.NVarChar, email, 256),
                P("@hash", SqlDbType.VarBinary, hash.Hash, 64), P("@salt", SqlDbType.VarBinary, hash.Salt, 32),
                P("@iterations", SqlDbType.Int, hash.Iterations), P("@mustChange", SqlDbType.Bit, mustChangePassword), P("@active", SqlDbType.Bit, isActive)))
            {
                id = (Guid)(await cmd.ExecuteScalarAsync(ct))!;
            }
            await ReplaceRolesAndAttributesAsync(conn, tx, id, roles, attributes, ct);
            await tx.CommitAsync(ct);
            return id;
        }

        /// <summary>Updates profile fields, roles and attributes, and rotates the security stamp. Returns false if not found.</summary>
        public async Task<bool> UpdateUserAsync(Guid userId, string displayName, string? email, bool isActive,
            IEnumerable<string> roles, IDictionary<string, List<string>> attributes, CancellationToken ct = default)
        {
            await using var conn = await OpenAsync(ct);
            await using var tx = (SqlTransaction)await conn.BeginTransactionAsync(ct);
            int rows;
            await using (var cmd = Command(conn, @"
UPDATE auth.AppUser
SET DisplayName = @d, Email = @e, IsActive = @active, SecurityStamp = NEWID(), UpdatedUtc = SYSUTCDATETIME(),
    LockoutEndUtc = CASE WHEN @active = 1 THEN LockoutEndUtc ELSE NULL END
WHERE UserId = @id", tx,
                P("@id", SqlDbType.UniqueIdentifier, userId), P("@d", SqlDbType.NVarChar, displayName, 200),
                P("@e", SqlDbType.NVarChar, email, 256), P("@active", SqlDbType.Bit, isActive)))
            {
                rows = await cmd.ExecuteNonQueryAsync(ct);
            }
            if (rows == 0) { await tx.RollbackAsync(ct); return false; }
            await ReplaceRolesAndAttributesAsync(conn, tx, userId, roles, attributes, ct);
            await tx.CommitAsync(ct);
            return true;
        }

        private static async Task ReplaceRolesAndAttributesAsync(SqlConnection conn, SqlTransaction tx, Guid userId,
            IEnumerable<string> roles, IDictionary<string, List<string>> attributes, CancellationToken ct)
        {
            // Only manually assigned grants are replaced; those synced from Entra groups stay as they are.
            await using (var del = Command(conn, @"
DELETE FROM auth.UserRole WHERE UserId = @id AND Source = 'Manual';
DELETE FROM auth.UserAttribute WHERE UserId = @id AND Source = 'Manual';", tx,
                P("@id", SqlDbType.UniqueIdentifier, userId)))
            {
                await del.ExecuteNonQueryAsync(ct);
            }

            foreach (var role in roles.Distinct(StringComparer.OrdinalIgnoreCase))
            {
                await using var ins = Command(conn, @"
INSERT INTO auth.UserRole (UserId, RoleId, Source) SELECT @id, r.RoleId, 'Manual' FROM auth.Role r
WHERE r.RoleName = @role AND NOT EXISTS (SELECT 1 FROM auth.UserRole x WHERE x.UserId = @id AND x.RoleId = r.RoleId)", tx,
                    P("@id", SqlDbType.UniqueIdentifier, userId), P("@role", SqlDbType.NVarChar, role, 100));
                await ins.ExecuteNonQueryAsync(ct);
            }

            foreach (var (key, values) in attributes)
            {
                foreach (var value in values.Where(v => !string.IsNullOrWhiteSpace(v)).Select(v => v.Trim()).Distinct(StringComparer.OrdinalIgnoreCase))
                {
                    await using var ins = Command(conn, @"
INSERT INTO auth.UserAttribute (UserId, AttributeKey, AttributeValue, Source)
SELECT @id, @k, @v, 'Manual' WHERE NOT EXISTS (SELECT 1 FROM auth.UserAttribute x WHERE x.UserId = @id AND x.AttributeKey = @k AND x.AttributeValue = @v)", tx,
                        P("@id", SqlDbType.UniqueIdentifier, userId), P("@k", SqlDbType.VarChar, key, 100), P("@v", SqlDbType.NVarChar, value, 200));
                    await ins.ExecuteNonQueryAsync(ct);
                }
            }
        }

        /// <summary>Active users holding the Administrator role (guards against deleting the last one).</summary>
        public async Task<int> CountActiveAdministratorsAsync(CancellationToken ct = default)
        {
            await using var conn = await OpenAsync(ct);
            await using var cmd = Command(conn, @"
SELECT COUNT(DISTINCT u.UserId)
FROM auth.AppUser u
JOIN auth.UserRole ur ON ur.UserId = u.UserId
JOIN auth.Role r ON r.RoleId = ur.RoleId
WHERE r.RoleName = N'Administrator' AND u.IsActive = 1");
            return Convert.ToInt32(await cmd.ExecuteScalarAsync(ct) ?? 0);
        }

        /// <summary>
        /// Permanently deletes a portal account; its roles and attributes go with it (ON DELETE CASCADE).
        /// Audit entries keep the user name. Returns false when the user doesn't exist.
        /// </summary>
        public async Task<bool> DeleteUserAsync(Guid userId, CancellationToken ct = default)
        {
            await using var conn = await OpenAsync(ct);
            await using var cmd = Command(conn, "DELETE FROM auth.AppUser WHERE UserId = @id", null, P("@id", SqlDbType.UniqueIdentifier, userId));
            return await cmd.ExecuteNonQueryAsync(ct) > 0;
        }

        /// <summary>
        /// Permanently deletes correspondence records and their status history in one transaction.
        /// Returns the number of CorrespondenceRequest rows removed.
        /// </summary>
        public async Task<int> DeleteCorrespondenceAsync(IReadOnlyCollection<Guid> correspondenceIds, CancellationToken ct = default)
        {
            if (correspondenceIds.Count == 0) return 0;
            await using var conn = await OpenAsync(ct);
            await using var tx = (SqlTransaction)await conn.BeginTransactionAsync(ct);
            var names = new List<string>();
            var parameters = new List<SqlParameter>();
            var i = 0;
            foreach (var id in correspondenceIds)
            {
                var name = "@c" + i++;
                names.Add(name);
                parameters.Add(P(name, SqlDbType.UniqueIdentifier, id));
            }
            var idList = string.Join(",", names);

            await using (var history = Command(conn, $"DELETE FROM dbo.CorrespondenceRequestStatusHistory WHERE CorrespondenceId IN ({idList})", tx,
                parameters.Select(Clone).ToArray()))
            {
                await history.ExecuteNonQueryAsync(ct);
            }
            int deleted;
            await using (var requests = Command(conn, $"DELETE FROM dbo.CorrespondenceRequest WHERE CorrespondenceId IN ({idList})", tx,
                parameters.Select(Clone).ToArray()))
            {
                deleted = await requests.ExecuteNonQueryAsync(ct);
            }
            await tx.CommitAsync(ct);
            return deleted;

            static SqlParameter Clone(SqlParameter p) => new(p.ParameterName, p.SqlDbType) { Value = p.Value };
        }

        public async Task UnlockUserAsync(Guid userId, CancellationToken ct = default)
        {
            await using var conn = await OpenAsync(ct);
            await using var cmd = Command(conn, "UPDATE auth.AppUser SET FailedLoginCount = 0, LockoutEndUtc = NULL WHERE UserId = @id", null,
                P("@id", SqlDbType.UniqueIdentifier, userId));
            await cmd.ExecuteNonQueryAsync(ct);
        }

        public async Task<List<RoleDto>> ListRolesAsync(CancellationToken ct = default)
        {
            const string sql = @"
SELECT r.RoleId, r.RoleName, r.Description, r.IsSystem, (SELECT COUNT(1) FROM auth.UserRole ur WHERE ur.RoleId = r.RoleId) AS UserCount
FROM auth.Role r ORDER BY r.RoleName;
SELECT rp.RoleId, p.PermissionKey FROM auth.RolePermission rp JOIN auth.Permission p ON p.PermissionId = rp.PermissionId;";
            var roles = new List<(int Id, string Name, string? Desc, bool System, int Users)>();
            var perms = new Dictionary<int, List<string>>();
            await using var conn = await OpenAsync(ct);
            await using var cmd = Command(conn, sql);
            await using var r = await cmd.ExecuteReaderAsync(ct);
            while (await r.ReadAsync(ct)) roles.Add((r.GetInt32(0), r.GetString(1), r.IsDBNull(2) ? null : r.GetString(2), r.GetBoolean(3), r.GetInt32(4)));
            await r.NextResultAsync(ct);
            while (await r.ReadAsync(ct))
            {
                var id = r.GetInt32(0);
                if (!perms.TryGetValue(id, out var list)) perms[id] = list = new List<string>();
                list.Add(r.GetString(1));
            }
            return roles.Select(x => new RoleDto
            {
                RoleId = x.Id, RoleName = x.Name, Description = x.Desc, IsSystem = x.System, UserCount = x.Users,
                Permissions = perms.TryGetValue(x.Id, out var p) ? p.OrderBy(k => k).ToList() : new List<string>()
            }).ToList();
        }

        public async Task<bool> SetRolePermissionsAsync(int roleId, IEnumerable<string> permissionKeys, CancellationToken ct = default)
        {
            await using var conn = await OpenAsync(ct);
            await using var tx = (SqlTransaction)await conn.BeginTransactionAsync(ct);
            await using (var exists = Command(conn, "SELECT COUNT(1) FROM auth.Role WHERE RoleId = @id", tx, P("@id", SqlDbType.Int, roleId)))
            {
                if (Convert.ToInt32(await exists.ExecuteScalarAsync(ct) ?? 0) == 0) { await tx.RollbackAsync(ct); return false; }
            }
            await using (var del = Command(conn, "DELETE FROM auth.RolePermission WHERE RoleId = @id", tx, P("@id", SqlDbType.Int, roleId)))
            {
                await del.ExecuteNonQueryAsync(ct);
            }
            foreach (var key in permissionKeys.Distinct(StringComparer.OrdinalIgnoreCase))
            {
                await using var ins = Command(conn, @"
INSERT INTO auth.RolePermission (RoleId, PermissionId) SELECT @id, PermissionId FROM auth.Permission WHERE PermissionKey = @k", tx,
                    P("@id", SqlDbType.Int, roleId), P("@k", SqlDbType.VarChar, key, 100));
                await ins.ExecuteNonQueryAsync(ct);
            }
            // Members' tokens carry no permissions (they're re-read per request), but rotate stamps so caches drop them.
            await using (var stamp = Command(conn, @"
UPDATE auth.AppUser SET SecurityStamp = NEWID() WHERE UserId IN (SELECT UserId FROM auth.UserRole WHERE RoleId = @id)", tx,
                P("@id", SqlDbType.Int, roleId)))
            {
                await stamp.ExecuteNonQueryAsync(ct);
            }
            await tx.CommitAsync(ct);
            return true;
        }

        public async Task<List<PermissionDto>> ListPermissionsAsync(CancellationToken ct = default)
        {
            var list = new List<PermissionDto>();
            await using var conn = await OpenAsync(ct);
            await using var cmd = Command(conn, "SELECT PermissionKey, Category, Description FROM auth.Permission ORDER BY Category, PermissionKey");
            await using var r = await cmd.ExecuteReaderAsync(ct);
            while (await r.ReadAsync(ct))
                list.Add(new PermissionDto { PermissionKey = r.GetString(0), Category = r.GetString(1), Description = r.IsDBNull(2) ? null : r.GetString(2) });
            return list;
        }

        public async Task<List<AttributeDefinitionDto>> ListAttributeDefinitionsAsync(CancellationToken ct = default)
        {
            var list = new List<AttributeDefinitionDto>();
            await using var conn = await OpenAsync(ct);
            await using var cmd = Command(conn, "SELECT AttributeKey, DisplayName, DataType, Description FROM auth.AttributeDefinition ORDER BY AttributeKey");
            await using var r = await cmd.ExecuteReaderAsync(ct);
            while (await r.ReadAsync(ct))
                list.Add(new AttributeDefinitionDto { AttributeKey = r.GetString(0), DisplayName = r.GetString(1), DataType = r.GetString(2), Description = r.IsDBNull(3) ? null : r.GetString(3) });
            return list;
        }

        public async Task<bool> UpdatePolicyAsync(int policyId, bool isActive, string conditionJson, string? denyMessage, CancellationToken ct = default)
        {
            await using var conn = await OpenAsync(ct);
            await using var cmd = Command(conn, @"
UPDATE auth.AccessPolicy SET IsActive = @a, ConditionJson = @c, DenyMessage = @m, UpdatedUtc = SYSUTCDATETIME() WHERE PolicyId = @id", null,
                P("@id", SqlDbType.Int, policyId), P("@a", SqlDbType.Bit, isActive),
                P("@c", SqlDbType.NVarChar, conditionJson, -1), P("@m", SqlDbType.NVarChar, denyMessage, 400));
            return await cmd.ExecuteNonQueryAsync(ct) > 0;
        }

        // ================= The account menu =================

        /// <summary>
        /// The sign-in before the latest one (the latest is normally this session) and the failed attempts since it,
        /// from the audit log, so the user can spot a sign-in that wasn't them.
        /// </summary>
        public async Task<(DateTime? PreviousUtc, string? PreviousIp, string? PreviousDetail, int FailedSince)> SignInHistoryAsync(Guid userId, CancellationToken ct = default)
        {
            var rows = new List<(DateTime At, string? Ip, string? Detail)>();
            await using var conn = await OpenAsync(ct);
            await using (var cmd = Command(conn, @"
SELECT TOP (2) OccurredUtc, IpAddress, Detail FROM auth.AuthAuditLog
WHERE UserId = @id AND EventType = 'LoginSucceeded' ORDER BY OccurredUtc DESC", null, P("@id", SqlDbType.UniqueIdentifier, userId)))
            await using (var r = await cmd.ExecuteReaderAsync(ct))
            {
                while (await r.ReadAsync(ct)) rows.Add((r.GetDateTime(0), r.IsDBNull(1) ? null : r.GetString(1), r.IsDBNull(2) ? null : r.GetString(2)));
            }
            if (rows.Count < 2) return (null, null, null, 0);
            var previous = rows[1];
            await using var count = Command(conn, @"
SELECT COUNT(*) FROM auth.AuthAuditLog WHERE UserId = @id AND EventType IN ('LoginFailed', 'LockedOut') AND OccurredUtc > @since",
                null, P("@id", SqlDbType.UniqueIdentifier, userId), P("@since", SqlDbType.DateTime2, previous.At));
            var failed = Convert.ToInt32(await count.ExecuteScalarAsync(ct) ?? 0);
            return (DateTime.SpecifyKind(previous.At, DateTimeKind.Utc), previous.Ip, previous.Detail, failed);
        }

        /// <summary>Rotates the security stamp: every token issued so far stops working (all sessions end).</summary>
        public async Task RotateSecurityStampAsync(Guid userId, CancellationToken ct = default)
        {
            await using var conn = await OpenAsync(ct);
            await using var cmd = Command(conn, "UPDATE auth.AppUser SET SecurityStamp = NEWID(), UpdatedUtc = SYSUTCDATETIME() WHERE UserId = @id",
                null, P("@id", SqlDbType.UniqueIdentifier, userId));
            await cmd.ExecuteNonQueryAsync(ct);
        }

        /// <summary>The user's saved preferences JSON, or null (none saved, or 006_PortalAuth_UserPreference.sql not run).</summary>
        public async Task<string?> GetPreferencesAsync(Guid userId, CancellationToken ct = default)
        {
            try
            {
                await using var conn = await OpenAsync(ct);
                await using var cmd = Command(conn, "SELECT PreferencesJson FROM auth.UserPreference WHERE UserId = @id", null, P("@id", SqlDbType.UniqueIdentifier, userId));
                return await cmd.ExecuteScalarAsync(ct) as string;
            }
            catch (SqlException ex) when (ex.Number == 208) { return null; }
        }

        /// <summary>Saves the preferences JSON. False when the table hasn't been created yet.</summary>
        public async Task<bool> SavePreferencesAsync(Guid userId, string json, CancellationToken ct = default)
        {
            try
            {
                await using var conn = await OpenAsync(ct);
                await using var cmd = Command(conn, @"
UPDATE auth.UserPreference SET PreferencesJson = @json, UpdatedUtc = SYSUTCDATETIME() WHERE UserId = @id;
IF @@ROWCOUNT = 0 INSERT INTO auth.UserPreference (UserId, PreferencesJson) VALUES (@id, @json);", null,
                    P("@id", SqlDbType.UniqueIdentifier, userId), P("@json", SqlDbType.NVarChar, json, 2000));
                await cmd.ExecuteNonQueryAsync(ct);
                return true;
            }
            catch (SqlException ex) when (ex.Number == 208) { return false; }
        }

        // ================= Audit =================

        public Task WriteAuditAsync(Guid? userId, string? userName, string eventType, string outcome, string? detail, string? ip) =>
            WriteAuditAsync(userId, userName, eventType, outcome, detail, ip, null);

        /// <summary>Writes an audit entry; <paramref name="resourceId"/> links it to a correspondence record (record activity).</summary>
        public async Task WriteAuditAsync(Guid? userId, string? userName, string eventType, string outcome, string? detail, string? ip, Guid? resourceId)
        {
            try
            {
                await using var conn = await OpenAsync();
                await using var cmd = Command(conn, @"
INSERT INTO auth.AuthAuditLog (UserId, UserName, EventType, Outcome, Detail, IpAddress, ResourceId) VALUES (@id, @u, @t, @o, @d, @ip, @r)", null,
                    P("@id", SqlDbType.UniqueIdentifier, userId), P("@u", SqlDbType.NVarChar, Truncate(userName, 100), 100),
                    P("@t", SqlDbType.VarChar, eventType, 50), P("@o", SqlDbType.VarChar, outcome, 20),
                    P("@d", SqlDbType.NVarChar, Truncate(detail, 1000), 1000), P("@ip", SqlDbType.VarChar, Truncate(ip, 64), 64),
                    P("@r", SqlDbType.UniqueIdentifier, resourceId));
                await cmd.ExecuteNonQueryAsync();
            }
            catch (SqlException)
            {
                // Auditing must never break the request it describes.
            }
        }

        /// <summary>Audit entries about one correspondence record, newest first.</summary>
        public async Task<List<AuditEntryDto>> ListAuditForResourceAsync(Guid resourceId, int top, CancellationToken ct = default)
        {
            const string sql = @"
SELECT TOP (@top) AuditId, OccurredUtc, UserName, EventType, Outcome, Detail, IpAddress
FROM auth.AuthAuditLog
WHERE ResourceId = @rid OR (ResourceId IS NULL AND Detail LIKE '%' + @ridText + '%')
ORDER BY OccurredUtc DESC, AuditId DESC";
            var list = new List<AuditEntryDto>();
            await using var conn = await OpenAsync(ct);
            await using var cmd = Command(conn, sql, null, P("@top", SqlDbType.Int, Math.Clamp(top, 1, 500)),
                P("@rid", SqlDbType.UniqueIdentifier, resourceId), P("@ridText", SqlDbType.NVarChar, resourceId.ToString(), 40));
            await using var r = await cmd.ExecuteReaderAsync(ct);
            while (await r.ReadAsync(ct))
            {
                list.Add(new AuditEntryDto
                {
                    AuditId = r.GetInt64(0), OccurredUtc = r.GetDateTime(1), UserName = r.IsDBNull(2) ? null : r.GetString(2),
                    EventType = r.GetString(3), Outcome = r.GetString(4), Detail = r.IsDBNull(5) ? null : r.GetString(5),
                    IpAddress = r.IsDBNull(6) ? null : r.GetString(6)
                });
            }
            return list;
        }

        // ================= Single sign-on (Entra ID) =================

        public const string GroupSource = "Group";
        public const string EntraProvider = "Entra";

        public async Task<Guid?> FindExternalUserAsync(string provider, string objectId, CancellationToken ct = default)
        {
            await using var conn = await OpenAsync(ct);
            await using var cmd = Command(conn, "SELECT UserId FROM auth.AppUser WHERE ExternalProvider = @p AND ExternalObjectId = @o", null,
                P("@p", SqlDbType.VarChar, provider, 20), P("@o", SqlDbType.NVarChar, objectId, 100));
            var v = await cmd.ExecuteScalarAsync(ct);
            return v is Guid g ? g : null;
        }

        /// <summary>
        /// Creates a password-less account for an Entra identity on its first sign-in. The account is keyed by the
        /// immutable object id; it never takes over an existing local account (user names are not a safe link).
        /// Returns null when a local account already uses this user name.
        /// </summary>
        public async Task<Guid?> ProvisionExternalUserAsync(string provider, string objectId, string tenantId, string userName,
            string displayName, string? email, CancellationToken ct = default)
        {
            await using var conn = await OpenAsync(ct);
            await using var ins = Command(conn, @"
INSERT INTO auth.AppUser (UserName, DisplayName, Email, ExternalProvider, ExternalObjectId, ExternalTenantId, MustChangePassword, IsActive)
OUTPUT inserted.UserId
VALUES (@u, @d, @e, @p, @o, @t, 0, 1)", null,
                P("@u", SqlDbType.NVarChar, userName, 100), P("@d", SqlDbType.NVarChar, displayName, 200), P("@e", SqlDbType.NVarChar, email, 256),
                P("@p", SqlDbType.VarChar, provider, 20), P("@o", SqlDbType.NVarChar, objectId, 100), P("@t", SqlDbType.NVarChar, tenantId, 100));
            try
            {
                return (Guid)(await ins.ExecuteScalarAsync(ct))!;
            }
            catch (SqlException ex) when (ex.Number is 2601 or 2627)
            {
                // Same object id provisioned concurrently: use it. Same user name on a local account: refuse.
                var existing = await FindExternalUserAsync(provider, objectId, ct);
                return existing;
            }
        }

        /// <summary>Keeps name and email in step with the directory (no stamp change: not access-relevant).</summary>
        public async Task UpdateExternalProfileAsync(Guid userId, string displayName, string? email, CancellationToken ct = default)
        {
            await using var conn = await OpenAsync(ct);
            await using var cmd = Command(conn, "UPDATE auth.AppUser SET DisplayName = @d, Email = @e WHERE UserId = @id AND (DisplayName <> @d OR ISNULL(Email, '') <> ISNULL(@e, ''))", null,
                P("@id", SqlDbType.UniqueIdentifier, userId), P("@d", SqlDbType.NVarChar, displayName, 200), P("@e", SqlDbType.NVarChar, email, 256));
            await cmd.ExecuteNonQueryAsync(ct);
        }

        /// <summary>
        /// Replaces the user's group-sourced roles and attributes with what the token's groups / app roles map to.
        /// Rotates the security stamp only when effective access actually changed. Returns true when it changed.
        /// </summary>
        public async Task<bool> SyncGroupGrantsAsync(Guid userId, IReadOnlyCollection<string> groupIds, IReadOnlyCollection<string> appRoles, CancellationToken ct = default)
        {
            var claims = groupIds.Select(g => ("group", g)).Concat(appRoles.Select(r => ("appRole", r))).ToList();
            var claimsJson = System.Text.Json.JsonSerializer.Serialize(claims.Select(c => new { t = c.Item1, v = c.Item2 }));
            const string sql = @"
DECLARE @claims TABLE (ClaimType VARCHAR(10), ClaimValue NVARCHAR(200));
INSERT @claims SELECT t, v FROM OPENJSON(@json) WITH (t VARCHAR(10) '$.t', v NVARCHAR(200) '$.v');

DECLARE @wantRoles TABLE (RoleId INT PRIMARY KEY);
INSERT @wantRoles SELECT DISTINCT m.RoleId FROM auth.ExternalGroupMapping m JOIN @claims c ON c.ClaimType = m.ClaimType AND c.ClaimValue = m.ClaimValue WHERE m.RoleId IS NOT NULL;
DECLARE @wantAttrs TABLE (AttributeKey VARCHAR(100), AttributeValue NVARCHAR(200), PRIMARY KEY (AttributeKey, AttributeValue));
INSERT @wantAttrs SELECT DISTINCT m.AttributeKey, m.AttributeValue FROM auth.ExternalGroupMapping m JOIN @claims c ON c.ClaimType = m.ClaimType AND c.ClaimValue = m.ClaimValue WHERE m.AttributeKey IS NOT NULL;

DECLARE @changes INT = 0;
DELETE ur FROM auth.UserRole ur WHERE ur.UserId = @id AND ur.Source = 'Group' AND ur.RoleId NOT IN (SELECT RoleId FROM @wantRoles);
SET @changes += @@ROWCOUNT;
INSERT auth.UserRole (UserId, RoleId, Source) SELECT @id, w.RoleId, 'Group' FROM @wantRoles w
WHERE NOT EXISTS (SELECT 1 FROM auth.UserRole x WHERE x.UserId = @id AND x.RoleId = w.RoleId);
SET @changes += @@ROWCOUNT;
DELETE ua FROM auth.UserAttribute ua WHERE ua.UserId = @id AND ua.Source = 'Group'
  AND NOT EXISTS (SELECT 1 FROM @wantAttrs w WHERE w.AttributeKey = ua.AttributeKey AND w.AttributeValue = ua.AttributeValue);
SET @changes += @@ROWCOUNT;
INSERT auth.UserAttribute (UserId, AttributeKey, AttributeValue, Source) SELECT @id, w.AttributeKey, w.AttributeValue, 'Group' FROM @wantAttrs w
WHERE NOT EXISTS (SELECT 1 FROM auth.UserAttribute x WHERE x.UserId = @id AND x.AttributeKey = w.AttributeKey AND x.AttributeValue = w.AttributeValue);
SET @changes += @@ROWCOUNT;
IF @changes > 0 UPDATE auth.AppUser SET SecurityStamp = NEWID(), UpdatedUtc = SYSUTCDATETIME() WHERE UserId = @id;
SELECT @changes;";
            await using var conn = await OpenAsync(ct);
            await using var tx = (SqlTransaction)await conn.BeginTransactionAsync(ct);
            await using var cmd = Command(conn, sql, tx, P("@id", SqlDbType.UniqueIdentifier, userId), P("@json", SqlDbType.NVarChar, claimsJson, -1));
            var changes = Convert.ToInt32(await cmd.ExecuteScalarAsync(ct) ?? 0);
            await tx.CommitAsync(ct);
            return changes > 0;
        }

        public async Task<List<GroupMappingDto>> ListGroupMappingsAsync(CancellationToken ct = default)
        {
            var list = new List<GroupMappingDto>();
            await using var conn = await OpenAsync(ct);
            await using var cmd = Command(conn, @"
SELECT m.MappingId, m.ClaimType, m.ClaimValue, m.DisplayName, r.RoleName, m.AttributeKey, m.AttributeValue
FROM auth.ExternalGroupMapping m LEFT JOIN auth.Role r ON r.RoleId = m.RoleId
ORDER BY ISNULL(m.DisplayName, m.ClaimValue), r.RoleName, m.AttributeKey");
            await using var r = await cmd.ExecuteReaderAsync(ct);
            while (await r.ReadAsync(ct))
            {
                list.Add(new GroupMappingDto
                {
                    MappingId = r.GetInt32(0), ClaimType = r.GetString(1), ClaimValue = r.GetString(2), DisplayName = r.IsDBNull(3) ? null : r.GetString(3),
                    RoleName = r.IsDBNull(4) ? null : r.GetString(4), AttributeKey = r.IsDBNull(5) ? null : r.GetString(5),
                    AttributeValue = r.IsDBNull(6) ? null : r.GetString(6)
                });
            }
            return list;
        }

        public async Task<int> AddGroupMappingAsync(string claimType, string claimValue, string? displayName, string? roleName,
            string? attributeKey, string? attributeValue, CancellationToken ct = default)
        {
            await using var conn = await OpenAsync(ct);
            await using var cmd = Command(conn, @"
INSERT INTO auth.ExternalGroupMapping (ClaimType, ClaimValue, DisplayName, RoleId, AttributeKey, AttributeValue)
OUTPUT inserted.MappingId
VALUES (@t, @v, @d, (SELECT RoleId FROM auth.Role WHERE RoleName = @role), @k, @av)", null,
                P("@t", SqlDbType.VarChar, claimType, 10), P("@v", SqlDbType.NVarChar, claimValue, 200), P("@d", SqlDbType.NVarChar, displayName, 200),
                P("@role", SqlDbType.NVarChar, roleName, 100), P("@k", SqlDbType.VarChar, attributeKey, 100), P("@av", SqlDbType.NVarChar, attributeValue, 200));
            return (int)(await cmd.ExecuteScalarAsync(ct))!;
        }

        public async Task<bool> DeleteGroupMappingAsync(int mappingId, CancellationToken ct = default)
        {
            await using var conn = await OpenAsync(ct);
            await using var cmd = Command(conn, "DELETE FROM auth.ExternalGroupMapping WHERE MappingId = @id", null, P("@id", SqlDbType.Int, mappingId));
            return await cmd.ExecuteNonQueryAsync(ct) > 0;
        }

        public async Task<List<AuditEntryDto>> ListAuditAsync(int top, string? userName, CancellationToken ct = default)
        {
            var sql = "SELECT TOP (@top) AuditId, OccurredUtc, UserName, EventType, Outcome, Detail, IpAddress FROM auth.AuthAuditLog"
                      + (string.IsNullOrWhiteSpace(userName) ? "" : " WHERE UserName = @u")
                      + " ORDER BY OccurredUtc DESC, AuditId DESC";
            var list = new List<AuditEntryDto>();
            await using var conn = await OpenAsync(ct);
            await using var cmd = Command(conn, sql, null, P("@top", SqlDbType.Int, Math.Clamp(top, 1, 1000)), P("@u", SqlDbType.NVarChar, userName, 100));
            await using var r = await cmd.ExecuteReaderAsync(ct);
            while (await r.ReadAsync(ct))
            {
                list.Add(new AuditEntryDto
                {
                    AuditId = r.GetInt64(0), OccurredUtc = r.GetDateTime(1), UserName = r.IsDBNull(2) ? null : r.GetString(2),
                    EventType = r.GetString(3), Outcome = r.GetString(4), Detail = r.IsDBNull(5) ? null : r.GetString(5),
                    IpAddress = r.IsDBNull(6) ? null : r.GetString(6)
                });
            }
            return list;
        }

        // ================= Correspondence resources (ABAC inputs) =================

        /// <summary>Attributes of one correspondence record for policy evaluation, or null when it doesn't exist.</summary>
        public async Task<Dictionary<string, string?>?> GetCorrespondenceResourceAsync(Guid correspondenceId, CancellationToken ct = default)
        {
            const string sql = @"
SELECT cr.RequestingDomainName,
       COALESCE(dm.DeliveryMethodName, cr.DeliveryMethodCode) AS DeliveryMethod,
       COALESCE(sc.StatusDescription, ls.LatestStatus) AS LatestStatus
FROM dbo.CorrespondenceRequest cr
OUTER APPLY (SELECT TOP (1) h.StatusCode AS LatestStatus FROM dbo.CorrespondenceRequestStatusHistory h
             WHERE h.CorrespondenceId = cr.CorrespondenceId ORDER BY h.HistorySequenceNum DESC, h.StatusUpdateTS DESC) ls
LEFT JOIN dbo.CorrespondenceDeliveryMethod dm ON dm.DeliveryMethodCode = cr.DeliveryMethodCode
LEFT JOIN dbo.CorrespondenceStatusCode sc ON sc.StatusCode = ls.LatestStatus
WHERE cr.CorrespondenceId = @id";
            await using var conn = await OpenAsync(ct);
            await using var cmd = Command(conn, sql, null, P("@id", SqlDbType.UniqueIdentifier, correspondenceId));
            await using var r = await cmd.ExecuteReaderAsync(ct);
            if (!await r.ReadAsync(ct)) return null;
            return new Dictionary<string, string?>(StringComparer.OrdinalIgnoreCase)
            {
                ["correspondenceId"] = correspondenceId.ToString(),
                ["customer"] = r.IsDBNull(0) ? null : r.GetString(0),
                ["deliveryMethod"] = r.IsDBNull(1) ? null : r.GetString(1),
                ["status"] = r.IsDBNull(2) ? null : r.GetString(2)
            };
        }

        /// <summary>Distinct customers / delivery methods (for scope pickers and grid filter lists).</summary>
        public async Task<List<string>> ListDistinctValuesAsync(string field, CancellationToken ct = default)
        {
            var sql = field switch
            {
                "customer" => "SELECT DISTINCT RequestingDomainName FROM dbo.CorrespondenceRequest WHERE RequestingDomainName IS NOT NULL ORDER BY 1",
                "delivery" => @"SELECT DISTINCT COALESCE(dm.DeliveryMethodName, cr.DeliveryMethodCode) FROM dbo.CorrespondenceRequest cr
LEFT JOIN dbo.CorrespondenceDeliveryMethod dm ON dm.DeliveryMethodCode = cr.DeliveryMethodCode
WHERE cr.DeliveryMethodCode IS NOT NULL ORDER BY 1",
                _ => throw new ArgumentOutOfRangeException(nameof(field))
            };
            var list = new List<string>();
            await using var conn = await OpenAsync(ct);
            await using var cmd = Command(conn, sql);
            await using var r = await cmd.ExecuteReaderAsync(ct);
            while (await r.ReadAsync(ct)) if (!r.IsDBNull(0)) list.Add(r.GetString(0));
            return list;
        }

        private static string? Truncate(string? s, int max) => s == null || s.Length <= max ? s : s.Substring(0, max);
    }
}
