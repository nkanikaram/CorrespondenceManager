using System.Data;
using System.Globalization;
using System.Text.Json;
using System.Text.RegularExpressions;
using Microsoft.Data.SqlClient;
using Microsoft.Extensions.Options;
using ProcessRequestEndPoint.Auth;
using ProcessRequestEndPoint.Pdf;

namespace ProcessRequestEndPoint.Letters
{
    /// <summary>"Letters" configuration section: the letterhead used on every customer letter.</summary>
    public sealed class LetterBrandingOptions
    {
        public const string SectionName = "Letters";

        public string CompanyName { get; set; } = "ECCO Insurance";
        public List<string> AddressLines { get; set; } = new() { "100 Main Street", "Springfield, IL 62701" };
        public string? Phone { get; set; } = "1-800-555-0100";
        public string? Website { get; set; } = "www.example.com";
        /// <summary>Hex colour for the letterhead, e.g. #0066CC.</summary>
        public string BrandColor { get; set; } = "#0066CC";
    }

    public sealed class LetterTemplate
    {
        public int TemplateId { get; init; }
        public string DocumentType { get; init; } = string.Empty;
        public int Version { get; init; }
        public string Status { get; init; } = "Draft";
        public string Name { get; init; } = string.Empty;
        public string Subject { get; init; } = string.Empty;
        public string Salutation { get; init; } = "Dear {{RecipientName}},";
        public string Body { get; init; } = string.Empty;
        public string Closing { get; init; } = "Sincerely,";
        public string? SignatureName { get; init; }
        public string? SignatureTitle { get; init; }
        public DateTime CreatedUtc { get; init; }
        public string? CreatedBy { get; init; }
        public DateTime UpdatedUtc { get; init; }
        public DateTime? PublishedUtc { get; init; }
        public string? PublishedBy { get; init; }
    }

    public sealed class SaveLetterTemplateRequest
    {
        public string? DocumentType { get; set; }
        public string? Name { get; set; }
        public string? Subject { get; set; }
        public string? Salutation { get; set; }
        public string? Body { get; set; }
        public string? Closing { get; set; }
        public string? SignatureName { get; set; }
        public string? SignatureTitle { get; set; }
    }

    public sealed class LetterTemplateException : Exception
    {
        public LetterTemplateException(string message) : base(message) { }
    }

    /// <summary>dbo.LetterTemplate (Build/DBScripts/Portal/002_LetterTemplates.sql).</summary>
    public sealed class LetterTemplateRepository
    {
        private readonly string _connectionString;

        public LetterTemplateRepository(IConfiguration configuration, IOptions<PortalAuthOptions> authOptions)
        {
            var name = authOptions.Value.ConnectionStringName;
            _connectionString = configuration.GetConnectionString(name) ?? configuration[name]
                ?? throw new InvalidOperationException($"{name} is not configured.");
        }

        private const string Columns = "TemplateId, DocumentType, Version, Status, Name, Subject, Salutation, Body, Closing, SignatureName, SignatureTitle, " +
                                       "CreatedUtc, CreatedBy, UpdatedUtc, PublishedUtc, PublishedBy";

        private static LetterTemplate Read(SqlDataReader r)
        {
            string? S(int i) => r.IsDBNull(i) ? null : r.GetString(i);
            return new LetterTemplate
            {
                TemplateId = r.GetInt32(0), DocumentType = r.GetString(1), Version = r.GetInt32(2), Status = r.GetString(3), Name = r.GetString(4),
                Subject = r.GetString(5), Salutation = r.GetString(6), Body = r.GetString(7), Closing = r.GetString(8), SignatureName = S(9),
                SignatureTitle = S(10), CreatedUtc = r.GetDateTime(11), CreatedBy = S(12), UpdatedUtc = r.GetDateTime(13),
                PublishedUtc = r.IsDBNull(14) ? null : r.GetDateTime(14), PublishedBy = S(15)
            };
        }

        private async Task<List<LetterTemplate>> QueryAsync(string where, Action<SqlParameterCollection> bind, CancellationToken ct)
        {
            var list = new List<LetterTemplate>();
            await using var conn = new SqlConnection(_connectionString);
            await conn.OpenAsync(ct);
            await using var cmd = new SqlCommand($"SELECT {Columns} FROM dbo.LetterTemplate {where}", conn);
            bind(cmd.Parameters);
            await using var r = await cmd.ExecuteReaderAsync(ct);
            while (await r.ReadAsync(ct)) list.Add(Read(r));
            return list;
        }

        public Task<List<LetterTemplate>> ListAsync(CancellationToken ct) =>
            QueryAsync("ORDER BY DocumentType, Version DESC", _ => { }, ct);

        public async Task<LetterTemplate?> GetAsync(int id, CancellationToken ct) =>
            (await QueryAsync("WHERE TemplateId = @id", p => p.Add(new SqlParameter("@id", SqlDbType.Int) { Value = id }), ct)).FirstOrDefault();

        /// <summary>The published template for a document type (case-insensitive), or null.</summary>
        public async Task<LetterTemplate?> GetPublishedAsync(string documentType, CancellationToken ct) =>
            (await QueryAsync("WHERE Status = 'Published' AND DocumentType = @t",
                p => p.Add(new SqlParameter("@t", SqlDbType.NVarChar, 100) { Value = documentType.Trim() }), ct)).FirstOrDefault();

        /// <summary>New draft = next version of its document type.</summary>
        public async Task<int> CreateDraftAsync(SaveLetterTemplateRequest t, string? by, CancellationToken ct)
        {
            await using var conn = new SqlConnection(_connectionString);
            await conn.OpenAsync(ct);
            await using var cmd = new SqlCommand(@"
INSERT INTO dbo.LetterTemplate (DocumentType, Version, Status, Name, Subject, Salutation, Body, Closing, SignatureName, SignatureTitle, CreatedBy)
OUTPUT inserted.TemplateId
SELECT @type, ISNULL((SELECT MAX(Version) FROM dbo.LetterTemplate WITH (UPDLOCK, HOLDLOCK) WHERE DocumentType = @type), 0) + 1, 'Draft',
       @name, @subject, @salutation, @body, @closing, @sigName, @sigTitle, @by", conn);
            Bind(cmd.Parameters, t);
            cmd.Parameters.Add(new SqlParameter("@type", SqlDbType.NVarChar, 100) { Value = t.DocumentType!.Trim() });
            cmd.Parameters.Add(new SqlParameter("@by", SqlDbType.NVarChar, 100) { Value = (object?)by ?? DBNull.Value });
            return (int)(await cmd.ExecuteScalarAsync(ct))!;
        }

        /// <summary>Only drafts can be edited; published and retired versions are immutable.</summary>
        public async Task<bool> UpdateDraftAsync(int id, SaveLetterTemplateRequest t, CancellationToken ct)
        {
            await using var conn = new SqlConnection(_connectionString);
            await conn.OpenAsync(ct);
            await using var cmd = new SqlCommand(@"
UPDATE dbo.LetterTemplate SET Name = @name, Subject = @subject, Salutation = @salutation, Body = @body, Closing = @closing,
       SignatureName = @sigName, SignatureTitle = @sigTitle, UpdatedUtc = SYSUTCDATETIME()
WHERE TemplateId = @id AND Status = 'Draft'", conn);
            Bind(cmd.Parameters, t);
            cmd.Parameters.Add(new SqlParameter("@id", SqlDbType.Int) { Value = id });
            return await cmd.ExecuteNonQueryAsync(ct) > 0;
        }

        /// <summary>Publishes a draft and retires the previously published version of the same document type.</summary>
        public async Task PublishAsync(int id, string? by, CancellationToken ct)
        {
            await using var conn = new SqlConnection(_connectionString);
            await conn.OpenAsync(ct);
            await using var tx = (SqlTransaction)await conn.BeginTransactionAsync(ct);
            await using var cmd = new SqlCommand(@"
DECLARE @type NVARCHAR(100) = (SELECT DocumentType FROM dbo.LetterTemplate WITH (UPDLOCK) WHERE TemplateId = @id AND Status = 'Draft');
IF @type IS NULL THROW 50010, 'Only a draft can be published.', 1;
UPDATE dbo.LetterTemplate SET Status = 'Retired', UpdatedUtc = SYSUTCDATETIME() WHERE DocumentType = @type AND Status = 'Published';
UPDATE dbo.LetterTemplate SET Status = 'Published', PublishedUtc = SYSUTCDATETIME(), PublishedBy = @by, UpdatedUtc = SYSUTCDATETIME() WHERE TemplateId = @id;", conn, tx);
            cmd.Parameters.Add(new SqlParameter("@id", SqlDbType.Int) { Value = id });
            cmd.Parameters.Add(new SqlParameter("@by", SqlDbType.NVarChar, 100) { Value = (object?)by ?? DBNull.Value });
            try
            {
                await cmd.ExecuteNonQueryAsync(ct);
                await tx.CommitAsync(ct);
            }
            catch (SqlException ex) when (ex.Number == 50010)
            {
                throw new LetterTemplateException(ex.Message);
            }
        }

        /// <summary>Retires a published version (its document type falls back to the data sheet) or discards a draft's use.</summary>
        public async Task<bool> RetireAsync(int id, CancellationToken ct)
        {
            await using var conn = new SqlConnection(_connectionString);
            await conn.OpenAsync(ct);
            await using var cmd = new SqlCommand("UPDATE dbo.LetterTemplate SET Status = 'Retired', UpdatedUtc = SYSUTCDATETIME() WHERE TemplateId = @id AND Status <> 'Retired'", conn);
            cmd.Parameters.Add(new SqlParameter("@id", SqlDbType.Int) { Value = id });
            return await cmd.ExecuteNonQueryAsync(ct) > 0;
        }

        private static void Bind(SqlParameterCollection p, SaveLetterTemplateRequest t)
        {
            p.Add(new SqlParameter("@name", SqlDbType.NVarChar, 200) { Value = t.Name!.Trim() });
            p.Add(new SqlParameter("@subject", SqlDbType.NVarChar, 300) { Value = t.Subject!.Trim() });
            p.Add(new SqlParameter("@salutation", SqlDbType.NVarChar, 200) { Value = string.IsNullOrWhiteSpace(t.Salutation) ? "Dear {{RecipientName}}," : t.Salutation.Trim() });
            p.Add(new SqlParameter("@body", SqlDbType.NVarChar, -1) { Value = t.Body!.Replace("\r\n", "\n").Trim() });
            p.Add(new SqlParameter("@closing", SqlDbType.NVarChar, 200) { Value = string.IsNullOrWhiteSpace(t.Closing) ? "Sincerely," : t.Closing.Trim() });
            p.Add(new SqlParameter("@sigName", SqlDbType.NVarChar, 200) { Value = (object?)t.SignatureName?.Trim() ?? DBNull.Value });
            p.Add(new SqlParameter("@sigTitle", SqlDbType.NVarChar, 200) { Value = (object?)t.SignatureTitle?.Trim() ?? DBNull.Value });
        }
    }

    /// <summary>
    /// Fills a template from a request and lays the letter out as a PDF: letterhead, date, recipient address,
    /// subject, salutation, body paragraphs, closing and signature. Placeholders are {{Name}} (see
    /// <see cref="Placeholders"/>) or {{payload.Path.To.Field}}; unknown or empty ones become "[Name]" in previews
    /// and blank in generated letters.
    /// </summary>
    public static class LetterRenderer
    {
        private static readonly Regex PlaceholderPattern = new(@"\{\{\s*([A-Za-z][A-Za-z0-9_.\[\]]*)\s*\}\}", RegexOptions.Compiled);

        public static readonly IReadOnlyList<(string Name, string Description)> Placeholders = new[]
        {
            ("RecipientName", "Recipient's full name"),
            ("RecipientFirstName", "Recipient's first name"),
            ("RecipientLastName", "Recipient's last name"),
            ("ClaimNumber", "Claim number"),
            ("PolicyNumber", "Policy number (masked to the last 4 characters)"),
            ("CustomerId", "Customer id"),
            ("DocId", "Document id"),
            ("DocumentType", "Document type, e.g. Welcome Kit"),
            ("RequestingSystem", "System that requested the letter"),
            ("DeliveryMethod", "Delivery method"),
            ("Today", "Today's date, e.g. November 3, 2026"),
            ("CompanyName", "Your company name (letterhead settings)"),
            ("CompanyPhone", "Your phone number"),
            ("CompanyWebsite", "Your website"),
            ("payload.Path.To.Field", "Any payload field by its path, e.g. payload.MailDeliveryInfo.RecipientAddress.RecipientAddressCity")
        };

        private static readonly HashSet<string> KnownNames = Placeholders.Select(p => p.Name).Where(n => !n.StartsWith("payload.")).ToHashSet(StringComparer.OrdinalIgnoreCase);

        /// <summary>Placeholders a template uses that the renderer doesn't know (checked before publishing).</summary>
        public static List<string> UnknownPlaceholders(params string?[] texts) =>
            texts.Where(t => t != null).SelectMany(t => PlaceholderPattern.Matches(t!).Select(m => m.Groups[1].Value))
                .Where(n => !KnownNames.Contains(n) && !n.StartsWith("payload.", StringComparison.OrdinalIgnoreCase))
                .Distinct(StringComparer.OrdinalIgnoreCase).ToList();

        public sealed class LetterModel
        {
            public Dictionary<string, string?> Values { get; } = new(StringComparer.OrdinalIgnoreCase);
            public JsonElement? Payload { get; set; }
            public List<string> AddressLines { get; } = new();
        }

        /// <summary>Values for the placeholders, from the request record (may be null in previews) and its payload JSON.</summary>
        public static LetterModel BuildModel(CorrespondencePdfRecord? record, string? payloadJson, LetterBrandingOptions brand, bool maskPolicy, DateTime today)
        {
            var model = new LetterModel();
            JsonElement process = default;
            if (!string.IsNullOrWhiteSpace(payloadJson))
            {
                using var doc = JsonDocument.Parse(payloadJson);
                var root = doc.RootElement.Clone();
                model.Payload = root;
                process = Prop(root, "Process") ?? root;
            }
            var mail = process.ValueKind == JsonValueKind.Object ? Prop(process, "MailDeliveryInfo") : null;
            var address = mail.HasValue ? Prop(mail.Value, "RecipientAddress") : null;

            string? P(JsonElement? obj, string name) => obj.HasValue && obj.Value.ValueKind == JsonValueKind.Object && Prop(obj.Value, name) is { } v
                ? (v.ValueKind == JsonValueKind.String ? v.GetString() : v.ValueKind is JsonValueKind.Null ? null : v.GetRawText()) : null;
            JsonElement? proc = process.ValueKind == JsonValueKind.Object ? process : null;

            var first = P(mail, "RecipientFirstName");
            var last = P(mail, "RecipientLastName");
            var full = P(mail, "RecipientFullName");
            if (string.IsNullOrWhiteSpace(full)) full = string.Join(" ", new[] { first, last }.Where(s => !string.IsNullOrWhiteSpace(s)));
            model.Values["RecipientName"] = string.IsNullOrWhiteSpace(full) ? null : full;
            model.Values["RecipientFirstName"] = first ?? full?.Split(' ').FirstOrDefault();
            model.Values["RecipientLastName"] = last;
            model.Values["ClaimNumber"] = record?.ClaimNumber ?? P(proc, "ECCOTrackingClaimNumber");
            model.Values["PolicyNumber"] = CorrespondencePdfRenderer.MaskPolicy(P(proc, "ECCOTrackingPolicyNumber"), maskPolicy);
            model.Values["CustomerId"] = record?.CustomerId ?? P(proc, "ECCOTrackingCustomerId");
            model.Values["DocId"] = record?.DocId ?? P(proc, "DocId");
            model.Values["DocumentType"] = P(proc, "DocumentDescription");
            model.Values["RequestingSystem"] = record?.RequestingDomainName ?? P(proc, "RequestingSystemName");
            model.Values["DeliveryMethod"] = record?.DeliveryMethod ?? P(proc, "DeliveryMethod");
            model.Values["Today"] = today.ToString("MMMM d, yyyy", CultureInfo.InvariantCulture);
            model.Values["CompanyName"] = brand.CompanyName;
            model.Values["CompanyPhone"] = brand.Phone;
            model.Values["CompanyWebsite"] = brand.Website;

            if (!string.IsNullOrWhiteSpace(full)) model.AddressLines.Add(full);
            if (address.HasValue)
            {
                foreach (var key in new[] { "RecipientAddressLine1", "RecipientAddressLine2", "RecipientAddressLine3" })
                    if (P(address, key) is { Length: > 0 } line) model.AddressLines.Add(line);
                var cityLine = string.Join(" ", new[]
                {
                    P(address, "RecipientAddressCity") is { Length: > 0 } c ? c + "," : null, P(address, "RecipientAddressST"), P(address, "RecipientAddressZIP")
                }.Where(s => !string.IsNullOrWhiteSpace(s)));
                if (cityLine.Length > 0) model.AddressLines.Add(cityLine);
                if (P(address, "RecipientCountry") is { Length: > 0 } country && !country.Equals("US", StringComparison.OrdinalIgnoreCase)) model.AddressLines.Add(country);
            }
            return model;
        }

        /// <summary>Fills placeholders. Missing values: "[Name]" when <paramref name="preview"/>, otherwise blank.</summary>
        public static string Fill(string text, LetterModel model, bool preview, ISet<string>? missing = null)
        {
            return PlaceholderPattern.Replace(text, m =>
            {
                var name = m.Groups[1].Value;
                string? value = null;
                if (name.StartsWith("payload.", StringComparison.OrdinalIgnoreCase))
                {
                    if (model.Payload.HasValue) value = Path(model.Payload.Value, name.Substring("payload.".Length));
                }
                else model.Values.TryGetValue(name, out value);
                if (!string.IsNullOrWhiteSpace(value)) return value!;
                missing?.Add(name);
                return preview ? "[" + name + "]" : string.Empty;
            });
        }

        public static byte[] Render(LetterTemplate template, LetterModel model, LetterBrandingOptions brand, bool preview, DateTime generatedUtc, ISet<string>? missing = null)
        {
            string F(string? text) => text == null ? string.Empty : Fill(text, model, preview, missing);
            var color = ParseColor(brand.BrandColor);
            var pdf = new SimplePdfDocument(F(template.Subject))
            {
                FooterText = string.Join("   ·   ", new[]
                {
                    brand.CompanyName, brand.Phone, brand.Website,
                    model.Values.TryGetValue("DocId", out var docId) && !string.IsNullOrWhiteSpace(docId) ? "Ref " + docId : null,
                    preview ? "PREVIEW" : null
                }.Where(s => !string.IsNullOrWhiteSpace(s)))
            };

            pdf.Letterhead(brand.CompanyName, brand.AddressLines.Concat(new[] { brand.Phone, brand.Website }).Where(s => !string.IsNullOrWhiteSpace(s)).ToList()!, color);
            pdf.RightAligned(model.Values["Today"]!);
            pdf.Spacer(8);
            if (model.AddressLines.Count > 0) pdf.Lines(model.AddressLines);
            else if (preview) pdf.Lines(new[] { "[Recipient address]" });
            pdf.Spacer(18);
            pdf.Paragraph(F(template.Subject), 11, PdfFont.Bold);
            pdf.Spacer(4);
            pdf.Paragraph(F(template.Salutation), 10.5);
            foreach (var paragraph in F(template.Body).Replace("\r\n", "\n").Split("\n\n", StringSplitOptions.RemoveEmptyEntries))
                pdf.Paragraph(paragraph.Trim(), 10.5);
            pdf.Spacer(6);
            pdf.Paragraph(F(template.Closing), 10.5);
            pdf.Spacer(26);
            var signature = new[] { F(template.SignatureName), F(template.SignatureTitle) }.Where(s => !string.IsNullOrWhiteSpace(s)).ToList();
            if (signature.Count > 0)
            {
                pdf.Lines(signature.Take(1), 10.5, PdfFont.Bold);
                pdf.Lines(signature.Skip(1), 10);
            }
            return pdf.ToBytes(generatedUtc);
        }

        private static (double, double, double) ParseColor(string hex)
        {
            var h = (hex ?? string.Empty).Trim().TrimStart('#');
            if (h.Length != 6 || !int.TryParse(h, NumberStyles.HexNumber, CultureInfo.InvariantCulture, out var v)) return (0.0, 0.40, 0.80);
            return (((v >> 16) & 0xFF) / 255.0, ((v >> 8) & 0xFF) / 255.0, (v & 0xFF) / 255.0);
        }

        private static string? Path(JsonElement root, string path)
        {
            JsonElement current = root;
            foreach (var part in path.Split('.', StringSplitOptions.RemoveEmptyEntries))
            {
                var name = part;
                int? index = null;
                var bracket = part.IndexOf('[');
                if (bracket > 0 && part.EndsWith(']') && int.TryParse(part[(bracket + 1)..^1], out var i)) { name = part[..bracket]; index = i; }
                var next = Prop(current, name);
                if (next == null && current.ValueKind == JsonValueKind.Object && Prop(current, "Process") is { } process) next = Prop(process, name);
                if (next == null) return null;
                current = next.Value;
                if (index.HasValue)
                {
                    if (current.ValueKind != JsonValueKind.Array || current.GetArrayLength() <= index.Value) return null;
                    current = current[index.Value];
                }
            }
            return current.ValueKind switch
            {
                JsonValueKind.String => current.GetString(),
                JsonValueKind.Null or JsonValueKind.Undefined or JsonValueKind.Object or JsonValueKind.Array => null,
                _ => current.GetRawText()
            };
        }

        private static JsonElement? Prop(JsonElement obj, string name)
        {
            if (obj.ValueKind != JsonValueKind.Object) return null;
            foreach (var p in obj.EnumerateObject())
                if (p.Name.Equals(name, StringComparison.OrdinalIgnoreCase)) return p.Value;
            return null;
        }
    }
}
