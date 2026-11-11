using CustomerCorrespondence.Domain.Entities;
using System;
using System.Collections.Generic;
using System.Threading.Tasks;

namespace CustomerCorrespondence.Application.Interfaces
{
    /// <summary>
    /// Manages persisted AI assistant chat conversations: listing a user's past conversations,
    /// loading a conversation's full message history, creating new conversations, and appending
    /// messages as the user chats with the assistant.
    /// </summary>
    public interface IChatHistoryService
    {
        Task<List<ChatConversationEntity>> ListConversationsAsync(string userId);
        Task<ChatConversationEntity?> GetConversationAsync(Guid conversationId);
        Task<ChatConversationEntity> CreateConversationAsync(string userId, string? title);
        Task<ChatMessageEntity> AppendMessageAsync(Guid conversationId, string role, string content);
        Task DeleteConversationAsync(Guid conversationId);
    }
}
