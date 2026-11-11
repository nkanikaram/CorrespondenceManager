namespace ProcessRequestEndPoint.Auth
{
    public static class PortalAuthDefaults
    {
        /// <summary>Authentication scheme for tokens issued by <see cref="TokenService"/> to portal users.</summary>
        public const string Scheme = "EccoPortal";

        public const string SecurityStampClaim = "ecco:stamp";
        public const string PermissionPolicyPrefix = "perm:";
    }

    /// <summary>"PortalAuth" configuration section.</summary>
    public class PortalAuthOptions
    {
        public const string SectionName = "PortalAuth";

        public string Issuer { get; set; } = "ecco-correspondence-portal";
        public string Audience { get; set; } = "ecco-correspondence-portal";

        /// <summary>
        /// Base64 HMAC-SHA256 signing key (at least 32 bytes). Required outside Development; supply it as an
        /// App Service setting / Key Vault reference (PortalAuth__SigningKey), never in a committed file.
        /// </summary>
        public string? SigningKey { get; set; }

        public int TokenLifetimeMinutes { get; set; } = 60;
        public int LockoutThreshold { get; set; } = 5;
        public int LockoutMinutes { get; set; } = 15;
        public int PasswordIterations { get; set; } = 600_000;
        public int MinPasswordLength { get; set; } = 12;

        /// <summary>How long a user's roles/permissions/attributes are cached before being re-read.</summary>
        public int AccessProfileCacheSeconds { get; set; } = 60;

        /// <summary>Creates the first administrator when the user table is empty (any environment).</summary>
        public BootstrapAdminOptions BootstrapAdmin { get; set; } = new();

        /// <summary>Microsoft Entra ID single sign-on.</summary>
        public EntraOptions Entra { get; set; } = new();

        /// <summary>Local user name + password sign-in (keep a break-glass administrator when SSO is on).</summary>
        public LocalLoginOptions LocalLogin { get; set; } = new();

        /// <summary>"Forgot password?" self-service reset by emailed link (local accounts only).</summary>
        public PasswordResetOptions PasswordReset { get; set; } = new();

        /// <summary>Help links in the account menu ("User guide", "Report a problem"); each is hidden while unset.</summary>
        public SupportOptions Support { get; set; } = new();

        /// <summary>Connection string name; defaults to the one CorrespondenceSearchController already uses.</summary>
        public string ConnectionStringName { get; set; } = "AZURE_SQL_CONNECTION";
    }

    public class SupportOptions
    {
        /// <summary>Where "Report a problem" emails go (e.g. the service desk's address).</summary>
        public string? Email { get; set; }

        /// <summary>The user guide (e.g. a SharePoint or Confluence page).</summary>
        public string? GuideUrl { get; set; }

        /// <summary>Shown next to the version, e.g. "Production". Defaults to the hosting environment's name.</summary>
        public string? EnvironmentName { get; set; }
    }

    public class EntraOptions
    {
        public bool Enabled { get; set; }

        /// <summary>Directory (tenant) id; only users of this tenant can sign in.</summary>
        public string? TenantId { get; set; }

        /// <summary>Application (client) id of the portal's app registration (SPA platform, redirect URI = the portal URL).</summary>
        public string? ClientId { get; set; }

        public string Instance { get; set; } = "https://login.microsoftonline.com/";

        /// <summary>
        /// Refuse sign-ins whose token doesn't show multi-factor authentication (the "amr" claim). Enforce MFA itself
        /// with an Entra Conditional Access policy; this makes the API refuse anything that slipped past it.
        /// </summary>
        public bool RequireMfa { get; set; } = true;

        /// <summary>amr values that count as MFA ("mfa" covers MFA and passwordless methods in Entra tokens).</summary>
        public List<string> MfaClaimValues { get; set; } = new() { "mfa" };

        /// <summary>An ID token older than this can't be exchanged (limits replay of a leaked token).</summary>
        public int MaxTokenAgeMinutes { get; set; } = 10;

        /// <summary>Create a portal account on first sign-in. Off = an administrator must add the user first.</summary>
        public bool AutoProvision { get; set; } = true;

        public string Authority => Instance.TrimEnd('/') + "/" + TenantId + "/v2.0";
    }

    public class LocalLoginOptions
    {
        public bool Enabled { get; set; } = true;

        /// <summary>When non-empty, only these local accounts may sign in with a password (e.g. a break-glass admin).</summary>
        public List<string> AllowedUserNames { get; set; } = new();
    }

    public class PasswordResetOptions
    {
        public bool Enabled { get; set; } = true;

        /// <summary>How long an emailed reset link works.</summary>
        public int LinkLifetimeMinutes { get; set; } = 30;

        /// <summary>Reset emails one account can be sent per hour (further requests are silently ignored).</summary>
        public int MaxRequestsPerHour { get; set; } = 3;

        /// <summary>Requests one IP address can make per hour, whether or not the account exists.</summary>
        public int MaxRequestsPerIpPerHour { get; set; } = 20;

        /// <summary>
        /// The portal's public address, used to build the link (e.g. https://portal.example.com/). Falls back to
        /// Alerts:PortalUrl. Never taken from the request, so a forged Host header can't redirect the link.
        /// </summary>
        public string? PortalUrl { get; set; }

        /// <summary>
        /// Development only: with no SMTP server configured, write the reset link to the API log instead of emailing it.
        /// </summary>
        public bool LogLinkInDevelopment { get; set; } = true;
    }

    public class BootstrapAdminOptions
    {
        public string? UserName { get; set; }
        public string? Password { get; set; }
        public string DisplayName { get; set; } = "Portal Administrator";
    }
}
