using CustomerCorrespondence.Application.Interfaces;
using CustomerCorrespondence.Domain.Entities;
using CustomerCorrespondence.Infrastructure.Persistence;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using System.Text;
using System.Text.Json;
using ProcessRequestEndPoint.Auth;

[ApiController]
[Route("api/[controller]")]
[RequirePermission(Permissions.AiUse)]
public class AiController : ControllerBase
{
    private readonly IHttpClientFactory _httpClientFactory;
    private readonly IConfiguration _configuration;
    private readonly IQueryIntentRouter _queryIntentRouter;
    private readonly CorrespondenceDBContext _db;
    private readonly ILogger<AiController> _logger;

    public AiController(
        IHttpClientFactory httpClientFactory,
        IConfiguration configuration,
        IQueryIntentRouter queryIntentRouter,
        CorrespondenceDBContext db,
        ILogger<AiController> logger)
    {
        _httpClientFactory = httpClientFactory;
        _configuration = configuration;
        _queryIntentRouter = queryIntentRouter;
        _db = db;
        _logger = logger;
    }

    /// <summary>
    /// Best-effort usage logging — never lets a logging failure affect the actual AI response.
    /// Swallows the error (beyond a debug log) specifically so this works whether or not the
    /// AiUsageLog table has been created yet; see AiUsageLogEntity for the schema and
    /// CorrespondenceDBContext.OnModelCreating for the exact column mapping to hand a DBA.
    /// </summary>
    private async Task LogUsageAsync(int inputTokens, int outputTokens, string source)
    {
        try
        {
            _db.AiUsageLogs.Add(new AiUsageLogEntity
            {
                InputTokens = inputTokens,
                OutputTokens = outputTokens,
                Source = source,
            });
            await _db.SaveChangesAsync();
        }
        catch (Exception ex)
        {
            _logger.LogDebug(ex, "AiController: usage logging skipped (table may not exist yet)");
        }
    }

    [HttpPost("messages")]
    public async Task<IActionResult> ProxyMessages([FromBody] JsonElement body)
    {
        // Tier-2 confidence-gated router: for a small set of strict, unambiguous phrasings
        // (see QueryIntentRouter), resolve the request locally and skip the Claude call
        // entirely. Anything it doesn't fully recognize returns null here and falls through
        // to the exact same Claude proxy behavior this endpoint always had.
        var latestUserMessage = TryGetLatestUserMessage(body);
        if (latestUserMessage != null)
        {
            var match = _queryIntentRouter.TryResolve(latestUserMessage);
            if (match != null)
            {
                var requestedModel = TryGetModel(body);
                var localResponse = BuildLocalToolUseResponse(match, requestedModel);
                _logger.LogInformation(
                    "AiController: resolved locally via QueryIntentRouter ({ToolName}) — Claude call skipped",
                    match.ToolName);
                await LogUsageAsync(0, 0, "router");
                return Content(localResponse, "application/json", Encoding.UTF8);
            }
        }

        var apiKey = _configuration["Anthropic:ApiKey"];
        if (string.IsNullOrEmpty(apiKey))
            return StatusCode(500, new { error = "Anthropic:ApiKey not configured in appsettings" });

        var client = _httpClientFactory.CreateClient();
        client.DefaultRequestHeaders.Add("x-api-key", apiKey);
        client.DefaultRequestHeaders.Add("anthropic-version", "2023-06-01");

        var content = new StringContent(body.GetRawText(), Encoding.UTF8, "application/json");
        var response = await client.PostAsync("https://api.anthropic.com/v1/messages", content);
        var responseBody = await response.Content.ReadAsStringAsync();

        _logger.LogInformation(
            "AiController: forwarded request to Claude (status {StatusCode})",
            (int)response.StatusCode);

        if (response.IsSuccessStatusCode)
        {
            try
            {
                using var doc = JsonDocument.Parse(responseBody);
                if (doc.RootElement.TryGetProperty("usage", out var usageEl))
                {
                    var inputTokens = usageEl.TryGetProperty("input_tokens", out var inEl) ? inEl.GetInt32() : 0;
                    var outputTokens = usageEl.TryGetProperty("output_tokens", out var outEl) ? outEl.GetInt32() : 0;
                    await LogUsageAsync(inputTokens, outputTokens, "claude");
                }
            }
            catch (JsonException ex)
            {
                _logger.LogDebug(ex, "AiController: could not parse usage from Claude response for logging");
            }
        }

        return Content(responseBody, "application/json", Encoding.UTF8);
    }

    /// <summary>
    /// Org-wide usage totals from AiUsageLog — the server-side counterpart to the per-browser
    /// localStorage tally, so the number survives a cache clear and reflects everyone, not just
    /// whoever is currently looking at it. Returns zeros (not an error) if the table doesn't exist
    /// yet, consistent with LogUsageAsync's best-effort approach.
    /// </summary>
    [HttpGet("usage/summary")]
    public async Task<IActionResult> GetUsageSummary()
    {
        try
        {
            var rows = await _db.AiUsageLogs.ToListAsync();
            var claudeCalls = rows.Where(r => r.Source == "claude").ToList();
            var routerHits = rows.Count(r => r.Source == "router");
            return Ok(new
            {
                inputTokens = claudeCalls.Sum(r => (long)r.InputTokens),
                outputTokens = claudeCalls.Sum(r => (long)r.OutputTokens),
                claudeCalls = claudeCalls.Count,
                routerHits,
            });
        }
        catch (Exception ex)
        {
            _logger.LogDebug(ex, "AiController: usage summary unavailable (table may not exist yet)");
            return Ok(new { inputTokens = 0L, outputTokens = 0L, claudeCalls = 0, routerHits = 0 });
        }
    }

    /// <summary>
    /// Extracts the plain-text content of the last message in the request body, but only when
    /// it's a user turn with plain-string content — the exact shape the frontend
    /// (kendoAiAdapter.ts) always sends. Any other shape (missing messages, non-user last turn,
    /// content blocks instead of a string) returns null so the router is never asked to guess
    /// at something it wasn't built to parse.
    /// </summary>
    private static string? TryGetLatestUserMessage(JsonElement body)
    {
        if (!body.TryGetProperty("messages", out var messagesEl) || messagesEl.ValueKind != JsonValueKind.Array)
            return null;

        JsonElement? last = null;
        foreach (var message in messagesEl.EnumerateArray())
            last = message;

        if (last is not { } lastMessage)
            return null;

        if (!lastMessage.TryGetProperty("role", out var roleEl) || roleEl.GetString() != "user")
            return null;

        if (!lastMessage.TryGetProperty("content", out var contentEl) || contentEl.ValueKind != JsonValueKind.String)
            return null;

        return contentEl.GetString();
    }

    private static string? TryGetModel(JsonElement body)
    {
        return body.TryGetProperty("model", out var modelEl) && modelEl.ValueKind == JsonValueKind.String
            ? modelEl.GetString()
            : null;
    }

    /// <summary>
    /// Builds a response matching the shape of Anthropic's Messages API tool_use response —
    /// same fields the frontend already parses (payload.content[].type === "tool_use",
    /// payload.stop_reason, payload.usage) — so kendoAiAdapter.ts needs zero changes to accept
    /// a locally-resolved answer. usage is reported as zero since no model call was made.
    /// </summary>
    private static string BuildLocalToolUseResponse(QueryIntentMatch match, string? requestedModel)
    {
        var response = new
        {
            id = $"msg_local_{Guid.NewGuid():N}",
            type = "message",
            role = "assistant",
            model = requestedModel ?? "claude-haiku-4-5",
            content = new object[]
            {
                new
                {
                    type = "tool_use",
                    id = $"toolu_local_{Guid.NewGuid():N}",
                    name = match.ToolName,
                    input = match.Input
                }
            },
            stop_reason = "tool_use",
            stop_sequence = (string?)null,
            usage = new { input_tokens = 0, output_tokens = 0 }
        };

        return JsonSerializer.Serialize(response);
    }
}
