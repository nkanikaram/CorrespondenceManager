using System.Security.Cryptography;
using Microsoft.Extensions.Options;

namespace ProcessRequestEndPoint.Pdf
{
    public sealed class PdfGenerationException : Exception
    {
        public PdfGenerationException(string message, Exception? inner = null) : base(message, inner) { }
    }

    /// <summary>
    /// Generates one request's PDF end to end: load the record, decode the stored payload, render, upload to
    /// Blob Storage, record the result. Used by the background worker and by the portal's on-demand action.
    /// </summary>
    public sealed class CorrespondencePdfService
    {
        private readonly CorrespondencePdfRepository _repository;
        private readonly CorrespondencePayloadDecoder _decoder;
        private readonly PdfBlobStorage _storage;
        private readonly PdfGenerationOptions _options;
        private readonly ILogger<CorrespondencePdfService> _logger;
        private readonly IServiceProvider _services;

        public CorrespondencePdfService(CorrespondencePdfRepository repository, CorrespondencePayloadDecoder decoder, PdfBlobStorage storage,
            IOptions<PdfGenerationOptions> options, ILogger<CorrespondencePdfService> logger, IServiceProvider services)
        {
            _repository = repository;
            _decoder = decoder;
            _storage = storage;
            _options = options.Value;
            _logger = logger;
            _services = services;
        }

        /// <summary>
        /// The customer letter when a published template exists for the payload's document type; otherwise the data
        /// sheet. Returns the PDF and the template used (null for the data sheet).
        /// </summary>
        private async Task<(byte[] Pdf, Letters.LetterTemplate? Template)> RenderAsync(CorrespondencePdfRecord record, string json, CancellationToken ct)
        {
            var templates = _services.GetService<Letters.LetterTemplateRepository>();
            var documentType = DocumentTypeOf(json);
            if (templates != null && !string.IsNullOrWhiteSpace(documentType))
            {
                Letters.LetterTemplate? template = null;
                try { template = await templates.GetPublishedAsync(documentType, ct); }
                catch (Microsoft.Data.SqlClient.SqlException ex) when (ex.Number == 208) { /* templates table not created here */ }
                if (template != null)
                {
                    var brand = _services.GetRequiredService<IOptions<Letters.LetterBrandingOptions>>().Value;
                    var model = Letters.LetterRenderer.BuildModel(record, json, brand, _options.MaskPolicyNumber, DateTime.UtcNow);
                    return (Letters.LetterRenderer.Render(template, model, brand, preview: false, DateTime.UtcNow), template);
                }
            }
            return (CorrespondencePdfRenderer.Render(record, json, _options.MaskPolicyNumber, DateTime.UtcNow), null);
        }

        private static string? DocumentTypeOf(string json)
        {
            using var doc = System.Text.Json.JsonDocument.Parse(json);
            var root = doc.RootElement;
            if (root.ValueKind != System.Text.Json.JsonValueKind.Object) return null;
            foreach (var p in root.EnumerateObject())
            {
                if (p.Name.Equals("Process", StringComparison.OrdinalIgnoreCase) && p.Value.ValueKind == System.Text.Json.JsonValueKind.Object)
                    foreach (var q in p.Value.EnumerateObject())
                        if (q.Name.Equals("DocumentDescription", StringComparison.OrdinalIgnoreCase)) return q.Value.ValueKind == System.Text.Json.JsonValueKind.String ? q.Value.GetString() : null;
                if (p.Name.Equals("DocumentDescription", StringComparison.OrdinalIgnoreCase)) return p.Value.ValueKind == System.Text.Json.JsonValueKind.String ? p.Value.GetString() : null;
            }
            return null;
        }

        /// <summary>Blob path: yyyy/MM/{correspondenceId}.pdf (by request creation month).</summary>
        public static string BlobNameFor(CorrespondencePdfRecord record) =>
            $"{(record.RequestCreationTS ?? DateTime.UtcNow):yyyy}/{(record.RequestCreationTS ?? DateTime.UtcNow):MM}/{record.CorrespondenceId}.pdf";

        /// <summary>Generates now (on demand). Throws <see cref="PdfGenerationException"/> with a user-safe message.</summary>
        public async Task<PdfStatus> GenerateNowAsync(Guid id, CancellationToken ct)
        {
            await _repository.BeginOnDemandAsync(id, _options.LeaseSeconds, ct);
            await GenerateClaimedAsync(id, ct);
            return (await _repository.GetStatusesAsync(new[] { id }, ct))[0];
        }

        /// <summary>Generates a request the caller has already claimed (leased). Records success or failure.</summary>
        public async Task GenerateClaimedAsync(Guid id, CancellationToken ct)
        {
            try
            {
                var record = await _repository.GetRecordAsync(id, ct)
                             ?? throw new PdfGenerationException("The correspondence record no longer exists.");
                if (record.RequestPayload == null || record.RequestPayload.Length == 0)
                    throw new PdfGenerationException("The record has no stored payload.");

                string json;
                try
                {
                    json = await _decoder.DecodeAsync(record.RequestPayload);
                }
                catch (Exception ex)
                {
                    throw new PdfGenerationException("The payload couldn't be decrypted with this environment's key.", ex);
                }

                byte[] pdf;
                Letters.LetterTemplate? template;
                try
                {
                    (pdf, template) = await RenderAsync(record, json, ct);
                }
                catch (System.Text.Json.JsonException ex)
                {
                    throw new PdfGenerationException("The payload isn't valid JSON.", ex);
                }

                var blobName = BlobNameFor(record);
                await _storage.UploadAsync(blobName, pdf, new Dictionary<string, string>
                {
                    ["correspondenceId"] = id.ToString(),
                    ["docId"] = record.DocId ?? string.Empty
                }, ct);
                await _repository.CompleteAsync(id, blobName, pdf.Length, SHA256.HashData(pdf), template?.TemplateId, template?.Version, ct);
                _logger.LogInformation("Generated {Kind} PDF for correspondence {CorrespondenceId} ({Bytes} bytes)",
                    template != null ? $"letter ({template.DocumentType} v{template.Version})" : "data-sheet", id, pdf.Length);
                var events = _services.GetService<Realtime.PortalEvents>();
                if (events != null) await events.PdfStatusChangedAsync(id, "Generated");
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                var message = ex is PdfGenerationException ? ex.Message : "PDF generation failed: " + ex.GetType().Name + ": " + ex.Message;
                _logger.LogWarning(ex, "PDF generation failed for correspondence {CorrespondenceId}", id);
                await _repository.FailAsync(id, message, CancellationToken.None);
                var events = _services.GetService<Realtime.PortalEvents>();
                if (events != null) await events.PdfStatusChangedAsync(id, "Failed");
                throw ex is PdfGenerationException ? ex : new PdfGenerationException(message, ex);
            }
        }
    }

    /// <summary>
    /// Background worker: every PollIntervalSeconds, enqueue recent requests that have no PDF yet, claim a batch
    /// (pending, retryable failures, expired leases) and generate them. Safe to run on several instances.
    /// </summary>
    public sealed class CorrespondencePdfWorker : BackgroundService
    {
        private readonly IServiceScopeFactory _scopes;
        private readonly PdfGenerationOptions _options;
        private readonly ILogger<CorrespondencePdfWorker> _logger;

        public CorrespondencePdfWorker(IServiceScopeFactory scopes, IOptions<PdfGenerationOptions> options, ILogger<CorrespondencePdfWorker> logger)
        {
            _scopes = scopes;
            _options = options.Value;
            _logger = logger;
        }

        protected override async Task ExecuteAsync(CancellationToken stoppingToken)
        {
            _logger.LogInformation("Correspondence PDF worker started (every {Seconds}s, batch {Batch}, backfill {Days} day(s))",
                _options.PollIntervalSeconds, _options.BatchSize, _options.BackfillDays);
            var delay = TimeSpan.FromSeconds(Math.Max(5, _options.PollIntervalSeconds));
            while (!stoppingToken.IsCancellationRequested)
            {
                try
                {
                    await RunOnceAsync(stoppingToken);
                }
                catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
                {
                    break;
                }
                catch (Exception ex)
                {
                    // e.g. the dbo.CorrespondencePdfDocument table hasn't been created, or storage is unreachable.
                    _logger.LogError(ex, "Correspondence PDF worker cycle failed. Has Build/DBScripts/Pdf been applied, and is Blob Storage reachable?");
                }
                try { await Task.Delay(delay, stoppingToken); } catch (OperationCanceledException) { break; }
            }
        }

        /// <summary>One cycle; returns how many PDFs were generated. Public for tests.</summary>
        public async Task<int> RunOnceAsync(CancellationToken ct)
        {
            using var scope = _scopes.CreateScope();
            var repository = scope.ServiceProvider.GetRequiredService<CorrespondencePdfRepository>();
            var service = scope.ServiceProvider.GetRequiredService<CorrespondencePdfService>();

            var now = DateTime.UtcNow;
            await repository.EnqueueNewRequestsAsync(now.AddDays(-Math.Max(0, _options.BackfillDays)), now.AddSeconds(-_options.MinAgeSeconds),
                Math.Max(1, _options.BatchSize) * 5, ct);

            var generated = 0;
            foreach (var id in await repository.ClaimAsync(Math.Max(1, _options.BatchSize), _options.MaxAttempts, _options.LeaseSeconds, ct))
            {
                try
                {
                    await service.GenerateClaimedAsync(id, ct);
                    generated++;
                }
                catch (PdfGenerationException)
                {
                    // Recorded on the row (Failed, with backoff); keep going with the rest of the batch.
                }
            }
            return generated;
        }
    }
}
