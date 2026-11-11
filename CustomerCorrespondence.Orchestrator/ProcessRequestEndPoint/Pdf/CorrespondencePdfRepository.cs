using System.Data;
using Microsoft.Data.SqlClient;
using Microsoft.Extensions.Options;
using ProcessRequestEndPoint.Auth;

namespace ProcessRequestEndPoint.Pdf
{
    public sealed class PdfStatus
    {
        public Guid CorrespondenceId { get; init; }
        public string Status { get; init; } = "None";
        public string? BlobName { get; init; }
        public long? ContentLength { get; init; }
        public int Attempts { get; init; }
        public string? LastError { get; init; }
        public DateTime? GeneratedUtc { get; init; }
    }

    /// <summary>dbo.CorrespondencePdfDocument (Build/DBScripts/Pdf) plus the request data a PDF is rendered from.</summary>
    public sealed class CorrespondencePdfRepository
    {
        private readonly string _connectionString;

        public CorrespondencePdfRepository(IConfiguration configuration, IOptions<PortalAuthOptions> authOptions)
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

        private static SqlParameter P(string name, SqlDbType type, object? value, int size = 0)
        {
            var p = new SqlParameter(name, type) { Value = value ?? DBNull.Value };
            if (size != 0) p.Size = size;
            return p;
        }

        /// <summary>Adds Pending rows for recent requests that have none yet. Returns how many were added.</summary>
        public async Task<int> EnqueueNewRequestsAsync(DateTime createdSince, DateTime createdBefore, int max, CancellationToken ct)
        {
            const string sql = @"
INSERT INTO dbo.CorrespondencePdfDocument (CorrespondenceId, Status, NextAttemptUtc)
SELECT TOP (@max) cr.CorrespondenceId, 'Pending', SYSUTCDATETIME()
FROM dbo.CorrespondenceRequest cr
WHERE cr.RequestCreationTS >= @since AND cr.RequestCreationTS < @before
  AND cr.RequestPayload IS NOT NULL
  AND NOT EXISTS (SELECT 1 FROM dbo.CorrespondencePdfDocument p WITH (UPDLOCK, HOLDLOCK) WHERE p.CorrespondenceId = cr.CorrespondenceId)
ORDER BY cr.RequestCreationTS DESC;";
            await using var conn = await OpenAsync(ct);
            await using var cmd = new SqlCommand(sql, conn);
            cmd.Parameters.Add(P("@max", SqlDbType.Int, max));
            cmd.Parameters.Add(P("@since", SqlDbType.DateTime2, createdSince));
            cmd.Parameters.Add(P("@before", SqlDbType.DateTime2, createdBefore));
            try
            {
                return await cmd.ExecuteNonQueryAsync(ct);
            }
            catch (SqlException ex) when (ex.Number is 2627 or 2601)
            {
                return 0; // another instance enqueued the same request first
            }
        }

        /// <summary>
        /// Claims up to <paramref name="max"/> rows that are due (pending, retryable failure, or an expired lease)
        /// and leases them, so concurrent workers skip them (READPAST).
        /// </summary>
        public async Task<List<Guid>> ClaimAsync(int max, int maxAttempts, int leaseSeconds, CancellationToken ct)
        {
            const string sql = @"
UPDATE TOP (@max) p
SET Status = 'Processing', Attempts = p.Attempts + 1, LeaseUntilUtc = DATEADD(SECOND, @lease, SYSUTCDATETIME()), UpdatedUtc = SYSUTCDATETIME()
OUTPUT inserted.CorrespondenceId
FROM dbo.CorrespondencePdfDocument p WITH (ROWLOCK, READPAST, UPDLOCK)
WHERE (p.Status = 'Pending' AND (p.NextAttemptUtc IS NULL OR p.NextAttemptUtc <= SYSUTCDATETIME()))
   OR (p.Status = 'Failed' AND p.Attempts < @maxAttempts AND p.NextAttemptUtc <= SYSUTCDATETIME())
   OR (p.Status = 'Processing' AND p.LeaseUntilUtc < SYSUTCDATETIME());";
            var ids = new List<Guid>();
            await using var conn = await OpenAsync(ct);
            await using var cmd = new SqlCommand(sql, conn);
            cmd.Parameters.Add(P("@max", SqlDbType.Int, max));
            cmd.Parameters.Add(P("@maxAttempts", SqlDbType.Int, maxAttempts));
            cmd.Parameters.Add(P("@lease", SqlDbType.Int, leaseSeconds));
            await using var r = await cmd.ExecuteReaderAsync(ct);
            while (await r.ReadAsync(ct)) ids.Add(r.GetGuid(0));
            return ids;
        }

        /// <summary>Marks one request as being generated now (on-demand path); creates the row if needed.</summary>
        public async Task BeginOnDemandAsync(Guid id, int leaseSeconds, CancellationToken ct)
        {
            const string sql = @"
MERGE dbo.CorrespondencePdfDocument WITH (HOLDLOCK) AS t
USING (SELECT @id AS CorrespondenceId) AS s ON t.CorrespondenceId = s.CorrespondenceId
WHEN MATCHED THEN UPDATE SET Status = 'Processing', Attempts = t.Attempts + 1,
    LeaseUntilUtc = DATEADD(SECOND, @lease, SYSUTCDATETIME()), UpdatedUtc = SYSUTCDATETIME()
WHEN NOT MATCHED THEN INSERT (CorrespondenceId, Status, Attempts, LeaseUntilUtc)
    VALUES (@id, 'Processing', 1, DATEADD(SECOND, @lease, SYSUTCDATETIME()));";
            await using var conn = await OpenAsync(ct);
            await using var cmd = new SqlCommand(sql, conn);
            cmd.Parameters.Add(P("@id", SqlDbType.UniqueIdentifier, id));
            cmd.Parameters.Add(P("@lease", SqlDbType.Int, leaseSeconds));
            await cmd.ExecuteNonQueryAsync(ct);
        }

        public async Task CompleteAsync(Guid id, string blobName, long length, byte[] sha256, int? templateId, int? templateVersion, CancellationToken ct)
        {
            await CompleteAsync(id, blobName, length, sha256, ct);
            // Which letter template produced it (columns from Build/DBScripts/Portal/002; skipped when absent).
            try
            {
                await using var conn = await OpenAsync(ct);
                await using var cmd = new SqlCommand("UPDATE dbo.CorrespondencePdfDocument SET TemplateId = @t, TemplateVersion = @v WHERE CorrespondenceId = @id", conn);
                cmd.Parameters.Add(P("@id", SqlDbType.UniqueIdentifier, id));
                cmd.Parameters.Add(P("@t", SqlDbType.Int, templateId));
                cmd.Parameters.Add(P("@v", SqlDbType.Int, templateVersion));
                await cmd.ExecuteNonQueryAsync(ct);
            }
            catch (SqlException ex) when (ex.Number == 207) { }
        }

        public async Task CompleteAsync(Guid id, string blobName, long length, byte[] sha256, CancellationToken ct)
        {
            const string sql = @"
UPDATE dbo.CorrespondencePdfDocument
SET Status = 'Generated', BlobName = @blob, ContentLength = @len, ContentSha256 = @sha, LastError = NULL,
    LeaseUntilUtc = NULL, NextAttemptUtc = NULL, GeneratedUtc = SYSUTCDATETIME(), UpdatedUtc = SYSUTCDATETIME()
WHERE CorrespondenceId = @id";
            await using var conn = await OpenAsync(ct);
            await using var cmd = new SqlCommand(sql, conn);
            cmd.Parameters.Add(P("@id", SqlDbType.UniqueIdentifier, id));
            cmd.Parameters.Add(P("@blob", SqlDbType.NVarChar, blobName, 400));
            cmd.Parameters.Add(P("@len", SqlDbType.BigInt, length));
            cmd.Parameters.Add(P("@sha", SqlDbType.VarBinary, sha256, 32));
            await cmd.ExecuteNonQueryAsync(ct);
        }

        /// <summary>Records a failure; the next attempt waits 2^attempts minutes (capped at an hour).</summary>
        public async Task FailAsync(Guid id, string error, CancellationToken ct)
        {
            const string sql = @"
UPDATE dbo.CorrespondencePdfDocument
SET Status = 'Failed', LastError = @error, LeaseUntilUtc = NULL, UpdatedUtc = SYSUTCDATETIME(),
    NextAttemptUtc = DATEADD(MINUTE, CASE WHEN Attempts >= 6 THEN 60 ELSE POWER(2, Attempts) END, SYSUTCDATETIME())
WHERE CorrespondenceId = @id";
            await using var conn = await OpenAsync(ct);
            await using var cmd = new SqlCommand(sql, conn);
            cmd.Parameters.Add(P("@id", SqlDbType.UniqueIdentifier, id));
            cmd.Parameters.Add(P("@error", SqlDbType.NVarChar, error.Length > 1000 ? error[..1000] : error, 1000));
            await cmd.ExecuteNonQueryAsync(ct);
        }

        /// <summary>The request fields and stored payload a PDF is rendered from, or null if it doesn't exist.</summary>
        public async Task<CorrespondencePdfRecord?> GetRecordAsync(Guid id, CancellationToken ct)
        {
            const string sql = @"
SELECT cr.CorrespondenceId, cr.DocId, cr.ClaimNumber, cr.CustomerId, cr.RequestingDomainName,
       COALESCE(dm.DeliveryMethodName, cr.DeliveryMethodCode) AS DeliveryMethod, cr.RequestCreationTS,
       COALESCE(sc.StatusDescription, ls.LatestStatus) AS LatestStatus, cr.RequestPayload
FROM dbo.CorrespondenceRequest cr
OUTER APPLY (SELECT TOP (1) h.StatusCode AS LatestStatus FROM dbo.CorrespondenceRequestStatusHistory h
             WHERE h.CorrespondenceId = cr.CorrespondenceId ORDER BY h.HistorySequenceNum DESC, h.StatusUpdateTS DESC) ls
LEFT JOIN dbo.CorrespondenceDeliveryMethod dm ON dm.DeliveryMethodCode = cr.DeliveryMethodCode
LEFT JOIN dbo.CorrespondenceStatusCode sc ON sc.StatusCode = ls.LatestStatus
WHERE cr.CorrespondenceId = @id";
            await using var conn = await OpenAsync(ct);
            await using var cmd = new SqlCommand(sql, conn);
            cmd.Parameters.Add(P("@id", SqlDbType.UniqueIdentifier, id));
            await using var r = await cmd.ExecuteReaderAsync(ct);
            if (!await r.ReadAsync(ct)) return null;
            string? S(int i) => r.IsDBNull(i) ? null : r.GetValue(i).ToString();
            return new CorrespondencePdfRecord
            {
                CorrespondenceId = r.GetGuid(0),
                DocId = S(1),
                ClaimNumber = S(2),
                CustomerId = S(3),
                RequestingDomainName = S(4),
                DeliveryMethod = S(5),
                RequestCreationTS = r.IsDBNull(6) ? null : r.GetDateTime(6),
                LatestStatus = S(7),
                RequestPayload = r.IsDBNull(8) ? null : (byte[])r[8]
            };
        }

        /// <summary>PDF states for a set of requests; requests without a row are reported as "None".</summary>
        public async Task<List<PdfStatus>> GetStatusesAsync(IReadOnlyCollection<Guid> ids, CancellationToken ct)
        {
            var result = new List<PdfStatus>();
            if (ids.Count == 0) return result;
            var names = ids.Select((_, i) => "@i" + i).ToList();
            var sql = "SELECT CorrespondenceId, Status, BlobName, ContentLength, Attempts, LastError, GeneratedUtc " +
                      $"FROM dbo.CorrespondencePdfDocument WHERE CorrespondenceId IN ({string.Join(",", names)})";
            await using var conn = await OpenAsync(ct);
            await using var cmd = new SqlCommand(sql, conn);
            var k = 0;
            foreach (var id in ids) cmd.Parameters.Add(P("@i" + k++, SqlDbType.UniqueIdentifier, id));
            var found = new Dictionary<Guid, PdfStatus>();
            await using (var r = await cmd.ExecuteReaderAsync(ct))
            {
                while (await r.ReadAsync(ct))
                {
                    found[r.GetGuid(0)] = new PdfStatus
                    {
                        CorrespondenceId = r.GetGuid(0),
                        Status = r.GetString(1),
                        BlobName = r.IsDBNull(2) ? null : r.GetString(2),
                        ContentLength = r.IsDBNull(3) ? null : r.GetInt64(3),
                        Attempts = r.GetInt32(4),
                        LastError = r.IsDBNull(5) ? null : r.GetString(5),
                        GeneratedUtc = r.IsDBNull(6) ? null : r.GetDateTime(6)
                    };
                }
            }
            foreach (var id in ids) result.Add(found.TryGetValue(id, out var s) ? s : new PdfStatus { CorrespondenceId = id });
            return result;
        }
    }
}
