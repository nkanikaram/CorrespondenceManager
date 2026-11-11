using ProcessRequestEndPoint.Auth;

namespace PortalAuthTests;

[TestClass]
public class AccessPolicyEvaluatorTests
{
    private static readonly DateTime Noon = new(2026, 11, 3, 12, 0, 0, DateTimeKind.Utc);

    private static UserAccessProfile User(string[] permissions, Dictionary<string, string[]>? attributes = null, bool active = true) => new()
    {
        UserId = Guid.NewGuid(),
        UserName = "u",
        IsActive = active,
        Permissions = new HashSet<string>(permissions, StringComparer.OrdinalIgnoreCase),
        Attributes = (attributes ?? new()).ToDictionary(kv => kv.Key, kv => (IReadOnlyList<string>)kv.Value, StringComparer.OrdinalIgnoreCase)
    };

    private static AccessPolicyRecord Policy(string permission, string condition, bool active = true, int priority = 10, string name = "p") => new()
    {
        PolicyName = name, PermissionKey = permission, ConditionJson = condition, DenyMessage = name + " denied", IsActive = active, Priority = priority
    };

    private static Dictionary<string, string?> Resource(params (string Key, string? Value)[] pairs) =>
        pairs.ToDictionary(p => p.Key, p => p.Value, StringComparer.OrdinalIgnoreCase);

    // The seeded policies (Build/DBScripts/Auth/002_PortalAuth_ReferenceData.sql).
    private const string ResubmitFailedOnly = """{"all":[{"attr":"resource.status","op":"notIn","values":["Failed"]}]}""";
    private const string WithinCustomerScope = """{"all":[{"attr":"resource.customer","op":"notIn","valuesFrom":"subject.scope.customer"}]}""";
    private const string PiiClearance = """{"all":[{"attr":"subject.piiClearance","op":"neq","values":["true"]}]}""";
    private const string ExportLimit = """{"all":[{"attr":"resource.rowCount","op":"gt","valuesFrom":"subject.maxExportRows"}]}""";
    private const string BusinessHours = """{"any":[{"attr":"env.hourUtc","op":"lt","values":["12"]},{"attr":"env.hourUtc","op":"gte","values":["22"]}]}""";

    [TestMethod]
    public void Rbac_DeniesWithoutPermission_AndForInactiveUsers()
    {
        var none = AccessPolicyEvaluator.Evaluate("correspondence.read", User(Array.Empty<string>()), Resource(), Array.Empty<AccessPolicyRecord>(), Noon);
        Assert.IsFalse(none.Allowed);
        Assert.IsNull(none.PolicyName);

        var inactive = AccessPolicyEvaluator.Evaluate("correspondence.read", User(new[] { "correspondence.read" }, active: false), Resource(), Array.Empty<AccessPolicyRecord>(), Noon);
        Assert.IsFalse(inactive.Allowed);

        var granted = AccessPolicyEvaluator.Evaluate("correspondence.read", User(new[] { "CORRESPONDENCE.READ" }), Resource(), Array.Empty<AccessPolicyRecord>(), Noon);
        Assert.IsTrue(granted.Allowed);
    }

    [TestMethod]
    public void ResubmitFailedOnly_AllowsFailed_DeniesOthersAndMissingStatus()
    {
        var user = User(new[] { "correspondence.resubmit" });
        var policies = new[] { Policy("correspondence.resubmit", ResubmitFailedOnly) };
        Assert.IsTrue(AccessPolicyEvaluator.Evaluate("correspondence.resubmit", user, Resource(("status", "failed")), policies, Noon).Allowed);
        var denied = AccessPolicyEvaluator.Evaluate("correspondence.resubmit", user, Resource(("status", "Completed")), policies, Noon);
        Assert.IsFalse(denied.Allowed);
        Assert.AreEqual("p denied", denied.Message);
        Assert.IsFalse(AccessPolicyEvaluator.Evaluate("correspondence.resubmit", user, Resource(), policies, Noon).Allowed, "missing status fails closed");
    }

    [TestMethod]
    public void CustomerScope_WildcardListAndMissingAttribute()
    {
        var policies = new[] { Policy("correspondence.create", WithinCustomerScope) };
        var scoped = User(new[] { "correspondence.create" }, new() { ["scope.customer"] = new[] { "Life Services", "Claims System" } });
        var all = User(new[] { "correspondence.create" }, new() { ["scope.customer"] = new[] { "*" } });
        var noScope = User(new[] { "correspondence.create" });

        Assert.IsTrue(AccessPolicyEvaluator.Evaluate("correspondence.create", scoped, Resource(("customer", "life services")), policies, Noon).Allowed);
        Assert.IsFalse(AccessPolicyEvaluator.Evaluate("correspondence.create", scoped, Resource(("customer", "Annuity")), policies, Noon).Allowed);
        Assert.IsTrue(AccessPolicyEvaluator.Evaluate("correspondence.create", all, Resource(("customer", "Anything")), policies, Noon).Allowed);
        Assert.IsFalse(AccessPolicyEvaluator.Evaluate("correspondence.create", noScope, Resource(("customer", "Life Services")), policies, Noon).Allowed);
        Assert.IsFalse(AccessPolicyEvaluator.Evaluate("correspondence.create", all, Resource(), policies, Noon).Allowed, "missing customer fails closed");
    }

    [TestMethod]
    public void PiiClearance_RequiresTrue()
    {
        var policies = new[] { Policy("correspondence.payload.read", PiiClearance) };
        var cleared = User(new[] { "correspondence.payload.read" }, new() { ["piiClearance"] = new[] { "true" } });
        var notCleared = User(new[] { "correspondence.payload.read" }, new() { ["piiClearance"] = new[] { "false" } });
        var missing = User(new[] { "correspondence.payload.read" });
        Assert.IsTrue(AccessPolicyEvaluator.Evaluate("correspondence.payload.read", cleared, Resource(), policies, Noon).Allowed);
        Assert.IsFalse(AccessPolicyEvaluator.Evaluate("correspondence.payload.read", notCleared, Resource(), policies, Noon).Allowed);
        Assert.IsFalse(AccessPolicyEvaluator.Evaluate("correspondence.payload.read", missing, Resource(), policies, Noon).Allowed);
    }

    [TestMethod]
    public void ExportLimit_OnlyAppliesWhenTheAttributeIsSet()
    {
        var policies = new[] { Policy("correspondence.export", ExportLimit) };
        var limited = User(new[] { "correspondence.export" }, new() { ["maxExportRows"] = new[] { "100" } });
        var unlimited = User(new[] { "correspondence.export" });
        Assert.IsTrue(AccessPolicyEvaluator.Evaluate("correspondence.export", limited, Resource(("rowCount", "100")), policies, Noon).Allowed);
        Assert.IsFalse(AccessPolicyEvaluator.Evaluate("correspondence.export", limited, Resource(("rowCount", "101")), policies, Noon).Allowed);
        Assert.IsTrue(AccessPolicyEvaluator.Evaluate("correspondence.export", unlimited, Resource(("rowCount", "100000")), policies, Noon).Allowed);
    }

    [TestMethod]
    public void EnvironmentRule_InactivePoliciesAndOtherPermissionsAreIgnored()
    {
        var user = User(new[] { "correspondence.resubmit", "correspondence.read" });
        var active = new[] { Policy("correspondence.resubmit", BusinessHours) };
        Assert.IsTrue(AccessPolicyEvaluator.Evaluate("correspondence.resubmit", user, Resource(), active, Noon).Allowed);
        Assert.IsFalse(AccessPolicyEvaluator.Evaluate("correspondence.resubmit", user, Resource(), active, Noon.AddHours(-3)).Allowed);
        Assert.IsTrue(AccessPolicyEvaluator.Evaluate("correspondence.resubmit", user, Resource(), new[] { Policy("correspondence.resubmit", BusinessHours, active: false) }, Noon.AddHours(-3)).Allowed);
        Assert.IsTrue(AccessPolicyEvaluator.Evaluate("correspondence.read", user, Resource(), active, Noon.AddHours(-3)).Allowed);
    }

    [TestMethod]
    public void PoliciesRunInPriorityOrder_AndMalformedJsonFailsClosed()
    {
        var user = User(new[] { "correspondence.resubmit" });
        var policies = new[]
        {
            Policy("correspondence.resubmit", ResubmitFailedOnly, priority: 20, name: "second"),
            Policy("correspondence.resubmit", ResubmitFailedOnly, priority: 5, name: "first")
        };
        Assert.AreEqual("first", AccessPolicyEvaluator.Evaluate("correspondence.resubmit", user, Resource(("status", "New")), policies, Noon).PolicyName);

        var broken = AccessPolicyEvaluator.Evaluate("correspondence.resubmit", user, Resource(("status", "Failed")),
            new[] { Policy("correspondence.resubmit", """{"all":[{"attr":"resource.status","op":"between"}]}""") }, Noon);
        Assert.IsFalse(broken.Allowed);
        StringAssert.Contains(broken.Message, "misconfigured");
    }

    [TestMethod]
    public void ValidateCondition_ReportsProblems()
    {
        Assert.IsNull(AccessPolicyEvaluator.ValidateCondition(ResubmitFailedOnly));
        Assert.IsNull(AccessPolicyEvaluator.ValidateCondition(BusinessHours));
        Assert.IsNotNull(AccessPolicyEvaluator.ValidateCondition("not json"));
        Assert.IsNotNull(AccessPolicyEvaluator.ValidateCondition("""{"all":[]}"""));
        Assert.IsNotNull(AccessPolicyEvaluator.ValidateCondition("""{"all":[{"attr":"user.name","op":"eq","values":["x"]}]}"""));
    }
}
