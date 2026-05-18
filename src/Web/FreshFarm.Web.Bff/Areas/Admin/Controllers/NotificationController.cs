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
                ExpiresAt = x.ExpiresAt,
                ReadAt = x.ReadAt
            }).ToList() ?? new List<NotificationCenterRowViewModel>();
        }
        catch (Exception ex)
        {
            ViewBag.Error = "Lỗi khi tải trung tâm thông báo: " + ex.Message;
        }

        return View(model);
    }

    [HttpGet]
    public IActionResult CreateBroadcast()
    {
        return View(new NotificationBroadcastEditorInput());
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> CreateBroadcast(NotificationBroadcastEditorInput input)
    {
        input.TargetAudience = NotificationBroadcastEditorInput.NormalizeTargetAudience(input.TargetAudience);
        input.PopupType = NotificationBroadcastEditorInput.NormalizePopupType(input.PopupType, input.ShowPopup);
        input.PopupImageUrl = input.PopupImageUrl?.Trim() ?? string.Empty;

        if (string.Equals(input.TargetAudience, "user", StringComparison.OrdinalIgnoreCase)
            && (!input.TargetUserId.HasValue || input.TargetUserId.Value <= 0))
        {
            ModelState.AddModelError(nameof(input.TargetUserId), "Vui lòng nhập User ID cần gửi.");
        }

        if (input.ShowPopup
            && string.Equals(input.PopupType, "image", StringComparison.OrdinalIgnoreCase)
            && string.IsNullOrWhiteSpace(input.PopupImageUrl))
        {
            ModelState.AddModelError(nameof(input.PopupImageUrl), "Vui lòng nhập URL ảnh popup.");
        }

        if (input.ExpiresAt.HasValue && input.ExpiresAt.Value <= DateTime.Now)
        {
            ModelState.AddModelError(nameof(input.ExpiresAt), "Thời gian hết hạn phải ở tương lai.");
        }

        if (!ModelState.IsValid)
        {
            return View(input);
        }

        try
        {
            var userIds = await ResolveBroadcastUserIdsAsync(input);
            if (userIds.Count == 0)
            {
                ModelState.AddModelError(string.Empty, "Không tìm thấy người nhận phù hợp với đối tượng đã chọn.");
                return View(input);
            }

            var client = CreateOrderingClient();
            var response = await client.PostAsJsonAsync("/api/orders/admin/notifications/broadcast", new NotificationBroadcastApiRequest
            {
                UserIds = userIds,
                NotificationType = input.NotificationType,
                Title = input.Title,
                Message = input.Message,
                ShowPopup = input.ShowPopup,
                PopupType = input.PopupType,
                PopupImageUrl = input.PopupType == "image" ? input.PopupImageUrl : null,
                ExpiresAt = input.ExpiresAt?.ToUniversalTime()
            });

            if (!response.IsSuccessStatusCode)
            {
                ModelState.AddModelError(string.Empty, await ReadApiErrorAsync(response, "Không thể gửi thông báo broadcast."));
                return View(input);
            }

            TempData["SuccessMessage"] = await ReadApiSuccessAsync(response, $"Đã gửi thông báo đến {userIds.Count} người nhận.");
            return RedirectToAction(nameof(CreateBroadcast));
        }
        catch (Exception ex)
        {
            ModelState.AddModelError(string.Empty, "Lỗi khi gửi thông báo broadcast: " + ex.Message);
            return View(input);
        }
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

    private async Task<List<int>> ResolveBroadcastUserIdsAsync(NotificationBroadcastEditorInput input)
    {
        if (string.Equals(input.TargetAudience, "user", StringComparison.OrdinalIgnoreCase))
        {
            return input.TargetUserId.HasValue && input.TargetUserId.Value > 0
                ? new List<int> { input.TargetUserId.Value }
                : new List<int>();
        }

        if (string.Equals(input.TargetAudience, "all", StringComparison.OrdinalIgnoreCase))
        {
            var buyersTask = FetchActiveUserIdsByTypeAsync("buyer");
            var sellersTask = FetchActiveUserIdsByTypeAsync("seller");
            await Task.WhenAll(buyersTask, sellersTask);

            return buyersTask.Result
                .Concat(sellersTask.Result)
                .Distinct()
                .ToList();
        }

        return await FetchActiveUserIdsByTypeAsync(input.TargetAudience);
    }

    private async Task<List<int>> FetchActiveUserIdsByTypeAsync(string userType)
    {
        var client = CreateIdentityClient();
        var query = "take=10000&isActive=true&userType=" + Uri.EscapeDataString(userType);

        var response = await client.GetAsync("/auth/admin/users?" + query);
        if (!response.IsSuccessStatusCode)
        {
            throw new InvalidOperationException(await ReadApiErrorAsync(response, "Không thể lấy danh sách người nhận."));
        }

        var users = await response.Content.ReadFromJsonAsync<List<AdminBroadcastUserApiDto>>(JsonOptions)
            ?? new List<AdminBroadcastUserApiDto>();

        return users
            .Where(user => user.userId > 0)
            .Select(user => user.userId)
            .Distinct()
            .ToList();
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

    private HttpClient CreateIdentityClient()
    {
        var client = _httpClientFactory.CreateClient("Identity");
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

    private sealed class AdminBroadcastUserApiDto
    {
        public int userId { get; set; }
    }

    private sealed class NotificationBroadcastApiRequest
    {
        public List<int> UserIds { get; set; } = new();

        public string NotificationType { get; set; } = string.Empty;

        public string Title { get; set; } = string.Empty;

        public string Message { get; set; } = string.Empty;

        public bool ShowPopup { get; set; }

        public string PopupType { get; set; } = "text";

        public string? PopupImageUrl { get; set; }

        public DateTime? ExpiresAt { get; set; }
    }
}
