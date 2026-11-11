using System;

namespace CustomerCorrespondence.Domain.Entities
{
    public class ChatMessageEntity
    {
        public Guid MessageId { get; set; }
        public Guid ConversationId { get; set; }
        public string Role { get; set; } = default!;
        public string Content { get; set; } = default!;
        public DateTime CreatedTS { get; set; } = DateTime.UtcNow;
    }
}
