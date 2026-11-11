using Microsoft.Data.SqlClient;
using Microsoft.Extensions.Configuration;
using System;
using System.Collections.Generic;
using System.Data;
using System.IO;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

class Program
{
    static int Main(string[] args)
    {
        // Build configuration (appsettings.json in output folder; environment variables override)
        var config = new ConfigurationBuilder()
            .SetBasePath(AppContext.BaseDirectory)
            .AddJsonFile("appsettings.json", optional: true, reloadOnChange: false)
            .AddEnvironmentVariables()
            .Build();

        // Read values from configuration (appsettings.json) with fallback to environment variables
        var connString = config["AZURE_SQL_CONNECTION"]
                         ?? Environment.GetEnvironmentVariable("AZURE_SQL_CONNECTION")
                         ?? throw new InvalidOperationException("Set AZURE_SQL_CONNECTION in appsettings.json or environment variables.");

        var encKeyString = config["PAYLOAD_ENCRYPTION_KEY"]
                           ?? Environment.GetEnvironmentVariable("PAYLOAD_ENCRYPTION_KEY"); // optional

        byte[]? encKey = null;
        if (!string.IsNullOrWhiteSpace(encKeyString))
        {
            // Accept base64 or hex; provide clear error if neither valid 32-byte key
            if (TryParseBase64(encKeyString, out var keyBytes) && keyBytes.Length == 32)
            {
                encKey = keyBytes;
            }
            else if (TryParseHex(encKeyString, out keyBytes) && keyBytes.Length == 32)
            {
                encKey = keyBytes;
            }
            else
            {
                throw new InvalidOperationException(
                    "PAYLOAD_ENCRYPTION_KEY must be a 32-byte key encoded as base64 or hex.\n" +
                    "Generate a secure base64 key using PowerShell:\n" +
                    "  $k = New-Object byte[] 32; [System.Security.Cryptography.RandomNumberGenerator]::Create().GetBytes($k); [Convert]::ToBase64String($k)\n" +
                    "Or using openssl:\n" +
                    "  openssl rand -out key.bin 32 && openssl base64 -in key.bin\n" +
                    "If you supplied a passphrase, consider deriving a 32-byte key (PBKDF2) instead of passing the passphrase directly.");
            }
        }


        var templateDir = Path.Combine(AppContext.BaseDirectory, "templates");
        var annuityPath = Path.Combine(templateDir, "annuity.json");
        var pgaPath = Path.Combine(templateDir, "pga.json");

        // parse args: count, optional terminal status (e.g. "Failed") to force for every seeded row
        var total = 30000;
        if (args.Length > 0 && int.TryParse(args[0], out var parsed)) total = parsed;
        string? statusOverride = args.Length > 1 ? args[1] : null;

        Console.WriteLine($"Seeding {total} rows. Encrypt: {encKey != null}. StatusOverride: {statusOverride ?? "(random)"}");

        // Load templates (fallback to defaults)
        var annuityTemplate = File.Exists(annuityPath) ? File.ReadAllText(annuityPath) : GetDefaultAnnuityTemplate();
        var pgaTemplate = File.Exists(pgaPath) ? File.ReadAllText(pgaPath) : GetDefaultPgaTemplate();

        var rnd = new Random();

        using var sql = new SqlConnection(connString);
        sql.Open();

        // Ensure MAIL exists in delivery methods
        using (var cmd = new SqlCommand(@"IF NOT EXISTS (SELECT 1 FROM dbo.CorrespondenceDeliveryMethod WHERE DeliveryMethodName = 'MAIL')
                INSERT INTO dbo.CorrespondenceDeliveryMethod (DeliveryMethodId, DeliveryMethodCode, DeliveryMethodName)
                VALUES (NEWID(), '01', 'Mail')", sql))
        {
            cmd.ExecuteNonQuery();
        }

        // Load existing DocIds and CorrespondenceIds from DB to avoid duplicates
        var existingDocIds = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        using (var cmdExistingDocs = new SqlCommand("SELECT DISTINCT DocId FROM dbo.CorrespondenceRequest", sql))
        using (var rdrExistingDocs = cmdExistingDocs.ExecuteReader())
        {
            while (rdrExistingDocs.Read()) existingDocIds.Add(rdrExistingDocs.GetString(0));
        }

        var existingCorrIds = new HashSet<Guid>();
        using (var cmdExistingCorr = new SqlCommand("SELECT CorrespondenceId FROM dbo.CorrespondenceRequest", sql))
        using (var rdrExistingCorr = cmdExistingCorr.ExecuteReader())
        {
            while (rdrExistingCorr.Read()) existingCorrIds.Add(rdrExistingCorr.GetGuid(0));
        }

        // DocumentId pool
        var docPool = new List<string>();
        using (var cmd = new SqlCommand("SELECT TOP (2000) DocIds FROM dbo.DocumentIds ORDER BY NEWID()", sql))
        using (var rdr = cmd.ExecuteReader())
        {
            while (rdr.Read())
            {
                var d = rdr.GetString(0);
                // only include DocIds not already present in CorrespondenceRequest
                if (!existingDocIds.Contains(d)) docPool.Add(d);
            }
        }
        if (docPool.Count == 0)
        {
            Console.WriteLine("No DocumentIds found; creating 2000 sample DocumentIds.");
            using var tx = sql.BeginTransaction();
            using var ins = new SqlCommand("INSERT INTO dbo.DocumentIds (DocIds, DocIdStatus, RequestCreationTS) VALUES (@d,@s,@t)", sql, tx);
            ins.Parameters.Add(new SqlParameter("@d", System.Data.SqlDbType.VarChar, 19));
            ins.Parameters.Add(new SqlParameter("@s", System.Data.SqlDbType.VarChar, 20));
            ins.Parameters.Add(new SqlParameter("@t", System.Data.SqlDbType.DateTime));
            for (int i = 0; i < 2000; i++)
            {
                var d = $"DOC-{i:0000000}-{Guid.NewGuid():N}"[..19];
                ins.Parameters["@d"].Value = d;
                ins.Parameters["@s"].Value = (i % 5 == 0) ? "Used" : "Available";
                ins.Parameters["@t"].Value = DateTime.UtcNow.AddSeconds(-rnd.Next(0, 60 * 60 * 24 * 30));
                ins.ExecuteNonQuery();
                docPool.Add(d);
            }
            tx.Commit();
        }

        // Prepare CorrespondenceRequest DataTable
        var dt = new DataTable();
        dt.Columns.Add("CorrespondenceId", typeof(Guid));
        dt.Columns.Add("DocId", typeof(string));
        dt.Columns.Add("RequestingDomainName", typeof(string));
        dt.Columns.Add("DeliveryMethodCode", typeof(string));
        dt.Columns.Add("VersionCode", typeof(string));
        dt.Columns.Add("ArchiveDocumentTypeCode", typeof(string));
        dt.Columns.Add("ArchiveDocumentSubTypeCode", typeof(string));
        dt.Columns.Add("PolicyNumber", typeof(byte[]));
        dt.Columns.Add("ClaimNumber", typeof(string));
        dt.Columns.Add("CustomerId", typeof(string));
        dt.Columns.Add("RequestPayload", typeof(byte[]));
        dt.Columns.Add("RequestCreationTS", typeof(DateTime));
        dt.Columns.Add("PolicyNumberHash", typeof(byte[]));
        dt.Columns.Add("VFResponseId", typeof(string));

        var ids = new List<Guid>(total);
        var usedDocIds = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        for (int i = 0; i < total; i++)
        {
            // Ensure CorrespondenceId is unique against existing DB values and this batch
            Guid id;
            do
            {
                id = Guid.NewGuid();
            } while (existingCorrIds.Contains(id) || ids.Contains(id));
            ids.Add(id);
            existingCorrIds.Add(id);

            var usePga = (i % 2 == 0);
            var template = usePga ? pgaTemplate : annuityTemplate;
            string doc;
            if (docPool.Count > 0)
            {
                var idx = rnd.Next(docPool.Count);
                doc = docPool[idx];
                // remove from pool so we don't reuse it
                docPool.RemoveAt(idx);
            }
            else
            {
                // generate a new unique DocId (19 chars like prior logic)
                string gen;
                do
                {
                    gen = $"DOC-{i:0000000}-{Guid.NewGuid():N}".Substring(0, 19);
                } while (existingDocIds.Contains(gen) || usedDocIds.Contains(gen));
                doc = gen;
            }
            usedDocIds.Add(doc);
            var customer = usePga ? "Payment Guard Advantage" : "Annuity Correspondence";
            var policyNumber = $"PN-{rnd.Next(100000, 999999)}";
            var claimNumber = $"CL-{rnd.Next(10000, 99999)}";
            var customerId = $"CUST-{rnd.Next(1, 30000)}";
            var created = DateTime.UtcNow.AddSeconds(-rnd.Next(0, 60 * 60 * 24 * 30));

            var payloadJson = MutateTemplateJson(template, rnd, id, doc, customer, policyNumber, claimNumber);
            var payloadBytes = Encoding.UTF8.GetBytes(payloadJson);

            if (encKey != null)
            {
                payloadBytes = EncryptAesGcm(encKey, payloadBytes);
            }

            var policyHash = SHA256.HashData(Encoding.UTF8.GetBytes(policyNumber));

            dt.Rows.Add(id, doc, customer, "MAIL", "v1.0", $"ArchiveType-{i % 10}", $"ArchiveSubType-{i % 7}",
                        Encoding.UTF8.GetBytes(policyNumber), claimNumber, customerId, payloadBytes, created, policyHash, $"VF-{Guid.NewGuid():N}"[..20]);
        }

        // Bulk insert requests
        using (var bulk = new SqlBulkCopy(sql))
        {
            bulk.DestinationTableName = "dbo.CorrespondenceRequest";
            bulk.BatchSize = 1000;
            bulk.ColumnMappings.Add("CorrespondenceId", "CorrespondenceId");
            bulk.ColumnMappings.Add("DocId", "DocId");
            bulk.ColumnMappings.Add("RequestingDomainName", "RequestingDomainName");
            bulk.ColumnMappings.Add("DeliveryMethodCode", "DeliveryMethodCode");
            bulk.ColumnMappings.Add("VersionCode", "VersionCode");
            bulk.ColumnMappings.Add("ArchiveDocumentTypeCode", "ArchiveDocumentTypeCode");
            bulk.ColumnMappings.Add("ArchiveDocumentSubTypeCode", "ArchiveDocumentSubTypeCode");
            bulk.ColumnMappings.Add("PolicyNumber", "PolicyNumber");
            bulk.ColumnMappings.Add("ClaimNumber", "ClaimNumber");
            bulk.ColumnMappings.Add("CustomerId", "CustomerId");
            bulk.ColumnMappings.Add("RequestPayload", "RequestPayload");
            bulk.ColumnMappings.Add("RequestCreationTS", "RequestCreationTS");
            bulk.ColumnMappings.Add("PolicyNumberHash", "PolicyNumberHash");
            bulk.ColumnMappings.Add("VFResponseId", "VFResponseId");
            bulk.WriteToServer(dt);
        }
        Console.WriteLine($"Inserted {ids.Count} CorrespondenceRequest rows.");

        // Build and bulk insert history
        var dtHist = new DataTable();
        dtHist.Columns.Add("CorrespondenceRequestStatusHistoryId", typeof(Guid));
        dtHist.Columns.Add("CorrespondenceId", typeof(Guid));
        dtHist.Columns.Add("HistorySequenceNum", typeof(int));
        dtHist.Columns.Add("StatusCode", typeof(string));
        dtHist.Columns.Add("StatusUpdatedBy", typeof(string));
        dtHist.Columns.Add("StatusUpdateTS", typeof(DateTime));
        dtHist.Columns.Add("SFMCEVentInstanceId", typeof(string));

        foreach (var cid in ids)
        {
            dtHist.Rows.Add(Guid.NewGuid(), cid, 1, "Submitted", "System", DateTime.UtcNow.AddSeconds(-rnd.Next(0, 3600)), $"sfmc-{Guid.NewGuid():N}"[..12]);
            if (statusOverride != null)
            {
                dtHist.Rows.Add(Guid.NewGuid(), cid, 2, "Processing", $"Worker-{rnd.Next(1, 40)}", DateTime.UtcNow.AddSeconds(-rnd.Next(0, 1800)), $"sfmc-{Guid.NewGuid():N}"[..12]);
                dtHist.Rows.Add(Guid.NewGuid(), cid, 3, statusOverride, $"Worker-{rnd.Next(1, 40)}", DateTime.UtcNow.AddSeconds(-rnd.Next(0, 600)), $"sfmc-{Guid.NewGuid():N}"[..12]);
            }
            else
            {
                if (rnd.Next(0, 100) < 80) dtHist.Rows.Add(Guid.NewGuid(), cid, 2, "Processing", $"Worker-{rnd.Next(1, 40)}", DateTime.UtcNow.AddSeconds(-rnd.Next(0, 1800)), $"sfmc-{Guid.NewGuid():N}"[..12]);
                if (rnd.Next(0, 100) < 40) dtHist.Rows.Add(Guid.NewGuid(), cid, 3, "Completed", $"Worker-{rnd.Next(1, 40)}", DateTime.UtcNow.AddSeconds(-rnd.Next(0, 600)), $"sfmc-{Guid.NewGuid():N}"[..12]);
            }
        }

        using (var bulk = new SqlBulkCopy(sql))
        {
            bulk.DestinationTableName = "dbo.CorrespondenceRequestStatusHistory";
            bulk.BatchSize = 1000;
            bulk.ColumnMappings.Add("CorrespondenceRequestStatusHistoryId", "CorrespondenceRequestStatusHistoryId");
            bulk.ColumnMappings.Add("CorrespondenceId", "CorrespondenceId");
            bulk.ColumnMappings.Add("HistorySequenceNum", "HistorySequenceNum");
            bulk.ColumnMappings.Add("StatusCode", "StatusCode");
            bulk.ColumnMappings.Add("StatusUpdatedBy", "StatusUpdatedBy");
            bulk.ColumnMappings.Add("StatusUpdateTS", "StatusUpdateTS");
            bulk.ColumnMappings.Add("SFMCEventInstanceId", "SFMCEventInstanceId");
            bulk.WriteToServer(dtHist);
        }
        Console.WriteLine($"Inserted {dtHist.Rows.Count} CorrespondenceRequestStatusHistory rows.");

        // Attempt to create a foreign key constraint from CorrespondenceRequestStatusHistory.CorrespondenceId
        // to CorrespondenceRequest.CorrespondenceId if it does not already exist and there are no orphan rows.
        using (var chk = new SqlCommand(@"SELECT COUNT(1) FROM dbo.CorrespondenceRequestStatusHistory h LEFT JOIN dbo.CorrespondenceRequest r ON h.CorrespondenceId = r.CorrespondenceId WHERE r.CorrespondenceId IS NULL", sql))
        {
            var orphan = (int)chk.ExecuteScalar();
            if (orphan == 0)
            {
                using (var addFk = new SqlCommand(@"IF NOT EXISTS (SELECT 1 FROM sys.foreign_keys WHERE name = 'FK_CorrespondenceRequestStatusHistory_CorrespondenceRequest')
                        BEGIN
                            ALTER TABLE dbo.CorrespondenceRequestStatusHistory
                            ADD CONSTRAINT FK_CorrespondenceRequestStatusHistory_CorrespondenceRequest
                            FOREIGN KEY (CorrespondenceId) REFERENCES dbo.CorrespondenceRequest(CorrespondenceId);
                        END", sql))
                {
                    addFk.ExecuteNonQuery();
                    Console.WriteLine("Foreign key constraint created (if it did not exist).");
                }
            }
            else
            {
                Console.WriteLine($"Skipped adding FK; found {orphan} orphan status history rows.");
            }
        }
        return 0;
    }

    // --- helper methods (same as prior) ---
    static string MutateTemplateJson(string template, Random rnd, Guid id, string docId, string customer, string policyNumber, string claimNumber)
    {
        try
        {
            using var doc = JsonDocument.Parse(template);
            var root = doc.RootElement.Clone();
            var obj = JsonElementToDictionary(root);

            obj["requestRef"] = id.ToString();
            obj["documentId"] = docId;
            obj["customerName"] = customer;
            obj["policyNumber"] = policyNumber;
            obj["claimNumber"] = claimNumber;
            obj["generatedAt"] = DateTime.UtcNow.ToString("o");
            if (!obj.ContainsKey("additionalData")) obj["additionalData"] = new Dictionary<string, object>();
            var additional = obj["additionalData"] as Dictionary<string, object>;
            additional!["seed"] = rnd.Next();
            additional!["notes"] = $"Auto-generated at {DateTime.UtcNow:yyyy-MM-dd HH:mm:ss}";
            additional!["randomFlag"] = (rnd.Next(0, 100) < 50);

            return JsonSerializer.Serialize(obj);
        }
        catch
        {
            var s = template.Replace("{{REQUEST_REF}}", id.ToString())
                            .Replace("{{DOCUMENT_ID}}", docId)
                            .Replace("{{CUSTOMER_NAME}}", customer)
                            .Replace("{{POLICY_NUMBER}}", policyNumber)
                            .Replace("{{CLAIM_NUMBER}}", claimNumber)
                            .Replace("{{GENERATED_AT}}", DateTime.UtcNow.ToString("o"));
            return s;
        }
    }

    static Dictionary<string, object?> JsonElementToDictionary(JsonElement el)
    {
        if (el.ValueKind != JsonValueKind.Object) return new Dictionary<string, object?> { ["value"] = el.ToString() };
        var dict = new Dictionary<string, object?>(StringComparer.OrdinalIgnoreCase);
        foreach (var prop in el.EnumerateObject())
        {
            dict[prop.Name] = JsonElementToValue(prop.Value);
        }
        return dict;
    }

    static object? JsonElementToValue(JsonElement el) =>
        el.ValueKind switch
        {
            JsonValueKind.Object => JsonElementToDictionary(el),
            JsonValueKind.Array => JsonArrayToList(el),
            JsonValueKind.String => el.GetString(),
            JsonValueKind.Number => el.TryGetInt64(out var i) ? (object)i : el.GetDouble(),
            JsonValueKind.True => true,
            JsonValueKind.False => false,
            JsonValueKind.Null => null,
            _ => el.ToString()
        };

    static List<object?> JsonArrayToList(JsonElement el)
    {
        var list = new List<object?>();
        foreach (var item in el.EnumerateArray()) list.Add(JsonElementToValue(item));
        return list;
    }

    // Accept base64 input, returns false on invalid input
    static bool TryParseBase64(string s, out byte[] bytes)
    {
        bytes = Array.Empty<byte>();
        try
        {
            var cleaned = s.Trim();
            bytes = Convert.FromBase64String(cleaned);
            return true;
        }
        catch
        {
            return false;
        }
    }

    // Accept hex string (optional 0x prefix), returns false on invalid input
    static bool TryParseHex(string s, out byte[] bytes)
    {
        bytes = Array.Empty<byte>();
        try
        {
            var hex = s.Trim();
            if (hex.StartsWith("0x", StringComparison.OrdinalIgnoreCase)) hex = hex.Substring(2);
            hex = hex.Replace(" ", "").Replace("-", "");
            if (hex.Length % 2 != 0) return false;
            bytes = new byte[hex.Length / 2];
            for (int i = 0; i < bytes.Length; i++)
            {
                bytes[i] = Convert.ToByte(hex.Substring(i * 2, 2), 16);
            }
            return true;
        }
        catch
        {
            return false;
        }
    }

    // AES-GCM encryption storing: nonce(12) | tag(16) | ciphertext
    static byte[] EncryptAesGcm(byte[] key, byte[] plaintext)
    {
        var nonce = new byte[12];
        RandomNumberGenerator.Fill(nonce);
        var ciphertext = new byte[plaintext.Length];
        var tag = new byte[16];

        // Specify tag size to avoid SYSLIB0053 warnings
        using var aesgcm = new AesGcm(key, tagSizeInBytes: 16);
        aesgcm.Encrypt(nonce, plaintext, ciphertext, tag);

        var outb = new byte[nonce.Length + tag.Length + ciphertext.Length];
        Buffer.BlockCopy(nonce, 0, outb, 0, nonce.Length);
        Buffer.BlockCopy(tag, 0, outb, nonce.Length, tag.Length);
        Buffer.BlockCopy(ciphertext, 0, outb, nonce.Length + tag.Length, ciphertext.Length);
        return outb;
    }

    static string GetDefaultAnnuityTemplate() =>
    @"{
        ""requestRef"": ""{{REQUEST_REF}}"",
        ""documentId"": ""{{DOCUMENT_ID}}"",
        ""customerName"": ""{{CUSTOMER_NAME}}"",
        ""productType"": ""Annuity"",
        ""recipient"": { ""name"": ""Member X"", ""address"": { ""line1"": ""1 Annuity St"", ""city"": ""CityA"", ""postcode"": ""P0001"" } },
        ""additionalData"": { ""policyType"": ""Life"", ""beneficiaries"": [{ ""name"": ""Ben 1"", ""share"": ""50%"" }] }
    }";

    static string GetDefaultPgaTemplate() =>
    @"{
        ""requestRef"": ""{{REQUEST_REF}}"",
        ""documentId"": ""{{DOCUMENT_ID}}"",
        ""customerName"": ""{{CUSTOMER_NAME}}"",
        ""productType"": ""Auto"",
        ""recipient"": { ""name"": ""Member Y"", ""address"": { ""line1"": ""2 PGA Ave"", ""city"": ""CityB"", ""postcode"": ""P0002"" } },
        ""additionalData"": { ""policyType"": ""Auto"", ""vehicle"": { ""make"": ""MakeX"", ""model"": ""ModelY"" } }
    }";
}