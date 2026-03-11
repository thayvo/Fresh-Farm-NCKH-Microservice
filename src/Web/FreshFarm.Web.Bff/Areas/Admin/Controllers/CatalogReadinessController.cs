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
public sealed class CatalogReadinessController : LegacySellerControllerBase
{
    private const string AccessTokenSessionKey = "ACCESS_TOKEN";

    private readonly IHttpClientFactory _httpClientFactory;

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true
    };

    public CatalogReadinessController(IHttpClientFactory httpClientFactory)
    {
        _httpClientFactory = httpClientFactory;
    }

    [HttpGet]
    public async Task<IActionResult> Index(string? q = null, int? categoryId = null, string? state = null)
    {
        var model = new CatalogReadinessPageViewModel
        {
            Query = q?.Trim() ?? string.Empty,
            CategoryId = categoryId > 0 ? categoryId : null,
            State = NormalizeState(state)
        };
        SeedFallbackOptions(model);

        try
        {
            var client = CreateCatalogClient();
            var response = await client.GetAsync(BuildCenterEndpoint(model));
            if (!response.IsSuccessStatusCode)
            {
                ViewBag.Error = await ReadApiErrorAsync(response, "Khong the tai catalog readiness.");
                return View(model);
            }

            var payload = await response.Content.ReadFromJsonAsync<CatalogReadinessApiResponse>(JsonOptions);
            if (payload is null)
            {
                ViewBag.Error = "Khong doc duoc du lieu catalog readiness.";
                return View(model);
            }

            model.Query = payload.Filters?.Q ?? model.Query;
            model.CategoryId = payload.Filters?.CategoryId > 0 ? payload.Filters.CategoryId : model.CategoryId;
            model.State = payload.Filters?.State ?? model.State;
            model.Stats = new CatalogReadinessStatsViewModel
            {
                TotalCategories = payload.Stats?.TotalCategories ?? 0,
                TotalAttributes = payload.Stats?.TotalAttributes ?? 0,
                RequiredAttributes = payload.Stats?.RequiredAttributes ?? 0,
                ProductsReady = payload.Stats?.ProductsReady ?? 0,
                ProductsMissing = payload.Stats?.ProductsMissing ?? 0,
                FacetAttributes = payload.Stats?.FacetAttributes ?? 0
            };
            model.CategoryOptions = payload.Filters?.Categories?.Select(MapCategoryOption).ToList() ?? model.CategoryOptions;
            model.StateOptions = payload.Filters?.StateOptions?.Select(MapStateOption).ToList() ?? model.StateOptions;
            model.Attributes = payload.Attributes?.Select(MapAttributeRow).ToList() ?? new List<CatalogAttributeRowViewModel>();
            model.ReadinessRows = payload.Readiness?.Select(MapReadinessRow).ToList() ?? new List<CatalogProductReadinessRowViewModel>();

            if (model.NewAttribute.CategoryId <= 0 && model.CategoryId.HasValue)
            {
                model.NewAttribute.CategoryId = model.CategoryId.Value;
            }
        }
        catch (Exception ex)
        {
            ViewBag.Error = "Loi khi tai catalog readiness: " + ex.Message;
        }

        return View(model);
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> CreateAttribute(CatalogAttributeEditorInput input, string? q = null, int? categoryId = null, string? state = null)
    {
        if (!ModelState.IsValid)
        {
            TempData["ErrorMessage"] = "Form attribute chua hop le.";
            return RedirectToAction(nameof(Index), BuildRouteValues(q, categoryId, state, input.CategoryId));
        }

        try
        {
            var client = CreateCatalogClient();
            var response = await client.PostAsJsonAsync("/api/catalog/admin/readiness/category-attributes", input);
            if (!response.IsSuccessStatusCode)
            {
                TempData["ErrorMessage"] = await ReadApiErrorAsync(response, "Khong the tao category attribute.");
                return RedirectToAction(nameof(Index), BuildRouteValues(q, categoryId, state, input.CategoryId));
            }

            TempData["SuccessMessage"] = "Da tao category attribute.";
        }
        catch (Exception ex)
        {
            TempData["ErrorMessage"] = "Loi khi tao category attribute: " + ex.Message;
        }

        return RedirectToAction(nameof(Index), BuildRouteValues(q, categoryId, state, input.CategoryId));
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> UpdateAttribute(int id, CatalogAttributeEditorInput input, string? q = null, int? categoryId = null, string? state = null)
    {
        if (id <= 0)
        {
            TempData["ErrorMessage"] = "Category attribute id khong hop le.";
            return RedirectToAction(nameof(Index), BuildRouteValues(q, categoryId, state, categoryId));
        }

        if (!ModelState.IsValid)
        {
            TempData["ErrorMessage"] = "Form cap nhat attribute chua hop le.";
            return RedirectToAction(nameof(Index), BuildRouteValues(q, categoryId, state, input.CategoryId));
        }

        try
        {
            var client = CreateCatalogClient();
            var response = await client.PutAsJsonAsync($"/api/catalog/admin/readiness/category-attributes/{id}", input);
            if (!response.IsSuccessStatusCode)
            {
                TempData["ErrorMessage"] = await ReadApiErrorAsync(response, "Khong the cap nhat category attribute.");
                return RedirectToAction(nameof(Index), BuildRouteValues(q, categoryId, state, input.CategoryId));
            }

            TempData["SuccessMessage"] = "Da cap nhat category attribute.";
        }
        catch (Exception ex)
        {
            TempData["ErrorMessage"] = "Loi khi cap nhat category attribute: " + ex.Message;
        }

        return RedirectToAction(nameof(Index), BuildRouteValues(q, categoryId, state, input.CategoryId));
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> SyncProductInfo(int? categoryId = null, string? q = null, string? state = null)
    {
        try
        {
            var client = CreateCatalogClient();
            var endpoint = "/api/catalog/admin/readiness/sync-product-info";
            if (categoryId.HasValue && categoryId.Value > 0)
            {
                endpoint += "?categoryId=" + categoryId.Value;
            }

            var response = await client.PostAsync(endpoint, content: null);
            if (!response.IsSuccessStatusCode)
            {
                TempData["ErrorMessage"] = await ReadApiErrorAsync(response, "Khong the sync product info.");
                return RedirectToAction(nameof(Index), BuildRouteValues(q, categoryId, state, categoryId));
            }

            TempData["SuccessMessage"] = await ReadApiErrorAsync(response, "Da sync product info vao readiness.");
        }
        catch (Exception ex)
        {
            TempData["ErrorMessage"] = "Loi khi sync product info: " + ex.Message;
        }

        return RedirectToAction(nameof(Index), BuildRouteValues(q, categoryId, state, categoryId));
    }

    private HttpClient CreateCatalogClient()
    {
        var client = _httpClientFactory.CreateClient("Catalog");
        client.DefaultRequestHeaders.Remove("Authorization");

        var token = GetAccessToken(AccessTokenSessionKey);
        if (!string.IsNullOrWhiteSpace(token))
        {
            client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);
        }

        return client;
    }

    private static object BuildRouteValues(string? q, int? categoryId, string? state, int? fallbackCategoryId)
        => new
        {
            q,
            categoryId = categoryId > 0 ? categoryId : fallbackCategoryId,
            state = NormalizeState(state)
        };

    private static string BuildCenterEndpoint(CatalogReadinessPageViewModel model)
    {
        var query = new List<string>
        {
            "state=" + Uri.EscapeDataString(model.State)
        };

        if (!string.IsNullOrWhiteSpace(model.Query))
        {
            query.Add("q=" + Uri.EscapeDataString(model.Query));
        }

        if (model.CategoryId.HasValue && model.CategoryId.Value > 0)
        {
            query.Add("categoryId=" + model.CategoryId.Value);
        }

        return "/api/catalog/admin/readiness/center?" + string.Join("&", query);
    }

    private static string NormalizeState(string? value)
        => (value ?? string.Empty).Trim().ToLowerInvariant() switch
        {
            "missing" => "missing",
            "ready" => "ready",
            _ => "all"
        };

    private static void SeedFallbackOptions(CatalogReadinessPageViewModel model)
    {
        model.StateOptions =
        [
            new CatalogReadinessOptionViewModel { Value = "all", Text = "Tat ca" },
            new CatalogReadinessOptionViewModel { Value = "missing", Text = "Thieu attribute" },
            new CatalogReadinessOptionViewModel { Value = "ready", Text = "Da san sang" }
        ];
    }

    private static CatalogReadinessOptionViewModel MapCategoryOption(CatalogCategoryOptionApiModel option)
        => new()
        {
            Value = option.CategoryId.ToString(),
            Text = option.CategoryName ?? string.Empty,
            IsActive = option.IsActive
        };

    private static CatalogReadinessOptionViewModel MapStateOption(CatalogStateOptionApiModel option)
        => new()
        {
            Value = option.Value ?? string.Empty,
            Text = option.Text ?? string.Empty
        };

    private static CatalogAttributeRowViewModel MapAttributeRow(CatalogAttributeApiModel row)
        => new()
        {
            CategoryAttributeId = row.CategoryAttributeId,
            CategoryId = row.CategoryId,
            CategoryName = row.CategoryName ?? string.Empty,
            AttributeKey = row.AttributeKey ?? string.Empty,
            DisplayName = row.DisplayName ?? string.Empty,
            InputType = row.InputType ?? string.Empty,
            IsRequired = row.IsRequired,
            IsFacet = row.IsFacet,
            SortOrder = row.SortOrder,
            Placeholder = row.Placeholder,
            IsActive = row.IsActive,
            CreatedAt = row.CreatedAt,
            UpdatedAt = row.UpdatedAt
        };

    private static CatalogProductReadinessRowViewModel MapReadinessRow(CatalogProductReadinessApiModel row)
        => new()
        {
            ProductId = row.ProductId,
            ProductName = row.ProductName ?? string.Empty,
            Sku = row.Sku ?? string.Empty,
            Status = row.Status,
            CategoryId = row.CategoryId,
            CategoryName = row.CategoryName ?? string.Empty,
            CreatedDate = row.CreatedDate,
            TotalRequired = row.TotalRequired,
            ReadyCount = row.ReadyCount,
            MissingCount = row.MissingCount,
            ReadinessScore = row.ReadinessScore,
            MissingAttributes = row.MissingAttributes ?? new List<string>()
        };

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
        }
        catch
        {
        }

        return fallback;
    }

    private sealed class CatalogReadinessApiResponse
    {
        public CatalogStatsApiModel? Stats { get; set; }

        public CatalogFiltersApiModel? Filters { get; set; }

        public List<CatalogAttributeApiModel>? Attributes { get; set; }

        public List<CatalogProductReadinessApiModel>? Readiness { get; set; }
    }

    private sealed class CatalogStatsApiModel
    {
        public int TotalCategories { get; set; }

        public int TotalAttributes { get; set; }

        public int RequiredAttributes { get; set; }

        public int ProductsReady { get; set; }

        public int ProductsMissing { get; set; }

        public int FacetAttributes { get; set; }
    }

    private sealed class CatalogFiltersApiModel
    {
        public string? Q { get; set; }

        public int? CategoryId { get; set; }

        public string? State { get; set; }

        public List<CatalogStateOptionApiModel>? StateOptions { get; set; }

        public List<CatalogCategoryOptionApiModel>? Categories { get; set; }
    }

    private sealed class CatalogStateOptionApiModel
    {
        public string? Value { get; set; }

        public string? Text { get; set; }
    }

    private sealed class CatalogCategoryOptionApiModel
    {
        public int CategoryId { get; set; }

        public string? CategoryName { get; set; }

        public bool IsActive { get; set; }
    }

    private sealed class CatalogAttributeApiModel
    {
        public int CategoryAttributeId { get; set; }

        public int CategoryId { get; set; }

        public string? CategoryName { get; set; }

        public string? AttributeKey { get; set; }

        public string? DisplayName { get; set; }

        public string? InputType { get; set; }

        public bool IsRequired { get; set; }

        public bool IsFacet { get; set; }

        public int SortOrder { get; set; }

        public string? Placeholder { get; set; }

        public bool IsActive { get; set; }

        public DateTime CreatedAt { get; set; }

        public DateTime? UpdatedAt { get; set; }
    }

    private sealed class CatalogProductReadinessApiModel
    {
        public int ProductId { get; set; }

        public string? ProductName { get; set; }

        public string? Sku { get; set; }

        public bool Status { get; set; }

        public int CategoryId { get; set; }

        public string? CategoryName { get; set; }

        public DateTime CreatedDate { get; set; }

        public int TotalRequired { get; set; }

        public int ReadyCount { get; set; }

        public int MissingCount { get; set; }

        public int ReadinessScore { get; set; }

        public List<string>? MissingAttributes { get; set; }
    }
}
