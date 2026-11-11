using Microsoft.AspNetCore.Mvc;
using Microsoft.Data.SqlClient;
using ProcessRequestEndPoint.Auth;
using ProcessRequestEndPoint.Pdf;

namespace ProcessRequestEndPoint.Controllers
{
    /// <summary>
    /// Portal access to the PDFs rendered from correspondence payloads (see ProcessRequestEndPoint/Pdf).
    /// A PDF holds the same personal data as the payload, so downloading it needs payload access
    /// (permission + PII-clearance policy) and the record must be within the user's data scope.
    /// </summary>
    [ApiController]
    [Route("api/portal/correspondence")]
    public class CorrespondencePdfController : ControllerBase
    {
        private const int MaxStatusBatch = 500;
        private readonly CorrespondencePdfRepository _pdfs;
        private readonly AuthRepository _auth;
        private readonly PortalAccessService _access;
        private readonly IServiceProvider _services;

        public CorrespondencePdfController(CorrespondencePdfRepository pdfs, AuthRepository auth, PortalAccessService access, IServiceProvider services)
        {
            _pdfs = pdfs;
            _auth = auth;
            _access = access;
            _services = services;
        }

        public sealed class PdfStatusRequest
        {
            public List<Guid> CorrespondenceIds { get; set; } = new();
        }

        /// <summary>PDF state of each listed record: None | Pending | Processing | Generated | Failed.</summary>
        [RequirePermission(Permissions.CorrespondenceRead)]
        [HttpPost("pdf/status")]
        public async Task<IActionResult> Status([FromBody] PdfStatusRequest request, CancellationToken ct)
        {
            var ids = (request?.CorrespondenceIds ?? new()).Where(i => i != Guid.Empty).Distinct().Take(MaxStatusBatch).ToList();
            var user = await _access.CurrentAsync();
            var canGenerate = user?.Has(Permissions.PdfGenerate) == true;
            try
            {
                var statuses = await _pdfs.GetStatusesAsync(ids, ct);
                return Ok(new
                {
                    storageConfigured = _services.GetService<CorrespondencePdfService>() != null,
                    items = statuses.Select(s => new
                    {
                        correspondenceId = s.CorrespondenceId,
                        status = s.Status,
                        generatedUtc = s.GeneratedUtc,
                        sizeBytes = s.ContentLength,
                        attempts = s.Attempts,
                        // Failure reasons can mention infrastructure; only shown to those who can act on them.
                        lastError = canGenerate ? s.LastError : null
                    })
                });
            }
            catch (SqlException ex) when (ex.Number == 208) // table missing: Build/DBScripts/Pdf not applied
            {
                return Ok(new { storageConfigured = false, items = ids.Select(id => new { correspondenceId = id, status = "None" }) });
            }
        }

        /// <summary>Downloads the stored PDF.</summary>
        [RequirePermission(Permissions.PayloadRead)]
        [HttpGet("{correspondenceId:guid}/pdf")]
        public async Task<IActionResult> Download(Guid correspondenceId, CancellationToken ct)
        {
            var storage = _services.GetService<PdfBlobStorage>();
            if (storage == null) return StatusCode(StatusCodes.Status503ServiceUnavailable, new { message = "PDF storage isn't configured on this server." });

            var denied = await AuthorizeRecordAsync(correspondenceId, Permissions.PayloadRead);
            if (denied != null) return denied;

            var status = (await _pdfs.GetStatusesAsync(new[] { correspondenceId }, ct))[0];
            if (status.Status != "Generated" || status.BlobName == null)
                return NotFound(new { message = status.Status == "None" ? "No PDF has been generated for this record yet." : $"The PDF is {status.Status.ToLowerInvariant()}." });

            var stream = await storage.OpenReadAsync(status.BlobName, ct);
            if (stream == null) return NotFound(new { message = "The PDF file is missing from storage. Generate it again." });

            var user = await _access.CurrentAsync();
            await _auth.WriteAuditAsync(user?.UserId, user?.UserName, "PdfDownloaded", "Success", $"record {correspondenceId}", _access.ClientIp(), correspondenceId);
            Response.Headers["Cache-Control"] = "no-store";
            return File(stream, "application/pdf", $"correspondence-{correspondenceId}.pdf");
        }

        /// <summary>Generates (or regenerates) the PDF now and returns its new state.</summary>
        [RequirePermission(Permissions.PdfGenerate)]
        [HttpPost("{correspondenceId:guid}/pdf")]
        public async Task<IActionResult> Generate(Guid correspondenceId, CancellationToken ct)
        {
            var service = _services.GetService<CorrespondencePdfService>();
            if (service == null) return StatusCode(StatusCodes.Status503ServiceUnavailable, new { message = "PDF storage isn't configured on this server." });

            var denied = await AuthorizeRecordAsync(correspondenceId, Permissions.PdfGenerate);
            if (denied != null) return denied;

            try
            {
                var status = await service.GenerateNowAsync(correspondenceId, ct);
                var user = await _access.CurrentAsync();
                await _auth.WriteAuditAsync(user?.UserId, user?.UserName, "PdfGenerated", "Success", $"record {correspondenceId}", _access.ClientIp(), correspondenceId);
                return Ok(new { correspondenceId, status = status.Status, generatedUtc = status.GeneratedUtc, sizeBytes = status.ContentLength });
            }
            catch (PdfGenerationException ex)
            {
                return UnprocessableEntity(new { message = ex.Message });
            }
        }

        /// <summary>Null when allowed; otherwise the 404/403 to return. Applies data scope, then the permission's policies.</summary>
        private async Task<IActionResult?> AuthorizeRecordAsync(Guid id, string permission)
        {
            var resource = await _auth.GetCorrespondenceResourceAsync(id);
            if (resource == null) return NotFound(new { message = "That correspondence record doesn't exist." });
            var user = await _access.CurrentAsync();
            // Out-of-scope records look the same as missing ones, as in search.
            if (user == null || !PortalAccessService.InDataScope(user, resource))
                return NotFound(new { message = "That correspondence record doesn't exist." });
            var decision = await _access.CheckAsync(permission, resource);
            if (decision.Allowed) return null;
            await _access.AuditDeniedAsync(permission, decision, $"record {id}");
            return StatusCode(StatusCodes.Status403Forbidden, new { message = decision.Message, policy = decision.PolicyName });
        }
    }
}
