using System.Security.Claims;
using FreshFarm.Web.Bff.Areas.Seller.Hubs;
using FreshFarm.Web.Bff.Areas.Seller.Infrastructure;
using Microsoft.AspNetCore.SignalR;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;

namespace FreshFarm.Web.Bff.Areas.Seller.Controllers;

[Authorize(Roles = "Seller")]
[ResponseCache(Duration = 0, Location = ResponseCacheLocation.None, NoStore = true)]
[Area("Seller")]
public class SupportChatController : LegacySellerControllerBase
{
    private const string AccessTokenSessionKey = "ACCESS_TOKEN";

    private readonly IHttpClientFactory _httpClientFactory;
    private readonly IHubContext<SupportChatHub> _hubContext;

    public SupportChatController(IHttpClientFactory httpClientFactory, IHubContext<SupportChatHub> hubContext)
    {
        _httpClientFactory = httpClientFactory;
        _hubContext = hubContext;
    }

    [HttpGet]
    public IActionResult Index()
    {
        return View();
    }

    [HttpGet]
    public async Task<IActionResult> Conversations()
    {
        try
        {
            var client = CreateAuthorizedClient("Ordering");
            var response = await client.GetAsync("/api/orders/admin/support-chat/conversations");
            var body = await response.Content.ReadAsStringAsync();

            if (!response.IsSuccessStatusCode)
            {
                return Json(new
                {
                    ok = false,
                    message = await ReadApiErrorAsync(response, "Khong the tai danh sach hoi thoai")
                });
            }

            return Content(body, "application/json");
        }
        catch (Exception ex)
        {
            return Json(new { ok = false, message = "Loi: " + ex.Message });
        }
    }

    [HttpGet]
    public async Task<IActionResult> Messages(int conversationId, int take = 100)
    {
        try
        {
            if (conversationId <= 0)
            {
                return Json(new { ok = false, message = "conversationId khong hop le" });
            }

            var client = CreateAuthorizedClient("Ordering");
            var response = await client.GetAsync($"/api/orders/admin/support-chat/conversations/{conversationId}/messages?take={take}");
            var body = await response.Content.ReadAsStringAsync();

            if (!response.IsSuccessStatusCode)
            {
                return Json(new
                {
                    ok = false,
                    message = await ReadApiErrorAsync(response, "Khong the tai tin nhan")
                });
            }

            return Content(body, "application/json");
        }
        catch (Exception ex)
        {
            return Json(new { ok = false, message = "Loi: " + ex.Message });
        }
    }

    [HttpGet]
    public async Task<IActionResult> ConversationDetails(int conversationId)
    {
        try
        {
            if (conversationId <= 0)
            {
                return Json(new { ok = false, message = "conversationId khong hop le" });
            }

            var client = CreateAuthorizedClient("Ordering");
            var response = await client.GetAsync($"/api/orders/admin/support-chat/conversations/{conversationId}/details");
            var body = await response.Content.ReadAsStringAsync();

            if (!response.IsSuccessStatusCode)
            {
                return Json(new
                {
                    ok = false,
                    message = await ReadApiErrorAsync(response, "Khong the tai chi tiet hoi thoai")
                });
            }

            return Content(body, "application/json");
        }
        catch (Exception ex)
        {
            return Json(new { ok = false, message = "Loi: " + ex.Message });
        }
    }

    private async Task NotifyConversationUpdatedAsync(
        int conversationId,
        JsonElement? messagePayload = null,
        string eventType = "updated")
    {
        var sellerId = TryGetSellerIdFromClaims();
        if (!sellerId.HasValue)
        {
            return;
        }

        await _hubContext.Clients.Group(SupportChatHubGroups.Seller(sellerId.Value))
            .SendAsync("newConversationOrMessage", new
            {
                conversationId,
                eventType,
                at = DateTime.UtcNow
            });

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

    private int? TryGetSellerIdFromClaims()
    {
        var claim =
            User.FindFirst("sub")?.Value ??
            User.FindFirst(ClaimTypes.NameIdentifier)?.Value;

        return int.TryParse(claim, out var sellerId) ? sellerId : null;
    }

    private static JsonElement? TryExtractMessagePayload(string? json)
    {
        if (string.IsNullOrWhiteSpace(json))
        {
            return null;
        }

        try
        {
            using var document = JsonDocument.Parse(json);
            var root = document.RootElement;
            if (root.ValueKind != JsonValueKind.Object)
            {
                return null;
            }

            if (!root.TryGetProperty("message", out var message))
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

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Close(int conversationId)
    {
        try
        {
            if (conversationId <= 0)
            {
                return Json(new { ok = false, message = "conversationId khong hop le" });
            }

            var client = CreateAuthorizedClient("Ordering");
            var response = await client.PostAsync($"/api/orders/admin/support-chat/conversations/{conversationId}/close", content: null);
            var body = await response.Content.ReadAsStringAsync();

            if (!response.IsSuccessStatusCode)
            {
                return Json(new
                {
                    ok = false,
                    message = await ReadApiErrorAsync(response, "Khong the ket thuc hoi thoai")
                });
            }

            await NotifyConversationUpdatedAsync(conversationId, eventType: "closed");
            return Content(body, "application/json");
        }
        catch (Exception ex)
        {
            return Json(new { ok = false, message = "Loi: " + ex.Message });
        }
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> MarkAsRead(int conversationId)
    {
        try
        {
            if (conversationId <= 0)
            {
                return Json(new { ok = false, message = "conversationId khong hop le" });
            }

            var client = CreateAuthorizedClient("Ordering");
            var response = await client.PostAsync($"/api/orders/admin/support-chat/conversations/{conversationId}/mark-read", content: null);
            var body = await response.Content.ReadAsStringAsync();

            if (!response.IsSuccessStatusCode)
            {
                return Json(new
                {
                    ok = false,
                    message = await ReadApiErrorAsync(response, "Khong the cap nhat trang thai da doc")
                });
            }

            await NotifyConversationUpdatedAsync(conversationId, eventType: "read");
            return Content(body, "application/json");
        }
        catch (Exception ex)
        {
            return Json(new { ok = false, message = "Loi: " + ex.Message });
        }
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> SendMessage(int conversationId, string content, int? replyToMessageId = null)
    {
        try
        {
            if (conversationId <= 0)
            {
                return Json(new { ok = false, message = "conversationId khong hop le" });
            }

            content = (content ?? string.Empty).Trim();
            if (string.IsNullOrWhiteSpace(content))
            {
                return Json(new { ok = false, message = "Noi dung tin nhan khong duoc de trong." });
            }

            var client = CreateAuthorizedClient("Ordering");
            var response = await client.PostAsJsonAsync(
                $"/api/orders/admin/support-chat/conversations/{conversationId}/messages",
                new
                {
                    content,
                    replyToMessageId
                });

            var body = await response.Content.ReadAsStringAsync();
            if (!response.IsSuccessStatusCode)
            {
                return Json(new
                {
                    ok = false,
                    message = await ReadApiErrorAsync(response, "Khong the gui tin nhan")
                });
            }

            var messagePayload = TryExtractMessagePayload(body);
            await NotifyConversationUpdatedAsync(conversationId, messagePayload, eventType: "message");
            return Content(body, "application/json");
        }
        catch (Exception ex)
        {
            return Json(new { ok = false, message = "Loi: " + ex.Message });
        }
    }

    private HttpClient CreateAuthorizedClient(string clientName)
    {
        var client = _httpClientFactory.CreateClient(clientName);

        client.DefaultRequestHeaders.Remove("Authorization");
        var token = GetAccessToken(AccessTokenSessionKey);
        if (!string.IsNullOrWhiteSpace(token))
        {
            client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);
        }

        return client;
    }

    private static async Task<string> ReadApiErrorAsync(HttpResponseMessage response, string fallback)
    {
        try
        {
            var json = await response.Content.ReadAsStringAsync();
            if (string.IsNullOrWhiteSpace(json))
            {
                return fallback;
            }

            using var doc = JsonDocument.Parse(json);
            var root = doc.RootElement;

            if (root.ValueKind == JsonValueKind.Object)
            {
                if (root.TryGetProperty("message", out var messageProp) && messageProp.ValueKind == JsonValueKind.String)
                {
                    return messageProp.GetString() ?? fallback;
                }

                if (root.TryGetProperty("detail", out var detailProp) && detailProp.ValueKind == JsonValueKind.String)
                {
                    return detailProp.GetString() ?? fallback;
                }

                if (root.TryGetProperty("title", out var titleProp) && titleProp.ValueKind == JsonValueKind.String)
                {
                    return titleProp.GetString() ?? fallback;
                }
            }

            return json;
        }
        catch
        {
            return fallback;
        }
    }
}
