using Microsoft.AspNetCore.Mvc;
using Microsoft.Data.SqlClient;
using Microsoft.Extensions.Options;
using ProcessRequestEndPoint.Auth;
using ProcessRequestEndPoint.Pdf;
using ProcessRequestEndPoint.Records;

namespace ProcessRequestEndPoint.Controllers
{
    /// <summary>Record detail panel (fields, status timeline, payload, PDF, activity) and the recycle bin.</summary>
    [ApiController]
    [Route("api/portal")]
    public class CorrespondenceRecordsController : ControllerBase
    {
        private readonly CorrespondenceDetailsRepository _details;
        private readonly RecycleBinRepository _bin;
        private readonly AuthRepository _auth;
        private readonly PortalAccessService _access;
        private readonly IServiceProvider _services;
        private readonly RetentionOptions _retention;

        public CorrespondenceRecordsController(CorrespondenceDetailsRepository details, RecycleBinRepository bin, AuthRepository auth,
            PortalAccessService access, IServiceProvider services, IOptions<RetentionOptions> retention)
        {
            _details = details;
            _bin = bin;
            _auth = auth;
            _access = access;
            _services = services;
            _retention = retention.Value;
        }

        /// <summary>
        /// Everything about one record: fields, status timeline, PDF state, activity from the audit log, and the
        /// payload when the user may see it (permission + PII-clearance policy).
        /// </summary>
        [RequirePermission(Permissions.CorrespondenceRead)]
        [HttpGet("correspondence/{id:guid}/details")]
        public async Task<IActionResult> Details(Guid id, CancellationToken ct)
        {
            var record = await _details.GetAsync(id, ct);
            var user = await _access.CurrentAsync();
            var resource = record == null ? null : new Dictionary<string, string?>(StringComparer.OrdinalIgnoreCase)
            {
                ["customer"] = record.RequestingDomainName, ["deliveryMethod"] = record.DeliveryMethod, ["status"] = record.LatestStatus
            };
            if (record == null || user == null || !PortalAccessService.InDataScope(user, resource!))
                return NotFound(new { message = "That correspondence record doesn't exist." });

            string? payload = null;
            string? payloadHiddenReason = null;
            var payloadDecision = await _access.CheckAsync(Permissions.PayloadRead, resource);
            if (!payloadDecision.Allowed) payloadHiddenReason = payloadDecision.Message;
            else if (record.RequestPayload is { Length: > 0 })
            {
                try { payload = await _services.GetRequiredService<CorrespondencePayloadDecoder>().DecodeAsync(record.RequestPayload); }
                catch (Exception) { payloadHiddenReason = "The payload couldn't be decrypted with this environment's key."; }
            }

            object? pdf = null;
            var pdfs = _services.GetService<CorrespondencePdfRepository>();
            if (pdfs != null)
            {
                try
                {
                    var s = (await pdfs.GetStatusesAsync(new[] { id }, ct))[0];
                    pdf = new { status = s.Status, generatedUtc = s.GeneratedUtc, sizeBytes = s.ContentLength,
                                lastError = user.Has(Permissions.PdfGenerate) ? s.LastError : null };
                }
                catch (SqlException ex) when (ex.Number == 208) { /* PDF table not created here */ }
            }

            var activity = await _auth.ListAuditForResourceAsync(id, 100, ct);
            return Ok(new
            {
                record = new
                {
                    correspondenceId = record.CorrespondenceId, docId = record.DocId, claimNumber = record.ClaimNumber, customerId = record.CustomerId,
                    requestingDomainName = record.RequestingDomainName, deliveryMethod = record.DeliveryMethod, deliveryMethodCode = record.DeliveryMethodCode,
                    versionCode = record.VersionCode, archiveDocumentTypeCode = record.ArchiveDocumentTypeCode,
                    archiveDocumentSubTypeCode = record.ArchiveDocumentSubTypeCode, vfResponseId = record.VFResponseId,
                    requestCreationTS = record.RequestCreationTS, latestStatus = record.LatestStatus
                },
                statusHistory = record.StatusHistory,
                payload,
                payloadHiddenReason,
                pdf,
                activity
            });
        }

        // ---------------- Recycle bin ----------------

        private async Task<bool> InScopeAsync(RecycleBinEntry entry)
        {
            var user = await _access.CurrentAsync();
            return user != null && PortalAccessService.InDataScope(user, new Dictionary<string, string?>
            {
                ["customer"] = entry.RequestingDomainName, ["deliveryMethod"] = entry.DeliveryMethodCode
            });
        }

        [RequirePermission(Permissions.Recycle)]
        [HttpGet("recycle-bin")]
        public async Task<IActionResult> RecycleBin([FromQuery] string? search, [FromQuery] int top = 200, CancellationToken ct = default)
        {
            var entries = await _bin.ListAsync(search, top, null, ct);
            var visible = new List<RecycleBinEntry>();
            foreach (var e in entries) if (await InScopeAsync(e)) visible.Add(e);
            return Ok(new { retentionDays = _retention.RecycleBinDays, items = visible });
        }

        [RequirePermission(Permissions.Recycle)]
        [HttpPost("recycle-bin/{id:guid}/restore")]
        public async Task<IActionResult> Restore(Guid id, CancellationToken ct)
        {
            var entry = await _bin.GetAsync(id, ct);
            if (entry == null || !await InScopeAsync(entry)) return NotFound(new { message = "That record is not in the recycle bin." });
            var failure = await RestoreOneAsync(entry, ct);
            if (failure != null) return failure.Value.Conflict ? Conflict(new { message = failure.Value.Message }) : StatusCode(500, new { message = failure.Value.Message });
            return Ok(new { correspondenceId = id });
        }

        public sealed class RestoreManyRequest
        {
            public List<Guid>? CorrespondenceIds { get; set; }
        }

        /// <summary>
        /// Restores several records. Each one is restored in its own transaction, so one failure doesn't block the
        /// rest. Returns { restored: [ids], failed: [{ correspondenceId, docId, message }], notFound: [ids] }.
        /// </summary>
        [RequirePermission(Permissions.Recycle)]
        [HttpPost("recycle-bin/restore")]
        public async Task<IActionResult> RestoreMany([FromBody] RestoreManyRequest request, CancellationToken ct)
        {
            var ids = (request?.CorrespondenceIds ?? new List<Guid>()).Where(g => g != Guid.Empty).Distinct().ToList();
            if (ids.Count == 0) return BadRequest(new { message = "Select at least one record to restore." });
            if (ids.Count > 500) return BadRequest(new { message = "Restore at most 500 records at a time." });

            var restored = new List<Guid>();
            var failed = new List<object>();
            var notFound = new List<Guid>();
            foreach (var id in ids)
            {
                var entry = await _bin.GetAsync(id, ct);
                if (entry == null || !await InScopeAsync(entry)) { notFound.Add(id); continue; }
                var failure = await RestoreOneAsync(entry, ct);
                if (failure == null) restored.Add(id);
                else failed.Add(new { correspondenceId = id, docId = entry.DocId, message = failure.Value.Message });
            }
            return Ok(new { restored, failed, notFound });
        }

        /// <summary>Restores one record and audits it; null on success, otherwise a message fit to show the user.</summary>
        private async Task<(string Message, bool Conflict)?> RestoreOneAsync(RecycleBinEntry entry, CancellationToken ct)
        {
            var user = await _access.CurrentAsync();
            try
            {
                await _bin.RestoreAsync(entry.CorrespondenceId, ct);
            }
            catch (RecycleBinException ex)
            {
                return (ex.Message, true);
            }
            catch (SqlException ex)
            {
                // Details go to the log and the audit trail, never to the browser.
                _services.GetRequiredService<ILogger<CorrespondenceRecordsController>>()
                    .LogError(ex, "Restoring correspondence {Id} from the recycle bin failed", entry.CorrespondenceId);
                await _auth.WriteAuditAsync(user?.UserId, user?.UserName, "CorrespondenceRestored", "Failure",
                    $"record {entry.CorrespondenceId}: SQL error {ex.Number}", _access.ClientIp(), entry.CorrespondenceId);
                var message = ex.Number == 547
                    ? "The database refused the record because a value no longer matches its lookup table (constraint conflict). It stays in the recycle bin."
                    : ex.Number is 1088 or 4902 or 15247 or 229
                        ? "The API's database login isn't allowed to restore records (it needs ALTER on the correspondence tables). It stays in the recycle bin."
                        : "The record couldn't be restored because of a database error. It stays in the recycle bin.";
                return (message, false);
            }
            await _auth.WriteAuditAsync(user?.UserId, user?.UserName, "CorrespondenceRestored", "Success", $"record {entry.CorrespondenceId}", _access.ClientIp(), entry.CorrespondenceId);
            return null;
        }

        /// <summary>Permanently removes one record from the recycle bin now (and its PDF).</summary>
        [RequirePermission(Permissions.Recycle)]
        [HttpDelete("recycle-bin/{id:guid}")]
        public async Task<IActionResult> Purge(Guid id, CancellationToken ct)
        {
            var entry = await _bin.GetAsync(id, ct);
            if (entry == null || !await InScopeAsync(entry)) return NotFound(new { message = "That record is not in the recycle bin." });
            var purged = await _bin.PurgeAsync(id, 1, ct);
            var storage = _services.GetService<PdfBlobStorage>();
            foreach (var (_, blob) in purged)
                if (blob != null && storage != null) await storage.DeleteIfExistsAsync(blob, ct);
            var user = await _access.CurrentAsync();
            await _auth.WriteAuditAsync(user?.UserId, user?.UserName, "CorrespondencePurged", "Success", $"record {id}", _access.ClientIp(), id);
            return NoContent();
        }
    }
}
