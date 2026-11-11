using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.Protocols;
using Microsoft.IdentityModel.Protocols.OpenIdConnect;
using Microsoft.IdentityModel.Tokens;

namespace ProcessRequestEndPoint.Auth
{
    /// <summary>What the portal needs from a validated Entra ID token.</summary>
    public sealed class EntraIdentity
    {
        public string ObjectId { get; init; } = string.Empty;
        public string TenantId { get; init; } = string.Empty;
        public string UserName { get; init; } = string.Empty;
        public string DisplayName { get; init; } = string.Empty;
        public string? Email { get; init; }
        public IReadOnlyList<string> Groups { get; init; } = Array.Empty<string>();
        public IReadOnlyList<string> AppRoles { get; init; } = Array.Empty<string>();

        /// <summary>True when the directory left groups out (over the token's group limit); mapped groups can't be read.</summary>
        public bool GroupsOverage { get; init; }
    }

    public sealed class EntraTokenException : Exception
    {
        public EntraTokenException(string message, Exception? inner = null) : base(message, inner) { }
    }

    /// <summary>
    /// Validates the ID token the portal obtained from Microsoft Entra ID (MSAL, authorization code + PKCE):
    /// signature against the tenant's published keys, issuer and tenant, audience (the portal's client id),
    /// lifetime, how recently it was issued, and — when required — that the sign-in used MFA.
    /// </summary>
    public class EntraTokenValidator
    {
        private readonly EntraOptions _options;
        private readonly IConfigurationManager<OpenIdConnectConfiguration> _metadata;

        public EntraTokenValidator(IOptions<PortalAuthOptions> options)
            : this(options, CreateMetadataManager(options.Value.Entra))
        {
        }

        /// <summary>For tests: supply signing keys without calling Microsoft.</summary>
        public EntraTokenValidator(IOptions<PortalAuthOptions> options, IConfigurationManager<OpenIdConnectConfiguration> metadata)
        {
            _options = options.Value.Entra;
            _metadata = metadata;
        }

        private static IConfigurationManager<OpenIdConnectConfiguration> CreateMetadataManager(EntraOptions o) =>
            new ConfigurationManager<OpenIdConnectConfiguration>(
                o.Authority + "/.well-known/openid-configuration",
                new OpenIdConnectConfigurationRetriever(),
                new HttpDocumentRetriever { RequireHttps = true });

        public async Task<EntraIdentity> ValidateAsync(string idToken, CancellationToken ct)
        {
            if (!_options.Enabled || string.IsNullOrWhiteSpace(_options.TenantId) || string.IsNullOrWhiteSpace(_options.ClientId))
                throw new EntraTokenException("Microsoft sign-in isn't configured on this server.");

            OpenIdConnectConfiguration metadata;
            try
            {
                metadata = await _metadata.GetConfigurationAsync(ct);
            }
            catch (Exception ex)
            {
                throw new EntraTokenException("Couldn't reach Microsoft to check the sign-in. Try again shortly.", ex);
            }

            var parameters = new TokenValidationParameters
            {
                ValidateIssuer = true,
                ValidIssuer = _options.Authority,
                ValidateAudience = true,
                ValidAudience = _options.ClientId,
                ValidateLifetime = true,
                ClockSkew = TimeSpan.FromMinutes(2),
                RequireSignedTokens = true,
                IssuerSigningKeys = metadata.SigningKeys,
                ValidAlgorithms = new[] { SecurityAlgorithms.RsaSha256 }
            };

            ClaimsPrincipal principal;
            try
            {
                principal = new JwtSecurityTokenHandler { MapInboundClaims = false }.ValidateToken(idToken, parameters, out _);
            }
            catch (SecurityTokenSignatureKeyNotFoundException)
            {
                _metadata.RequestRefresh(); // Microsoft rotated its keys; the next attempt fetches them
                throw new EntraTokenException("The Microsoft sign-in couldn't be verified. Please sign in again.");
            }
            catch (Exception ex) when (ex is SecurityTokenException or ArgumentException)
            {
                throw new EntraTokenException("The Microsoft sign-in couldn't be verified. Please sign in again.", ex);
            }

            string? Claim(string type) => principal.FindFirst(type)?.Value;
            var tenant = Claim("tid");
            if (!string.Equals(tenant, _options.TenantId, StringComparison.OrdinalIgnoreCase))
                throw new EntraTokenException("This account belongs to a different organisation.");

            var issuedAt = long.TryParse(Claim("iat"), out var iat) ? DateTimeOffset.FromUnixTimeSeconds(iat) : DateTimeOffset.MinValue;
            if (DateTimeOffset.UtcNow - issuedAt > TimeSpan.FromMinutes(_options.MaxTokenAgeMinutes))
                throw new EntraTokenException("The Microsoft sign-in is too old. Please sign in again.");

            if (_options.RequireMfa)
            {
                var amr = principal.FindAll("amr").Select(c => c.Value).ToList();
                if (!amr.Any(a => _options.MfaClaimValues.Contains(a, StringComparer.OrdinalIgnoreCase)))
                    throw new EntraTokenException("Multi-factor authentication is required. Sign in again and complete the extra verification step.");
            }

            var objectId = Claim("oid");
            if (string.IsNullOrWhiteSpace(objectId)) throw new EntraTokenException("The Microsoft sign-in has no user id.");
            var userName = Claim("preferred_username") ?? Claim("upn") ?? Claim("email") ?? objectId;

            return new EntraIdentity
            {
                ObjectId = objectId,
                TenantId = tenant!,
                UserName = userName.Length > 100 ? userName[..100] : userName,
                DisplayName = Claim("name") ?? userName,
                Email = Claim("email") ?? (userName.Contains('@') ? userName : null),
                Groups = principal.FindAll("groups").Select(c => c.Value).ToList(),
                AppRoles = principal.FindAll("roles").Select(c => c.Value).ToList(),
                GroupsOverage = principal.FindFirst("_claim_names")?.Value.Contains("groups") == true || principal.HasClaim(c => c.Type == "hasgroups")
            };
        }
    }
}
