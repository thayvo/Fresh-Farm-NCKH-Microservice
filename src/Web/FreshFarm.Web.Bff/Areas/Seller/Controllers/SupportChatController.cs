using FreshFarm.Web.Bff.Areas.Seller.Infrastructure;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using System.Net.Http.Headers;
using System.Text.Json;

namespace FreshFarm.Web.Bff.Areas.Seller.Controllers;

[Authorize(Roles = "Seller")]
[ResponseCache(Duration = 0, Location = ResponseCacheLocation.None, NoStore = true)]
[Area("Seller")]
public class SupportChatController : LegacySellerControllerBase
{
    private const string AccessTokenSessionKey = "ACCESS_TOKEN";

    private readonly IHttpClientFactory _httpClientFactory;

    public SupportChatController(IHttpClientFactory httpClientFactory)
    {
        _httpClientFactory = httpClientFactory;
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

    [HttpPost]
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

            return Content(body, "application/json");
        }
        catch (Exception ex)
        {
            return Json(new { ok = false, message = "Loi: " + ex.Message });
        }
    }

    [HttpPost]
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
        var token = HttpContext.Session.GetString(AccessTokenSessionKey);
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
