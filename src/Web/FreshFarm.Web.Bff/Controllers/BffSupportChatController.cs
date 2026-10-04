using FreshFarm.Web.Bff.Areas.Seller.Hubs;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.AspNetCore.SignalR;

namespace FreshFarm.Web.Bff.Controllers;

[Authorize]
[ApiController]
[Route("bff/support-chat")]
public sealed class BffSupportChatController : ControllerBase
{
    private const string AccessTokenSessionKey = "ACCESS_TOKEN";
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    private readonly IHttpClientFactory _httpClientFactory;
    private readonly IHubContext<SupportChatHub> _hubContext;

    public BffSupportChatController(IHttpClientFactory httpClientFactory, IHubContext<SupportChatHub> hubContext)
    {
        _httpClientFactory = httpClientFactory;
        _hubContext = hubContext;
    }

    [HttpGet("summaries")]
    public async Task<IActionResult> Summaries([FromQuery] int[]? sellerIds)
    {
        if (User.IsInRole("Seller") || User.IsInRole("Admin"))
        {
            return Forbid();
        }

        var normalizedSellerIds = (sellerIds ?? Array.Empty<int>())
            .Where(x => x > 0)
            .Distinct()
            .Take(50)
            .ToArray();

        var queryString = normalizedSellerIds.Length == 0
            ? string.Empty
            : "?" + string.Join("&", normalizedSellerIds.Select(x => $"sellerIds={x}"));

        var client = CreateAuthorizedClient("Ordering");
        var response = await client.GetAsync($"/api/orders/support-chat/summaries{queryString}");
        if (!response.IsSuccessStatusCode)
        {
            return await ProxyJsonResponseAsync(response, "Không thể tải trạng thái chat.");
        }

        var body = await response.Content.ReadAsStringAsync();
        var payload = JsonSerializer.Deserialize<SupportChatSummariesApiResponse>(body, JsonOptions);
        var summaries = payload?.Summaries ?? new List<SupportChatSummaryApiDto>();
        if (summaries.Count == 0)
        {
            return Ok(new { ok = true, summaries = Array.Empty<object>() });
        }

        var merchantLookup = await LoadMerchantLookupAsync(summaries.Select(x => x.SellerId));
        var enrichedSummaries = summaries
            .Select(summary =>
            {
                merchantLookup.TryGetValue(summary.SellerId, out var merchant);
                return new
                {
                    sellerId = summary.SellerId,
                    conversationId = summary.ConversationId,
                    status = summary.Status,
                    startedAt = summary.StartedAt,
                    closedAt = summary.ClosedAt,
                    lastContent = summary.LastContent ?? string.Empty,
                    lastTime = summary.LastTime,
                    hasUnread = summary.HasUnread,
                    shopName = merchant?.ShopName ?? merchant?.UserName ?? $"FreshFarm Seller {summary.SellerId}",
                    avatar = merchant?.Avatar,
                    addressSummary = merchant?.AddressSummary ?? "Chưa cập nhật địa chỉ hoạt động",
                    joinedAt = merchant?.JoinedAt
                };
            })
            .ToList();

        return Ok(new
        {
            ok = true,
            summaries = enrichedSummaries
        });
    }

    [HttpGet("sellers/{sellerId:int}/conversation")]
    public async Task<IActionResult> ConversationBySeller([FromRoute] int sellerId, [FromQuery] bool createIfMissing = false)
    {
        if (sellerId <= 0)
        {
            return BadRequest(new { ok = false, message = "sellerId không hợp lệ." });
        }

        if (User.IsInRole("Seller") || User.IsInRole("Admin"))
        {
            return Forbid();
        }

        var client = CreateAuthorizedClient("Ordering");
        var response = await client.GetAsync($"/api/orders/support-chat/sellers/{sellerId}/conversation?createIfMissing={createIfMissing.ToString().ToLowerInvariant()}");
        return await ProxyJsonResponseAsync(response, "Không thể tải hội thoại với shop.");
    }

    [HttpGet("conversations/{conversationId:int}/messages")]
    public async Task<IActionResult> Messages([FromRoute] int conversationId, [FromQuery] int take = 100)
    {
        if (conversationId <= 0)
        {
            return BadRequest(new { ok = false, message = "conversationId không hợp lệ." });
        }

        if (User.IsInRole("Seller") || User.IsInRole("Admin"))
        {
            return Forbid();
        }

        var client = CreateAuthorizedClient("Ordering");
        var response = await client.GetAsync($"/api/orders/support-chat/conversations/{conversationId}/messages?take={take}");
        return await ProxyJsonResponseAsync(response, "Không thể tải tin nhắn.");
    }

    [HttpPost("conversations/{conversationId:int}/messages")]
    [ValidateAntiForgeryToken]
    [EnableRateLimiting("support-chat")]
    public async Task<IActionResult> SendMessage([FromRoute] int conversationId, [FromBody] SendSupportMessageBridgeRequest? request)
    {
        if (conversationId <= 0)
        {
            return BadRequest(new { ok = false, message = "conversationId không hợp lệ." });
        }

        if (User.IsInRole("Seller") || User.IsInRole("Admin"))
        {
            return Forbid();
        }

        var client = CreateAuthorizedClient("Ordering");
        var response = await client.PostAsJsonAsync(
            $"/api/orders/support-chat/conversations/{conversationId}/messages",
            new
            {
                content = request?.Content,
                replyToMessageId = request?.ReplyToMessageId
            });

        var body = await response.Content.ReadAsStringAsync();
        if (response.IsSuccessStatusCode)
        {
            var messagePayload = TryExtractMessagePayload(body);
            await NotifyConversationUpdatedAsync(conversationId, request?.SellerId, messagePayload, eventType: "message");
        }

        return BuildProxyJsonResponse(response.StatusCode, body, "Không thể gửi tin nhắn.");
    }

    [HttpPost("conversations/{conversationId:int}/mark-read")]
    [ValidateAntiForgeryToken]
    [EnableRateLimiting("support-chat")]
    public async Task<IActionResult> MarkAsRead([FromRoute] int conversationId)
    {
        if (conversationId <= 0)
        {
            return BadRequest(new { ok = false, message = "conversationId không hợp lệ." });
        }

        if (User.IsInRole("Seller") || User.IsInRole("Admin"))
        {
            return Forbid();
        }

        var client = CreateAuthorizedClient("Ordering");
        var response = await client.PostAsync($"/api/orders/support-chat/conversations/{conversationId}/mark-read", content: null);
        return await ProxyJsonResponseAsync(response, "Không thể cập nhật trạng thái đã đọc.");
    }

    private HttpClient CreateAuthorizedClient(string clientName)
    {
        var client = _httpClientFactory.CreateClient(clientName);
        client.DefaultRequestHeaders.Remove("Authorization");

        var token = HttpContext.Session.GetString(AccessTokenSessionKey);
        if (!string.IsNullOrWhiteSpace(token))
        {
            client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);
        }

        return client;
    }

    private async Task<IReadOnlyDictionary<int, PublicMerchantApiDto>> LoadMerchantLookupAsync(IEnumerable<int> sellerIds)
    {
        var normalizedSellerIds = sellerIds
            .Where(x => x > 0)
            .Distinct()
            .Take(50)
            .ToArray();

        if (normalizedSellerIds.Length == 0)
        {
            return new Dictionary<int, PublicMerchantApiDto>();
        }

        var identityClient = _httpClientFactory.CreateClient("Identity");
        var merchantQuery = string.Join("&", normalizedSellerIds.Select(id => $"sellerIds={id}"));
        var response = await identityClient.GetAsync($"/auth/public/merchants?{merchantQuery}");
        if (!response.IsSuccessStatusCode)
        {
            return new Dictionary<int, PublicMerchantApiDto>();
        }

        var body = await response.Content.ReadAsStringAsync();
        var payload = JsonSerializer.Deserialize<PublicMerchantListApiResponse>(body, JsonOptions);
        var merchants = payload?.Merchants ?? new List<PublicMerchantApiDto>();
        return merchants
            .Where(merchant => merchant.SellerId > 0)
            .GroupBy(merchant => merchant.SellerId)
            .ToDictionary(group => group.Key, group => SelectPreferredMerchant(group));
    }

    private static PublicMerchantApiDto SelectPreferredMerchant(IEnumerable<PublicMerchantApiDto> merchants)
    {
        return merchants
            .OrderByDescending(CalculateMerchantScore)
            .ThenByDescending(merchant => merchant.JoinedAt ?? DateTime.MinValue)
            .First();
    }

    private static int CalculateMerchantScore(PublicMerchantApiDto merchant)
    {
        var score = 0;
        score += HasMeaningfulValue(merchant.ShopName) ? 4 : 0;
        score += HasMeaningfulValue(merchant.UserName) ? 2 : 0;
        score += HasMeaningfulValue(merchant.AddressSummary) ? 3 : 0;
        score += HasMeaningfulValue(merchant.Avatar) ? 1 : 0;
        score += merchant.JoinedAt.HasValue ? 1 : 0;
        return score;
    }

    private static bool HasMeaningfulValue(string? value)
        => !string.IsNullOrWhiteSpace(value);

    private static async Task<IActionResult> ProxyJsonResponseAsync(HttpResponseMessage response, string fallback)
    {
        var body = await response.Content.ReadAsStringAsync();
        return BuildProxyJsonResponse(response.StatusCode, body, fallback);
    }

    private async Task NotifyConversationUpdatedAsync(
        int conversationId,
        int? sellerId,
        JsonElement? messagePayload = null,
        string eventType = "updated")
    {
        if (sellerId.HasValue && sellerId.Value > 0)
        {
            await _hubContext.Clients.Group(SupportChatHubGroups.Seller(sellerId.Value))
                .SendAsync("newConversationOrMessage", new
                {
                    conversationId,
                    eventType,
                    at = DateTime.UtcNow
                });
        }

        if (messagePayload.HasValue && messagePayload.Value.ValueKind == JsonValueKind.Object)
        {
            await _hubContext.Clients.Group(SupportChatHubGroups.Conversation(conversationId))
                .SendAsync("receiveMessage", new
                {
                    conversationId,
                    message = messagePayload.Value
                });
        }
    }

    private static IActionResult BuildProxyJsonResponse(
        System.Net.HttpStatusCode statusCode,
        string body,
        string fallback)
    {
        if ((int)statusCode >= 200 && (int)statusCode <= 299)
        {
            return new ContentResult
            {
                Content = body,
                ContentType = "application/json",
                StatusCode = (int)statusCode
            };
        }

        var message = ExtractMessage(body) ?? fallback;
        return new ObjectResult(new { ok = false, message })
        {
            StatusCode = (int)statusCode
        };
    }

    private static JsonElement? TryExtractMessagePayload(string? body)
    {
        if (string.IsNullOrWhiteSpace(body))
        {
            return null;
        }

        try
        {
            using var document = JsonDocument.Parse(body);
            var root = document.RootElement;
            if (root.ValueKind != JsonValueKind.Object || !root.TryGetProperty("message", out var message))
            {
                return null;
            }

            return message.Clone();
        }
        catch
        {
            return null;
        }
    }

    private static string? ExtractMessage(string? body)
    {
        if (string.IsNullOrWhiteSpace(body))
        {
            return null;
        }

        try
        {
            using var document = JsonDocument.Parse(body);
            var root = document.RootElement;

            if (root.ValueKind != JsonValueKind.Object)
            {
                return body;
            }

            if (root.TryGetProperty("message", out var messageProp) && messageProp.ValueKind == JsonValueKind.String)
            {
                return messageProp.GetString();
            }

            if (root.TryGetProperty("detail", out var detailProp) && detailProp.ValueKind == JsonValueKind.String)
            {
                return detailProp.GetString();
            }

            if (root.TryGetProperty("title", out var titleProp) && titleProp.ValueKind == JsonValueKind.String)
            {
                return titleProp.GetString();
            }
        }
        catch
        {
            return body;
        }

        return body;
    }

    public sealed class SendSupportMessageBridgeRequest
    {
        public string? Content { get; set; }

        public int? ReplyToMessageId { get; set; }

        public int? SellerId { get; set; }
    }

    private sealed class SupportChatSummariesApiResponse
    {
        public bool Ok { get; set; }

        public List<SupportChatSummaryApiDto>? Summaries { get; set; }
    }

    private sealed class SupportChatSummaryApiDto
    {
        public int SellerId { get; set; }

        public int ConversationId { get; set; }

        public string Status { get; set; } = string.Empty;

        public DateTime StartedAt { get; set; }

        public DateTime? ClosedAt { get; set; }

        public string? LastContent { get; set; }

        public DateTime LastTime { get; set; }

        public bool HasUnread { get; set; }
    }

    private sealed class PublicMerchantListApiResponse
    {
        public List<PublicMerchantApiDto>? Merchants { get; set; }
    }

    private sealed class PublicMerchantApiDto
    {
        public int SellerId { get; set; }

        public string ShopName { get; set; } = string.Empty;

        public string UserName { get; set; } = string.Empty;

        public string? Avatar { get; set; }

        public string AddressSummary { get; set; } = string.Empty;

        public DateTime? JoinedAt { get; set; }
    }
}
