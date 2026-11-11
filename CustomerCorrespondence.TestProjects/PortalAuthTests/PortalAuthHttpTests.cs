using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Security.Cryptography;
using System.Text.Json;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using ProcessRequestEndPoint.Auth;
using ProcessRequestEndPoint.Controllers;

namespace PortalAuthTests;

/// <summary>
/// Exercises the real auth pipeline (AddPortalAuth, JWT scheme, permission policies, security-stamp
/// checks, AuthController, AdminController) in an in-memory host against the seeded test database.
/// Skipped unless ECCO_AUTH_TEST_CONNECTION is set (see AuthRepositoryIntegrationTests).
/// </summary>
[TestClass]
public class PortalAuthHttpTests
{
    private const string DevPassword = "EccoDev!2026";
    private static WebApplication? _app;
    private static HttpClient? _client;

    [ClassInitialize]
    public static async Task Start(TestContext _)
    {
        var cs = Environment.GetEnvironmentVariable("ECCO_AUTH_TEST_CONNECTION");
        if (string.IsNullOrWhiteSpace(cs)) return;

        var builder = WebApplication.CreateBuilder(new WebApplicationOptions { EnvironmentName = "Testing" });
        builder.WebHost.UseTestServer();
        builder.Configuration.AddInMemoryCollection(new Dictionary<string, string?>
        {
            // Must be the ConnectionStrings key: the API's appsettings.json (copied to the test output) sets it to the dev database.
            ["ConnectionStrings:AZURE_SQL_CONNECTION"] = cs,
            ["PortalAuth:SigningKey"] = Convert.ToBase64String(RandomNumberGenerator.GetBytes(32)),
            ["PortalAuth:AccessProfileCacheSeconds"] = "1"
        });
        builder.Services.AddControllers().AddApplicationPart(typeof(AuthController).Assembly);
        builder.Services.AddAuthorization();
        builder.AddPortalAuth();

        _app = builder.Build();
        _app.UseAuthentication();
        _app.UseAuthorization();
        _app.MapControllers();
        await _app.StartAsync();
        _client = _app.GetTestClient();
    }

    [ClassCleanup]
    public static async Task Stop()
    {
        if (_app != null) await _app.DisposeAsync();
    }

    private static HttpClient Client => _client ?? throw new AssertInconclusiveException("ECCO_AUTH_TEST_CONNECTION is not set.");

    private static async Task<(HttpStatusCode Status, JsonElement Body)> Send(HttpMethod method, string url, string? token = null, object? body = null)
    {
        using var req = new HttpRequestMessage(method, url);
        if (token != null) req.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        if (body != null) req.Content = JsonContent.Create(body);
        using var res = await Client.SendAsync(req);
        var text = await res.Content.ReadAsStringAsync();
        return (res.StatusCode, string.IsNullOrWhiteSpace(text) ? default : JsonDocument.Parse(text).RootElement.Clone());
    }

    private static async Task<string> Login(string user, string password = DevPassword)
    {
        var (status, body) = await Send(HttpMethod.Post, "/api/auth/login", body: new { userName = user, password });
        Assert.AreEqual(HttpStatusCode.OK, status, body.ToString());
        return body.GetProperty("accessToken").GetString()!;
    }

    [TestMethod]
    public async Task Login_ReturnsProfileWithRbacAndAbacData()
    {
        var (status, body) = await Send(HttpMethod.Post, "/api/auth/login", body: new { userName = "Operator", password = DevPassword });
        Assert.AreEqual(HttpStatusCode.OK, status);
        var user = body.GetProperty("user");
        Assert.AreEqual("operator", user.GetProperty("userName").GetString());
        CollectionAssert.Contains(user.GetProperty("permissions").EnumerateArray().Select(p => p.GetString()).ToList(), "correspondence.resubmit");
        Assert.AreEqual(2, user.GetProperty("attributes").GetProperty("scope.customer").GetArrayLength());
    }

    [TestMethod]
    public async Task WrongPasswordAndUnknownUser_GetTheSameMessage()
    {
        var wrong = await Send(HttpMethod.Post, "/api/auth/login", body: new { userName = "viewer", password = "nope-nope-nope" });
        var unknown = await Send(HttpMethod.Post, "/api/auth/login", body: new { userName = "nobody-" + Guid.NewGuid(), password = "nope-nope-nope" });
        Assert.AreEqual(HttpStatusCode.Unauthorized, wrong.Status);
        Assert.AreEqual(HttpStatusCode.Unauthorized, unknown.Status);
        Assert.AreEqual(wrong.Body.GetProperty("message").GetString(), unknown.Body.GetProperty("message").GetString());
        await Login("viewer"); // resets the failed-attempt counter
    }

    [TestMethod]
    public async Task PermissionPolicies_Return401And403()
    {
        Assert.AreEqual(HttpStatusCode.Unauthorized, (await Send(HttpMethod.Get, "/api/admin/users")).Status);
        Assert.AreEqual(HttpStatusCode.Unauthorized, (await Send(HttpMethod.Get, "/api/admin/users", "not-a-token")).Status);

        var viewer = await Login("viewer");
        Assert.AreEqual(HttpStatusCode.OK, (await Send(HttpMethod.Get, "/api/auth/me", viewer)).Status);
        Assert.AreEqual(HttpStatusCode.Forbidden, (await Send(HttpMethod.Get, "/api/admin/users", viewer)).Status);

        var admin = await Login("admin");
        var (status, users) = await Send(HttpMethod.Get, "/api/admin/users", admin);
        Assert.AreEqual(HttpStatusCode.OK, status);
        Assert.IsTrue(users.GetArrayLength() >= 4);
        Assert.AreEqual(HttpStatusCode.OK, (await Send(HttpMethod.Get, "/api/admin/audit?top=5", admin)).Status);
    }

    [TestMethod]
    public async Task NewUser_MustChangePassword_AndAccessChangesEndSessions()
    {
        var admin = await Login("admin");
        var name = "http-" + Guid.NewGuid().ToString("N")[..8];
        var (created, body) = await Send(HttpMethod.Post, "/api/admin/users", admin, new
        {
            userName = name, displayName = "HTTP Test", password = "Temp-Password-77", isActive = true,
            roles = new[] { "Viewer" }, attributes = new Dictionary<string, string[]> { ["scope.customer"] = new[] { "*" } }
        });
        Assert.AreEqual(HttpStatusCode.OK, created, body.ToString());
        var id = body.GetProperty("userId").GetString();

        // Until the temporary password is changed, only profile/password endpoints work.
        var temp = await Login(name, "Temp-Password-77");
        Assert.AreEqual(HttpStatusCode.Forbidden, (await Send(HttpMethod.Get, "/api/admin/users", temp)).Status);
        var (changed, changeBody) = await Send(HttpMethod.Post, "/api/auth/change-password", temp,
            new { currentPassword = "Temp-Password-77", newPassword = "Brand-New-Secret-8" });
        Assert.AreEqual(HttpStatusCode.OK, changed, changeBody.ToString());
        Assert.AreEqual(HttpStatusCode.Unauthorized, (await Send(HttpMethod.Get, "/api/auth/me", temp)).Status, "old token is dead after a password change");
        var fresh = changeBody.GetProperty("accessToken").GetString();
        Assert.AreEqual(HttpStatusCode.OK, (await Send(HttpMethod.Get, "/api/auth/me", fresh)).Status);

        // Deactivating the account rejects its existing token.
        var (updated, _) = await Send(HttpMethod.Put, $"/api/admin/users/{id}", admin, new
        {
            displayName = "HTTP Test", isActive = false, roles = new[] { "Viewer" }, attributes = new Dictionary<string, string[]>()
        });
        Assert.AreEqual(HttpStatusCode.NoContent, updated);
        Assert.AreEqual(HttpStatusCode.Unauthorized, (await Send(HttpMethod.Get, "/api/auth/me", fresh)).Status);
        Assert.AreEqual(HttpStatusCode.Unauthorized, (await Send(HttpMethod.Post, "/api/auth/login", body: new { userName = name, password = "Brand-New-Secret-8" })).Status);
    }

    [TestMethod]
    public async Task DeleteUser_RemovesTheAccount_ButNeverYourself()
    {
        var admin = await Login("admin");
        var name = "del-" + Guid.NewGuid().ToString("N")[..8];
        var (_, created) = await Send(HttpMethod.Post, "/api/admin/users", admin, new
        {
            userName = name, displayName = "Delete Me", password = "Temp-Password-77", isActive = true,
            roles = new[] { "Viewer" }, attributes = new Dictionary<string, string[]> { ["scope.customer"] = new[] { "*" } }
        });
        var id = created.GetProperty("userId").GetString();
        var token = await Login(name, "Temp-Password-77");

        Assert.AreEqual(HttpStatusCode.Forbidden, (await Send(HttpMethod.Delete, $"/api/admin/users/{id}", await Login("viewer"))).Status, "needs admin.users.manage");
        Assert.AreEqual(HttpStatusCode.NoContent, (await Send(HttpMethod.Delete, $"/api/admin/users/{id}", admin)).Status);
        Assert.AreEqual(HttpStatusCode.Unauthorized, (await Send(HttpMethod.Get, "/api/auth/me", token)).Status, "deleted user's session ends");
        Assert.AreEqual(HttpStatusCode.Unauthorized, (await Send(HttpMethod.Post, "/api/auth/login", body: new { userName = name, password = "Temp-Password-77" })).Status);
        Assert.AreEqual(HttpStatusCode.NotFound, (await Send(HttpMethod.Delete, $"/api/admin/users/{id}", admin)).Status);

        var me = (await Send(HttpMethod.Get, "/api/auth/me", admin)).Body.GetProperty("userId").GetString();
        Assert.AreEqual(HttpStatusCode.BadRequest, (await Send(HttpMethod.Delete, $"/api/admin/users/{me}", admin)).Status);

        var audit = (await Send(HttpMethod.Get, "/api/admin/audit?top=20", admin)).Body;
        Assert.IsTrue(audit.EnumerateArray().Any(a => a.GetProperty("eventType").GetString() == "UserDeleted"
                                                     && (a.GetProperty("detail").GetString() ?? "").StartsWith(name)));
    }

    [TestMethod]
    public async Task EditingYourOwnAccount_ReturnsAFreshSession()
    {
        var admin = await Login("admin");
        var me = (await Send(HttpMethod.Get, "/api/auth/me", admin)).Body;
        var (status, body) = await Send(HttpMethod.Put, $"/api/admin/users/{me.GetProperty("userId").GetString()}", admin, new
        {
            displayName = "Avery Admin", email = "admin@ecco.local", isActive = true, roles = new[] { "Administrator" },
            attributes = new Dictionary<string, string[]>
            {
                ["scope.customer"] = new[] { "*" }, ["scope.deliveryMethod"] = new[] { "*" }, ["piiClearance"] = new[] { "true" }
            }
        });
        Assert.AreEqual(HttpStatusCode.OK, status, body.ToString());
        Assert.AreEqual(HttpStatusCode.Unauthorized, (await Send(HttpMethod.Get, "/api/auth/me", admin)).Status, "the old token still ends");
        Assert.AreEqual(HttpStatusCode.OK, (await Send(HttpMethod.Get, "/api/auth/me", body.GetProperty("accessToken").GetString())).Status);
    }

    [TestMethod]
    public async Task AdminGuards_SelfLockoutAndBrokenPolicies()
    {
        var admin = await Login("admin");
        var me = (await Send(HttpMethod.Get, "/api/auth/me", admin)).Body;
        var (selfDeactivate, msg) = await Send(HttpMethod.Put, $"/api/admin/users/{me.GetProperty("userId").GetString()}", admin, new
        {
            displayName = "Avery Admin", isActive = false, roles = new[] { "Administrator" }, attributes = new Dictionary<string, string[]>()
        });
        Assert.AreEqual(HttpStatusCode.BadRequest, selfDeactivate, msg.ToString());

        var policies = (await Send(HttpMethod.Get, "/api/admin/policies", admin)).Body;
        var id = policies.EnumerateArray().First().GetProperty("policyId").GetInt32();
        var (badPolicy, _) = await Send(HttpMethod.Put, $"/api/admin/policies/{id}", admin, new { isActive = true, conditionJson = "{\"all\":[]}" });
        Assert.AreEqual(HttpStatusCode.BadRequest, badPolicy);
    }
}
