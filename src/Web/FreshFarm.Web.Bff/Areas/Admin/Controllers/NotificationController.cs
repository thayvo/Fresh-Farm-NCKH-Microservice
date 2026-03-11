using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using FreshFarm.Web.Bff.Areas.Admin.Models;
using FreshFarm.Web.Bff.Areas.Seller.Infrastructure;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace FreshFarm.Web.Bff.Areas.Admin.Controllers;

[Authorize(Policy = "AdminOnly")]
[ResponseCache(Duration = 0, Location = ResponseCacheLocation.None, NoStore = true)]
[Area("Admin")]
public sealed class NotificationController : LegacySellerControllerBase
{
    private const string AccessTokenSessionKey = "ACCESS_TOKEN";
    private readonly IHttpClientFactory _httpClientFactory;

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true
    };

    public NotificationController(IHttpClientFactory httpClientFactory)
    {
        _httpClientFactory = httpClientFactory;
    }

    [HttpGet]
    public async Task<IActionResult> Index(
        string? q = null,
        string? type = null,
        bool? isRead = null,
        int? userId = null,
        int? orderId = null,
        int page = 1)
    {
        var model = new NotificationCenterPageViewModel
        {
            Query = q ?? string.Empty,
            Type = type ?? string.Empty,
            IsRead = isRead,
            UserId = userId,
            OrderId = orderId,
            Page = page < 1 ? 1 : page
        };

        try
        {
            var client = CreateOrderingClient();
            var query = new List<string>
            {
                $"page={model.Page}",
                $"pageSize={model.PageSize}"
            };

            if (!string.IsNullOrWhiteSpace(model.Query))
            {
                query.Add($"q={Uri.EscapeDataString(model.Query)}");
            }

            if (!string.IsNullOrWhiteSpace(model.Type))
            {
                query.Add($"type={Uri.EscapeDataString(model.Type)}");
            }

            if (model.IsRead.HasValue)
            {
                query.Add($"isRead={model.IsRead.Value.ToString().ToLowerInvariant()}");
            }

            if (model.UserId.HasValue && model.UserId.Value > 0)
            {
                query.Add($"userId={model.UserId.Value}");
            }

            if (model.OrderId.HasValue && model.OrderId.Value > 0)
            {
                query.Add($"orderId={model.OrderId.Value}");
            }

            var response = await client.GetAsync("/api/orders/admin/notifications?" + string.Join("&", query));
            if (!response.IsSuccessStatusCode)
            {
                ViewBag.Error = await ReadApiErrorAsync(response, "Không thể tải trung tâm thông báo.");
                return View(model);
            }

            var payload = await response.Content.ReadFromJsonAsync<NotificationCenterApiResponse>(JsonOptions);
            if (payload is null)
            {
                ViewBag.Error = "Không đọc được dữ liệu trung tâm thông báo.";
                return View(model);
            }

            model.Page = payload.Page;
            model.PageSize = payload.PageSize;
            model.Total = payload.Total;
            model.TotalPages = payload.TotalPages <= 0 ? 1 : payload.TotalPages;
            model.TotalNotifications = payload.Stats?.TotalNotifications ?? 0;
            model.UnreadNotifications = payload.Stats?.UnreadNotifications ?? 0;
            model.PushNotifications = payload.Stats?.PushNotifications ?? 0;
            model.Recent24hNotifications = payload.Stats?.Recent24hNotifications ?? 0;
            model.TypeOptions = payload.Filters?.TypeOptions ?? new List<string>();
            model.Rows = payload.Notifications?.Select(x => new NotificationCenterRowViewModel
            {
                NotificationId = x.NotificationId,
                UserId = x.UserId,
                OrderId = x.OrderId,
                NotificationType = x.NotificationType ?? string.Empty,
                Title = x.Title ?? string.Empty,
                Message = x.Message ?? string.Empty,
                IsRead = x.IsRead,
                IsPushNotification = x.IsPushNotification,
                CreatedAt = x.CreatedAt,
                ReadAt = x.ReadAt
            }).ToList() ?? new List<NotificationCenterRowViewModel>();
        }
        catch (Exception ex)
        {
            ViewBag.Error = "Lỗi khi tải trung tâm thông báo: " + ex.Message;
        }

        return View(model);
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> MarkAsRead(int id, string? q, string? type, bool? isRead, int? userId, int? orderId, int page = 1)
    {
        if (id <= 0)
        {
            TempData["ErrorMessage"] = "ID thông báo không hợp lệ.";
            return RedirectToIndex(q, type, isRead, userId, orderId, page);
        }

        try
        {
            var client = CreateOrderingClient();
            var response = await client.PostAsync($"/api/orders/admin/notifications/{id}/mark-read", content: null);
            TempData[response.IsSuccessStatusCode ? "SuccessMessage" : "ErrorMessage"] =
                response.IsSuccessStatusCode
                    ? "Đã đánh dấu đã đọc."
                    : await ReadApiErrorAsync(response, "Không thể cập nhật trạng thái thông báo.");
        }
        catch (Exception ex)
        {
            TempData["ErrorMessage"] = "Lỗi khi cập nhật thông báo: " + ex.Message;
        }

        return RedirectToIndex(q, type, isRead, userId, orderId, page);
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> MarkAllAsRead(string? q, string? type, bool? isRead, int? userId, int? orderId, int page = 1)
    {
        try
        {
            var client = CreateOrderingClient();
            var response = await client.PostAsJsonAsync("/api/orders/admin/notifications/mark-all-read", new
            {
                q,
                type,
                isRead,
                userId,
                orderId
            });

            TempData[response.IsSuccessStatusCode ? "SuccessMessage" : "ErrorMessage"] =
                response.IsSuccessStatusCode
                    ? await ReadApiSuccessAsync(response, "Đã cập nhật toàn bộ thông báo trong phạm vi lọc.")
                    : await ReadApiErrorAsync(response, "Không thể cập nhật toàn bộ thông báo.");
        }
        catch (Exception ex)
        {
            TempData["ErrorMessage"] = "Lỗi khi cập nhật thông báo: " + ex.Message;
        }

        return RedirectToIndex(q, type, isRead, userId, orderId, page);
    }

    private RedirectToActionResult RedirectToIndex(string? q, string? type, bool? isRead, int? userId, int? orderId, int page)
    {
        return RedirectToAction(nameof(Index), new
        {
            q,
            type,
            isRead,
            userId,
            orderId,
            page
        });
    }

    private HttpClient CreateOrderingClient()
    {
        var client = _httpClientFactory.CreateClient("Ordering");
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
        var body = await response.Content.ReadAsStringAsync();
        if (string.IsNullOrWhiteSpace(body))
        {
            return fallback;
        }

        try
        {
            using var doc = JsonDocument.Parse(body);
            var root = doc.RootElement;

            if (root.TryGetProperty("message", out var messageElement) && messageElement.ValueKind == JsonValueKind.String)
            {
                return messageElement.GetString() ?? fallback;
            }
        }
        catch
        {
        }

        return fallback;
    }

    private static async Task<string> ReadApiSuccessAsync(HttpResponseMessage response, string fallback)
    {
        var body = await response.Content.ReadAsStringAsync();
        if (string.IsNullOrWhiteSpace(body))
        {
            return fallback;
        }

        try
        {
            using var doc = JsonDocument.Parse(body);
            var root = doc.RootElement;

            if (root.TryGetProperty("message", out var messageElement) && messageElement.ValueKind == JsonValueKind.String)
            {
                return messageElement.GetString() ?? fallback;
            }
        }
        catch
        {
        }

        return fallback;
    }
}
