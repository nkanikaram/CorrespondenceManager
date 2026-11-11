using System.Security.Cryptography;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.Tokens;

namespace ProcessRequestEndPoint.Auth
{
    public static class PortalAuthServiceCollectionExtensions
    {
        /// <summary>
        /// Registers portal sign-in (local accounts + HS256 tokens), permission policies (RBAC) and
        /// the ABAC services. Adds a second JWT scheme next to the existing OIDC one; the default
        /// scheme is unchanged, and portal endpoints opt in with [RequirePermission] / [RequirePortalUser].
        /// </summary>
        public static WebApplicationBuilder AddPortalAuth(this WebApplicationBuilder builder)
        {
            var services = builder.Services;
            var section = builder.Configuration.GetSection(PortalAuthOptions.SectionName);
            services.Configure<PortalAuthOptions>(section);
            var options = section.Get<PortalAuthOptions>() ?? new PortalAuthOptions();

            services.AddSingleton(ResolveSigningKey(options, builder.Environment));
            services.AddHttpContextAccessor();
            services.AddMemoryCache();
            services.AddSingleton<AuthRepository>();
            services.AddSingleton<TokenService>();
            services.AddSingleton<EntraTokenValidator>();
            services.AddSingleton<IPasswordResetMailer, SmtpPasswordResetMailer>();
            services.AddSingleton<PasswordResetService>();
            services.AddScoped<PortalAccessService>();
            services.AddSingleton<IAuthorizationPolicyProvider, PermissionPolicyProvider>();
            services.AddScoped<IAuthorizationHandler, PermissionHandler>();
            services.AddHostedService<PortalAuthBootstrapper>();

            services.AddAuthentication()
                .AddJwtBearer(PortalAuthDefaults.Scheme, _ => { });

            // Configured after the container is built so the validation parameters come from TokenService.
            services.AddOptions<JwtBearerOptions>(PortalAuthDefaults.Scheme)
                .Configure<TokenService>((jwt, tokens) =>
                {
                    jwt.MapInboundClaims = false;
                    jwt.TokenValidationParameters = tokens.ValidationParameters();
                    jwt.Events = new JwtBearerEvents
                    {
                        // Reject tokens of deactivated users or whose security stamp was rotated
                        // (password reset, role/attribute change, deactivation).
                        OnTokenValidated = async ctx =>
                        {
                            var access = ctx.HttpContext.RequestServices.GetRequiredService<PortalAccessService>();
                            if (ctx.Principal == null || await access.LoadValidatedAsync(ctx.Principal) == null)
                                ctx.Fail("The session is no longer valid.");
                        }
                    };
                });

            return builder;
        }

        private static PortalSigningKey ResolveSigningKey(PortalAuthOptions options, IHostEnvironment env)
        {
            if (!string.IsNullOrWhiteSpace(options.SigningKey))
            {
                byte[] bytes;
                try { bytes = Convert.FromBase64String(options.SigningKey.Trim()); }
                catch (FormatException) { throw new InvalidOperationException("PortalAuth:SigningKey must be base64."); }
                if (bytes.Length < 32) throw new InvalidOperationException("PortalAuth:SigningKey must decode to at least 32 bytes.");
                return new PortalSigningKey(new SymmetricSecurityKey(bytes), isEphemeral: false);
            }

            if (!env.IsDevelopment())
                throw new InvalidOperationException(
                    "PortalAuth:SigningKey is not configured. Set it (base64, 32+ bytes) via an App Service setting or Key Vault reference.");

            // Development only: a per-process key, so signing in again is needed after each restart.
            return new PortalSigningKey(new SymmetricSecurityKey(RandomNumberGenerator.GetBytes(32)), isEphemeral: true);
        }
    }

    /// <summary>
    /// Creates the first administrator from PortalAuth:BootstrapAdmin when no portal users exist,
    /// so a fresh environment can be signed into without running the development seed script.
    /// The account must change its password on first sign-in.
    /// </summary>
    public sealed class PortalAuthBootstrapper : IHostedService
    {
        private readonly AuthRepository _repository;
        private readonly PortalAuthOptions _options;
        private readonly PortalSigningKey _key;
        private readonly ILogger<PortalAuthBootstrapper> _logger;

        public PortalAuthBootstrapper(AuthRepository repository, IOptions<PortalAuthOptions> options, PortalSigningKey key,
            ILogger<PortalAuthBootstrapper> logger)
        {
            _repository = repository;
            _options = options.Value;
            _key = key;
            _logger = logger;
        }

        public async Task StartAsync(CancellationToken cancellationToken)
        {
            if (_key.IsEphemeral)
                _logger.LogWarning("PortalAuth:SigningKey is not set; using a temporary development key. Portal sessions end when the API restarts.");

            var admin = _options.BootstrapAdmin;
            if (string.IsNullOrWhiteSpace(admin.UserName) || string.IsNullOrWhiteSpace(admin.Password)) return;

            try
            {
                if (await _repository.CountUsersAsync(cancellationToken) > 0) return;

                var problems = PasswordHasher.Validate(admin.Password, admin.UserName, _options.MinPasswordLength);
                if (problems.Count > 0)
                {
                    _logger.LogError("PortalAuth:BootstrapAdmin password rejected: {Problems}", string.Join(" ", problems));
                    return;
                }

                var id = await _repository.CreateUserAsync(admin.UserName.Trim(), admin.DisplayName, null, true,
                    PasswordHasher.Hash(admin.Password, _options.PasswordIterations), mustChangePassword: true,
                    new[] { "Administrator" },
                    new Dictionary<string, List<string>>
                    {
                        [SubjectAttributes.CustomerScope] = new() { SubjectAttributes.Wildcard },
                        [SubjectAttributes.DeliveryMethodScope] = new() { SubjectAttributes.Wildcard },
                        [SubjectAttributes.PiiClearance] = new() { "true" }
                    }, cancellationToken);
                await _repository.WriteAuditAsync(id, admin.UserName, "UserCreated", "Success", "Bootstrap administrator", null);
                _logger.LogWarning("Created bootstrap portal administrator '{UserName}'. Remove PortalAuth:BootstrapAdmin settings now.", admin.UserName);
            }
            catch (Exception ex)
            {
                // A missing auth schema must not stop the rest of the API from starting.
                _logger.LogError(ex, "Portal auth bootstrap skipped. Has Build/DBScripts/Auth been applied?");
            }
        }

        public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;
    }
}
