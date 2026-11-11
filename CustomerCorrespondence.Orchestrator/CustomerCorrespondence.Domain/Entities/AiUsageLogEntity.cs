using System;

namespace CustomerCorrespondence.Domain.Entities
{
    /// <summary>
    /// One row per AI Assistant call (real Claude call or a QueryIntentRouter local resolution,
    /// distinguished by Source) — the server-side counterpart to the per-browser localStorage
    /// token tally in AssistantPanel.tsx, so usage survives a cache clear and is comparable across
    /// users instead of being a single browser's private estimate.
    /// </summary>
    public class AiUsageLogEntity
    {
        public long UsageId { get; set; }
        public Guid? ConversationId { get; set; }
        public string? UserId { get; set; }
        public int InputTokens { get; set; }
        public int OutputTokens { get; set; }
        /// <summary>"claude" for a real API call, "router" for a QueryIntentRouter local resolution (0 tokens).</summary>
        public string Source { get; set; } = "claude";
        public DateTime CreatedTS { get; set; } = DateTime.UtcNow;
    }
}
