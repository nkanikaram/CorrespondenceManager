using System.Net;
using System.Net.Mail;
using System.Security.Cryptography;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Options;
using ProcessRequestEndPoint.Alerts;

namespace ProcessRequestEndPoint.Auth
{
    /// <summary>
    /// "Forgot password?" for local accounts: issues one-time reset links and completes resets.
    ///
    /// The request endpoint never reveals whether an account exists: every request gets the same answer, the
    /// email is sent in the background (so response time doesn't depend on it), and every outcome is audited
    /// with the real reason for administrators. Only a SHA-256 hash of each token is stored.
    /// </summary>
    public sealed class PasswordResetService
    {
        private readonly AuthRepository _repository;
        private readonly IPasswordResetMailer _mailer;
        private readonly IMemoryCache _cache;
        private readonly PortalAuthOptions _options;
        private readonly string? _portalUrl;
        private readonly ILogger<PasswordResetService> _logger;

        public PasswordResetService(AuthRepository repository, IPasswordResetMailer mailer, IMemoryCache cache, IOptions<PortalAuthOptions> options,
            IConfiguration configuration, IHostEnvironment env, ILogger<PasswordResetService> logger)
        {
            _repository = repository;
            _mailer = mailer;
            _cache = cache;
            _options = options.Value;
            _logger = logger;
            _portalUrl = FirstNonEmpty(_options.PasswordReset.PortalUrl, configuration["Alerts:PortalUrl"])
                         ?? (env.IsDevelopment() ? "http://localhost:8080/" : null);
        }

        private PasswordResetOptions Reset => _options.PasswordReset;

        /// <summary>Whether the sign-in page should offer "Forgot password?".</summary>
        public bool IsAvailable => Reset.Enabled && _options.LocalLogin.Enabled && _mailer.CanSend && _portalUrl != null;

        /// <summary>Handles a "forgot password" request. Always completes the same way; the outcome only shows in the audit log.</summary>
        public async Task RequestAsync(string userNameOrEmail, string? ip, CancellationToken ct)
        {
            var value = userNameOrEmail.Trim();
            if (!IsAvailable || value.Length == 0 || value.Length > 256) return;

            if (!WithinIpLimit(ip))
            {
                await _repository.WriteAuditAsync(null, Truncate(value), "PasswordResetIgnored", "Failure", "Too many reset requests from this address", ip);
                return;
            }

            var user = await _repository.FindResetCandidateAsync(value, ct);
            var refusal = user == null ? "No single account matches"
                : !user.IsActive ? "Account deactivated"
                : user.ExternalProvider != null ? "Microsoft sign-in account (reset the password with Microsoft)"
                : string.IsNullOrWhiteSpace(user.Email) ? "Account has no email address"
                : _options.LocalLogin.AllowedUserNames.Count > 0 && !_options.LocalLogin.AllowedUserNames.Contains(user.UserName, StringComparer.OrdinalIgnoreCase)
                    ? "Password sign-in not allowed for this account"
                : await _repository.CountRecentResetRequestsAsync(user.UserId, TimeSpan.FromHours(1), ct) >= Reset.MaxRequestsPerHour
                    ? $"More than {Reset.MaxRequestsPerHour} reset emails in the last hour"
                : null;
            if (refusal != null)
            {
                await _repository.WriteAuditAsync(user?.UserId, user?.UserName ?? Truncate(value), "PasswordResetIgnored", "Failure", refusal, ip);
                return;
            }

            var token = Base64Url(RandomNumberGenerator.GetBytes(32));
            var expires = DateTime.UtcNow.AddMinutes(Reset.LinkLifetimeMinutes);
            await _repository.CreateResetTokenAsync(user!.UserId, Hash(token), user.SecurityStamp, expires, ip, ct);
            await _repository.WriteAuditAsync(user.UserId, user.UserName, "PasswordResetRequested", "Success", "Reset link emailed to " + MaskEmail(user.Email!), ip);

            // The token sits in the URL fragment (after #), which browsers never send to a server or in a Referer header.
            var link = _portalUrl!.TrimEnd('/') + "/#!/reset-password?token=" + token;
            _ = SendInBackground(() => _mailer.SendResetLinkAsync(user, link, expires, CancellationToken.None), "reset link", user.UserName);
        }

        /// <summary>The account a reset link is for, or null when the link is invalid, expired, used or superseded.</summary>
        public async Task<PasswordResetCandidate?> FindAsync(string? token, CancellationToken ct)
        {
            if (!Reset.Enabled || string.IsNullOrWhiteSpace(token) || token.Length > 100) return null;
            return await _repository.FindValidResetTokenAsync(Hash(token.Trim()), ct);
        }

        /// <summary>Sets the new password. Returns the problems with it, or null when the link is no longer valid.</summary>
        public async Task<PasswordResetResult> CompleteAsync(string? token, string? newPassword, string? ip, CancellationToken ct)
        {
            var user = await FindAsync(token, ct);
            if (user == null) return PasswordResetResult.InvalidLink;

            var problems = PasswordHasher.Validate(newPassword, user.UserName, _options.MinPasswordLength);
            if (problems.Count > 0) return PasswordResetResult.Weak(problems);

            var hash = PasswordHasher.Hash(newPassword!, _options.PasswordIterations);
            if (!await _repository.CompletePasswordResetAsync(user.TokenId!.Value, user.UserId, user.SecurityStamp, hash, ct))
                return PasswordResetResult.InvalidLink;

            await _repository.WriteAuditAsync(user.UserId, user.UserName, "PasswordReset", "Success", "Self-service reset by emailed link", ip);
            _ = SendInBackground(() => _mailer.SendPasswordChangedAsync(user, ip, CancellationToken.None), "password-changed notice", user.UserName);
            return PasswordResetResult.Done(user);
        }

        private bool WithinIpLimit(string? ip)
        {
            var key = "pwreset:ip:" + (ip ?? "unknown");
            var count = _cache.GetOrCreate(key, e => { e.AbsoluteExpirationRelativeToNow = TimeSpan.FromHours(1); return new int[1]; })!;
            return Interlocked.Increment(ref count[0]) <= Reset.MaxRequestsPerIpPerHour;
        }

        private async Task SendInBackground(Func<Task> send, string what, string userName)
        {
            await Task.Yield();
            try { await send(); }
            catch (Exception ex) { _logger.LogError(ex, "Could not email the {What} to portal user {UserName}", what, userName); }
        }

        internal static byte[] Hash(string token) => SHA256.HashData(System.Text.Encoding.UTF8.GetBytes(token));

        private static string Base64Url(byte[] bytes) => Convert.ToBase64String(bytes).TrimEnd('=').Replace('+', '-').Replace('/', '_');

        public static string MaskEmail(string email)
        {
            var at = email.IndexOf('@');
            if (at <= 0) return "***";
            return email[0] + new string('*', Math.Max(1, Math.Min(at - 1, 6))) + email[at..];
        }

        private static string Truncate(string value) => value.Length > 100 ? value[..100] : value;
        private static string? FirstNonEmpty(params string?[] values) => values.FirstOrDefault(v => !string.IsNullOrWhiteSpace(v));
    }

    public sealed class PasswordResetResult
    {
        public bool Succeeded { get; private init; }
        public bool LinkInvalid { get; private init; }
        public IReadOnlyList<string> Problems { get; private init; } = Array.Empty<string>();
        public PasswordResetCandidate? User { get; private init; }

        public static readonly PasswordResetResult InvalidLink = new() { LinkInvalid = true };
        public static PasswordResetResult Weak(IReadOnlyList<string> problems) => new() { Problems = problems };
        public static PasswordResetResult Done(PasswordResetCandidate user) => new() { Succeeded = true, User = user };
    }

    /// <summary>Delivers password-reset emails. Replaceable (tests capture the link instead of sending it).</summary>
    public interface IPasswordResetMailer
    {
        /// <summary>False when nothing can be delivered (no SMTP server and no development fallback).</summary>
        bool CanSend { get; }
        Task SendResetLinkAsync(PasswordResetCandidate user, string link, DateTime expiresUtc, CancellationToken ct);
        Task SendPasswordChangedAsync(PasswordResetCandidate user, string? ip, CancellationToken ct);
    }

    /// <summary>
    /// Sends through the SMTP server in PortalAuth:PasswordReset:Email, or the alerts one (Alerts:Email) when that isn't set.
    /// In Development with no SMTP server, the link is written to the API log instead.
    /// </summary>
    public sealed class SmtpPasswordResetMailer : IPasswordResetMailer
    {
        private readonly AlertOptions.EmailOptions? _smtp;
        private readonly bool _logOnly;
        private readonly ILogger<SmtpPasswordResetMailer> _logger;

        public SmtpPasswordResetMailer(IConfiguration configuration, IOptions<PortalAuthOptions> options, IHostEnvironment env, ILogger<SmtpPasswordResetMailer> logger)
        {
            _logger = logger;
            _smtp = new[] { "PortalAuth:PasswordReset:Email", "Alerts:Email" }
                .Select(path => configuration.GetSection(path).Get<AlertOptions.EmailOptions>())
                .FirstOrDefault(e => e != null && !string.IsNullOrWhiteSpace(e.Host) && !string.IsNullOrWhiteSpace(e.From));
            _logOnly = _smtp == null && env.IsDevelopment() && options.Value.PasswordReset.LogLinkInDevelopment;
        }

        public bool CanSend => _smtp != null || _logOnly;

        public Task SendResetLinkAsync(PasswordResetCandidate user, string link, DateTime expiresUtc, CancellationToken ct)
        {
            var minutes = Math.Max(1, (int)Math.Round((expiresUtc - DateTime.UtcNow).TotalMinutes));
            if (_logOnly)
            {
                _logger.LogWarning("DEVELOPMENT ONLY (no SMTP server configured): password reset link for {UserName}: {Link}", user.UserName, link);
                return Task.CompletedTask;
            }
            var text =
                $"Hello {user.DisplayName},\n\n" +
                $"Someone asked to reset the password for your Correspondence Manager account ({user.UserName}).\n\n" +
                $"Choose a new password here (the link works once, for the next {minutes} minutes):\n{link}\n\n" +
                "If you didn't ask for this, ignore this email: your password won't change. " +
                "Tell your portal administrator if you keep getting these emails.";
            var html =
                $"<p>Hello {WebUtility.HtmlEncode(user.DisplayName)},</p>" +
                $"<p>Someone asked to reset the password for your Correspondence Manager account (<strong>{WebUtility.HtmlEncode(user.UserName)}</strong>).</p>" +
                $"<p><a href=\"{WebUtility.HtmlEncode(link)}\" style=\"display:inline-block;padding:10px 18px;background:#0066cc;color:#ffffff;text-decoration:none;border-radius:6px;font-weight:600\">Choose a new password</a></p>" +
                $"<p style=\"color:#666666\">The link works once, for the next {minutes} minutes.</p>" +
                "<p style=\"color:#666666\">If you didn't ask for this, ignore this email: your password won't change. Tell your portal administrator if you keep getting these emails.</p>";
            return SendAsync(user.Email!, "Reset your Correspondence Manager password", text, html, ct);
        }

        public Task SendPasswordChangedAsync(PasswordResetCandidate user, string? ip, CancellationToken ct)
        {
            if (_logOnly)
            {
                _logger.LogInformation("DEVELOPMENT ONLY: password-changed notice for {UserName} not emailed (no SMTP server configured)", user.UserName);
                return Task.CompletedTask;
            }
            var when = DateTime.UtcNow.ToString("dd MMM yyyy HH:mm") + " UTC";
            var text =
                $"Hello {user.DisplayName},\n\n" +
                $"The password for your Correspondence Manager account ({user.UserName}) was changed on {when} using a reset link.\n\n" +
                "If this wasn't you, contact your portal administrator straight away.";
            var html =
                $"<p>Hello {WebUtility.HtmlEncode(user.DisplayName)},</p>" +
                $"<p>The password for your Correspondence Manager account (<strong>{WebUtility.HtmlEncode(user.UserName)}</strong>) was changed on {when} using a reset link.</p>" +
                "<p><strong>If this wasn't you, contact your portal administrator straight away.</strong></p>";
            return SendAsync(user.Email!, "Your Correspondence Manager password was changed", text, html, ct);
        }

        private async Task SendAsync(string to, string subject, string text, string html, CancellationToken ct)
        {
            var e = _smtp!;
            using var message = new MailMessage { From = new MailAddress(e.From!), Subject = subject, Body = text };
            message.To.Add(to);
            message.AlternateViews.Add(AlternateView.CreateAlternateViewFromString(html, System.Text.Encoding.UTF8, "text/html"));
            using var client = new SmtpClient(e.Host!, e.Port) { EnableSsl = e.EnableSsl };
            if (!string.IsNullOrWhiteSpace(e.UserName)) client.Credentials = new NetworkCredential(e.UserName, e.Password);
            await client.SendMailAsync(message, ct);
        }
    }
}
