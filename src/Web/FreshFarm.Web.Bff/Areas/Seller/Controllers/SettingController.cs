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
public class SettingController : LegacySellerControllerBase
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
                ViewBag.Error = await ReadApiErrorAsync(response, "Loi khi tai cai dat cua hang");
                return View(CreateDefaultSettings());
            }

            var payload = await response.Content.ReadFromJsonAsync<SellerSettingViewModel>(JsonOptions);
            return View(payload ?? CreateDefaultSettings());
        }
        catch (Exception ex)
        {
            ViewBag.Error = "Loi khi tai cai dat: " + ex.Message;
            return View(CreateDefaultSettings());
        }
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Save([FromForm] SellerSettingViewModel model)
    {
        if (!ModelState.IsValid)
        {
            return Json(new { success = false, message = "Du lieu khong hop le" });
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
                    message = await ReadApiErrorAsync(response, "Khong the luu cai dat")
                });
            }

            return Json(new { success = true, message = "Luu cai dat thanh cong!" });
        }
        catch (Exception ex)
        {
            return Json(new { success = false, message = "Loi: " + ex.Message });
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
