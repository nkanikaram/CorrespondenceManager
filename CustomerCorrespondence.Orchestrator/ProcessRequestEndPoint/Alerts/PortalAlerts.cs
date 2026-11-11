using System.Data;
using System.Net;
using System.Net.Mail;
using System.Text;
using System.Text.Json;
using Microsoft.Data.SqlClient;
using Microsoft.Extensions.Options;
using ProcessRequestEndPoint.Auth;
using ProcessRequestEndPoint.Realtime;

namespace ProcessRequestEndPoint.Alerts
{
    /// <summary>"Alerts" configuration section.</summary>
    public sealed class AlertOptions
    {
        public const string SectionName = "Alerts";

        public bool Enabled { get; set; } = true;
        public int IntervalMinutes { get; set; } = 10;

        /// <summary>A rule doesn't raise again while its last alert is unacknowledged and younger than this.</summary>
        public int CooldownHours { get; set; } = 6;

        /// <summary>Link included in notifications.</summary>
        public string? PortalUrl { get; set; }

        public FailedRateRule FailedRate { get; set; } = new();
        public BacklogRule ProcessingBacklog { get; set; } = new();
        public PdfFailureRule PdfFailures { get; set; } = new();
        public SlaBreachRule SlaBreaches { get; set; } = new();
        public TeamsOptions Teams { get; set; } = new();
        public EmailOptions Email { get; set; } = new();

        /// <summary>Failed share of requests created in the window, compared with the average of earlier checks.</summary>
        public sealed class FailedRateRule
        {
            public bool Enabled { get; set; } = true;
            public int WindowHours { get; set; } = 24;
            public int MinSample { get; set; } = 20;
            public int BaselineChecks { get; set; } = 12;
            public double Multiplier { get; set; } = 1.5;
            public double MinIncrease { get; set; } = 0.10;
            /// <summary>Also alert when the rate is at least this, whatever the baseline (0 = off).</summary>
            public double AbsoluteThreshold { get; set; } = 0.25;
            public List<string> FailedStatuses { get; set; } = new() { "Failed", "Error" };
        }

        /// <summary>Requests stuck in an in-progress status for too long.</summary>
        public sealed class BacklogRule
        {
            public bool Enabled { get; set; } = true;
            public int OlderThanHours { get; set; } = 48;
            public int Threshold { get; set; } = 25;
            public List<string> Statuses { get; set; } = new() { "New", "Processing", "Submitted", "SentForPrint", "SentForImaging", "AwaitingImaging", "AwaitingEDelivery" };
        }

        /// <summary>PDFs that failed every attempt.</summary>
        public sealed class PdfFailureRule
        {
            public bool Enabled { get; set; } = true;
            public int Threshold { get; set; } = 1;
        }

        /// <summary>
        /// Open requests that passed their SLA (Administration > SLA) since the previous check. Counting new breaches,
        /// not every breached request, keeps an old backlog from raising an alert at every check.
        /// </summary>
        public sealed class SlaBreachRule
        {
            public bool Enabled { get; set; } = true;
            public int Threshold { get; set; } = 1;
            /// <summary>Critical from this many new breaches in one check.</summary>
            public int CriticalThreshold { get; set; } = 25;
        }

        public sealed class TeamsOptions
        {
            /// <summary>A Teams "Workflows" (Power Automate) webhook URL that posts an Adaptive Card. Keep it in Key Vault.</summary>
            public string? WebhookUrl { get; set; }
        }

        public sealed class EmailOptions
        {
            public string? Host { get; set; }
            public int Port { get; set; } = 587;
            public bool EnableSsl { get; set; } = true;
            public string? UserName { get; set; }
            /// <summary>Keep it in Key Vault / App Service settings, not in a file.</summary>
            public string? Password { get; set; }
            public string? From { get; set; }
            public List<string> To { get; set; } = new();
        }
    }

    public sealed class PortalAlert
    {
        public long AlertId { get; init; }
        public string RuleKey { get; init; } = string.Empty;
        public string Severity { get; init; } = "Warning";
        public string Title { get; init; } = string.Empty;
        public string Message { get; init; } = string.Empty;
        public double? MetricValue { get; init; }
        public double? BaselineValue { get; init; }
        public DateTime RaisedUtc { get; init; }
        public DateTime? AcknowledgedUtc { get; init; }
        public string? AcknowledgedBy { get; init; }
        public bool NotifiedTeams { get; init; }
        public bool NotifiedEmail { get; init; }
    }

    /// <summary>A rule outcome worth alerting on.</summary>
    public sealed record AlertCandidate(string RuleKey, string Severity, string Title, string Message, double? Value, double? Baseline);

    /// <summary>dbo.PortalAlert / dbo.PortalAlertMetric (Build/DBScripts/Portal/003_Alerts.sql) and the rule queries.</summary>
    public sealed class AlertRepository
    {
        private readonly string _connectionString;

        public AlertRepository(IConfiguration configuration, IOptions<PortalAuthOptions> authOptions)
        {
            var name = authOptions.Value.ConnectionStringName;
            _connectionString = configuration.GetConnectionString(name) ?? configuration[name]
                ?? throw new InvalidOperationException($"{name} is not configured.");
        }

        public string ConnectionString => _connectionString;

        private async Task<SqlConnection> OpenAsync(CancellationToken ct)
        {
            var conn = new SqlConnection(_connectionString);
            await conn.OpenAsync(ct);
            return conn;
        }

        private static string InList(SqlCommand cmd, string prefix, IReadOnlyList<string> values)
        {
            var names = new List<string>();
            for (var i = 0; i < values.Count; i++)
            {
                cmd.Parameters.Add(new SqlParameter(prefix + i, SqlDbType.VarChar, 255) { Value = values[i] });
                names.Add(prefix + i);
            }
            return names.Count == 0 ? "NULL" : string.Join(",", names);
        }

        private const string LatestStatusApply = @"
OUTER APPLY (SELECT TOP (1) COALESCE(sc.StatusDescription, h.StatusCode) AS LatestStatus, h.StatusUpdateTS
             FROM dbo.CorrespondenceRequestStatusHistory h LEFT JOIN dbo.CorrespondenceStatusCode sc ON sc.StatusCode = h.StatusCode
             WHERE h.CorrespondenceId = cr.CorrespondenceId ORDER BY h.HistorySequenceNum DESC, h.StatusUpdateTS DESC) ls";

        /// <summary>(failed, total) among requests created in the last <paramref name="hours"/>.</summary>
        public async Task<(int Failed, int Total)> FailedRateAsync(int hours, IReadOnlyList<string> failedStatuses, CancellationToken ct)
        {
            await using var conn = await OpenAsync(ct);
            await using var cmd = new SqlCommand { Connection = conn };
            var list = InList(cmd, "@f", failedStatuses);
            cmd.CommandText = $@"
SELECT SUM(CASE WHEN ls.LatestStatus IN ({list}) THEN 1 ELSE 0 END), COUNT(*)
FROM dbo.CorrespondenceRequest cr {LatestStatusApply}
WHERE cr.RequestCreationTS >= DATEADD(HOUR, -@hours, SYSUTCDATETIME())";
            cmd.Parameters.Add(new SqlParameter("@hours", SqlDbType.Int) { Value = hours });
            await using var r = await cmd.ExecuteReaderAsync(ct);
            await r.ReadAsync(ct);
            return (r.IsDBNull(0) ? 0 : r.GetInt32(0), r.GetInt32(1));
        }

        /// <summary>Requests whose latest status is in progress and whose last update is older than the cutoff.</summary>
        public async Task<int> StaleBacklogAsync(int olderThanHours, IReadOnlyList<string> statuses, CancellationToken ct)
        {
            await using var conn = await OpenAsync(ct);
            await using var cmd = new SqlCommand { Connection = conn };
            var list = InList(cmd, "@s", statuses);
            cmd.CommandText = $@"
SELECT COUNT(*) FROM dbo.CorrespondenceRequest cr {LatestStatusApply}
WHERE ls.LatestStatus IN ({list}) AND COALESCE(ls.StatusUpdateTS, cr.RequestCreationTS) < DATEADD(HOUR, -@hours, SYSUTCDATETIME())";
            cmd.Parameters.Add(new SqlParameter("@hours", SqlDbType.Int) { Value = olderThanHours });
            return Convert.ToInt32(await cmd.ExecuteScalarAsync(ct) ?? 0);
        }

        /// <summary>PDFs that failed and have no attempts left (0 when the PDF table doesn't exist).</summary>
        public async Task<int> ExhaustedPdfFailuresAsync(int maxAttempts, CancellationToken ct)
        {
            await using var conn = await OpenAsync(ct);
            await using var cmd = new SqlCommand(@"
IF OBJECT_ID('dbo.CorrespondencePdfDocument') IS NULL SELECT 0
ELSE EXEC sp_executesql N'SELECT COUNT(*) FROM dbo.CorrespondencePdfDocument WHERE Status = ''Failed'' AND Attempts >= @max', N'@max INT', @max = @max", conn);
            cmd.Parameters.Add(new SqlParameter("@max", SqlDbType.Int) { Value = maxAttempts });
            return Convert.ToInt32(await cmd.ExecuteScalarAsync(ct) ?? 0);
        }

        /// <summary>
        /// (newly breached since <paramref name="sinceUtc"/>, breached now, at risk now) among open requests, by the SLA settings.
        /// </summary>
        public async Task<(int New, int Breached, int AtRisk)> SlaPositionsAsync(Sla.SlaConfig sla, DateTime sinceUtc, DateTime nowUtc, CancellationToken ct)
        {
            await using var conn = await OpenAsync(ct);
            await using var cmd = new SqlCommand { Connection = conn };
            var n = 0;
            string Add(SqlDbType type, object value, int size)
            {
                var name = "@sla" + n++;
                var p = new SqlParameter(name, type) { Value = value };
                if (size != 0) p.Size = size;
                cmd.Parameters.Add(p);
                return name;
            }
            var q = new Sla.SlaSql(sla, nowUtc, Add, "cr.RequestCreationTS", "cr.RequestingDomainName", "ls.LatestStatus");
            cmd.CommandText = $@"
SELECT SUM(CASE WHEN {q.BreachedSince(sinceUtc)} THEN 1 ELSE 0 END),
       SUM(CASE WHEN {q.Condition("Breached")} THEN 1 ELSE 0 END),
       SUM(CASE WHEN {q.Condition("At risk")} THEN 1 ELSE 0 END)
FROM dbo.CorrespondenceRequest cr {LatestStatusApply}";
            await using var r = await cmd.ExecuteReaderAsync(ct);
            await r.ReadAsync(ct);
            int Get(int i) => r.IsDBNull(i) ? 0 : r.GetInt32(i);
            return (Get(0), Get(1), Get(2));
        }

        /// <summary>When the rule last recorded a metric (its previous check), or null.</summary>
        public async Task<DateTime?> LastCheckUtcAsync(string rule, CancellationToken ct)
        {
            await using var conn = await OpenAsync(ct);
            await using var cmd = new SqlCommand("SELECT MAX(CapturedUtc) FROM dbo.PortalAlertMetric WHERE RuleKey = @r", conn);
            cmd.Parameters.Add(new SqlParameter("@r", SqlDbType.VarChar, 50) { Value = rule });
            var v = await cmd.ExecuteScalarAsync(ct);
            return v is DateTime d ? DateTime.SpecifyKind(d, DateTimeKind.Utc) : null;
        }

        public async Task RecordMetricAsync(string rule, double value, int sample, CancellationToken ct)
        {
            await using var conn = await OpenAsync(ct);
            await using var cmd = new SqlCommand("INSERT dbo.PortalAlertMetric (RuleKey, Value, SampleSize) VALUES (@r, @v, @n)", conn);
            cmd.Parameters.Add(new SqlParameter("@r", SqlDbType.VarChar, 50) { Value = rule });
            cmd.Parameters.Add(new SqlParameter("@v", SqlDbType.Float) { Value = value });
            cmd.Parameters.Add(new SqlParameter("@n", SqlDbType.Int) { Value = sample });
            await cmd.ExecuteNonQueryAsync(ct);
        }

        /// <summary>Average of the last <paramref name="checks"/> recorded values (null with fewer than 2).</summary>
        public async Task<double?> BaselineAsync(string rule, int checks, CancellationToken ct)
        {
            await using var conn = await OpenAsync(ct);
            await using var cmd = new SqlCommand(
                "SELECT AVG(Value), COUNT(*) FROM (SELECT TOP (@n) Value FROM dbo.PortalAlertMetric WHERE RuleKey = @r ORDER BY CapturedUtc DESC) x", conn);
            cmd.Parameters.Add(new SqlParameter("@r", SqlDbType.VarChar, 50) { Value = rule });
            cmd.Parameters.Add(new SqlParameter("@n", SqlDbType.Int) { Value = checks });
            await using var r = await cmd.ExecuteReaderAsync(ct);
            await r.ReadAsync(ct);
            return r.GetInt32(1) >= 2 && !r.IsDBNull(0) ? r.GetDouble(0) : null;
        }

        public async Task<bool> InCooldownAsync(string rule, int hours, CancellationToken ct)
        {
            await using var conn = await OpenAsync(ct);
            await using var cmd = new SqlCommand(
                "SELECT COUNT(*) FROM dbo.PortalAlert WHERE RuleKey = @r AND AcknowledgedUtc IS NULL AND RaisedUtc > DATEADD(HOUR, -@h, SYSUTCDATETIME())", conn);
            cmd.Parameters.Add(new SqlParameter("@r", SqlDbType.VarChar, 50) { Value = rule });
            cmd.Parameters.Add(new SqlParameter("@h", SqlDbType.Int) { Value = hours });
            return Convert.ToInt32(await cmd.ExecuteScalarAsync(ct) ?? 0) > 0;
        }

        public async Task<long> RaiseAsync(AlertCandidate a, CancellationToken ct)
        {
            await using var conn = await OpenAsync(ct);
            await using var cmd = new SqlCommand(@"
INSERT dbo.PortalAlert (RuleKey, Severity, Title, Message, MetricValue, BaselineValue) OUTPUT inserted.AlertId VALUES (@r, @s, @t, @m, @v, @b)", conn);
            cmd.Parameters.Add(new SqlParameter("@r", SqlDbType.VarChar, 50) { Value = a.RuleKey });
            cmd.Parameters.Add(new SqlParameter("@s", SqlDbType.VarChar, 10) { Value = a.Severity });
            cmd.Parameters.Add(new SqlParameter("@t", SqlDbType.NVarChar, 200) { Value = a.Title });
            cmd.Parameters.Add(new SqlParameter("@m", SqlDbType.NVarChar, 2000) { Value = a.Message });
            cmd.Parameters.Add(new SqlParameter("@v", SqlDbType.Float) { Value = (object?)a.Value ?? DBNull.Value });
            cmd.Parameters.Add(new SqlParameter("@b", SqlDbType.Float) { Value = (object?)a.Baseline ?? DBNull.Value });
            return (long)(await cmd.ExecuteScalarAsync(ct))!;
        }

        public async Task MarkNotifiedAsync(long id, bool teams, bool email, string? error, CancellationToken ct)
        {
            await using var conn = await OpenAsync(ct);
            await using var cmd = new SqlCommand("UPDATE dbo.PortalAlert SET NotifiedTeams = @t, NotifiedEmail = @e, NotifyError = @err WHERE AlertId = @id", conn);
            cmd.Parameters.Add(new SqlParameter("@id", SqlDbType.BigInt) { Value = id });
            cmd.Parameters.Add(new SqlParameter("@t", SqlDbType.Bit) { Value = teams });
            cmd.Parameters.Add(new SqlParameter("@e", SqlDbType.Bit) { Value = email });
            cmd.Parameters.Add(new SqlParameter("@err", SqlDbType.NVarChar, 1000) { Value = (object?)error ?? DBNull.Value });
            await cmd.ExecuteNonQueryAsync(ct);
        }

        public async Task<List<PortalAlert>> ListAsync(bool openOnly, int top, CancellationToken ct)
        {
            var list = new List<PortalAlert>();
            await using var conn = await OpenAsync(ct);
            await using var cmd = new SqlCommand(@"
SELECT TOP (@top) AlertId, RuleKey, Severity, Title, Message, MetricValue, BaselineValue, RaisedUtc, AcknowledgedUtc, AcknowledgedBy, NotifiedTeams, NotifiedEmail
FROM dbo.PortalAlert" + (openOnly ? " WHERE AcknowledgedUtc IS NULL" : "") + " ORDER BY RaisedUtc DESC", conn);
            cmd.Parameters.Add(new SqlParameter("@top", SqlDbType.Int) { Value = Math.Clamp(top, 1, 500) });
            await using var r = await cmd.ExecuteReaderAsync(ct);
            while (await r.ReadAsync(ct))
            {
                list.Add(new PortalAlert
                {
                    AlertId = r.GetInt64(0), RuleKey = r.GetString(1), Severity = r.GetString(2), Title = r.GetString(3), Message = r.GetString(4),
                    MetricValue = r.IsDBNull(5) ? null : r.GetDouble(5), BaselineValue = r.IsDBNull(6) ? null : r.GetDouble(6), RaisedUtc = r.GetDateTime(7),
                    AcknowledgedUtc = r.IsDBNull(8) ? null : r.GetDateTime(8), AcknowledgedBy = r.IsDBNull(9) ? null : r.GetString(9),
                    NotifiedTeams = r.GetBoolean(10), NotifiedEmail = r.GetBoolean(11)
                });
            }
            return list;
        }

        public async Task<bool> AcknowledgeAsync(long id, string by, CancellationToken ct)
        {
            await using var conn = await OpenAsync(ct);
            await using var cmd = new SqlCommand(
                "UPDATE dbo.PortalAlert SET AcknowledgedUtc = SYSUTCDATETIME(), AcknowledgedBy = @by WHERE AlertId = @id AND AcknowledgedUtc IS NULL", conn);
            cmd.Parameters.Add(new SqlParameter("@id", SqlDbType.BigInt) { Value = id });
            cmd.Parameters.Add(new SqlParameter("@by", SqlDbType.NVarChar, 100) { Value = by });
            return await cmd.ExecuteNonQueryAsync(ct) > 0;
        }
    }

    /// <summary>The alert rules. Pure decisions over measured values, so they can be unit-tested.</summary>
    public static class AlertRules
    {
        public static AlertCandidate? FailedRate(int failed, int total, double? baseline, AlertOptions.FailedRateRule rule)
        {
            if (total < rule.MinSample) return null;
            var rate = (double)failed / total;
            var aboveBaseline = baseline is > 0 && rate >= baseline.Value * rule.Multiplier && rate - baseline.Value >= rule.MinIncrease;
            var aboveAbsolute = rule.AbsoluteThreshold > 0 && rate >= rule.AbsoluteThreshold;
            if (!aboveBaseline && !aboveAbsolute) return null;
            var message = $"{failed} of {total} requests created in the last {rule.WindowHours} hours failed ({rate:P0})" +
                          (baseline.HasValue ? $", against a usual rate of {baseline.Value:P0}." : ".");
            // Critical once at least half of recent requests are failing.
            return new AlertCandidate("FailedRate", rate >= 0.5 ? "Critical" : "Warning",
                $"Failed rate is {rate:P0}", message, rate, baseline);
        }

        public static AlertCandidate? Backlog(int stale, AlertOptions.BacklogRule rule) =>
            stale < rule.Threshold ? null : new AlertCandidate("ProcessingBacklog", stale >= rule.Threshold * 4 ? "Critical" : "Warning",
                $"{stale} requests stuck in progress",
                $"{stale} requests have been in an in-progress status for more than {rule.OlderThanHours} hours.", stale, rule.Threshold);

        public static AlertCandidate? PdfFailures(int failed, AlertOptions.PdfFailureRule rule) =>
            failed < rule.Threshold ? null : new AlertCandidate("PdfFailures", "Warning",
                $"{failed} PDF{(failed == 1 ? "" : "s")} failed",
                $"{failed} correspondence PDF{(failed == 1 ? " has" : "s have")} failed every generation attempt. Open the record and regenerate, or check the payload key.",
                failed, rule.Threshold);

        /// <summary>New SLA breaches since the previous check; the message gives the totals for context.</summary>
        public static AlertCandidate? SlaBreaches(int newlyBreached, int breached, int atRisk, TimeSpan window, AlertOptions.SlaBreachRule rule)
        {
            if (newlyBreached < Math.Max(1, rule.Threshold)) return null;
            var many = newlyBreached != 1;
            var since = window.TotalMinutes < 90 ? $"{Math.Max(1, (int)Math.Round(window.TotalMinutes))} minutes" : $"{Math.Round(window.TotalHours, 1)} hours";
            var message = $"{newlyBreached} open request{(many ? "s" : "")} passed {(many ? "their" : "its")} SLA in the last {since}. " +
                          $"{breached} open request{(breached == 1 ? " is" : "s are")} past the SLA in total" +
                          (atRisk > 0 ? $", and {atRisk} more {(atRisk == 1 ? "is" : "are")} at risk." : ".") +
                          " In the portal, choose \"Breached only\" above the grid to see them.";
            return new AlertCandidate("SlaBreaches", newlyBreached >= rule.CriticalThreshold ? "Critical" : "Warning",
                $"{newlyBreached} request{(many ? "s" : "")} breached the SLA", message, newlyBreached, breached);
        }
    }

    /// <summary>Sends an alert to Teams (Workflows webhook, Adaptive Card) and/or email (SMTP).</summary>
    public sealed class AlertNotifier
    {
        private readonly IHttpClientFactory _http;
        private readonly AlertOptions _options;
        private readonly ILogger<AlertNotifier> _logger;

        public AlertNotifier(IHttpClientFactory http, IOptions<AlertOptions> options, ILogger<AlertNotifier> logger)
        {
            _http = http;
            _options = options.Value;
            _logger = logger;
        }

        public async Task<(bool Teams, bool Email, string? Error)> NotifyAsync(PortalAlert alert, CancellationToken ct)
        {
            bool teams = false, email = false;
            var errors = new List<string>();
            if (!string.IsNullOrWhiteSpace(_options.Teams.WebhookUrl))
            {
                try { await SendTeamsAsync(alert, ct); teams = true; }
                catch (Exception ex) { errors.Add("Teams: " + ex.Message); _logger.LogWarning(ex, "Teams alert notification failed"); }
            }
            if (!string.IsNullOrWhiteSpace(_options.Email.Host) && _options.Email.To.Count > 0 && !string.IsNullOrWhiteSpace(_options.Email.From))
            {
                try { await SendEmailAsync(alert, ct); email = true; }
                catch (Exception ex) { errors.Add("Email: " + ex.Message); _logger.LogWarning(ex, "Email alert notification failed"); }
            }
            return (teams, email, errors.Count > 0 ? string.Join(" | ", errors) : null);
        }

        /// <summary>The portal link for an alert: SLA breaches open the grid filtered to breached requests.</summary>
        public static string? LinkFor(PortalAlert alert, string? portalUrl)
        {
            if (string.IsNullOrWhiteSpace(portalUrl)) return null;
            if (alert.RuleKey != "SlaBreaches" || portalUrl.Contains('#')) return portalUrl;
            return portalUrl.TrimEnd('/') + "/#!/?sla=breached";
        }

        private async Task SendTeamsAsync(PortalAlert alert, CancellationToken ct)
        {
            var link = LinkFor(alert, _options.PortalUrl);
            var facts = new List<object> { new { title = "Severity", value = alert.Severity }, new { title = "Raised", value = alert.RaisedUtc.ToString("u") } };
            var body = new List<object>
            {
                new { type = "TextBlock", text = "ECCO Correspondence Manager", size = "Small", isSubtle = true },
                new { type = "TextBlock", text = alert.Title, weight = "Bolder", size = "Medium", color = alert.Severity == "Critical" ? "Attention" : "Warning", wrap = true },
                new { type = "TextBlock", text = alert.Message, wrap = true },
                new { type = "FactSet", facts }
            };
            var actions = link == null
                ? Array.Empty<object>()
                : new object[] { new { type = "Action.OpenUrl", title = alert.RuleKey == "SlaBreaches" ? "Show breached requests" : "Open the portal", url = link } };
            var payload = new
            {
                type = "message",
                attachments = new[]
                {
                    new { contentType = "application/vnd.microsoft.card.adaptive", content = new { type = "AdaptiveCard", version = "1.4", body, actions } }
                }
            };
            using var client = _http.CreateClient("alerts");
            client.Timeout = TimeSpan.FromSeconds(15);
            using var response = await client.PostAsync(_options.Teams.WebhookUrl, new StringContent(JsonSerializer.Serialize(payload), Encoding.UTF8, "application/json"), ct);
            response.EnsureSuccessStatusCode();
        }

        private async Task SendEmailAsync(PortalAlert alert, CancellationToken ct)
        {
            var e = _options.Email;
            using var message = new MailMessage { From = new MailAddress(e.From!), Subject = $"[{alert.Severity}] ECCO: {alert.Title}" };
            foreach (var to in e.To) message.To.Add(to);
            message.Body = $"{alert.Message}\n\nRaised: {alert.RaisedUtc:u}\nSeverity: {alert.Severity}" +
                           (LinkFor(alert, _options.PortalUrl) is { } link ? $"\n\nOpen the portal: {link}" : "");
            using var smtp = new SmtpClient(e.Host!, e.Port) { EnableSsl = e.EnableSsl };
            if (!string.IsNullOrWhiteSpace(e.UserName)) smtp.Credentials = new NetworkCredential(e.UserName, e.Password);
            await smtp.SendMailAsync(message, ct);
        }
    }

    /// <summary>
    /// Scheduled checks. Every IntervalMinutes one API instance (SQL application lock) measures each rule, records the
    /// metric, raises an alert when a rule fires (respecting the cooldown), notifies Teams / email and pushes it live.
    /// </summary>
    public sealed class AlertWorker : BackgroundService
    {
        private readonly IServiceScopeFactory _scopes;
        private readonly AlertOptions _options;
        private readonly ILogger<AlertWorker> _logger;

        public AlertWorker(IServiceScopeFactory scopes, IOptions<AlertOptions> options, ILogger<AlertWorker> logger)
        {
            _scopes = scopes;
            _options = options.Value;
            _logger = logger;
        }

        protected override async Task ExecuteAsync(CancellationToken stoppingToken)
        {
            try { await Task.Delay(TimeSpan.FromSeconds(30), stoppingToken); } catch (OperationCanceledException) { return; }
            while (!stoppingToken.IsCancellationRequested)
            {
                try { await RunOnceAsync(stoppingToken); }
                catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested) { break; }
                catch (Exception ex) { _logger.LogError(ex, "Alert checks failed. Has Build/DBScripts/Portal/003_Alerts.sql been applied?"); }
                try { await Task.Delay(TimeSpan.FromMinutes(Math.Max(1, _options.IntervalMinutes)), stoppingToken); } catch (OperationCanceledException) { break; }
            }
        }

        /// <summary>One run; returns the alerts raised (empty when another instance holds the lock). Public for tests.</summary>
        public async Task<List<PortalAlert>> RunOnceAsync(CancellationToken ct)
        {
            using var scope = _scopes.CreateScope();
            var repo = scope.ServiceProvider.GetRequiredService<AlertRepository>();
            var raised = new List<PortalAlert>();

            await using var lockConn = new SqlConnection(repo.ConnectionString);
            await lockConn.OpenAsync(ct);
            await using (var acquire = new SqlCommand("DECLARE @r INT; EXEC @r = sp_getapplock @Resource = 'ecco-portal-alerts', @LockMode = 'Exclusive', @LockOwner = 'Session', @LockTimeout = 0; SELECT @r;", lockConn))
            {
                if (Convert.ToInt32(await acquire.ExecuteScalarAsync(ct)) < 0) return raised; // another instance is checking
            }

            var candidates = new List<AlertCandidate>();
            var fr = _options.FailedRate;
            if (fr.Enabled)
            {
                var (failed, total) = await repo.FailedRateAsync(fr.WindowHours, fr.FailedStatuses, ct);
                var baseline = await repo.BaselineAsync("FailedRate", fr.BaselineChecks, ct);
                if (total >= fr.MinSample) await repo.RecordMetricAsync("FailedRate", (double)failed / total, total, ct);
                if (AlertRules.FailedRate(failed, total, baseline, fr) is { } c) candidates.Add(c);
            }
            if (_options.ProcessingBacklog.Enabled)
            {
                var stale = await repo.StaleBacklogAsync(_options.ProcessingBacklog.OlderThanHours, _options.ProcessingBacklog.Statuses, ct);
                await repo.RecordMetricAsync("ProcessingBacklog", stale, stale, ct);
                if (AlertRules.Backlog(stale, _options.ProcessingBacklog) is { } c) candidates.Add(c);
            }
            if (_options.PdfFailures.Enabled)
            {
                var maxAttempts = scope.ServiceProvider.GetService<IOptions<Pdf.PdfGenerationOptions>>()?.Value.MaxAttempts ?? 5;
                var failed = await repo.ExhaustedPdfFailuresAsync(maxAttempts, ct);
                if (AlertRules.PdfFailures(failed, _options.PdfFailures) is { } c) candidates.Add(c);
            }

            if (_options.SlaBreaches.Enabled && scope.ServiceProvider.GetService<Sla.SlaService>() is { } slaService &&
                await slaService.GetAsync(ct) is { } sla)
            {
                var now = DateTime.UtcNow;
                // Since the previous check (at most a day back); the first check looks back one interval.
                var since = await repo.LastCheckUtcAsync("SlaBreaches", ct) ?? now.AddMinutes(-Math.Max(1, _options.IntervalMinutes));
                if (since < now.AddDays(-1)) since = now.AddDays(-1);
                var (fresh, breached, atRisk) = await repo.SlaPositionsAsync(sla, since, now, ct);
                await repo.RecordMetricAsync("SlaBreaches", breached, fresh, ct);
                if (AlertRules.SlaBreaches(fresh, breached, atRisk, now - since, _options.SlaBreaches) is { } c) candidates.Add(c);
            }

            var notifier = scope.ServiceProvider.GetRequiredService<AlertNotifier>();
            var events = scope.ServiceProvider.GetService<PortalEvents>();
            foreach (var candidate in candidates)
            {
                if (await repo.InCooldownAsync(candidate.RuleKey, _options.CooldownHours, ct)) continue;
                var id = await repo.RaiseAsync(candidate, ct);
                var alert = new PortalAlert
                {
                    AlertId = id, RuleKey = candidate.RuleKey, Severity = candidate.Severity, Title = candidate.Title, Message = candidate.Message,
                    MetricValue = candidate.Value, BaselineValue = candidate.Baseline, RaisedUtc = DateTime.UtcNow
                };
                var (teams, email, error) = await notifier.NotifyAsync(alert, ct);
                await repo.MarkNotifiedAsync(id, teams, email, error, ct);
                if (events != null) await events.AlertRaisedAsync(id, alert.RuleKey, alert.Severity, alert.Title);
                _logger.LogWarning("Alert raised: {Title} (teams={Teams}, email={Email})", alert.Title, teams, email);
                raised.Add(alert);
            }
            return raised; // closing lockConn releases the session lock
        }
    }
}
