using System;
using System.Collections.Generic;

namespace CustomerCorrespondence.Domain.Entities
{
    public class ChatConversationEntity
    {
        public Guid ConversationId { get; set; }
        public string UserId { get; set; } = default!;
        public string? Title { get; set; }
        public DateTime CreatedTS { get; set; } = DateTime.UtcNow;
        public DateTime UpdatedTS { get; set; } = DateTime.UtcNow;
        public List<ChatMessageEntity> Messages { get; set; } = new();
    }
}
