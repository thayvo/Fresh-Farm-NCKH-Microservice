using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace FreshFarm.Web.Bff.Controllers;

[Authorize]
[ApiController]
[Route("bff/support-chat")]
public sealed class BffSupportChatController : ControllerBase
{
    private const string AccessTokenSessionKey = "ACCESS_TOKEN";

    private readonly IHttpClientFactory _httpClientFactory;

    public BffSupportChatController(IHttpClientFactory httpClientFactory)
    {
        _httpClientFactory = httpClientFactory;
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

        var client = CreateAuthorizedClient();
        var response = await client.GetAsync($"/api/orders/support-chat/summaries{queryString}");
        return await ProxyJsonResponseAsync(response, "Không thể tải trạng thái chat.");
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

        var client = CreateAuthorizedClient();
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

        var client = CreateAuthorizedClient();
        var response = await client.GetAsync($"/api/orders/support-chat/conversations/{conversationId}/messages?take={take}");
        return await ProxyJsonResponseAsync(response, "Không thể tải tin nhắn.");
    }

    [HttpPost("conversations/{conversationId:int}/messages")]
    [ValidateAntiForgeryToken]
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

        var client = CreateAuthorizedClient();
        var response = await client.PostAsJsonAsync(
            $"/api/orders/support-chat/conversations/{conversationId}/messages",
            new
            {
                content = request?.Content,
                replyToMessageId = request?.ReplyToMessageId
            });

        return await ProxyJsonResponseAsync(response, "Không thể gửi tin nhắn.");
    }

    [HttpPost("conversations/{conversationId:int}/mark-read")]
    [ValidateAntiForgeryToken]
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

        var client = CreateAuthorizedClient();
        var response = await client.PostAsync($"/api/orders/support-chat/conversations/{conversationId}/mark-read", content: null);
        return await ProxyJsonResponseAsync(response, "Không thể cập nhật trạng thái đã đọc.");
    }

    private HttpClient CreateAuthorizedClient()
    {
        var client = _httpClientFactory.CreateClient("Ordering");
        client.DefaultRequestHeaders.Remove("Authorization");

        var token = HttpContext.Session.GetString(AccessTokenSessionKey);
        if (!string.IsNullOrWhiteSpace(token))
        {
            client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);
        }

        return client;
    }

    private static async Task<IActionResult> ProxyJsonResponseAsync(HttpResponseMessage response, string fallback)
    {
        var body = await response.Content.ReadAsStringAsync();
        if (response.IsSuccessStatusCode)
        {
            return new ContentResult
            {
                Content = body,
                ContentType = "application/json",
                StatusCode = (int)response.StatusCode
            };
        }

        var message = ExtractMessage(body) ?? fallback;
        return new ObjectResult(new { ok = false, message })
        {
            StatusCode = (int)response.StatusCode
        };
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
    }
}
