using System.Text.Json;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Data.SqlClient;
using Microsoft.Extensions.Options;
using ProcessRequestEndPoint.Auth;
using ProcessRequestEndPoint.Letters;
using ProcessRequestEndPoint.Pdf;

namespace ProcessRequestEndPoint.Controllers
{
    /// <summary>Branded letter templates (versioned per document type) and letter previews.</summary>
    [ApiController]
    [Route("api/portal/letters")]
    public class LettersController : ControllerBase
    {
        private readonly LetterTemplateRepository _templates;
        private readonly LetterBrandingOptions _brand;
        private readonly PdfGenerationOptions _pdf;
        private readonly PortalAccessService _access;
        private readonly AuthRepository _auth;
        private readonly IServiceProvider _services;

        public LettersController(LetterTemplateRepository templates, IOptions<LetterBrandingOptions> brand, IOptions<PdfGenerationOptions> pdf,
            PortalAccessService access, AuthRepository auth, IServiceProvider services)
        {
            _templates = templates;
            _brand = brand.Value;
            _pdf = pdf.Value;
            _access = access;
            _auth = auth;
            _services = services;
        }

        [RequirePermission(Permissions.TemplatesManage)]
        [HttpGet("templates")]
        public async Task<IActionResult> List(CancellationToken ct) => Ok(await _templates.ListAsync(ct));

        [RequirePortalUser]
        [HttpGet("placeholders")]
        public IActionResult Placeholders() => Ok(LetterRenderer.Placeholders.Select(p => new { name = p.Name, description = p.Description }));

        [RequirePermission(Permissions.TemplatesManage)]
        [HttpPost("templates")]
        public async Task<IActionResult> Create([FromBody] SaveLetterTemplateRequest request, CancellationToken ct)
        {
            var problem = Validate(request, requireDocumentType: true);
            if (problem != null) return BadRequest(new { message = problem });
            var me = await _access.CurrentAsync();
            var id = await _templates.CreateDraftAsync(request, me?.UserName, ct);
            await Audit("LetterTemplateDrafted", $"{request.DocumentType} template {id}");
            return Ok(await _templates.GetAsync(id, ct));
        }

        [RequirePermission(Permissions.TemplatesManage)]
        [HttpPut("templates/{id:int}")]
        public async Task<IActionResult> Update(int id, [FromBody] SaveLetterTemplateRequest request, CancellationToken ct)
        {
            var problem = Validate(request, requireDocumentType: false);
            if (problem != null) return BadRequest(new { message = problem });
            if (!await _templates.UpdateDraftAsync(id, request, ct))
                return Conflict(new { message = "Only drafts can be edited. Create a new version to change a published template." });
            return Ok(await _templates.GetAsync(id, ct));
        }

        /// <summary>Publishes a draft (checking its placeholders); the previous published version is retired.</summary>
        [RequirePermission(Permissions.TemplatesManage)]
        [HttpPost("templates/{id:int}/publish")]
        public async Task<IActionResult> Publish(int id, CancellationToken ct)
        {
            var t = await _templates.GetAsync(id, ct);
            if (t == null) return NotFound();
            var unknown = LetterRenderer.UnknownPlaceholders(t.Subject, t.Salutation, t.Body, t.Closing, t.SignatureName, t.SignatureTitle);
            if (unknown.Count > 0) return BadRequest(new { message = "Unknown placeholders: " + string.Join(", ", unknown.Select(u => "{{" + u + "}}")) });
            var me = await _access.CurrentAsync();
            try
            {
                await _templates.PublishAsync(id, me?.UserName, ct);
            }
            catch (LetterTemplateException ex)
            {
                return Conflict(new { message = ex.Message });
            }
            await Audit("LetterTemplatePublished", $"{t.DocumentType} v{t.Version}");
            return Ok(await _templates.GetAsync(id, ct));
        }

        [RequirePermission(Permissions.TemplatesManage)]
        [HttpPost("templates/{id:int}/retire")]
        public async Task<IActionResult> Retire(int id, CancellationToken ct)
        {
            var t = await _templates.GetAsync(id, ct);
            if (t == null) return NotFound();
            if (!await _templates.RetireAsync(id, ct)) return Conflict(new { message = "That version is already retired." });
            await Audit("LetterTemplateRetired", $"{t.DocumentType} v{t.Version}");
            return Ok(await _templates.GetAsync(id, ct));
        }

        public sealed class PreviewRequest
        {
            /// <summary>A saved template; or leave empty and send <see cref="Template"/> (unsaved edits).</summary>
            public int? TemplateId { get; set; }
            public SaveLetterTemplateRequest? Template { get; set; }

            /// <summary>Fill from this stored record…</summary>
            public Guid? CorrespondenceId { get; set; }

            /// <summary>…or from this payload (e.g. the Insert form before sending). Neither = sample data.</summary>
            public string? Payload { get; set; }
        }

        /// <summary>
        /// Renders a letter preview (not stored). Template managers can preview any template with sample data or a
        /// record; anyone who may create correspondence can preview the published letter for a payload before sending.
        /// Missing placeholder values are shown as [Name] and listed in the X-Letter-Missing header.
        /// </summary>
        [RequirePortalUser]
        [HttpPost("preview")]
        public async Task<IActionResult> Preview([FromBody] PreviewRequest request, CancellationToken ct)
        {
            var me = await _access.CurrentAsync();
            if (me == null) return Unauthorized();
            var canManage = me.Has(Permissions.TemplatesManage);
            if (!canManage && !me.Has(Permissions.CorrespondenceCreate)) return StatusCode(403, new { message = "You don't have permission to preview letters." });

            CorrespondencePdfRecord? record = null;
            string? json = request.Payload;
            if (request.CorrespondenceId.HasValue)
            {
                var resource = await _auth.GetCorrespondenceResourceAsync(request.CorrespondenceId.Value);
                if (resource == null || !PortalAccessService.InDataScope(me, resource)) return NotFound(new { message = "That correspondence record doesn't exist." });
                var decision = await _access.CheckAsync(Permissions.PayloadRead, resource);
                if (!decision.Allowed) return StatusCode(403, new { message = decision.Message });
                record = await _services.GetRequiredService<CorrespondencePdfRepository>().GetRecordAsync(request.CorrespondenceId.Value, ct);
                if (record?.RequestPayload != null)
                {
                    try { json = await _services.GetRequiredService<CorrespondencePayloadDecoder>().DecodeAsync(record.RequestPayload); }
                    catch (Exception) { return UnprocessableEntity(new { message = "The record's payload couldn't be decrypted with this environment's key." }); }
                }
            }
            json ??= canManage ? SamplePayload : null;
            if (json == null) return BadRequest(new { message = "Send the payload to preview." });
            try { using var _ = JsonDocument.Parse(json); }
            catch (JsonException) { return BadRequest(new { message = "The payload isn't valid JSON." }); }

            LetterTemplate? template;
            if (request.Template != null && canManage)
            {
                var problem = Validate(request.Template, requireDocumentType: false);
                if (problem != null) return BadRequest(new { message = problem });
                template = new LetterTemplate
                {
                    DocumentType = request.Template.DocumentType ?? "Preview", Name = request.Template.Name!, Subject = request.Template.Subject!,
                    Salutation = request.Template.Salutation ?? "Dear {{RecipientName}},", Body = request.Template.Body!,
                    Closing = request.Template.Closing ?? "Sincerely,", SignatureName = request.Template.SignatureName, SignatureTitle = request.Template.SignatureTitle
                };
            }
            else if (request.TemplateId.HasValue && canManage)
            {
                template = await _templates.GetAsync(request.TemplateId.Value, ct);
                if (template == null) return NotFound(new { message = "That template doesn't exist." });
            }
            else
            {
                var documentType = DocumentType(json);
                if (string.IsNullOrWhiteSpace(documentType)) return BadRequest(new { message = "The payload has no DocumentDescription, so no letter template applies." });
                try { template = await _templates.GetPublishedAsync(documentType, ct); }
                catch (SqlException ex) when (ex.Number == 208) { template = null; }
                if (template == null)
                    return NotFound(new { message = $"No published letter template for \"{documentType}\". The PDF for this request would be the data sheet." });
            }

            var missing = new SortedSet<string>(StringComparer.OrdinalIgnoreCase);
            var model = LetterRenderer.BuildModel(record, json, _brand, _pdf.MaskPolicyNumber, DateTime.UtcNow);
            var bytes = LetterRenderer.Render(template, model, _brand, preview: true, DateTime.UtcNow, missing);
            Response.Headers["X-Letter-Missing"] = string.Join(",", missing);
            Response.Headers["X-Letter-Template"] = template.TemplateId > 0 ? $"{template.DocumentType} v{template.Version} ({template.Status})" : "unsaved draft";
            Response.Headers["Cache-Control"] = "no-store";
            return File(bytes, "application/pdf", "letter-preview.pdf");
        }

        private static string? DocumentType(string json)
        {
            using var doc = JsonDocument.Parse(json);
            var root = doc.RootElement;
            JsonElement process = root;
            if (root.ValueKind == JsonValueKind.Object)
                foreach (var p in root.EnumerateObject())
                    if (p.Name.Equals("Process", StringComparison.OrdinalIgnoreCase)) process = p.Value;
            if (process.ValueKind != JsonValueKind.Object) return null;
            foreach (var p in process.EnumerateObject())
                if (p.Name.Equals("DocumentDescription", StringComparison.OrdinalIgnoreCase) && p.Value.ValueKind == JsonValueKind.String) return p.Value.GetString();
            return null;
        }

        private static string? Validate(SaveLetterTemplateRequest t, bool requireDocumentType)
        {
            if (requireDocumentType && string.IsNullOrWhiteSpace(t.DocumentType)) return "Enter the document type this template is for (as in the payload's DocumentDescription).";
            if (t.DocumentType?.Length > 100) return "The document type is at most 100 characters.";
            if (string.IsNullOrWhiteSpace(t.Name)) return "Give the template a name.";
            if (string.IsNullOrWhiteSpace(t.Subject)) return "Enter a subject line.";
            if (string.IsNullOrWhiteSpace(t.Body)) return "Write the letter body.";
            if (t.Body.Length > 20000) return "The body is limited to 20,000 characters.";
            return null;
        }

        private async Task Audit(string eventType, string detail)
        {
            var me = await _access.CurrentAsync();
            await _auth.WriteAuditAsync(me?.UserId, me?.UserName, eventType, "Success", detail, _access.ClientIp());
        }

        private const string SamplePayload = """
        {"Process":{"DocId":"DOC-0000000-sample","RequestingSystemName":"Claims System","ECCOTrackingClaimNumber":"CL-12345",
          "ECCOTrackingPolicyNumber":"PL-00001234","ECCOTrackingCustomerId":"CUST-1001","DeliveryMethod":"Mail","DocumentDescription":"Welcome Kit",
          "MailDeliveryInfo":{"RecipientFullName":"Jordan Sample","RecipientFirstName":"Jordan","RecipientLastName":"Sample",
            "RecipientAddress":{"RecipientAddressLine1":"123 Main Street","RecipientAddressLine2":"Apt 4B","RecipientAddressCity":"Springfield",
              "RecipientAddressST":"IL","RecipientAddressZIP":"62701","RecipientCountry":"US"}},
          "CompositionInfo":{"SolutionsId":"SOL-1","VersionCode":"v1"}}}
        """;
    }
}
