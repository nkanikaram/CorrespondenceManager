using System.Security.Claims;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Options;

namespace ProcessRequestEndPoint.Auth
{
    /// <summary>
    /// Per-request entry point for authorization decisions: loads the signed-in user's access
    /// profile (briefly cached) and evaluates RBAC + ABAC for an action on a resource.
    /// </summary>
    public class PortalAccessService
    {
        private const string PoliciesCacheKey = "portal-auth:policies";
        private readonly AuthRepository _repository;
        private readonly IMemoryCache _cache;
        private readonly IHttpContextAccessor _http;
        private readonly PortalAuthOptions _options;
        private UserAccessProfile? _current;
        private bool _loaded;

        public PortalAccessService(AuthRepository repository, IMemoryCache cache, IHttpContextAccessor http, IOptions<PortalAuthOptions> options)
        {
            _repository = repository;
            _cache = cache;
            _http = http;
            _options = options.Value;
        }

        // Bumped by Invalidate() with no user (e.g. role permissions changed) so every cached profile is
        // re-read, without clearing the shared IMemoryCache (which also holds Key Vault secrets).
        private static int _generation;

        private static string ProfileKey(Guid userId) => "portal-auth:profile:" + Volatile.Read(ref _generation) + ":" + userId;

        /// <summary>The current portal user's profile, or null (anonymous, OIDC caller, deactivated, or stale token).</summary>
        public async Task<UserAccessProfile?> CurrentAsync()
        {
            if (_loaded) return _current;
            _loaded = true;
            var principal = _http.HttpContext?.User;
            if (principal == null) return null;
            _current = await LoadValidatedAsync(principal);
            return _current;
        }

        /// <summary>Loads the profile for a token's principal and checks it is still valid (active, matching stamp).</summary>
        public async Task<UserAccessProfile?> LoadValidatedAsync(ClaimsPrincipal principal)
        {
            var userId = principal.PortalUserId();
            if (userId == null) return null;
            var profile = await GetProfileAsync(userId.Value);
            if (profile == null || !profile.IsActive) return null;
            var stamp = principal.FindFirstValue(PortalAuthDefaults.SecurityStampClaim);
            return Guid.TryParse(stamp, out var s) && s == profile.SecurityStamp ? profile : null;
        }

        public async Task<UserAccessProfile?> GetProfileAsync(Guid userId)
        {
            if (_cache.TryGetValue(ProfileKey(userId), out UserAccessProfile? cached)) return cached;
            var profile = await _repository.GetAccessProfileAsync(userId);
            _cache.Set(ProfileKey(userId), profile, TimeSpan.FromSeconds(Math.Max(1, _options.AccessProfileCacheSeconds)));
            return profile;
        }

        /// <summary>Drops cached data after an administrative change so it applies on the next request.</summary>
        public void Invalidate(Guid? userId = null)
        {
            if (userId.HasValue) _cache.Remove(ProfileKey(userId.Value));
            _cache.Remove(PoliciesCacheKey);
            if (!userId.HasValue) Interlocked.Increment(ref _generation);
        }

        public async Task<IReadOnlyList<AccessPolicyRecord>> ActivePoliciesAsync()
        {
            if (_cache.TryGetValue(PoliciesCacheKey, out IReadOnlyList<AccessPolicyRecord>? cached) && cached != null) return cached;
            var policies = await _repository.GetPoliciesAsync(activeOnly: true);
            _cache.Set(PoliciesCacheKey, (IReadOnlyList<AccessPolicyRecord>)policies, TimeSpan.FromSeconds(Math.Max(1, _options.AccessProfileCacheSeconds)));
            return policies;
        }

        /// <summary>RBAC + ABAC decision for the current user. Resource attributes may be empty for subject-only checks.</summary>
        public async Task<PolicyDecision> CheckAsync(string permission, IReadOnlyDictionary<string, string?>? resource = null)
        {
            var user = await CurrentAsync();
            if (user == null) return new PolicyDecision(false, null, "Sign in to continue.");
            return AccessPolicyEvaluator.Evaluate(permission, user, resource ?? new Dictionary<string, string?>(),
                await ActivePoliciesAsync(), DateTime.UtcNow);
        }

        /// <summary>
        /// The data-scope rule that searches apply in SQL, for endpoints that take a record id directly:
        /// the record's customer and delivery method must be within scope.customer / scope.deliveryMethod.
        /// </summary>
        public static bool InDataScope(UserAccessProfile user, IReadOnlyDictionary<string, string?> resource)
        {
            static bool Allows(IReadOnlyList<string>? scope, string? value) =>
                scope == null || (value != null && scope.Any(s => string.Equals(s, value, StringComparison.OrdinalIgnoreCase)));
            resource.TryGetValue("customer", out var customer);
            resource.TryGetValue("deliveryMethod", out var delivery);
            return Allows(user.ScopeValues(SubjectAttributes.CustomerScope), customer)
                && Allows(user.ScopeValues(SubjectAttributes.DeliveryMethodScope), delivery);
        }

        /// <summary>Records a denial in the audit log (callers return 403 with the decision's message).</summary>
        public async Task AuditDeniedAsync(string permission, PolicyDecision decision, string? resourceDescription = null)
        {
            var user = await CurrentAsync();
            var detail = $"{permission}{(resourceDescription != null ? " on " + resourceDescription : "")}: " +
                         (decision.PolicyName != null ? $"policy '{decision.PolicyName}'" : "no role grants it");
            // "record {guid}" descriptions link the denial to that record's activity.
            Guid? resourceId = resourceDescription != null && resourceDescription.StartsWith("record ", StringComparison.Ordinal)
                               && Guid.TryParse(resourceDescription.Substring(7), out var rid) ? rid : null;
            await _repository.WriteAuditAsync(user?.UserId, user?.UserName, "AccessDenied", "Denied", detail, ClientIp(), resourceId);
        }

        public string? ClientIp() => _http.HttpContext?.Connection.RemoteIpAddress?.ToString();
    }
}
