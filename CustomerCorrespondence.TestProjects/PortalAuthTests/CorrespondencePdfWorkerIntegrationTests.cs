using System.Text;
using Azure.Storage.Blobs;
using CustomerCorrespondence.Application.Interfaces;
using CustomerCorrespondence.Domain.Interfaces;
using CustomerCorrespondence.Infrastructure.Services;
using Microsoft.Data.SqlClient;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using ProcessRequestEndPoint.Auth;
using ProcessRequestEndPoint.Pdf;
using UglyToad.PdfPig;

namespace PortalAuthTests;

/// <summary>
/// Runs the real PDF worker against the throwaway database and a Blob Storage emulator. Needs
/// ECCO_AUTH_TEST_CONNECTION (see AuthRepositoryIntegrationTests) and ECCO_PDF_TEST_BLOB, e.g.
/// "UseDevelopmentStorage=true" with Azurite running. Creates minimal stand-ins for the correspondence
/// tables when missing and a fresh container per run.
/// </summary>
[TestClass]
public class CorrespondencePdfWorkerIntegrationTests
{
    private const string DevSettings = @"C:\ECCO\Repos\ECCO-CommunicationManager\CustomerCorrespondence.Orchestrator\ProcessRequestEndPoint\appsettings.Development.json";
    private const string SchemaScript = @"C:\ECCO\Repos\ECCO-CommunicationManager\Build\DBScripts\Pdf\001_CorrespondencePdf_Schema.sql";

    private static async Task Exec(string cs, string sql)
    {
        await using var c = new SqlConnection(cs);
        await c.OpenAsync();
        foreach (var batch in sql.Split(new[] { "\nGO", "\r\nGO" }, StringSplitOptions.RemoveEmptyEntries))
        {
            if (string.IsNullOrWhiteSpace(batch)) continue;
            await using var cmd = new SqlCommand(batch, c);
            await cmd.ExecuteNonQueryAsync();
        }
    }

    private sealed class LocalSecrets : ISecretsCacheService
    {
        private readonly IConfiguration _c;
        public LocalSecrets(IConfiguration c) => _c = c;
        public Task<string> GetSecretAsync(string n) => Task.FromResult(_c[$"LocalSecretOverrides:{n}"] ?? string.Empty);
        public bool IsSecretCached(string n) => false;
        public string? GetCachedSecret(string n) => null;
        public void ClearCache() { }
        public void RemoveSecret(string n) { }
        public IEnumerable<string> GetCachedSecretNames() => Array.Empty<string>();
        public void SetSecret(string n, string v, TimeSpan d) { }
    }

    [TestMethod]
    public async Task Worker_GeneratesStoresAndRecords_PlainAndPgpPayloads_AndBacksOffFailures()
    {
        var cs = Environment.GetEnvironmentVariable("ECCO_AUTH_TEST_CONNECTION");
        var blob = Environment.GetEnvironmentVariable("ECCO_PDF_TEST_BLOB");
        if (string.IsNullOrWhiteSpace(cs) || string.IsNullOrWhiteSpace(blob))
            throw new AssertInconclusiveException("ECCO_AUTH_TEST_CONNECTION and ECCO_PDF_TEST_BLOB are not set.");

        // Stand-ins for the correspondence tables (throwaway database only), then the real PDF schema script.
        await Exec(cs, @"
IF OBJECT_ID('dbo.CorrespondenceRequest') IS NULL
  CREATE TABLE dbo.CorrespondenceRequest (CorrespondenceId UNIQUEIDENTIFIER PRIMARY KEY, RequestingDomainName VARCHAR(100), DeliveryMethodCode VARCHAR(50));
IF COL_LENGTH('dbo.CorrespondenceRequest', 'DocId') IS NULL ALTER TABLE dbo.CorrespondenceRequest ADD DocId VARCHAR(50) NULL;
IF COL_LENGTH('dbo.CorrespondenceRequest', 'ClaimNumber') IS NULL ALTER TABLE dbo.CorrespondenceRequest ADD ClaimNumber VARCHAR(50) NULL;
IF COL_LENGTH('dbo.CorrespondenceRequest', 'CustomerId') IS NULL ALTER TABLE dbo.CorrespondenceRequest ADD CustomerId VARCHAR(50) NULL;
IF COL_LENGTH('dbo.CorrespondenceRequest', 'RequestCreationTS') IS NULL ALTER TABLE dbo.CorrespondenceRequest ADD RequestCreationTS DATETIME NULL;
IF COL_LENGTH('dbo.CorrespondenceRequest', 'RequestPayload') IS NULL ALTER TABLE dbo.CorrespondenceRequest ADD RequestPayload VARBINARY(MAX) NULL;
IF OBJECT_ID('dbo.CorrespondenceRequestStatusHistory') IS NULL
  CREATE TABLE dbo.CorrespondenceRequestStatusHistory (HistoryId INT IDENTITY PRIMARY KEY, CorrespondenceId UNIQUEIDENTIFIER NOT NULL
    REFERENCES dbo.CorrespondenceRequest (CorrespondenceId), StatusCode VARCHAR(50), HistorySequenceNum INT, StatusUpdateTS DATETIME2);
IF OBJECT_ID('dbo.CorrespondenceDeliveryMethod') IS NULL
  CREATE TABLE dbo.CorrespondenceDeliveryMethod (DeliveryMethodCode VARCHAR(50) PRIMARY KEY, DeliveryMethodName VARCHAR(100));
IF OBJECT_ID('dbo.CorrespondenceStatusCode') IS NULL
  CREATE TABLE dbo.CorrespondenceStatusCode (StatusCode VARCHAR(50) PRIMARY KEY, StatusDescription VARCHAR(255));");
        await Exec(cs, "SET QUOTED_IDENTIFIER ON;\n" + await File.ReadAllTextAsync(SchemaScript));
        // Earlier runs' rows would be picked up too: start from a clean queue.
        await Exec(cs, "DELETE FROM dbo.CorrespondencePdfDocument;");
        // Other tests share this database: age their records out of the backfill window so only ours are picked up.
        await Exec(cs, "UPDATE dbo.CorrespondenceRequest SET RequestCreationTS = DATEADD(DAY, -10, SYSUTCDATETIME()) WHERE RequestCreationTS > DATEADD(DAY, -2, SYSUTCDATETIME());");

        var config = new ConfigurationBuilder()
            .AddJsonFile(DevSettings)
            .AddInMemoryCollection(new Dictionary<string, string?> { ["ConnectionStrings:AZURE_SQL_CONNECTION"] = cs })
            .Build();
        var container = "test-pdfs-" + Guid.NewGuid().ToString("N")[..12];
        var options = new PdfGenerationOptions
        {
            Enabled = true, BatchSize = 10, MaxAttempts = 3, MinAgeSeconds = 60, BackfillDays = 1,
            Blob = new PdfGenerationOptions.BlobOptions { ConnectionString = blob, ContainerName = container }
        };

        var services = new ServiceCollection()
            .AddLogging(b => b.SetMinimumLevel(LogLevel.Warning))
            .AddSingleton<IConfiguration>(config)
            .AddSingleton(Options.Create(options))
            .AddSingleton(Options.Create(new PortalAuthOptions()))
            .AddSingleton<ISecretsCacheService>(new LocalSecrets(config))
            .AddScoped<IPgpEncryptionService, PgpEncryptionService>()
            .AddScoped<IEncryptionService, EncryptionService>()
            .AddSingleton<CorrespondencePdfRepository>()
            .AddScoped<CorrespondencePayloadDecoder>()
            .AddSingleton<PdfBlobStorage>()
            .AddScoped<CorrespondencePdfService>()
            .AddSingleton<CorrespondencePdfWorker>()
            .BuildServiceProvider();

        // Three requests created 10 minutes ago: plain JSON, PGP-encrypted (dev key) and unreadable.
        var json = """{"DocId":"D-1","RequestingSystemName":"Claims System","DeliveryMethod":"Mail","DocumentDescription":"Welcome Kit","MailDeliveryInfo":{"RecipientFullName":"Ana Lopez"},"CompositionInfo":{}}""";
        string pgpText;
        using (var scope = services.CreateScope())
            pgpText = await scope.ServiceProvider.GetRequiredService<IEncryptionService>().EncryptAsync(json.Replace("Ana Lopez", "Wei Chen"));
        var plainId = Guid.NewGuid(); var pgpId = Guid.NewGuid(); var badId = Guid.NewGuid(); var oldId = Guid.NewGuid();
        await using (var c = new SqlConnection(cs))
        {
            await c.OpenAsync();
            foreach (var (id, payload, minutesAgo) in new[]
                     {
                         (plainId, Encoding.UTF8.GetBytes(json), 10), (pgpId, Encoding.UTF8.GetBytes(pgpText), 10),
                         (badId, Encoding.UTF8.GetBytes("not json"), 10), (oldId, Encoding.UTF8.GetBytes(json), 60 * 24 * 5)
                     })
            {
                await using var cmd = new SqlCommand(@"INSERT dbo.CorrespondenceRequest (CorrespondenceId, RequestingDomainName, DeliveryMethodCode, DocId, ClaimNumber, RequestCreationTS, RequestPayload)
VALUES (@id, 'Claims System', 'Mail', 'D-1', 'CL-1', DATEADD(MINUTE, -@ago, SYSUTCDATETIME()), @p)", c);
                cmd.Parameters.AddWithValue("@id", id);
                cmd.Parameters.AddWithValue("@ago", minutesAgo);
                cmd.Parameters.Add("@p", System.Data.SqlDbType.VarBinary, -1).Value = payload;
                await cmd.ExecuteNonQueryAsync();
            }
        }

        var worker = services.GetRequiredService<CorrespondencePdfWorker>();
        var repo = services.GetRequiredService<CorrespondencePdfRepository>();
        Assert.AreEqual(2, await worker.RunOnceAsync(CancellationToken.None));

        var statuses = (await repo.GetStatusesAsync(new[] { plainId, pgpId, badId, oldId }, CancellationToken.None)).ToDictionary(s => s.CorrespondenceId);
        Assert.AreEqual("Generated", statuses[plainId].Status);
        Assert.AreEqual("Generated", statuses[pgpId].Status);
        Assert.AreEqual("Failed", statuses[badId].Status);
        StringAssert.Contains(statuses[badId].LastError, "valid JSON");
        Assert.AreEqual("None", statuses[oldId].Status, "older than BackfillDays: not picked up automatically");

        // The PGP one really was decrypted and stored.
        var blobs = new BlobServiceClient(blob).GetBlobContainerClient(container);
        var download = await blobs.GetBlobClient(statuses[pgpId].BlobName).DownloadContentAsync();
        Assert.AreEqual("application/pdf", download.Value.Details.ContentType);
        Assert.AreEqual(pgpId.ToString(), download.Value.Details.Metadata["correspondenceId"]);
        using (var pdf = PdfDocument.Open(download.Value.Content.ToArray()))
            StringAssert.Contains(string.Join(" ", pdf.GetPage(1).GetWords().Select(w => w.Text)), "Wei Chen");

        // Second cycle: nothing new, and the failure waits for its backoff.
        Assert.AreEqual(0, await worker.RunOnceAsync(CancellationToken.None));
        Assert.AreEqual(1, (await repo.GetStatusesAsync(new[] { badId }, CancellationToken.None))[0].Attempts);

        // On demand works for the old record.
        using (var scope = services.CreateScope())
        {
            var status = await scope.ServiceProvider.GetRequiredService<CorrespondencePdfService>().GenerateNowAsync(oldId, CancellationToken.None);
            Assert.AreEqual("Generated", status.Status);
        }

        await blobs.DeleteIfExistsAsync();
    }
}
