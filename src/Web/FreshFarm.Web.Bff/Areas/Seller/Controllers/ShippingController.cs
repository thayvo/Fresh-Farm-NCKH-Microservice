using FreshFarm.Web.Bff.Areas.Seller.Infrastructure;
using FreshFarm.Web.Bff.Areas.Seller.Models;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Rendering;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;

namespace FreshFarm.Web.Bff.Areas.Seller.Controllers;

[Authorize(Roles = "Seller")]
[ResponseCache(Duration = 0, Location = ResponseCacheLocation.None, NoStore = true)]
[Area("Seller")]
public class ShippingController : LegacySellerControllerBase
{
    private const string AccessTokenSessionKey = "ACCESS_TOKEN";

    private static readonly string[] PaidStatuses = { "Đã thanh toán", "Da thanh toan", "Hoàn tất", "Hoan tat" };

    private readonly IHttpClientFactory _httpClientFactory;

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true
    };

    public ShippingController(IHttpClientFactory httpClientFactory)
    {
        _httpClientFactory = httpClientFactory;
    }

    public async Task<IActionResult> ManageShipping(
        string status = "",
        int? staffId = null,
        string q = "",
        string sort = "date_desc",
        int page = 1,
        int pageSize = 10,
        string deliveredState = "")
    {
        ViewBag.CurrentPage = page;
        ViewBag.TotalPages = 1;
        ViewBag.TotalItems = 0;
        ViewBag.PageSize = pageSize;
        ViewBag.CurrentStatus = status;
        ViewBag.CurrentStaffId = staffId;
        ViewBag.CurrentQuery = q;
        ViewBag.CurrentSort = sort;
        ViewBag.DeliveredState = deliveredState;
        ViewBag.PaidStatuses = PaidStatuses;
        ViewBag.DeliveryStaffs = new SelectList(Enumerable.Empty<SelectListItem>(), "Value", "Text");
        ViewBag.Orders = new SelectList(Enumerable.Empty<SelectListItem>(), "Value", "Text");
        ViewBag.Provinces = new SelectList(Enumerable.Empty<SelectListItem>(), "Value", "Text");

        try
        {
            var client = CreateAuthorizedClient("Ordering");

            var query = new List<string>
            {
                $"status={Uri.EscapeDataString(status ?? string.Empty)}",
                $"q={Uri.EscapeDataString(q ?? string.Empty)}",
                $"sort={Uri.EscapeDataString(sort ?? string.Empty)}",
                $"page={page}",
                $"pageSize={pageSize}",
                $"deliveredState={Uri.EscapeDataString(deliveredState ?? string.Empty)}"
            };

            if (staffId.HasValue)
            {
                query.Add($"staffId={staffId.Value}");
            }

            var response = await client.GetAsync($"/api/orders/admin/shippings?{string.Join("&", query)}");
            if (!response.IsSuccessStatusCode)
            {
                TempData["Error"] = "Co loi khi tai danh sach van chuyen: " + await ReadApiErrorAsync(response, "Unknown error");
                return View(new List<Shipping>());
            }

            var payload = await response.Content.ReadFromJsonAsync<ShippingListApiResponse>(JsonOptions);
            var data = payload?.data;

            var items = data?.items?.Select(MapShipping).ToList() ?? new List<Shipping>();

            ViewBag.CurrentPage = data?.currentPage ?? page;
            ViewBag.TotalPages = data?.totalPages ?? 1;
            ViewBag.TotalItems = data?.totalItems ?? items.Count;
            ViewBag.PageSize = data?.pageSize ?? pageSize;
            ViewBag.CurrentStatus = data?.currentStatus ?? status;
            ViewBag.CurrentStaffId = data?.currentStaffId;
            ViewBag.CurrentQuery = data?.currentQuery ?? q;
            ViewBag.CurrentSort = data?.currentSort ?? sort;
            ViewBag.DeliveredState = data?.deliveredState ?? deliveredState;
            ViewBag.PaidStatuses = PaidStatuses;

            var staffs = (data?.deliveryStaffs ?? new List<DeliveryStaffDto>())
                .Select(s => new SelectListItem
                {
                    Value = s.adminID.ToString(),
                    Text = string.IsNullOrWhiteSpace(s.name) ? $"NV #{s.adminID:D4}" : s.name
                })
                .ToList();
            ViewBag.DeliveryStaffs = new SelectList(staffs, "Value", "Text", (data?.currentStaffId)?.ToString());

            var orders = (data?.orders ?? new List<OrderOptionDto>())
                .Select(o => new
                {
                    o.orderID,
                    displayText = string.IsNullOrWhiteSpace(o.displayText)
                        ? "Don hang #DH" + o.orderID.ToString().PadLeft(5, '0')
                        : o.displayText
                })
                .ToList();
            ViewBag.Orders = new SelectList(orders, "orderID", "displayText");

            var provinces = (data?.provinces ?? new List<ProvinceDto>())
                .Select(p => new SelectListItem
                {
                    Value = p.provinceId.ToString(),
                    Text = p.provinceName ?? string.Empty
                })
                .ToList();
            ViewBag.Provinces = new SelectList(provinces, "Value", "Text");

            return View(items);
        }
        catch (Exception ex)
        {
            TempData["Error"] = "Co loi khi tai danh sach van chuyen: " + ex.Message;
            return View(new List<Shipping>());
        }
    }

    [HttpGet]
    public async Task<JsonResult> GetCommunes(int provinceId)
    {
        try
        {
            var client = CreateAuthorizedClient("Ordering");
            var response = await client.GetAsync($"/api/orders/admin/shippings/communes?provinceId={provinceId}");

            if (!response.IsSuccessStatusCode)
            {
                return Json(new { error = await ReadApiErrorAsync(response, "Khong the tai danh sach xa/phuong") });
            }

            var communes = await response.Content.ReadFromJsonAsync<List<CommuneOptionDto>>(JsonOptions)
                ?? new List<CommuneOptionDto>();

            return Json(communes.Select(c => new { value = c.value, text = c.text }));
        }
        catch (Exception ex)
        {
            return Json(new { error = ex.Message });
        }
    }

    [HttpGet]
    public async Task<JsonResult> GetOrderInfo(int orderId)
    {
        try
        {
            var client = CreateAuthorizedClient("Ordering");
            var response = await client.GetAsync($"/api/orders/admin/shippings/order-info/{orderId}");
            if (!response.IsSuccessStatusCode)
            {
                return Json(new { success = false, message = await ReadApiErrorAsync(response, "Khong tim thay don hang") });
            }

            var payload = await response.Content.ReadFromJsonAsync<OrderInfoApiResponse>(JsonOptions);
            if (payload is null)
            {
                return Json(new { success = false, message = "Khong doc duoc thong tin don hang" });
            }

            return Json(payload);
        }
        catch (Exception ex)
        {
            return Json(new { success = false, message = ex.Message });
        }
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<JsonResult> Create(Shipping shipping)
    {
        try
        {
            var client = CreateAuthorizedClient("Ordering");

            var response = await client.PostAsJsonAsync("/api/orders/admin/shippings", new
            {
                orderID = shipping.OrderID,
                shippingType = shipping.ShippingType,
                fullName = shipping.FullName,
                phone = shipping.Phone,
                email = shipping.Email,
                addressDetail = shipping.AddressDetail,
                provinceId = shipping.ProvinceId,
                communeId = shipping.CommuneId,
                isStorePickup = shipping.IsStorePickup,
                storeAddress = shipping.StoreAddress,
                deliveryStaffId = ParseNullableInt(Request.Form["DeliveryStaffId"].ToString())
            });

            if (!response.IsSuccessStatusCode)
            {
                return Json(new { success = false, message = await ReadApiErrorAsync(response, "Khong the them van chuyen") });
            }

            using var doc = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
            var root = doc.RootElement;

            return Json(new
            {
                success = root.TryGetProperty("success", out var success) && success.GetBoolean(),
                message = root.TryGetProperty("message", out var message) ? message.GetString() : "Them van chuyen thanh cong"
            });
        }
        catch (Exception ex)
        {
            return Json(new { success = false, message = "Loi: " + ex.Message });
        }
    }

    [HttpGet]
    public async Task<JsonResult> Edit(int id)
    {
        try
        {
            var client = CreateAuthorizedClient("Ordering");
            var response = await client.GetAsync($"/api/orders/admin/shippings/{id}");

            if (!response.IsSuccessStatusCode)
            {
                return Json(new { success = false, message = await ReadApiErrorAsync(response, "Khong tim thay thong tin van chuyen") });
            }

            var payload = await response.Content.ReadFromJsonAsync<ShippingEditApiResponse>(JsonOptions);
            if (payload is null)
            {
                return Json(new { success = false, message = "Khong doc duoc du lieu" });
            }

            return Json(payload);
        }
        catch (Exception ex)
        {
            return Json(new { success = false, message = ex.Message });
        }
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<JsonResult> Edit(Shipping shipping)
    {
        try
        {
            var client = CreateAuthorizedClient("Ordering");
            var response = await client.PutAsJsonAsync($"/api/orders/admin/shippings/{shipping.ShippingID}", new
            {
                orderID = shipping.OrderID,
                shippingType = shipping.ShippingType,
                fullName = shipping.FullName,
                phone = shipping.Phone,
                email = shipping.Email,
                addressDetail = shipping.AddressDetail,
                provinceId = shipping.ProvinceId,
                communeId = shipping.CommuneId,
                isStorePickup = shipping.IsStorePickup,
                storeAddress = shipping.StoreAddress,
                deliveryStaffId = ParseNullableInt(Request.Form["DeliveryStaffId"].ToString())
            });

            if (!response.IsSuccessStatusCode)
            {
                return Json(new { success = false, message = await ReadApiErrorAsync(response, "Khong the cap nhat van chuyen") });
            }

            using var doc = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
            var root = doc.RootElement;

            return Json(new
            {
                success = root.TryGetProperty("success", out var success) && success.GetBoolean(),
                message = root.TryGetProperty("message", out var message) ? message.GetString() : "Cap nhat van chuyen thanh cong"
            });
        }
        catch (Exception ex)
        {
            return Json(new { success = false, message = "Loi: " + ex.Message });
        }
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<JsonResult> Delete(int id)
    {
        try
        {
            var client = CreateAuthorizedClient("Ordering");
            var response = await client.DeleteAsync($"/api/orders/admin/shippings/{id}");

            if (!response.IsSuccessStatusCode)
            {
                return Json(new { success = false, message = await ReadApiErrorAsync(response, "Khong the xoa thong tin van chuyen") });
            }

            using var doc = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
            var root = doc.RootElement;

            return Json(new
            {
                success = root.TryGetProperty("success", out var success) && success.GetBoolean(),
                message = root.TryGetProperty("message", out var message) ? message.GetString() : "Xoa thong tin van chuyen thanh cong"
            });
        }
        catch (Exception ex)
        {
            return Json(new { success = false, message = "Loi: " + ex.Message });
        }
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<JsonResult> ReconcileCod(int orderId)
    {
        try
        {
            var client = CreateAuthorizedClient("Ordering");
            var response = await client.PostAsJsonAsync("/api/orders/admin/shippings/reconcile-cod", new
            {
                orderId
            });

            if (!response.IsSuccessStatusCode)
            {
                return Json(new { success = false, message = await ReadApiErrorAsync(response, "Khong the doi soat COD") });
            }

            using var doc = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
            var root = doc.RootElement;

            return Json(new
            {
                success = root.TryGetProperty("success", out var success) && success.GetBoolean(),
                message = root.TryGetProperty("message", out var message) ? message.GetString() : "Da doi soat COD"
            });
        }
        catch (Exception ex)
        {
            return Json(new { success = false, message = "Loi: " + ex.Message });
        }
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<JsonResult> UnreconcileCod(int orderId)
    {
        try
        {
            var client = CreateAuthorizedClient("Ordering");
            var response = await client.PostAsJsonAsync("/api/orders/admin/shippings/unreconcile-cod", new
            {
                orderId
            });

            if (!response.IsSuccessStatusCode)
            {
                return Json(new { success = false, message = await ReadApiErrorAsync(response, "Khong the bo doi soat COD") });
            }

            using var doc = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
            var root = doc.RootElement;

            return Json(new
            {
                success = root.TryGetProperty("success", out var success) && success.GetBoolean(),
                message = root.TryGetProperty("message", out var message) ? message.GetString() : "Da bo doi soat COD"
            });
        }
        catch (Exception ex)
        {
            return Json(new { success = false, message = "Loi: " + ex.Message });
        }
    }

    private static int? ParseNullableInt(string? value)
    {
        return int.TryParse(value, out var result) && result > 0 ? result : null;
    }

    private HttpClient CreateAuthorizedClient(string clientName)
    {
        var client = _httpClientFactory.CreateClient(clientName);

        client.DefaultRequestHeaders.Remove("Authorization");
        client.DefaultRequestHeaders.Authorization = null;

        var authHeader = Request.Headers.Authorization.ToString();
        if (!string.IsNullOrWhiteSpace(authHeader))
        {
            if (AuthenticationHeaderValue.TryParse(authHeader, out var parsed))
            {
                client.DefaultRequestHeaders.Authorization = parsed;
            }
            else
            {
                client.DefaultRequestHeaders.TryAddWithoutValidation("Authorization", authHeader);
            }

            return client;
        }

        var token = HttpContext.Session.GetString(AccessTokenSessionKey);
        if (!string.IsNullOrWhiteSpace(token))
        {
            client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);
        }

        return client;
    }

    private static Shipping MapShipping(ShippingItemDto item)
    {
        return new Shipping
        {
            ShippingID = item.shippingID,
            OrderID = item.orderID,
            ShippingType = item.shippingType ?? "Giao noi bo",
            FullName = item.fullName ?? string.Empty,
            Phone = item.phone ?? string.Empty,
            Email = item.email,
            AddressDetail = item.addressDetail,
            ProvinceId = item.provinceId,
            CommuneId = item.communeId,
            IsStorePickup = item.isStorePickup,
            StoreAddress = item.storeAddress,
            Province = item.province is null
                ? null
                : new Province
                {
                    ProvinceId = item.province.provinceId,
                    ProvinceName = item.province.provinceName ?? string.Empty
                },
            Commune = item.commune is null
                ? null
                : new Commune
                {
                    CommuneId = item.commune.communeId,
                    CommuneName = item.commune.communeName ?? string.Empty
                },
            Order = item.order is null
                ? null
                : new Order
                {
                    OrderID = item.order.orderID,
                    Status = item.order.status ?? string.Empty,
                    Payments = item.order.payments?.Select(p => new Payment
                    {
                        PaymentID = p.paymentID,
                        PaymentMethod = p.paymentMethod ?? string.Empty,
                        PaymentStatus = p.paymentStatus,
                        PaymentDate = p.paymentDate
                    }).ToList() ?? new List<Payment>(),
                    DeliveryAssignments = new List<DeliveryAssignment>()
                }
        };
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

            if (root.TryGetProperty("message", out var message) && message.ValueKind == JsonValueKind.String)
            {
                return message.GetString() ?? fallback;
            }

            if (root.TryGetProperty("detail", out var detail) && detail.ValueKind == JsonValueKind.String)
            {
                return detail.GetString() ?? fallback;
            }

            if (root.TryGetProperty("title", out var title) && title.ValueKind == JsonValueKind.String)
            {
                return title.GetString() ?? fallback;
            }
        }
        catch
        {
            // ignore parse failure
        }

        return fallback;
    }

    private sealed class ShippingListApiResponse
    {
        public bool success { get; set; }

        public ShippingListDataDto? data { get; set; }
    }

    private sealed class ShippingListDataDto
    {
        public List<ShippingItemDto>? items { get; set; }

        public int totalItems { get; set; }

        public int totalPages { get; set; }

        public int currentPage { get; set; }

        public int pageSize { get; set; }

        public string? currentStatus { get; set; }

        public int? currentStaffId { get; set; }

        public string? currentQuery { get; set; }

        public string? currentSort { get; set; }

        public string? deliveredState { get; set; }

        public List<ProvinceDto>? provinces { get; set; }

        public List<DeliveryStaffDto>? deliveryStaffs { get; set; }

        public List<OrderOptionDto>? orders { get; set; }
    }

    private sealed class ShippingItemDto
    {
        public int shippingID { get; set; }

        public int orderID { get; set; }

        public string? shippingType { get; set; }

        public string? fullName { get; set; }

        public string? phone { get; set; }

        public string? email { get; set; }

        public string? addressDetail { get; set; }

        public int? provinceId { get; set; }

        public int? communeId { get; set; }

        public bool isStorePickup { get; set; }

        public string? storeAddress { get; set; }

        public ProvinceDto? province { get; set; }

        public CommuneDto? commune { get; set; }

        public ShippingOrderDto? order { get; set; }
    }

    private sealed class ShippingOrderDto
    {
        public int orderID { get; set; }

        public string? status { get; set; }

        public List<ShippingPaymentDto>? payments { get; set; }
    }

    private sealed class ShippingPaymentDto
    {
        public int paymentID { get; set; }

        public string? paymentMethod { get; set; }

        public string? paymentStatus { get; set; }

        public DateTime? paymentDate { get; set; }
    }

    private sealed class ProvinceDto
    {
        public int provinceId { get; set; }

        public string? provinceName { get; set; }
    }

    private sealed class CommuneDto
    {
        public int communeId { get; set; }

        public string? communeName { get; set; }
    }

    private sealed class DeliveryStaffDto
    {
        public int adminID { get; set; }

        public string? name { get; set; }
    }

    private sealed class OrderOptionDto
    {
        public int orderID { get; set; }

        public string? displayText { get; set; }
    }

    private sealed class CommuneOptionDto
    {
        public int value { get; set; }

        public string? text { get; set; }
    }

    private sealed class OrderInfoApiResponse
    {
        public bool success { get; set; }

        public string? fullName { get; set; }

        public string? phone { get; set; }

        public string? email { get; set; }

        public string? address { get; set; }

        public int? provinceId { get; set; }

        public int? communeId { get; set; }
    }

    private sealed class ShippingEditApiResponse
    {
        public bool success { get; set; }

        public int shippingID { get; set; }

        public int orderID { get; set; }

        public string? shippingType { get; set; }

        public string? fullName { get; set; }

        public string? phone { get; set; }

        public string? email { get; set; }

        public string? addressDetail { get; set; }

        public int? provinceId { get; set; }

        public int? communeId { get; set; }

        public bool isStorePickup { get; set; }

        public string? storeAddress { get; set; }

        public int? deliveryStaffId { get; set; }
    }
}
