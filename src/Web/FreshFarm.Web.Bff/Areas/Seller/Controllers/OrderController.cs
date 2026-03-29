using FreshFarm.Web.Bff.Areas.Seller.Infrastructure;
using FreshFarm.Web.Bff.Areas.Seller.Models;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;

namespace FreshFarm.Web.Bff.Areas.Seller.Controllers;

[Authorize(Roles = "Seller")]
[ResponseCache(Duration = 0, Location = ResponseCacheLocation.None, NoStore = true)]
[Area("Seller")]
public class OrderController : LegacySellerControllerBase
{
    private const string AccessTokenSessionKey = "ACCESS_TOKEN";
    private readonly IHttpClientFactory _httpClientFactory;

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true
    };

    public OrderController(IHttpClientFactory httpClientFactory)
    {
        _httpClientFactory = httpClientFactory;
    }

    public sealed class UpdateOrderStatusDto
    {
        public int orderId { get; set; }

        public string newStatus { get; set; } = string.Empty;
    }

    public sealed class DeleteOrderDto
    {
        public int orderId { get; set; }
    }

    public sealed class BulkUpdateStatusDto
    {
        public int[] orderIds { get; set; } = Array.Empty<int>();

        public string newStatus { get; set; } = string.Empty;
    }

    public sealed class CancelOrderDto
    {
        public int orderId { get; set; }

        public string reason { get; set; } = string.Empty;
    }

    public IActionResult ManageOrders(int page = 1)
    {
        return View();
    }

    [HttpGet]
    public async Task<JsonResult> GetAllOrders()
    {
        var response = await GetOrdersFromApiAsync(page: 1, pageSize: 500, null, null, null);
        if (response.Payload is null)
        {
            return Json(new { success = false, message = response.ErrorMessage ?? "Lỗi khi tải danh sách đơn hàng." });
        }

        var allowedOrderIds = await GetAllowedOrderIdsAsync();
        if (allowedOrderIds.Payload is null)
        {
            return Json(new { success = false, message = allowedOrderIds.ErrorMessage ?? "Lỗi xác thực phạm vi dữ liệu seller." });
        }

        var rows = FilterRowsByAllowedOrderIds(NormalizeOrderRows(response.Payload.data), allowedOrderIds.Payload);
        return Json(new { success = true, data = rows });
    }

    [HttpGet]
    public async Task<JsonResult> GetOrderDetail(int orderId)
    {
        if (!await CanAccessOrderAsync(orderId))
        {
            return Json(new { success = false, message = "Bạn không có quyền xem đơn hàng này." });
        }

        var client = CreateOrderingClient();
        var response = await client.GetAsync($"/api/orders/admin/{orderId}/detail");

        return await ToJsonResultAsync(response, "Lỗi khi tải chi tiết đơn hàng.");
    }

    [HttpGet]
    public async Task<IActionResult> PrintInvoice(int orderId)
    {
        if (!await CanAccessOrderAsync(orderId))
        {
            return Forbid();
        }

        var client = CreateOrderingClient();
        var response = await client.GetAsync($"/api/orders/admin/{orderId}/detail");

        if (!response.IsSuccessStatusCode)
        {
            return StatusCode((int)response.StatusCode, await ReadApiErrorAsync(response, "Không thể tải dữ liệu in hóa đơn."));
        }

        var detail = await response.Content.ReadFromJsonAsync<AdminOrderDetailDto>(JsonOptions);
        if (detail is null || !detail.success)
        {
            return StatusCode(500, "Không đọc được dữ liệu hóa đơn.");
        }

        var model = new SellerOrderInvoiceViewModel
        {
            OrderID = detail.orderId,
            OrderDate = detail.orderDate,
            Status = detail.status ?? detail.statusCode ?? string.Empty,
            PaymentStatus = detail.paymentStatus,
            OrderNote = detail.orderNote,
            CustomerName = detail.customerName,
            BuyerFullName = detail.buyerFullName,
            BuyerPhone = detail.buyerPhone,
            BuyerEmail = detail.buyerEmail,
            Address = detail.address,
            ShippingFee = detail.shippingFee,
            TotalAmount = detail.total,
            OrderDetails = detail.items?.Select(it => new SellerOrderInvoiceItemViewModel
            {
                ProductID = it.productId,
                ProductName = string.IsNullOrWhiteSpace(it.productName) ? $"#P{it.productId}" : it.productName,
                Quantity = it.quantity,
                UnitPrice = it.unitPrice,
                UnitSymbol = it.unitSymbol
            }).ToList() ?? new List<SellerOrderInvoiceItemViewModel>()
        };

        return View(model);
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<JsonResult> UpdateOrderStatus(UpdateOrderStatusDto req)
    {
        if (req is null || req.orderId <= 0 || string.IsNullOrWhiteSpace(req.newStatus))
        {
            return Json(new { success = false, message = "Dữ liệu không hợp lệ." });
        }

        if (!await CanAccessOrderAsync(req.orderId))
        {
            return Json(new { success = false, message = "Bạn không có quyền cập nhật đơn hàng này." });
        }

        var client = CreateOrderingClient();
        var response = await client.PostAsJsonAsync($"/api/orders/admin/{req.orderId}/status", new
        {
            newStatus = req.newStatus
        });

        return await ToJsonResultAsync(response, "Lỗi khi cập nhật trạng thái đơn hàng.");
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<JsonResult> DeleteOrder(DeleteOrderDto req)
    {
        if (req is null || req.orderId <= 0)
        {
            return Json(new { success = false, message = "Dữ liệu không hợp lệ." });
        }

        if (!await CanAccessOrderAsync(req.orderId))
        {
            return Json(new { success = false, message = "Bạn không có quyền xóa đơn hàng này." });
        }

        var client = CreateOrderingClient();
        var response = await client.DeleteAsync($"/api/orders/admin/{req.orderId}");

        return await ToJsonResultAsync(response, "Lỗi khi xóa đơn hàng.");
    }

    [HttpGet]
    public async Task<JsonResult> GetOrdersPaged(int page = 1, int pageSize = 10)
    {
        var response = await GetOrdersFromApiAsync(page, pageSize, null, null, null);
        if (response.Payload is null)
        {
            return Json(new { success = false, message = response.ErrorMessage ?? "Lỗi khi tải danh sách đơn hàng." });
        }

        var allowedOrderIds = await GetAllowedOrderIdsAsync();
        if (allowedOrderIds.Payload is null)
        {
            return Json(new { success = false, message = allowedOrderIds.ErrorMessage ?? "Lỗi xác thực phạm vi dữ liệu seller." });
        }

        var rows = FilterRowsByAllowedOrderIds(NormalizeOrderRows(response.Payload.data), allowedOrderIds.Payload);
        return Json(new
        {
            success = true,
            data = rows,
            page = response.Payload.page,
            pageSize = response.Payload.pageSize,
            total = Math.Min(response.Payload.total, allowedOrderIds.Payload.Count)
        });
    }

    [HttpGet]
    public async Task<JsonResult> SearchOrders(string searchTerm, string statusFilter, string dateFilter)
    {
        var response = await GetOrdersFromApiAsync(page: 1, pageSize: 100, searchTerm, statusFilter, dateFilter);
        if (response.Payload is null)
        {
            return Json(new { success = false, message = response.ErrorMessage ?? "Lỗi khi tìm kiếm đơn hàng." });
        }

        var allowedOrderIds = await GetAllowedOrderIdsAsync();
        if (allowedOrderIds.Payload is null)
        {
            return Json(new { success = false, message = allowedOrderIds.ErrorMessage ?? "Lỗi xác thực phạm vi dữ liệu seller." });
        }

        var rows = FilterRowsByAllowedOrderIds(NormalizeOrderRows(response.Payload.data), allowedOrderIds.Payload);
        return Json(new { success = true, data = rows });
    }

    [HttpGet]
    public async Task<JsonResult> SearchOrdersPaged(string searchTerm, string statusFilter, string dateFilter, int page = 1, int pageSize = 10)
    {
        var response = await GetOrdersFromApiAsync(page, pageSize, searchTerm, statusFilter, dateFilter);
        if (response.Payload is null)
        {
            return Json(new { success = false, message = response.ErrorMessage ?? "Lỗi khi tìm kiếm đơn hàng." });
        }

        var allowedOrderIds = await GetAllowedOrderIdsAsync();
        if (allowedOrderIds.Payload is null)
        {
            return Json(new { success = false, message = allowedOrderIds.ErrorMessage ?? "Lỗi xác thực phạm vi dữ liệu seller." });
        }

        var rows = FilterRowsByAllowedOrderIds(NormalizeOrderRows(response.Payload.data), allowedOrderIds.Payload);
        return Json(new
        {
            success = true,
            data = rows,
            page = response.Payload.page,
            pageSize = response.Payload.pageSize,
            total = Math.Min(response.Payload.total, allowedOrderIds.Payload.Count)
        });
    }

    [HttpGet]
    public async Task<JsonResult> GetStatistics()
    {
        var client = CreateOrderingClient();
        var response = await client.GetAsync("/api/orders/admin/statistics");

        if (!response.IsSuccessStatusCode)
        {
            return Json(new { success = false, message = await ReadApiErrorAsync(response, "Lỗi khi tải thống kê.") });
        }

        var payload = await response.Content.ReadFromJsonAsync<AdminStatisticsResponse>(JsonOptions);
        if (payload?.success != true || payload.data is null)
        {
            return Json(new { success = false, message = "Không đọc được dữ liệu thống kê." });
        }

        var d = payload.data;

        return Json(new
        {
            success = true,
            data = new
            {
                totalOrders = d.totalOrders,
                pendingOrders = d.pendingOrders,
                processingOrders = d.processingOrders,
                shippedOrders = d.shippedOrders,
                deliveredOrders = d.deliveredOrders,
                canceledOrders = d.canceledOrders,
                totalRevenue = string.Format("{0:N0}", d.totalRevenue),
                todayRevenue = string.Format("{0:N0}", d.todayRevenue),
                monthRevenue = string.Format("{0:N0}", d.monthRevenue),
                todayOrders = d.todayOrders,
                thisWeekOrders = d.thisWeekOrders,
                thisMonthOrders = d.thisMonthOrders,
                averageOrderValue = string.Format("{0:N0}", d.averageOrderValue),
                deliveryRate = string.Format("{0:0.##}%", d.deliveryRate),
                cancelRate = string.Format("{0:0.##}%", d.cancelRate)
            }
        });
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<JsonResult> BulkUpdateStatus(BulkUpdateStatusDto req)
    {
        if (req?.orderIds is null || req.orderIds.Length == 0 || string.IsNullOrWhiteSpace(req.newStatus))
        {
            return Json(new { success = false, message = "Vui lòng chọn ít nhất một đơn hàng và trạng thái hợp lệ." });
        }

        var allowedOrderIds = await GetAllowedOrderIdsAsync();
        if (allowedOrderIds.Payload is null)
        {
            return Json(new { success = false, message = allowedOrderIds.ErrorMessage ?? "Lỗi xác thực phạm vi dữ liệu seller." });
        }

        var requestedOrderIds = req.orderIds.Distinct().ToList();
        var permittedOrderIds = requestedOrderIds.Where(allowedOrderIds.Payload.Contains).ToList();
        if (permittedOrderIds.Count == 0)
        {
            return Json(new { success = false, message = "Không có đơn hàng hợp lệ trong phạm vi của bạn." });
        }

        var successCount = 0;
        var failCount = 0;
        var errors = new List<string>();

        var client = CreateOrderingClient();
        foreach (var orderId in permittedOrderIds)
        {
            var response = await client.PostAsJsonAsync($"/api/orders/admin/{orderId}/status", new { newStatus = req.newStatus });
            if (response.IsSuccessStatusCode)
            {
                successCount++;
                continue;
            }

            failCount++;
            errors.Add($"Đơn #{orderId}: {await ReadApiErrorAsync(response, "không thể cập nhật")}");
        }

        var blockedCount = requestedOrderIds.Count - permittedOrderIds.Count;
        if (blockedCount > 0)
        {
            failCount += blockedCount;
            errors.Add($"{blockedCount} đơn hàng không thuộc phạm vi seller hiện tại.");
        }

        return Json(new
        {
            success = true,
            message = $"Cập nhật thành công {successCount} đơn hàng" + (failCount > 0 ? $", {failCount} đơn thất bại" : string.Empty),
            successCount,
            failCount,
            errors
        });
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<JsonResult> CancelOrder(CancelOrderDto req)
    {
        if (req is null || req.orderId <= 0)
        {
            return Json(new { success = false, message = "Dữ liệu không hợp lệ." });
        }

        if (!await CanAccessOrderAsync(req.orderId))
        {
            return Json(new { success = false, message = "Bạn không có quyền hủy đơn hàng này." });
        }

        var client = CreateOrderingClient();
        var response = await client.PostAsJsonAsync($"/api/orders/admin/{req.orderId}/status", new { newStatus = "Canceled" });

        if (!response.IsSuccessStatusCode)
        {
            return Json(new { success = false, message = await ReadApiErrorAsync(response, "Lỗi khi hủy đơn hàng.") });
        }

        return Json(new
        {
            success = true,
            message = "Đã hủy đơn hàng thành công",
            status = "Canceled",
            statusText = "Đã hủy",
            statusClass = "bg-danger"
        });
    }

    private async Task<ApiCallResult<PagedOrdersResponse>> GetOrdersFromApiAsync(
        int page,
        int pageSize,
        string? searchTerm,
        string? statusFilter,
        string? dateFilter)
    {
        var query = new List<string>
        {
            $"page={page}",
            $"pageSize={pageSize}"
        };

        if (!string.IsNullOrWhiteSpace(searchTerm))
        {
            query.Add($"searchTerm={Uri.EscapeDataString(searchTerm)}");
        }

        if (!string.IsNullOrWhiteSpace(statusFilter))
        {
            query.Add($"statusFilter={Uri.EscapeDataString(statusFilter)}");
        }

        if (!string.IsNullOrWhiteSpace(dateFilter))
        {
            query.Add($"dateFilter={Uri.EscapeDataString(dateFilter)}");
        }

        var client = CreateOrderingClient();
        var response = await client.GetAsync($"/api/orders/admin/paged?{string.Join("&", query)}");
        if (!response.IsSuccessStatusCode)
        {
            return new ApiCallResult<PagedOrdersResponse>(null, await ReadApiErrorAsync(response, "Lỗi khi tải danh sách đơn hàng."));
        }

        var payload = await response.Content.ReadFromJsonAsync<PagedOrdersResponse>(JsonOptions);
        if (payload is null)
        {
            return new ApiCallResult<PagedOrdersResponse>(null, "Không đọc được dữ liệu danh sách đơn hàng.");
        }

        if (!payload.success)
        {
            return new ApiCallResult<PagedOrdersResponse>(null, "Ordering API trả về danh sách đơn hàng không thành công.");
        }

        return new ApiCallResult<PagedOrdersResponse>(payload, null);
    }

    private async Task<ApiCallResult<HashSet<int>>> GetAllowedOrderIdsAsync()
    {
        var client = CreateOrderingClient();
        var response = await client.GetAsync("/api/orders/admin/order-ids");
        if (!response.IsSuccessStatusCode)
        {
            return new ApiCallResult<HashSet<int>>(null, await ReadApiErrorAsync(response, "Lỗi xác thực phạm vi dữ liệu seller."));
        }

        var payload = await response.Content.ReadFromJsonAsync<SellerOrderIdsResponse>(JsonOptions);
        if (payload?.success != true || payload.data is null)
        {
            return new ApiCallResult<HashSet<int>>(null, "Không đọc được phạm vi đơn hàng của seller.");
        }

        return new ApiCallResult<HashSet<int>>(payload.data.Where(id => id > 0).ToHashSet(), null);
    }

    private async Task<bool> CanAccessOrderAsync(int orderId)
    {
        if (orderId <= 0)
        {
            return false;
        }

        var allowedOrderIds = await GetAllowedOrderIdsAsync();
        return allowedOrderIds.Payload is not null && allowedOrderIds.Payload.Contains(orderId);
    }

    private HttpClient CreateOrderingClient()
    {
        var client = _httpClientFactory.CreateClient("Ordering");
        client.DefaultRequestHeaders.Remove("Authorization");
        client.DefaultRequestHeaders.Authorization = null;

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
            return $"{fallback} (HTTP {(int)response.StatusCode} {response.ReasonPhrase})";
        }

        try
        {
            using var doc = JsonDocument.Parse(body);
            var root = doc.RootElement;

            if (root.TryGetProperty("message", out var messageElement) && messageElement.ValueKind == JsonValueKind.String)
            {
                return messageElement.GetString() ?? fallback;
            }

            if (root.TryGetProperty("detail", out var detailElement) && detailElement.ValueKind == JsonValueKind.String)
            {
                return detailElement.GetString() ?? fallback;
            }

            if (root.TryGetProperty("title", out var titleElement) && titleElement.ValueKind == JsonValueKind.String)
            {
                return titleElement.GetString() ?? fallback;
            }
        }
        catch
        {
            // ignore parse failure
        }

        var compactBody = body.Trim();
        if (compactBody.Length > 220)
        {
            compactBody = compactBody[..220] + "...";
        }

        return $"{fallback} (HTTP {(int)response.StatusCode} {response.ReasonPhrase}) - {compactBody}";
    }

    private async Task<JsonResult> ToJsonResultAsync(HttpResponseMessage response, string fallbackError)
    {
        var body = await response.Content.ReadAsStringAsync();

        if (response.IsSuccessStatusCode)
        {
            if (string.IsNullOrWhiteSpace(body))
            {
                return Json(new { success = true });
            }

            try
            {
                using var document = JsonDocument.Parse(body);
                return Json(JsonElementToObject(document.RootElement));
            }
            catch
            {
                return Json(new { success = true });
            }
        }

        return Json(new { success = false, message = await ReadApiErrorAsync(response, fallbackError) });
    }

    private static object? JsonElementToObject(JsonElement element)
    {
        return element.ValueKind switch
        {
            JsonValueKind.Object => element.EnumerateObject().ToDictionary(prop => prop.Name, prop => JsonElementToObject(prop.Value)),
            JsonValueKind.Array => element.EnumerateArray().Select(JsonElementToObject).ToList(),
            JsonValueKind.String => element.GetString(),
            JsonValueKind.Number when element.TryGetInt64(out var l) => l,
            JsonValueKind.Number when element.TryGetDecimal(out var d) => d,
            JsonValueKind.True => true,
            JsonValueKind.False => false,
            JsonValueKind.Null => null,
            _ => element.GetRawText()
        };
    }

    private static List<Dictionary<string, object?>> NormalizeOrderRows(IEnumerable<Dictionary<string, object?>> rows)
    {
        var normalized = new List<Dictionary<string, object?>>();
        foreach (var row in rows)
        {
            var orderId = GetRowValue(row, "OrderID", "orderID", "orderId", "id");
            var orderCode = GetRowValue(row, "OrderCode", "orderCode");
            var status = GetRowValue(row, "Status", "status");

            var normalizedRow = new Dictionary<string, object?>
            {
                ["OrderID"] = orderId,
                ["OrderCode"] = orderCode ?? BuildOrderCodeFromId(orderId),
                ["CustomerName"] = GetRowValue(row, "CustomerName", "customerName", "buyerFullName"),
                ["OrderDate"] = GetRowValue(row, "OrderDate", "orderDate"),
                ["TotalAmount"] = GetRowValue(row, "TotalAmount", "totalAmount", "total"),
                ["Status"] = status,
                ["StatusBadgeClass"] = GetRowValue(row, "StatusBadgeClass", "statusBadgeClass"),
                ["StatusText"] = GetRowValue(row, "StatusText", "statusText") ?? status
            };

            normalized.Add(normalizedRow);
        }

        return normalized;
    }

    private static List<Dictionary<string, object?>> FilterRowsByAllowedOrderIds(
        List<Dictionary<string, object?>> rows,
        HashSet<int> allowedOrderIds)
    {
        return rows
            .Select(row => new
            {
                Row = row,
                OrderId = TryGetOrderIdFromRow(row)
            })
            .Where(entry => entry.OrderId.HasValue && allowedOrderIds.Contains(entry.OrderId.Value))
            .GroupBy(entry => entry.OrderId!.Value)
            .Select(group => SelectPreferredOrderRow(group.Select(entry => entry.Row)))
            .ToList();
    }

    private static Dictionary<string, object?> SelectPreferredOrderRow(IEnumerable<Dictionary<string, object?>> rows)
    {
        return rows
            .OrderByDescending(CalculateOrderRowScore)
            .ThenByDescending(CalculateOrderRowSignalLength)
            .First();
    }

    private static int CalculateOrderRowScore(IReadOnlyDictionary<string, object?> row)
    {
        var score = 0;

        score += HasMeaningfulValue(GetRowValue(row, "OrderCode", "orderCode")) ? 3 : 0;
        score += HasMeaningfulValue(GetRowValue(row, "CustomerName", "customerName", "buyerFullName")) ? 3 : 0;
        score += HasMeaningfulValue(GetRowValue(row, "OrderDate", "orderDate")) ? 2 : 0;
        score += HasMeaningfulValue(GetRowValue(row, "TotalAmount", "totalAmount", "total")) ? 2 : 0;
        score += HasMeaningfulValue(GetRowValue(row, "Status", "status")) ? 2 : 0;
        score += HasMeaningfulValue(GetRowValue(row, "StatusText", "statusText")) ? 1 : 0;
        score += HasMeaningfulValue(GetRowValue(row, "StatusBadgeClass", "statusBadgeClass")) ? 1 : 0;

        return score;
    }

    private static int CalculateOrderRowSignalLength(IReadOnlyDictionary<string, object?> row)
    {
        var values = new[]
        {
            GetRowValue(row, "OrderCode", "orderCode"),
            GetRowValue(row, "CustomerName", "customerName", "buyerFullName"),
            GetRowValue(row, "OrderDate", "orderDate"),
            GetRowValue(row, "TotalAmount", "totalAmount", "total"),
            GetRowValue(row, "Status", "status"),
            GetRowValue(row, "StatusText", "statusText"),
            GetRowValue(row, "StatusBadgeClass", "statusBadgeClass")
        };

        return values.Sum(value => value?.ToString()?.Length ?? 0);
    }

    private static int? TryGetOrderIdFromRow(IReadOnlyDictionary<string, object?> row)
    {
        var raw = GetRowValue(row, "OrderID", "orderID", "orderId", "id");
        if (raw is null)
        {
            return null;
        }

        try
        {
            return Convert.ToInt32(raw);
        }
        catch
        {
            return null;
        }
    }

    private static object? GetRowValue(IReadOnlyDictionary<string, object?> row, params string[] keys)
    {
        foreach (var key in keys)
        {
            if (TryGetRowValueIgnoreCase(row, key, out var value))
            {
                return NormalizeRowValue(value);
            }
        }

        return null;
    }

    private static bool TryGetRowValueIgnoreCase(IReadOnlyDictionary<string, object?> row, string key, out object? value)
    {
        if (row.TryGetValue(key, out value))
        {
            return true;
        }

        foreach (var kvp in row)
        {
            if (string.Equals(kvp.Key, key, StringComparison.OrdinalIgnoreCase))
            {
                value = kvp.Value;
                return true;
            }
        }

        value = null;
        return false;
    }

    private static object? NormalizeRowValue(object? value)
    {
        if (value is JsonElement element)
        {
            return JsonElementToObject(element);
        }

        return value;
    }

    private static bool HasMeaningfulValue(object? value)
    {
        return value switch
        {
            null => false,
            string text => !string.IsNullOrWhiteSpace(text),
            JsonElement element => element.ValueKind != JsonValueKind.Null && element.ValueKind != JsonValueKind.Undefined,
            _ => true
        };
    }

    private static string? BuildOrderCodeFromId(object? orderId)
    {
        try
        {
            var id = Convert.ToInt32(orderId);
            return id > 0 ? $"#{id:D6}" : null;
        }
        catch
        {
            return null;
        }
    }

    private sealed class PagedOrdersResponse
    {
        public bool success { get; set; }

        public List<Dictionary<string, object?>> data
        {
            get;
            set;
        } = new();

        public int page
        {
            get;
            set;
        }

        public int pageSize
        {
            get;
            set;
        }

        public int total
        {
            get;
            set;
        }
    }

    private sealed record ApiCallResult<T>(T? Payload, string? ErrorMessage) where T : class;

    private sealed class SellerOrderIdsResponse
    {
        public bool success { get; set; }

        public List<int>? data { get; set; }
    }

    private sealed class AdminOrderDetailDto
    {
        public bool success { get; set; }

        public int orderId { get; set; }

        public string? orderCode { get; set; }

        public int userId { get; set; }

        public string? customerName { get; set; }

        public string? customerEmail { get; set; }

        public string? buyerFullName { get; set; }

        public string? buyerPhone { get; set; }

        public string? buyerEmail { get; set; }

        public string? phone { get; set; }

        public string? address { get; set; }

        public DateTime orderDate { get; set; }

        public string? status { get; set; }

        public string? statusCode { get; set; }

        public string? paymentMethod { get; set; }

        public string? paymentStatus { get; set; }

        public string? bankName { get; set; }

        public string? transactionCode { get; set; }

        public decimal subtotal { get; set; }

        public decimal shippingFee { get; set; }

        public decimal total { get; set; }

        public string? orderNote { get; set; }

        public List<AdminOrderItemDto>? items { get; set; }
    }

    private sealed class AdminOrderItemDto
    {
        public int productId { get; set; }

        public string? productName { get; set; }

        public int quantity { get; set; }

        public decimal unitPrice { get; set; }

        public string? unitSymbol { get; set; }

        public decimal totalPrice { get; set; }
    }

    private sealed class AdminStatisticsResponse
    {
        public bool success { get; set; }

        public AdminStatisticsData? data { get; set; }
    }

    private sealed class AdminStatisticsData
    {
        public int totalOrders { get; set; }

        public int pendingOrders { get; set; }

        public int processingOrders { get; set; }

        public int shippedOrders { get; set; }

        public int deliveredOrders { get; set; }

        public int canceledOrders { get; set; }

        public decimal totalRevenue { get; set; }

        public decimal todayRevenue { get; set; }

        public decimal monthRevenue { get; set; }

        public int todayOrders { get; set; }

        public int thisWeekOrders { get; set; }

        public int thisMonthOrders { get; set; }

        public decimal averageOrderValue { get; set; }

        public decimal deliveryRate { get; set; }

        public decimal cancelRate { get; set; }
    }
}
