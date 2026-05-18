using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using FreshFarm.Web.Bff.Areas.Seller.Infrastructure;
using FreshFarm.Web.Bff.Areas.Seller.Models;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Rendering;

namespace FreshFarm.Web.Bff.Areas.Seller.Controllers;

[Authorize(Roles = "Seller")]
[ResponseCache(Duration = 0, Location = ResponseCacheLocation.None, NoStore = true)]
[Area("Seller")]
public sealed class FreshOpsController : LegacySellerControllerBase
{
    private const string AccessTokenSessionKey = "ACCESS_TOKEN";

    private readonly IHttpClientFactory _httpClientFactory;

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true
    };

    public FreshOpsController(IHttpClientFactory httpClientFactory)
    {
        _httpClientFactory = httpClientFactory;
    }

    [HttpGet]
    public async Task<IActionResult> Index()
    {
        var model = new FreshOpsSellerPageViewModel
        {
            NewLot = new FreshLotSellerEditorInput
            {
                ReceivedAt = DateTime.Now,
                Status = "active",
                QualityStatus = "ok"
            }
        };

        await PopulateProductOptions(model);
        return View(model);
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> CreateLot([Bind(Prefix = "NewLot")] FreshLotSellerEditorInput input)
    {
        if (!ModelState.IsValid)
        {
            var invalidModel = new FreshOpsSellerPageViewModel { NewLot = input };
            await PopulateProductOptions(invalidModel);
            return View(nameof(Index), invalidModel);
        }

        try
        {
            var client = CreateCatalogClient();
            var response = await client.PostAsJsonAsync("/api/catalog/seller/fresh-ops/lots", input);
            TempData[response.IsSuccessStatusCode ? "SuccessMessage" : "ErrorMessage"] =
                response.IsSuccessStatusCode
                    ? "Đã tạo lô hàng tươi."
                    : await ReadApiErrorAsync(response, "Không thể tạo lô hàng tươi.");
        }
        catch (Exception ex)
        {
            TempData["ErrorMessage"] = "Lỗi khi tạo lô hàng: " + ex.Message;
        }

        return RedirectToAction(nameof(Index));
    }

    private async Task PopulateProductOptions(FreshOpsSellerPageViewModel model)
    {
        var client = CreateCatalogClient();
        var response = await client.GetAsync("/api/admin/warehouse/products?page=1&pageSize=5000");

        if (!response.IsSuccessStatusCode)
        {
            model.ProductOptions = [];
            return;
        }

        var payload = await response.Content.ReadFromJsonAsync<WarehouseProductsApiResponse>(JsonOptions);
        model.ProductOptions = payload?.Data?.Products?
            .OrderBy(product => product.ProductName)
            .Select(product => new SelectListItem
            {
                Value = product.ProductId.ToString(),
                Text = $"{product.ProductName} ({product.Sku})"
            })
            .ToList() ?? [];
    }

    private HttpClient CreateCatalogClient()
    {
        var client = _httpClientFactory.CreateClient("Catalog");
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
        }

        return fallback;
    }

    private sealed class WarehouseProductsApiResponse
    {
        public WarehouseProductsDataDto? Data { get; set; }
    }

    private sealed class WarehouseProductsDataDto
    {
        public List<WarehouseProductDto>? Products { get; set; }
    }

    private sealed class WarehouseProductDto
    {
        public int ProductId { get; set; }

        public string ProductName { get; set; } = string.Empty;

        public string Sku { get; set; } = string.Empty;
    }
}
