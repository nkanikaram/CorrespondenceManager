using CustomerCorrespondence.Domain.Entities;
using CustomerCorrespondence.Domain.Interfaces;
using CustomerCorrespondence.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace CustomerCorrespondence.Infrastructure.Repositories
{
    public class ChatHistoryRepository(CorrespondenceDBContext dbContext) : IChatHistoryRepository
    {
        public async Task<List<ChatConversationEntity>> ListConversationsAsync(string userId)
        {
            return await dbContext.ChatConversations
                .Where(c => c.UserId == userId)
                .OrderByDescending(c => c.UpdatedTS)
                .ToListAsync();
        }

        public async Task<ChatConversationEntity?> GetConversationAsync(Guid conversationId)
        {
            return await dbContext.ChatConversations
                .Include(c => c.Messages.OrderBy(m => m.CreatedTS))
                .FirstOrDefaultAsync(c => c.ConversationId == conversationId);
        }

        public async Task<ChatConversationEntity> CreateConversationAsync(string userId, string? title)
        {
            var conversation = new ChatConversationEntity
            {
                ConversationId = Guid.NewGuid(),
                UserId = userId,
                Title = title,
                CreatedTS = DateTime.UtcNow,
                UpdatedTS = DateTime.UtcNow,
            };

            await dbContext.ChatConversations.AddAsync(conversation);
            await dbContext.SaveChangesAsync();
            return conversation;
        }

        public async Task<ChatMessageEntity> AppendMessageAsync(Guid conversationId, string role, string content)
        {
            var conversation = await dbContext.ChatConversations.FirstOrDefaultAsync(c => c.ConversationId == conversationId)
                ?? throw new InvalidOperationException($"Conversation '{conversationId}' was not found.");

            var message = new ChatMessageEntity
            {
                MessageId = Guid.NewGuid(),
                ConversationId = conversationId,
                Role = role,
                Content = content,
                CreatedTS = DateTime.UtcNow,
            };

            conversation.UpdatedTS = DateTime.UtcNow;

            await dbContext.ChatMessages.AddAsync(message);
            await dbContext.SaveChangesAsync();
            return message;
        }

        public async Task DeleteConversationAsync(Guid conversationId)
        {
            var conversation = await dbContext.ChatConversations.FirstOrDefaultAsync(c => c.ConversationId == conversationId);
            if (conversation != null)
            {
                dbContext.ChatConversations.Remove(conversation);
                await dbContext.SaveChangesAsync();
            }
        }
    }
}
