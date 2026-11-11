using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.Tokens;

namespace ProcessRequestEndPoint.Auth
{
    /// <summary>
    /// Issues HS256 access tokens for portal users. Tokens deliberately carry only identity
    /// (sub, name, security stamp): roles, permissions and attributes are re-read from the
    /// database per request (cached briefly) so admin changes and deactivation apply quickly.
    /// </summary>
    public class TokenService
    {
        private readonly PortalAuthOptions _options;
        private readonly SymmetricSecurityKey _key;

        public TokenService(IOptions<PortalAuthOptions> options, PortalSigningKey signingKey)
        {
            _options = options.Value;
            _key = signingKey.Key;
        }

        public (string Token, DateTime ExpiresUtc) Issue(UserAccessProfile user)
        {
            var now = DateTime.UtcNow;
            var expires = now.AddMinutes(_options.TokenLifetimeMinutes);
            var claims = new[]
            {
                new Claim(JwtRegisteredClaimNames.Sub, user.UserId.ToString()),
                new Claim(JwtRegisteredClaimNames.UniqueName, user.UserName),
                new Claim(JwtRegisteredClaimNames.Jti, Guid.NewGuid().ToString("N")),
                new Claim(PortalAuthDefaults.SecurityStampClaim, user.SecurityStamp.ToString())
            };
            var token = new JwtSecurityToken(
                issuer: _options.Issuer,
                audience: _options.Audience,
                claims: claims,
                notBefore: now,
                expires: expires,
                signingCredentials: new SigningCredentials(_key, SecurityAlgorithms.HmacSha256));
            return (new JwtSecurityTokenHandler().WriteToken(token), expires);
        }

        public TokenValidationParameters ValidationParameters() => new()
        {
            ValidateIssuer = true,
            ValidIssuer = _options.Issuer,
            ValidateAudience = true,
            ValidAudience = _options.Audience,
            ValidateIssuerSigningKey = true,
            IssuerSigningKey = _key,
            ValidAlgorithms = new[] { SecurityAlgorithms.HmacSha256 },
            ValidateLifetime = true,
            ClockSkew = TimeSpan.FromSeconds(30),
            NameClaimType = JwtRegisteredClaimNames.UniqueName,
            // Lets PortalUserId() tell portal principals apart from OIDC ones.
            AuthenticationType = PortalAuthDefaults.Scheme
        };
    }

    /// <summary>The resolved signing key (from configuration, or an ephemeral one in Development).</summary>
    public sealed class PortalSigningKey
    {
        public PortalSigningKey(SymmetricSecurityKey key, bool isEphemeral)
        {
            Key = key;
            IsEphemeral = isEphemeral;
        }

        public SymmetricSecurityKey Key { get; }
        public bool IsEphemeral { get; }
    }

    public static class ClaimsPrincipalExtensions
    {
        /// <summary>The portal user id, or null for anonymous / non-portal (OIDC) principals.</summary>
        public static Guid? PortalUserId(this ClaimsPrincipal principal)
        {
            if (principal.Identity?.AuthenticationType != PortalAuthDefaults.Scheme) return null;
            var sub = principal.FindFirstValue(JwtRegisteredClaimNames.Sub) ?? principal.FindFirstValue(ClaimTypes.NameIdentifier);
            return Guid.TryParse(sub, out var id) ? id : null;
        }
    }
}
