using System.Collections.Concurrent;
using System.Net;
using System.Net.Http.Json;
using System.Security.Cryptography;
using System.Text.Json;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Data.SqlClient;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using ProcessRequestEndPoint.Auth;
using ProcessRequestEndPoint.Controllers;

namespace PortalAuthTests;

/// <summary>
/// "Forgot password?" end to end: request → emailed link (captured) → check → reset → sign in. Each test uses its own
/// throwaway account. Skipped unless ECCO_AUTH_TEST_CONNECTION is set (see AuthRepositoryIntegrationTests).
/// </summary>
[TestClass]
public class PasswordResetHttpTests
{
    private const string NewPassword = "Brand-New-Passw0rd!";
    private static WebApplication? _app;
    private static HttpClient? _client;
    private static string? _cs;
    private static readonly CapturingMailer Mailer = new();
    private static readonly List<Guid> Created = new();

    [ClassInitialize]
    public static async Task Start(TestContext _)
    {
        _cs = Environment.GetEnvironmentVariable("ECCO_AUTH_TEST_CONNECTION");
        if (string.IsNullOrWhiteSpace(_cs)) return;

        var builder = WebApplication.CreateBuilder(new WebApplicationOptions { EnvironmentName = "Testing" });
        builder.WebHost.UseTestServer();
        builder.Configuration.AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["ConnectionStrings:AZURE_SQL_CONNECTION"] = _cs,
            ["PortalAuth:SigningKey"] = Convert.ToBase64String(RandomNumberGenerator.GetBytes(32)),
            ["PortalAuth:AccessProfileCacheSeconds"] = "1",
            ["PortalAuth:PasswordIterations"] = "1000",
            ["PortalAuth:PasswordReset:PortalUrl"] = "https://portal.example.test/",
            ["PortalAuth:PasswordReset:MaxRequestsPerIpPerHour"] = "1000"
        });
        builder.Services.AddControllers().AddApplicationPart(typeof(AuthController).Assembly);
        builder.Services.AddAuthorization();
        builder.AddPortalAuth();
        builder.Services.AddSingleton<IPasswordResetMailer>(Mailer);

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
        if (_app == null) return;
        var repo = _app.Services.GetRequiredService<AuthRepository>();
        foreach (var id in Created) await repo.DeleteUserAsync(id);
        await _app.DisposeAsync();
    }

    private static HttpClient Client => _client ?? throw new AssertInconclusiveException("ECCO_AUTH_TEST_CONNECTION is not set.");

    private static async Task<(HttpStatusCode Status, JsonElement Body)> Post(string url, object body)
    {
        using var res = await Client.PostAsJsonAsync(url, body);
        var text = await res.Content.ReadAsStringAsync();
        return (res.StatusCode, string.IsNullOrWhiteSpace(text) ? default : JsonDocument.Parse(text).RootElement.Clone());
    }

    private static async Task<(string UserName, Guid Id)> NewUser(string? email = "auto", bool active = true)
    {
        _ = Client;
        var name = "reset-" + Guid.NewGuid().ToString("N")[..10];
        var repo = _app!.Services.GetRequiredService<AuthRepository>();
        var id = await repo.CreateUserAsync(name, "Reset Test", email == "auto" ? name + "@example.test" : email, active,
            PasswordHasher.Hash("Old-Passw0rd!-123", 1000), false, new[] { "Viewer" }, new Dictionary<string, List<string>>());
        lock (Created) Created.Add(id);
        return (name, id);
    }

    private static async Task<string> RequestLink(string userNameOrEmail)
    {
        var (status, body) = await Post("/api/auth/forgot-password", new { userNameOrEmail });
        Assert.AreEqual(HttpStatusCode.Accepted, status, body.ToString());
        Assert.AreEqual(AuthController.ResetRequestedMessage, body.GetProperty("message").GetString());
        return await Mailer.WaitForLink(userNameOrEmail);
    }

    private static string TokenOf(string link) => link[(link.IndexOf("token=", StringComparison.Ordinal) + 6)..];

    private static async Task<HttpStatusCode> Login(string user, string password) =>
        (await Post("/api/auth/login", new { userName = user, password })).Status;

    [TestMethod]
    public async Task Config_OffersPasswordReset()
    {
        using var res = await Client.GetAsync("/api/auth/config");
        var body = JsonDocument.Parse(await res.Content.ReadAsStringAsync()).RootElement;
        Assert.IsTrue(body.GetProperty("passwordReset").GetProperty("enabled").GetBoolean());
        Assert.AreEqual(30, body.GetProperty("passwordReset").GetProperty("linkLifetimeMinutes").GetInt32());
    }

    [TestMethod]
    public async Task FullFlow_ResetsPassword_AndTheLinkWorksOnce()
    {
        var (user, _) = await NewUser();
        var link = await RequestLink(user);
        StringAssert.StartsWith(link, "https://portal.example.test/#!/reset-password?token=");
        var token = TokenOf(link);

        var check = await Post("/api/auth/reset-password/check", new { token });
        Assert.AreEqual(HttpStatusCode.OK, check.Status);
        Assert.AreEqual(user, check.Body.GetProperty("userName").GetString());

        var weak = await Post("/api/auth/reset-password", new { token, newPassword = "short" });
        Assert.AreEqual(HttpStatusCode.BadRequest, weak.Status);
        Assert.IsTrue(weak.Body.GetProperty("problems").GetArrayLength() > 0);

        var done = await Post("/api/auth/reset-password", new { token, newPassword = NewPassword });
        Assert.AreEqual(HttpStatusCode.OK, done.Status, done.Body.ToString());

        Assert.AreEqual(HttpStatusCode.Unauthorized, await Login(user, "Old-Passw0rd!-123"));
        Assert.AreEqual(HttpStatusCode.OK, await Login(user, NewPassword));
        await Mailer.WaitFor(user, "changed");

        var again = await Post("/api/auth/reset-password", new { token, newPassword = "Another-Passw0rd!-9" });
        Assert.AreEqual(HttpStatusCode.BadRequest, again.Status);
        Assert.IsTrue(again.Body.GetProperty("linkInvalid").GetBoolean());
    }

    [TestMethod]
    public async Task FindsTheAccountByEmail()
    {
        var (user, _) = await NewUser();
        var link = await RequestLink(user + "@example.test");
        var check = await Post("/api/auth/reset-password/check", new { token = TokenOf(link) });
        Assert.AreEqual(user, check.Body.GetProperty("userName").GetString());
    }

    [TestMethod]
    public async Task UnknownSsoNoEmailInactiveAndSharedEmail_GetTheSameAnswer_AndNoEmail()
    {
        var (noEmail, _) = await NewUser(email: null);
        var (inactive, _) = await NewUser(active: false);
        var shared = "shared-" + Guid.NewGuid().ToString("N")[..8] + "@example.test";
        await NewUser(email: shared);
        await NewUser(email: shared);
        var sso = await ScalarAsync<string>("SELECT TOP 1 UserName FROM auth.AppUser WHERE ExternalProvider IS NOT NULL");

        foreach (var who in new[] { "nobody-" + Guid.NewGuid().ToString("N")[..8], noEmail, inactive, shared, sso ?? "nobody-sso" })
        {
            var (status, body) = await Post("/api/auth/forgot-password", new { userNameOrEmail = who });
            Assert.AreEqual(HttpStatusCode.Accepted, status, who);
            Assert.AreEqual(AuthController.ResetRequestedMessage, body.GetProperty("message").GetString(), who);
        }
        await Task.Delay(400);
        foreach (var who in new[] { noEmail, inactive, shared }) Assert.IsFalse(Mailer.SentTo(who), who);

        var reason = await ScalarAsync<string>(
            "SELECT TOP 1 Detail FROM auth.AuthAuditLog WHERE EventType = 'PasswordResetIgnored' AND UserName = @u ORDER BY AuditId DESC", ("@u", noEmail));
        Assert.AreEqual("Account has no email address", reason);
    }

    [TestMethod]
    public async Task ANewerLinkOrAPasswordChange_RetiresTheOlderLink()
    {
        var (user, id) = await NewUser();
        var first = TokenOf(await RequestLink(user));
        Mailer.Clear(user);
        var second = TokenOf(await RequestLink(user));

        Assert.AreEqual(HttpStatusCode.BadRequest, (await Post("/api/auth/reset-password/check", new { token = first })).Status);
        Assert.AreEqual(HttpStatusCode.OK, (await Post("/api/auth/reset-password/check", new { token = second })).Status);

        // An administrator resets the password: the stamp rotates and the outstanding link dies.
        await _app!.Services.GetRequiredService<AuthRepository>().SetPasswordAsync(id, PasswordHasher.Hash("Admin-Set-Passw0rd!", 1000), mustChange: true);
        Assert.AreEqual(HttpStatusCode.BadRequest, (await Post("/api/auth/reset-password", new { token = second, newPassword = NewPassword })).Status);
    }

    [TestMethod]
    public async Task ExpiredLink_IsRefused()
    {
        var (user, id) = await NewUser();
        var token = TokenOf(await RequestLink(user));
        await ExecAsync("UPDATE auth.PasswordResetToken SET ExpiresUtc = DATEADD(MINUTE, -1, SYSUTCDATETIME()) WHERE UserId = @id", ("@id", id));
        var res = await Post("/api/auth/reset-password/check", new { token });
        Assert.AreEqual(HttpStatusCode.BadRequest, res.Status);
        Assert.IsTrue(res.Body.GetProperty("linkInvalid").GetBoolean());
        Assert.AreEqual(HttpStatusCode.BadRequest, (await Post("/api/auth/reset-password/check", new { token = "not-a-real-token" })).Status);
    }

    [TestMethod]
    public async Task AccountIsRateLimited()
    {
        var (user, _) = await NewUser();
        for (var i = 0; i < 3; i++) { await RequestLink(user); Mailer.Clear(user); }
        var (status, _) = await Post("/api/auth/forgot-password", new { userNameOrEmail = user });
        Assert.AreEqual(HttpStatusCode.Accepted, status);
        await Task.Delay(400);
        Assert.IsFalse(Mailer.SentTo(user), "a 4th email within the hour must not be sent");
    }

    [TestMethod]
    public async Task Reset_UnlocksALockedAccount()
    {
        var (user, _) = await NewUser();
        for (var i = 0; i < 5; i++) await Login(user, "wrong-password-" + i);
        Assert.AreEqual(HttpStatusCode.Unauthorized, await Login(user, "Old-Passw0rd!-123"), "locked");

        var token = TokenOf(await RequestLink(user));
        Assert.AreEqual(HttpStatusCode.OK, (await Post("/api/auth/reset-password", new { token, newPassword = NewPassword })).Status);
        Assert.AreEqual(HttpStatusCode.OK, await Login(user, NewPassword));
    }

    [TestMethod]
    public void MaskEmail_HidesMostOfTheAddress()
    {
        Assert.AreEqual("j******@example.com", PasswordResetService.MaskEmail("jsmith.long@example.com"));
        Assert.AreEqual("a*@x.io", PasswordResetService.MaskEmail("a@x.io"));
    }

    private static async Task<T?> ScalarAsync<T>(string sql, params (string Name, object Value)[] args)
    {
        await using var conn = new SqlConnection(_cs);
        await conn.OpenAsync();
        await using var cmd = new SqlCommand(sql, conn);
        foreach (var (n, v) in args) cmd.Parameters.AddWithValue(n, v);
        var value = await cmd.ExecuteScalarAsync();
        return value is null or DBNull ? default : (T)value;
    }

    private static async Task ExecAsync(string sql, params (string Name, object Value)[] args)
    {
        await using var conn = new SqlConnection(_cs);
        await conn.OpenAsync();
        await using var cmd = new SqlCommand(sql, conn);
        foreach (var (n, v) in args) cmd.Parameters.AddWithValue(n, v);
        await cmd.ExecuteNonQueryAsync();
    }

    /// <summary>Records the emails instead of sending them, keyed by user name and email address.</summary>
    private sealed class CapturingMailer : IPasswordResetMailer
    {
        private readonly ConcurrentDictionary<string, ConcurrentQueue<(string Kind, string? Link)>> _sent = new(StringComparer.OrdinalIgnoreCase);

        public bool CanSend => true;

        public Task SendResetLinkAsync(PasswordResetCandidate user, string link, DateTime expiresUtc, CancellationToken ct) => Add(user, "link", link);
        public Task SendPasswordChangedAsync(PasswordResetCandidate user, string? ip, CancellationToken ct) => Add(user, "changed", null);

        private Task Add(PasswordResetCandidate user, string kind, string? link)
        {
            foreach (var key in new[] { user.UserName, user.Email }.Where(k => k != null))
                _sent.GetOrAdd(key!, _ => new()).Enqueue((kind, link));
            return Task.CompletedTask;
        }

        public bool SentTo(string key) => _sent.TryGetValue(key, out var q) && !q.IsEmpty;
        public void Clear(string key) => _sent.TryRemove(key, out _);

        public async Task<string> WaitForLink(string key) => (await WaitFor(key, "link")).Link!;

        public async Task<(string Kind, string? Link)> WaitFor(string key, string kind)
        {
            for (var i = 0; i < 50; i++)
            {
                if (_sent.TryGetValue(key, out var q))
                {
                    var hit = q.LastOrDefault(m => m.Kind == kind);
                    if (hit.Kind != null) return hit;
                }
                await Task.Delay(50);
            }
            throw new AssertFailedException($"No '{kind}' email was sent to {key}.");
        }
    }
}
