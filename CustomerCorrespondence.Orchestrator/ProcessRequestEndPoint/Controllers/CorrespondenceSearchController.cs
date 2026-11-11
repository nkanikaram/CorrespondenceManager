using System;
using System.Collections.Generic;
using System.Data;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Configuration;
using Microsoft.Data.SqlClient;
using System.Security.Cryptography;
using System.Text.Json;
using CustomerCorrespondence.Application.Interfaces;
using ProcessRequestEndPoint.Auth;

namespace ProcessRequestEndPoint.Controllers
{
    [ApiController]
    [Route("api/correspondence")]
    [RequirePermission(Permissions.CorrespondenceRead)]
    public class CorrespondenceSearchController : ControllerBase
    {
        private readonly string _connectionString;
        private readonly byte[]? _payloadKey; // 32-byte AES key if available
        private readonly IEncryptionService _encryptionService;
        private readonly PortalAccessService _access;

        private readonly ProcessRequestEndPoint.Sla.SlaService? _sla;

        public CorrespondenceSearchController(IConfiguration configuration, IEncryptionService encryptionService, PortalAccessService access,
            ProcessRequestEndPoint.Sla.SlaService? sla = null)
        {
            _sla = sla;
            _encryptionService = encryptionService ?? throw new ArgumentNullException(nameof(encryptionService));
            _access = access ?? throw new ArgumentNullException(nameof(access));
            _connectionString = configuration.GetConnectionString("AZURE_SQL_CONNECTION")
                     ?? configuration["AZURE_SQL_CONNECTION"]
                     ?? throw new InvalidOperationException("AZURE_SQL_CONNECTION is not configured.");

            // Load payload encryption key (optional). Prefer retrieving from Key Vault / secret store in prod.
            var keyString = configuration["PAYLOAD_ENCRYPTION_KEY"];
            if (!string.IsNullOrWhiteSpace(keyString))
            {
                if (TryParseBase64(keyString.Trim(), out var kb) && kb.Length == 32)
                {
                    _payloadKey = kb;
                }
                else if (TryParseHex(keyString.Trim(), out kb) && kb.Length == 32)
                {
                    _payloadKey = kb;
                }
                else
                {
                    // do not crash app on bad config; log instead in production
                    throw new InvalidOperationException("PAYLOAD_ENCRYPTION_KEY must be a 32-byte key encoded as base64 or hex.");
                }
            }
        }

        // The authoritative list of status values that actually appear in the data — used by the
        // AI assistant so it can validate a status name (or suggest the closest match) instead of
        // guessing from training knowledge, and by search_grid callers who want to present a picker.
        // Resolved the same way the search filter resolves LatestStatus (COALESCE through the
        // lookup table), so a value returned here is guaranteed to actually match when filtered on.
        [HttpGet("statuses")]
        public async Task<IActionResult> GetStatusValues()
        {
            const string sql = @"
SELECT DISTINCT COALESCE(sc.StatusDescription, h.StatusCode) AS Status
FROM dbo.CorrespondenceRequestStatusHistory h
LEFT JOIN dbo.CorrespondenceStatusCode sc ON sc.StatusCode = h.StatusCode
ORDER BY Status";

            var statuses = new List<string>();
            await using var conn = new SqlConnection(_connectionString);
            await conn.OpenAsync();
            await using var cmd = new SqlCommand(sql, conn);
            await using var rdr = await cmd.ExecuteReaderAsync();
            while (await rdr.ReadAsync())
            {
                if (!rdr.IsDBNull(0)) statuses.Add(rdr.GetString(0));
            }

            return Ok(statuses);
        }

        /// <summary>Distinct customers or delivery methods within the caller's data scope (grid checkbox filters).</summary>
        [HttpGet("values/{field}")]
        public async Task<IActionResult> GetFieldValues(string field)
        {
            var user = await _access.CurrentAsync();
            if (user == null) return Unauthorized();
            var column = field switch
            {
                "customer" => "cr.RequestingDomainName",
                "delivery" => CorrespondenceQueryBuilder.DeliveryExpression,
                _ => null
            };
            if (column == null) return NotFound();

            var parameters = new List<SqlParameter>();
            var where = new StringBuilder($"WHERE {column} IS NOT NULL");
            CorrespondenceQueryBuilder.AppendScope(where, parameters, user);
            var sql = $@"
SELECT DISTINCT {column} AS Value
FROM dbo.CorrespondenceRequest cr
LEFT JOIN dbo.CorrespondenceDeliveryMethod dm ON dm.DeliveryMethodCode = cr.DeliveryMethodCode
{where}
ORDER BY Value";

            var values = new List<string>();
            await using var conn = new SqlConnection(_connectionString);
            await conn.OpenAsync();
            await using var cmd = new SqlCommand(sql, conn);
            cmd.Parameters.AddRange(parameters.ToArray());
            await using var rdr = await cmd.ExecuteReaderAsync();
            while (await rdr.ReadAsync())
            {
                if (!rdr.IsDBNull(0)) values.Add(rdr.GetString(0));
            }
            return Ok(values);
        }

        [HttpPost("search")]
        public async Task<IActionResult> Search([FromBody] CorrespondenceSearchRequest request)
        {
            request ??= new CorrespondenceSearchRequest();
            var user = await _access.CurrentAsync();
            if (user == null) return Unauthorized();

            var parameters = new List<SqlParameter>();
            var where = new StringBuilder("WHERE 1=1");

            // ABAC data scope: only rows for the user's customers / delivery methods.
            CorrespondenceQueryBuilder.AppendScope(where, parameters, user);

            // SLA settings and one "now" for both the SLA filter and each row's SLA, so they agree.
            var sla = _sla == null ? null : await _sla.GetAsync();
            var nowUtc = DateTime.UtcNow;

            // Grid column filters (Kendo-style composite descriptor), applied across all matching rows.
            try
            {
                CorrespondenceQueryBuilder.AppendFilter(where, parameters, request.Filter, sla, nowUtc);
                // The search box: one term (FreeText) or several (FreeTerms, any of them).
                CorrespondenceQueryBuilder.AppendFreeText(where, parameters,
                    request.FreeTerms is { Count: > 0 } ? request.FreeTerms : new[] { request.FreeText });
            }
            catch (FilterValidationException ex)
            {
                return BadRequest(new { message = ex.Message });
            }

            // Payloads hold personal data: RBAC permission plus the PII-clearance policy.
            string? payloadsHiddenReason = null;
            if (request.IncludePayload)
            {
                var payloadDecision = await _access.CheckAsync(Permissions.PayloadRead);
                if (!payloadDecision.Allowed)
                {
                    request.IncludePayload = false;
                    payloadsHiddenReason = payloadDecision.Message;
                }
            }

            if (!string.IsNullOrWhiteSpace(request.CorrespondenceId))
            {
                // validate GUID first
                if (Guid.TryParse(request.CorrespondenceId, out var gid))
                {
                    parameters.Add(new SqlParameter("@cid", System.Data.SqlDbType.UniqueIdentifier) { Value = gid });
                    where.Append(" AND cr.CorrespondenceId = @cid");
                }
                else
                {
                    // optional: return BadRequest("Invalid CorrespondenceId") to inform client
                }
            }
            if (!string.IsNullOrWhiteSpace(request.ClaimNumber))
            {
                parameters.Add(new SqlParameter("@cnum", SqlDbType.VarChar, 50) { Value = request.ClaimNumber });
                where.Append(" AND cr.ClaimNumber = @cnum");
            }
            if (!string.IsNullOrWhiteSpace(request.PolicyNumber))
            {
                var policyHash = _encryptionService.GenerateSearchHash(request.PolicyNumber);
                parameters.Add(new SqlParameter("@pnumHash", SqlDbType.VarBinary, 32) { Value = policyHash });
                where.Append(" AND cr.PolicyNumberHash = @pnumHash");
            }
            var statusList = (request.LatestStatuses ?? new List<string>())
                .Where(s => !string.IsNullOrWhiteSpace(s))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToList();
            if (statusList.Count > 0)
            {
                var statusParamNames = new List<string>();
                for (int i = 0; i < statusList.Count; i++)
                {
                    var pname = $"@ls{i}";
                    parameters.Add(new SqlParameter(pname, SqlDbType.VarChar, 50) { Value = statusList[i] });
                    statusParamNames.Add(pname);
                }
                // Filter on the RESOLVED status (description when the lookup table has one, else the
                // raw code) — matches what clients are actually shown/given, e.g. "AwaitingImaging".
                // Filtering ls.LatestStatus (the raw StatusCode) directly would silently match nothing
                // for any status whose description differs from its code.
                where.Append(" AND COALESCE(sc.StatusDescription, ls.LatestStatus) IN (" + string.Join(",", statusParamNames) + ")");
            }
            else if (!string.IsNullOrWhiteSpace(request.LatestStatus))
            {
                parameters.Add(new SqlParameter("@ls", SqlDbType.VarChar, 50) { Value = request.LatestStatus });
                where.Append(" AND COALESCE(sc.StatusDescription, ls.LatestStatus) = @ls");
            }
            if (!string.IsNullOrWhiteSpace(request.DeliveryMethodCode))
            {
                parameters.Add(new SqlParameter("@dm", SqlDbType.VarChar, 50) { Value = request.DeliveryMethodCode });
                where.Append(" AND cr.DeliveryMethodCode = @dm");
            }
            if (!string.IsNullOrWhiteSpace(request.CustomerName))
            {
                parameters.Add(new SqlParameter("@cn", SqlDbType.VarChar, 100) { Value = request.CustomerName });
                where.Append(" AND cr.RequestingDomainName = @cn");
            }
            if (request.FromDate.HasValue)
            {
                parameters.Add(new SqlParameter("@from", SqlDbType.DateTime) { Value = request.FromDate.Value });
                where.Append(" AND cr.RequestCreationTS >= @from");
            }
            if (request.ToDate.HasValue)
            {
                // ToDate is a calendar date (time defaults to midnight) — use an exclusive upper bound
                // of the following day so records occurring anywhere on ToDate itself are included.
                parameters.Add(new SqlParameter("@to", SqlDbType.DateTime) { Value = request.ToDate.Value.Date.AddDays(1) });
                where.Append(" AND cr.RequestCreationTS < @to");
            }

            var sortBy = CorrespondenceQueryBuilder.SortExpression(request.SortBy);
            var sortDir = string.Equals(request.SortDirection, "asc", StringComparison.OrdinalIgnoreCase) ? "ASC" : "DESC";
            var page = Math.Max(1, request.Page);
            var pageSize = Math.Clamp(request.PageSize <= 0 ? 50 : request.PageSize, 1, 2000);
            var offset = (page - 1) * pageSize;

            // Use OUTER APPLY to compute LatestStatus per row and make it available for filtering and counting
            var latestStatusApply = @"
OUTER APPLY (
    SELECT TOP (1) h.StatusCode AS LatestStatus, h.StatusUpdateTS AS LatestStatusTS
    FROM dbo.CorrespondenceRequestStatusHistory h
    WHERE h.CorrespondenceId = cr.CorrespondenceId
    ORDER BY h.HistorySequenceNum DESC, h.StatusUpdateTS DESC
) ls";

            // Joined here too (not just sqlPage below) so the status filter — which matches against
            // the resolved description via this same join — works identically for the count query.
            var statusJoin = "LEFT JOIN dbo.CorrespondenceStatusCode sc ON sc.StatusCode = ls.LatestStatus";
            // The delivery-method join is needed here as well: the data scope and the grid's delivery filter use its name.
            var deliveryJoin = "LEFT JOIN dbo.CorrespondenceDeliveryMethod dm ON dm.DeliveryMethodCode = cr.DeliveryMethodCode";
            var sqlCount = $"SELECT COUNT(1) FROM dbo.CorrespondenceRequest cr {latestStatusApply} {deliveryJoin} {statusJoin} {where}";
            // LEFT JOIN the DeliveryMethod/StatusCode lookup tables so numeric codes ("01") resolve to
            // their human-readable name/description ("Mail", "New"). COALESCE falls back to the raw
            // code/value when there's no lookup match — e.g. older rows seeded with descriptive strings
            // directly in DeliveryMethodCode/StatusCode instead of numeric codes.
            // Only select the potentially large RequestPayload column when the caller explicitly
            // asked for payloads (IncludePayload=true). This avoids unnecessary IO for standard searches.
            var payloadSelect = request.IncludePayload ? "cr.RequestPayload AS RequestPayloadBytes" : "CAST(NULL AS varbinary(max)) AS RequestPayloadBytes";

            var sqlPage = $@"
SELECT
    cr.CorrespondenceId,
    cr.DocId,
    cr.RequestingDomainName,
    COALESCE(dm.DeliveryMethodName, cr.DeliveryMethodCode) AS DeliveryMethodCode,
    cr.RequestCreationTS,
    cr.ClaimNumber,
    cr.CustomerId,
    cr.VFResponseId,
    COALESCE(sc.StatusDescription, ls.LatestStatus) AS LatestStatus,
    ls.LatestStatusTS,
    {payloadSelect}
FROM dbo.CorrespondenceRequest cr
{latestStatusApply}
{deliveryJoin}
{statusJoin}
{where}
ORDER BY {sortBy} {sortDir}
OFFSET @offset ROWS FETCH NEXT @pageSize ROWS ONLY;";

            parameters.Add(new SqlParameter("@offset", SqlDbType.Int) { Value = offset });
            parameters.Add(new SqlParameter("@pageSize", SqlDbType.Int) { Value = pageSize });

            var results = new List<CorrespondenceSearchResultItem>();
            int total = 0;

            await using (var conn = new SqlConnection(_connectionString))
            {
                await conn.OpenAsync();

                await using (var cmdCount = new SqlCommand(sqlCount, conn))
                {
                    var countParams = CloneParameters(parameters.Where(p => p.ParameterName != "@offset" && p.ParameterName != "@pageSize"));
                    cmdCount.Parameters.AddRange(countParams);
                    var c = await cmdCount.ExecuteScalarAsync();
                    total = Convert.ToInt32(c ?? 0);
                }

                // "Export all matching" pages through every row; the export permission and the
                // export-row-limit policy apply to the whole result set.
                if (string.Equals(request.Purpose, "export", StringComparison.OrdinalIgnoreCase))
                {
                    var exportDecision = await _access.CheckAsync(Permissions.CorrespondenceExport,
                        new Dictionary<string, string?> { ["rowCount"] = total.ToString(System.Globalization.CultureInfo.InvariantCulture) });
                    if (!exportDecision.Allowed)
                    {
                        await _access.AuditDeniedAsync(Permissions.CorrespondenceExport, exportDecision, $"{total} rows");
                        return StatusCode(StatusCodes.Status403Forbidden, new { message = exportDecision.Message, policy = exportDecision.PolicyName });
                    }
                }

                await using (var cmd = new SqlCommand(sqlPage, conn))
                {
                    var pageParams = CloneParameters(parameters);
                    cmd.Parameters.AddRange(pageParams);

                    await using (var rdr = await cmd.ExecuteReaderAsync())
                    {
                        while (await rdr.ReadAsync())
                        {
                            var item = new CorrespondenceSearchResultItem
                            {
                                CorrespondenceId = rdr.GetGuid(rdr.GetOrdinal("CorrespondenceId")),
                                DocId = rdr.IsDBNull(rdr.GetOrdinal("DocId")) ? null : rdr.GetString(rdr.GetOrdinal("DocId")),
                                RequestingDomainName = rdr.IsDBNull(rdr.GetOrdinal("RequestingDomainName")) ? null : rdr.GetString(rdr.GetOrdinal("RequestingDomainName")),
                                DeliveryMethodCode = rdr.IsDBNull(rdr.GetOrdinal("DeliveryMethodCode")) ? null : rdr.GetString(rdr.GetOrdinal("DeliveryMethodCode")),
                                RequestCreationTS = rdr.IsDBNull(rdr.GetOrdinal("RequestCreationTS")) ? null : rdr.GetDateTime(rdr.GetOrdinal("RequestCreationTS")),
                                ClaimNumber = rdr.IsDBNull(rdr.GetOrdinal("ClaimNumber")) ? null : rdr.GetString(rdr.GetOrdinal("ClaimNumber")),
                                CustomerId = rdr.IsDBNull(rdr.GetOrdinal("CustomerId")) ? null : rdr.GetString(rdr.GetOrdinal("CustomerId")),
                                VFResponseId = rdr.IsDBNull(rdr.GetOrdinal("VFResponseId")) ? null : rdr.GetString(rdr.GetOrdinal("VFResponseId")),
                                LatestStatus = rdr.IsDBNull(rdr.GetOrdinal("LatestStatus")) ? null : rdr.GetString(rdr.GetOrdinal("LatestStatus")),
                                LatestStatusTS = rdr.IsDBNull(rdr.GetOrdinal("LatestStatusTS")) ? null : rdr.GetDateTime(rdr.GetOrdinal("LatestStatusTS")),
                            };
                            // Time lapse and SLA position (timestamps are UTC, as the alert checks assume).
                            if (sla != null && item.RequestCreationTS.HasValue)
                                item.Sla = ProcessRequestEndPoint.Sla.SlaCalculator.Evaluate(item.RequestCreationTS.Value, item.LatestStatus,
                                    item.LatestStatusTS, item.RequestingDomainName, sla, nowUtc);

                            // Only attempt to read/decrypt the payload if the request asked for it.
                            if (request.IncludePayload)
                            {
                                var payloadIdx = rdr.GetOrdinal("RequestPayloadBytes");
                                if (!rdr.IsDBNull(payloadIdx))
                                {
                                    byte[] bytes = (byte[])rdr.GetValue(payloadIdx);
                                    string? payloadText = null;

                                    // If an AES-GCM key is configured, try to decrypt using the expected layout
                                    // nonce(12) | tag(16) | ciphertext
                                    if (_payloadKey != null && TryDecryptPayload(bytes, _payloadKey, out var decrypted))
                                    {
                                        payloadText = decrypted;
                                    }
                                    else
                                    {
                                        // Try interpret as UTF-8 JSON / text
                                        try
                                        {
                                            payloadText = Encoding.UTF8.GetString(bytes);
                                        }
                                        catch
                                        {
                                            // Fallback to base64
                                            payloadText = Convert.ToBase64String(bytes);
                                        }
                                    }

                                    item.RequestPayload = payloadText;
                                }
                            }

                            results.Add(item);
                        }
                    }
                }
            }

            // AddCorrespondenceDBHandler stores payloads PGP-encrypted (IEncryptionService). Decrypt them
            // with the same service so the portal gets the original JSON.
            await DecryptPgpPayloadsAsync(results);

            var response = new CorrespondenceSearchResponse
            {
                Page = page,
                PageSize = pageSize,
                Total = total,
                Items = results,
                PayloadsHidden = payloadsHiddenReason != null,
                PayloadsHiddenReason = payloadsHiddenReason
            };

            return Ok(response);
        }

        private const string PgpMessageHeader = "-----BEGIN PGP MESSAGE-----";
        private const int MaxParallelPgpDecryptions = 8;

        /// <summary>
        /// Replaces ASCII-armored PGP payloads with their decrypted JSON. A payload that can't be
        /// decrypted (e.g. encrypted with another environment's key) is returned unchanged and
        /// flagged, so the UI can say so instead of showing ciphertext as if it were the payload.
        /// </summary>
        private async Task DecryptPgpPayloadsAsync(List<CorrespondenceSearchResultItem> items)
        {
            var encrypted = items
                .Where(i => i.RequestPayload != null && i.RequestPayload.TrimStart().StartsWith(PgpMessageHeader, StringComparison.Ordinal))
                .ToList();
            if (encrypted.Count == 0) return;

            using var gate = new SemaphoreSlim(MaxParallelPgpDecryptions);
            await Task.WhenAll(encrypted.Select(async item =>
            {
                await gate.WaitAsync();
                try
                {
                    var plain = await _encryptionService.DecryptAsync(item.RequestPayload!.Trim());
                    if (!string.IsNullOrEmpty(plain)) item.RequestPayload = plain;
                    else item.PayloadEncrypted = true;
                }
                catch (Exception)
                {
                    item.PayloadEncrypted = true;
                }
                finally
                {
                    gate.Release();
                }
            }));
        }

        //// Decrypt AES-GCM payload layout: nonce(12) | tag(16) | ciphertext
        private static bool TryDecryptPayload(byte[] stored, byte[] key, out string plaintextJson)
        {
            plaintextJson = string.Empty;
            // Expect nonce(12) + tag(16) + ciphertext
            if (stored == null || stored.Length < 12 + 16) return false;
            try
            {
                var nonce = new byte[12];
                var tag = new byte[16];
                var ciphertext = new byte[stored.Length - nonce.Length - tag.Length];
                Buffer.BlockCopy(stored, 0, nonce, 0, nonce.Length);
                Buffer.BlockCopy(stored, nonce.Length, tag, 0, tag.Length);
                Buffer.BlockCopy(stored, nonce.Length + tag.Length, ciphertext, 0, ciphertext.Length);

                var plaintext = new byte[ciphertext.Length];
                using var aes = new AesGcm(key);
                aes.Decrypt(nonce, ciphertext, tag, plaintext, null);
                plaintextJson = Encoding.UTF8.GetString(plaintext);
                return true;
            }
            catch
            {
                return false;
            }
        }

        private static SqlParameter[] CloneParameters(IEnumerable<SqlParameter> parameters)
        {
            return parameters.Select(p =>
            {
                var clone = new SqlParameter(p.ParameterName, p.SqlDbType);
                if (p.Size != 0) clone.Size = p.Size;
                clone.Direction = p.Direction;
                clone.Value = p.Value ?? DBNull.Value;
                clone.Precision = p.Precision;
                clone.Scale = p.Scale;
                clone.IsNullable = p.IsNullable;
                return clone;
            }).ToArray();
        }

        private static bool TryParseBase64(string s, out byte[] bytes)
        {
            bytes = Array.Empty<byte>();
            try
            {
                bytes = Convert.FromBase64String(s);
                return true;
            }
            catch { return false; }
        }
        private static bool TryParseHex(string s, out byte[] bytes)
        {
            bytes = Array.Empty<byte>();
            try
            {
                var hex = s.StartsWith("0x", StringComparison.OrdinalIgnoreCase) ? s.Substring(2) : s;
                hex = hex.Replace(" ", "").Replace("-", "");
                if (hex.Length % 2 != 0) return false;
                bytes = new byte[hex.Length / 2];
                for (int i = 0; i < bytes.Length; i++) bytes[i] = Convert.ToByte(hex.Substring(i * 2, 2), 16);
                return true;
            }
            catch { return false; }
        }
    }

    public class CorrespondenceSearchRequest
    {
        public string? FreeText { get; set; }
        /// <summary>
        /// Several search-box terms, matched as "any of them" (e.g. a pasted list of DocIds); at most 100.
        /// Takes precedence over <see cref="FreeText"/>.
        /// </summary>
        public List<string>? FreeTerms { get; set; }
        public string? CorrespondenceId { get; set; }
        public string? ClaimNumber { get; set; }
        public string? PolicyNumber { get; set; }
        public string? DeliveryMethodCode { get; set; }
        public string? CustomerName { get; set; }
        public string? LatestStatus { get; set; }
        /// <summary>
        /// Optional multi-value alternative to <see cref="LatestStatus"/> — matches any status in the
        /// list (SQL IN). When both are supplied, this list takes precedence.
        /// </summary>
        public List<string>? LatestStatuses { get; set; }
        public DateTime? FromDate { get; set; }
        public DateTime? ToDate { get; set; }
        public int Page { get; set; } = 1;
        public int PageSize { get; set; } = 50;
        public string? SortBy { get; set; }
        public string? SortDirection { get; set; }
        /// <summary>
        /// When true the API will include the decrypted/decoded request payload in each result's
        /// `RequestPayload` property. Default is false to avoid returning large or sensitive data
        /// unless explicitly requested by a client.
        /// </summary>
        public bool IncludePayload { get; set; } = false;

        /// <summary>
        /// Grid column filters as a Kendo-style CompositeFilterDescriptor:
        /// { "logic": "and", "filters": [ { "field": "customer", "operator": "contains", "value": "life" }, ... ] }.
        /// Fields: id, docId, claimNumber, customer, delivery, created (yyyy-MM-dd, compared by day), status,
        /// sla (eq / neq: Breached, At risk, On track, Finished).
        /// </summary>
        public JsonElement? Filter { get; set; }

        /// <summary>"export" when the caller is fetching every row for an Excel export (checked against export policies).</summary>
        public string? Purpose { get; set; }
    }

    public class CorrespondenceSearchResponse
    {
        public int Page { get; set; }
        public int PageSize { get; set; }
        public int Total { get; set; }
        public List<CorrespondenceSearchResultItem> Items { get; set; } = new();

        /// <summary>True when payloads were requested but the caller may not see them (see the reason).</summary>
        public bool PayloadsHidden { get; set; }
        public string? PayloadsHiddenReason { get; set; }
    }

    public class CorrespondenceSearchResultItem
    {
        public Guid CorrespondenceId { get; set; }
        public string? DocId { get; set; }
        public string? RequestingDomainName { get; set; }
        public string? DeliveryMethodCode { get; set; }
        public DateTime? RequestCreationTS { get; set; }
        public string? ClaimNumber { get; set; }
        public string? CustomerId { get; set; }
        public string? VFResponseId { get; set; }
        public string? LatestStatus { get; set; }

        /// <summary>When the latest status was recorded.</summary>
        public DateTime? LatestStatusTS { get; set; }

        /// <summary>Time lapse and SLA position; null when the SLA tables don't exist yet.</summary>
        public ProcessRequestEndPoint.Sla.SlaResult? Sla { get; set; }

        /// <summary>
        /// Decrypted or decoded request payload. If the payload is stored encrypted with AES-GCM
        /// and a valid payload encryption key is configured, this will contain the decrypted
        /// UTF-8 JSON. Otherwise the controller will attempt to decode the bytes as UTF-8 and
        /// fall back to a Base64 string when decoding is not possible.
        /// </summary>
        public string? RequestPayload { get; set; }

        /// <summary>True when the stored payload is PGP-encrypted and this API's key couldn't decrypt it.</summary>
        public bool PayloadEncrypted { get; set; }
    }
}