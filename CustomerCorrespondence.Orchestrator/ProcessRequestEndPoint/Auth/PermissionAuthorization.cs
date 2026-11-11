using Microsoft.AspNetCore.Authorization;
using Microsoft.Extensions.Options;

namespace ProcessRequestEndPoint.Auth
{
    /// <summary>
    /// [RequirePermission("correspondence.read")] — RBAC gate: the caller must be a signed-in,
    /// active portal user whose roles grant the permission. Attribute-based rules that depend on
    /// a specific record are checked inside the action with <see cref="PortalAccessService.CheckAsync"/>.
    /// </summary>
    [AttributeUsage(AttributeTargets.Class | AttributeTargets.Method, AllowMultiple = true)]
    public sealed class RequirePermissionAttribute : AuthorizeAttribute
    {
        public RequirePermissionAttribute(string permission)
            : base(PortalAuthDefaults.PermissionPolicyPrefix + permission)
        {
            AuthenticationSchemes = PortalAuthDefaults.Scheme;
        }
    }

    /// <summary>[RequirePortalUser] — any signed-in, active portal user.</summary>
    [AttributeUsage(AttributeTargets.Class | AttributeTargets.Method)]
    public sealed class RequirePortalUserAttribute : AuthorizeAttribute
    {
        public RequirePortalUserAttribute() : base(PortalAuthDefaults.PermissionPolicyPrefix)
        {
            AuthenticationSchemes = PortalAuthDefaults.Scheme;
        }
    }

    public sealed class PermissionRequirement : IAuthorizationRequirement
    {
        public PermissionRequirement(string? permission) => Permission = permission;

        /// <summary>Null/empty = any active portal user.</summary>
        public string? Permission { get; }
    }

    /// <summary>Builds "perm:&lt;key&gt;" policies on demand; everything else goes to the default provider.</summary>
    public sealed class PermissionPolicyProvider : IAuthorizationPolicyProvider
    {
        private readonly DefaultAuthorizationPolicyProvider _fallback;

        public PermissionPolicyProvider(IOptions<AuthorizationOptions> options)
        {
            _fallback = new DefaultAuthorizationPolicyProvider(options);
        }

        public Task<AuthorizationPolicy?> GetPolicyAsync(string policyName)
        {
            if (!policyName.StartsWith(PortalAuthDefaults.PermissionPolicyPrefix, StringComparison.Ordinal))
                return _fallback.GetPolicyAsync(policyName);

            var permission = policyName.Substring(PortalAuthDefaults.PermissionPolicyPrefix.Length);
            var policy = new AuthorizationPolicyBuilder(PortalAuthDefaults.Scheme)
                .RequireAuthenticatedUser()
                .AddRequirements(new PermissionRequirement(permission))
                .Build();
            return Task.FromResult<AuthorizationPolicy?>(policy);
        }

        public Task<AuthorizationPolicy> GetDefaultPolicyAsync() => _fallback.GetDefaultPolicyAsync();

        public Task<AuthorizationPolicy?> GetFallbackPolicyAsync() => _fallback.GetFallbackPolicyAsync();
    }

    public sealed class PermissionHandler : AuthorizationHandler<PermissionRequirement>
    {
        private readonly PortalAccessService _access;

        public PermissionHandler(PortalAccessService access) => _access = access;

        protected override async Task HandleRequirementAsync(AuthorizationHandlerContext context, PermissionRequirement requirement)
        {
            var user = await _access.LoadValidatedAsync(context.User);
            if (user == null) return; // not a valid portal session -> 401/403

            // Until a forced password change is done, only the endpoints needed to do it are allowed.
            if (user.MustChangePassword && !string.IsNullOrEmpty(requirement.Permission)) return;

            if (string.IsNullOrEmpty(requirement.Permission) || user.Has(requirement.Permission))
            {
                context.Succeed(requirement);
                return;
            }

            await _access.AuditDeniedAsync(requirement.Permission, new PolicyDecision(false, null, null));
        }
    }
}
