namespace CustomerCorrespondence.Application.Interfaces
{
    /// <summary>
    /// Result of attempting to resolve a natural-language AI Assistant question locally,
    /// without forwarding it to Claude.
    /// </summary>
    public class QueryIntentMatch
    {
        /// <summary>
        /// The Claude tool name this intent corresponds to (e.g. "search_grid", "clear_filters").
        /// Mirrors the tool names the frontend (kendoAiAdapter.ts) already defines, so the
        /// synthetic response can be parsed by the exact same client-side code path.
        /// </summary>
        public string ToolName { get; set; } = string.Empty;

        /// <summary>
        /// The tool's input parameters, matching the shape Claude would have produced
        /// (e.g. { "claimNumber": "CL-12345" } for search_grid).
        /// </summary>
        public Dictionary<string, object> Input { get; set; } = new();
    }

    /// <summary>
    /// Attempts to resolve a subset of AI Assistant questions deterministically, without
    /// calling Claude — the "Tier 2" confidence-gated router: a small, strict set of
    /// single-field, unambiguous phrasings (e.g. "find claim CL-12345", "show failed records").
    /// Anything it doesn't confidently recognize returns null, and the caller (AiController)
    /// forwards the request to Claude exactly as it did before this router existed — this
    /// component can only ever save a call, never change what a Claude-routed answer returns.
    /// </summary>
    public interface IQueryIntentRouter
    {
        /// <summary>
        /// Attempts to match the user's latest message against known, strict, single-intent
        /// patterns. Returns null if the router isn't confident that its own rules fully
        /// account for the whole message.
        /// </summary>
        /// <param name="userMessage">The most recent user message text.</param>
        QueryIntentMatch? TryResolve(string userMessage);
    }
}
