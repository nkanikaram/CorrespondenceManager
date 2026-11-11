using System.Data;
using Microsoft.Data.SqlClient;
using Microsoft.Extensions.Options;
using ProcessRequestEndPoint.Auth;
using ProcessRequestEndPoint.Pdf;

namespace ProcessRequestEndPoint.Records
{
    /// <summary>"Retention" configuration section.</summary>
    public sealed class RetentionOptions
    {
        public const string SectionName = "Retention";

        /// <summary>Days a deleted record stays restorable before it's purged for good.</summary>
        public int RecycleBinDays { get; set; } = 30;

        /// <summary>Runs the scheduled job (recycle-bin purge, and record expiry when set).</summary>
        public bool Enabled { get; set; } = true;

        public int IntervalHours { get; set; } = 6;

        /// <summary>
        /// Records older than this many days move to the recycle bin automatically (then purge after RecycleBinDays).
        /// Null = never. Set it to your records-retention period once the business has agreed one.
        /// </summary>
        public int? RecordRetentionDays { get; set; }

        public int BatchSize { get; set; } = 500;
    }

    public sealed class RecycleBinEntry
    {
        public Guid CorrespondenceId { get; init; }
        public string? DocId { get; init; }
        public string? ClaimNumber { get; init; }
        public string? RequestingDomainName { get; init; }
        public string? DeliveryMethodCode { get; init; }
        public DateTime? RequestCreationTS { get; init; }
        public string? LatestStatus { get; init; }
        public DateTime DeletedUtc { get; init; }
        public string? DeletedByUserName { get; init; }
        public string? DeleteReason { get; init; }
        public DateTime PurgeAfterUtc { get; init; }
        public bool HasPdf { get; init; }
    }

    public sealed class RecycleBinException : Exception
    {
        public RecycleBinException(string message) : base(message) { }
    }

    /// <summary>
    /// dbo.CorrespondenceRecycleBin and its procedures (Build/DBScripts/Portal/001_RecycleBin.sql). Recycling moves a
    /// record, its status history and its PDF reference into the bin; restoring puts them back exactly; purging
    /// deletes the bin entry (the caller deletes the PDF blob).
    /// </summary>
    public sealed class RecycleBinRepository
    {
        private readonly string _connectionString;

        public RecycleBinRepository(IConfiguration configuration, IOptions<PortalAuthOptions> authOptions)
        {
            var name = authOptions.Value.ConnectionStringName;
            _connectionString = configuration.GetConnectionString(name) ?? configuration[name]
                ?? throw new InvalidOperationException($"{name} is not configured.");
        }

        private async Task<SqlConnection> OpenAsync(CancellationToken ct)
        {
            var conn = new SqlConnection(_connectionString);
            await conn.OpenAsync(ct);
            return conn;
        }

        /// <summary>Moves records into the recycle bin. Returns the ids that were moved.</summary>
        public async Task<List<Guid>> RecycleAsync(IReadOnlyCollection<Guid> ids, Guid? byUserId, string? byUserName, string reason, int retentionDays, CancellationToken ct)
        {
            var moved = new List<Guid>();
            if (ids.Count == 0) return moved;
            await using var conn = await OpenAsync(ct);
            await using var cmd = new SqlCommand("dbo.usp_RecycleCorrespondence", conn) { CommandType = CommandType.StoredProcedure, CommandTimeout = 120 };
            cmd.Parameters.Add(new SqlParameter("@IdsJson", SqlDbType.NVarChar, -1) { Value = System.Text.Json.JsonSerializer.Serialize(ids) });
            cmd.Parameters.Add(new SqlParameter("@DeletedByUserId", SqlDbType.UniqueIdentifier) { Value = (object?)byUserId ?? DBNull.Value });
            cmd.Parameters.Add(new SqlParameter("@DeletedByUserName", SqlDbType.NVarChar, 100) { Value = (object?)byUserName ?? DBNull.Value });
            cmd.Parameters.Add(new SqlParameter("@Reason", SqlDbType.NVarChar, 200) { Value = reason });
            cmd.Parameters.Add(new SqlParameter("@RetentionDays", SqlDbType.Int) { Value = retentionDays });
            await using var r = await cmd.ExecuteReaderAsync(ct);
            while (await r.ReadAsync(ct)) moved.Add(r.GetGuid(0));
            return moved;
        }

        public async Task RestoreAsync(Guid id, CancellationToken ct)
        {
            await using var conn = await OpenAsync(ct);
            await using var cmd = new SqlCommand("dbo.usp_RestoreCorrespondence", conn) { CommandType = CommandType.StoredProcedure };
            cmd.Parameters.Add(new SqlParameter("@CorrespondenceId", SqlDbType.UniqueIdentifier) { Value = id });
            try
            {
                await cmd.ExecuteNonQueryAsync(ct);
            }
            catch (SqlException ex) when (ex.Number is 50001 or 50002)
            {
                throw new RecycleBinException(ex.Message);
            }
        }

        public async Task<RecycleBinEntry?> GetAsync(Guid id, CancellationToken ct) =>
            (await ListAsync(null, 1, id, ct)).FirstOrDefault();

        /// <summary>Newest deletions first; <paramref name="search"/> matches doc id, claim number or customer.</summary>
        public async Task<List<RecycleBinEntry>> ListAsync(string? search, int top, Guid? id, CancellationToken ct)
        {
            var sql = @"
SELECT TOP (@top) CorrespondenceId, DocId, ClaimNumber, RequestingDomainName, DeliveryMethodCode, RequestCreationTS, LatestStatus,
       DeletedUtc, DeletedByUserName, DeleteReason, PurgeAfterUtc, CAST(CASE WHEN PdfBlobName IS NULL THEN 0 ELSE 1 END AS bit)
FROM dbo.CorrespondenceRecycleBin
WHERE (@id IS NULL OR CorrespondenceId = @id)
  AND (@q IS NULL OR DocId LIKE @q OR ClaimNumber LIKE @q OR RequestingDomainName LIKE @q OR CONVERT(varchar(36), CorrespondenceId) LIKE @q)
ORDER BY DeletedUtc DESC";
            var list = new List<RecycleBinEntry>();
            await using var conn = await OpenAsync(ct);
            await using var cmd = new SqlCommand(sql, conn);
            cmd.Parameters.Add(new SqlParameter("@top", SqlDbType.Int) { Value = Math.Clamp(top, 1, 1000) });
            cmd.Parameters.Add(new SqlParameter("@id", SqlDbType.UniqueIdentifier) { Value = (object?)id ?? DBNull.Value });
            var q = string.IsNullOrWhiteSpace(search) ? null : "%" + search.Trim().Replace("[", "[[]").Replace("%", "[%]").Replace("_", "[_]") + "%";
            cmd.Parameters.Add(new SqlParameter("@q", SqlDbType.NVarChar, 210) { Value = (object?)q ?? DBNull.Value });
            await using var r = await cmd.ExecuteReaderAsync(ct);
            while (await r.ReadAsync(ct))
            {
                string? S(int i) => r.IsDBNull(i) ? null : r.GetString(i);
                list.Add(new RecycleBinEntry
                {
                    CorrespondenceId = r.GetGuid(0), DocId = S(1), ClaimNumber = S(2), RequestingDomainName = S(3), DeliveryMethodCode = S(4),
                    RequestCreationTS = r.IsDBNull(5) ? null : r.GetDateTime(5), LatestStatus = S(6), DeletedUtc = r.GetDateTime(7),
                    DeletedByUserName = S(8), DeleteReason = S(9), PurgeAfterUtc = r.GetDateTime(10), HasPdf = r.GetBoolean(11)
                });
            }
            return list;
        }

        /// <summary>Permanently removes bin entries (one, or all whose purge date has passed). Returns their PDF blob names.</summary>
        public async Task<List<(Guid Id, string? PdfBlobName)>> PurgeAsync(Guid? id, int max, CancellationToken ct)
        {
            var sql = id.HasValue
                ? "DELETE FROM dbo.CorrespondenceRecycleBin OUTPUT deleted.CorrespondenceId, deleted.PdfBlobName WHERE CorrespondenceId = @id"
                : "DELETE TOP (@max) FROM dbo.CorrespondenceRecycleBin OUTPUT deleted.CorrespondenceId, deleted.PdfBlobName WHERE PurgeAfterUtc <= SYSUTCDATETIME()";
            var purged = new List<(Guid, string?)>();
            await using var conn = await OpenAsync(ct);
            await using var cmd = new SqlCommand(sql, conn);
            cmd.Parameters.Add(new SqlParameter("@id", SqlDbType.UniqueIdentifier) { Value = (object?)id ?? DBNull.Value });
            cmd.Parameters.Add(new SqlParameter("@max", SqlDbType.Int) { Value = max });
            await using var r = await cmd.ExecuteReaderAsync(ct);
            while (await r.ReadAsync(ct)) purged.Add((r.GetGuid(0), r.IsDBNull(1) ? null : r.GetString(1)));
            return purged;
        }

        /// <summary>Records created before the cutoff (for retention expiry), oldest first.</summary>
        public async Task<List<Guid>> ExpiredRecordIdsAsync(DateTime createdBefore, int max, CancellationToken ct)
        {
            var ids = new List<Guid>();
            await using var conn = await OpenAsync(ct);
            await using var cmd = new SqlCommand(
                "SELECT TOP (@max) CorrespondenceId FROM dbo.CorrespondenceRequest WHERE RequestCreationTS < @before ORDER BY RequestCreationTS", conn);
            cmd.Parameters.Add(new SqlParameter("@max", SqlDbType.Int) { Value = max });
            cmd.Parameters.Add(new SqlParameter("@before", SqlDbType.DateTime) { Value = createdBefore });
            await using var r = await cmd.ExecuteReaderAsync(ct);
            while (await r.ReadAsync(ct)) ids.Add(r.GetGuid(0));
            return ids;
        }
    }

    /// <summary>
    /// Scheduled retention job: purges recycle-bin entries past their purge date (and their PDF blobs) and, when
    /// RecordRetentionDays is set, moves records older than that into the recycle bin. Idempotent, so it is safe
    /// on several instances. Archiving PDFs to cooler storage tiers is best done with a Blob lifecycle policy (see README).
    /// </summary>
    public sealed class RetentionWorker : BackgroundService
    {
        private readonly IServiceScopeFactory _scopes;
        private readonly RetentionOptions _options;
        private readonly ILogger<RetentionWorker> _logger;

        public RetentionWorker(IServiceScopeFactory scopes, IOptions<RetentionOptions> options, ILogger<RetentionWorker> logger)
        {
            _scopes = scopes;
            _options = options.Value;
            _logger = logger;
        }

        protected override async Task ExecuteAsync(CancellationToken stoppingToken)
        {
            try { await Task.Delay(TimeSpan.FromMinutes(1), stoppingToken); } catch (OperationCanceledException) { return; }
            while (!stoppingToken.IsCancellationRequested)
            {
                try
                {
                    var (expired, purged) = await RunOnceAsync(stoppingToken);
                    if (expired + purged > 0) _logger.LogInformation("Retention: {Expired} record(s) moved to the recycle bin, {Purged} purged", expired, purged);
                }
                catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested) { break; }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Retention job failed. Has Build/DBScripts/Portal/001_RecycleBin.sql been applied?");
                }
                try { await Task.Delay(TimeSpan.FromHours(Math.Max(1, _options.IntervalHours)), stoppingToken); } catch (OperationCanceledException) { break; }
            }
        }

        public async Task<(int Expired, int Purged)> RunOnceAsync(CancellationToken ct)
        {
            using var scope = _scopes.CreateScope();
            var bin = scope.ServiceProvider.GetRequiredService<RecycleBinRepository>();
            var audit = scope.ServiceProvider.GetRequiredService<AuthRepository>();
            var storage = scope.ServiceProvider.GetService<PdfBlobStorage>();

            var expired = 0;
            if (_options.RecordRetentionDays is > 0)
            {
                var ids = await bin.ExpiredRecordIdsAsync(DateTime.UtcNow.AddDays(-_options.RecordRetentionDays.Value), _options.BatchSize, ct);
                var moved = await bin.RecycleAsync(ids, null, "retention job", "Retention period ended", _options.RecycleBinDays, ct);
                expired = moved.Count;
                if (expired > 0)
                    await audit.WriteAuditAsync(null, "retention job", "RecordsExpired", "Success",
                        $"{expired} record(s) older than {_options.RecordRetentionDays} days moved to the recycle bin", null);
            }

            var purged = await bin.PurgeAsync(null, _options.BatchSize, ct);
            foreach (var (id, blob) in purged)
            {
                if (blob != null && storage != null)
                {
                    try { await storage.DeleteIfExistsAsync(blob, ct); }
                    catch (Exception ex) { _logger.LogWarning(ex, "Couldn't delete PDF blob {Blob} of purged record {Id}", blob, id); }
                }
            }
            if (purged.Count > 0)
                await audit.WriteAuditAsync(null, "retention job", "RecordsPurged", "Success", $"{purged.Count} record(s) permanently removed from the recycle bin", null);
            return (expired, purged.Count);
        }
    }
}
