using System.Data;
using Microsoft.Data.SqlClient;
using Microsoft.Extensions.Options;
using ProcessRequestEndPoint.Auth;

namespace ProcessRequestEndPoint.Records
{
    public sealed class StatusHistoryEntry
    {
        public int Sequence { get; init; }
        public string StatusCode { get; init; } = string.Empty;
        public string Status { get; init; } = string.Empty;
        public string? UpdatedBy { get; init; }
        public DateTime? UpdatedTS { get; init; }
        public string? EventInstanceId { get; init; }
    }

    public sealed class CorrespondenceRecordDetails
    {
        public Guid CorrespondenceId { get; init; }
        public string? DocId { get; init; }
        public string? ClaimNumber { get; init; }
        public string? CustomerId { get; init; }
        public string? RequestingDomainName { get; init; }
        public string? DeliveryMethod { get; init; }
        public string? DeliveryMethodCode { get; init; }
        public string? VersionCode { get; init; }
        public string? ArchiveDocumentTypeCode { get; init; }
        public string? ArchiveDocumentSubTypeCode { get; init; }
        public string? VFResponseId { get; init; }
        public DateTime? RequestCreationTS { get; init; }
        public string? LatestStatus { get; set; }
        public List<StatusHistoryEntry> StatusHistory { get; init; } = new();
        internal byte[]? RequestPayload { get; init; }
    }

    /// <summary>One record's fields and full status history (CorrespondenceRequestStatusHistory, oldest first).</summary>
    public sealed class CorrespondenceDetailsRepository
    {
        private readonly string _connectionString;

        public CorrespondenceDetailsRepository(IConfiguration configuration, IOptions<PortalAuthOptions> authOptions)
        {
            var name = authOptions.Value.ConnectionStringName;
            _connectionString = configuration.GetConnectionString(name) ?? configuration[name]
                ?? throw new InvalidOperationException($"{name} is not configured.");
        }

        public async Task<CorrespondenceRecordDetails?> GetAsync(Guid id, CancellationToken ct)
        {
            const string sql = @"
SELECT cr.CorrespondenceId, cr.DocId, cr.ClaimNumber, cr.CustomerId, cr.RequestingDomainName,
       COALESCE(dm.DeliveryMethodName, cr.DeliveryMethodCode), cr.DeliveryMethodCode, cr.VersionCode,
       cr.ArchiveDocumentTypeCode, cr.ArchiveDocumentSubTypeCode, cr.VFResponseId, cr.RequestCreationTS, cr.RequestPayload
FROM dbo.CorrespondenceRequest cr
LEFT JOIN dbo.CorrespondenceDeliveryMethod dm ON dm.DeliveryMethodCode = cr.DeliveryMethodCode
WHERE cr.CorrespondenceId = @id;

SELECT h.HistorySequenceNum, h.StatusCode, COALESCE(sc.StatusDescription, h.StatusCode), h.StatusUpdatedBy, h.StatusUpdateTS, h.SFMCEventInstanceId
FROM dbo.CorrespondenceRequestStatusHistory h
LEFT JOIN dbo.CorrespondenceStatusCode sc ON sc.StatusCode = h.StatusCode
WHERE h.CorrespondenceId = @id
ORDER BY h.HistorySequenceNum, h.StatusUpdateTS;";
            await using var conn = new SqlConnection(_connectionString);
            await conn.OpenAsync(ct);
            await using var cmd = new SqlCommand(sql, conn);
            cmd.Parameters.Add(new SqlParameter("@id", SqlDbType.UniqueIdentifier) { Value = id });
            await using var r = await cmd.ExecuteReaderAsync(ct);
            if (!await r.ReadAsync(ct)) return null;
            string? S(int i) => r.IsDBNull(i) ? null : r.GetValue(i).ToString();
            var details = new CorrespondenceRecordDetails
            {
                CorrespondenceId = r.GetGuid(0), DocId = S(1), ClaimNumber = S(2), CustomerId = S(3), RequestingDomainName = S(4),
                DeliveryMethod = S(5), DeliveryMethodCode = S(6), VersionCode = S(7), ArchiveDocumentTypeCode = S(8),
                ArchiveDocumentSubTypeCode = S(9), VFResponseId = S(10), RequestCreationTS = r.IsDBNull(11) ? null : r.GetDateTime(11),
                RequestPayload = r.IsDBNull(12) ? null : (byte[])r[12]
            };
            await r.NextResultAsync(ct);
            while (await r.ReadAsync(ct))
            {
                details.StatusHistory.Add(new StatusHistoryEntry
                {
                    Sequence = r.GetInt32(0), StatusCode = r.GetString(1), Status = r.GetString(2), UpdatedBy = S(3),
                    UpdatedTS = r.IsDBNull(4) ? null : r.GetDateTime(4), EventInstanceId = S(5)
                });
            }
            details.LatestStatus = details.StatusHistory.LastOrDefault()?.Status;
            return details;
        }
    }
}
