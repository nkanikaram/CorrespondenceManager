using System.Text.RegularExpressions;
using CustomerCorrespondence.Application.Interfaces;
using Microsoft.Extensions.Logging;

namespace CustomerCorrespondence.Application.Services
{
    /// <summary>
    /// Deterministic, rule-based implementation of <see cref="IQueryIntentRouter"/>.
    ///
    /// Deliberately NOT a numeric "confidence score" or ML classifier — every rule here is a
    /// strict, whole-message regex. A message either matches one of these narrow templates
    /// completely, or it falls through untouched to Claude. That is the safety property this
    /// design relies on: the router can only ever remove a Claude call for a message it fully
    /// understands, never partially guess at one it doesn't (which is where a "confidence
    /// threshold" approach would risk silently wrong answers on edge cases).
    ///
    /// Every pattern here must stay in lockstep with the tool definitions and example phrasings
    /// in the frontend's src/api/kendoAiAdapter.ts (SEARCH_TOOL, CLEAR_FILTERS_TOOL,
    /// UNDO_TOOL) — if that file's decision rules change, revisit these patterns.
    /// </summary>
    public class QueryIntentRouter : IQueryIntentRouter
    {
        private readonly ILogger<QueryIntentRouter> _logger;

        private static readonly string[] ValidStatuses = { "Completed", "Processing", "Submitted", "Failed" };

        // Whole-string, case-insensitive matches only — see class remarks.
        private static readonly Regex ClearFiltersRegex = new(
            @"^(clear|reset|remove)\s+(all\s+)?(the\s+)?filters?(\s+in\s+the\s+grid)?$",
            RegexOptions.IgnoreCase | RegexOptions.Compiled);

        private static readonly Regex UndoRegex = new(
            @"^(undo(\s+the\s+last\s+(change|search))?|go\s+back|revert(\s+that)?)$",
            RegexOptions.IgnoreCase | RegexOptions.Compiled);

        private static readonly Regex ClaimLookupRegex = new(
            @"^(find|show|search|lookup)\s+claim\s+([A-Za-z]{2,3}-\d{3,6})$",
            RegexOptions.IgnoreCase | RegexOptions.Compiled);

        private static readonly Regex PolicyLookupRegex = new(
            @"^(find|show|search|lookup)\s+policy\s+([A-Za-z]{1,3}-\d{4,8})$",
            RegexOptions.IgnoreCase | RegexOptions.Compiled);

        // "show failed records", "list all completed requests", "show submitted records"
        private static readonly Regex StatusOnlyRegexA = new(
            @"^(show|list)\s+(all\s+)?(completed|processing|submitted|failed)\s+(records|requests)$",
            RegexOptions.IgnoreCase | RegexOptions.Compiled);

        // "show records in Processing status"
        private static readonly Regex StatusOnlyRegexB = new(
            @"^show\s+records\s+in\s+(completed|processing|submitted|failed)\s+status$",
            RegexOptions.IgnoreCase | RegexOptions.Compiled);

        public QueryIntentRouter(ILogger<QueryIntentRouter> logger)
        {
            _logger = logger;
        }

        public QueryIntentMatch? TryResolve(string userMessage)
        {
            if (string.IsNullOrWhiteSpace(userMessage))
                return null;

            var text = userMessage.Trim();

            if (ClearFiltersRegex.IsMatch(text))
                return Resolved("clear_filters", new Dictionary<string, object>(), text);

            if (UndoRegex.IsMatch(text))
                return Resolved("undo_last_search", new Dictionary<string, object>(), text);

            var claimMatch = ClaimLookupRegex.Match(text);
            if (claimMatch.Success)
            {
                var claimNumber = claimMatch.Groups[2].Value.ToUpperInvariant();
                return Resolved("search_grid", new Dictionary<string, object> { ["claimNumber"] = claimNumber }, text);
            }

            var policyMatch = PolicyLookupRegex.Match(text);
            if (policyMatch.Success)
            {
                var policyNumber = policyMatch.Groups[2].Value.ToUpperInvariant();
                return Resolved("search_grid", new Dictionary<string, object> { ["policyNumber"] = policyNumber }, text);
            }

            var statusMatchA = StatusOnlyRegexA.Match(text);
            if (statusMatchA.Success)
            {
                var status = NormalizeStatus(statusMatchA.Groups[3].Value);
                return Resolved("search_grid", new Dictionary<string, object> { ["status"] = status }, text);
            }

            var statusMatchB = StatusOnlyRegexB.Match(text);
            if (statusMatchB.Success)
            {
                var status = NormalizeStatus(statusMatchB.Groups[1].Value);
                return Resolved("search_grid", new Dictionary<string, object> { ["status"] = status }, text);
            }

            return null;
        }

        private static string NormalizeStatus(string raw)
        {
            foreach (var known in ValidStatuses)
            {
                if (string.Equals(known, raw, StringComparison.OrdinalIgnoreCase))
                    return known;
            }
            // Regex already constrains raw to one of ValidStatuses (case-insensitively), so this
            // is unreachable in practice — kept only as a defensive fallback.
            return raw;
        }

        private QueryIntentMatch Resolved(string toolName, Dictionary<string, object> input, string originalText)
        {
            _logger.LogInformation(
                "QueryIntentRouter resolved {ToolName} locally for message {Message} (skipped Claude call)",
                toolName, originalText);
            return new QueryIntentMatch { ToolName = toolName, Input = input };
        }
    }
}
