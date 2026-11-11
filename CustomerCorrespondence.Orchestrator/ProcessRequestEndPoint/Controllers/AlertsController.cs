using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Options;
using ProcessRequestEndPoint.Alerts;
using ProcessRequestEndPoint.Auth;
using ProcessRequestEndPoint.Realtime;

namespace ProcessRequestEndPoint.Controllers
{
    /// <summary>Operational alerts and the change feed (the live-update fallback when WebSockets aren't available).</summary>
    [ApiController]
    [Route("api/portal")]
    public class AlertsController : ControllerBase
    {
        private readonly AlertRepository _alerts;
        private readonly AlertOptions _options;
        private readonly PortalAccessService _access;
        private readonly AuthRepository _auth;
        private readonly ChangeFeedRepository _feed;

        public AlertsController(AlertRepository alerts, IOptions<AlertOptions> options, PortalAccessService access, AuthRepository auth, ChangeFeedRepository feed)
        {
            _alerts = alerts;
            _options = options.Value;
            _access = access;
            _auth = auth;
            _feed = feed;
        }

        [RequirePermission(Permissions.AlertsRead)]
        [HttpGet("alerts")]
        public async Task<IActionResult> List([FromQuery] bool open = false, [FromQuery] int top = 50, CancellationToken ct = default) =>
            Ok(await _alerts.ListAsync(open, top, ct));

        [RequirePermission(Permissions.AlertsRead)]
        [HttpPost("alerts/{id:long}/acknowledge")]
        public async Task<IActionResult> Acknowledge(long id, CancellationToken ct)
        {
            var me = await _access.CurrentAsync();
            if (!await _alerts.AcknowledgeAsync(id, me!.UserName, ct)) return Conflict(new { message = "That alert is already acknowledged." });
            await _auth.WriteAuditAsync(me.UserId, me.UserName, "AlertAcknowledged", "Success", "alert " + id, _access.ClientIp());
            return NoContent();
        }

        /// <summary>The configured rules and notification channels (no secrets), for the Alerts page.</summary>
        [RequirePermission(Permissions.AlertsRead)]
        [HttpGet("alerts/rules")]
        public IActionResult Rules() => Ok(new
        {
            enabled = _options.Enabled,
            intervalMinutes = _options.IntervalMinutes,
            cooldownHours = _options.CooldownHours,
            rules = new object[]
            {
                new { key = "FailedRate", enabled = _options.FailedRate.Enabled,
                      description = $"Failed share of requests created in the last {_options.FailedRate.WindowHours} h is at least {_options.FailedRate.Multiplier}× the usual rate (and {_options.FailedRate.MinIncrease:P0} higher)" +
                                    (_options.FailedRate.AbsoluteThreshold > 0 ? $", or at least {_options.FailedRate.AbsoluteThreshold:P0}" : "") +
                                    $"; needs {_options.FailedRate.MinSample}+ requests." },
                new { key = "ProcessingBacklog", enabled = _options.ProcessingBacklog.Enabled,
                      description = $"{_options.ProcessingBacklog.Threshold}+ requests in progress ({string.Join(", ", _options.ProcessingBacklog.Statuses)}) for more than {_options.ProcessingBacklog.OlderThanHours} h." },
                new { key = "PdfFailures", enabled = _options.PdfFailures.Enabled,
                      description = $"{_options.PdfFailures.Threshold}+ PDFs failed every generation attempt." },
                new { key = "SlaBreaches", enabled = _options.SlaBreaches.Enabled,
                      description = $"{_options.SlaBreaches.Threshold}+ open requests passed their SLA (Administration > SLA) since the previous check; " +
                                    $"critical from {_options.SlaBreaches.CriticalThreshold}." }
            },
            channels = new
            {
                teams = !string.IsNullOrWhiteSpace(_options.Teams.WebhookUrl),
                email = !string.IsNullOrWhiteSpace(_options.Email.Host) && _options.Email.To.Count > 0 ? string.Join(", ", _options.Email.To) : null
            }
        });

        /// <summary>
        /// What changed since <paramref name="since"/> (UTC): new records, records with status updates, PDF changes and
        /// new alerts. Ids and counts only. Clients without a WebSocket poll this; pass back asOfUtc next time.
        /// </summary>
        [RequirePermission(Permissions.CorrespondenceRead)]
        [HttpGet("changes")]
        public async Task<IActionResult> Changes([FromQuery] DateTime? since, CancellationToken ct)
        {
            var from = since?.ToUniversalTime() ?? DateTime.UtcNow.AddMinutes(-1);
            if (from < DateTime.UtcNow.AddHours(-24)) from = DateTime.UtcNow.AddHours(-24);
            var me = await _access.CurrentAsync();
            var c = await _feed.SinceAsync(from, ct);
            return Ok(new
            {
                asOfUtc = c.AsOfUtc,
                newRecords = c.NewRecords,
                changedIds = c.ChangedIds,
                pdfChanges = c.PdfChanges.Select(p => new { correspondenceId = p.Id, status = p.Status }),
                newAlerts = me?.Has(Permissions.AlertsRead) == true ? c.NewAlerts : 0
            });
        }
    }
}
