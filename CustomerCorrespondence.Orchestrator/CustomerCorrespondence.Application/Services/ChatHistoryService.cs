using CustomerCorrespondence.Application.Interfaces;
using CustomerCorrespondence.Domain.Entities;
using CustomerCorrespondence.Domain.Interfaces;
using Microsoft.Extensions.Logging;

namespace CustomerCorrespondence.Application.Services
{
    public class ChatHistoryService : IChatHistoryService
    {
        private readonly IChatHistoryRepository _chatHistoryRepository;
        private readonly ILogger<ChatHistoryService> _logger;

        public ChatHistoryService(IChatHistoryRepository chatHistoryRepository, ILogger<ChatHistoryService> logger)
        {
            _chatHistoryRepository = chatHistoryRepository;
            _logger = logger;
        }

        public async Task<List<ChatConversationEntity>> ListConversationsAsync(string userId)
        {
            try
            {
                _logger.LogInformation("Listing AI chat conversations for user {UserId}", userId);
                return await _chatHistoryRepository.ListConversationsAsync(userId);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error listing AI chat conversations for user {UserId}", userId);
                throw;
            }
        }

        public async Task<ChatConversationEntity?> GetConversationAsync(Guid conversationId)
        {
            try
            {
                _logger.LogInformation("Fetching AI chat conversation {ConversationId}", conversationId);
                return await _chatHistoryRepository.GetConversationAsync(conversationId);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error fetching AI chat conversation {ConversationId}", conversationId);
                throw;
            }
        }

        public async Task<ChatConversationEntity> CreateConversationAsync(string userId, string? title)
        {
            try
            {
                _logger.LogInformation("Creating AI chat conversation for user {UserId}", userId);
                return await _chatHistoryRepository.CreateConversationAsync(userId, title);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error creating AI chat conversation for user {UserId}", userId);
                throw;
            }
        }

        public async Task<ChatMessageEntity> AppendMessageAsync(Guid conversationId, string role, string content)
        {
            try
            {
                _logger.LogInformation("Appending {Role} message to AI chat conversation {ConversationId}", role, conversationId);
                return await _chatHistoryRepository.AppendMessageAsync(conversationId, role, content);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error appending message to AI chat conversation {ConversationId}", conversationId);
                throw;
            }
        }

        public async Task DeleteConversationAsync(Guid conversationId)
        {
            try
            {
                _logger.LogInformation("Deleting AI chat conversation {ConversationId}", conversationId);
                await _chatHistoryRepository.DeleteConversationAsync(conversationId);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error deleting AI chat conversation {ConversationId}", conversationId);
                throw;
            }
        }
    }
}
