using System.Data;
using Microsoft.AspNetCore.SignalR;
using Microsoft.Data.SqlClient;
using Microsoft.Extensions.Options;
using ProcessRequestEndPoint.Auth;

namespace ProcessRequestEndPoint.Realtime
{
    /// <summary>
    /// Live updates for the portal (/hubs/portal). Signed-in portal users only (the token arrives as the
    /// access_token query value on the WebSocket). Messages carry ids and counts — never record contents:
    ///   recordsChanged { newRecords, changedIds[] }   pdfStatusChanged { correspondenceId, status }
    ///   alertRaised { alertId, ruleKey, severity, title }  (only to users with alerts.read)
    ///   sessionsEnded {}  (to one user's connections when their sessions were ended; each browser re-checks its token)
    /// </summary>
    [RequirePortalUser]
    public sealed class PortalHub : Hub
    {
        public const string Path = "/hubs/portal";
        public const string AlertsGroup = "alerts";

        /// <summary>Every connection of one user, so they can be told when their sessions end.</summary>
        public static string UserGroup(Guid userId) => "user:" + userId.ToString("N");
        private readonly PortalAccessService _access;

        public PortalHub(PortalAccessService access) => _access = access;

        public override async Task OnConnectedAsync()
        {
            var user = Context.User == null ? null : await _access.LoadValidatedAsync(Context.User);
            if (user == null) { Context.Abort(); return; }
            if (user.Has(Permissions.AlertsRead)) await Groups.AddToGroupAsync(Context.ConnectionId, AlertsGroup);
            await Groups.AddToGroupAsync(Context.ConnectionId, UserGroup(user.UserId));
            await base.OnConnectedAsync();
        }
    }

    /// <summary>Sends portal events to connected clients (no-op when nobody is connected).</summary>
    public sealed class PortalEvents
    {
        private readonly IHubContext<PortalHub> _hub;
        public PortalEvents(IHubContext<PortalHub> hub) => _hub = hub;

        public Task PdfStatusChangedAsync(Guid id, string status) =>
            _hub.Clients.All.SendAsync("pdfStatusChanged", new { correspondenceId = id, status });

        public Task RecordsChangedAsync(int newRecords, IReadOnlyCollection<Guid> changedIds) =>
            _hub.Clients.All.SendAsync("recordsChanged", new { newRecords, changedIds });

        public Task AlertRaisedAsync(long alertId, string ruleKey, string severity, string title) =>
            _hub.Clients.Group(PortalHub.AlertsGroup).SendAsync("alertRaised", new { alertId, ruleKey, severity, title });

        /// <summary>
        /// The user's security stamp changed (sign out other sessions, password change or reset, an admin edit), so
        /// their older tokens no longer work. Each open browser re-checks its own token and signs out if it's stale,
        /// instead of looking signed in until its next request.
        /// </summary>
        public Task SessionsEndedAsync(Guid userId) =>
            _hub.Clients.Group(PortalHub.UserGroup(userId)).SendAsync("sessionsEnded", new { });

        /// <summary>Best effort from a controller: live updates may not be set up, and a failed push must not fail the request.</summary>
        public static async Task NotifySessionsEndedAsync(HttpContext http, Guid userId)
        {
            var events = http.RequestServices.GetService<PortalEvents>();
            if (events == null) return;
            try { await events.SessionsEndedAsync(userId); }
            catch (Exception ex) { http.RequestServices.GetService<ILogger<PortalEvents>>()?.LogWarning(ex, "Could not push sessionsEnded"); }
        }
    }

    public sealed class ChangeSet
    {
        public DateTime AsOfUtc { get; init; }
        public int NewRecords { get; init; }
        public List<Guid> ChangedIds { get; init; } = new();
        public List<(Guid Id, string Status)> PdfChanges { get; init; } = new();
        public int NewAlerts { get; init; }
    }

    /// <summary>
    /// What changed since a point in time: new requests, status updates (CorrespondenceRequestStatusHistory), PDF
    /// state changes and new alerts. Feeds the hub, and GET /api/portal/changes for clients that can't hold a
    /// WebSocket open. Ids only; the client re-fetches through the normal, permission-checked endpoints.
    /// </summary>
    public sealed class ChangeFeedRepository
    {
        private readonly string _connectionString;

        public ChangeFeedRepository(IConfiguration configuration, IOptions<PortalAuthOptions> authOptions)
        {
            var name = authOptions.Value.ConnectionStringName;
            _connectionString = configuration.GetConnectionString(name) ?? configuration[name]
                ?? throw new InvalidOperationException($"{name} is not configured.");
        }

        public async Task<ChangeSet> SinceAsync(DateTime sinceUtc, CancellationToken ct)
        {
            var asOf = DateTime.UtcNow;
            const string sql = @"
SELECT COUNT(*) FROM dbo.CorrespondenceRequest WHERE RequestCreationTS > @since AND RequestCreationTS <= @asOf;
SELECT DISTINCT TOP (200) CorrespondenceId FROM dbo.CorrespondenceRequestStatusHistory WHERE StatusUpdateTS > @since AND StatusUpdateTS <= @asOf;
IF OBJECT_ID('dbo.CorrespondencePdfDocument') IS NOT NULL
    EXEC sp_executesql N'SELECT TOP (200) CorrespondenceId, Status FROM dbo.CorrespondencePdfDocument WHERE UpdatedUtc > @since AND UpdatedUtc <= @asOf',
         N'@since DATETIME2, @asOf DATETIME2', @since = @since, @asOf = @asOf;
ELSE SELECT CAST(NULL AS UNIQUEIDENTIFIER), CAST(NULL AS VARCHAR(20)) WHERE 1 = 0;
IF OBJECT_ID('dbo.PortalAlert') IS NOT NULL
    EXEC sp_executesql N'SELECT COUNT(*) FROM dbo.PortalAlert WHERE RaisedUtc > @since AND RaisedUtc <= @asOf', N'@since DATETIME2, @asOf DATETIME2', @since = @since, @asOf = @asOf;
ELSE SELECT 0;";
            await using var conn = new SqlConnection(_connectionString);
            await conn.OpenAsync(ct);
            await using var cmd = new SqlCommand(sql, conn);
            cmd.Parameters.Add(new SqlParameter("@since", SqlDbType.DateTime2) { Value = sinceUtc });
            cmd.Parameters.Add(new SqlParameter("@asOf", SqlDbType.DateTime2) { Value = asOf });
            await using var r = await cmd.ExecuteReaderAsync(ct);
            await r.ReadAsync(ct);
            var newRecords = r.GetInt32(0);
            var changed = new List<Guid>();
            await r.NextResultAsync(ct);
            while (await r.ReadAsync(ct)) changed.Add(r.GetGuid(0));
            var pdfs = new List<(Guid, string)>();
            await r.NextResultAsync(ct);
            while (await r.ReadAsync(ct)) pdfs.Add((r.GetGuid(0), r.GetString(1)));
            await r.NextResultAsync(ct);
            var alerts = await r.ReadAsync(ct) ? r.GetInt32(0) : 0;
            return new ChangeSet { AsOfUtc = asOf, NewRecords = newRecords, ChangedIds = changed, PdfChanges = pdfs, NewAlerts = alerts };
        }
    }

    /// <summary>Every 15 seconds, pushes record changes to connected clients (each API instance to its own clients).</summary>
    public sealed class ChangeFeedWorker : BackgroundService
    {
        private readonly ChangeFeedRepository _feed;
        private readonly PortalEvents _events;
        private readonly ILogger<ChangeFeedWorker> _logger;

        public ChangeFeedWorker(ChangeFeedRepository feed, PortalEvents events, ILogger<ChangeFeedWorker> logger)
        {
            _feed = feed;
            _events = events;
            _logger = logger;
        }

        protected override async Task ExecuteAsync(CancellationToken stoppingToken)
        {
            var since = DateTime.UtcNow;
            while (!stoppingToken.IsCancellationRequested)
            {
                try { await Task.Delay(TimeSpan.FromSeconds(15), stoppingToken); } catch (OperationCanceledException) { break; }
                try
                {
                    var changes = await _feed.SinceAsync(since, stoppingToken);
                    since = changes.AsOfUtc;
                    if (changes.NewRecords > 0 || changes.ChangedIds.Count > 0)
                        await _events.RecordsChangedAsync(changes.NewRecords, changes.ChangedIds);
                }
                catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested) { break; }
                catch (Exception ex)
                {
                    _logger.LogWarning(ex, "Change feed check failed");
                }
            }
        }
    }
}
