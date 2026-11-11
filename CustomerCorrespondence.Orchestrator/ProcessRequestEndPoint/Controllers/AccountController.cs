using System.Reflection;
using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Options;
using ProcessRequestEndPoint.Auth;
using ProcessRequestEndPoint.Realtime;

namespace ProcessRequestEndPoint.Controllers
{
    /// <summary>
    /// The account menu: the caller's data scope, previous sign-in, saved display preferences, "sign out other
    /// sessions", and the support links / version. Everything here is about the caller only.
    /// </summary>
    [ApiController]
    [Route("api/account")]
    [RequirePortalUser]
    public class AccountController : ControllerBase
    {
        private readonly AuthRepository _repository;
        private readonly PortalAccessService _access;
        private readonly TokenService _tokens;
        private readonly PortalAuthOptions _options;
        private readonly IHostEnvironment _environment;

        public AccountController(AuthRepository repository, PortalAccessService access, TokenService tokens,
            IOptions<PortalAuthOptions> options, IHostEnvironment environment)
        {
            _repository = repository;
            _access = access;
            _tokens = tokens;
            _options = options.Value;
            _environment = environment;
        }

        /// <summary>The API's version (the assembly's informational version, without the source hash).</summary>
        public static string ApiVersion { get; } =
            (typeof(AccountController).Assembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion
             ?? typeof(AccountController).Assembly.GetName().Version?.ToString() ?? "unknown").Split('+')[0];

        [HttpGet]
        public async Task<IActionResult> Get(CancellationToken ct)
        {
            var me = await _access.CurrentAsync();
            if (me == null) return Unauthorized();
            var (previousUtc, previousIp, previousDetail, failedSince) = await _repository.SignInHistoryAsync(me.UserId, ct);
            return Ok(new
            {
                // null = every value ("*"); an empty list = none.
                scope = new
                {
                    customers = me.ScopeValues(SubjectAttributes.CustomerScope),
                    deliveryMethods = me.ScopeValues(SubjectAttributes.DeliveryMethodScope)
                },
                previousSignIn = previousUtc == null ? null : new
                {
                    utc = previousUtc,
                    ip = previousIp,
                    method = previousDetail != null && previousDetail.StartsWith("Microsoft", StringComparison.Ordinal) ? "Microsoft" : "Password"
                },
                failedSignInsSincePrevious = failedSince,
                preferences = UserPreferences.Parse(await _repository.GetPreferencesAsync(me.UserId, ct)),
                support = new
                {
                    email = string.IsNullOrWhiteSpace(_options.Support.Email) ? null : _options.Support.Email,
                    guideUrl = string.IsNullOrWhiteSpace(_options.Support.GuideUrl) ? null : _options.Support.GuideUrl
                },
                version = ApiVersion,
                environment = string.IsNullOrWhiteSpace(_options.Support.EnvironmentName) ? _environment.EnvironmentName : _options.Support.EnvironmentName
            });
        }

        /// <summary>Saves the caller's display preferences (theme, time zone, page size, density).</summary>
        [HttpPut("preferences")]
        public async Task<IActionResult> SavePreferences([FromBody] UserPreferences? preferences, CancellationToken ct)
        {
            var me = await _access.CurrentAsync();
            if (me == null) return Unauthorized();
            var problems = UserPreferences.Validate(preferences);
            if (problems.Count > 0) return BadRequest(new { message = "Check your preferences.", problems });
            if (!await _repository.SavePreferencesAsync(me.UserId, preferences!.ToJson(), ct))
                return StatusCode(StatusCodes.Status503ServiceUnavailable,
                    new { message = "Preferences can't be saved yet (run Build/DBScripts/Auth/006_PortalAuth_UserPreference.sql)." });
            return Ok(preferences);
        }

        /// <summary>
        /// Ends every other session (other browsers and devices) by rotating the security stamp, and returns a fresh
        /// token so this one carries on.
        /// </summary>
        [HttpPost("sign-out-others")]
        public async Task<IActionResult> SignOutOthers(CancellationToken ct)
        {
            var me = await _access.CurrentAsync();
            if (me == null) return Unauthorized();
            await _repository.RotateSecurityStampAsync(me.UserId, ct);
            _access.Invalidate(me.UserId);
            var updated = await _repository.GetAccessProfileAsync(me.UserId);
            if (updated == null) return Unauthorized();
            var (token, expires) = _tokens.Issue(updated);
            await _repository.WriteAuditAsync(me.UserId, me.UserName, "SignedOutOtherSessions", "Success", null, _access.ClientIp());
            await PortalEvents.NotifySessionsEndedAsync(HttpContext, me.UserId); // the other browsers sign out now, not at their next request
            return Ok(new { accessToken = token, expiresAtUtc = expires, user = AuthController.ToDto(updated) });
        }
    }

    /// <summary>Display preferences saved with the account. A null value means "not set" (the portal's default).</summary>
    public sealed class UserPreferences
    {
        public static readonly string[] Themes = { "light", "dark", "system" };
        public static readonly string[] TimeZones = { "local", "utc" };
        public static readonly int[] PageSizes = { 10, 25, 50, 100 };
        public static readonly string[] Densities = { "comfortable", "compact" };
        public static readonly string[] Fonts = { "default", "calibri", "arial", "verdana", "georgia", "mono" };
        public static readonly string[] TextSizes = { "sm", "md", "lg", "xl" };
        private static readonly System.Text.RegularExpressions.Regex Hex = new("^#[0-9a-fA-F]{6}$");

        private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web)
        {
            DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
        };

        public string? Theme { get; set; }
        public string? TimeZone { get; set; }
        public int? PageSize { get; set; }
        public string? Density { get; set; }
        /// <summary>Appearance, for every screen: font, text size, header colour (#rrggbb) and text colour (#rrggbb or "contrast").</summary>
        public string? Font { get; set; }
        public string? TextSize { get; set; }
        public string? HeaderColor { get; set; }
        public string? TextColor { get; set; }

        private static bool IsTextColor(string? v) => v == "contrast" || (v != null && Hex.IsMatch(v));

        public static List<string> Validate(UserPreferences? p)
        {
            var problems = new List<string>();
            if (p == null) { problems.Add("Send the preferences to save."); return problems; }
            if (p.Theme != null && !Themes.Contains(p.Theme)) problems.Add("Theme must be light, dark or system.");
            if (p.TimeZone != null && !TimeZones.Contains(p.TimeZone)) problems.Add("Time zone must be local or utc.");
            if (p.PageSize != null && !PageSizes.Contains(p.PageSize.Value)) problems.Add("Page size must be 10, 25, 50 or 100.");
            if (p.Density != null && !Densities.Contains(p.Density)) problems.Add("Density must be comfortable or compact.");
            if (p.Font != null && !Fonts.Contains(p.Font)) problems.Add("Font must be one of " + string.Join(", ", Fonts) + ".");
            if (p.TextSize != null && !TextSizes.Contains(p.TextSize)) problems.Add("Text size must be sm, md, lg or xl.");
            if (p.HeaderColor != null && !Hex.IsMatch(p.HeaderColor)) problems.Add("Header colour must be a colour like #0b6bcb.");
            if (p.TextColor != null && !IsTextColor(p.TextColor)) problems.Add("Text colour must be a colour like #1f2937, or contrast.");
            return problems;
        }

        public string ToJson() => JsonSerializer.Serialize(this, Json);

        /// <summary>Saved JSON → preferences; anything unreadable or no longer valid is dropped.</summary>
        public static UserPreferences Parse(string? json)
        {
            if (string.IsNullOrWhiteSpace(json)) return new UserPreferences();
            try
            {
                var p = JsonSerializer.Deserialize<UserPreferences>(json, Json) ?? new UserPreferences();
                if (p.Theme != null && !Themes.Contains(p.Theme)) p.Theme = null;
                if (p.TimeZone != null && !TimeZones.Contains(p.TimeZone)) p.TimeZone = null;
                if (p.PageSize != null && !PageSizes.Contains(p.PageSize.Value)) p.PageSize = null;
                if (p.Density != null && !Densities.Contains(p.Density)) p.Density = null;
                if (p.Font != null && !Fonts.Contains(p.Font)) p.Font = null;
                if (p.TextSize != null && !TextSizes.Contains(p.TextSize)) p.TextSize = null;
                if (p.HeaderColor != null && !Hex.IsMatch(p.HeaderColor)) p.HeaderColor = null;
                if (p.TextColor != null && !IsTextColor(p.TextColor)) p.TextColor = null;
                return p;
            }
            catch (JsonException) { return new UserPreferences(); }
        }
    }
}
