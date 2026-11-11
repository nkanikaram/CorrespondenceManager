using CustomerCorrespondence.Application.Interfaces;
using CustomerCorrespondence.Infrastructure.Persistence;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using ProcessRequestEndPoint.Auth;

namespace ProcessRequestEndPoint.Controllers
{
    public class CreateConversationRequest
    {
        public string UserId { get; set; } = default!;
        public string? Title { get; set; }
    }

    public class AppendMessageRequest
    {
        public string Role { get; set; } = default!;
        public string Content { get; set; } = default!;
    }

    /// <summary>
    /// Assistant chat history. Conversations belong to the signed-in portal user: the owner is taken
    /// from the token (any userId sent by the client is ignored), and other users' conversations
    /// are reported as not found.
    /// </summary>
    [ApiController]
    [Route("api/[controller]")]
    [RequirePermission(Permissions.AiUse)]
    public class ChatHistoryController : ControllerBase
    {
        private readonly IChatHistoryService _chatHistoryService;
        private readonly CorrespondenceDBContext _db;
        private readonly PortalAccessService _access;

        public ChatHistoryController(IChatHistoryService chatHistoryService, CorrespondenceDBContext db, PortalAccessService access)
        {
            _chatHistoryService = chatHistoryService;
            _db = db;
            _access = access;
        }

        private async Task<string?> OwnerIdAsync() => (await _access.CurrentAsync())?.UserId.ToString();

        private async Task<bool> OwnsAsync(Guid conversationId)
        {
            var owner = await OwnerIdAsync();
            var conversation = owner == null ? null : await _chatHistoryService.GetConversationAsync(conversationId);
            return conversation != null && string.Equals(conversation.UserId, owner, StringComparison.OrdinalIgnoreCase);
        }

        /// <summary>
        /// Groups past user messages by normalized text and returns the most frequent ones —
        /// a data-driven worklist for deciding what to add next to QueryIntentRouter's exact-match
        /// rules, instead of guessing which phrasings are common. Normalizing (trim/lowercase/
        /// collapse whitespace) groups near-duplicate phrasings like "Show failed records" and
        /// "show failed records " together; it does not attempt fuzzy matching beyond that.
        /// </summary>
        [HttpGet("frequent-queries")]
        [RequirePermission(Permissions.UsersManage)] // reads every user's messages
        public async Task<IActionResult> GetFrequentQueries([FromQuery] int top = 25, [FromQuery] int minCount = 2)
        {
            var userMessages = await _db.ChatMessages
                .Where(m => m.Role == "user")
                .Select(m => m.Content)
                .ToListAsync();

            var grouped = userMessages
                .Select(c => c.Trim())
                .Where(c => c.Length > 0)
                .GroupBy(c => string.Join(' ', c.ToLowerInvariant().Split(' ', StringSplitOptions.RemoveEmptyEntries)))
                .Select(g => new { text = g.First(), count = g.Count() })
                .Where(g => g.count >= minCount)
                .OrderByDescending(g => g.count)
                .Take(top)
                .ToList();

            return Ok(grouped);
        }

        [HttpGet("conversations")]
        public async Task<IActionResult> ListConversations()
        {
            var userId = await OwnerIdAsync();
            if (userId == null) return Unauthorized();

            var conversations = await _chatHistoryService.ListConversationsAsync(userId);
            var result = conversations.Select(c => new
            {
                conversationId = c.ConversationId,
                title = c.Title,
                createdTS = c.CreatedTS,
                updatedTS = c.UpdatedTS,
            });
            return Ok(result);
        }

        [HttpGet("conversations/{id:guid}")]
        public async Task<IActionResult> GetConversation(Guid id)
        {
            var conversation = await _chatHistoryService.GetConversationAsync(id);
            if (conversation == null || !string.Equals(conversation.UserId, await OwnerIdAsync(), StringComparison.OrdinalIgnoreCase))
                return NotFound();

            return Ok(new
            {
                conversationId = conversation.ConversationId,
                title = conversation.Title,
                createdTS = conversation.CreatedTS,
                updatedTS = conversation.UpdatedTS,
                messages = conversation.Messages.Select(m => new
                {
                    messageId = m.MessageId,
                    role = m.Role,
                    content = m.Content,
                    createdTS = m.CreatedTS,
                }),
            });
        }

        [HttpPost("conversations")]
        public async Task<IActionResult> CreateConversation([FromBody] CreateConversationRequest request)
        {
            var userId = await OwnerIdAsync();
            if (userId == null) return Unauthorized();

            var conversation = await _chatHistoryService.CreateConversationAsync(userId, request?.Title);
            return Ok(new { conversationId = conversation.ConversationId });
        }

        [HttpPost("conversations/{id:guid}/messages")]
        public async Task<IActionResult> AppendMessage(Guid id, [FromBody] AppendMessageRequest request)
        {
            if (string.IsNullOrWhiteSpace(request?.Role) || string.IsNullOrWhiteSpace(request?.Content))
                return BadRequest(new { error = "role and content are required" });
            if (!await OwnsAsync(id))
                return NotFound();

            var message = await _chatHistoryService.AppendMessageAsync(id, request.Role, request.Content);
            return Ok(new { messageId = message.MessageId, createdTS = message.CreatedTS });
        }

        [HttpDelete("conversations/{id:guid}")]
        public async Task<IActionResult> DeleteConversation(Guid id)
        {
            if (!await OwnsAsync(id))
                return NotFound();
            await _chatHistoryService.DeleteConversationAsync(id);
            return NoContent();
        }
    }
}
