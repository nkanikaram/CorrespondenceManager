using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Options;
using ProcessRequestEndPoint.Auth;
using ProcessRequestEndPoint.Realtime;

namespace ProcessRequestEndPoint.Controllers
{
    /// <summary>Portal sign-in, the current user's profile, and self-service password change.</summary>
    [ApiController]
    [Route("api/auth")]
    public class AuthController : ControllerBase
    {
        private const string InvalidCredentials = "Incorrect user name or password.";
        private readonly AuthRepository _repository;
        private readonly TokenService _tokens;
        private readonly PortalAccessService _access;
        private readonly PortalAuthOptions _options;
        private readonly EntraTokenValidator _entra;
        private readonly PasswordResetService _reset;

        public AuthController(AuthRepository repository, TokenService tokens, PortalAccessService access, IOptions<PortalAuthOptions> options,
            EntraTokenValidator entra, PasswordResetService reset)
        {
            _repository = repository;
            _tokens = tokens;
            _access = access;
            _options = options.Value;
            _entra = entra;
            _reset = reset;
        }

        /// <summary>Which sign-in methods the portal should offer, and the public Entra settings MSAL needs.</summary>
        [AllowAnonymous]
        [HttpGet("config")]
        public IActionResult Config() => Ok(new
        {
            entra = new
            {
                enabled = _options.Entra.Enabled,
                clientId = _options.Entra.Enabled ? _options.Entra.ClientId : null,
                tenantId = _options.Entra.Enabled ? _options.Entra.TenantId : null,
                authority = _options.Entra.Enabled ? _options.Entra.Instance.TrimEnd('/') + "/" + _options.Entra.TenantId : null
            },
            localLogin = new { enabled = _options.LocalLogin.Enabled },
            passwordReset = new { enabled = _reset.IsAvailable, linkLifetimeMinutes = _options.PasswordReset.LinkLifetimeMinutes }
        });

        public const string ResetRequestedMessage =
            "If that account exists and has an email address, we've sent it a link to choose a new password. Check your inbox (and junk folder).";
        private const string ResetLinkInvalid = "This reset link has expired or has already been used. Ask for a new one.";

        /// <summary>
        /// "Forgot password?": emails a one-time reset link to the account's address. Always answers the same way,
        /// so it can't be used to find out which user names or email addresses exist.
        /// </summary>
        [AllowAnonymous]
        [HttpPost("forgot-password")]
        public async Task<IActionResult> ForgotPassword([FromBody] ForgotPasswordRequest request, CancellationToken ct)
        {
            if (!_reset.IsAvailable)
                return StatusCode(StatusCodes.Status503ServiceUnavailable, new { message = "Password reset by email isn't available. Ask a portal administrator to reset your password." });
            if (string.IsNullOrWhiteSpace(request?.UserNameOrEmail))
                return BadRequest(new { message = "Enter your user name or email address." });

            await _reset.RequestAsync(request.UserNameOrEmail, _access.ClientIp(), ct);
            return Accepted(new { message = ResetRequestedMessage });
        }

        /// <summary>Checks a reset link before the user types a new password. POST, so the token stays out of URLs and logs.</summary>
        [AllowAnonymous]
        [HttpPost("reset-password/check")]
        public async Task<IActionResult> CheckResetLink([FromBody] SelfServiceResetRequest request, CancellationToken ct)
        {
            var user = await _reset.FindAsync(request?.Token, ct);
            return user == null
                ? BadRequest(new { message = ResetLinkInvalid, linkInvalid = true })
                : Ok(new { userName = user.UserName, displayName = user.DisplayName });
        }

        /// <summary>Sets a new password from a reset link. Unlocks the account and ends every existing session.</summary>
        [AllowAnonymous]
        [HttpPost("reset-password")]
        public async Task<IActionResult> ResetPassword([FromBody] SelfServiceResetRequest request, CancellationToken ct)
        {
            var ip = _access.ClientIp();
            var result = await _reset.CompleteAsync(request?.Token, request?.NewPassword, ip, ct);
            if (result.LinkInvalid)
            {
                await _repository.WriteAuditAsync(null, null, "PasswordResetFailed", "Failure", "Invalid, expired or used reset link", ip);
                return BadRequest(new { message = ResetLinkInvalid, linkInvalid = true });
            }
            if (!result.Succeeded) return BadRequest(new { message = "Choose a stronger password.", problems = result.Problems });

            _access.Invalidate(result.User!.UserId);
            await PortalEvents.NotifySessionsEndedAsync(HttpContext, result.User.UserId); // a reset ends every session
            return Ok(new { message = "Your password has been changed. Sign in with your new password.", userName = result.User.UserName });
        }

        /// <summary>
        /// Exchanges a Microsoft Entra ID token for a portal session. Creates the portal account on first sign-in
        /// and syncs roles / data-access attributes from the user's Entra groups and app roles every time.
        /// </summary>
        [AllowAnonymous]
        [HttpPost("entra")]
        public async Task<IActionResult> EntraSignIn([FromBody] EntraExchangeRequest request, CancellationToken ct)
        {
            var ip = _access.ClientIp();
            if (string.IsNullOrWhiteSpace(request?.IdToken)) return BadRequest(new { message = "Sign in with Microsoft first." });

            EntraIdentity identity;
            try
            {
                identity = await _entra.ValidateAsync(request.IdToken, ct);
            }
            catch (EntraTokenException ex)
            {
                await _repository.WriteAuditAsync(null, null, "SsoFailed", "Failure", ex.Message, ip);
                return Unauthorized(new { message = ex.Message });
            }

            var userId = await _repository.FindExternalUserAsync(AuthRepository.EntraProvider, identity.ObjectId, ct);
            if (userId == null)
            {
                if (!_options.Entra.AutoProvision)
                {
                    await _repository.WriteAuditAsync(null, identity.UserName, "SsoFailed", "Failure", "No portal account (auto-provisioning is off)", ip);
                    return Unauthorized(new { message = "You don't have access to this portal yet. Ask a portal administrator." });
                }
                userId = await _repository.ProvisionExternalUserAsync(AuthRepository.EntraProvider, identity.ObjectId, identity.TenantId,
                    identity.UserName, identity.DisplayName, identity.Email, ct);
                if (userId == null)
                {
                    await _repository.WriteAuditAsync(null, identity.UserName, "SsoFailed", "Failure", "A local account already uses this user name", ip);
                    return Conflict(new { message = "A portal account with your user name already exists. Ask a portal administrator to resolve it." });
                }
                await _repository.WriteAuditAsync(userId, identity.UserName, "UserProvisioned", "Success", "First Microsoft sign-in", ip);
            }

            await _repository.UpdateExternalProfileAsync(userId.Value, identity.DisplayName, identity.Email, ct);
            if (!identity.GroupsOverage)
            {
                if (await _repository.SyncGroupGrantsAsync(userId.Value, identity.Groups, identity.AppRoles, ct))
                    _access.Invalidate(userId.Value);
            }

            var profile = await _repository.GetAccessProfileAsync(userId.Value, ct);
            if (profile == null || !profile.IsActive)
            {
                await _repository.WriteAuditAsync(userId, identity.UserName, "LoginFailed", "Failure", "Account deactivated (SSO)", ip);
                return Unauthorized(new { message = "Your portal access has been switched off. Ask a portal administrator." });
            }

            await _repository.RecordSuccessfulLoginAsync(userId.Value);
            _access.Invalidate(userId.Value);
            var (token, expires) = _tokens.Issue(profile);
            await _repository.WriteAuditAsync(userId, profile.UserName, "LoginSucceeded", "Success",
                "Microsoft sign-in" + (identity.GroupsOverage ? " (too many groups in the token; group roles not refreshed)" : ""), ip);
            return Ok(new { accessToken = token, expiresAtUtc = expires, user = ToDto(profile) });
        }

        [AllowAnonymous]
        [HttpPost("login")]
        public async Task<IActionResult> Login([FromBody] LoginRequest request)
        {
            var userName = request?.UserName?.Trim() ?? string.Empty;
            var password = request?.Password ?? string.Empty;
            var ip = _access.ClientIp();
            if (userName.Length == 0 || password.Length == 0 || userName.Length > 100 || password.Length > 256)
                return Unauthorized(new { message = InvalidCredentials });

            if (!_options.LocalLogin.Enabled ||
                (_options.LocalLogin.AllowedUserNames.Count > 0 && !_options.LocalLogin.AllowedUserNames.Contains(userName, StringComparer.OrdinalIgnoreCase)))
            {
                await _repository.WriteAuditAsync(null, userName, "LoginFailed", "Failure", "Password sign-in not allowed for this account", ip);
                return StatusCode(StatusCodes.Status403Forbidden, new { message = "Sign in with your Microsoft work account." });
            }

            var user = await _repository.FindCredentialsAsync(userName);
            if (user == null || !user.HasPassword)
            {
                PasswordHasher.VerifyDummy(password, _options.PasswordIterations);
                await _repository.WriteAuditAsync(null, userName, "LoginFailed", "Failure", "Unknown user name", ip);
                return Unauthorized(new { message = InvalidCredentials });
            }

            if (user.LockoutEndUtc.HasValue && user.LockoutEndUtc.Value > DateTime.UtcNow)
            {
                await _repository.WriteAuditAsync(user.UserId, user.UserName, "LoginFailed", "Failure", "Account locked", ip);
                return Unauthorized(new { message = LockedMessage(user.LockoutEndUtc.Value), locked = true });
            }

            var valid = PasswordHasher.Verify(password, user.PasswordHash, user.PasswordSalt, user.PasswordIterations);
            if (!valid)
            {
                var nowLocked = await _repository.RecordFailedLoginAsync(user.UserId, _options.LockoutThreshold, _options.LockoutMinutes);
                await _repository.WriteAuditAsync(user.UserId, user.UserName, nowLocked ? "LockedOut" : "LoginFailed", "Failure", "Wrong password", ip);
                return nowLocked
                    ? Unauthorized(new { message = LockedMessage(DateTime.UtcNow.AddMinutes(_options.LockoutMinutes)), locked = true })
                    : Unauthorized(new { message = InvalidCredentials });
            }

            if (!user.IsActive)
            {
                await _repository.WriteAuditAsync(user.UserId, user.UserName, "LoginFailed", "Failure", "Account deactivated", ip);
                return Unauthorized(new { message = InvalidCredentials });
            }

            await _repository.RecordSuccessfulLoginAsync(user.UserId);
            var profile = await _repository.GetAccessProfileAsync(user.UserId);
            if (profile == null) return Unauthorized(new { message = InvalidCredentials });
            _access.Invalidate(user.UserId);

            var (token, expires) = _tokens.Issue(profile);
            await _repository.WriteAuditAsync(user.UserId, user.UserName, "LoginSucceeded", "Success", null, ip);
            return Ok(new { accessToken = token, expiresAtUtc = expires, user = ToDto(profile) });
        }

        [RequirePortalUser]
        [HttpGet("me")]
        public async Task<IActionResult> Me()
        {
            var profile = await _access.CurrentAsync();
            return profile == null ? Unauthorized() : Ok(ToDto(profile));
        }

        /// <summary>Changes the caller's password. Rotates the security stamp, so a fresh token is returned.</summary>
        [RequirePortalUser]
        [HttpPost("change-password")]
        public async Task<IActionResult> ChangePassword([FromBody] ChangePasswordRequest request)
        {
            var profile = await _access.CurrentAsync();
            if (profile == null) return Unauthorized();

            var creds = await _repository.FindCredentialsAsync(profile.UserName);
            if (creds != null && !creds.HasPassword)
                return BadRequest(new { message = "Your password is managed by your Microsoft work account." });
            if (creds == null || !PasswordHasher.Verify(request?.CurrentPassword ?? string.Empty, creds.PasswordHash, creds.PasswordSalt, creds.PasswordIterations))
                return BadRequest(new { message = "Your current password is incorrect." });

            var problems = PasswordHasher.Validate(request?.NewPassword, profile.UserName, _options.MinPasswordLength).ToList();
            if (request?.NewPassword == request?.CurrentPassword) problems.Add("Choose a password different from the current one.");
            if (problems.Count > 0) return BadRequest(new { message = "Choose a stronger password.", problems });

            await _repository.SetPasswordAsync(profile.UserId, PasswordHasher.Hash(request!.NewPassword!, _options.PasswordIterations), mustChange: false);
            await _repository.WriteAuditAsync(profile.UserId, profile.UserName, "PasswordChanged", "Success", null, _access.ClientIp());
            _access.Invalidate(profile.UserId);
            await PortalEvents.NotifySessionsEndedAsync(HttpContext, profile.UserId);

            var updated = await _repository.GetAccessProfileAsync(profile.UserId);
            var (token, expires) = _tokens.Issue(updated!);
            return Ok(new { accessToken = token, expiresAtUtc = expires, user = ToDto(updated!) });
        }

        /// <summary>Tokens are stateless; this records the sign-out. The client discards its token.</summary>
        [RequirePortalUser]
        [HttpPost("logout")]
        public async Task<IActionResult> Logout()
        {
            var profile = await _access.CurrentAsync();
            if (profile != null)
                await _repository.WriteAuditAsync(profile.UserId, profile.UserName, "Logout", "Success", null, _access.ClientIp());
            return NoContent();
        }

        /// <summary>The password policy, so the UI can show it before the user submits.</summary>
        [AllowAnonymous]
        [HttpGet("password-policy")]
        public IActionResult PasswordPolicy() => Ok(new
        {
            minLength = _options.MinPasswordLength,
            rules = new[] { $"At least {_options.MinPasswordLength} characters", "Three of: upper case, lower case, digits, symbols", "Must not contain your user name" }
        });

        private static string LockedMessage(DateTime untilUtc)
        {
            var minutes = Math.Max(1, (int)Math.Ceiling((untilUtc - DateTime.UtcNow).TotalMinutes));
            return $"Too many failed attempts. The account is locked for {minutes} more minute{(minutes == 1 ? "" : "s")}.";
        }

        internal static object ToDto(UserAccessProfile p) => new
        {
            userId = p.UserId,
            signInMethod = p.ExternalProvider ?? "Local",
            userName = p.UserName,
            displayName = p.DisplayName,
            email = p.Email,
            mustChangePassword = p.MustChangePassword,
            roles = p.Roles,
            permissions = p.Permissions.OrderBy(x => x).ToList(),
            attributes = p.Attributes
        };
    }
}
