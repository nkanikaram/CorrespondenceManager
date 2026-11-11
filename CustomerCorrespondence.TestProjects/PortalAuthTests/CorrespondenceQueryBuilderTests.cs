using System.Text;
using System.Text.Json;
using Microsoft.Data.SqlClient;
using ProcessRequestEndPoint.Auth;

namespace PortalAuthTests;

[TestClass]
public class CorrespondenceQueryBuilderTests
{
    private static (string Where, List<SqlParameter> Parameters) Filter(string json)
    {
        var where = new StringBuilder("WHERE 1=1");
        var ps = new List<SqlParameter>();
        using var doc = JsonDocument.Parse(json);
        CorrespondenceQueryBuilder.AppendFilter(where, ps, doc.RootElement.Clone());
        return (where.ToString(), ps);
    }

    [TestMethod]
    public void TextOperators_AreParameterizedAndEscapeLikeWildcards()
    {
        var (where, ps) = Filter("""{"logic":"and","filters":[{"field":"customer","operator":"contains","value":"50%_off[x]"}]}""");
        Assert.AreEqual("WHERE 1=1 AND (cr.RequestingDomainName LIKE @gf0 ESCAPE '\\')", where);
        Assert.AreEqual(@"%50\%\_off\[x]%", ps[0].Value);
    }

    [TestMethod]
    public void NestedOrGroups_AndNullHandlingMatchTheGrid()
    {
        var (where, ps) = Filter("""
            {"logic":"and","filters":[
              {"logic":"or","filters":[{"field":"status","operator":"eq","value":"Failed"},{"field":"status","operator":"eq","value":"New"}]},
              {"field":"docId","operator":"neq","value":"D-1"},
              {"field":"claimNumber","operator":"isnotempty"}
            ]}
            """);
        StringAssert.Contains(where, "((COALESCE(sc.StatusDescription, ls.LatestStatus) = @gf0 OR COALESCE(sc.StatusDescription, ls.LatestStatus) = @gf1)");
        StringAssert.Contains(where, "(cr.DocId IS NULL OR cr.DocId <> @gf2)");
        StringAssert.Contains(where, "(cr.ClaimNumber IS NULL OR cr.ClaimNumber <> '')");
        Assert.AreEqual(3, ps.Count);
    }

    [TestMethod]
    public void Dates_CompareByCalendarDay()
    {
        var (eq, eqPs) = Filter("""{"logic":"and","filters":[{"field":"created","operator":"eq","value":"2026-11-03"}]}""");
        Assert.AreEqual("WHERE 1=1 AND ((cr.RequestCreationTS >= @gf0 AND cr.RequestCreationTS < @gf1))", eq);
        Assert.AreEqual(new DateTime(2026, 11, 3), eqPs[0].Value);
        Assert.AreEqual(new DateTime(2026, 11, 4), eqPs[1].Value);

        var (gt, gtPs) = Filter("""{"logic":"and","filters":[{"field":"created","operator":"gt","value":"2026-11-03"}]}""");
        Assert.AreEqual("WHERE 1=1 AND (cr.RequestCreationTS >= @gf0)", gt);
        Assert.AreEqual(new DateTime(2026, 11, 4), gtPs[0].Value);
    }

    [TestMethod]
    public void UnknownFieldsOperatorsAndBadValuesAreRejected()
    {
        Assert.ThrowsException<FilterValidationException>(() => Filter("""{"filters":[{"field":"PolicyNumber","operator":"eq","value":"1"}]}"""));
        Assert.ThrowsException<FilterValidationException>(() => Filter("""{"filters":[{"field":"docId","operator":"; DROP TABLE x","value":"1"}]}"""));
        Assert.ThrowsException<FilterValidationException>(() => Filter("""{"filters":[{"field":"created","operator":"eq","value":"yesterday"}]}"""));
        Assert.ThrowsException<FilterValidationException>(() => Filter("""{"filters":[{"field":"docId","operator":"contains"}]}"""));
    }

    [TestMethod]
    public void EmptyFilters_AddNothing()
    {
        Assert.AreEqual("WHERE 1=1", Filter("""{"logic":"and","filters":[]}""").Where);
        var where = new StringBuilder("WHERE 1=1");
        CorrespondenceQueryBuilder.AppendFilter(where, new List<SqlParameter>(), null);
        Assert.AreEqual("WHERE 1=1", where.ToString());
    }

    [TestMethod]
    public void FreeText_SeveralTermsMatchAnyOfThem()
    {
        var where = new StringBuilder("WHERE 1=1");
        var ps = new List<SqlParameter>();
        var id = Guid.NewGuid();
        CorrespondenceQueryBuilder.AppendFreeText(where, ps, new[] { "DOC-1", " doc-1 ", "", "50%", id.ToString() });

        var sql = where.ToString();
        StringAssert.StartsWith(sql, "WHERE 1=1 AND ((ISNULL(cr.DocId,'') LIKE @ft0 ESCAPE '\\'");
        StringAssert.Contains(sql, ") OR (ISNULL(cr.DocId,'') LIKE @ft1");
        StringAssert.Contains(sql, "cr.CorrespondenceId = @ftId2");
        StringAssert.Contains(sql, "CONVERT(varchar(36), cr.CorrespondenceId) LIKE @ft0");
        // Repeats and blanks are dropped; LIKE wildcards are literal.
        CollectionAssert.AreEqual(new object[] { "%DOC-1%", @"%50\%%", "%" + id + "%", id },
            ps.Select(p => p.Value).ToArray());
    }

    [TestMethod]
    public void FreeText_NothingOrTooMany()
    {
        var where = new StringBuilder("WHERE 1=1");
        CorrespondenceQueryBuilder.AppendFreeText(where, new List<SqlParameter>(), new string?[] { null, " " });
        Assert.AreEqual("WHERE 1=1", where.ToString());
        var tooMany = Enumerable.Range(0, CorrespondenceQueryBuilder.MaxFreeTerms + 1).Select(i => "D-" + i);
        Assert.ThrowsException<FilterValidationException>(() =>
            CorrespondenceQueryBuilder.AppendFreeText(new StringBuilder(), new List<SqlParameter>(), tooMany));
    }

    [TestMethod]
    public void Scope_WildcardRestrictedAndMissing()
    {
        UserAccessProfile User(Dictionary<string, string[]> a) => new()
        {
            IsActive = true,
            Attributes = a.ToDictionary(kv => kv.Key, kv => (IReadOnlyList<string>)kv.Value, StringComparer.OrdinalIgnoreCase)
        };

        var where = new StringBuilder("WHERE 1=1");
        var ps = new List<SqlParameter>();
        CorrespondenceQueryBuilder.AppendScope(where, ps, User(new() { ["scope.customer"] = new[] { "*" }, ["scope.deliveryMethod"] = new[] { "*" } }));
        Assert.AreEqual("WHERE 1=1", where.ToString());

        where = new StringBuilder("WHERE 1=1");
        ps.Clear();
        CorrespondenceQueryBuilder.AppendScope(where, ps, User(new() { ["scope.customer"] = new[] { "A", "B" }, ["scope.deliveryMethod"] = new[] { "*" } }));
        Assert.AreEqual("WHERE 1=1 AND cr.RequestingDomainName IN (@scopeCust0,@scopeCust1)", where.ToString());

        where = new StringBuilder("WHERE 1=1");
        CorrespondenceQueryBuilder.AppendScope(where, new List<SqlParameter>(), User(new() { ["scope.deliveryMethod"] = new[] { "Email" } }));
        StringAssert.StartsWith(where.ToString(), "WHERE 1=1 AND 1 = 0", "no customer scope = no rows");
    }

    [TestMethod]
    public void SortExpression_AcceptsGridKeysAndBackendNames()
    {
        Assert.AreEqual("cr.ClaimNumber", CorrespondenceQueryBuilder.SortExpression("claimNumber"));
        Assert.AreEqual("cr.RequestCreationTS", CorrespondenceQueryBuilder.SortExpression("requestCreationTS"));
        Assert.AreEqual(CorrespondenceQueryBuilder.StatusExpression, CorrespondenceQueryBuilder.SortExpression("status"));
        Assert.AreEqual("cr.RequestCreationTS", CorrespondenceQueryBuilder.SortExpression("1;DROP TABLE x"));
    }
}

[TestClass]
public class PasswordHasherTests
{
    [TestMethod]
    public void HashAndVerify()
    {
        var hash = PasswordHasher.Hash("Correct-Horse-9", 1000);
        Assert.IsTrue(PasswordHasher.Verify("Correct-Horse-9", hash.Hash, hash.Salt, hash.Iterations));
        Assert.IsFalse(PasswordHasher.Verify("correct-horse-9", hash.Hash, hash.Salt, hash.Iterations));
        Assert.AreNotEqual(Convert.ToBase64String(hash.Salt), Convert.ToBase64String(PasswordHasher.Hash("Correct-Horse-9", 1000).Salt));
    }

    [TestMethod]
    public void MatchesTheDevSeedScriptHashes()
    {
        // admin row of Build/DBScripts/Auth/003_PortalAuth_DevUsers.sql (PBKDF2-SHA256, 600k iterations).
        var hash = Convert.FromHexString("26BCD51604D3248DF6A1866DC96DC4A1FD155429AEA112875CFEDC876C8C0CE9");
        var salt = Convert.FromHexString("1277086C287803A151F419166F5729E6");
        Assert.IsTrue(PasswordHasher.Verify("EccoDev!2026", hash, salt, 600_000));
    }

    [TestMethod]
    public void Validate_EnforcesThePolicy()
    {
        Assert.AreEqual(0, PasswordHasher.Validate("Blue-Lantern-42", "olivia", 12).Count);
        Assert.IsTrue(PasswordHasher.Validate("short1A!", "olivia", 12).Count > 0);
        Assert.IsTrue(PasswordHasher.Validate("alllowercaseletters", "olivia", 12).Count > 0);
        Assert.IsTrue(PasswordHasher.Validate("Olivia-Password-1", "olivia", 12).Count > 0);
    }
}
