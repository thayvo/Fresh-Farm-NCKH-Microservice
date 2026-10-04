using FreshFarm.Web.Bff.Areas.Seller.Infrastructure;
using FreshFarm.Web.Bff.Areas.Seller.Models;
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
public class CouponController : LegacySellerControllerBase
{
    private const string AccessTokenSessionKey = "ACCESS_TOKEN";
    private const string SellerCouponPolicyMessage = "Nguoi ban khong the tu tao, cap nhat, xoa hoac phan phoi ma giam gia. Vui long lien he admin va neu ro ly do de duoc cap ma.";
    private readonly IHttpClientFactory _httpClientFactory;

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true
    };

    public CouponController(IHttpClientFactory httpClientFactory)
    {
        _httpClientFactory = httpClientFactory;
    }

    public IActionResult ManageCoupons()
    {
        ViewData["CouponTitle"] = "Mã giảm giá";
        ViewData["CouponSubtitle"] = "Mã giảm giá do admin tạo và cấp theo chính sách của sàn.";
        ViewData["CouponCanManage"] = false;
        ViewData["CouponCanSend"] = false;
        ViewData["CouponPolicyNotice"] = "Người bán không thể tự tạo mã giảm giá. Nếu cần chạy khuyến mãi, hãy liên hệ admin và nêu rõ lý do, thời gian áp dụng, mức giảm mong muốn và phạm vi sản phẩm.";
        ViewData["CouponEmptyTitle"] = "Seller không tự quản lý mã giảm giá";
        ViewData["CouponEmptySubtitle"] = "Chỉ admin có quyền tạo, chỉnh sửa, phân phối và thu hồi mã giảm giá.";
        return View(new List<Coupon>());
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public JsonResult Create(Coupon coupon)
    {
        Response.StatusCode = StatusCodes.Status403Forbidden;
        return Json(new { success = false, message = SellerCouponPolicyMessage });
    }

    public JsonResult Edit(int? id)
    {
        Response.StatusCode = StatusCodes.Status403Forbidden;
        return Json(new { error = SellerCouponPolicyMessage });
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public JsonResult Edit(Coupon coupon)
    {
        Response.StatusCode = StatusCodes.Status403Forbidden;
        return Json(new { success = false, message = SellerCouponPolicyMessage });
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public JsonResult Delete(int id)
    {
        Response.StatusCode = StatusCodes.Status403Forbidden;
        return Json(new { success = false, message = SellerCouponPolicyMessage });
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public JsonResult ToggleActive(int id)
    {
        Response.StatusCode = StatusCodes.Status403Forbidden;
        return Json(new { success = false, message = SellerCouponPolicyMessage });
    }

    public async Task<JsonResult> GetCustomers(string search = "")
    {
        try
        {
            var sellerCustomerIds = await GetSellerCustomerIdsAsync();
            if (sellerCustomerIds.Count == 0)
            {
                return Json(new List<object>());
            }

            var client = CreateAuthorizedClient("Identity");
            var query = "take=50";
            if (!string.IsNullOrWhiteSpace(search))
            {
                query += "&keyword=" + Uri.EscapeDataString(search.Trim());
            }

            var response = await client.GetAsync($"/auth/admin/customers?{query}");
            if (!response.IsSuccessStatusCode)
            {
                return Json(new { error = await ReadApiErrorAsync(response, "Khong the tai danh sach khach hang") });
            }

            var customers = DeduplicateCustomers(
                await response.Content.ReadFromJsonAsync<List<IdentityCustomerDto>>(JsonOptions)
                ?? new List<IdentityCustomerDto>());

            var result = customers.Select(c => new
            {
                userID = c.userId,
                fullName = c.fullName,
                email = c.email,
                phone = c.phone
            })
            .Where(x => sellerCustomerIds.Contains(x.userID))
            .ToList();

            return Json(result);
        }
        catch (Exception ex)
        {
            return Json(new { error = ex.Message });
        }
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public JsonResult SendToCustomers(string couponCode, List<int>? customerIds, bool sendToAll = false)
    {
        Response.StatusCode = StatusCodes.Status403Forbidden;
        return Json(new { success = false, message = SellerCouponPolicyMessage });
    }

    private async Task<HashSet<int>> GetSellerCustomerIdsAsync()
    {
        var client = CreateAuthorizedClient("Ordering");
        var response = await client.GetAsync("/api/orders/admin/customers/ids");
        if (!response.IsSuccessStatusCode)
        {
            return new HashSet<int>();
        }

        var payload = await response.Content.ReadFromJsonAsync<SellerCustomerIdsResponse>(JsonOptions);
        if (payload?.success != true || payload.data is null)
        {
            return new HashSet<int>();
        }

        return payload.data.Where(id => id > 0).ToHashSet();
    }

    private static List<IdentityCustomerDto> DeduplicateCustomers(IEnumerable<IdentityCustomerDto> customers)
    {
        return customers
            .Where(customer => customer.userId > 0)
            .GroupBy(customer => customer.userId)
            .Select(group => SelectPreferredCustomer(group))
            .ToList();
    }

    private static IdentityCustomerDto SelectPreferredCustomer(IEnumerable<IdentityCustomerDto> customers)
    {
        return customers
            .OrderByDescending(CalculateCustomerScore)
            .ThenByDescending(CalculateCustomerSignalLength)
            .First();
    }

    private static int CalculateCustomerScore(IdentityCustomerDto customer)
    {
        var score = 0;

        score += HasMeaningfulValue(customer.fullName) ? 3 : 0;
        score += HasMeaningfulValue(customer.email) ? 3 : 0;
        score += HasMeaningfulValue(customer.phone) ? 2 : 0;

        return score;
    }

    private static int CalculateCustomerSignalLength(IdentityCustomerDto customer)
    {
        var values = new[]
        {
            customer.fullName,
            customer.email,
            customer.phone
        };

        return values.Sum(value => value?.Length ?? 0);
    }

    private static bool HasMeaningfulValue(string? value) => !string.IsNullOrWhiteSpace(value);

    public JsonResult GenerateCode()
    {
        Response.StatusCode = StatusCodes.Status403Forbidden;
        return Json(new { code = "FORBIDDEN", message = SellerCouponPolicyMessage });
    }

    public async Task<IActionResult> ValidateCoupon(string code, decimal orderAmount)
    {
        try
        {
            var client = CreateAuthorizedClient("Ordering");
            var response = await client.GetAsync($"/api/orders/admin/coupons/validate?code={Uri.EscapeDataString(code ?? string.Empty)}&orderAmount={orderAmount}");

            if (!response.IsSuccessStatusCode)
            {
                return Json(new { valid = false, message = await ReadApiErrorAsync(response, "Khong the kiem tra ma giam gia") });
            }

            var json = await response.Content.ReadAsStringAsync();
            return Content(json, "application/json");
        }
        catch (Exception ex)
        {
            return Json(new { valid = false, message = "Loi: " + ex.Message });
        }
    }

    public async Task<IActionResult> GetStatistics()
    {
        try
        {
            var client = CreateAuthorizedClient("Ordering");
            var response = await client.GetAsync("/api/orders/admin/coupons/statistics");

            if (!response.IsSuccessStatusCode)
            {
                return Json(new { error = await ReadApiErrorAsync(response, "Khong the tai thong ke") });
            }

            var json = await response.Content.ReadAsStringAsync();
            return Content(json, "application/json");
        }
        catch (Exception ex)
        {
            return Json(new { error = ex.Message });
        }
    }

    public async Task<IActionResult> GetUsageHistory(int id)
    {
        try
        {
            var client = CreateAuthorizedClient("Ordering");
            var response = await client.GetAsync($"/api/orders/admin/coupons/{id}/usage-history");
            if (!response.IsSuccessStatusCode)
            {
                return Json(new { error = await ReadApiErrorAsync(response, "Khong the tai lich su su dung") });
            }

            var json = await response.Content.ReadAsStringAsync();
            return Content(json, "application/json");
        }
        catch (Exception ex)
        {
            return Json(new { error = ex.Message });
        }
    }

    public async Task<IActionResult> GetDistributionList(int id)
    {
        try
        {
            var client = CreateAuthorizedClient("Ordering");
            var response = await client.GetAsync($"/api/orders/admin/coupons/{id}/distribution-list");
            if (!response.IsSuccessStatusCode)
            {
                return Json(new { error = await ReadApiErrorAsync(response, "Khong the tai danh sach phan phoi") });
            }

            var json = await response.Content.ReadAsStringAsync();
            return Content(json, "application/json");
        }
        catch (Exception ex)
        {
            return Json(new { error = ex.Message });
        }
    }

    private static string? ValidateCouponInput(Coupon coupon)
    {
        if (coupon is null)
        {
            return "Du lieu khong hop le";
        }

        if (string.IsNullOrWhiteSpace(coupon.Code))
        {
            return "Ma giam gia khong duoc de trong!";
        }

        var type = (coupon.DiscountType ?? string.Empty).Trim().ToLowerInvariant();
        if (type != "fixed" && type != "percent")
        {
            return "Loai giam gia khong hop le!";
        }

        if (type == "percent")
        {
            if (coupon.DiscountValue <= 0 || coupon.DiscountValue > 100)
            {
                return "Gia tri phan tram khong hop le!";
            }

            if (coupon.MaxDiscountAmount.HasValue && coupon.MaxDiscountAmount.Value <= 0)
            {
                return "Giam toi da phai lon hon 0!";
            }
        }
        else
        {
            if (coupon.DiscountValue < 1000)
            {
                return "Gia tri giam toi thieu la 1,000₫!";
            }
        }

        if (coupon.ExpiryDate.Date < DateTime.Today)
        {
            return "Ngay het han phai tu hom nay tro di!";
        }

        if (coupon.UsageLimit.HasValue && coupon.UsageLimit.Value <= 0)
        {
            return "Gioi han so lan su dung phai lon hon 0!";
        }

        if (coupon.MinOrderValue.HasValue && coupon.MinOrderValue.Value < 0)
        {
            return "Gia tri don hang toi thieu khong hop le!";
        }

        return null;
    }

    private static Coupon MapCoupon(CouponApiDto dto)
    {
        return new Coupon
        {
            CouponID = dto.couponId,
            Code = dto.code ?? string.Empty,
            DiscountValue = dto.discountValue,
            DiscountType = dto.discountType ?? "fixed",
            ExpiryDate = dto.expiryDate.ToDateTime(TimeOnly.MinValue),
            MinOrderValue = dto.minOrderValue,
            UsageLimit = dto.usageLimit,
            UsedCount = dto.usedCount,
            Description = dto.description,
            MaxDiscountAmount = dto.maxDiscountAmount,
            IsActive = dto.isActive,
            CreatedBy = dto.createdBy,
            CreatedDate = dto.createdDate,
            UpdatedDate = dto.updatedDate
        };
    }

    private HttpClient CreateAuthorizedClient(string clientName)
    {
        var client = _httpClientFactory.CreateClient(clientName);

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

        return fallback;
    }

    private sealed class CouponApiDto
    {
        public int couponId { get; set; }

        public string? code { get; set; }

        public decimal discountValue { get; set; }

        public DateOnly expiryDate { get; set; }

        public decimal? minOrderValue { get; set; }

        public string? discountType { get; set; }

        public bool isActive { get; set; }

        public int? usageLimit { get; set; }

        public int usedCount { get; set; }

        public string? description { get; set; }

        public int? createdBy { get; set; }

        public DateTime createdDate { get; set; }

        public DateTime? updatedDate { get; set; }

        public decimal? maxDiscountAmount { get; set; }
    }

    private sealed class IdentityCustomerDto
    {
        public int userId { get; set; }

        public string? fullName { get; set; }

        public string? email { get; set; }

        public string? phone { get; set; }
    }

    private sealed class SellerCustomerIdsResponse
    {
        public bool success { get; set; }

        public List<int>? data { get; set; }
    }
}
