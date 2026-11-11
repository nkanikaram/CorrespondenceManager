using System.Globalization;
using System.Text.Json;

namespace ProcessRequestEndPoint.Pdf
{
    /// <summary>A correspondence request's database fields, as of PDF generation.</summary>
    public sealed class CorrespondencePdfRecord
    {
        public Guid CorrespondenceId { get; init; }
        public string? DocId { get; init; }
        public string? ClaimNumber { get; init; }
        public string? CustomerId { get; init; }
        public string? RequestingDomainName { get; init; }
        public string? DeliveryMethod { get; init; }
        public DateTime? RequestCreationTS { get; init; }
        public string? LatestStatus { get; init; }
        public byte[]? RequestPayload { get; init; }
    }

    /// <summary>
    /// Lays out the PDF for one correspondence request: request details, recipient, delivery and composition
    /// information from the payload, then every payload field as an appendix. Pure: same input, same layout.
    /// </summary>
    public static class CorrespondencePdfRenderer
    {
        private const int MaxValueLength = 600;
        private static readonly (double, double, double) Brand = (0.0, 0.40, 0.80);

        public static byte[] Render(CorrespondencePdfRecord record, string payloadJson, bool maskPolicyNumber, DateTime generatedUtc)
        {
            using var doc = JsonDocument.Parse(payloadJson);
            var root = doc.RootElement;
            // AddCorrespondenceDBHandler stores the SequentialProcess itself; the API request wraps it in { Process }.
            var process = Prop(root, "Process") ?? root;
            var mail = Prop(process, "MailDeliveryInfo");
            var address = mail.HasValue ? Prop(mail.Value, "RecipientAddress") : null;
            var journey = Prop(process, "JourneyDeliveryInfo");
            var composition = Prop(process, "CompositionInfo");

            var documentName = Str(process, "DocumentDescription");
            var pdf = new SimplePdfDocument($"Correspondence {record.DocId ?? record.CorrespondenceId.ToString()}")
            {
                FooterText = $"ECCO Correspondence Manager   ·   Contains personal data — confidential   ·   Generated {generatedUtc:yyyy-MM-dd HH:mm} UTC"
            };
            pdf.TitleBand("Correspondence Request", documentName ?? (record.DocId != null ? "Document " + record.DocId : null), Brand);

            pdf.Heading("Request");
            pdf.KeyValueTable(new (string, string?)[]
            {
                ("Correspondence Id", record.CorrespondenceId.ToString()),
                ("Doc Id", record.DocId ?? Str(process, "DocId")),
                ("Claim number", record.ClaimNumber ?? Str(process, "ECCOTrackingClaimNumber")),
                ("Policy number", MaskPolicy(Str(process, "ECCOTrackingPolicyNumber"), maskPolicyNumber)),
                ("Customer id", record.CustomerId ?? Str(process, "ECCOTrackingCustomerId")),
                ("Requesting system", record.RequestingDomainName ?? Str(process, "RequestingSystemName")),
                ("Delivery method", record.DeliveryMethod ?? Str(process, "DeliveryMethod")),
                ("Document", documentName),
                ("Created", record.RequestCreationTS?.ToString("MM/dd/yyyy HH:mm:ss", CultureInfo.InvariantCulture)),
                ("Status when generated", record.LatestStatus)
            });

            if (mail.HasValue)
            {
                var m = mail.Value;
                var name = Str(m, "RecipientFullName");
                if (string.IsNullOrWhiteSpace(name)) name = string.Join(" ", new[] { Str(m, "RecipientFirstName"), Str(m, "RecipientLastName") }.Where(s => !string.IsNullOrWhiteSpace(s)));
                var addressLines = new List<string>();
                if (address.HasValue)
                {
                    var a = address.Value;
                    addressLines.AddRange(new[] { Str(a, "RecipientAddressLine1"), Str(a, "RecipientAddressLine2"), Str(a, "RecipientAddressLine3") }
                        .Where(s => !string.IsNullOrWhiteSpace(s))!);
                    var cityLine = string.Join(" ", new[]
                    {
                        Str(a, "RecipientAddressCity") is { Length: > 0 } city ? city + "," : null,
                        Str(a, "RecipientAddressST"), Str(a, "RecipientAddressZIP")
                    }.Where(s => !string.IsNullOrWhiteSpace(s)));
                    if (cityLine.Length > 0) addressLines.Add(cityLine);
                }
                var flags = new List<string>();
                if (Bool(m, "NonUSAddressFlag") == true) flags.Add("Non-US address");
                if (Bool(m, "BadAddressFlag") == true) flags.Add("Bad address");

                pdf.Heading("Recipient");
                pdf.KeyValueTable(new (string, string?)[]
                {
                    ("Name", name),
                    ("Address", addressLines.Count > 0 ? string.Join("\n", addressLines) : null),
                    ("Country", address.HasValue ? Str(address.Value, "RecipientCountry") : null),
                    ("Email", Str(m, "RecipientEmailAddress")),
                    ("Phone", Str(m, "RecipientPhoneNumber")),
                    ("Address flags", flags.Count > 0 ? string.Join(", ", flags) : "None")
                });
            }

            if (journey.HasValue && journey.Value.ValueKind == JsonValueKind.Object)
            {
                var j = journey.Value;
                var body = Prop(j, "MessageBody");
                var data = body.HasValue ? Prop(body.Value, "Data") : null;
                pdf.Heading("Journey delivery");
                pdf.KeyValueTable(new (string, string?)[]
                {
                    ("Account id", Str(j, "AccountId")),
                    ("Contact key", body.HasValue ? Str(body.Value, "ContactKey") : null),
                    ("Event definition", body.HasValue ? Str(body.Value, "EventDefinitionKey") : null),
                    ("Email", data.HasValue ? Str(data.Value, "EmailAddress") : null),
                    ("First name", data.HasValue ? Str(data.Value, "FirstName") : null),
                    ("Source system", data.HasValue ? Str(data.Value, "SourceSystem") : null),
                    ("Attachment", data.HasValue ? Str(data.Value, "AttachmentName") : null)
                });
            }

            if (composition.HasValue && composition.Value.ValueKind == JsonValueKind.Object)
            {
                pdf.Heading("Composition");
                pdf.KeyValueTable(new (string, string?)[]
                {
                    ("Solutions id", Str(composition.Value, "SolutionsId")),
                    ("Version code", Str(composition.Value, "VersionCode")),
                    ("Composition schema", Str(composition.Value, "CompositionSchemaId"))
                });
            }

            var fields = new List<(string, string?)>();
            Flatten(root, string.Empty, fields, maskPolicyNumber);
            pdf.Heading("All payload fields");
            pdf.Paragraph("Every field of the stored request payload, as submitted.", 9, PdfFont.Regular, (0.42, 0.45, 0.52));
            pdf.KeyValueTable(fields, labelWidth: 210, size: 8.5);

            return pdf.ToBytes(generatedUtc);
        }

        /// <summary>"****1234" — keeps the last 4 characters.</summary>
        public static string? MaskPolicy(string? value, bool mask)
        {
            if (!mask || string.IsNullOrWhiteSpace(value)) return value;
            var v = value.Trim();
            return v.Length <= 4 ? new string('*', v.Length) : new string('*', v.Length - 4) + v[^4..];
        }

        private static void Flatten(JsonElement element, string path, List<(string, string?)> rows, bool maskPolicy)
        {
            switch (element.ValueKind)
            {
                case JsonValueKind.Object:
                    foreach (var p in element.EnumerateObject())
                    {
                        if (p.Name.Equals("ResponseHeaders", StringComparison.OrdinalIgnoreCase)) continue; // transport noise
                        Flatten(p.Value, path.Length == 0 ? p.Name : path + "." + p.Name, rows, maskPolicy);
                    }
                    break;
                case JsonValueKind.Array:
                    var i = 0;
                    foreach (var item in element.EnumerateArray()) Flatten(item, $"{path}[{i++}]", rows, maskPolicy);
                    if (i == 0) rows.Add((path, "(empty)"));
                    break;
                default:
                    var value = element.ValueKind switch
                    {
                        JsonValueKind.String => element.GetString(),
                        JsonValueKind.Null => null,
                        _ => element.GetRawText()
                    };
                    if (path.Contains("PolicyNumber", StringComparison.OrdinalIgnoreCase)) value = MaskPolicy(value, maskPolicy);
                    if (value != null && value.Length > MaxValueLength) value = value[..MaxValueLength] + "… (truncated)";
                    rows.Add((path, value));
                    break;
            }
        }

        private static JsonElement? Prop(JsonElement obj, string name)
        {
            if (obj.ValueKind != JsonValueKind.Object) return null;
            foreach (var p in obj.EnumerateObject())
                if (p.Name.Equals(name, StringComparison.OrdinalIgnoreCase)) return p.Value;
            return null;
        }

        private static string? Str(JsonElement obj, string name)
        {
            var v = Prop(obj, name);
            if (!v.HasValue) return null;
            return v.Value.ValueKind switch
            {
                JsonValueKind.String => v.Value.GetString(),
                JsonValueKind.Null or JsonValueKind.Undefined => null,
                _ => v.Value.GetRawText()
            };
        }

        private static bool? Bool(JsonElement obj, string name)
        {
            var v = Prop(obj, name);
            return v?.ValueKind switch { JsonValueKind.True => true, JsonValueKind.False => false, _ => null };
        }
    }
}
