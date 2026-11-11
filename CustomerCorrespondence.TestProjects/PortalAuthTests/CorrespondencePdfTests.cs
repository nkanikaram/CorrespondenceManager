using System.Text;
using ProcessRequestEndPoint.Pdf;
using UglyToad.PdfPig;

namespace PortalAuthTests;

[TestClass]
public class CorrespondencePdfTests
{
    private const string Payload = """
    {
      "DocId": "6100000826700940721",
      "RequestingSystemName": "Claims System",
      "ECCOTrackingPolicyNumber": "PL-99887766",
      "ECCOTrackingClaimNumber": "CL-10472",
      "DeliveryMethod": "Mail",
      "DocumentDescription": "Welcome Kit",
      "MailDeliveryInfo": {
        "RecipientFullName": "José Müller (Jr.)",
        "RecipientAddress": { "RecipientAddressLine1": "123 Main St", "RecipientAddressLine2": "Apt 4", "RecipientAddressCity": "Springfield",
                              "RecipientAddressST": "IL", "RecipientAddressZIP": "62701", "RecipientCountry": "US" },
        "NonUSAddressFlag": false, "BadAddressFlag": true
      },
      "CompositionInfo": { "SolutionsId": "SOL-1", "VersionCode": "v2", "CompositionSchemaId": "schema-7" },
      "AdditionalData": { "Notes": "Line with (parentheses) and a backslash \\ in it" }
    }
    """;

    private static readonly CorrespondencePdfRecord Record = new()
    {
        CorrespondenceId = Guid.Parse("30c5a3be-2315-49fe-bd2d-da1000000001"),
        DocId = "6100000826700940721",
        ClaimNumber = "CL-10472",
        RequestingDomainName = "Claims System",
        DeliveryMethod = "Mail",
        RequestCreationTS = new DateTime(2026, 11, 3, 16, 23, 31),
        LatestStatus = "Failed"
    };

    private static string AllText(byte[] pdf)
    {
        using var doc = PdfDocument.Open(pdf);
        return string.Join("\n", doc.GetPages().Select(p => string.Join(" ", p.GetWords().Select(w => w.Text))));
    }

    [TestMethod]
    public void Renders_AValidPdf_WithTheRequestAndRecipient()
    {
        var bytes = CorrespondencePdfRenderer.Render(Record, Payload, maskPolicyNumber: true, new DateTime(2026, 11, 3, 18, 0, 0, DateTimeKind.Utc));
        StringAssert.StartsWith(Encoding.ASCII.GetString(bytes, 0, 8), "%PDF-1.4");

        using var doc = PdfDocument.Open(bytes);
        Assert.AreEqual("Correspondence 6100000826700940721", doc.Information.Title);
        var text = AllText(bytes);
        foreach (var expected in new[] { "Correspondence Request", "Welcome Kit", "CL-10472", "Claims System", "José Müller (Jr.)",
                                         "Springfield, IL 62701", "Bad address", "SOL-1", "Status when generated", "Failed",
                                         "Page 1 of", "backslash \\ in it" })
            StringAssert.Contains(text, expected);
    }

    [TestMethod]
    public void PolicyNumbers_AreMaskedEverywhere_UnlessMaskingIsOff()
    {
        var masked = AllText(CorrespondencePdfRenderer.Render(Record, Payload, true, DateTime.UtcNow));
        Assert.IsFalse(masked.Contains("PL-99887766"), "full policy number must not appear");
        StringAssert.Contains(masked, "*******7766");

        var plain = AllText(CorrespondencePdfRenderer.Render(Record, Payload, false, DateTime.UtcNow));
        StringAssert.Contains(plain, "PL-99887766");
        Assert.AreEqual("***", CorrespondencePdfRenderer.MaskPolicy("123", true));
    }

    [TestMethod]
    public void WrappedPayloadShape_AndLongContent_FlowOntoMorePages()
    {
        var notes = string.Join(" ", Enumerable.Range(1, 400).Select(i => "word" + i));
        var json = "{\"Process\":" + Payload.Replace("\"Line with (parentheses) and a backslash \\\\ in it\"", "\"" + notes + "\"") + ",\"Route\":[\"a\",\"b\"]}";
        var bytes = CorrespondencePdfRenderer.Render(Record, json, true, DateTime.UtcNow);
        using var doc = PdfDocument.Open(bytes);
        Assert.IsTrue(doc.NumberOfPages >= 2, "long payloads continue on further pages");
        var text = AllText(bytes);
        StringAssert.Contains(text, "Welcome Kit", "the { Process } wrapper is unwrapped for the summary");
        StringAssert.Contains(text, $"Page {doc.NumberOfPages} of {doc.NumberOfPages}");
        StringAssert.Contains(text, "Route[1]");
    }

    [TestMethod]
    public void Writer_WrapsByWidth_AndBreaksUnbreakableWords()
    {
        var lines = SimplePdfDocument.Wrap("aaaa bbbb cccc", PdfFont.Regular, 10, SimplePdfDocument.Measure("aaaa bbbb", PdfFont.Regular, 10));
        CollectionAssert.AreEqual(new[] { "aaaa bbbb", "cccc" }, lines);
        var id = new string('x', 300);
        var broken = SimplePdfDocument.Wrap(id, PdfFont.Regular, 10, 100);
        Assert.IsTrue(broken.Count > 1 && broken.All(l => SimplePdfDocument.Measure(l, PdfFont.Regular, 10) <= 100));
        Assert.AreEqual(id, string.Concat(broken));
    }

    [TestMethod]
    public void InvalidJson_Throws()
    {
        // CorrespondencePdfService catches JsonException (and its JsonReaderException subtype) as "not valid JSON".
        try
        {
            CorrespondencePdfRenderer.Render(Record, "-----BEGIN PGP MESSAGE-----", true, DateTime.UtcNow);
            Assert.Fail("expected a JSON error");
        }
        catch (System.Text.Json.JsonException)
        {
        }
    }
}
