using CustomerCorrespondence.Domain.Entities;
using System;
using System.Collections.Generic;
using System.Threading.Tasks;

namespace CustomerCorrespondence.Domain.Interfaces
{
    public interface IChatHistoryRepository
    {
        Task<List<ChatConversationEntity>> ListConversationsAsync(string userId);
        Task<ChatConversationEntity?> GetConversationAsync(Guid conversationId);
        Task<ChatConversationEntity> CreateConversationAsync(string userId, string? title);
        Task<ChatMessageEntity> AppendMessageAsync(Guid conversationId, string role, string content);
        Task DeleteConversationAsync(Guid conversationId);
    }
}
