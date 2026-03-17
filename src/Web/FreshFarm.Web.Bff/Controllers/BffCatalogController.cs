using Microsoft.AspNetCore.Mvc;
using System.Globalization;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;

namespace FreshFarm.Web.Bff.Controllers;

[ApiController]
[Route("bff")]
public sealed class BffCatalogController : ControllerBase
{
    private const string AccessTokenSessionKey = "ACCESS_TOKEN";
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
    {
        PropertyNameCaseInsensitive = true
    };

    private readonly IHttpClientFactory _httpClientFactory;

    public BffCatalogController(IHttpClientFactory httpClientFactory)
    {
        _httpClientFactory = httpClientFactory;
    }

    private void AttachAccessToken(HttpClient client)
    {
        var authHeader = Request.Headers.Authorization.ToString();
        if (!string.IsNullOrWhiteSpace(authHeader))
        {
            client.DefaultRequestHeaders.TryAddWithoutValidation("Authorization", authHeader);
            return;
        }

        var token = HttpContext.Session.GetString(AccessTokenSessionKey);
        if (!string.IsNullOrWhiteSpace(token))
        {
            client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);
        }
    }

    [HttpGet("products")]
    public async Task<IActionResult> GetProducts(
        [FromQuery] string? name,
        [FromQuery] int? sellerId = null,
        [FromQuery] int[]? categoryIds = null,
        [FromQuery] string[]? origins = null,
        [FromQuery] string[]? standards = null,
        [FromQuery] string[]? units = null)
    {
        var client = _httpClientFactory.CreateClient("Catalog");
        AttachAccessToken(client);

        var query = new List<string>();
        if (!string.IsNullOrWhiteSpace(name))
        {
            query.Add($"name={Uri.EscapeDataString(name)}");
        }

        if (sellerId.HasValue && sellerId.Value > 0)
        {
            query.Add($"sellerId={sellerId.Value}");
        }

        foreach (var categoryId in (categoryIds ?? Array.Empty<int>()).Where(id => id > 0).Distinct())
        {
            query.Add($"categoryIds={categoryId}");
        }

        foreach (var origin in (origins ?? Array.Empty<string>()).Where(value => !string.IsNullOrWhiteSpace(value)).Select(value => value.Trim()).Distinct(StringComparer.OrdinalIgnoreCase))
        {
            query.Add($"origins={Uri.EscapeDataString(origin)}");
        }

        foreach (var standard in (standards ?? Array.Empty<string>()).Where(value => !string.IsNullOrWhiteSpace(value)).Select(value => value.Trim()).Distinct(StringComparer.OrdinalIgnoreCase))
        {
            query.Add($"standards={Uri.EscapeDataString(standard)}");
        }

        foreach (var unit in (units ?? Array.Empty<string>()).Where(value => !string.IsNullOrWhiteSpace(value)).Select(value => value.Trim()).Distinct(StringComparer.OrdinalIgnoreCase))
        {
            query.Add($"units={Uri.EscapeDataString(unit)}");
        }

        var url = query.Count == 0 ? "/api/products" : "/api/products?" + string.Join("&", query);

        var resp = await client.GetAsync(url);
        var content = await resp.Content.ReadAsStringAsync();

        return new ContentResult
        {
            StatusCode = (int)resp.StatusCode,
            Content = content,
            ContentType = "application/json"
        };
    }

    [HttpGet("product-search")]
    public async Task<IActionResult> SearchProducts(
        [FromQuery] string? name,
        [FromQuery] int[]? categoryIds = null,
        [FromQuery] int[]? sellerIds = null,
        [FromQuery] string[]? availability = null,
        [FromQuery] string[]? deliveryScopes = null,
        [FromQuery] string[]? origins = null,
        [FromQuery] string[]? standards = null,
        [FromQuery] string[]? units = null,
        [FromQuery] decimal? minPrice = null,
        [FromQuery] decimal? maxPrice = null,
        [FromQuery] decimal? minRating = null,
        [FromQuery] string? preset = null,
        [FromQuery] string? sort = null,
        [FromQuery] int page = 1,
        [FromQuery] int pageSize = 12)
    {
        var client = _httpClientFactory.CreateClient("Catalog");
        AttachAccessToken(client);

        var query = new List<string>();
        if (!string.IsNullOrWhiteSpace(name))
        {
            query.Add($"name={Uri.EscapeDataString(name)}");
        }

        foreach (var categoryId in (categoryIds ?? Array.Empty<int>()).Where(id => id > 0).Distinct())
        {
            query.Add($"categoryIds={categoryId}");
        }

        foreach (var origin in (origins ?? Array.Empty<string>()).Where(value => !string.IsNullOrWhiteSpace(value)).Select(value => value.Trim()).Distinct(StringComparer.OrdinalIgnoreCase))
        {
            query.Add($"origins={Uri.EscapeDataString(origin)}");
        }

        foreach (var standard in (standards ?? Array.Empty<string>()).Where(value => !string.IsNullOrWhiteSpace(value)).Select(value => value.Trim()).Distinct(StringComparer.OrdinalIgnoreCase))
        {
            query.Add($"standards={Uri.EscapeDataString(standard)}");
        }

        foreach (var unit in (units ?? Array.Empty<string>()).Where(value => !string.IsNullOrWhiteSpace(value)).Select(value => value.Trim()).Distinct(StringComparer.OrdinalIgnoreCase))
        {
            query.Add($"units={Uri.EscapeDataString(unit)}");
        }

        var url = query.Count == 0 ? "/api/products" : "/api/products?" + string.Join("&", query);
        var response = await client.GetAsync(url);
        var content = await response.Content.ReadAsStringAsync();
        if (!response.IsSuccessStatusCode)
        {
            return new ContentResult
            {
                StatusCode = (int)response.StatusCode,
                Content = content,
                ContentType = "application/json"
            };
        }

        var items = JsonSerializer.Deserialize<List<ProductSearchApiDto>>(content, JsonOptions) ?? new List<ProductSearchApiDto>();
        IEnumerable<ProductSearchApiDto> filtered = items;
        var normalizedSellerIds = (sellerIds ?? Array.Empty<int>()).Where(id => id > 0).Distinct().ToHashSet();
        var normalizedAvailability = (availability ?? Array.Empty<string>())
            .Where(value => !string.IsNullOrWhiteSpace(value))
            .Select(value => value.Trim().ToLowerInvariant())
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
        var normalizedDeliveryScopes = (deliveryScopes ?? Array.Empty<string>())
            .Where(value => !string.IsNullOrWhiteSpace(value))
            .Select(value => value.Trim().ToLowerInvariant())
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
        var normalizedPreset = (preset ?? string.Empty).Trim().ToLowerInvariant();

        if (minPrice.HasValue)
        {
            filtered = filtered.Where(item => item.Price >= minPrice.Value);
        }

        if (maxPrice.HasValue)
        {
            filtered = filtered.Where(item => item.Price <= maxPrice.Value);
        }

        if (minRating.HasValue && minRating.Value > 0)
        {
            filtered = filtered.Where(item => item.AverageRating >= minRating.Value);
        }

        if (string.Equals(normalizedPreset, "fresh-pick", StringComparison.Ordinal))
        {
            filtered = filtered.Where(HasFreshnessSignals);
        }

        if (string.Equals(normalizedPreset, "seasonal-pick", StringComparison.Ordinal))
        {
            filtered = filtered.Where(IsSeasonalProduct);
        }

        var facetedList = filtered.ToList();
        if (normalizedAvailability.Count > 0)
        {
            filtered = facetedList.Where(item => normalizedAvailability.Contains(GetAvailabilityStatus(item.AvailableStock)));
            facetedList = filtered.ToList();
        }
        else if (string.Equals(normalizedPreset, "ready-today", StringComparison.Ordinal))
        {
            filtered = facetedList.Where(item => string.Equals(GetAvailabilityStatus(item.AvailableStock), "in-stock", StringComparison.OrdinalIgnoreCase));
            facetedList = filtered.ToList();
        }

        var availableShops = await BuildAvailableShopsAsync(facetedList, normalizedSellerIds);
        if (string.Equals(normalizedPreset, "trusted-shop", StringComparison.Ordinal))
        {
            var trustedSellerIds = availableShops
                .Where(IsTrustedShop)
                .Select(shop => shop.SellerId)
                .ToHashSet();

            filtered = facetedList.Where(item => item.PrimarySellerId.HasValue && trustedSellerIds.Contains(item.PrimarySellerId.Value));
            facetedList = filtered.ToList();
            availableShops = availableShops.Where(IsTrustedShop).ToList();
        }

        if (normalizedDeliveryScopes.Count == 0)
        {
            if (string.Equals(normalizedPreset, "local-delivery", StringComparison.Ordinal))
            {
                normalizedDeliveryScopes = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
                {
                    "district-level",
                    "ward-level"
                };
            }
            else if (string.Equals(normalizedPreset, "same-day-urban", StringComparison.Ordinal))
            {
                normalizedDeliveryScopes = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
                {
                    "ward-level"
                };
            }
        }

        if (normalizedDeliveryScopes.Count > 0)
        {
            var sellerScopeMap = availableShops.ToDictionary(
                shop => shop.SellerId,
                shop => GetDeliveryScopeKey(shop.AddressSummary));

            filtered = facetedList.Where(item =>
                item.PrimarySellerId.HasValue
                && sellerScopeMap.TryGetValue(item.PrimarySellerId.Value, out var scopeKey)
                && normalizedDeliveryScopes.Contains(scopeKey));
            facetedList = filtered.ToList();
            availableShops = availableShops
                .Where(shop => normalizedDeliveryScopes.Contains(GetDeliveryScopeKey(shop.AddressSummary)))
                .ToList();
        }

        if (normalizedSellerIds.Count > 0)
        {
            filtered = facetedList.Where(item => item.PrimarySellerId.HasValue && normalizedSellerIds.Contains(item.PrimarySellerId.Value));
        }

        var normalizedKeyword = (name ?? string.Empty).Trim();
        var effectiveSort = string.IsNullOrWhiteSpace(sort)
            ? normalizedPreset switch
            {
                "newest" => "newest",
                "bestseller" => "bestseller",
                "budget" => "price-asc",
                "fresh-pick" => "newest",
                "seasonal-pick" => "related",
                _ => sort
            }
            : sort;
        filtered = ApplySort(filtered, effectiveSort, normalizedKeyword);

        var filteredList = filtered.ToList();
        var totalCount = filteredList.Count;
        var safePageSize = Math.Clamp(pageSize, 1, 48);
        var totalPages = Math.Max(1, (int)Math.Ceiling(totalCount / (double)safePageSize));
        var safePage = Math.Clamp(page, 1, totalPages);
        var pagedItems = filteredList
            .Skip((safePage - 1) * safePageSize)
            .Take(safePageSize)
            .ToList();

        var availableLocations = filteredList
            .Select(item => item.Origin?.Trim())
            .Where(value => !string.IsNullOrWhiteSpace(value))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderBy(value => value, StringComparer.OrdinalIgnoreCase)
            .ToList();

        var availableUnits = filteredList
            .Select(item => item.UnitName?.Trim())
            .Where(value => !string.IsNullOrWhiteSpace(value))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderBy(value => value, StringComparer.OrdinalIgnoreCase)
            .ToList();

        var availableStandards = filteredList
            .Select(item => item.Standard?.Trim())
            .Where(value => !string.IsNullOrWhiteSpace(value))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderBy(value => value, StringComparer.OrdinalIgnoreCase)
            .ToList();

        var availableAvailability = BuildAvailabilityFacet(facetedList, normalizedAvailability);
        var availableDeliveryScopes = BuildDeliveryScopeFacet(facetedList, availableShops, normalizedDeliveryScopes);
        var relatedItems = filteredList.Take(4).ToList();

        return Ok(new
        {
            items = pagedItems,
            totalCount,
            page = safePage,
            pageSize = safePageSize,
            totalPages,
            availableLocations,
            availableUnits,
            availableStandards,
            availableShops,
            availableAvailability,
            availableDeliveryScopes,
            relatedItems
        });
    }

    [HttpGet("categories")]
    public async Task<IActionResult> GetCategories([FromQuery] string? search = null)
    {
        var client = _httpClientFactory.CreateClient("Catalog");

        var url = string.IsNullOrWhiteSpace(search)
            ? "/api/categories/public"
            : $"/api/categories/public?search={Uri.EscapeDataString(search)}";

        var response = await client.GetAsync(url);
        var content = await response.Content.ReadAsStringAsync();

        return new ContentResult
        {
            StatusCode = (int)response.StatusCode,
            Content = content,
            ContentType = "application/json"
        };
    }

    [HttpGet("products/{id:int}")]
    public async Task<IActionResult> GetProductById([FromRoute] int id)
    {
        var client = _httpClientFactory.CreateClient("Catalog");
        AttachAccessToken(client);

        var resp = await client.GetAsync($"/api/products/{id}");
        var content = await resp.Content.ReadAsStringAsync();

        return new ContentResult
        {
            StatusCode = (int)resp.StatusCode,
            Content = content,
            ContentType = "application/json"
        };
    }

    [HttpGet("products/{id:int}/offers")]
    public async Task<IActionResult> GetProductOffers([FromRoute] int id)
    {
        var catalogClient = _httpClientFactory.CreateClient("Catalog");
        AttachAccessToken(catalogClient);

        var catalogResponse = await catalogClient.GetAsync($"/api/products/{id}/offers");
        var catalogContent = await catalogResponse.Content.ReadAsStringAsync();
        if (!catalogResponse.IsSuccessStatusCode)
        {
            return new ContentResult
            {
                StatusCode = (int)catalogResponse.StatusCode,
                Content = catalogContent,
                ContentType = "application/json"
            };
        }

        var offers = JsonSerializer.Deserialize<List<ProductOfferApiDto>>(catalogContent, JsonOptions) ?? new List<ProductOfferApiDto>();
        if (offers.Count == 0)
        {
            return Ok(Array.Empty<object>());
        }

        var sellerIds = offers
            .Select(x => x.SellerId)
            .Where(x => x > 0)
            .Distinct()
            .ToList();

        List<PublicMerchantApiDto> merchants = new();
        if (sellerIds.Count > 0)
        {
            var identityClient = _httpClientFactory.CreateClient("Identity");
            var merchantQuery = string.Join("&", sellerIds.Select(idValue => $"sellerIds={idValue}"));
            var merchantResponse = await identityClient.GetAsync($"/auth/public/merchants?{merchantQuery}");
            if (merchantResponse.IsSuccessStatusCode)
            {
                var merchantContent = await merchantResponse.Content.ReadAsStringAsync();
                merchants = JsonSerializer.Deserialize<List<PublicMerchantApiDto>>(merchantContent, JsonOptions) ?? new List<PublicMerchantApiDto>();
            }
        }

        var merchantBySellerId = merchants.ToDictionary(x => x.SellerId);
        var result = offers.Select(offer =>
        {
            merchantBySellerId.TryGetValue(offer.SellerId, out var merchant);
            return new
            {
                offer.SellerId,
                offer.ProductId,
                offer.ActiveSinceUtc,
                ShopName = merchant?.ShopName ?? $"FreshFarm Seller {offer.SellerId}",
                UserName = merchant?.UserName ?? string.Empty,
                Avatar = merchant?.Avatar,
                AddressSummary = merchant?.AddressSummary ?? "Chưa cập nhật địa chỉ hoạt động",
                JoinedAt = merchant?.JoinedAt
            };
        });

        return Ok(result);
    }

    [HttpGet("shops")]
    public async Task<IActionResult> GetShops([FromQuery] string? q = null, [FromQuery] int page = 1, [FromQuery] int pageSize = 24)
    {
        var client = _httpClientFactory.CreateClient("Identity");
        var query = new List<string>();
        if (!string.IsNullOrWhiteSpace(q))
        {
            query.Add($"q={Uri.EscapeDataString(q)}");
        }

        query.Add($"page={Math.Max(page, 1)}");
        query.Add($"pageSize={pageSize}");

        var url = "/auth/public/merchants?" + string.Join("&", query);
        var response = await client.GetAsync(url);
        var content = await response.Content.ReadAsStringAsync();

        return new ContentResult
        {
            StatusCode = (int)response.StatusCode,
            Content = content,
            ContentType = "application/json"
        };
    }

    [HttpGet("shops/{sellerId:int}")]
    public async Task<IActionResult> GetShopById([FromRoute] int sellerId)
    {
        var client = _httpClientFactory.CreateClient("Identity");
        var response = await client.GetAsync($"/auth/public/merchants/{sellerId}");
        var content = await response.Content.ReadAsStringAsync();

        return new ContentResult
        {
            StatusCode = (int)response.StatusCode,
            Content = content,
            ContentType = "application/json"
        };
    }

    [HttpPost("products")]
    public async Task<IActionResult> CreateProduct([FromBody] object request)
    {
        var client = _httpClientFactory.CreateClient("Catalog");
        AttachAccessToken(client);

        var resp = await client.PostAsJsonAsync("/api/products", request);
        var body = await resp.Content.ReadAsStringAsync();

        return new ContentResult
        {
            StatusCode = (int)resp.StatusCode,
            Content = body,
            ContentType = "application/json"
        };
    }

    private sealed class ProductOfferApiDto
    {
        public int SellerId { get; set; }
        public int ProductId { get; set; }
        public DateTime ActiveSinceUtc { get; set; }
    }

    private sealed class PublicMerchantApiDto
    {
        public int SellerId { get; set; }
        public string? ShopName { get; set; }
        public string? UserName { get; set; }
        public string? Avatar { get; set; }
        public string? AddressSummary { get; set; }
        public DateTime? JoinedAt { get; set; }
    }

    private sealed class PublicMerchantListApiDto
    {
        public List<PublicMerchantApiDto>? Merchants { get; set; }
    }

    private async Task<List<AvailableShopFacetDto>> BuildAvailableShopsAsync(List<ProductSearchApiDto> items, HashSet<int> selectedSellerIds)
    {
        var sellerSummaries = items
            .Where(item => item.PrimarySellerId.HasValue && item.PrimarySellerId.Value > 0)
            .GroupBy(item => item.PrimarySellerId!.Value)
            .Select(group => new
            {
                SellerId = group.Key,
                ProductCount = group.Select(item => item.ProductId).Distinct().Count()
            })
            .OrderByDescending(group => group.ProductCount)
            .ThenBy(group => group.SellerId)
            .ToList();

        if (sellerSummaries.Count == 0)
        {
            return new List<AvailableShopFacetDto>();
        }

        var identityClient = _httpClientFactory.CreateClient("Identity");
        var merchantQuery = string.Join("&", sellerSummaries.Select(summary => $"sellerIds={summary.SellerId}"));
        var merchantResponse = await identityClient.GetAsync($"/auth/public/merchants?{merchantQuery}");
        List<PublicMerchantApiDto> merchants = new();
        if (merchantResponse.IsSuccessStatusCode)
        {
            var merchantContent = await merchantResponse.Content.ReadAsStringAsync();
            var merchantPayload = JsonSerializer.Deserialize<PublicMerchantListApiDto>(merchantContent, JsonOptions);
            merchants = merchantPayload?.Merchants ?? new List<PublicMerchantApiDto>();
        }

        var merchantBySellerId = merchants.ToDictionary(merchant => merchant.SellerId);
        return sellerSummaries
            .Select(summary =>
            {
                merchantBySellerId.TryGetValue(summary.SellerId, out var merchant);
                return new AvailableShopFacetDto
                {
                    SellerId = summary.SellerId,
                    ShopName = merchant?.ShopName ?? merchant?.UserName ?? $"FreshFarm Seller {summary.SellerId}",
                    Avatar = merchant?.Avatar,
                    AddressSummary = merchant?.AddressSummary ?? "Chưa cập nhật địa chỉ hoạt động",
                    JoinedAt = merchant?.JoinedAt,
                    ProductCount = summary.ProductCount,
                    IsSelected = selectedSellerIds.Contains(summary.SellerId)
                };
            })
            .ToList();
    }

    private static bool IsTrustedShop(AvailableShopFacetDto shop)
    {
        var joinedAt = shop.JoinedAt;
        var daysActive = joinedAt.HasValue
            ? (DateTime.UtcNow.Date - joinedAt.Value.Date).TotalDays
            : (double?)null;
        var hasAddress = !string.IsNullOrWhiteSpace(shop.AddressSummary)
            && !shop.AddressSummary.StartsWith("Chưa cập nhật", StringComparison.OrdinalIgnoreCase);

        return (daysActive.HasValue && daysActive.Value >= 180)
            || shop.ProductCount >= 4
            || hasAddress;
    }

    private static List<object> BuildAvailabilityFacet(List<ProductSearchApiDto> items, HashSet<string> selectedAvailability)
    {
        var labels = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            ["in-stock"] = "Còn hàng",
            ["low-stock"] = "Sắp hết",
            ["out-of-stock"] = "Hết hàng"
        };

        return new[] { "in-stock", "low-stock", "out-of-stock" }
            .Select(key => new
            {
                key,
                label = labels[key],
                count = items.Count(item => string.Equals(GetAvailabilityStatus(item.AvailableStock), key, StringComparison.OrdinalIgnoreCase)),
                isSelected = selectedAvailability.Contains(key)
            })
            .Where(item => item.count > 0)
            .Cast<object>()
            .ToList();
    }

    private static List<object> BuildDeliveryScopeFacet(
        List<ProductSearchApiDto> items,
        List<AvailableShopFacetDto> shops,
        HashSet<string> selectedDeliveryScopes)
    {
        var shopScopeMap = shops.ToDictionary(shop => shop.SellerId, shop => GetDeliveryScopeKey(shop.AddressSummary));
        var labels = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            ["province-level"] = "Tỉnh/thành rõ",
            ["district-level"] = "Tới quận/huyện",
            ["ward-level"] = "Tới xã/phường"
        };

        return new[] { "province-level", "district-level", "ward-level" }
            .Select(key => new
            {
                key,
                label = labels[key],
                count = items.Count(item =>
                    item.PrimarySellerId.HasValue
                    && shopScopeMap.TryGetValue(item.PrimarySellerId.Value, out var scopeKey)
                    && string.Equals(scopeKey, key, StringComparison.OrdinalIgnoreCase)),
                isSelected = selectedDeliveryScopes.Contains(key)
            })
            .Where(item => item.count > 0)
            .Cast<object>()
            .ToList();
    }

    private sealed class ProductSearchApiDto
    {
        public int ProductId { get; set; }
        public string? ProductName { get; set; }
        public string? CategoryName { get; set; }
        public int CategoryId { get; set; }
        public string? UnitName { get; set; }
        public decimal Price { get; set; }
        public decimal AverageRating { get; set; }
        public int SoldCount { get; set; }
        public int StockQuantity { get; set; }
        public int AvailableStock { get; set; }
        public int OnHandStock { get; set; }
        public int ReservedStock { get; set; }
        public string? Origin { get; set; }
        public string? Standard { get; set; }
        public string? Preservation { get; set; }
        public string? Weight { get; set; }
        public string? ImageFileName { get; set; }
        public int? PrimarySellerId { get; set; }
    }

    private sealed class AvailableShopFacetDto
    {
        public int SellerId { get; set; }
        public string? ShopName { get; set; }
        public string? Avatar { get; set; }
        public string? AddressSummary { get; set; }
        public DateTime? JoinedAt { get; set; }
        public int ProductCount { get; set; }
        public bool IsSelected { get; set; }
    }

    private static string GetAvailabilityStatus(int stockQuantity)
    {
        if (stockQuantity <= 0)
        {
            return "out-of-stock";
        }

        if (stockQuantity <= 10)
        {
            return "low-stock";
        }

        return "in-stock";
    }

    private static bool HasFreshnessSignals(ProductSearchApiDto item)
    {
        var preservation = (item.Preservation ?? string.Empty).Trim();
        var origin = (item.Origin ?? string.Empty).Trim();
        var weight = (item.Weight ?? string.Empty).Trim();
        return !string.IsNullOrWhiteSpace(origin)
            || !string.IsNullOrWhiteSpace(preservation)
            || !string.IsNullOrWhiteSpace(weight);
    }

    private static bool IsSeasonalProduct(ProductSearchApiDto item)
    {
        var month = DateTime.UtcNow.AddHours(7).Month;
        var seasonalKeywords = month switch
        {
            1 or 2 => new[] { "cam", "quyt", "buoi", "bap cai", "sup lo", "ca rot", "khoai tay" },
            3 or 4 => new[] { "xoai", "dua hau", "dua luoi", "bi do", "mang tay", "dua leo" },
            5 or 6 => new[] { "vai", "man", "dao", "xoai", "chom chom", "dua hau" },
            7 or 8 => new[] { "nhan", "chom chom", "sau rieng", "mit", "ngo", "dua leo" },
            9 or 10 => new[] { "buoi", "oi", "tao", "bi do", "khoai lang", "nam" },
            _ => new[] { "cam", "quyt", "buoi", "hong", "su hao", "ca rot", "rau la" }
        };

        var haystack = string.Join(
            " ",
            item.ProductName ?? string.Empty,
            item.CategoryName ?? string.Empty,
            item.Origin ?? string.Empty);
        var normalizedHaystack = NormalizeText(haystack);

        return seasonalKeywords.Any(keyword => normalizedHaystack.Contains(keyword, StringComparison.Ordinal));
    }

    private static string GetDeliveryScopeKey(string? addressSummary)
    {
        var normalized = NormalizeText(addressSummary);
        if (string.IsNullOrWhiteSpace(normalized) || normalized.StartsWith("chua cap nhat", StringComparison.Ordinal))
        {
            return string.Empty;
        }

        if (ContainsAny(normalized, "phuong", "xa ", " xa", "thi tran", "ap ", "thon ", "xom ", "khu pho", "to dan pho"))
        {
            return "ward-level";
        }

        if (ContainsAny(normalized, "quan ", "huyen", "thi xa"))
        {
            return "district-level";
        }

        if (ContainsAny(normalized, "tinh", "thanh pho", "tp ", "tp."))
        {
            return "province-level";
        }

        return string.Empty;
    }

    private static bool ContainsAny(string text, params string[] patterns)
    {
        return patterns.Any(pattern => text.Contains(pattern, StringComparison.Ordinal));
    }

    private static string NormalizeText(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return string.Empty;
        }

        var normalized = value.Trim().ToLowerInvariant().Normalize(NormalizationForm.FormD);
        var builder = new StringBuilder(normalized.Length);
        foreach (var ch in normalized)
        {
            if (CharUnicodeInfo.GetUnicodeCategory(ch) != UnicodeCategory.NonSpacingMark)
            {
                builder.Append(ch);
            }
        }

        return builder.ToString().Normalize(NormalizationForm.FormC);
    }

    private static IEnumerable<ProductSearchApiDto> ApplySort(IEnumerable<ProductSearchApiDto> items, string? sort, string keyword)
    {
        var normalizedSort = (sort ?? "related").Trim().ToLowerInvariant();
        return normalizedSort switch
        {
            "newest" => items.OrderByDescending(item => item.ProductId),
            "bestseller" => items.OrderByDescending(item => item.SoldCount).ThenByDescending(item => item.ProductId),
            "price-asc" => items.OrderBy(item => item.Price).ThenByDescending(item => item.ProductId),
            "price-desc" => items.OrderByDescending(item => item.Price).ThenByDescending(item => item.ProductId),
            _ => items
                .OrderByDescending(item => CalculateRelevanceScore(item, keyword))
                .ThenByDescending(item => item.ProductId)
        };
    }

    private static decimal CalculateRelevanceScore(ProductSearchApiDto item, string keyword)
    {
        if (string.IsNullOrWhiteSpace(keyword))
        {
            return 0;
        }

        var normalizedKeyword = keyword.Trim().ToLowerInvariant();
        var productName = (item.ProductName ?? string.Empty).ToLowerInvariant();
        var categoryName = (item.CategoryName ?? string.Empty).ToLowerInvariant();
        decimal score = 0;

        if (productName.StartsWith(normalizedKeyword, StringComparison.Ordinal))
        {
            score += 120;
        }

        if (productName.Contains(normalizedKeyword, StringComparison.Ordinal))
        {
            score += 60;
        }

        if (categoryName.Contains(normalizedKeyword, StringComparison.Ordinal))
        {
            score += 28;
        }

        score += item.AverageRating * 3;
        score += item.SoldCount / 1000m;
        return score;
    }
}
