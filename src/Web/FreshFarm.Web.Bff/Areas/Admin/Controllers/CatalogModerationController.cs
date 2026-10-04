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
public sealed class CatalogModerationController : LegacySellerControllerBase
{
    private const string AccessTokenSessionKey = "ACCESS_TOKEN";
    private const int DefaultPageSize = 10;

    private static readonly string[] ModerationKeywords =
    {
        "thuoc la", "rượu", "ruou", "bia", "dao", "kiem", "sung",
        "fake", "giả", "gia mao", "adult", "sex", "18+", "cấm", "cam"
    };

    private readonly IHttpClientFactory _httpClientFactory;

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true
    };

    public CatalogModerationController(IHttpClientFactory httpClientFactory)
    {
        _httpClientFactory = httpClientFactory;
    }

    [HttpGet]
    public async Task<IActionResult> Index(
        string? q = null,
        string state = "all",
        string risk = "all",
        string? keyword = null,
        int page = 1)
    {
        if (page < 1)
        {
            page = 1;
        }

        var model = new ProductModerationPageViewModel
        {
            Query = q ?? string.Empty,
            State = NormalizeState(state),
            Risk = NormalizeRisk(risk),
            Keyword = keyword ?? string.Empty,
            Page = page,
            PageSize = DefaultPageSize,
            KeywordSuggestions = ModerationKeywords
        };

        try
        {
            var products = await GetProductsAsync();
            var rows = products.Select(BuildModerationRow).ToList();

            model.TotalProducts = rows.Count;
            model.FlaggedProducts = rows.Count(r => string.Equals(r.ModerationState, "flagged", StringComparison.OrdinalIgnoreCase));
            model.HighRiskProducts = rows.Count(r => string.Equals(r.RiskLevel, "high", StringComparison.OrdinalIgnoreCase));
            model.HiddenProducts = rows.Count(r => !r.Status);
            model.CleanProducts = rows.Count(r => string.Equals(r.ModerationState, "clean", StringComparison.OrdinalIgnoreCase));

            IEnumerable<ProductModerationRowViewModel> query = rows;

            if (!string.IsNullOrWhiteSpace(model.Query))
            {
                var term = model.Query.Trim().ToLowerInvariant();
                query = query.Where(r =>
                    r.ProductName.ToLowerInvariant().Contains(term) ||
                    r.Sku.ToLowerInvariant().Contains(term) ||
                    r.CategoryName.ToLowerInvariant().Contains(term));
            }

            if (!string.Equals(model.State, "all", StringComparison.OrdinalIgnoreCase))
            {
                query = query.Where(r => string.Equals(r.ModerationState, model.State, StringComparison.OrdinalIgnoreCase));
            }

            if (!string.Equals(model.Risk, "all", StringComparison.OrdinalIgnoreCase))
            {
                query = query.Where(r => string.Equals(r.RiskLevel, model.Risk, StringComparison.OrdinalIgnoreCase));
            }

            if (!string.IsNullOrWhiteSpace(model.Keyword))
            {
                var keywordTerm = model.Keyword.Trim().ToLowerInvariant();
                query = query.Where(r => r.MatchedKeywords.Any(k => k.Contains(keywordTerm, StringComparison.OrdinalIgnoreCase)));
            }

            var filtered = query
                .OrderByDescending(GetRiskWeight)
                .ThenByDescending(r => r.IsManuallyDisabled)
                .ThenBy(r => r.ProductName)
                .ToList();

            model.Total = filtered.Count;
            model.TotalPages = (int)Math.Ceiling(model.Total / (double)model.PageSize);
            if (model.TotalPages <= 0)
            {
                model.TotalPages = 1;
            }

            if (model.Page > model.TotalPages)
            {
                model.Page = model.TotalPages;
            }

            model.Rows = filtered
                .Skip((model.Page - 1) * model.PageSize)
                .Take(model.PageSize)
                .ToList();
        }
        catch (Exception ex)
        {
            TempData["ErrorMessage"] = "Không thể tải hàng đợi kiểm duyệt sản phẩm: " + ex.Message;
        }

        return View(model);
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Approve(int id)
    {
        return await ToggleProductStatusAsync(id, expectedActiveState: true, "Đã duyệt và mở bán sản phẩm.");
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Reject(int id)
    {
        return await ToggleProductStatusAsync(id, expectedActiveState: false, "Đã ẩn sản phẩm khỏi gian hàng.");
    }

    private async Task<IActionResult> ToggleProductStatusAsync(int id, bool expectedActiveState, string successMessage)
    {
        if (id <= 0)
        {
            TempData["ErrorMessage"] = "ID sản phẩm không hợp lệ.";
            return RedirectToAction(nameof(Index));
        }

        try
        {
            var client = CreateCatalogClient();
            var productResponse = await client.GetAsync($"/api/products/{id}");
            if (!productResponse.IsSuccessStatusCode)
            {
                TempData["ErrorMessage"] = await ReadApiErrorAsync(productResponse, "Không tìm thấy sản phẩm.");
                return RedirectToAction(nameof(Index));
            }

            var product = await productResponse.Content.ReadFromJsonAsync<ApiProductDto>(JsonOptions);
            if (product is null)
            {
                TempData["ErrorMessage"] = "Không đọc được dữ liệu sản phẩm.";
                return RedirectToAction(nameof(Index));
            }

            if (product.Status == expectedActiveState)
            {
                TempData["SuccessMessage"] = expectedActiveState
                    ? "Sản phẩm đã ở trạng thái đang bán."
                    : "Sản phẩm đã ở trạng thái ẩn.";
                return RedirectToAction(nameof(Index));
            }

            var toggleResponse = await client.PostAsync($"/api/products/{id}/toggle-status", content: null);
            if (!toggleResponse.IsSuccessStatusCode)
            {
                TempData["ErrorMessage"] = await ReadApiErrorAsync(toggleResponse, "Không thể cập nhật trạng thái sản phẩm.");
                return RedirectToAction(nameof(Index));
            }

            TempData["SuccessMessage"] = successMessage;
        }
        catch (Exception ex)
        {
            TempData["ErrorMessage"] = "Không thể cập nhật quyết định kiểm duyệt: " + ex.Message;
        }

        return RedirectToAction(nameof(Index));
    }

    private async Task<List<ApiProductDto>> GetProductsAsync()
    {
        var client = CreateCatalogClient();
        var response = await client.GetAsync("/api/products");
        if (!response.IsSuccessStatusCode)
        {
            throw new InvalidOperationException(await ReadApiErrorAsync(response, "Không thể tải danh sách sản phẩm."));
        }

        return DeduplicateProducts(
            await response.Content.ReadFromJsonAsync<List<ApiProductDto>>(JsonOptions) ?? new List<ApiProductDto>());
    }

    private ProductModerationRowViewModel BuildModerationRow(ApiProductDto product)
    {
        var textToScan = string.Join(" ", new[]
        {
            product.ProductName,
            product.ShortDescription,
            product.LongDescription
        }.Where(x => !string.IsNullOrWhiteSpace(x))).ToLowerInvariant();

        var matchedKeywords = ModerationKeywords
            .Where(keyword => textToScan.Contains(keyword, StringComparison.OrdinalIgnoreCase))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();

        var issues = new List<string>();
        var riskLevel = "low";

        if (matchedKeywords.Count > 0)
        {
            issues.Add("Phát hiện từ khóa nhạy cảm: " + string.Join(", ", matchedKeywords));
            riskLevel = "high";
        }

        if (product.Price <= 0)
        {
            issues.Add("Giá bán bằng 0 hoặc âm.");
            riskLevel = "high";
        }

        if (string.IsNullOrWhiteSpace(product.ImageFileName))
        {
            issues.Add("Thiếu ảnh đại diện.");
            riskLevel = ElevateRisk(riskLevel, "medium");
        }

        if (string.IsNullOrWhiteSpace(product.ShortDescription))
        {
            issues.Add("Thiếu mô tả ngắn.");
            riskLevel = ElevateRisk(riskLevel, "medium");
        }

        if (string.IsNullOrWhiteSpace(product.LongDescription))
        {
            issues.Add("Thiếu mô tả chi tiết.");
            riskLevel = ElevateRisk(riskLevel, "medium");
        }

        if (product.StockQuantity <= 0)
        {
            issues.Add("Tồn kho bằng 0.");
            riskLevel = ElevateRisk(riskLevel, "medium");
        }

        if (!product.Status && product.IsManuallyDisabled)
        {
            issues.Add("Đang bị ẩn thủ công.");
        }

        var moderationState = issues.Count == 0 ? "clean" : "flagged";

        return new ProductModerationRowViewModel
        {
            ProductId = product.ProductId,
            ProductName = product.ProductName ?? string.Empty,
            Sku = product.Sku ?? string.Empty,
            CategoryName = product.CategoryName ?? string.Empty,
            Price = product.Price,
            StockQuantity = product.StockQuantity,
            Status = product.Status,
            IsManuallyDisabled = product.IsManuallyDisabled,
            ImageFileName = product.ImageFileName,
            ShortDescription = product.ShortDescription ?? string.Empty,
            LongDescription = product.LongDescription ?? string.Empty,
            ModerationState = moderationState,
            RiskLevel = issues.Count == 0 ? "low" : riskLevel,
            MatchedKeywords = matchedKeywords,
            Issues = issues
        };
    }

    private static int GetRiskWeight(ProductModerationRowViewModel row)
    {
        return row.RiskLevel switch
        {
            "high" => 3,
            "medium" => 2,
            _ => 1
        };
    }

    private static string ElevateRisk(string current, string candidate)
    {
        if (current == "high" || current == candidate)
        {
            return current;
        }

        if (candidate == "high")
        {
            return "high";
        }

        return current == "low" ? candidate : current;
    }

    private static List<ApiProductDto> DeduplicateProducts(IEnumerable<ApiProductDto> products)
    {
        return products
            .Where(product => product.ProductId > 0)
            .GroupBy(product => product.ProductId)
            .Select(group => group
                .OrderByDescending(CalculateProductScore)
                .ThenByDescending(CalculateProductSignalLength)
                .ThenByDescending(product => product.UpdatedDate ?? product.CreatedDate)
                .First())
            .ToList();
    }

    private static int CalculateProductScore(ApiProductDto product)
    {
        var score = 0;

        score += HasMeaningfulValue(product.ProductName) ? 3 : 0;
        score += HasMeaningfulValue(product.Sku) ? 2 : 0;
        score += HasMeaningfulValue(product.CategoryName) ? 2 : 0;
        score += HasMeaningfulValue(product.ShortDescription) ? 1 : 0;
        score += HasMeaningfulValue(product.LongDescription) ? 1 : 0;
        score += HasMeaningfulValue(product.ImageFileName) ? 1 : 0;
        score += product.Price > 0 ? 1 : 0;
        score += product.StockQuantity > 0 ? 1 : 0;

        return score;
    }

    private static int CalculateProductSignalLength(ApiProductDto product)
    {
        var values = new[]
        {
            product.ProductName,
            product.Sku,
            product.CategoryName,
            product.ShortDescription,
            product.LongDescription,
            product.ImageFileName
        };

        return values.Sum(value => value?.Length ?? 0);
    }

    private static bool HasMeaningfulValue(string? value) => !string.IsNullOrWhiteSpace(value);

    private static string NormalizeState(string state)
    {
        return (state ?? string.Empty).Trim().ToLowerInvariant() switch
        {
            "flagged" => "flagged",
            "clean" => "clean",
            _ => "all"
        };
    }

    private static string NormalizeRisk(string risk)
    {
        return (risk ?? string.Empty).Trim().ToLowerInvariant() switch
        {
            "high" => "high",
            "medium" => "medium",
            "low" => "low",
            _ => "all"
        };
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

    private sealed class ApiProductDto
    {
        public int ProductId { get; set; }

        public string? ProductName { get; set; }

        public string? Sku { get; set; }

        public decimal Price { get; set; }

        public bool Status { get; set; }

        public int StockQuantity { get; set; }

        public string? ImageFileName { get; set; }

        public string? ShortDescription { get; set; }

        public string? LongDescription { get; set; }

        public DateTime CreatedDate { get; set; }

        public DateTime? UpdatedDate { get; set; }

        public bool IsManuallyDisabled { get; set; }

        public string? CategoryName { get; set; }
    }
}
