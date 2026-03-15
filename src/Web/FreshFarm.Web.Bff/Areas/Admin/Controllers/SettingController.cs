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
public sealed class SettingController : LegacySellerControllerBase
{
    private const string AccessTokenSessionKey = "ACCESS_TOKEN";

    private readonly IHttpClientFactory _httpClientFactory;

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true
    };

    public SettingController(IHttpClientFactory httpClientFactory)
    {
        _httpClientFactory = httpClientFactory;
    }

    [HttpGet]
    public async Task<IActionResult> Index()
    {
        try
        {
            var client = CreateAuthorizedClient("Identity");
            var response = await client.GetAsync("/auth/admin/settings/store");

            if (!response.IsSuccessStatusCode)
            {
                ViewBag.Error = await ReadApiErrorAsync(response, "Lỗi khi tải cài đặt hệ thống");
                return RenderSettingsView(CreateDefaultSettings());
            }

            var payload = await response.Content.ReadFromJsonAsync<SellerSettingViewModel>(JsonOptions);
            return RenderSettingsView(payload ?? CreateDefaultSettings());
        }
        catch (Exception ex)
        {
            ViewBag.Error = "Lỗi khi tải cài đặt: " + ex.Message;
            return RenderSettingsView(CreateDefaultSettings());
        }
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Save([FromForm] SellerSettingViewModel model)
    {
        if (!ModelState.IsValid)
        {
            return Json(new { success = false, message = "Dữ liệu không hợp lệ" });
        }

        try
        {
            model.StoreName = model.StoreName.Trim();
            model.StoreAddress = model.StoreAddress.Trim();
            model.StoreEmail = model.StoreEmail.Trim();
            model.StorePhone = model.StorePhone.Trim();
            model.AdminNotificationEmail = model.AdminNotificationEmail.Trim();
            model.BankTransferInstructions = model.BankTransferInstructions?.Trim();
            model.BankAccountInfo = model.BankAccountInfo?.Trim();

            var client = CreateAuthorizedClient("Identity");
            var response = await client.PutAsJsonAsync("/auth/admin/settings/store", model);
            if (!response.IsSuccessStatusCode)
            {
                return Json(new
                {
                    success = false,
                    message = await ReadApiErrorAsync(response, "Không thể lưu cài đặt")
                });
            }

            return Json(new { success = true, message = "Lưu cài đặt thành công!" });
        }
        catch (Exception ex)
        {
            return Json(new { success = false, message = "Lỗi: " + ex.Message });
        }
    }

    private IActionResult RenderSettingsView(SellerSettingViewModel model)
    {
        ViewData["AreaName"] = "Admin";
        ViewData["LayoutPath"] = "~/Areas/Admin/Views/Shared/_LayoutAdmin.cshtml";
        ViewData["SettingsTitle"] = "Cài đặt hệ thống";
        ViewData["SettingsScopeLabel"] = "Toàn sàn";
        ViewData["ShowSellerGhnSettings"] = false;
        return View("~/Areas/Seller/Views/Setting/Index.cshtml", model);
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

    private static SellerSettingViewModel CreateDefaultSettings()
    {
        return new SellerSettingViewModel
        {
            StoreName = "Fresh Farm",
            StoreAddress = "123 Đường ABC, Quận 1, TP.HCM",
            StoreEmail = "support@freshfarm.vn",
            StorePhone = "1900 1234",
            IsCODEnabled = true,
            BankTransferInstructions = "Vui lòng chuyển khoản với nội dung: TT [Mã đơn hàng]",
            BankAccountInfo = "Ngân hàng: Vietcombank...",
            DefaultShippingFee = 30000,
            FreeShippingThreshold = 500000,
            IsEmailNewOrderEnabled = true,
            IsEmailDeliveredEnabled = true,
            IsEmailCancelledEnabled = true,
            AdminNotificationEmail = "admin@freshfarm.vn",
            CreatedAt = DateTime.UtcNow,
            UpdatedAt = DateTime.UtcNow
        };
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
