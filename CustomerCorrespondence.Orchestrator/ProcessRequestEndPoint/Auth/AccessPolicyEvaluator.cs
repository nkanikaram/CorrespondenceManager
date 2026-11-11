using System.Globalization;
using System.Text.Json;

namespace ProcessRequestEndPoint.Auth
{
    public sealed record PolicyDecision(bool Allowed, string? PolicyName, string? Message)
    {
        public static readonly PolicyDecision Allow = new(true, null, null);
    }

    /// <summary>
    /// RBAC then ABAC: the subject must hold the permission through a role, and no active Deny
    /// policy for that permission may match the subject, resource and environment attributes.
    ///
    /// Condition grammar (auth.AccessPolicy.ConditionJson):
    ///   { "all": [clause, ...] } | { "any": [clause, ...] }
    ///   clause: { "attr": "resource.status", "op": "notIn", "values": ["Failed"] }
    ///           { "attr": "resource.customer", "op": "notIn", "valuesFrom": "subject.scope.customer" }
    ///   attr:   subject.&lt;attribute key&gt; | resource.&lt;name&gt; | env.hourUtc | env.dayOfWeek
    ///   ops:    eq, neq, in, notIn, exists, notExists, gt, gte, lt, lte (case-insensitive text; numeric for gt..lte)
    ///
    /// Semantics chosen to fail closed: a missing value never satisfies eq/in but always satisfies
    /// neq/notIn, so "resource.customer notIn subject.scope.customer" denies when either side is
    /// missing. A '*' among the compared values matches anything. A policy whose JSON cannot be
    /// parsed denies.
    /// </summary>
    public static class AccessPolicyEvaluator
    {
        public static PolicyDecision Evaluate(
            string permission,
            UserAccessProfile subject,
            IReadOnlyDictionary<string, string?> resource,
            IEnumerable<AccessPolicyRecord> policies,
            DateTime nowUtc)
        {
            if (!subject.IsActive || !subject.Has(permission))
                return new PolicyDecision(false, null, "You don't have permission to do this.");

            foreach (var policy in policies
                         .Where(p => p.IsActive && string.Equals(p.PermissionKey, permission, StringComparison.OrdinalIgnoreCase))
                         .OrderBy(p => p.Priority))
            {
                bool matches;
                try
                {
                    using var doc = JsonDocument.Parse(policy.ConditionJson);
                    matches = MatchesCondition(doc.RootElement, subject, resource, nowUtc);
                }
                catch (Exception ex) when (ex is JsonException or InvalidOperationException or FormatException)
                {
                    return new PolicyDecision(false, policy.PolicyName, $"Access policy '{policy.PolicyName}' is misconfigured.");
                }

                if (matches)
                    return new PolicyDecision(false, policy.PolicyName, policy.DenyMessage ?? $"Denied by policy '{policy.PolicyName}'.");
            }

            return PolicyDecision.Allow;
        }

        /// <summary>Validates a condition's shape without evaluating it (used before saving an edited policy).</summary>
        public static string? ValidateCondition(string json)
        {
            try
            {
                using var doc = JsonDocument.Parse(json);
                var probe = new UserAccessProfile { IsActive = true };
                MatchesCondition(doc.RootElement, probe, new Dictionary<string, string?>(), DateTime.UtcNow);
                return null;
            }
            catch (Exception ex) when (ex is JsonException or InvalidOperationException or FormatException)
            {
                return ex.Message;
            }
        }

        private static bool MatchesCondition(JsonElement node, UserAccessProfile subject,
            IReadOnlyDictionary<string, string?> resource, DateTime nowUtc)
        {
            if (node.ValueKind != JsonValueKind.Object)
                throw new InvalidOperationException("A condition must be a JSON object.");

            if (node.TryGetProperty("all", out var all))
                return Clauses(all).All(c => MatchesCondition(c, subject, resource, nowUtc));
            if (node.TryGetProperty("any", out var any))
                return Clauses(any).Any(c => MatchesCondition(c, subject, resource, nowUtc));
            if (node.TryGetProperty("attr", out _))
                return MatchesClause(node, subject, resource, nowUtc);

            throw new InvalidOperationException("A condition needs 'all', 'any' or 'attr'.");
        }

        private static IEnumerable<JsonElement> Clauses(JsonElement array)
        {
            if (array.ValueKind != JsonValueKind.Array || array.GetArrayLength() == 0)
                throw new InvalidOperationException("'all' and 'any' must be non-empty arrays.");
            return array.EnumerateArray().ToList();
        }

        private static bool MatchesClause(JsonElement clause, UserAccessProfile subject,
            IReadOnlyDictionary<string, string?> resource, DateTime nowUtc)
        {
            var attr = RequiredString(clause, "attr");
            var op = RequiredString(clause, "op");
            var actual = Resolve(attr, subject, resource, nowUtc);

            IReadOnlyList<string> expected;
            if (clause.TryGetProperty("values", out var valuesEl))
            {
                if (valuesEl.ValueKind != JsonValueKind.Array) throw new InvalidOperationException("'values' must be an array.");
                expected = valuesEl.EnumerateArray().Select(v => v.ValueKind == JsonValueKind.String ? v.GetString()! : v.GetRawText()).ToList();
            }
            else if (clause.TryGetProperty("valuesFrom", out var fromEl) && fromEl.ValueKind == JsonValueKind.String)
            {
                expected = Resolve(fromEl.GetString()!, subject, resource, nowUtc);
            }
            else
            {
                expected = Array.Empty<string>();
            }

            switch (op)
            {
                case "exists": return actual.Count > 0;
                case "notExists": return actual.Count == 0;
                case "eq":
                case "in": return AnyIn(actual, expected);
                case "neq":
                case "notIn": return !AnyIn(actual, expected);
                case "gt": return Compare(actual, expected, c => c > 0);
                case "gte": return Compare(actual, expected, c => c >= 0);
                case "lt": return Compare(actual, expected, c => c < 0);
                case "lte": return Compare(actual, expected, c => c <= 0);
                default: throw new InvalidOperationException($"Unknown operator '{op}'.");
            }
        }

        private static bool AnyIn(IReadOnlyList<string> actual, IReadOnlyList<string> expected)
        {
            if (actual.Count == 0) return false;
            if (expected.Any(e => e == SubjectAttributes.Wildcard)) return true;
            return actual.Any(a => expected.Any(e => string.Equals(a, e, StringComparison.OrdinalIgnoreCase)));
        }

        private static bool Compare(IReadOnlyList<string> actual, IReadOnlyList<string> expected, Func<int, bool> test)
        {
            if (actual.Count == 0 || expected.Count == 0) return false;
            if (!decimal.TryParse(actual[0], NumberStyles.Number, CultureInfo.InvariantCulture, out var a)) return false;
            if (!decimal.TryParse(expected[0], NumberStyles.Number, CultureInfo.InvariantCulture, out var e)) return false;
            return test(a.CompareTo(e));
        }

        private static IReadOnlyList<string> Resolve(string attr, UserAccessProfile subject,
            IReadOnlyDictionary<string, string?> resource, DateTime nowUtc)
        {
            if (attr.StartsWith("subject.", StringComparison.Ordinal))
                return subject.AttributeValues(attr.Substring("subject.".Length));

            if (attr.StartsWith("resource.", StringComparison.Ordinal))
            {
                return resource.TryGetValue(attr.Substring("resource.".Length), out var value) && !string.IsNullOrWhiteSpace(value)
                    ? new[] { value! }
                    : Array.Empty<string>();
            }

            return attr switch
            {
                "env.hourUtc" => new[] { nowUtc.Hour.ToString(CultureInfo.InvariantCulture) },
                "env.dayOfWeek" => new[] { nowUtc.DayOfWeek.ToString() },
                _ => throw new InvalidOperationException($"Unknown attribute '{attr}'. Use subject.*, resource.* or env.*.")
            };
        }

        private static string RequiredString(JsonElement el, string name)
        {
            if (!el.TryGetProperty(name, out var v) || v.ValueKind != JsonValueKind.String || string.IsNullOrWhiteSpace(v.GetString()))
                throw new InvalidOperationException($"Each clause needs a '{name}' string.");
            return v.GetString()!;
        }
    }
}
