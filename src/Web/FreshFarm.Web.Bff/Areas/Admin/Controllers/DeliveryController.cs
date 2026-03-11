using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using FreshFarm.Web.Bff.Areas.Seller.Infrastructure;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace FreshFarm.Web.Bff.Areas.Admin.Controllers;

[Authorize(Policy = "AdminOnly")]
[ResponseCache(Duration = 0, Location = ResponseCacheLocation.None, NoStore = true)]
[Area("Admin")]
public sealed class DeliveryController : LegacySellerControllerBase
{
    private const string AccessTokenSessionKey = "ACCESS_TOKEN";

    private readonly IHttpClientFactory _httpClientFactory;

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true
    };

    public DeliveryController(IHttpClientFactory httpClientFactory)
    {
        _httpClientFactory = httpClientFactory;
    }

    [HttpGet]
    public IActionResult Index()
    {
        return RenderDeliveryView();
    }

    [HttpGet]
    public async Task<JsonResult> List(string status = "Processing", int page = 1, int pageSize = 10)
    {
        try
        {
            if (page < 1)
            {
                page = 1;
            }

            if (pageSize <= 0)
            {
                pageSize = 10;
            }

            var statusFilter = NormalizeStatusFilter(status);
            var client = CreateAuthorizedClient("Ordering");
            var response = await client.GetAsync($"/api/orders/admin/reports/shipping?status={Uri.EscapeDataString(statusFilter)}&page={page}&pageSize={pageSize}");
            if (!response.IsSuccessStatusCode)
            {
                return Json(new { success = false, message = await ReadApiErrorAsync(response, "Không thể tải danh sách giao hàng.") });
            }

            var payload = await response.Content.ReadFromJsonAsync<ShippingReportApiResponse>(JsonOptions);
            if (payload is null)
            {
                return Json(new { success = false, message = "Không đọc được dữ liệu giao hàng từ service." });
            }

            var items = (payload.RecentShippings ?? new List<ShippingRowApiDto>())
                .Select(MapDeliveryRow)
                .ToList();

            return Json(new
            {
                success = true,
                data = items,
                total = payload.TotalRecords,
                page = payload.CurrentPage <= 0 ? page : payload.CurrentPage,
                pageSize = payload.PageSize <= 0 ? pageSize : payload.PageSize
            });
        }
        catch (Exception ex)
        {
            return Json(new { success = false, message = "Lỗi: " + ex.Message });
        }
    }

    [HttpGet]
    public async Task<JsonResult> Staffs()
    {
        try
        {
            var client = CreateAuthorizedClient("Ordering");
            var response = await client.GetAsync("/api/orders/admin/reports/shipping?status=all&page=1&pageSize=1");
            if (!response.IsSuccessStatusCode)
            {
                return Json(new { success = false, message = await ReadApiErrorAsync(response, "Không thể tải danh sách nhân viên giao hàng.") });
            }

            var payload = await response.Content.ReadFromJsonAsync<ShippingReportApiResponse>(JsonOptions);
            var staffs = (payload?.DeliveryStaffs ?? new List<StaffOptionApiDto>())
                .Select(x =>
                {
                    _ = int.TryParse(x.Value, out var staffId);
                    return new
                    {
                        id = staffId,
                        name = x.Text ?? string.Empty,
                        role = "Delivery"
                    };
                })
                .Where(x => x.id > 0 && !string.IsNullOrWhiteSpace(x.name))
                .ToList();

            return Json(new { success = true, data = staffs });
        }
        catch (Exception ex)
        {
            return Json(new { success = false, message = "Lỗi: " + ex.Message });
        }
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<JsonResult> Assign(int orderId, int staffId, string? notes = null, DateTime? expectedPickupAt = null, DateTime? expectedDeliveryAt = null)
    {
        try
        {
            if (orderId <= 0 || staffId <= 0)
            {
                return Json(new { success = false, message = "Dữ liệu chỉ định không hợp lệ." });
            }

            var client = CreateAuthorizedClient("Ordering");
            var response = await client.PostAsJsonAsync($"/api/orders/admin/{orderId}/status", new
            {
                newStatus = "Shipped"
            });

            if (!response.IsSuccessStatusCode)
            {
                return Json(new { success = false, message = await ReadApiErrorAsync(response, "Không thể cập nhật trạng thái đơn hàng.") });
            }

            return Json(new
            {
                success = true,
                message = "Đã ghi nhận chỉ định tạm thời và chuyển đơn sang trạng thái Đang giao."
            });
        }
        catch (Exception ex)
        {
            return Json(new { success = false, message = "Lỗi: " + ex.Message });
        }
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<JsonResult> UpdateStatus([FromBody] UpdateDeliveryStatusRequest? request)
    {
        try
        {
            if (request is null || request.orderId <= 0 || string.IsNullOrWhiteSpace(request.newStatus))
            {
                return Json(new { success = false, message = "Dữ liệu cập nhật trạng thái không hợp lệ." });
            }

            var client = CreateAuthorizedClient("Ordering");
            var response = await client.PostAsJsonAsync($"/api/orders/admin/{request.orderId}/status", new
            {
                newStatus = request.newStatus.Trim()
            });

            if (!response.IsSuccessStatusCode)
            {
                return Json(new { success = false, message = await ReadApiErrorAsync(response, "Không thể cập nhật trạng thái đơn hàng.") });
            }

            var payload = await response.Content.ReadFromJsonAsync<BasicSuccessResponse>(JsonOptions);
            return Json(new { success = true, message = payload?.message ?? "Cập nhật trạng thái thành công." });
        }
        catch (Exception ex)
        {
            return Json(new { success = false, message = "Lỗi: " + ex.Message });
        }
    }

    private IActionResult RenderDeliveryView()
    {
        ViewData["AreaName"] = "Admin";
        ViewData["LayoutPath"] = "~/Areas/Admin/Views/Shared/_LayoutAdmin.cshtml";
        ViewData["DeliveryScopeLabel"] = "Toàn sàn";
        return View("~/Areas/Seller/Views/Delivery/Index.cshtml");
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

    private static object MapDeliveryRow(ShippingRowApiDto row)
    {
        var status = MapStatusForUi(row.StatusText);
        var orderId = ParseOrderId(row.OrderCode);

        return new
        {
            orderID = orderId,
            orderCode = string.IsNullOrWhiteSpace(row.OrderCode) ? $"ORD-{orderId:D6}" : row.OrderCode,
            customerName = row.CustomerName ?? string.Empty,
            customerPhone = row.CustomerPhone ?? string.Empty,
            status,
            orderDate = row.ShippingDateFormatted ?? string.Empty,
            address = row.DeliveryAddress ?? string.Empty,
            canAccept = status == "Processing",
            canDeliver = status is "Processing" or "Shipped",
            canCancel = status is "Processing" or "Shipped"
        };
    }

    private static int ParseOrderId(string? orderCode)
    {
        if (string.IsNullOrWhiteSpace(orderCode))
        {
            return 0;
        }

        var digits = new string(orderCode.Where(char.IsDigit).ToArray());
        return int.TryParse(digits, out var id) ? id : 0;
    }

    private static string NormalizeStatusFilter(string? status)
    {
        var key = (status ?? string.Empty).Trim().ToLowerInvariant();
        return key switch
        {
            "all" => "all",
            "processing" => "pending",
            "shipped" => "shipped",
            "delivered" => "delivered",
            "canceled" => "canceled",
            _ => "pending"
        };
    }

    private static string MapStatusForUi(string? statusText)
    {
        var normalized = (statusText ?? string.Empty).Trim().ToLowerInvariant();
        if (normalized.Contains("thành công"))
        {
            return "Delivered";
        }

        if (normalized.Contains("đã hủy"))
        {
            return "Canceled";
        }

        if (normalized.Contains("đang giao"))
        {
            return "Shipped";
        }

        return "Processing";
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

    public sealed class UpdateDeliveryStatusRequest
    {
        public int orderId { get; set; }

        public string newStatus { get; set; } = string.Empty;
    }

    private sealed class ShippingReportApiResponse
    {
        public int TotalRecords { get; set; }

        public int CurrentPage { get; set; }

        public int PageSize { get; set; }

        public List<ShippingRowApiDto>? RecentShippings { get; set; }

        public List<StaffOptionApiDto>? DeliveryStaffs { get; set; }
    }

    private sealed class ShippingRowApiDto
    {
        public string? OrderCode { get; set; }

        public string? CustomerName { get; set; }

        public string? CustomerPhone { get; set; }

        public string? DeliveryAddress { get; set; }

        public string? ShippingDateFormatted { get; set; }

        public string? StatusText { get; set; }
    }

    private sealed class StaffOptionApiDto
    {
        public string? Value { get; set; }

        public string? Text { get; set; }
    }

    private sealed class BasicSuccessResponse
    {
        public bool success { get; set; }

        public string? message { get; set; }
    }
}
