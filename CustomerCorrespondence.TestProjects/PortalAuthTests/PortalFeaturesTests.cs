using System.IdentityModel.Tokens.Jwt;
using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Security.Claims;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http.Connections;
using Microsoft.AspNetCore.SignalR.Client;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Data.SqlClient;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.Protocols;
using Microsoft.IdentityModel.Protocols.OpenIdConnect;
using Microsoft.IdentityModel.Tokens;
using ProcessRequestEndPoint.Alerts;
using ProcessRequestEndPoint.Auth;
using ProcessRequestEndPoint.Controllers;
using ProcessRequestEndPoint.Letters;
using ProcessRequestEndPoint.Pdf;
using ProcessRequestEndPoint.Realtime;
using UglyToad.PdfPig;

namespace PortalAuthTests;

[TestClass]
public class PortalFeatureUnitTests
{
    [TestMethod]
    public void FailedRateRule_BaselineAbsoluteAndMinimumSample()
    {
        var rule = new AlertOptions.FailedRateRule { MinSample = 20, Multiplier = 1.5, MinIncrease = 0.10, AbsoluteThreshold = 0.25 };
        Assert.IsNull(AlertRules.FailedRate(10, 19, 0.05, rule), "too few requests");
        Assert.IsNull(AlertRules.FailedRate(2, 20, 0.08, rule), "10% vs 8% baseline: not unusual");
        Assert.AreEqual("Warning", AlertRules.FailedRate(4, 20, 0.08, rule)!.Severity, "20% vs 8%: 2.5x and +12 points");
        Assert.IsNotNull(AlertRules.FailedRate(5, 20, null, rule), "25% hits the absolute threshold without a baseline");
        Assert.AreEqual("Critical", AlertRules.FailedRate(10, 20, 0.1, rule)!.Severity);
        Assert.IsNull(AlertRules.Backlog(24, new AlertOptions.BacklogRule { Threshold = 25 }));
        Assert.AreEqual("Critical", AlertRules.Backlog(100, new AlertOptions.BacklogRule { Threshold = 25 })!.Severity);
        Assert.IsNotNull(AlertRules.PdfFailures(1, new AlertOptions.PdfFailureRule { Threshold = 1 }));

        var sla = new AlertOptions.SlaBreachRule { Threshold = 1, CriticalThreshold = 25 };
        Assert.IsNull(AlertRules.SlaBreaches(0, 900, 40, TimeSpan.FromMinutes(10), sla), "an old backlog alone doesn't alert");
        var one = AlertRules.SlaBreaches(1, 900, 0, TimeSpan.FromMinutes(10), sla)!;
        Assert.AreEqual("Warning", one.Severity);
        Assert.AreEqual("1 request breached the SLA", one.Title);
        StringAssert.Contains(one.Message, "passed its SLA in the last 10 minutes. 900 open requests are past the SLA in total.");
        Assert.AreEqual("Critical", AlertRules.SlaBreaches(30, 900, 5, TimeSpan.FromHours(2), sla)!.Severity);

        var slaAlert = new PortalAlert { RuleKey = "SlaBreaches" };
        Assert.AreEqual("https://portal.example/#!/?sla=breached", AlertNotifier.LinkFor(slaAlert, "https://portal.example/"));
        Assert.AreEqual("https://portal.example/", AlertNotifier.LinkFor(new PortalAlert { RuleKey = "FailedRate" }, "https://portal.example/"));
        Assert.IsNull(AlertNotifier.LinkFor(slaAlert, null));
    }

    private const string Payload = """
    {"Process":{"DocId":"D-9","RequestingSystemName":"Claims System","ECCOTrackingClaimNumber":"CL-777","ECCOTrackingPolicyNumber":"PL-11112222",
     "DocumentDescription":"Welcome Kit","MailDeliveryInfo":{"RecipientFullName":"Ana Lopez",
     "RecipientAddress":{"RecipientAddressLine1":"9 Elm St","RecipientAddressCity":"Springfield","RecipientAddressST":"IL","RecipientAddressZIP":"62701"}}}}
    """;

    [TestMethod]
    public void Letter_FillsPlaceholders_MarksMissingOnes_AndRendersALetter()
    {
        var brand = new LetterBrandingOptions { CompanyName = "Acme Mutual", Phone = "555-0100" };
        var template = new LetterTemplate
        {
            DocumentType = "Welcome Kit", Name = "t", Subject = "Welcome, {{RecipientFirstName}}", Body = "Your claim {{ClaimNumber}} (policy {{PolicyNumber}}).\n\nCity: {{payload.MailDeliveryInfo.RecipientAddress.RecipientAddressCity}}. {{CustomerId}}",
            SignatureName = "Claims Team", SignatureTitle = "{{CompanyName}}"
        };
        var model = LetterRenderer.BuildModel(null, Payload, brand, maskPolicy: true, new DateTime(2026, 11, 3));
        var missing = new SortedSet<string>();
        var bytes = LetterRenderer.Render(template, model, brand, preview: true, DateTime.UtcNow, missing);
        CollectionAssert.AreEqual(new[] { "CustomerId" }, missing.ToArray());

        using var pdf = PdfDocument.Open(bytes);
        var text = string.Join(" ", pdf.GetPage(1).GetWords().Select(w => w.Text));
        foreach (var expected in new[] { "Acme Mutual", "November 3, 2026", "Ana Lopez", "9 Elm St", "Springfield, IL 62701", "Welcome, Ana",
                                         "CL-777", "****2222", "City: Springfield.", "[CustomerId]", "Claims Team", "PREVIEW" })
            StringAssert.Contains(text, expected);
        Assert.IsFalse(text.Contains("PL-11112222"));

        CollectionAssert.AreEqual(new[] { "Nmae" }, LetterRenderer.UnknownPlaceholders("Hi {{Nmae}} {{RecipientName}} {{payload.Anything.Goes}}"));
    }

    [TestMethod]
    public void Letter_NonPreview_LeavesMissingValuesBlank()
    {
        var template = new LetterTemplate { Subject = "S", Body = "Ref [{{CustomerId}}]", Name = "t", DocumentType = "x" };
        var model = LetterRenderer.BuildModel(null, Payload, new LetterBrandingOptions(), true, DateTime.UtcNow);
        using var pdf = PdfDocument.Open(LetterRenderer.Render(template, model, new LetterBrandingOptions(), preview: false, DateTime.UtcNow));
        var text = string.Join(" ", pdf.GetPage(1).GetWords().Select(w => w.Text));
        StringAssert.Contains(text, "Ref []");
        Assert.IsFalse(text.Contains("PREVIEW"));
    }
}

/// <summary>
/// SSO exchange, record details, recycle bin, letters, alerts and live updates against an in-memory host and the
/// throwaway database (ECCO_AUTH_TEST_CONNECTION with Build/DBScripts Auth 001-004, Pdf 001, Portal 001-003 applied).
/// </summary>
[TestClass]
public class PortalFeaturesHttpTests
{
    private const string Tenant = "11111111-2222-3333-4444-555555555555";
    private const string ClientId = "aaaaaaaa-bbbb-cccc-dddd-eeeeeeeeeeee";
    private const string Group = "0f7c4c39-1111-4c4e-8f39-000000000001";
    private static WebApplication? _app;
    private static HttpClient? _client;
    private static string _cs = string.Empty;
    private static RsaSecurityKey _key = null!;
    private static readonly CapturingHandler TeamsWebhook = new();

    private sealed class CapturingHandler : HttpMessageHandler
    {
        public List<string> Bodies { get; } = new();
        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
        {
            Bodies.Add(await request.Content!.ReadAsStringAsync(ct));
            return new HttpResponseMessage(HttpStatusCode.Accepted);
        }
    }

    [ClassInitialize]
    public static async Task Start(TestContext _)
    {
        var cs = Environment.GetEnvironmentVariable("ECCO_AUTH_TEST_CONNECTION");
        if (string.IsNullOrWhiteSpace(cs)) return;
        _cs = cs;
        await Exec(@"
IF COL_LENGTH('dbo.CorrespondenceRequest', 'VersionCode') IS NULL ALTER TABLE dbo.CorrespondenceRequest ADD VersionCode VARCHAR(50) NULL;
IF COL_LENGTH('dbo.CorrespondenceRequest', 'ArchiveDocumentTypeCode') IS NULL ALTER TABLE dbo.CorrespondenceRequest ADD ArchiveDocumentTypeCode VARCHAR(50) NULL;
IF COL_LENGTH('dbo.CorrespondenceRequest', 'ArchiveDocumentSubTypeCode') IS NULL ALTER TABLE dbo.CorrespondenceRequest ADD ArchiveDocumentSubTypeCode VARCHAR(50) NULL;
IF COL_LENGTH('dbo.CorrespondenceRequest', 'VFResponseId') IS NULL ALTER TABLE dbo.CorrespondenceRequest ADD VFResponseId NVARCHAR(200) NULL;
IF COL_LENGTH('dbo.CorrespondenceRequestStatusHistory', 'StatusUpdatedBy') IS NULL ALTER TABLE dbo.CorrespondenceRequestStatusHistory ADD StatusUpdatedBy VARCHAR(100) NULL;
IF COL_LENGTH('dbo.CorrespondenceRequestStatusHistory', 'SFMCEventInstanceId') IS NULL ALTER TABLE dbo.CorrespondenceRequestStatusHistory ADD SFMCEventInstanceId NVARCHAR(200) NULL;
IF NOT EXISTS (SELECT 1 FROM dbo.CorrespondenceStatusCode WHERE StatusCode = '01') INSERT dbo.CorrespondenceStatusCode VALUES ('01', 'New');
IF NOT EXISTS (SELECT 1 FROM dbo.CorrespondenceStatusCode WHERE StatusCode = '08') INSERT dbo.CorrespondenceStatusCode VALUES ('08', 'Failed');
DELETE FROM auth.ExternalGroupMapping;");

        _key = new RsaSecurityKey(RSA.Create(2048)) { KeyId = "test-key" };
        var metadata = new OpenIdConnectConfiguration();
        metadata.SigningKeys.Add(_key);

        var builder = WebApplication.CreateBuilder(new WebApplicationOptions { EnvironmentName = "Testing" });
        builder.WebHost.UseTestServer();
        builder.Configuration.AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["ConnectionStrings:AZURE_SQL_CONNECTION"] = cs,
            ["PortalAuth:SigningKey"] = Convert.ToBase64String(RandomNumberGenerator.GetBytes(32)),
            ["PortalAuth:AccessProfileCacheSeconds"] = "1",
            ["PortalAuth:Entra:Enabled"] = "true",
            ["PortalAuth:Entra:TenantId"] = Tenant,
            ["PortalAuth:Entra:ClientId"] = ClientId,
            ["PortalAuth:LocalLogin:AllowedUserNames:0"] = "admin",
            ["PortalAuth:LocalLogin:AllowedUserNames:1"] = "operator",
            ["PortalAuth:LocalLogin:AllowedUserNames:2"] = "viewer",
            ["Retention:Enabled"] = "false",
            ["Alerts:Enabled"] = "false",
            ["Alerts:Teams:WebhookUrl"] = "https://teams.example.test/workflows/abc",
            ["Alerts:FailedRate:WindowHours"] = "1",
            ["Alerts:FailedRate:MinSample"] = "4",
            ["Alerts:FailedRate:AbsoluteThreshold"] = "0.5",
            ["Alerts:ProcessingBacklog:Enabled"] = "false",
            ["Alerts:PdfFailures:Enabled"] = "false",
            ["Alerts:SlaBreaches:Enabled"] = "false",
            ["Letters:CompanyName"] = "Test Mutual"
        });
        builder.Services.AddControllers().AddApplicationPart(typeof(AuthController).Assembly);
        builder.Services.AddAuthorization();
        builder.AddPortalAuth();
        builder.AddCorrespondencePdfs();
        builder.AddPortalFeatures();
        builder.Services.AddSingleton(sp => new EntraTokenValidator(sp.GetRequiredService<IOptions<PortalAuthOptions>>(),
            new StaticConfigurationManager<OpenIdConnectConfiguration>(metadata)));
        builder.Services.AddHttpClient("alerts").ConfigurePrimaryHttpMessageHandler(() => TeamsWebhook);
        builder.Services.AddScoped<CustomerCorrespondence.Application.Interfaces.IEncryptionService, PlainEncryption>();

        _app = builder.Build();
        _app.UseAuthentication();
        _app.UseAuthorization();
        _app.MapControllers();
        _app.MapPortalFeatures();
        await _app.StartAsync();
        _client = _app.GetTestClient();
    }

    private sealed class PlainEncryption : CustomerCorrespondence.Application.Interfaces.IEncryptionService
    {
        public Task<string> EncryptAsync(string plainText) => Task.FromResult(plainText);
        public Task<string> DecryptAsync(string encryptedText) => Task.FromResult(encryptedText);
        public byte[] GenerateSearchHash(string plainText) => SHA256.HashData(Encoding.UTF8.GetBytes(plainText));
    }

    [ClassCleanup]
    public static async Task Stop()
    {
        if (_app != null) await _app.DisposeAsync();
    }

    private static HttpClient Client => _client ?? throw new AssertInconclusiveException("ECCO_AUTH_TEST_CONNECTION is not set.");

    private static async Task Exec(string sql)
    {
        await using var c = new SqlConnection(_cs);
        await c.OpenAsync();
        await using var cmd = new SqlCommand(sql, c);
        await cmd.ExecuteNonQueryAsync();
    }

    private static async Task<(HttpStatusCode Status, JsonElement Body, HttpResponseMessage Raw)> Send(HttpMethod method, string url, string? token = null, object? body = null)
    {
        var req = new HttpRequestMessage(method, url);
        if (token != null) req.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        if (body != null) req.Content = JsonContent.Create(body);
        var res = await Client.SendAsync(req);
        var bytes = await res.Content.ReadAsByteArrayAsync();
        JsonElement json = default;
        if (res.Content.Headers.ContentType?.MediaType == "application/json" && bytes.Length > 0)
            json = JsonDocument.Parse(bytes).RootElement.Clone();
        return (res.StatusCode, json, res);
    }

    private static async Task<string> Login(string user)
    {
        var (status, body, _) = await Send(HttpMethod.Post, "/api/auth/login", body: new { userName = user, password = "EccoDev!2026" });
        Assert.AreEqual(HttpStatusCode.OK, status, body.ToString());
        return body.GetProperty("accessToken").GetString()!;
    }

    private static string EntraToken(string oid, string[] groups, string[]? amr = null, string tenant = Tenant, string audience = ClientId,
        DateTime? issuedAt = null, string[]? roles = null)
    {
        var claims = new List<Claim> { new("oid", oid), new("tid", tenant), new("preferred_username", $"user-{oid[..8]}@contoso.test"), new("name", "Entra User " + oid[..4]) };
        claims.AddRange((amr ?? new[] { "pwd", "mfa" }).Select(a => new Claim("amr", a)));
        claims.AddRange(groups.Select(g => new Claim("groups", g)));
        claims.AddRange((roles ?? Array.Empty<string>()).Select(r => new Claim("roles", r)));
        var now = issuedAt ?? DateTime.UtcNow;
        return new JwtSecurityTokenHandler().CreateEncodedJwt(new SecurityTokenDescriptor
        {
            Issuer = $"https://login.microsoftonline.com/{Tenant}/v2.0",
            Audience = audience,
            Subject = new ClaimsIdentity(claims),
            IssuedAt = now,
            NotBefore = now,
            Expires = now.AddHours(1),
            SigningCredentials = new SigningCredentials(_key, SecurityAlgorithms.RsaSha256)
        });
    }

    // ================= SSO =================

    [TestMethod]
    public async Task Sso_ProvisionsSyncsGroupsAndEnforcesMfaTenantAndAge()
    {
        var admin = await Login("admin");
        var config = (await Send(HttpMethod.Get, "/api/auth/config")).Body;
        Assert.IsTrue(config.GetProperty("entra").GetProperty("enabled").GetBoolean());
        Assert.AreEqual(ClientId, config.GetProperty("entra").GetProperty("clientId").GetString());

        Assert.AreEqual(HttpStatusCode.OK, (await Send(HttpMethod.Post, "/api/admin/group-mappings", admin,
            new { claimType = "group", claimValue = Group, displayName = "ECCO Claims Team", roleName = "Operator" })).Status);
        Assert.AreEqual(HttpStatusCode.OK, (await Send(HttpMethod.Post, "/api/admin/group-mappings", admin,
            new { claimType = "group", claimValue = Group, attributeKey = "scope.customer", attributeValue = "Claims System" })).Status);
        Assert.AreEqual(HttpStatusCode.BadRequest, (await Send(HttpMethod.Post, "/api/admin/group-mappings", admin,
            new { claimType = "group", claimValue = "not-a-guid", roleName = "Operator" })).Status);

        var oid = Guid.NewGuid().ToString();
        var (status, body, _) = await Send(HttpMethod.Post, "/api/auth/entra", body: new { idToken = EntraToken(oid, new[] { Group }) });
        Assert.AreEqual(HttpStatusCode.OK, status, body.ToString());
        var user = body.GetProperty("user");
        Assert.AreEqual("Entra", user.GetProperty("signInMethod").GetString());
        CollectionAssert.AreEqual(new[] { "Operator" }, user.GetProperty("roles").EnumerateArray().Select(r => r.GetString()).ToArray());
        Assert.AreEqual("Claims System", user.GetProperty("attributes").GetProperty("scope.customer")[0].GetString());
        var firstSession = body.GetProperty("accessToken").GetString();

        // Removed from the group: the next sign-in drops the role, and older sessions end.
        (status, body, _) = await Send(HttpMethod.Post, "/api/auth/entra", body: new { idToken = EntraToken(oid, Array.Empty<string>()) });
        Assert.AreEqual(HttpStatusCode.OK, status);
        Assert.AreEqual(0, body.GetProperty("user").GetProperty("roles").GetArrayLength());
        Assert.AreEqual(HttpStatusCode.Unauthorized, (await Send(HttpMethod.Get, "/api/auth/me", firstSession)).Status);
        var sso = body.GetProperty("accessToken").GetString();

        // Passwords are managed by Microsoft for SSO accounts.
        Assert.AreEqual(HttpStatusCode.BadRequest, (await Send(HttpMethod.Post, "/api/auth/change-password", sso, new { currentPassword = "x", newPassword = "Brand-New-Secret-8" })).Status);
        var listed = (await Send(HttpMethod.Get, "/api/admin/users", admin)).Body.EnumerateArray().First(u => u.GetProperty("externalProvider").ValueKind == JsonValueKind.String
                                                                                                       && u.GetProperty("userName").GetString()!.StartsWith("user-" + oid[..8]));
        Assert.AreEqual(HttpStatusCode.BadRequest, (await Send(HttpMethod.Post, $"/api/admin/users/{listed.GetProperty("userId").GetString()}/reset-password", admin, new { newPassword = "Temp-Password-77" })).Status);

        foreach (var (token, why) in new[]
                 {
                     (EntraToken(oid, new[] { Group }, amr: new[] { "pwd" }), "no MFA"),
                     (EntraToken(oid, new[] { Group }, tenant: Guid.NewGuid().ToString()), "other tenant"),
                     (EntraToken(oid, new[] { Group }, audience: Guid.NewGuid().ToString()), "other app"),
                     (EntraToken(oid, new[] { Group }, issuedAt: DateTime.UtcNow.AddMinutes(-20)), "too old"),
                     (EntraToken(oid, new[] { Group })[..^4] + "AAAA", "tampered")
                 })
            Assert.AreEqual(HttpStatusCode.Unauthorized, (await Send(HttpMethod.Post, "/api/auth/entra", body: new { idToken = token })).Status, why);

        // Local passwords only for the allowed break-glass list.
        Assert.AreEqual(HttpStatusCode.Forbidden, (await Send(HttpMethod.Post, "/api/auth/login", body: new { userName = "analyst", password = "EccoDev!2026" })).Status);
    }

    // ================= Details + recycle bin =================

    private static async Task<Guid> InsertRecord(string customer = "Claims System", string status = "01", int minutesAgo = 5)
    {
        var id = Guid.NewGuid();
        var payload = Encoding.UTF8.GetBytes("""{"Process":{"DocumentDescription":"Welcome Kit","MailDeliveryInfo":{"RecipientFullName":"Ana Lopez"}}}""");
        await using var c = new SqlConnection(_cs);
        await c.OpenAsync();
        await using var cmd = new SqlCommand(@"
INSERT dbo.CorrespondenceRequest (CorrespondenceId, RequestingDomainName, DeliveryMethodCode, DocId, ClaimNumber, RequestCreationTS, RequestPayload)
VALUES (@id, @cust, 'Mail', 'D-T', 'CL-T', DATEADD(MINUTE, -@ago, SYSUTCDATETIME()), @p);
INSERT dbo.CorrespondenceRequestStatusHistory (CorrespondenceId, StatusCode, HistorySequenceNum, StatusUpdateTS, StatusUpdatedBy) VALUES (@id, '01', 1, DATEADD(MINUTE, -@ago, SYSUTCDATETIME()), 'pipeline');
IF @status <> '01' INSERT dbo.CorrespondenceRequestStatusHistory (CorrespondenceId, StatusCode, HistorySequenceNum, StatusUpdateTS, StatusUpdatedBy) VALUES (@id, @status, 2, SYSUTCDATETIME(), 'pipeline');", c);
        cmd.Parameters.AddWithValue("@id", id);
        cmd.Parameters.AddWithValue("@cust", customer);
        cmd.Parameters.AddWithValue("@ago", minutesAgo);
        cmd.Parameters.AddWithValue("@status", status);
        cmd.Parameters.Add("@p", System.Data.SqlDbType.VarBinary, -1).Value = payload;
        await cmd.ExecuteNonQueryAsync();
        return id;
    }

    [TestMethod]
    public async Task Details_Timeline_Payload_AndRecycleRestorePurge()
    {
        var admin = await Login("admin");
        var viewer = await Login("viewer"); // scoped to Annuity Correspondence
        var id = await InsertRecord(status: "08");

        var (status, details, _) = await Send(HttpMethod.Get, $"/api/portal/correspondence/{id}/details", admin);
        Assert.AreEqual(HttpStatusCode.OK, status, details.ToString());
        var history = details.GetProperty("statusHistory").EnumerateArray().Select(h => h.GetProperty("status").GetString()).ToArray();
        CollectionAssert.AreEqual(new[] { "New", "Failed" }, history);
        Assert.AreEqual("Failed", details.GetProperty("record").GetProperty("latestStatus").GetString());
        StringAssert.Contains(details.GetProperty("payload").GetString(), "Ana Lopez");
        Assert.AreEqual(HttpStatusCode.NotFound, (await Send(HttpMethod.Get, $"/api/portal/correspondence/{id}/details", viewer)).Status, "outside the viewer's scope");

        var (delStatus, del, _) = await Send(HttpMethod.Post, "/api/portal/correspondence/delete", admin, new { correspondenceIds = new[] { id } });
        Assert.AreEqual(HttpStatusCode.OK, delStatus, del.ToString());
        Assert.AreEqual(30, del.GetProperty("recycleBinDays").GetInt32());
        Assert.AreEqual(HttpStatusCode.NotFound, (await Send(HttpMethod.Get, $"/api/portal/correspondence/{id}/details", admin)).Status);
        var bin = (await Send(HttpMethod.Get, "/api/portal/recycle-bin?search=D-T", admin)).Body;
        var entry = bin.GetProperty("items").EnumerateArray().First(e => e.GetProperty("correspondenceId").GetString() == id.ToString());
        Assert.AreEqual("Failed", entry.GetProperty("latestStatus").GetString());
        Assert.AreEqual(HttpStatusCode.Forbidden, (await Send(HttpMethod.Get, "/api/portal/recycle-bin", await Login("operator"))).Status, "recycle permission");

        Assert.AreEqual(HttpStatusCode.OK, (await Send(HttpMethod.Post, $"/api/portal/recycle-bin/{id}/restore", admin)).Status);
        (status, details, _) = await Send(HttpMethod.Get, $"/api/portal/correspondence/{id}/details", admin);
        Assert.AreEqual(HttpStatusCode.OK, status);
        Assert.AreEqual(2, details.GetProperty("statusHistory").GetArrayLength(), "history restored");
        var events = details.GetProperty("activity").EnumerateArray().Select(a => a.GetProperty("eventType").GetString()).ToList();
        CollectionAssert.Contains(events, "CorrespondenceDeleted");
        CollectionAssert.Contains(events, "CorrespondenceRestored");
        Assert.AreEqual(HttpStatusCode.NotFound, (await Send(HttpMethod.Post, $"/api/portal/recycle-bin/{Guid.NewGuid()}/restore", admin)).Status, "unknown ids");

        await Send(HttpMethod.Post, "/api/portal/correspondence/delete", admin, new { correspondenceIds = new[] { id } });
        Assert.AreEqual(HttpStatusCode.NoContent, (await Send(HttpMethod.Delete, $"/api/portal/recycle-bin/{id}", admin)).Status);
        Assert.AreEqual(HttpStatusCode.NotFound, (await Send(HttpMethod.Post, $"/api/portal/recycle-bin/{id}/restore", admin)).Status, "purged for good");
    }

    /// <summary>
    /// ECCDB-D02 has foreign keys added WITH NOCHECK that its existing rows break (DeliveryMethodCode 'MAIL' vs a
    /// lookup of '01'-'04'). Restoring such rows must still work, several at a time, and leave the key as it was.
    /// </summary>
    [TestMethod]
    public async Task RecycleBin_RestoreMany_WithUntrustedForeignKey()
    {
        var admin = await Login("admin");
        var ids = new[] { await InsertRecord(), await InsertRecord(), await InsertRecord() };
        var del = (await Send(HttpMethod.Post, "/api/portal/correspondence/delete", admin, new { correspondenceIds = ids })).Body;
        Assert.AreEqual(3, del.GetProperty("deleted").GetArrayLength());

        async Task Sql(string sql)
        {
            await using var c = new SqlConnection(_cs);
            await c.OpenAsync();
            await new SqlCommand(sql, c).ExecuteNonQueryAsync();
        }
        await Sql(@"IF OBJECT_ID('dbo.TestDeliveryLookup') IS NULL CREATE TABLE dbo.TestDeliveryLookup (Code VARCHAR(50) NOT NULL PRIMARY KEY);
                    IF NOT EXISTS (SELECT 1 FROM dbo.TestDeliveryLookup) INSERT dbo.TestDeliveryLookup VALUES ('01'), ('02');
                    ALTER TABLE dbo.CorrespondenceRequest WITH NOCHECK ADD CONSTRAINT FK_Test_Delivery FOREIGN KEY (DeliveryMethodCode) REFERENCES dbo.TestDeliveryLookup (Code);");
        try
        {
            var unknown = Guid.NewGuid();
            var (status, body, _) = await Send(HttpMethod.Post, "/api/portal/recycle-bin/restore", admin, new { correspondenceIds = ids.Append(unknown) });
            Assert.AreEqual(HttpStatusCode.OK, status, body.ToString());
            CollectionAssert.AreEquivalent(ids.Select(i => i.ToString()).ToList(), body.GetProperty("restored").EnumerateArray().Select(e => e.GetString()).ToList());
            Assert.AreEqual(0, body.GetProperty("failed").GetArrayLength(), body.ToString());
            Assert.AreEqual(unknown.ToString(), body.GetProperty("notFound")[0].GetString());
            foreach (var id in ids)
                Assert.AreEqual(HttpStatusCode.OK, (await Send(HttpMethod.Get, $"/api/portal/correspondence/{id}/details", admin)).Status);

            await using var c = new SqlConnection(_cs);
            await c.OpenAsync();
            await using var r = await new SqlCommand("SELECT is_disabled, is_not_trusted FROM sys.foreign_keys WHERE name = 'FK_Test_Delivery'", c).ExecuteReaderAsync();
            Assert.IsTrue(await r.ReadAsync());
            Assert.IsFalse(r.GetBoolean(0), "the key is switched back on");
            Assert.IsTrue(r.GetBoolean(1), "and untrusted, as before");

            Assert.AreEqual(HttpStatusCode.BadRequest, (await Send(HttpMethod.Post, "/api/portal/recycle-bin/restore", admin, new { correspondenceIds = Array.Empty<Guid>() })).Status);
            Assert.AreEqual(HttpStatusCode.Forbidden, (await Send(HttpMethod.Post, "/api/portal/recycle-bin/restore", await Login("operator"), new { correspondenceIds = ids })).Status);
        }
        finally
        {
            await Sql("IF OBJECT_ID('FK_Test_Delivery') IS NOT NULL ALTER TABLE dbo.CorrespondenceRequest DROP CONSTRAINT FK_Test_Delivery; DROP TABLE IF EXISTS dbo.TestDeliveryLookup;");
        }
    }

    // ================= Letters =================

    [TestMethod]
    public async Task Letters_DraftPublishPreview_AndVersioning()
    {
        var admin = await Login("admin");
        var type = "Test Letter " + Guid.NewGuid().ToString("N")[..6];
        var (status, created, _) = await Send(HttpMethod.Post, "/api/portal/letters/templates", admin,
            new { documentType = type, name = "v1", subject = "Hello {{RecipientName}}", body = "Claim {{ClaimNumber}}. {{Nope}}" });
        Assert.AreEqual(HttpStatusCode.OK, status, created.ToString());
        var id = created.GetProperty("templateId").GetInt32();
        Assert.AreEqual("Draft", created.GetProperty("status").GetString());

        var (pubStatus, pub, _) = await Send(HttpMethod.Post, $"/api/portal/letters/templates/{id}/publish", admin);
        Assert.AreEqual(HttpStatusCode.BadRequest, pubStatus);
        StringAssert.Contains(pub.GetProperty("message").GetString(), "{{Nope}}");
        Assert.AreEqual(HttpStatusCode.OK, (await Send(HttpMethod.Put, $"/api/portal/letters/templates/{id}", admin,
            new { name = "v1", subject = "Hello {{RecipientName}}", body = "Claim {{ClaimNumber}}." })).Status);
        Assert.AreEqual(HttpStatusCode.OK, (await Send(HttpMethod.Post, $"/api/portal/letters/templates/{id}/publish", admin)).Status);
        Assert.AreEqual(HttpStatusCode.Conflict, (await Send(HttpMethod.Put, $"/api/portal/letters/templates/{id}", admin,
            new { name = "x", subject = "x", body = "x" })).Status, "published versions are immutable");

        // An operator previews the published letter for a payload before sending; a viewer may not.
        var payload = "{\"Process\":{\"DocumentDescription\":\"" + type + "\",\"MailDeliveryInfo\":{\"RecipientFullName\":\"Ana Lopez\"}}}";
        var (prevStatus, _, raw) = await Send(HttpMethod.Post, "/api/portal/letters/preview", await Login("operator"), new { payload });
        Assert.AreEqual(HttpStatusCode.OK, prevStatus);
        Assert.AreEqual("application/pdf", raw.Content.Headers.ContentType?.MediaType);
        Assert.AreEqual("ClaimNumber", raw.Headers.GetValues("X-Letter-Missing").Single());
        using (var pdf = PdfDocument.Open(await raw.Content.ReadAsByteArrayAsync()))
            StringAssert.Contains(string.Join(" ", pdf.GetPage(1).GetWords().Select(w => w.Text)), "Hello Ana Lopez");
        Assert.AreEqual(HttpStatusCode.Forbidden, (await Send(HttpMethod.Post, "/api/portal/letters/preview", await Login("viewer"), new { payload })).Status);

        // A second version replaces the first when published.
        var v2 = (await Send(HttpMethod.Post, "/api/portal/letters/templates", admin, new { documentType = type, name = "v2", subject = "Hi", body = "B" })).Body;
        Assert.AreEqual(2, v2.GetProperty("version").GetInt32());
        await Send(HttpMethod.Post, $"/api/portal/letters/templates/{v2.GetProperty("templateId").GetInt32()}/publish", admin);
        var all = (await Send(HttpMethod.Get, "/api/portal/letters/templates", admin)).Body.EnumerateArray().Where(t => t.GetProperty("documentType").GetString() == type).ToList();
        CollectionAssert.AreEquivalent(new[] { "Published", "Retired" }, all.Select(t => t.GetProperty("status").GetString()).ToArray());
        foreach (var t in all) await Send(HttpMethod.Post, $"/api/portal/letters/templates/{t.GetProperty("templateId").GetInt32()}/retire", admin);
    }

    // ================= Alerts + live updates =================

    [TestMethod]
    public async Task Alerts_RaiseNotifyCooldownAcknowledge_AndLiveUpdates()
    {
        var admin = await Login("admin");
        await Exec(@"UPDATE dbo.CorrespondenceRequest SET RequestCreationTS = DATEADD(HOUR, -3, RequestCreationTS) WHERE RequestCreationTS > DATEADD(HOUR, -2, SYSUTCDATETIME());
                     UPDATE dbo.PortalAlert SET AcknowledgedUtc = SYSUTCDATETIME() WHERE AcknowledgedUtc IS NULL;");
        for (var i = 0; i < 3; i++) await InsertRecord(status: "08");
        await InsertRecord();

        // Live: a signed-in client hears about the alert.
        var server = _app!.GetTestServer();
        var heard = new TaskCompletionSource<string>(TaskCreationOptions.RunContinuationsAsynchronously);
        var pdfHeard = new TaskCompletionSource<string>(TaskCreationOptions.RunContinuationsAsynchronously);
        await using var hub = new HubConnectionBuilder()
            .WithUrl(new Uri(server.BaseAddress, PortalHub.Path), o =>
            {
                o.HttpMessageHandlerFactory = _ => server.CreateHandler();
                o.AccessTokenProvider = () => Task.FromResult<string?>(admin);
                o.Transports = HttpTransportType.LongPolling;
            }).Build();
        hub.On<JsonElement>("alertRaised", a => heard.TrySetResult(a.GetProperty("title").GetString()!));
        hub.On<JsonElement>("pdfStatusChanged", p => pdfHeard.TrySetResult(p.GetProperty("status").GetString()!));
        await hub.StartAsync();

        var worker = ActivatorUtilities.CreateInstance<AlertWorker>(_app.Services);
        var raised = await worker.RunOnceAsync(CancellationToken.None);
        Assert.AreEqual(1, raised.Count);
        Assert.AreEqual("FailedRate", raised[0].RuleKey);
        Assert.AreEqual("Failed rate is 75%", await heard.Task.WaitAsync(TimeSpan.FromSeconds(10)));
        StringAssert.Contains(TeamsWebhook.Bodies.Last(), "application/vnd.microsoft.card.adaptive");
        StringAssert.Contains(TeamsWebhook.Bodies.Last(), "Failed rate is 75%");

        Assert.AreEqual(0, (await worker.RunOnceAsync(CancellationToken.None)).Count, "cooldown while unacknowledged");
        var open = (await Send(HttpMethod.Get, "/api/portal/alerts?open=true", admin)).Body;
        var alertId = open.EnumerateArray().First().GetProperty("alertId").GetInt64();
        Assert.IsTrue(open.EnumerateArray().First().GetProperty("notifiedTeams").GetBoolean());
        Assert.AreEqual(HttpStatusCode.NoContent, (await Send(HttpMethod.Post, $"/api/portal/alerts/{alertId}/acknowledge", admin)).Status);
        Assert.AreEqual(0, (await Send(HttpMethod.Get, "/api/portal/alerts?open=true", admin)).Body.GetArrayLength());
        Assert.AreEqual(HttpStatusCode.Forbidden, (await Send(HttpMethod.Get, "/api/portal/alerts", await Login("viewer"))).Status);

        await _app.Services.GetRequiredService<PortalEvents>().PdfStatusChangedAsync(Guid.NewGuid(), "Generated");
        Assert.AreEqual("Generated", await pdfHeard.Task.WaitAsync(TimeSpan.FromSeconds(10)));

        var changes = (await Send(HttpMethod.Get, $"/api/portal/changes?since={DateTime.UtcNow.AddMinutes(-30):O}", admin)).Body;
        Assert.IsTrue(changes.GetProperty("newRecords").GetInt32() >= 4);
        Assert.IsTrue(changes.GetProperty("newAlerts").GetInt32() >= 1);

        // No token, no hub.
        await using var anonymous = new HubConnectionBuilder()
            .WithUrl(new Uri(server.BaseAddress, PortalHub.Path), o => { o.HttpMessageHandlerFactory = _ => server.CreateHandler(); o.Transports = HttpTransportType.LongPolling; })
            .Build();
        await Assert.ThrowsExceptionAsync<HttpRequestException>(() => anonymous.StartAsync());
    }

    [TestMethod]
    public async Task Sla_AdminSetsTargets_AndSearchResultsCarryTheSlaPosition()
    {
        var admin = await Login("admin");
        Assert.AreEqual(HttpStatusCode.Forbidden, (await Send(HttpMethod.Get, "/api/portal/sla", await Login("viewer"))).Status);
        var (status, original, _) = await Send(HttpMethod.Get, "/api/portal/sla", admin);
        Assert.AreEqual(HttpStatusCode.OK, status, original.ToString());

        var invalid = await Send(HttpMethod.Put, "/api/portal/sla", admin, new { businessDays = false, atRiskPercent = 0, defaultDays = 0, stopStatuses = new string[0] });
        Assert.AreEqual(HttpStatusCode.BadRequest, invalid.Status);
        Assert.AreEqual(3, invalid.Body.GetProperty("problems").GetArrayLength());

        await Exec("IF NOT EXISTS (SELECT 1 FROM dbo.CorrespondenceStatusCode WHERE StatusCode = '07') INSERT dbo.CorrespondenceStatusCode VALUES ('07', 'Delivered');");
        try
        {
            var saved = await Send(HttpMethod.Put, "/api/portal/sla", admin, new
            {
                businessDays = false, atRiskPercent = 50, defaultDays = 5, stopStatuses = new[] { "Delivered" },
                customers = new[] { new { customerName = "Claims System", targetDays = 2 } }
            });
            Assert.AreEqual(HttpStatusCode.OK, saved.Status, saved.Body.ToString());
            Assert.AreEqual("admin", saved.Body.GetProperty("updatedBy").GetString());

            var atRisk = await InsertRecord(customer: "Claims System", minutesAgo: 36 * 60);    // 75% of 2 days
            var onTrack = await InsertRecord(customer: "Annuity Correspondence", minutesAgo: 36 * 60); // 30% of 5 days
            var breached = await InsertRecord(customer: "Claims System", minutesAgo: 3 * 24 * 60);
            var missed = await InsertRecord(customer: "Claims System", status: "07", minutesAgo: 3 * 24 * 60);
            var met = await InsertRecord(customer: "Claims System", status: "07", minutesAgo: 60);

            async Task<JsonElement> SlaOf(Guid id)
            {
                var (s, body, _) = await Send(HttpMethod.Post, "/api/correspondence/search", admin, new { correspondenceId = id.ToString(), page = 1, pageSize = 5 });
                Assert.AreEqual(HttpStatusCode.OK, s, body.ToString());
                return body.GetProperty("items")[0].GetProperty("sla");
            }
            Assert.AreEqual("AtRisk", (await SlaOf(atRisk)).GetProperty("state").GetString());
            Assert.AreEqual("OnTrack", (await SlaOf(onTrack)).GetProperty("state").GetString());
            var b = await SlaOf(breached);
            Assert.AreEqual("Breached", b.GetProperty("state").GetString());
            Assert.AreEqual(2m, b.GetProperty("targetDays").GetDecimal());
            Assert.AreEqual("Missed", (await SlaOf(missed)).GetProperty("state").GetString());
            var m = await SlaOf(met);
            Assert.AreEqual("Met", m.GetProperty("state").GetString());
            Assert.IsTrue(m.GetProperty("elapsedSeconds").GetInt64() is > 3500 and < 3700, "time lapse runs from creation to delivery");

            var audit = (await Send(HttpMethod.Get, "/api/admin/audit?top=20", admin)).Body;
            Assert.IsTrue(audit.EnumerateArray().Any(a => a.GetProperty("eventType").GetString() == "SlaChanged"));

            // The grid's SLA filter runs in SQL and agrees with each row's SLA.
            async Task<bool> Matches(Guid id, string op, string group)
            {
                var filter = new { logic = "and", filters = new object[] { new { field = "id", @operator = "eq", value = id.ToString() }, new { field = "sla", @operator = op, value = group } } };
                var (s, body, _) = await Send(HttpMethod.Post, "/api/correspondence/search", admin, new { filter, page = 1, pageSize = 5 });
                Assert.AreEqual(HttpStatusCode.OK, s, body.ToString());
                return body.GetProperty("total").GetInt32() == 1;
            }
            Assert.IsTrue(await Matches(breached, "eq", "Breached"));
            Assert.IsFalse(await Matches(atRisk, "eq", "Breached"));
            Assert.IsTrue(await Matches(atRisk, "eq", "At risk"));
            Assert.IsTrue(await Matches(onTrack, "eq", "OnTrack"));
            Assert.IsFalse(await Matches(onTrack, "eq", "At risk"));
            Assert.IsTrue(await Matches(missed, "eq", "Finished"));
            Assert.IsFalse(await Matches(missed, "eq", "Breached"), "a late but finished request isn't breached any more");
            Assert.IsTrue(await Matches(met, "neq", "Breached"));
            var bad = await Send(HttpMethod.Post, "/api/correspondence/search", admin,
                new { filter = new { logic = "and", filters = new[] { new { field = "sla", @operator = "eq", value = "Late" } } } });
            Assert.AreEqual(HttpStatusCode.BadRequest, bad.Status);

            // The search box with several terms: any of them, combined (AND) with the grid filters.
            var anyOf = await Send(HttpMethod.Post, "/api/correspondence/search", admin, new
            {
                freeTerms = new[] { breached.ToString(), atRisk.ToString(), onTrack.ToString() },
                filter = new { logic = "and", filters = new[] { new { field = "customer", @operator = "eq", value = "Claims System" } } },
                page = 1, pageSize = 10
            });
            Assert.AreEqual(HttpStatusCode.OK, anyOf.Status, anyOf.Body.ToString());
            CollectionAssert.AreEquivalent(new[] { breached.ToString(), atRisk.ToString() },
                anyOf.Body.GetProperty("items").EnumerateArray().Select(i => i.GetProperty("correspondenceId").GetString()).ToArray());
            var tooManyTerms = await Send(HttpMethod.Post, "/api/correspondence/search", admin,
                new { freeTerms = Enumerable.Range(0, 101).Select(i => "D-" + i).ToArray() });
            Assert.AreEqual(HttpStatusCode.BadRequest, tooManyTerms.Status);

            // Alerts: a request that passed its SLA since the previous check raises one SlaBreaches alert.
            var fresh = await InsertRecord(customer: "Claims System", minutesAgo: 2 * 24 * 60 + 20); // breached 20 minutes ago
            await Exec(@"UPDATE dbo.PortalAlert SET AcknowledgedUtc = SYSUTCDATETIME() WHERE AcknowledgedUtc IS NULL;
                         DELETE FROM dbo.PortalAlertMetric WHERE RuleKey = 'SlaBreaches';
                         INSERT dbo.PortalAlertMetric (RuleKey, Value, SampleSize, CapturedUtc) VALUES ('SlaBreaches', 0, 0, DATEADD(MINUTE, -60, SYSUTCDATETIME()));");
            var options = Options.Create(new AlertOptions
            {
                FailedRate = { Enabled = false }, ProcessingBacklog = { Enabled = false }, PdfFailures = { Enabled = false },
                SlaBreaches = { Enabled = true, Threshold = 1 }
            });
            var worker = ActivatorUtilities.CreateInstance<AlertWorker>(_app!.Services, options);
            var raised = await worker.RunOnceAsync(CancellationToken.None);
            Assert.AreEqual(1, raised.Count);
            Assert.AreEqual("SlaBreaches", raised[0].RuleKey);
            Assert.IsTrue(raised[0].MetricValue >= 1);
            StringAssert.Contains(raised[0].Title, "breached the SLA");
            Assert.IsTrue(raised[0].BaselineValue >= 2, "the total includes the earlier breached request");
            Assert.AreEqual(0, (await worker.RunOnceAsync(CancellationToken.None)).Count, "nothing new breached since the last check");
            Assert.IsTrue(await Matches(fresh, "eq", "Breached"));
        }
        finally
        {
            await Send(HttpMethod.Put, "/api/portal/sla", admin, original);
        }
    }
    // ================= Account menu =================

    [TestMethod]
    public async Task Account_ScopePreviousSignInPreferences_AndSignOutOtherSessions()
    {
        await Login("viewer");
        await Send(HttpMethod.Post, "/api/auth/login", body: new { userName = "viewer", password = "wrong password" });
        var viewer = await Login("viewer");

        var (status, account, _) = await Send(HttpMethod.Get, "/api/account", viewer);
        Assert.AreEqual(HttpStatusCode.OK, status, account.ToString());
        var customers = account.GetProperty("scope").GetProperty("customers");
        Assert.AreEqual(JsonValueKind.Array, customers.ValueKind, "the viewer is scoped to some customers");
        Assert.AreEqual("Annuity Correspondence", customers[0].GetString());
        Assert.AreEqual(JsonValueKind.String, account.GetProperty("previousSignIn").GetProperty("utc").ValueKind);
        Assert.AreEqual("Password", account.GetProperty("previousSignIn").GetProperty("method").GetString());
        Assert.AreEqual(1, account.GetProperty("failedSignInsSincePrevious").GetInt32());
        Assert.IsFalse(string.IsNullOrEmpty(account.GetProperty("version").GetString()));
        Assert.AreEqual(JsonValueKind.Null, (await Send(HttpMethod.Get, "/api/account", await Login("admin"))).Body.GetProperty("scope").GetProperty("customers").ValueKind,
            "the admin sees every customer");

        // Preferences: validated, saved with the account, read back.
        var bad = await Send(HttpMethod.Put, "/api/account/preferences", viewer, new { theme = "purple", pageSize = 33, font = "comic", headerColor = "red", textColor = "#12" });
        Assert.AreEqual(HttpStatusCode.BadRequest, bad.Status);
        Assert.AreEqual(5, bad.Body.GetProperty("problems").GetArrayLength());
        Assert.AreEqual(HttpStatusCode.OK, (await Send(HttpMethod.Put, "/api/account/preferences", viewer,
            new { theme = "system", timeZone = "utc", pageSize = 50, density = "compact", font = "georgia", textSize = "lg", headerColor = "#0f766e", textColor = "contrast" })).Status);
        var prefs = (await Send(HttpMethod.Get, "/api/account", viewer)).Body.GetProperty("preferences");
        Assert.AreEqual("utc", prefs.GetProperty("timeZone").GetString());
        Assert.AreEqual(50, prefs.GetProperty("pageSize").GetInt32());
        Assert.AreEqual("georgia", prefs.GetProperty("font").GetString());
        Assert.AreEqual("#0f766e", prefs.GetProperty("headerColor").GetString());
        Assert.AreEqual("contrast", prefs.GetProperty("textColor").GetString());

        // Sign out other sessions: the other token stops working, this one carries on with a fresh token.
        var otherBrowser = await Login("viewer");
        // The other browser is connected for live updates: it's told at once, not at its next request.
        var server = _app!.GetTestServer();
        var ended = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        await using var hub = new HubConnectionBuilder()
            .WithUrl(new Uri(server.BaseAddress, PortalHub.Path), o =>
            {
                o.HttpMessageHandlerFactory = _ => server.CreateHandler();
                o.AccessTokenProvider = () => Task.FromResult<string?>(otherBrowser);
                o.Transports = HttpTransportType.LongPolling;
            }).Build();
        hub.On<JsonElement>("sessionsEnded", _ => ended.TrySetResult(true));
        await hub.StartAsync();

        var (outStatus, outBody, _) = await Send(HttpMethod.Post, "/api/account/sign-out-others", viewer);
        Assert.AreEqual(HttpStatusCode.OK, outStatus, outBody.ToString());
        Assert.IsTrue(await ended.Task.WaitAsync(TimeSpan.FromSeconds(10)), "the other browser hears sessionsEnded");
        var fresh = outBody.GetProperty("accessToken").GetString()!;
        Assert.AreEqual(HttpStatusCode.Unauthorized, (await Send(HttpMethod.Get, "/api/auth/me", otherBrowser)).Status);
        Assert.AreEqual(HttpStatusCode.Unauthorized, (await Send(HttpMethod.Get, "/api/auth/me", viewer)).Status, "the old token of this browser too");
        Assert.AreEqual(HttpStatusCode.OK, (await Send(HttpMethod.Get, "/api/auth/me", fresh)).Status);
        var audit = (await Send(HttpMethod.Get, "/api/admin/audit?top=20", await Login("admin"))).Body;
        Assert.IsTrue(audit.EnumerateArray().Any(a => a.GetProperty("eventType").GetString() == "SignedOutOtherSessions"));
        await Send(HttpMethod.Put, "/api/account/preferences", fresh, new { });
    }
}
