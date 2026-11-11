using System.Globalization;
using System.Text.RegularExpressions;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Options;
using ProcessRequestEndPoint.Auth;
using ProcessRequestEndPoint.Realtime;

namespace ProcessRequestEndPoint.Controllers
{
    /// <summary>User, role, attribute and access-policy administration for the portal.</summary>
    [ApiController]
    [Route("api/admin")]
    public class AdminController : ControllerBase
    {
        private static readonly Regex UserNamePattern = new("^[A-Za-z0-9._@-]{3,100}$", RegexOptions.Compiled);
        private const string AdministratorRole = "Administrator";

        private readonly AuthRepository _repository;
        private readonly PortalAccessService _access;
        private readonly PortalAuthOptions _options;
        private readonly TokenService _tokens;

        public AdminController(AuthRepository repository, PortalAccessService access, IOptions<PortalAuthOptions> options, TokenService tokens)
        {
            _tokens = tokens;
            _repository = repository;
            _access = access;
            _options = options.Value;
        }

        // ---------------- Users ----------------

        [RequirePermission(Permissions.UsersManage)]
        [HttpGet("users")]
        public async Task<IActionResult> ListUsers() => Ok(await _repository.ListUsersAsync());

        [RequirePermission(Permissions.UsersManage)]
        [HttpPost("users")]
        public async Task<IActionResult> CreateUser([FromBody] SaveUserRequest request)
        {
            var userName = request.UserName?.Trim() ?? string.Empty;
            if (!UserNamePattern.IsMatch(userName))
                return BadRequest(new { message = "User names are 3-100 characters: letters, digits and . _ @ -" });
            if (await _repository.UserNameExistsAsync(userName))
                return Conflict(new { message = $"The user name '{userName}' is taken." });

            var problems = PasswordHasher.Validate(request.Password, userName, _options.MinPasswordLength);
            if (problems.Count > 0) return BadRequest(new { message = "Choose a stronger temporary password.", problems });

            var validation = await ValidateRolesAndAttributesAsync(request);
            if (validation != null) return BadRequest(new { message = validation });

            var id = await _repository.CreateUserAsync(userName, DisplayName(request, userName), Email(request), request.IsActive,
                PasswordHasher.Hash(request.Password!, _options.PasswordIterations), mustChangePassword: true,
                request.Roles, request.Attributes);
            await AuditAsync("UserCreated", $"{userName}: roles {string.Join(", ", request.Roles)}");
            return Ok(new { userId = id });
        }

        [RequirePermission(Permissions.UsersManage)]
        [HttpPut("users/{id:guid}")]
        public async Task<IActionResult> UpdateUser(Guid id, [FromBody] SaveUserRequest request)
        {
            var me = await _access.CurrentAsync();
            if (me != null && me.UserId == id)
            {
                // Guard against an administrator locking themselves (and possibly everyone) out.
                if (!request.IsActive) return BadRequest(new { message = "You can't deactivate your own account." });
                if (!request.Roles.Contains(AdministratorRole, StringComparer.OrdinalIgnoreCase) && me.Roles.Contains(AdministratorRole))
                    return BadRequest(new { message = "You can't remove the Administrator role from yourself." });
            }

            var validation = await ValidateRolesAndAttributesAsync(request);
            if (validation != null) return BadRequest(new { message = validation });

            var existing = (await _repository.ListUsersAsync()).FirstOrDefault(u => u.UserId == id);
            if (existing == null) return NotFound();

            await _repository.UpdateUserAsync(id, DisplayName(request, existing.UserName), Email(request), request.IsActive, request.Roles, request.Attributes);
            _access.Invalidate(id);
            await PortalEvents.NotifySessionsEndedAsync(HttpContext, id);
            await AuditAsync("UserUpdated", $"{existing.UserName}: active={request.IsActive}, roles {string.Join(", ", request.Roles)}, " +
                                            $"attributes {string.Join("; ", request.Attributes.Select(kv => kv.Key + "=" + string.Join("|", kv.Value)))}");

            // The update rotated the security stamp, which ends the user's sessions. When you edit your own
            // account, return a fresh token so you stay signed in (with the new access applied).
            if (me != null && me.UserId == id)
            {
                var updated = await _repository.GetAccessProfileAsync(id);
                if (updated != null)
                {
                    var (token, expires) = _tokens.Issue(updated);
                    return Ok(new { accessToken = token, expiresAtUtc = expires, user = AuthController.ToDto(updated) });
                }
            }
            return NoContent();
        }

        /// <summary>Sets a temporary password; the user must change it at next sign-in. Ends their current sessions.</summary>
        [RequirePermission(Permissions.UsersManage)]
        [HttpPost("users/{id:guid}/reset-password")]
        public async Task<IActionResult> ResetPassword(Guid id, [FromBody] ResetPasswordRequest request)
        {
            var existing = (await _repository.ListUsersAsync()).FirstOrDefault(u => u.UserId == id);
            if (existing == null) return NotFound();
            if (existing.ExternalProvider != null)
                return BadRequest(new { message = "This account signs in with Microsoft; its password is managed in Entra ID." });
            var problems = PasswordHasher.Validate(request.NewPassword, existing.UserName, _options.MinPasswordLength);
            if (problems.Count > 0) return BadRequest(new { message = "Choose a stronger temporary password.", problems });

            await _repository.SetPasswordAsync(id, PasswordHasher.Hash(request.NewPassword!, _options.PasswordIterations), mustChange: true);
            _access.Invalidate(id);
            await PortalEvents.NotifySessionsEndedAsync(HttpContext, id);
            await AuditAsync("PasswordReset", existing.UserName);
            return NoContent();
        }

        /// <summary>
        /// Permanently deletes an account (roles and attributes included). Not allowed for yourself or for the
        /// last active Administrator. Audit entries keep the user name; deactivation is the reversible option.
        /// </summary>
        [RequirePermission(Permissions.UsersManage)]
        [HttpDelete("users/{id:guid}")]
        public async Task<IActionResult> DeleteUser(Guid id)
        {
            var me = await _access.CurrentAsync();
            if (me != null && me.UserId == id) return BadRequest(new { message = "You can't delete your own account." });

            var existing = (await _repository.ListUsersAsync()).FirstOrDefault(u => u.UserId == id);
            if (existing == null) return NotFound(new { message = "That user doesn't exist." });

            if (existing.IsActive && existing.Roles.Contains(AdministratorRole, StringComparer.OrdinalIgnoreCase)
                && await _repository.CountActiveAdministratorsAsync() <= 1)
                return BadRequest(new { message = "You can't delete the last active Administrator." });

            await _repository.DeleteUserAsync(id);
            _access.Invalidate(id);
            await AuditAsync("UserDeleted", $"{existing.UserName} ({existing.DisplayName}): roles {string.Join(", ", existing.Roles)}");
            return NoContent();
        }

        [RequirePermission(Permissions.UsersManage)]
        [HttpPost("users/{id:guid}/unlock")]
        public async Task<IActionResult> Unlock(Guid id)
        {
            await _repository.UnlockUserAsync(id);
            await AuditAsync("UserUnlocked", id.ToString());
            return NoContent();
        }

        // ---------------- Roles & permissions (RBAC) ----------------

        [RequirePermission(Permissions.UsersManage)]
        [HttpGet("roles")]
        public async Task<IActionResult> ListRoles() => Ok(await _repository.ListRolesAsync());

        [RequirePermission(Permissions.UsersManage)]
        [HttpGet("permissions")]
        public async Task<IActionResult> ListPermissions() => Ok(await _repository.ListPermissionsAsync());

        [RequirePermission(Permissions.UsersManage)]
        [HttpPut("roles/{id:int}/permissions")]
        public async Task<IActionResult> SetRolePermissions(int id, [FromBody] UpdateRolePermissionsRequest request)
        {
            var roles = await _repository.ListRolesAsync();
            var role = roles.FirstOrDefault(r => r.RoleId == id);
            if (role == null) return NotFound();

            var known = (await _repository.ListPermissionsAsync()).Select(p => p.PermissionKey).ToHashSet(StringComparer.OrdinalIgnoreCase);
            var unknown = request.Permissions.Where(p => !known.Contains(p)).ToList();
            if (unknown.Count > 0) return BadRequest(new { message = "Unknown permissions: " + string.Join(", ", unknown) });

            if (role.RoleName == AdministratorRole && !request.Permissions.Contains(Permissions.UsersManage, StringComparer.OrdinalIgnoreCase))
                return BadRequest(new { message = "The Administrator role must keep admin.users.manage, or nobody could administer access." });

            await _repository.SetRolePermissionsAsync(id, request.Permissions);
            _access.Invalidate();
            await AuditAsync("RolePermissionsChanged", $"{role.RoleName}: {string.Join(", ", request.Permissions.OrderBy(x => x))}");
            return NoContent();
        }

        // ---------------- Attributes & policies (ABAC) ----------------

        [RequirePermission(Permissions.UsersManage)]
        [HttpGet("attributes")]
        public async Task<IActionResult> ListAttributes() => Ok(await _repository.ListAttributeDefinitionsAsync());

        [RequirePermission(Permissions.UsersManage)]
        [HttpGet("policies")]
        public async Task<IActionResult> ListPolicies() => Ok(await _repository.GetPoliciesAsync(activeOnly: false));

        [RequirePermission(Permissions.UsersManage)]
        [HttpPut("policies/{id:int}")]
        public async Task<IActionResult> UpdatePolicy(int id, [FromBody] UpdatePolicyRequest request)
        {
            var existing = (await _repository.GetPoliciesAsync(activeOnly: false)).FirstOrDefault(p => p.PolicyId == id);
            if (existing == null) return NotFound();
            var condition = string.IsNullOrWhiteSpace(request.ConditionJson) ? existing.ConditionJson : request.ConditionJson!;
            var error = AccessPolicyEvaluator.ValidateCondition(condition);
            if (error != null) return BadRequest(new { message = "The condition is not valid: " + error });

            await _repository.UpdatePolicyAsync(id, request.IsActive, condition, request.DenyMessage ?? existing.DenyMessage);
            _access.Invalidate();
            await AuditAsync("PolicyChanged", $"{existing.PolicyName}: active={request.IsActive}");
            return NoContent();
        }

        /// <summary>All distinct customers / delivery methods, for scope pickers (unscoped: administrators assign scope).</summary>
        [RequirePermission(Permissions.UsersManage)]
        [HttpGet("lookups/{field}")]
        public async Task<IActionResult> Lookup(string field)
        {
            if (field != "customer" && field != "delivery") return NotFound();
            return Ok(await _repository.ListDistinctValuesAsync(field));
        }

        // ---------------- Entra group mappings (SSO) ----------------

        [RequirePermission(Permissions.UsersManage)]
        [HttpGet("group-mappings")]
        public async Task<IActionResult> ListGroupMappings() => Ok(await _repository.ListGroupMappingsAsync());

        /// <summary>Maps an Entra group (object id) or app role (value) to a portal role and/or an attribute value.</summary>
        [RequirePermission(Permissions.UsersManage)]
        [HttpPost("group-mappings")]
        public async Task<IActionResult> AddGroupMapping([FromBody] SaveGroupMappingRequest request)
        {
            var claimType = request.ClaimType == "appRole" ? "appRole" : request.ClaimType == "group" ? "group" : null;
            var value = request.ClaimValue?.Trim();
            if (claimType == null) return BadRequest(new { message = "Choose an Entra group or an app role." });
            if (string.IsNullOrEmpty(value) || value.Length > 200) return BadRequest(new { message = "Enter the group's object id or the app role value." });
            if (claimType == "group" && !Guid.TryParse(value, out _)) return BadRequest(new { message = "A group is identified by its object id (a GUID) in Entra ID." });

            var hasRole = !string.IsNullOrWhiteSpace(request.RoleName);
            var hasAttribute = !string.IsNullOrWhiteSpace(request.AttributeKey) && !string.IsNullOrWhiteSpace(request.AttributeValue);
            if (!hasRole && !hasAttribute) return BadRequest(new { message = "Choose a role or an attribute value to grant." });
            if (hasRole && !(await _repository.ListRolesAsync()).Any(r => r.RoleName == request.RoleName))
                return BadRequest(new { message = $"Unknown role '{request.RoleName}'." });
            if (hasAttribute)
            {
                var problem = await ValidateRolesAndAttributesAsync(new SaveUserRequest
                {
                    Attributes = new Dictionary<string, List<string>> { [request.AttributeKey!] = new() { request.AttributeValue! } }
                });
                if (problem != null) return BadRequest(new { message = problem });
            }

            var id = await _repository.AddGroupMappingAsync(claimType, value, request.DisplayName?.Trim(), hasRole ? request.RoleName : null,
                hasAttribute ? request.AttributeKey : null, hasAttribute ? request.AttributeValue!.Trim() : null);
            await AuditAsync("GroupMappingAdded", $"{claimType} {request.DisplayName ?? value} -> {(hasRole ? "role " + request.RoleName : "")}{(hasRole && hasAttribute ? ", " : "")}{(hasAttribute ? request.AttributeKey + "=" + request.AttributeValue : "")}");
            return Ok(new { mappingId = id });
        }

        [RequirePermission(Permissions.UsersManage)]
        [HttpDelete("group-mappings/{id:int}")]
        public async Task<IActionResult> DeleteGroupMapping(int id)
        {
            if (!await _repository.DeleteGroupMappingAsync(id)) return NotFound();
            await AuditAsync("GroupMappingRemoved", "mapping " + id);
            return NoContent(); // takes effect at each user's next sign-in
        }

        // ---------------- Audit ----------------

        [RequirePermission(Permissions.AuditRead)]
        [HttpGet("audit")]
        public async Task<IActionResult> Audit([FromQuery] int top = 200, [FromQuery] string? userName = null) =>
            Ok(await _repository.ListAuditAsync(top, userName));

        // ---------------- helpers ----------------

        private async Task<string?> ValidateRolesAndAttributesAsync(SaveUserRequest request)
        {
            var roles = (await _repository.ListRolesAsync()).Select(r => r.RoleName).ToHashSet(StringComparer.OrdinalIgnoreCase);
            var unknownRoles = request.Roles.Where(r => !roles.Contains(r)).ToList();
            if (unknownRoles.Count > 0) return "Unknown roles: " + string.Join(", ", unknownRoles);

            var definitions = (await _repository.ListAttributeDefinitionsAsync()).ToDictionary(d => d.AttributeKey, StringComparer.OrdinalIgnoreCase);
            foreach (var (key, values) in request.Attributes)
            {
                if (!definitions.TryGetValue(key, out var def)) return $"Unknown attribute '{key}'.";
                var cleaned = values.Where(v => !string.IsNullOrWhiteSpace(v)).ToList();
                if (def.DataType != "list" && cleaned.Count > 1) return $"'{def.DisplayName}' takes a single value.";
                foreach (var v in cleaned)
                {
                    if (v.Length > 200) return $"'{def.DisplayName}' values are at most 200 characters.";
                    if (def.DataType == "boolean" && v != "true" && v != "false") return $"'{def.DisplayName}' must be true or false.";
                    if (def.DataType == "integer" && !int.TryParse(v, NumberStyles.None, CultureInfo.InvariantCulture, out _))
                        return $"'{def.DisplayName}' must be a whole number.";
                }
            }
            return null;
        }

        private static string DisplayName(SaveUserRequest r, string fallback) =>
            string.IsNullOrWhiteSpace(r.DisplayName) ? fallback : r.DisplayName.Trim()[..Math.Min(200, r.DisplayName.Trim().Length)];

        private static string? Email(SaveUserRequest r) => string.IsNullOrWhiteSpace(r.Email) ? null : r.Email.Trim();

        private async Task AuditAsync(string eventType, string detail)
        {
            var me = await _access.CurrentAsync();
            await _repository.WriteAuditAsync(me?.UserId, me?.UserName, eventType, "Success", detail, _access.ClientIp());
        }
    }
}
