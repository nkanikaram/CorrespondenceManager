namespace ProcessRequestEndPoint.Auth
{
    /// <summary>Well-known permission keys (rows of auth.Permission).</summary>
    public static class Permissions
    {
        public const string CorrespondenceRead = "correspondence.read";
        public const string PayloadRead = "correspondence.payload.read";
        public const string CorrespondenceCreate = "correspondence.create";
        public const string CorrespondenceResubmit = "correspondence.resubmit";
        public const string CorrespondenceExport = "correspondence.export";
        public const string CorrespondenceDelete = "correspondence.delete";
        public const string PdfGenerate = "correspondence.pdf.generate";
        public const string Recycle = "correspondence.recycle";
        public const string TemplatesManage = "letters.templates.manage";
        public const string AlertsRead = "alerts.read";
        public const string AnalyticsView = "analytics.view";
        public const string AiUse = "ai.use";
        public const string UsersManage = "admin.users.manage";
        public const string AuditRead = "admin.audit.read";
        public const string SlaManage = "sla.manage";
    }

    /// <summary>Well-known ABAC subject attribute keys (rows of auth.AttributeDefinition).</summary>
    public static class SubjectAttributes
    {
        public const string CustomerScope = "scope.customer";
        public const string DeliveryMethodScope = "scope.deliveryMethod";
        public const string PiiClearance = "piiClearance";
        public const string MaxExportRows = "maxExportRows";
        public const string Wildcard = "*";
    }

    /// <summary>Everything authorization needs about a signed-in user, loaded from the auth schema.</summary>
    public sealed class UserAccessProfile
    {
        public Guid UserId { get; init; }
        public string UserName { get; init; } = string.Empty;
        public string DisplayName { get; init; } = string.Empty;
        public string? Email { get; init; }
        public bool IsActive { get; init; }
        public bool MustChangePassword { get; init; }
        public Guid SecurityStamp { get; init; }
        public string? ExternalProvider { get; init; }
        public IReadOnlyList<string> Roles { get; init; } = Array.Empty<string>();
        public IReadOnlySet<string> Permissions { get; init; } = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        public IReadOnlyDictionary<string, IReadOnlyList<string>> Attributes { get; init; } =
            new Dictionary<string, IReadOnlyList<string>>(StringComparer.OrdinalIgnoreCase);

        public bool Has(string permission) => Permissions.Contains(permission);

        public IReadOnlyList<string> AttributeValues(string key) =>
            Attributes.TryGetValue(key, out var values) ? values : Array.Empty<string>();

        /// <summary>
        /// Values the user is limited to for a scope attribute, or null when unrestricted ("*").
        /// An empty list means the user can see nothing in that dimension.
        /// </summary>
        public IReadOnlyList<string>? ScopeValues(string key)
        {
            var values = AttributeValues(key);
            return values.Any(v => v == SubjectAttributes.Wildcard) ? null : values;
        }
    }

    /// <summary>auth.AppUser row including credentials (never leaves the server).</summary>
    public sealed class AppUserCredentials
    {
        public Guid UserId { get; init; }
        public string UserName { get; init; } = string.Empty;
        public byte[] PasswordHash { get; init; } = Array.Empty<byte>();
        public byte[] PasswordSalt { get; init; } = Array.Empty<byte>();
        public int PasswordIterations { get; init; }
        public bool IsActive { get; init; }
        public int FailedLoginCount { get; init; }
        public DateTime? LockoutEndUtc { get; init; }

        /// <summary>"Entra" for single sign-on accounts (they have no password); null for local accounts.</summary>
        public string? ExternalProvider { get; init; }

        public bool HasPassword => PasswordHash.Length > 0;
    }

    /// <summary>An account found by a "forgot password" request or a reset link (never leaves the server).</summary>
    public sealed class PasswordResetCandidate
    {
        public long? TokenId { get; init; }
        public Guid UserId { get; init; }
        public string UserName { get; init; } = string.Empty;
        public string DisplayName { get; init; } = string.Empty;
        public string? Email { get; init; }
        public bool IsActive { get; init; }
        public string? ExternalProvider { get; init; }
        public Guid SecurityStamp { get; init; }
    }

    /// <summary>An Entra group / app role → portal role and/or attribute value.</summary>
    public sealed class GroupMappingDto
    {
        public int MappingId { get; init; }
        public string ClaimType { get; init; } = "group";
        public string ClaimValue { get; init; } = string.Empty;
        public string? DisplayName { get; init; }
        public string? RoleName { get; init; }
        public string? AttributeKey { get; init; }
        public string? AttributeValue { get; init; }
    }

    public sealed class SaveGroupMappingRequest
    {
        public string? ClaimType { get; set; }
        public string? ClaimValue { get; set; }
        public string? DisplayName { get; set; }
        public string? RoleName { get; set; }
        public string? AttributeKey { get; set; }
        public string? AttributeValue { get; set; }
    }

    public sealed class EntraExchangeRequest
    {
        public string? IdToken { get; set; }
    }

    public sealed class AccessPolicyRecord
    {
        public int PolicyId { get; init; }
        public string PolicyName { get; init; } = string.Empty;
        public string? Description { get; init; }
        public string PermissionKey { get; init; } = string.Empty;
        public string Effect { get; init; } = "Deny";
        public string ConditionJson { get; init; } = "{}";
        public string? DenyMessage { get; init; }
        public int Priority { get; init; }
        public bool IsActive { get; init; }
        public DateTime UpdatedUtc { get; init; }
    }

    // ---------- API contracts ----------

    public sealed class LoginRequest
    {
        public string? UserName { get; set; }
        public string? Password { get; set; }
    }

    public sealed class ChangePasswordRequest
    {
        public string? CurrentPassword { get; set; }
        public string? NewPassword { get; set; }
    }

    public sealed class ForgotPasswordRequest
    {
        /// <summary>User name or the email address on the account.</summary>
        public string? UserNameOrEmail { get; set; }
    }

    public sealed class SelfServiceResetRequest
    {
        public string? Token { get; set; }
        public string? NewPassword { get; set; }
    }

    public sealed class UserSummaryDto
    {
        public Guid UserId { get; init; }
        public string UserName { get; init; } = string.Empty;
        public string DisplayName { get; init; } = string.Empty;
        public string? Email { get; init; }
        public bool IsActive { get; init; }
        public bool MustChangePassword { get; init; }
        public bool IsLockedOut { get; init; }
        public DateTime? LastLoginUtc { get; init; }
        public IReadOnlyList<string> Roles { get; init; } = Array.Empty<string>();
        public IReadOnlyDictionary<string, IReadOnlyList<string>> Attributes { get; init; } =
            new Dictionary<string, IReadOnlyList<string>>();

        /// <summary>"Entra" for single sign-on accounts.</summary>
        public string? ExternalProvider { get; init; }

        /// <summary>Roles / attribute values that come from Entra group mappings (read-only in the portal).</summary>
        public IReadOnlyList<string> GroupRoles { get; init; } = Array.Empty<string>();
        public IReadOnlyDictionary<string, IReadOnlyList<string>> GroupAttributes { get; init; } =
            new Dictionary<string, IReadOnlyList<string>>();
    }

    public sealed class SaveUserRequest
    {
        public string? UserName { get; set; }          // create only
        public string? DisplayName { get; set; }
        public string? Email { get; set; }
        public bool IsActive { get; set; } = true;
        public string? Password { get; set; }          // create only (temporary; user must change it)
        public List<string> Roles { get; set; } = new();
        public Dictionary<string, List<string>> Attributes { get; set; } = new();
    }

    public sealed class ResetPasswordRequest
    {
        public string? NewPassword { get; set; }
    }

    public sealed class RoleDto
    {
        public int RoleId { get; init; }
        public string RoleName { get; init; } = string.Empty;
        public string? Description { get; init; }
        public bool IsSystem { get; init; }
        public int UserCount { get; init; }
        public IReadOnlyList<string> Permissions { get; init; } = Array.Empty<string>();
    }

    public sealed class PermissionDto
    {
        public string PermissionKey { get; init; } = string.Empty;
        public string Category { get; init; } = string.Empty;
        public string? Description { get; init; }
    }

    public sealed class AttributeDefinitionDto
    {
        public string AttributeKey { get; init; } = string.Empty;
        public string DisplayName { get; init; } = string.Empty;
        public string DataType { get; init; } = "string";
        public string? Description { get; init; }
    }

    public sealed class UpdateRolePermissionsRequest
    {
        public List<string> Permissions { get; set; } = new();
    }

    public sealed class UpdatePolicyRequest
    {
        public bool IsActive { get; set; }
        public string? ConditionJson { get; set; }
        public string? DenyMessage { get; set; }
    }

    public sealed class AuditEntryDto
    {
        public long AuditId { get; init; }
        public DateTime OccurredUtc { get; init; }
        public string? UserName { get; init; }
        public string EventType { get; init; } = string.Empty;
        public string Outcome { get; init; } = string.Empty;
        public string? Detail { get; init; }
        public string? IpAddress { get; init; }
    }
}
