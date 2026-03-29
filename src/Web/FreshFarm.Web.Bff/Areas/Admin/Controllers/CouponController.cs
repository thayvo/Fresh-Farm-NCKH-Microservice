using FreshFarm.Web.Bff.Areas.Seller.Infrastructure;
using FreshFarm.Web.Bff.Areas.Seller.Models;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;

namespace FreshFarm.Web.Bff.Areas.Admin.Controllers;

[Authorize(Policy = "AdminOnly")]
[ResponseCache(Duration = 0, Location = ResponseCacheLocation.None, NoStore = true)]
[Area("Admin")]
public sealed class CouponController : LegacySellerControllerBase
{
    private const string AccessTokenSessionKey = "ACCESS_TOKEN";
    private const string PlatformCouponScope = "platform";
    private readonly IHttpClientFactory _httpClientFactory;

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true
    };

    public CouponController(IHttpClientFactory httpClientFactory)
    {
        _httpClientFactory = httpClientFactory;
    }

    [HttpGet]
    public async Task<IActionResult> ManageCoupons()
    {
        try
        {
            var client = CreateAuthorizedClient("Ordering");
            var response = await client.GetAsync($"/api/orders/admin/coupons?scope={PlatformCouponScope}");

            if (!response.IsSuccessStatusCode)
            {
                TempData["Error"] = "Co loi khi tai danh sach ma giam gia: " + await ReadApiErrorAsync(response, "Unknown error");
                return RenderCouponView(new List<Coupon>());
            }

            var payload = await response.Content.ReadFromJsonAsync<List<CouponApiDto>>(JsonOptions) ?? new List<CouponApiDto>();
            var coupons = payload.Select(MapCoupon).ToList();

            return RenderCouponView(coupons);
        }
        catch (Exception ex)
        {
            TempData["Error"] = "Co loi khi tai danh sach ma giam gia: " + ex.Message;
            return RenderCouponView(new List<Coupon>());
        }
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<JsonResult> Create(Coupon coupon)
    {
        try
        {
            var validationError = ValidateCouponInput(coupon);
            if (!string.IsNullOrWhiteSpace(validationError))
            {
                return Json(new { success = false, message = validationError });
            }

            var client = CreateAuthorizedClient("Ordering");
            var response = await client.PostAsJsonAsync("/api/orders/admin/coupons", new
            {
                code = coupon.Code?.Trim().ToUpperInvariant(),
                discountValue = coupon.DiscountValue,
                discountType = coupon.DiscountType,
                expiryDate = coupon.ExpiryDate,
                minOrderValue = coupon.MinOrderValue,
                usageLimit = coupon.UsageLimit,
                description = coupon.Description,
                maxDiscountAmount = coupon.MaxDiscountAmount,
                isActive = coupon.IsActive
            });

            if (!response.IsSuccessStatusCode)
            {
                return Json(new { success = false, message = await ReadApiErrorAsync(response, "Khong the tao ma giam gia") });
            }

            return Json(new { success = true, message = "Tao ma giam gia thanh cong!" });
        }
        catch (Exception ex)
        {
            return Json(new { success = false, message = "Loi: " + ex.Message });
        }
    }

    [HttpGet]
    public async Task<JsonResult> Edit(int? id)
    {
        try
        {
            if (!id.HasValue)
            {
                return Json(new { error = "ID khong hop le" });
            }

            var client = CreateAuthorizedClient("Ordering");
            var response = await client.GetAsync($"/api/orders/admin/coupons/{id.Value}?scope={PlatformCouponScope}");
            if (!response.IsSuccessStatusCode)
            {
                return Json(new { error = await ReadApiErrorAsync(response, "Khong tim thay ma giam gia") });
            }

            var dto = await response.Content.ReadFromJsonAsync<CouponApiDto>(JsonOptions);
            if (dto is null)
            {
                return Json(new { error = "Khong doc duoc thong tin ma giam gia" });
            }

            return Json(new
            {
                couponID = dto.couponId,
                code = dto.code,
                discountValue = dto.discountValue,
                discountType = dto.discountType,
                expiryDate = dto.expiryDate.ToString("yyyy-MM-dd"),
                minOrderValue = dto.minOrderValue,
                usageLimit = dto.usageLimit,
                usedCount = dto.usedCount,
                description = dto.description,
                maxDiscountAmount = dto.maxDiscountAmount,
                isActive = dto.isActive
            });
        }
        catch (Exception ex)
        {
            return Json(new { error = ex.Message });
        }
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<JsonResult> Edit(Coupon coupon)
    {
        try
        {
            var validationError = ValidateCouponInput(coupon);
            if (!string.IsNullOrWhiteSpace(validationError))
            {
                return Json(new { success = false, message = validationError });
            }

            var client = CreateAuthorizedClient("Ordering");
            var response = await client.PutAsJsonAsync($"/api/orders/admin/coupons/{coupon.CouponID}?scope={PlatformCouponScope}", new
            {
                code = coupon.Code?.Trim().ToUpperInvariant(),
                discountValue = coupon.DiscountValue,
                discountType = coupon.DiscountType,
                expiryDate = coupon.ExpiryDate,
                minOrderValue = coupon.MinOrderValue,
                usageLimit = coupon.UsageLimit,
                description = coupon.Description,
                maxDiscountAmount = coupon.MaxDiscountAmount,
                isActive = coupon.IsActive
            });

            if (!response.IsSuccessStatusCode)
            {
                return Json(new { success = false, message = await ReadApiErrorAsync(response, "Khong the cap nhat ma giam gia") });
            }

            return Json(new { success = true, message = "Cap nhat ma giam gia thanh cong!" });
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
            var response = await client.DeleteAsync($"/api/orders/admin/coupons/{id}?scope={PlatformCouponScope}");

            if (!response.IsSuccessStatusCode)
            {
                return Json(new { success = false, message = await ReadApiErrorAsync(response, "Khong the xoa ma giam gia") });
            }

            return Json(new { success = true, message = "Xoa ma giam gia thanh cong!" });
        }
        catch (Exception ex)
        {
            return Json(new { success = false, message = "Loi: " + ex.Message });
        }
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<JsonResult> ToggleActive(int id)
    {
        try
        {
            var client = CreateAuthorizedClient("Ordering");
            var response = await client.PostAsync($"/api/orders/admin/coupons/{id}/toggle-active?scope={PlatformCouponScope}", content: null);

            if (!response.IsSuccessStatusCode)
            {
                return Json(new { success = false, message = await ReadApiErrorAsync(response, "Khong the cap nhat trang thai") });
            }

            using var doc = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
            var root = doc.RootElement;

            return Json(new
            {
                success = true,
                message = root.TryGetProperty("message", out var message) ? message.GetString() : "Cap nhat thanh cong",
                isActive = root.TryGetProperty("isActive", out var active) && active.GetBoolean()
            });
        }
        catch (Exception ex)
        {
            return Json(new { success = false, message = "Loi: " + ex.Message });
        }
    }

    [HttpGet]
    public async Task<JsonResult> GetCustomers(string search = "")
    {
        try
        {
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
            }).ToList();

            return Json(result);
        }
        catch (Exception ex)
        {
            return Json(new { error = ex.Message });
        }
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<JsonResult> SendToCustomers(string couponCode, List<int>? customerIds, bool sendToAll = false)
    {
        try
        {
            if (string.IsNullOrWhiteSpace(couponCode))
            {
                return Json(new { success = false, message = "Ma giam gia khong hop le!" });
            }

            var ids = customerIds?.Distinct().Where(x => x > 0).ToList() ?? new List<int>();
            if (sendToAll)
            {
                ids = await GetAllCustomerIdsAsync();
            }

            if (ids.Count == 0)
            {
                return Json(new { success = false, message = "Khong co khach hang nao de gui!" });
            }

            var orderingClient = CreateAuthorizedClient("Ordering");
            var response = await orderingClient.PostAsJsonAsync($"/api/orders/admin/coupons/send?scope={PlatformCouponScope}", new
            {
                couponCode = couponCode.Trim().ToUpperInvariant(),
                customerIds = ids,
                sendToAll
            });

            if (!response.IsSuccessStatusCode)
            {
                return Json(new { success = false, message = await ReadApiErrorAsync(response, "Khong the gui ma giam gia") });
            }

            using var doc = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
            var root = doc.RootElement;

            return Json(new
            {
                success = true,
                message = root.TryGetProperty("message", out var m) ? m.GetString() : "Gui ma thanh cong",
                sent = root.TryGetProperty("sent", out var sent) ? sent.GetInt32() : 0,
                skipped = root.TryGetProperty("skipped", out var skipped) ? skipped.GetInt32() : 0
            });
        }
        catch (Exception ex)
        {
            return Json(new { success = false, message = "Loi: " + ex.Message });
        }
    }

    [HttpGet]
    public async Task<JsonResult> GenerateCode()
    {
        try
        {
            var client = CreateAuthorizedClient("Ordering");
            var response = await client.GetAsync("/api/orders/admin/coupons/generate-code");
            if (!response.IsSuccessStatusCode)
            {
                return Json(new { code = "ERROR", message = await ReadApiErrorAsync(response, "Khong the tao ma") });
            }

            using var doc = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
            var root = doc.RootElement;
            return Json(new { code = root.TryGetProperty("code", out var code) ? code.GetString() : "ERROR" });
        }
        catch (Exception ex)
        {
            return Json(new { code = "ERROR", message = ex.Message });
        }
    }

    [HttpGet]
    public async Task<IActionResult> ValidateCoupon(string code, decimal orderAmount)
    {
        try
        {
            var client = CreateAuthorizedClient("Ordering");
            var response = await client.GetAsync($"/api/orders/admin/coupons/validate?scope={PlatformCouponScope}&code={Uri.EscapeDataString(code ?? string.Empty)}&orderAmount={orderAmount}");

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

    [HttpGet]
    public async Task<IActionResult> GetStatistics()
    {
        try
        {
            var client = CreateAuthorizedClient("Ordering");
            var response = await client.GetAsync($"/api/orders/admin/coupons/statistics?scope={PlatformCouponScope}");

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

    [HttpGet]
    public async Task<IActionResult> GetUsageHistory(int id)
    {
        try
        {
            var client = CreateAuthorizedClient("Ordering");
            var response = await client.GetAsync($"/api/orders/admin/coupons/{id}/usage-history?scope={PlatformCouponScope}");
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

    [HttpGet]
    public async Task<IActionResult> GetDistributionList(int id)
    {
        try
        {
            var client = CreateAuthorizedClient("Ordering");
            var response = await client.GetAsync($"/api/orders/admin/coupons/{id}/distribution-list?scope={PlatformCouponScope}");
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

    private async Task<List<int>> GetAllCustomerIdsAsync()
    {
        var client = CreateAuthorizedClient("Identity");
        var response = await client.GetAsync("/auth/admin/customers?take=5000");
        if (!response.IsSuccessStatusCode)
        {
            return new List<int>();
        }

        var customers = DeduplicateCustomers(
            await response.Content.ReadFromJsonAsync<List<IdentityCustomerDto>>(JsonOptions)
            ?? new List<IdentityCustomerDto>());

        return customers
            .Select(x => x.userId)
            .Where(x => x > 0)
            .Distinct()
            .ToList();
    }

    private IActionResult RenderCouponView(List<Coupon> model)
    {
        ViewData["AreaName"] = "Admin";
        ViewData["LayoutPath"] = "~/Areas/Admin/Views/Shared/_LayoutAdmin.cshtml";
        ViewData["CouponTitle"] = "Voucher sàn";
        ViewData["CouponSubtitle"] = "Tạo và vận hành voucher toàn sàn cho các chiến dịch tăng trưởng của marketplace.";
        ViewData["CouponCreateLabel"] = "Tạo voucher sàn";
        ViewData["CouponEmptyTitle"] = "Chưa có voucher sàn nào";
        ViewData["CouponEmptySubtitle"] = "Tạo voucher đầu tiên để chuẩn bị cho campaign center và flash sale MVP.";
        return View("~/Areas/Seller/Views/Coupon/ManageCoupons.cshtml", model);
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
        else if (coupon.DiscountValue < 1000)
        {
            return "Gia tri giam toi thieu la 1,000₫!";
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
}
