using System.Data;
using Microsoft.Data.SqlClient;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Options;
using ProcessRequestEndPoint.Auth;

namespace ProcessRequestEndPoint.Sla
{
    /// <summary>The SLA settings (Build/DBScripts/Portal/004_Sla.sql), edited under Administration > SLA.</summary>
    public sealed class SlaConfig
    {
        /// <summary>Count Monday–Friday only (true) or every day.</summary>
        public bool BusinessDays { get; set; } = true;

        /// <summary>"At risk" once this share of the SLA is used (1–99).</summary>
        public int AtRiskPercent { get; set; } = 80;

        /// <summary>Statuses (as shown in the portal) that finish a request and stop its clock.</summary>
        public List<string> StopStatuses { get; set; } = new();

        /// <summary>SLA in days for every customer without its own target.</summary>
        public decimal DefaultDays { get; set; } = 5;

        /// <summary>Per-customer (requesting domain) targets.</summary>
        public List<SlaCustomerTarget> Customers { get; set; } = new();

        public DateTime? UpdatedUtc { get; set; }
        public string? UpdatedBy { get; set; }

        public decimal DaysFor(string? customer)
        {
            if (!string.IsNullOrWhiteSpace(customer))
            {
                var match = Customers.FirstOrDefault(c => string.Equals(c.CustomerName, customer, StringComparison.OrdinalIgnoreCase));
                if (match != null) return match.TargetDays;
            }
            return DefaultDays;
        }

        public bool IsStopStatus(string? status) =>
            !string.IsNullOrWhiteSpace(status) && StopStatuses.Any(s => string.Equals(s, status, StringComparison.OrdinalIgnoreCase));
    }

    public sealed class SlaCustomerTarget
    {
        public string CustomerName { get; set; } = string.Empty;
        public decimal TargetDays { get; set; }
    }

    /// <summary>One request's SLA position, returned with each search result.</summary>
    public sealed class SlaResult
    {
        /// <summary>OnTrack | AtRisk | Breached (still open) · Met | Missed (finished on time / late).</summary>
        public string State { get; init; } = "OnTrack";
        public decimal TargetDays { get; init; }
        public bool BusinessDays { get; init; }
        public DateTime DueUtc { get; init; }

        /// <summary>When the clock stopped (a finishing status), or null while it's running.</summary>
        public DateTime? StoppedUtc { get; init; }

        /// <summary>Wall-clock time from creation to now (or to when it finished): the "time lapse".</summary>
        public long ElapsedSeconds { get; init; }

        /// <summary>Share of the SLA used, counted the SLA's way (business or calendar days). Over 100 = breached.</summary>
        public int PercentUsed { get; init; }
    }

    public static class SlaCalculator
    {
        public static SlaResult Evaluate(DateTime createdUtc, string? latestStatus, DateTime? latestStatusUtc, string? customer, SlaConfig config, DateTime nowUtc)
        {
            var days = config.DaysFor(customer);
            var stopped = config.IsStopStatus(latestStatus) ? (latestStatusUtc ?? nowUtc) : (DateTime?)null;
            var end = stopped ?? nowUtc;
            if (end < createdUtc) end = createdUtc;

            var target = TimeSpan.FromHours((double)days * 24);
            var used = config.BusinessDays ? BusinessTime(createdUtc, end) : end - createdUtc;
            var due = config.BusinessDays ? AddBusinessTime(createdUtc, target) : createdUtc + target;
            var percent = target <= TimeSpan.Zero ? 0 : (int)Math.Floor(used.TotalSeconds / target.TotalSeconds * 100);

            string state;
            if (stopped != null) state = used <= target ? "Met" : "Missed";
            else if (used > target) state = "Breached";
            else if (percent >= config.AtRiskPercent) state = "AtRisk";
            else state = "OnTrack";

            return new SlaResult
            {
                State = state,
                TargetDays = days,
                BusinessDays = config.BusinessDays,
                DueUtc = due,
                StoppedUtc = stopped,
                ElapsedSeconds = (long)(end - createdUtc).TotalSeconds,
                PercentUsed = percent
            };
        }

        /// <summary>Time between two instants that falls on Monday–Friday (UTC).</summary>
        public static TimeSpan BusinessTime(DateTime start, DateTime end)
        {
            if (end <= start) return TimeSpan.Zero;
            var total = TimeSpan.Zero;
            var cursor = start;
            // Whole weeks first (5 business days each), then day by day for the remainder.
            var weeks = (int)((end - start).TotalDays / 7);
            if (weeks > 0) { total += TimeSpan.FromDays(5 * weeks); cursor = start.AddDays(7 * weeks); }
            while (cursor < end)
            {
                var nextMidnight = cursor.Date.AddDays(1);
                var sliceEnd = nextMidnight < end ? nextMidnight : end;
                if (cursor.DayOfWeek is not (DayOfWeek.Saturday or DayOfWeek.Sunday)) total += sliceEnd - cursor;
                cursor = sliceEnd;
            }
            return total;
        }

        /// <summary>
        /// The latest creation time whose clock has used at least <paramref name="amount"/> by <paramref name="end"/>
        /// (business or calendar time). Requests created at or before it have used that much.
        /// </summary>
        public static DateTime LatestStartWithUsed(DateTime end, TimeSpan amount, bool businessDays)
        {
            if (!businessDays) return end - amount;
            var cursor = end;
            var left = amount;
            while (true)
            {
                // The part of the day that ends at the cursor (a cursor at midnight takes the whole previous day).
                var sliceStart = cursor.TimeOfDay == TimeSpan.Zero ? cursor.AddDays(-1) : cursor.Date;
                if (sliceStart.DayOfWeek is not (DayOfWeek.Saturday or DayOfWeek.Sunday))
                {
                    var length = cursor - sliceStart;
                    if (left <= length) return cursor - left;
                    left -= length;
                }
                cursor = sliceStart;
            }
        }

        /// <summary>
        /// Creation-time cutoffs for one SLA target at <paramref name="nowUtc"/>, for an open request: created before
        /// BreachedBefore = Breached; else created at or before AtRiskUpTo = AtRisk; else OnTrack. Matches
        /// <see cref="Evaluate"/>, so the grid's SLA filter and its SLA column agree.
        /// </summary>
        public static (DateTime BreachedBefore, DateTime AtRiskUpTo) Cutoffs(decimal days, SlaConfig config, DateTime nowUtc)
        {
            var target = TimeSpan.FromHours((double)days * 24);
            // Breached = more than the target used. A weekend just before the exact point adds nothing, so step back over it.
            var breached = LatestStartWithUsed(nowUtc, target, config.BusinessDays);
            while (config.BusinessDays && breached.TimeOfDay == TimeSpan.Zero && breached.AddDays(-1).DayOfWeek is DayOfWeek.Saturday or DayOfWeek.Sunday)
                breached = breached.AddDays(-1);
            var atRisk = LatestStartWithUsed(nowUtc, TimeSpan.FromTicks((long)Math.Ceiling(target.Ticks * (config.AtRiskPercent / 100.0))), config.BusinessDays);
            return (breached, atRisk);
        }

        /// <summary>The instant <paramref name="amount"/> of Monday–Friday time after <paramref name="start"/>.</summary>
        public static DateTime AddBusinessTime(DateTime start, TimeSpan amount)
        {
            var cursor = start;
            var left = amount;
            var weeks = (int)(left.TotalDays / 5);
            if (weeks > 0 && cursor.DayOfWeek is not (DayOfWeek.Saturday or DayOfWeek.Sunday))
            {
                cursor = cursor.AddDays(7 * weeks);
                left -= TimeSpan.FromDays(5 * weeks);
            }
            while (true)
            {
                if (cursor.DayOfWeek is DayOfWeek.Saturday or DayOfWeek.Sunday) { cursor = cursor.Date.AddDays(1); continue; }
                var toMidnight = cursor.Date.AddDays(1) - cursor;
                if (left <= toMidnight) return cursor + left;
                left -= toMidnight;
                cursor = cursor.Date.AddDays(1);
            }
        }
    }

    /// <summary>
    /// SQL conditions for SLA positions, so the grid can filter on them across every row and the alert checks can count
    /// them. The settings become creation-time cutoffs per customer target (see <see cref="SlaCalculator.Cutoffs"/>),
    /// so no business-day maths runs in SQL. Every value is a parameter.
    /// </summary>
    public sealed class SlaSql
    {
        /// <summary>The grid filter's values: the open positions, plus Finished (Met or Missed).</summary>
        public static readonly string[] Groups = { "Breached", "At risk", "On track", "Finished" };

        private readonly SlaConfig _config;
        private readonly DateTime _nowUtc;
        private readonly Func<SqlDbType, object, int, string> _add;
        private readonly string _created, _customer, _status;

        /// <param name="addParameter">Adds a parameter (type, value, size) and returns its name.</param>
        public SlaSql(SlaConfig config, DateTime nowUtc, Func<SqlDbType, object, int, string> addParameter,
            string createdExpression, string customerExpression, string statusExpression)
        {
            _config = config;
            _nowUtc = nowUtc;
            _add = addParameter;
            _created = createdExpression;
            _customer = customerExpression;
            _status = statusExpression;
        }

        /// <summary>"Breached", "at risk", "AtRisk", "on-track"… → the canonical group name, or null.</summary>
        public static string? NormalizeGroup(string? value)
        {
            var key = new string((value ?? string.Empty).Where(char.IsLetter).ToArray());
            return Groups.FirstOrDefault(g => string.Equals(g.Replace(" ", ""), key, StringComparison.OrdinalIgnoreCase))
                   ?? (key.Equals("Met", StringComparison.OrdinalIgnoreCase) || key.Equals("Missed", StringComparison.OrdinalIgnoreCase) ? "Finished" : null);
        }

        private string StopList() =>
            string.Join(",", _config.StopStatuses.Select(s => _add(SqlDbType.NVarChar, s, 200)));

        /// <summary>Requests in one SLA group (a <see cref="Groups"/> value).</summary>
        public string Condition(string group)
        {
            var stops = StopList();
            if (group == "Finished") return $"({_status} IN ({stops}))";
            string Time(decimal days)
            {
                var (breachedBefore, atRiskUpTo) = SlaCalculator.Cutoffs(days, _config, _nowUtc);
                return group switch
                {
                    "Breached" => $"{_created} < {Date(breachedBefore)}",
                    "At risk" => $"{_created} >= {Date(breachedBefore)} AND {_created} <= {Date(atRiskUpTo)}",
                    _ => $"{_created} > {Date(atRiskUpTo)}"
                };
            }
            return $"({Open(stops)} AND {PerTarget(Time)})";
        }

        /// <summary>Open requests whose SLA ran out after <paramref name="sinceUtc"/> (and by now): the newly breached.</summary>
        public string BreachedSince(DateTime sinceUtc)
        {
            var stops = StopList();
            string Time(decimal days) =>
                $"{_created} >= {Date(SlaCalculator.Cutoffs(days, _config, sinceUtc).BreachedBefore)} AND " +
                $"{_created} < {Date(SlaCalculator.Cutoffs(days, _config, _nowUtc).BreachedBefore)}";
            return $"({Open(stops)} AND {PerTarget(Time)})";
        }

        private string Open(string stops) => $"({_status} IS NULL OR {_status} NOT IN ({stops}))";

        private string Date(DateTime value) => _add(SqlDbType.DateTime2, value, 0);

        /// <summary>One time condition per customer target, plus the default for every other customer.</summary>
        private string PerTarget(Func<decimal, string> timeCondition)
        {
            var parts = new List<string>();
            var names = new List<string>();
            foreach (var t in _config.Customers)
            {
                var name = _add(SqlDbType.NVarChar, t.CustomerName, 200);
                names.Add(name);
                parts.Add($"({_customer} = {name} AND {timeCondition(t.TargetDays)})");
            }
            parts.Add(names.Count == 0
                ? $"({timeCondition(_config.DefaultDays)})"
                : $"(({_customer} IS NULL OR {_customer} NOT IN ({string.Join(",", names)})) AND {timeCondition(_config.DefaultDays)})");
            return "(" + string.Join(" OR ", parts) + ")";
        }
    }

    /// <summary>Reads and saves the SLA settings; cached briefly because every search result page uses them.</summary>
    public sealed class SlaService
    {
        private const string CacheKey = "sla:config";
        private readonly string _connectionString;
        private readonly IMemoryCache _cache;

        public SlaService(IConfiguration configuration, IOptions<PortalAuthOptions> options, IMemoryCache cache)
        {
            var name = options.Value.ConnectionStringName;
            _connectionString = configuration.GetConnectionString(name) ?? configuration[name]
                ?? throw new InvalidOperationException($"{name} is not configured.");
            _cache = cache;
        }

        /// <summary>The settings, or null when the SLA tables haven't been created (the grid then shows no SLA).</summary>
        public async Task<SlaConfig?> GetAsync(CancellationToken ct = default)
        {
            if (_cache.TryGetValue(CacheKey, out SlaConfig? cached)) return cached;
            SlaConfig? config;
            try { config = await LoadAsync(ct); }
            catch (SqlException ex) when (ex.Number == 208) { config = null; } // invalid object name: script not run
            _cache.Set(CacheKey, config, TimeSpan.FromSeconds(30));
            return config;
        }

        private async Task<SlaConfig> LoadAsync(CancellationToken ct)
        {
            await using var conn = new SqlConnection(_connectionString);
            await conn.OpenAsync(ct);
            var config = new SlaConfig();
            await using (var cmd = new SqlCommand("SELECT BusinessDays, AtRiskPercent, StopStatuses, UpdatedUtc, UpdatedBy FROM dbo.SlaSetting WHERE SlaSettingId = 1", conn))
            await using (var r = await cmd.ExecuteReaderAsync(ct))
            {
                if (await r.ReadAsync(ct))
                {
                    config.BusinessDays = r.GetBoolean(0);
                    config.AtRiskPercent = r.GetInt32(1);
                    config.StopStatuses = r.GetString(2).Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries).ToList();
                    config.UpdatedUtc = r.GetDateTime(3);
                    config.UpdatedBy = r.IsDBNull(4) ? null : r.GetString(4);
                }
            }
            await using (var cmd = new SqlCommand("SELECT CustomerName, TargetDays FROM dbo.SlaTarget ORDER BY CustomerName", conn))
            await using (var r = await cmd.ExecuteReaderAsync(ct))
            {
                while (await r.ReadAsync(ct))
                {
                    if (r.IsDBNull(0)) config.DefaultDays = r.GetDecimal(1);
                    else config.Customers.Add(new SlaCustomerTarget { CustomerName = r.GetString(0), TargetDays = r.GetDecimal(1) });
                }
            }
            return config;
        }

        /// <summary>Checks a change before saving it. Returns the problems (empty = valid).</summary>
        public static List<string> Validate(SlaConfig c)
        {
            var problems = new List<string>();
            if (c.DefaultDays <= 0 || c.DefaultDays > 365) problems.Add("The default SLA must be more than 0 and at most 365 days.");
            if (c.AtRiskPercent < 1 || c.AtRiskPercent > 99) problems.Add("\"At risk\" must be between 1% and 99% of the SLA.");
            if (c.StopStatuses.Count == 0) problems.Add("Choose at least one status that finishes a request.");
            if (string.Join(",", c.StopStatuses).Length > 1000) problems.Add("Too many finishing statuses.");
            foreach (var t in c.Customers)
            {
                if (string.IsNullOrWhiteSpace(t.CustomerName)) problems.Add("A customer target has no customer.");
                else if (t.TargetDays <= 0 || t.TargetDays > 365) problems.Add($"The SLA for {t.CustomerName} must be more than 0 and at most 365 days.");
            }
            var dupes = c.Customers.GroupBy(t => t.CustomerName.Trim(), StringComparer.OrdinalIgnoreCase).Where(g => g.Count() > 1).Select(g => g.Key).ToList();
            if (dupes.Count > 0) problems.Add("Each customer can have one target: " + string.Join(", ", dupes) + ".");
            return problems;
        }

        public async Task SaveAsync(SlaConfig c, string userName, CancellationToken ct = default)
        {
            await using var conn = new SqlConnection(_connectionString);
            await conn.OpenAsync(ct);
            await using var tx = (SqlTransaction)await conn.BeginTransactionAsync(ct);
            await using (var cmd = new SqlCommand(@"
UPDATE dbo.SlaSetting SET BusinessDays = @bd, AtRiskPercent = @risk, StopStatuses = @stop, UpdatedUtc = SYSUTCDATETIME(), UpdatedBy = @by WHERE SlaSettingId = 1;
IF @@ROWCOUNT = 0 INSERT INTO dbo.SlaSetting (SlaSettingId, BusinessDays, AtRiskPercent, StopStatuses, UpdatedBy) VALUES (1, @bd, @risk, @stop, @by);
DELETE FROM dbo.SlaTarget;
INSERT INTO dbo.SlaTarget (CustomerName, TargetDays, UpdatedBy) VALUES (NULL, @days, @by);", conn, tx))
            {
                cmd.Parameters.Add(new SqlParameter("@bd", SqlDbType.Bit) { Value = c.BusinessDays });
                cmd.Parameters.Add(new SqlParameter("@risk", SqlDbType.Int) { Value = c.AtRiskPercent });
                cmd.Parameters.Add(new SqlParameter("@stop", SqlDbType.NVarChar, 1000) { Value = string.Join(",", c.StopStatuses.Select(s => s.Trim())) });
                cmd.Parameters.Add(new SqlParameter("@days", SqlDbType.Decimal) { Value = c.DefaultDays, Precision = 6, Scale = 2 });
                cmd.Parameters.Add(new SqlParameter("@by", SqlDbType.NVarChar, 100) { Value = userName });
                await cmd.ExecuteNonQueryAsync(ct);
            }
            foreach (var t in c.Customers)
            {
                await using var cmd = new SqlCommand("INSERT INTO dbo.SlaTarget (CustomerName, TargetDays, UpdatedBy) VALUES (@c, @d, @by)", conn, tx);
                cmd.Parameters.Add(new SqlParameter("@c", SqlDbType.NVarChar, 200) { Value = t.CustomerName.Trim() });
                cmd.Parameters.Add(new SqlParameter("@d", SqlDbType.Decimal) { Value = t.TargetDays, Precision = 6, Scale = 2 });
                cmd.Parameters.Add(new SqlParameter("@by", SqlDbType.NVarChar, 100) { Value = userName });
                await cmd.ExecuteNonQueryAsync(ct);
            }
            await tx.CommitAsync(ct);
            _cache.Remove(CacheKey);
        }
    }
}
