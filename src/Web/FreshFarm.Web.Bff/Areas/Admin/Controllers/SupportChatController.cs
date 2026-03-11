using FreshFarm.Web.Bff.Areas.Seller.Hubs;
using FreshFarm.Web.Bff.Areas.Seller.Infrastructure;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.SignalR;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;

namespace FreshFarm.Web.Bff.Areas.Admin.Controllers;

[Authorize(Policy = "AdminOnly")]
[ResponseCache(Duration = 0, Location = ResponseCacheLocation.None, NoStore = true)]
[Area("Admin")]
public sealed class SupportChatController : LegacySellerControllerBase
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
        ViewData["AreaName"] = "Admin";
        ViewData["LayoutPath"] = "~/Areas/Admin/Views/Shared/_LayoutAdmin.cshtml";
        ViewData["PageTitle"] = "Hỗ trợ khách hàng";
        ViewData["ScopeLabel"] = "Toàn sàn";
        ViewData["OrderPageUrl"] = Url.Action("ManageOrders", "Order", new { area = "Admin" }) ?? "/Admin/Order/ManageOrders";
        ViewData["ForcePolling"] = true;

        return View("~/Areas/Seller/Views/SupportChat/Index.cshtml");
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
                    message = await ReadApiErrorAsync(response, "Không thể tải danh sách hội thoại")
                });
            }

            return Content(body, "application/json");
        }
        catch (Exception ex)
        {
            return Json(new { ok = false, message = "Lỗi: " + ex.Message });
        }
    }

    [HttpGet]
    public async Task<IActionResult> Messages(int conversationId, int take = 100)
    {
        try
        {
            if (conversationId <= 0)
            {
                return Json(new { ok = false, message = "conversationId không hợp lệ" });
            }

            var client = CreateAuthorizedClient("Ordering");
            var response = await client.GetAsync($"/api/orders/admin/support-chat/conversations/{conversationId}/messages?take={take}");
            var body = await response.Content.ReadAsStringAsync();

            if (!response.IsSuccessStatusCode)
            {
                return Json(new
                {
                    ok = false,
                    message = await ReadApiErrorAsync(response, "Không thể tải tin nhắn")
                });
            }

            return Content(body, "application/json");
        }
        catch (Exception ex)
        {
            return Json(new { ok = false, message = "Lỗi: " + ex.Message });
        }
    }

    [HttpGet]
    public async Task<IActionResult> ConversationDetails(int conversationId)
    {
        try
        {
            if (conversationId <= 0)
            {
                return Json(new { ok = false, message = "conversationId không hợp lệ" });
            }

            var client = CreateAuthorizedClient("Ordering");
            var response = await client.GetAsync($"/api/orders/admin/support-chat/conversations/{conversationId}/details");
            var body = await response.Content.ReadAsStringAsync();

            if (!response.IsSuccessStatusCode)
            {
                return Json(new
                {
                    ok = false,
                    message = await ReadApiErrorAsync(response, "Không thể tải chi tiết hội thoại")
                });
            }

            return Content(body, "application/json");
        }
        catch (Exception ex)
        {
            return Json(new { ok = false, message = "Lỗi: " + ex.Message });
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
                return Json(new { ok = false, message = "conversationId không hợp lệ" });
            }

            var client = CreateAuthorizedClient("Ordering");
            var response = await client.PostAsync($"/api/orders/admin/support-chat/conversations/{conversationId}/close", content: null);
            var body = await response.Content.ReadAsStringAsync();

            if (!response.IsSuccessStatusCode)
            {
                return Json(new
                {
                    ok = false,
                    message = await ReadApiErrorAsync(response, "Không thể kết thúc hội thoại")
                });
            }

            await _hubContext.Clients.Group(SupportChatHubGroups.Conversation(conversationId))
                .SendAsync("newConversationOrMessage", new
                {
                    conversationId,
                    eventType = "closed",
                    at = DateTime.UtcNow
                });

            return Content(body, "application/json");
        }
        catch (Exception ex)
        {
            return Json(new { ok = false, message = "Lỗi: " + ex.Message });
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
                return Json(new { ok = false, message = "conversationId không hợp lệ" });
            }

            var client = CreateAuthorizedClient("Ordering");
            var response = await client.PostAsync($"/api/orders/admin/support-chat/conversations/{conversationId}/mark-read", content: null);
            var body = await response.Content.ReadAsStringAsync();

            if (!response.IsSuccessStatusCode)
            {
                return Json(new
                {
                    ok = false,
                    message = await ReadApiErrorAsync(response, "Không thể cập nhật trạng thái đã đọc")
                });
            }

            return Content(body, "application/json");
        }
        catch (Exception ex)
        {
            return Json(new { ok = false, message = "Lỗi: " + ex.Message });
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
                return Json(new { ok = false, message = "conversationId không hợp lệ" });
            }

            content = (content ?? string.Empty).Trim();
            if (string.IsNullOrWhiteSpace(content))
            {
                return Json(new { ok = false, message = "Nội dung tin nhắn không được để trống." });
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
                    message = await ReadApiErrorAsync(response, "Không thể gửi tin nhắn")
                });
            }

            var messagePayload = TryExtractMessagePayload(body);
            if (messagePayload.HasValue)
            {
                await _hubContext.Clients.Group(SupportChatHubGroups.Conversation(conversationId))
                    .SendAsync("receiveMessage", new
                    {
                        conversationId,
                        message = messagePayload.Value
                    });
            }

            return Content(body, "application/json");
        }
        catch (Exception ex)
        {
            return Json(new { ok = false, message = "Lỗi: " + ex.Message });
        }
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
