using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Options;
using ProcessRequestEndPoint.Auth;

namespace PortalAuthTests;

/// <summary>
/// Runs against a database with Build/DBScripts/Auth 001-003 applied. Skipped (inconclusive) unless
/// ECCO_AUTH_TEST_CONNECTION is set, e.g.
///   Data Source=(localdb)\MSSQLLocalDB;Initial Catalog=ECCO_AuthScratch;Integrated Security=True
/// Never point it at a shared database: it creates and edits users.
/// </summary>
[TestClass]
public class AuthRepositoryIntegrationTests
{
    private static AuthRepository? _repo;

    [ClassInitialize]
    public static void Init(TestContext _)
    {
        var cs = Environment.GetEnvironmentVariable("ECCO_AUTH_TEST_CONNECTION");
        if (string.IsNullOrWhiteSpace(cs)) return;
        var config = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?> { ["ConnectionStrings:AZURE_SQL_CONNECTION"] = cs }).Build();
        _repo = new AuthRepository(config, Options.Create(new PortalAuthOptions()));
    }

    private static AuthRepository Repo => _repo ?? throw new AssertInconclusiveException("ECCO_AUTH_TEST_CONNECTION is not set.");

    [TestMethod]
    public async Task SeededUsers_HaveRolePermissionsAndAttributes()
    {
        var creds = await Repo.FindCredentialsAsync("OPERATOR");
        Assert.IsNotNull(creds);
        Assert.IsTrue(PasswordHasher.Verify("EccoDev!2026", creds.PasswordHash, creds.PasswordSalt, creds.PasswordIterations));

        var profile = await Repo.GetAccessProfileAsync(creds.UserId);
        Assert.IsNotNull(profile);
        CollectionAssert.AreEqual(new[] { "Operator" }, profile.Roles.ToArray());
        Assert.IsTrue(profile.Has(Permissions.CorrespondenceResubmit));
        Assert.IsFalse(profile.Has(Permissions.UsersManage));
        CollectionAssert.AreEquivalent(new[] { "Claims System", "Life Services" }, profile.ScopeValues(SubjectAttributes.CustomerScope)!.ToArray());
        Assert.IsNull(profile.ScopeValues(SubjectAttributes.DeliveryMethodScope), "'*' means unrestricted");

        var policies = await Repo.GetPoliciesAsync(activeOnly: true);
        Assert.IsTrue(policies.Any(p => p.PolicyName == "Resubmit failed records only"));
        Assert.IsFalse(policies.Any(p => p.PolicyName.StartsWith("Resubmit during business hours")), "sample policy is inactive");
    }

    [TestMethod]
    public async Task DeleteCorrespondence_RemovesRecordsAndStatusHistoryOnly()
    {
        var cs = Environment.GetEnvironmentVariable("ECCO_AUTH_TEST_CONNECTION")!;
        Repo.GetType(); // inconclusive when not configured
        // Minimal stand-ins for the real tables (same names, keys and FK) in the throwaway database.
        await Exec(cs, @"
IF OBJECT_ID('dbo.CorrespondenceRequest') IS NULL
  CREATE TABLE dbo.CorrespondenceRequest (CorrespondenceId UNIQUEIDENTIFIER PRIMARY KEY, RequestingDomainName VARCHAR(100), DeliveryMethodCode VARCHAR(50));
IF OBJECT_ID('dbo.CorrespondenceRequestStatusHistory') IS NULL
  CREATE TABLE dbo.CorrespondenceRequestStatusHistory (HistoryId INT IDENTITY PRIMARY KEY, CorrespondenceId UNIQUEIDENTIFIER NOT NULL
    REFERENCES dbo.CorrespondenceRequest (CorrespondenceId), StatusCode VARCHAR(50), HistorySequenceNum INT, StatusUpdateTS DATETIME2);");
        var keep = Guid.NewGuid(); var drop1 = Guid.NewGuid(); var drop2 = Guid.NewGuid();
        foreach (var id in new[] { keep, drop1, drop2 })
            await Exec(cs, $@"INSERT dbo.CorrespondenceRequest (CorrespondenceId, RequestingDomainName, DeliveryMethodCode) VALUES ('{id}', 'Claims System', 'Mail');
INSERT dbo.CorrespondenceRequestStatusHistory (CorrespondenceId, StatusCode, HistorySequenceNum, StatusUpdateTS) VALUES ('{id}', 'Failed', 1, SYSUTCDATETIME()), ('{id}', 'New', 0, SYSUTCDATETIME());");

        Assert.AreEqual(2, await Repo.DeleteCorrespondenceAsync(new[] { drop1, drop2, Guid.NewGuid() }));
        Assert.AreEqual(0, await Count(cs, $"SELECT COUNT(*) FROM dbo.CorrespondenceRequestStatusHistory WHERE CorrespondenceId IN ('{drop1}','{drop2}')"));
        Assert.AreEqual(1, await Count(cs, $"SELECT COUNT(*) FROM dbo.CorrespondenceRequest WHERE CorrespondenceId = '{keep}'"));
        Assert.AreEqual(2, await Count(cs, $"SELECT COUNT(*) FROM dbo.CorrespondenceRequestStatusHistory WHERE CorrespondenceId = '{keep}'"));
        Assert.AreEqual(0, await Repo.DeleteCorrespondenceAsync(Array.Empty<Guid>()));

        static async Task Exec(string cs, string sql)
        {
            await using var c = new Microsoft.Data.SqlClient.SqlConnection(cs); await c.OpenAsync();
            await using var cmd = new Microsoft.Data.SqlClient.SqlCommand(sql, c); await cmd.ExecuteNonQueryAsync();
        }
        static async Task<int> Count(string cs, string sql)
        {
            await using var c = new Microsoft.Data.SqlClient.SqlConnection(cs); await c.OpenAsync();
            await using var cmd = new Microsoft.Data.SqlClient.SqlCommand(sql, c); return Convert.ToInt32(await cmd.ExecuteScalarAsync());
        }
    }

    [TestMethod]
    public async Task CreateUpdateLockoutAndPasswordReset()
    {
        var name = "it-" + Guid.NewGuid().ToString("N")[..10];
        var id = await Repo.CreateUserAsync(name, "Integration Test", null, true, PasswordHasher.Hash("Temp-Password-1", 1000), true,
            new[] { "Viewer" }, new Dictionary<string, List<string>> { [SubjectAttributes.CustomerScope] = new() { "Life Services" } });

        var profile = await Repo.GetAccessProfileAsync(id);
        Assert.IsTrue(profile!.MustChangePassword);
        Assert.IsTrue(profile.Has(Permissions.CorrespondenceRead));
        var stamp = profile.SecurityStamp;

        Assert.IsTrue(await Repo.UpdateUserAsync(id, "Renamed", "it@example.test", true, new[] { "Analyst" },
            new Dictionary<string, List<string>> { [SubjectAttributes.CustomerScope] = new() { "*" }, [SubjectAttributes.MaxExportRows] = new() { "10" } }));
        profile = await Repo.GetAccessProfileAsync(id);
        Assert.AreEqual("Renamed", profile!.DisplayName);
        Assert.IsTrue(profile.Has(Permissions.CorrespondenceExport));
        Assert.AreNotEqual(stamp, profile.SecurityStamp, "access changes rotate the stamp");

        for (var i = 0; i < 4; i++) Assert.IsFalse(await Repo.RecordFailedLoginAsync(id, 5, 15));
        Assert.IsTrue(await Repo.RecordFailedLoginAsync(id, 5, 15), "fifth failure locks");
        Assert.IsTrue((await Repo.FindCredentialsAsync(name))!.LockoutEndUtc > DateTime.UtcNow);

        await Repo.SetPasswordAsync(id, PasswordHasher.Hash("New-Password-22", 1000), mustChange: false);
        var creds = await Repo.FindCredentialsAsync(name);
        Assert.IsNull(creds!.LockoutEndUtc, "reset clears the lockout");
        Assert.IsTrue(PasswordHasher.Verify("New-Password-22", creds.PasswordHash, creds.PasswordSalt, creds.PasswordIterations));

        await Repo.WriteAuditAsync(id, name, "IntegrationTest", "Success", "ok", "127.0.0.1");
        var audit = await Repo.ListAuditAsync(5, name);
        Assert.AreEqual("IntegrationTest", audit[0].EventType);
    }
}
