using System.Data;
using System.Globalization;
using System.Text;
using System.Text.Json;
using Microsoft.Data.SqlClient;

namespace ProcessRequestEndPoint.Auth
{
    public sealed class FilterValidationException : Exception
    {
        public FilterValidationException(string message) : base(message) { }
    }

    /// <summary>
    /// SQL fragments for the correspondence search: ABAC data scope, the grid's Kendo-style filter
    /// descriptors, and sort columns. Every value is a parameter; field names and operators come
    /// from fixed allow-lists, so client input never becomes SQL text.
    ///
    /// Expects the aliases used by CorrespondenceSearchController: cr (CorrespondenceRequest),
    /// ls (latest status OUTER APPLY), sc (CorrespondenceStatusCode), dm (CorrespondenceDeliveryMethod).
    /// </summary>
    public static class CorrespondenceQueryBuilder
    {
        public const string DeliveryExpression = "COALESCE(dm.DeliveryMethodName, cr.DeliveryMethodCode)";
        public const string StatusExpression = "COALESCE(sc.StatusDescription, ls.LatestStatus)";

        private const int MaxDepth = 4;
        private const int MaxConditions = 50;
        public const int MaxFreeTerms = 100;

        private enum FieldKind { Text, Date, Sla }

        private static readonly Dictionary<string, (string Sql, FieldKind Kind)> Fields = new(StringComparer.OrdinalIgnoreCase)
        {
            ["id"] = ("CONVERT(varchar(36), cr.CorrespondenceId)", FieldKind.Text),
            ["docId"] = ("cr.DocId", FieldKind.Text),
            ["claimNumber"] = ("cr.ClaimNumber", FieldKind.Text),
            ["customer"] = ("cr.RequestingDomainName", FieldKind.Text),
            ["delivery"] = (DeliveryExpression, FieldKind.Text),
            ["created"] = ("cr.RequestCreationTS", FieldKind.Date),
            ["status"] = (StatusExpression, FieldKind.Text),
            // SLA position: Breached | At risk | On track | Finished, from Administration > SLA.
            ["sla"] = ("cr.RequestCreationTS", FieldKind.Sla)
        };

        private static readonly Dictionary<string, string> SortColumns = new(StringComparer.OrdinalIgnoreCase)
        {
            ["id"] = "cr.CorrespondenceId", ["CorrespondenceId"] = "cr.CorrespondenceId",
            ["docId"] = "cr.DocId",
            ["claimNumber"] = "cr.ClaimNumber",
            ["customer"] = "cr.RequestingDomainName", ["RequestingDomainName"] = "cr.RequestingDomainName",
            ["delivery"] = DeliveryExpression, ["DeliveryMethodCode"] = DeliveryExpression,
            ["created"] = "cr.RequestCreationTS", ["RequestCreationTS"] = "cr.RequestCreationTS",
            ["status"] = StatusExpression, ["latestStatus"] = StatusExpression
        };

        /// <summary>ORDER BY expression for a grid column or backend field name; defaults to creation time.</summary>
        public static string SortExpression(string? sortBy) =>
            !string.IsNullOrWhiteSpace(sortBy) && SortColumns.TryGetValue(sortBy, out var sql) ? sql : "cr.RequestCreationTS";

        /// <summary>
        /// Restricts rows to the user's scope.customer / scope.deliveryMethod attributes ('*' = all).
        /// A user with no value for a scope attribute sees no rows.
        /// </summary>
        public static void AppendScope(StringBuilder where, List<SqlParameter> parameters, UserAccessProfile user)
        {
            AppendIn(where, parameters, "cr.RequestingDomainName", user.ScopeValues(SubjectAttributes.CustomerScope), "@scopeCust");
            AppendIn(where, parameters, DeliveryExpression, user.ScopeValues(SubjectAttributes.DeliveryMethodScope), "@scopeDm");
        }

        private static void AppendIn(StringBuilder where, List<SqlParameter> parameters, string expression, IReadOnlyList<string>? values, string prefix)
        {
            if (values == null) return; // unrestricted
            if (values.Count == 0)
            {
                where.Append(" AND 1 = 0");
                return;
            }
            var names = new List<string>();
            for (var i = 0; i < values.Count; i++)
            {
                var name = prefix + i;
                parameters.Add(new SqlParameter(name, SqlDbType.NVarChar, 200) { Value = values[i] });
                names.Add(name);
            }
            where.Append($" AND {expression} IN ({string.Join(",", names)})");
        }

        /// <summary>
        /// Appends " AND (&lt;filter&gt;)" for a composite filter descriptor; no-op for null/empty filters.
        /// <paramref name="sla"/> (null when the SLA isn't set up) is needed only for the "sla" field.
        /// </summary>
        public static void AppendFilter(StringBuilder where, List<SqlParameter> parameters, JsonElement? filter,
            Sla.SlaConfig? sla = null, DateTime? nowUtc = null)
        {
            if (filter == null || filter.Value.ValueKind is JsonValueKind.Null or JsonValueKind.Undefined) return;
            var state = new BuildState(parameters) { SlaSettings = sla, NowUtc = nowUtc ?? DateTime.UtcNow };
            var sql = Build(filter.Value, state, 0);
            if (sql != null) where.Append(" AND ").Append(sql);
        }

        private sealed class BuildState
        {
            public BuildState(List<SqlParameter> parameters) => Parameters = parameters;
            public List<SqlParameter> Parameters { get; }
            public int Conditions;
            public Sla.SlaConfig? SlaSettings;
            public DateTime NowUtc;

            public string Add(SqlDbType type, object value, int size = 0)
            {
                var name = "@gf" + Parameters.Count(p => p.ParameterName.StartsWith("@gf", StringComparison.Ordinal));
                var p = new SqlParameter(name, type) { Value = value };
                if (size != 0) p.Size = size;
                Parameters.Add(p);
                return name;
            }
        }

        private static string? Build(JsonElement node, BuildState state, int depth)
        {
            if (node.ValueKind != JsonValueKind.Object) throw new FilterValidationException("Each filter must be an object.");
            if (depth > MaxDepth) throw new FilterValidationException("Filters are nested too deeply.");

            if (node.TryGetProperty("filters", out var children))
            {
                if (children.ValueKind != JsonValueKind.Array) throw new FilterValidationException("'filters' must be an array.");
                var logic = node.TryGetProperty("logic", out var l) && l.ValueKind == JsonValueKind.String && l.GetString() == "or" ? " OR " : " AND ";
                var parts = children.EnumerateArray().Select(c => Build(c, state, depth + 1)).Where(s => s != null).ToList();
                return parts.Count == 0 ? null : "(" + string.Join(logic, parts) + ")";
            }

            if (++state.Conditions > MaxConditions) throw new FilterValidationException("Too many filter conditions.");

            var field = StringProp(node, "field") ?? throw new FilterValidationException("A filter is missing 'field'.");
            var op = StringProp(node, "operator") ?? throw new FilterValidationException("A filter is missing 'operator'.");
            if (!Fields.TryGetValue(field, out var column)) throw new FilterValidationException($"Filtering on '{field}' is not supported.");
            node.TryGetProperty("value", out var value);

            return column.Kind switch
            {
                FieldKind.Date => DateCondition(column.Sql, op, value, state),
                FieldKind.Sla => SlaCondition(op, value, state),
                _ => TextCondition(column.Sql, op, value, state)
            };
        }

        /// <summary>"sla eq Breached" / "sla neq Finished". Matches the grid's Age / SLA column (see Sla.SlaSql).</summary>
        private static string SlaCondition(string op, JsonElement value, BuildState state)
        {
            if (state.SlaSettings == null) throw new FilterValidationException("The SLA isn't set up yet, so you can't filter on it.");
            if (op is not ("eq" or "neq")) throw new FilterValidationException($"Operator '{op}' is not supported for the SLA.");
            var group = Sla.SlaSql.NormalizeGroup(value.ValueKind == JsonValueKind.String ? value.GetString() : null)
                ?? throw new FilterValidationException("The SLA filter must be one of: " + string.Join(", ", Sla.SlaSql.Groups) + ".");
            var sql = new Sla.SlaSql(state.SlaSettings, state.NowUtc, (type, v, size) => state.Add(type, v, size),
                "cr.RequestCreationTS", "cr.RequestingDomainName", StatusExpression).Condition(group);
            // Rows without a creation time have no SLA, so they match "is not" but never "is".
            return op == "eq" ? sql : $"(cr.RequestCreationTS IS NULL OR NOT {sql})";
        }

        private static string TextCondition(string expr, string op, JsonElement value, BuildState state)
        {
            switch (op)
            {
                case "isnull": return $"{expr} IS NULL";
                case "isnotnull": return $"{expr} IS NOT NULL";
                case "isempty": return $"{expr} = ''";
                case "isnotempty": return $"({expr} IS NULL OR {expr} <> '')";
            }

            var text = value.ValueKind switch
            {
                JsonValueKind.String => value.GetString() ?? string.Empty,
                JsonValueKind.Number or JsonValueKind.True or JsonValueKind.False => value.GetRawText(),
                _ => throw new FilterValidationException($"'{op}' needs a value.")
            };
            if (text.Length > 200) throw new FilterValidationException("Filter values are limited to 200 characters.");

            string Like(string pattern) => state.Add(SqlDbType.NVarChar, pattern, 410);
            var escaped = EscapeLike(text);
            switch (op)
            {
                case "contains": return $"{expr} LIKE {Like("%" + escaped + "%")} ESCAPE '\\'";
                case "doesnotcontain": return $"({expr} IS NULL OR {expr} NOT LIKE {Like("%" + escaped + "%")} ESCAPE '\\')";
                case "startswith": return $"{expr} LIKE {Like(escaped + "%")} ESCAPE '\\'";
                case "endswith": return $"{expr} LIKE {Like("%" + escaped)} ESCAPE '\\'";
                case "eq": return $"{expr} = {state.Add(SqlDbType.NVarChar, text, 200)}";
                case "neq": return $"({expr} IS NULL OR {expr} <> {state.Add(SqlDbType.NVarChar, text, 200)})";
                default: throw new FilterValidationException($"Operator '{op}' is not supported for text.");
            }
        }

        /// <summary>Dates compare by calendar day, like the grid: "eq 2026-11-03" is that whole day.</summary>
        private static string DateCondition(string expr, string op, JsonElement value, BuildState state)
        {
            if (op == "isnull") return $"{expr} IS NULL";
            if (op == "isnotnull") return $"{expr} IS NOT NULL";

            var raw = value.ValueKind == JsonValueKind.String ? value.GetString() : null;
            if (raw == null) throw new FilterValidationException($"'{op}' needs a date.");
            DateTime day;
            if (!DateTime.TryParseExact(raw, "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out day) &&
                !DateTime.TryParse(raw, CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind, out day))
                throw new FilterValidationException($"'{raw}' is not a date (use yyyy-MM-dd).");
            day = day.Date;

            string Start() => state.Add(SqlDbType.DateTime2, day);
            string Next() => state.Add(SqlDbType.DateTime2, day.AddDays(1));
            return op switch
            {
                "eq" => $"({expr} >= {Start()} AND {expr} < {Next()})",
                "neq" => $"({expr} IS NULL OR {expr} < {Start()} OR {expr} >= {Next()})",
                "gt" => $"{expr} >= {Next()}",
                "gte" => $"{expr} >= {Start()}",
                "lt" => $"{expr} < {Start()}",
                "lte" => $"{expr} < {Next()}",
                _ => throw new FilterValidationException($"Operator '{op}' is not supported for dates.")
            };
        }

        /// <summary>
        /// Appends the search box: " AND (&lt;term 1&gt; OR &lt;term 2&gt; …)". A row matches a term when its DocId, claim number,
        /// status, customer, customer id, VF response id or correspondence id contains it (a GUID term also matches the id
        /// exactly). Several terms mean "any of them", e.g. a pasted list of DocIds. Blank and repeated terms are ignored.
        /// </summary>
        public static void AppendFreeText(StringBuilder where, List<SqlParameter> parameters, IEnumerable<string?>? terms)
        {
            var list = (terms ?? Enumerable.Empty<string?>())
                .Where(t => !string.IsNullOrWhiteSpace(t))
                .Select(t => t!.Trim())
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToList();
            if (list.Count == 0) return;
            if (list.Count > MaxFreeTerms) throw new FilterValidationException($"Search for at most {MaxFreeTerms} terms at a time.");

            var parts = new List<string>();
            for (var i = 0; i < list.Count; i++)
            {
                var name = "@ft" + i;
                parameters.Add(new SqlParameter(name, SqlDbType.NVarChar, -1) { Value = "%" + EscapeLike(list[i]) + "%" });
                var like = $" LIKE {name} ESCAPE '\\'";
                var columns = new[]
                {
                    "ISNULL(cr.DocId,'')", "ISNULL(cr.ClaimNumber,'')", "ISNULL(ls.LatestStatus,'')",
                    "ISNULL(cr.RequestingDomainName,'')", "ISNULL(cr.CustomerId,'')", "ISNULL(cr.VFResponseId,'')"
                }.Select(c => c + like).ToList();
                if (Guid.TryParse(list[i], out var guid))
                {
                    var g = "@ftId" + i;
                    parameters.Add(new SqlParameter(g, SqlDbType.UniqueIdentifier) { Value = guid });
                    columns.Add("cr.CorrespondenceId = " + g);
                }
                else
                {
                    columns.Add("CONVERT(varchar(36), cr.CorrespondenceId)" + like);
                }
                parts.Add("(" + string.Join(" OR ", columns) + ")");
            }
            where.Append(" AND (").Append(string.Join(" OR ", parts)).Append(')');
        }

        private static string EscapeLike(string s)
        {
            var sb = new StringBuilder(s.Length);
            foreach (var c in s)
            {
                if (c is '\\' or '%' or '_' or '[') sb.Append('\\');
                sb.Append(c);
            }
            return sb.ToString();
        }

        private static string? StringProp(JsonElement node, string name) =>
            node.TryGetProperty(name, out var v) && v.ValueKind == JsonValueKind.String ? v.GetString() : null;
    }
}
