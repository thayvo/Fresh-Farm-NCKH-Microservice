using FreshFarm.Web.Bff.Areas.Seller.Infrastructure;
using FreshFarm.Web.Bff.Areas.Seller.Models;
using FreshFarm.Web.Bff.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.RegularExpressions;
using System.Text.Json;

namespace FreshFarm.Web.Bff.Areas.Seller.Controllers;

[Authorize(Roles = "Seller")]
[ResponseCache(Duration = 0, Location = ResponseCacheLocation.None, NoStore = true)]
[Area("Seller")]
public class SettingController : LegacySellerControllerBase
{
    private const string AccessTokenSessionKey = "ACCESS_TOKEN";
    private static readonly Regex VietnamPhoneRegex = new(@"^(0(3|5|7|8|9)\d{8}|\+84(3|5|7|8|9)\d{8})$", RegexOptions.Compiled);

    private readonly IHttpClientFactory _httpClientFactory;
    private readonly IGhnSandboxService _ghnSandboxService;

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true
    };

    public SettingController(IHttpClientFactory httpClientFactory, IGhnSandboxService ghnSandboxService)
    {
        _httpClientFactory = httpClientFactory;
        _ghnSandboxService = ghnSandboxService;
    }

    [HttpGet]
    public async Task<IActionResult> Index()
    {
        ViewData["ShowSellerGhnSettings"] = true;
        ViewData["GhnSandboxConfigured"] = _ghnSandboxService.IsConfigured;
        ViewData["GhnSandboxShopId"] = _ghnSandboxService.ShopId?.ToString() ?? "Chưa có";

        try
        {
            var client = CreateAuthorizedClient("Identity");
            var response = await client.GetAsync("/auth/admin/settings/store");

            if (!response.IsSuccessStatusCode)
            {
                ViewBag.Error = await ReadApiErrorAsync(response, "Lỗi khi tải cài đặt cửa hàng.");
                return View(CreateDefaultSettings());
            }

            var payload = await response.Content.ReadFromJsonAsync<SellerSettingViewModel>(JsonOptions);
            return View(payload ?? CreateDefaultSettings());
        }
        catch (Exception ex)
        {
            ViewBag.Error = "Lỗi khi tải cài đặt: " + ex.Message;
            return View(CreateDefaultSettings());
        }
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Save([FromForm] SellerSettingViewModel model)
    {
        ViewData["ShowSellerGhnSettings"] = true;
        ViewData["GhnSandboxConfigured"] = _ghnSandboxService.IsConfigured;
        ViewData["GhnSandboxShopId"] = _ghnSandboxService.ShopId?.ToString() ?? "Chưa có";

        try
        {
            model.StoreName = model.StoreName.Trim();
            model.StoreAddress = model.StoreAddress.Trim();
            model.StoreEmail = model.StoreEmail.Trim();
            model.StorePhone = model.StorePhone.Trim();
            model.AdminNotificationEmail = model.AdminNotificationEmail.Trim();
            model.BankTransferInstructions = model.BankTransferInstructions?.Trim();
            model.BankAccountInfo = model.BankAccountInfo?.Trim();
            model.GhnPickupName = model.GhnPickupName?.Trim();
            model.GhnPickupPhone = model.GhnPickupPhone?.Trim();
            model.GhnPickupAddress = model.GhnPickupAddress?.Trim();
            model.GhnProvinceName = model.GhnProvinceName?.Trim();
            model.GhnDistrictName = model.GhnDistrictName?.Trim();
            model.GhnWardCode = model.GhnWardCode?.Trim();
            model.GhnWardName = model.GhnWardName?.Trim();

            if (!IsVietnamPhone(model.StorePhone))
            {
                return Json(new
                {
                    success = false,
                    message = "Số điện thoại cửa hàng phải đúng định dạng di động Việt Nam, ví dụ 0328898307 hoặc +84328898307."
                });
            }

            if (!string.IsNullOrWhiteSpace(model.GhnPickupPhone) && !IsVietnamPhone(model.GhnPickupPhone))
            {
                return Json(new
                {
                    success = false,
                    message = "Số điện thoại lấy hàng GHN phải đúng định dạng di động Việt Nam, ví dụ 0328898307 hoặc +84328898307."
                });
            }

            if (!ModelState.IsValid)
            {
                return Json(new { success = false, message = "Dữ liệu không hợp lệ." });
            }

            var client = CreateAuthorizedClient("Identity");
            var response = await client.PutAsJsonAsync("/auth/admin/settings/store", model);
            if (!response.IsSuccessStatusCode)
            {
                return Json(new
                {
                    success = false,
                    message = await ReadApiErrorAsync(response, $"Không thể lưu cài đặt (HTTP {(int)response.StatusCode}).")
                });
            }

            return Json(new { success = true, message = "Lưu cài đặt thành công." });
        }
        catch (Exception ex)
        {
            return Json(new { success = false, message = "Lỗi: " + ex.Message });
        }
    }

    [HttpGet]
    public async Task<JsonResult> GetGhnProvinces(CancellationToken cancellationToken)
    {
        var items = await _ghnSandboxService.GetProvincesAsync(cancellationToken);
        return Json(new
        {
            success = true,
            configured = _ghnSandboxService.IsConfigured,
            shopId = _ghnSandboxService.ShopId,
            items
        });
    }

    [HttpGet]
    public async Task<JsonResult> GetGhnDistricts(int provinceId, CancellationToken cancellationToken)
    {
        if (provinceId <= 0)
        {
            return Json(new
            {
                success = false,
                message = "Thiếu mã tỉnh/thành GHN."
            });
        }

        var items = await _ghnSandboxService.GetDistrictsAsync(provinceId, cancellationToken);
        return Json(new
        {
            success = true,
            provinceId,
            items
        });
    }

    [HttpGet]
    public async Task<JsonResult> GetGhnWards(int districtId, CancellationToken cancellationToken)
    {
        if (districtId <= 0)
        {
            return Json(new
            {
                success = false,
                message = "Thiếu mã quận/huyện GHN."
            });
        }

        var items = await _ghnSandboxService.GetWardsAsync(districtId, cancellationToken);
        return Json(new
        {
            success = true,
            districtId,
            items
        });
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
            StoreAddress = "123 Duong ABC, Quan 1, TP.HCM",
            StoreEmail = "support@freshfarm.vn",
            StorePhone = "1900 1234",
            IsCODEnabled = true,
            BankTransferInstructions = "Vui long chuyen khoan voi noi dung: TT [Ma don hang]",
            BankAccountInfo = "Ngan hang: Vietcombank...",
            DefaultShippingFee = 30000,
            FreeShippingThreshold = 500000,
            IsEmailNewOrderEnabled = true,
            IsEmailDeliveredEnabled = true,
            IsEmailCancelledEnabled = true,
            AdminNotificationEmail = "admin@freshfarm.vn",
            GhnPickupName = string.Empty,
            GhnPickupPhone = string.Empty,
            GhnPickupAddress = string.Empty,
            CreatedAt = DateTime.UtcNow,
            UpdatedAt = DateTime.UtcNow
        };
    }

    private static bool IsVietnamPhone(string? value)
    {
        return !string.IsNullOrWhiteSpace(value) && VietnamPhoneRegex.IsMatch(value.Trim());
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
                if (root.TryGetProperty("errors", out var errorsProp) && errorsProp.ValueKind == JsonValueKind.Object)
                {
                    foreach (var property in errorsProp.EnumerateObject())
                    {
                        if (property.Value.ValueKind == JsonValueKind.Array)
                        {
                            foreach (var item in property.Value.EnumerateArray())
                            {
                                if (item.ValueKind == JsonValueKind.String && !string.IsNullOrWhiteSpace(item.GetString()))
                                {
                                    return item.GetString()!;
                                }
                            }
                        }
                    }
                }

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

            if (root.ValueKind == JsonValueKind.String)
            {
                return root.GetString() ?? fallback;
            }

            return json;
        }
        catch
        {
            return fallback;
        }
    }
}
