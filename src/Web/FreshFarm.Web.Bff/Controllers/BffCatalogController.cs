using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using System.Globalization;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace FreshFarm.Web.Bff.Controllers;

[ApiController]
[Route("bff")]
public sealed class BffCatalogController : ControllerBase
{
    private const string AccessTokenSessionKey = "ACCESS_TOKEN";
    private const string RecommendationSessionMarkerKey = "__recommendation_session_initialized";
    private const string HomeRecommendationPlacement = "home_today";
    private const string HomeRecommendationAlgorithm = "content_based_home_v1";
    private const string HybridHomeRecommendationAlgorithm = "hybrid_home_v1";
    private const string SimilarRecommendationPlacement = "product_similar";
    private const string SimilarRecommendationAlgorithm = "content_based_similar_v1";
    private const string HybridSimilarRecommendationAlgorithm = "hybrid_similar_v1";
    private const string HybridSearchRankingAlgorithm = "hybrid_search_v1";
    private const string KeywordSearchRankingAlgorithm = "keyword_relevance_v1";
    private const string HomeContentSignalSource = "catalog_content_v1";
    private const string SimilarContentSignalSource = "catalog_content_v1";
    private const string SearchContentSignalSource = "catalog_keyword_v1";
    private const string RecommendationSignalSourceHeader = "X-Recommendation-Signal-Source";
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
    [EnableRateLimiting("public-read")]
    public async Task<IActionResult> GetProducts(
        [FromQuery] string? name,
        [FromQuery] int? sellerId = null,
        [FromQuery] int[]? categoryIds = null,
        [FromQuery] string[]? origins = null,
        [FromQuery] string[]? standards = null,
        [FromQuery] string[]? units = null)
    {
        var loadResult = await LoadCatalogProductsAsync(
            name,
            sellerId,
            categoryIds,
            origins,
            standards,
            units,
            "Chua ket noi duoc dich vu san pham. Vui long thu lai sau.");

        return loadResult.ErrorResult ?? CreateJsonContentResult(loadResult.Items);
    }

    [HttpGet("product-search")]
    [EnableRateLimiting("search-read")]
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
        HttpResponseMessage response;
        try
        {
            response = await client.GetAsync(url);
        }
        catch (HttpRequestException)
        {
            return CreateUpstreamUnavailableResult("Catalog", "Chua ket noi duoc dich vu tim kiem san pham. Vui long thu lai sau.");
        }

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
        foreach (var item in items)
        {
            NormalizeCatalogProductAttributes(item.ProductAttributes);
        }
        var productStatsById = await GetOrderingProductStatsAsync(items.Select(item => item.ProductId));
        ApplyProductStats(items, productStatsById);
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
        var searchRankingResult = IsHybridSearchSort(effectiveSort, normalizedKeyword)
            ? await GetOrderingSearchRankingSignalsAsync(normalizedKeyword, facetedList.Select(item => item.ProductId))
            : new OrderingSearchRankingSignalResult();
        var searchRankingSignals = searchRankingResult.Signals;
        var rankingAlgorithm = IsHybridSearchSort(effectiveSort, normalizedKeyword)
            ? (searchRankingSignals.Count > 0 ? HybridSearchRankingAlgorithm : KeywordSearchRankingAlgorithm)
            : null;
        var rankingSignalSource = searchRankingSignals.Count > 0 ? searchRankingResult.SignalSource : null;
        filtered = ApplySort(filtered, effectiveSort, normalizedKeyword, searchRankingSignals);

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
            relatedItems,
            rankingAlgorithm,
            rankingSignalSource,
            contentSignalSource = SearchContentSignalSource
        });
    }

    [HttpGet("categories")]
    [EnableRateLimiting("public-read")]
    public async Task<IActionResult> GetCategories([FromQuery] string? search = null)
    {
        var client = _httpClientFactory.CreateClient("Catalog");

        var url = string.IsNullOrWhiteSpace(search)
            ? "/api/categories/public"
            : $"/api/categories/public?search={Uri.EscapeDataString(search)}";

        HttpResponseMessage response;
        try
        {
            response = await client.GetAsync(url);
        }
        catch (HttpRequestException)
        {
            return CreateUpstreamUnavailableResult("Catalog", "Chua ket noi duoc dich vu danh muc. Vui long thu lai sau.");
        }

        var content = await response.Content.ReadAsStringAsync();

        return new ContentResult
        {
            StatusCode = (int)response.StatusCode,
            Content = content,
            ContentType = "application/json"
        };
    }

    [HttpGet("products/{id:int}")]
    [EnableRateLimiting("public-read")]
    public async Task<IActionResult> GetProductById([FromRoute] int id)
    {
        var loadResult = await LoadCatalogProductByIdAsync(id, "Chua ket noi duoc dich vu chi tiet san pham. Vui long thu lai sau.");
        if (loadResult.ErrorResult is not null)
        {
            return loadResult.ErrorResult;
        }

        if (loadResult.Product is null)
        {
            return CreateJsonContentResult(new { message = "Không tìm thấy sản phẩm." }, StatusCodes.Status404NotFound);
        }

        return CreateJsonContentResult(loadResult.Product);
    }

    [HttpGet("recommendations/home")]
    [EnableRateLimiting("public-read")]
    public async Task<IActionResult> GetHomeRecommendations([FromQuery] int limit = 12)
    {
        var loadResult = await LoadCatalogProductsAsync(
            name: null,
            sellerId: null,
            categoryIds: null,
            origins: null,
            standards: null,
            units: null,
            "Chua ket noi duoc dich vu goi y san pham. Vui long thu lai sau.");

        if (loadResult.ErrorResult is not null)
        {
            return loadResult.ErrorResult;
        }

        var normalizedLimit = Math.Clamp(limit, 1, 24);
        var stableSessionId = await EnsureStableRecommendationSessionIdAsync();
        var preferenceSeedResult = await GetOrderingHomePreferenceSeedsAsync(stableSessionId, 12);
        var collaborativeCandidateResult = await GetOrderingHomeCollaborativeCandidatesAsync(stableSessionId, 24);
        var preferenceSeeds = preferenceSeedResult.Seeds;
        var collaborativeCandidates = collaborativeCandidateResult.Candidates;
        var rankedItems = BuildHomeRecommendations(loadResult.Items, normalizedLimit, preferenceSeeds, collaborativeCandidates);
        return Ok(new RecommendationCollectionApiDto
        {
            Placement = HomeRecommendationPlacement,
            Algorithm = preferenceSeeds.Count > 0 || collaborativeCandidates.Count > 0
                ? HybridHomeRecommendationAlgorithm
                : HomeRecommendationAlgorithm,
            ContentSignalSource = HomeContentSignalSource,
            SignalSource = BuildRecommendationSignalSource(preferenceSeedResult.SignalSource, collaborativeCandidateResult.SignalSource),
            PreferenceSignalSource = preferenceSeedResult.SignalSource,
            CollaborativeSignalSource = collaborativeCandidates.Count > 0 ? collaborativeCandidateResult.SignalSource : null,
            GeneratedAtUtc = DateTime.UtcNow,
            Items = rankedItems
        });
    }

    private async Task<string> EnsureStableRecommendationSessionIdAsync()
    {
        await HttpContext.Session.LoadAsync();
        if (!HttpContext.Session.TryGetValue(RecommendationSessionMarkerKey, out _))
        {
            HttpContext.Session.SetString(RecommendationSessionMarkerKey, "1");
        }

        return HttpContext.Session.Id;
    }

    [HttpGet("recommendations/products/{id:int}/similar")]
    [EnableRateLimiting("public-read")]
    public async Task<IActionResult> GetSimilarProducts([FromRoute] int id, [FromQuery] int limit = 8)
    {
        var seedResult = await LoadCatalogProductByIdAsync(id, "Chua ket noi duoc dich vu goi y san pham tuong tu. Vui long thu lai sau.");
        if (seedResult.ErrorResult is not null)
        {
            return seedResult.ErrorResult;
        }

        if (seedResult.Product is null)
        {
            return CreateJsonContentResult(new { message = "Không tìm thấy sản phẩm gốc để gợi ý." }, StatusCodes.Status404NotFound);
        }

        var seedProduct = seedResult.Product;
        var categoryScopedResult = await LoadCatalogProductsAsync(
            name: null,
            sellerId: null,
            categoryIds: seedProduct.CategoryId > 0 ? [seedProduct.CategoryId] : null,
            origins: null,
            standards: null,
            units: null,
            "Chua ket noi duoc dich vu goi y san pham tuong tu. Vui long thu lai sau.");

        if (categoryScopedResult.ErrorResult is not null)
        {
            return categoryScopedResult.ErrorResult;
        }

        var similarSignalResult = await GetOrderingSimilarProductSignalsAsync(
            seedProduct.ProductId,
            categoryScopedResult.Items.Select(item => item.ProductId));
        var collaborativeSignals = similarSignalResult.Signals;
        var similarItems = BuildSimilarRecommendations(
            seedProduct,
            categoryScopedResult.Items,
            Math.Clamp(limit, 1, 16),
            collaborativeSignals);
        return Ok(new RecommendationCollectionApiDto
        {
            Placement = SimilarRecommendationPlacement,
            Algorithm = collaborativeSignals.Count > 0
                ? HybridSimilarRecommendationAlgorithm
                : SimilarRecommendationAlgorithm,
            ContentSignalSource = SimilarContentSignalSource,
            SignalSource = collaborativeSignals.Count > 0 ? similarSignalResult.SignalSource : null,
            CollaborativeSignalSource = collaborativeSignals.Count > 0 ? similarSignalResult.SignalSource : null,
            GeneratedAtUtc = DateTime.UtcNow,
            SeedProductId = seedProduct.ProductId,
            Items = similarItems
        });
    }

    [HttpGet("products/{id:int}/offers")]
    [EnableRateLimiting("public-read")]
    public async Task<IActionResult> GetProductOffers([FromRoute] int id)
    {
        var catalogClient = _httpClientFactory.CreateClient("Catalog");
        AttachAccessToken(catalogClient);

        HttpResponseMessage catalogResponse;
        try
        {
            catalogResponse = await catalogClient.GetAsync($"/api/products/{id}/offers");
        }
        catch (HttpRequestException)
        {
            return CreateUpstreamUnavailableResult("Catalog", "Chua ket noi duoc dich vu nguoi ban cua san pham. Vui long thu lai sau.");
        }

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
            try
            {
                var merchantResponse = await identityClient.GetAsync($"/auth/public/merchants?{merchantQuery}");
                if (merchantResponse.IsSuccessStatusCode)
                {
                    var merchantContent = await merchantResponse.Content.ReadAsStringAsync();
                    merchants = JsonSerializer.Deserialize<List<PublicMerchantApiDto>>(merchantContent, JsonOptions) ?? new List<PublicMerchantApiDto>();
                }
            }
            catch (HttpRequestException)
            {
                // Keep offers visible even when the merchant profile endpoint is temporarily unavailable.
            }
        }

        var merchantBySellerId = BuildMerchantLookup(merchants);
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
    [EnableRateLimiting("public-read")]
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
        HttpResponseMessage response;
        try
        {
            response = await client.GetAsync(url);
        }
        catch (HttpRequestException)
        {
            return CreateUpstreamUnavailableResult("Identity", "Chua ket noi duoc dich vu cua hang. Vui long thu lai sau.");
        }

        var content = await response.Content.ReadAsStringAsync();

        return new ContentResult
        {
            StatusCode = (int)response.StatusCode,
            Content = content,
            ContentType = "application/json"
        };
    }

    [HttpGet("shops/{sellerId:int}")]
    [EnableRateLimiting("public-read")]
    public async Task<IActionResult> GetShopById([FromRoute] int sellerId)
    {
        var client = _httpClientFactory.CreateClient("Identity");
        HttpResponseMessage response;
        try
        {
            response = await client.GetAsync($"/auth/public/merchants/{sellerId}");
        }
        catch (HttpRequestException)
        {
            return CreateUpstreamUnavailableResult("Identity", "Chua ket noi duoc dich vu thong tin cua hang. Vui long thu lai sau.");
        }

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

        HttpResponseMessage resp;
        try
        {
            resp = await client.PostAsJsonAsync("/api/products", request);
        }
        catch (HttpRequestException)
        {
            return CreateUpstreamUnavailableResult("Catalog", "Chua ket noi duoc dich vu tao san pham. Vui long thu lai sau.");
        }

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

    private sealed class CatalogProductApiDto
    {
        public int ProductId { get; set; }
        public string? ProductName { get; set; }
        public string? Sku { get; set; }
        public decimal Price { get; set; }
        public bool Status { get; set; }
        public int StockQuantity { get; set; }
        public int ReservedStock { get; set; }
        public int AvailableStock { get; set; }
        public int OnHandStock { get; set; }
        public string? ImageFileName { get; set; }
        public DateTime CreatedDate { get; set; }
        public string? ShortDescription { get; set; }
        public string? LongDescription { get; set; }
        public bool IsManuallyDisabled { get; set; }
        public int CategoryId { get; set; }
        public string? CategoryName { get; set; }
        public int UnitId { get; set; }
        public string? UnitName { get; set; }
        public string? UnitSymbol { get; set; }
        public string? Origin { get; set; }
        public string? Standard { get; set; }
        public string? Preservation { get; set; }
        public string? Weight { get; set; }
        public int? PrimarySellerId { get; set; }
        public decimal AverageRating { get; set; }
        public int SoldCount { get; set; }
        public int ReviewCount { get; set; }
        public List<CatalogProductInfoApiDto>? ProductInfos { get; set; }
        public List<CatalogProductAttributeApiDto>? ProductAttributes { get; set; }
    }

    private sealed class CatalogProductInfoApiDto
    {
        public string? Weight { get; set; }
        public string? Origin { get; set; }
        public string? Standard { get; set; }
        public string? Preservation { get; set; }
    }

    private sealed class CatalogProductAttributeApiDto
    {
        public int CategoryAttributeId { get; set; }
        public string? AttributeKey { get; set; }
        public string? DisplayName { get; set; }
        public string? ValueText { get; set; }
        public string? NormalizedValue { get; set; }
    }

    private sealed class OrderingProductStatsApiDto
    {
        public int ProductId { get; set; }
        public decimal AverageRating { get; set; }
        public int ReviewCount { get; set; }
        public int SoldCount { get; set; }
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

    private sealed class CatalogProductsLoadResult
    {
        public List<CatalogProductApiDto> Items { get; init; } = new();
        public ContentResult? ErrorResult { get; init; }
    }

    private sealed class CatalogProductLoadResult
    {
        public CatalogProductApiDto? Product { get; init; }
        public ContentResult? ErrorResult { get; init; }
    }

    private sealed class RecommendationCollectionApiDto
    {
        public string Placement { get; set; } = string.Empty;
        public string Algorithm { get; set; } = string.Empty;
        public string? ContentSignalSource { get; set; }
        public string? SignalSource { get; set; }
        public string? PreferenceSignalSource { get; set; }
        public string? CollaborativeSignalSource { get; set; }
        public DateTime GeneratedAtUtc { get; set; }
        public int? SeedProductId { get; set; }
        public List<RecommendationProductApiDto> Items { get; set; } = new();
    }

    private sealed class RecommendationProductApiDto
    {
        public int ProductId { get; set; }
        public string? ProductName { get; set; }
        public decimal Price { get; set; }
        public string? CategoryName { get; set; }
        public string? UnitName { get; set; }
        public string? ImageFileName { get; set; }
        public string? Origin { get; set; }
        public string? Standard { get; set; }
        public string? Preservation { get; set; }
        public string? Weight { get; set; }
        public int? PrimarySellerId { get; set; }
        public decimal AverageRating { get; set; }
        public int SoldCount { get; set; }
        public int ReviewCount { get; set; }
        public int AvailableStock { get; set; }
        public double RecommendationScore { get; set; }
        public string RecommendationReason { get; set; } = string.Empty;
        public string[] RecommendationTags { get; set; } = Array.Empty<string>();
    }

    private sealed class OrderingHomePreferenceSeedApiDto
    {
        public int ProductId { get; set; }
        public int ViewCount { get; set; }
        public int SearchClickCount { get; set; }
        public int RecommendationClickCount { get; set; }
        public int PurchaseCount { get; set; }
        public double PreferenceScore { get; set; }
        public DateTime? LastInteractedAtUtc { get; set; }
    }

    private sealed class OrderingHomeCollaborativeCandidateApiDto
    {
        public int ProductId { get; set; }
        public int CoPurchaseOrderCount { get; set; }
        public int CoViewSessionCount { get; set; }
        public int CoClickSessionCount { get; set; }
        public double CollaborativeScore { get; set; }
    }

    private sealed class OrderingHomePreferenceSeedResult
    {
        public IReadOnlyDictionary<int, OrderingHomePreferenceSeedApiDto> Seeds { get; init; } = new Dictionary<int, OrderingHomePreferenceSeedApiDto>();
        public string? SignalSource { get; init; }
    }

    private sealed class OrderingHomeCollaborativeCandidateResult
    {
        public IReadOnlyDictionary<int, OrderingHomeCollaborativeCandidateApiDto> Candidates { get; init; } = new Dictionary<int, OrderingHomeCollaborativeCandidateApiDto>();
        public string? SignalSource { get; init; }
    }

    private sealed class OrderingSimilarProductSignalResult
    {
        public IReadOnlyDictionary<int, OrderingSimilarProductSignalApiDto> Signals { get; init; } = new Dictionary<int, OrderingSimilarProductSignalApiDto>();
        public string? SignalSource { get; init; }
    }

    private sealed class OrderingSearchRankingSignalResult
    {
        public IReadOnlyDictionary<int, OrderingSearchRankingSignalApiDto> Signals { get; init; } = new Dictionary<int, OrderingSearchRankingSignalApiDto>();
        public string? SignalSource { get; init; }
    }

    private sealed class HomePreferenceSeedContext
    {
        public CatalogProductApiDto? Product { get; set; }
        public OrderingHomePreferenceSeedApiDto Signal { get; set; } = new();
    }

    private sealed class HomePreferenceMatchDto
    {
        public int SeedProductId { get; set; }
        public CatalogProductApiDto? SeedProduct { get; set; }
        public OrderingHomePreferenceSeedApiDto Signal { get; set; } = new();
        public bool IsDirectMatch { get; set; }
        public double Score { get; set; }
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

        var merchantBySellerId = BuildMerchantLookup(merchants);
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

    private async Task<CatalogProductsLoadResult> LoadCatalogProductsAsync(
        string? name,
        int? sellerId,
        IEnumerable<int>? categoryIds,
        IEnumerable<string>? origins,
        IEnumerable<string>? standards,
        IEnumerable<string>? units,
        string unavailableMessage)
    {
        var client = _httpClientFactory.CreateClient("Catalog");
        AttachAccessToken(client);

        var url = BuildCatalogProductsUrl(name, sellerId, categoryIds, origins, standards, units);
        HttpResponseMessage response;
        try
        {
            response = await client.GetAsync(url);
        }
        catch (HttpRequestException)
        {
            return new CatalogProductsLoadResult
            {
                ErrorResult = CreateUpstreamUnavailableResult("Catalog", unavailableMessage)
            };
        }

        var content = await response.Content.ReadAsStringAsync();
        if (!response.IsSuccessStatusCode)
        {
            return new CatalogProductsLoadResult
            {
                ErrorResult = new ContentResult
                {
                    StatusCode = (int)response.StatusCode,
                    Content = content,
                    ContentType = "application/json"
                }
            };
        }

        var items = JsonSerializer.Deserialize<List<CatalogProductApiDto>>(content, JsonOptions) ?? new List<CatalogProductApiDto>();
        foreach (var item in items)
        {
            HydrateCatalogProductInfo(item);
        }

        var productStatsById = await GetOrderingProductStatsAsync(items.Select(item => item.ProductId));
        ApplyProductStats(items, productStatsById);
        return new CatalogProductsLoadResult
        {
            Items = items
        };
    }

    private async Task<CatalogProductLoadResult> LoadCatalogProductByIdAsync(int id, string unavailableMessage)
    {
        var client = _httpClientFactory.CreateClient("Catalog");
        AttachAccessToken(client);

        HttpResponseMessage response;
        try
        {
            response = await client.GetAsync($"/api/products/{id}");
        }
        catch (HttpRequestException)
        {
            return new CatalogProductLoadResult
            {
                ErrorResult = CreateUpstreamUnavailableResult("Catalog", unavailableMessage)
            };
        }

        var content = await response.Content.ReadAsStringAsync();
        if (!response.IsSuccessStatusCode)
        {
            return new CatalogProductLoadResult
            {
                ErrorResult = new ContentResult
                {
                    StatusCode = (int)response.StatusCode,
                    Content = content,
                    ContentType = "application/json"
                }
            };
        }

        var product = JsonSerializer.Deserialize<CatalogProductApiDto>(content, JsonOptions);
        if (product is null)
        {
            return new CatalogProductLoadResult();
        }

        HydrateCatalogProductInfo(product);
        var productStatsById = await GetOrderingProductStatsAsync([product.ProductId]);
        ApplyProductStats([product], productStatsById);
        return new CatalogProductLoadResult
        {
            Product = product
        };
    }

    private static string BuildCatalogProductsUrl(
        string? name,
        int? sellerId,
        IEnumerable<int>? categoryIds,
        IEnumerable<string>? origins,
        IEnumerable<string>? standards,
        IEnumerable<string>? units)
    {
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

        foreach (var origin in (origins ?? Array.Empty<string>())
                     .Where(value => !string.IsNullOrWhiteSpace(value))
                     .Select(value => value.Trim())
                     .Distinct(StringComparer.OrdinalIgnoreCase))
        {
            query.Add($"origins={Uri.EscapeDataString(origin)}");
        }

        foreach (var standard in (standards ?? Array.Empty<string>())
                     .Where(value => !string.IsNullOrWhiteSpace(value))
                     .Select(value => value.Trim())
                     .Distinct(StringComparer.OrdinalIgnoreCase))
        {
            query.Add($"standards={Uri.EscapeDataString(standard)}");
        }

        foreach (var unit in (units ?? Array.Empty<string>())
                     .Where(value => !string.IsNullOrWhiteSpace(value))
                     .Select(value => value.Trim())
                     .Distinct(StringComparer.OrdinalIgnoreCase))
        {
            query.Add($"units={Uri.EscapeDataString(unit)}");
        }

        return query.Count == 0 ? "/api/products" : "/api/products?" + string.Join("&", query);
    }

    private static List<RecommendationProductApiDto> BuildHomeRecommendations(
        IEnumerable<CatalogProductApiDto> items,
        int limit,
        IReadOnlyDictionary<int, OrderingHomePreferenceSeedApiDto> preferenceSeeds,
        IReadOnlyDictionary<int, OrderingHomeCollaborativeCandidateApiDto> collaborativeCandidates)
    {
        var candidates = items
            .Where(IsRecommendationCandidate)
            .ToList();
        var candidateLookup = candidates.ToDictionary(item => item.ProductId);
        var activePreferenceSeeds = preferenceSeeds.Values
            .Where(seed => seed.ProductId > 0 && seed.PreferenceScore > 0)
            .OrderByDescending(seed => seed.PreferenceScore)
            .ThenByDescending(seed => seed.PurchaseCount)
            .ThenByDescending(seed => seed.SearchClickCount)
            .ThenByDescending(seed => seed.RecommendationClickCount)
            .ThenByDescending(seed => seed.ViewCount)
            .Take(6)
            .Select(seed => new HomePreferenceSeedContext
            {
                Signal = seed,
                Product = candidateLookup.GetValueOrDefault(seed.ProductId)
            })
            .ToList();

        var scoredItems = candidates
            .Select(item => new
            {
                Item = item,
                Match = CalculateHomePersonalizationMatch(item, activePreferenceSeeds),
                CollaborativeCandidate = collaborativeCandidates.TryGetValue(item.ProductId, out var collaborativeCandidate)
                    ? collaborativeCandidate
                    : null,
                BaseScore = CalculateHomeRecommendationScore(item)
            })
            .Select(entry => new
            {
                entry.Item,
                entry.Match,
                entry.CollaborativeCandidate,
                Score = entry.BaseScore
                    + (entry.Match?.Score ?? 0d)
                    + (entry.CollaborativeCandidate?.CollaborativeScore ?? 0d)
            })
            .OrderByDescending(entry => entry.Score)
            .ThenByDescending(entry => entry.CollaborativeCandidate?.CollaborativeScore ?? 0d)
            .ThenByDescending(entry => entry.Match?.Score ?? 0d)
            .ThenByDescending(entry => entry.Item.AverageRating)
            .ThenByDescending(entry => entry.Item.SoldCount)
            .ThenByDescending(entry => entry.Item.ProductId)
            .ToList();

        var categoryCounts = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        var selected = new List<RecommendationProductApiDto>();

        foreach (var entry in scoredItems)
        {
            var categoryKey = string.IsNullOrWhiteSpace(entry.Item.CategoryName)
                ? $"category-{entry.Item.CategoryId}"
                : entry.Item.CategoryName!.Trim();
            var currentCount = categoryCounts.TryGetValue(categoryKey, out var existingCount) ? existingCount : 0;
            if (currentCount >= 2)
            {
                continue;
            }

            categoryCounts[categoryKey] = currentCount + 1;
            selected.Add(MapRecommendationProduct(
                entry.Item,
                entry.Score,
                BuildHomeRecommendationReason(entry.Item, entry.Match, entry.CollaborativeCandidate)));
            if (selected.Count >= limit)
            {
                return selected;
            }
        }

        foreach (var entry in scoredItems)
        {
            if (selected.Any(item => item.ProductId == entry.Item.ProductId))
            {
                continue;
            }

            selected.Add(MapRecommendationProduct(
                entry.Item,
                entry.Score,
                BuildHomeRecommendationReason(entry.Item, entry.Match, entry.CollaborativeCandidate)));
            if (selected.Count >= limit)
            {
                break;
            }
        }

        return selected;
    }

    private static HomePreferenceMatchDto? CalculateHomePersonalizationMatch(
        CatalogProductApiDto candidate,
        IEnumerable<HomePreferenceSeedContext> preferenceSeeds)
    {
        HomePreferenceMatchDto? bestMatch = null;
        foreach (var seed in preferenceSeeds)
        {
            if (seed.Signal.ProductId <= 0 || seed.Signal.PreferenceScore <= 0)
            {
                continue;
            }

            double score;
            var isDirectMatch = candidate.ProductId == seed.Signal.ProductId;
            if (isDirectMatch)
            {
                score =
                    (seed.Signal.ViewCount * 18d)
                    + (seed.Signal.SearchClickCount * 52d)
                    + (seed.Signal.RecommendationClickCount * 40d)
                    + (seed.Signal.PurchaseCount * 64d)
                    + (seed.Signal.PreferenceScore * 1.75d)
                    + 120d;
            }
            else if (seed.Product is not null)
            {
                var affinityScore = CalculateHomeAffinityScore(seed.Product, candidate);
                if (affinityScore <= 0d)
                {
                    continue;
                }

                var preferenceMultiplier = 0.4d + Math.Min(1.8d, Math.Log10(seed.Signal.PreferenceScore + 1d));
                score = affinityScore * preferenceMultiplier;
            }
            else
            {
                continue;
            }

            if (score <= 0d)
            {
                continue;
            }

            if (bestMatch is null || score > bestMatch.Score)
            {
                bestMatch = new HomePreferenceMatchDto
                {
                    SeedProductId = seed.Signal.ProductId,
                    SeedProduct = seed.Product,
                    Signal = seed.Signal,
                    IsDirectMatch = isDirectMatch,
                    Score = score
                };
            }
        }

        return bestMatch;
    }

    private static List<RecommendationProductApiDto> BuildSimilarRecommendations(
        CatalogProductApiDto seedProduct,
        IEnumerable<CatalogProductApiDto> items,
        int limit,
        IReadOnlyDictionary<int, OrderingSimilarProductSignalApiDto> collaborativeSignals)
    {
        var candidates = items
            .Where(item => item.ProductId != seedProduct.ProductId)
            .Where(IsRecommendationCandidate)
            .Select(item =>
            {
                collaborativeSignals.TryGetValue(item.ProductId, out var signal);
                return new
                {
                    Item = item,
                    CollaborativeSignal = signal,
                    Score = CalculateSimilarProductScore(seedProduct, item, signal)
                };
            })
            .Where(entry => entry.Score > 0)
            .OrderByDescending(entry => entry.Score)
            .ThenByDescending(entry => entry.CollaborativeSignal?.CollaborativeScore ?? 0d)
            .ThenByDescending(entry => entry.Item.AverageRating)
            .ThenByDescending(entry => entry.Item.SoldCount)
            .ThenByDescending(entry => entry.Item.ProductId)
            .Take(limit)
            .Select(entry => MapRecommendationProduct(
                entry.Item,
                entry.Score,
                BuildSimilarRecommendationReason(seedProduct, entry.Item, entry.CollaborativeSignal)))
            .ToList();

        return candidates;
    }

    private static IReadOnlyDictionary<int, PublicMerchantApiDto> BuildMerchantLookup(IEnumerable<PublicMerchantApiDto> merchants)
    {
        return merchants
            .Where(merchant => merchant.SellerId > 0)
            .GroupBy(merchant => merchant.SellerId)
            .ToDictionary(group => group.Key, group => SelectPreferredMerchant(group));
    }

    private ContentResult CreateUpstreamUnavailableResult(string upstreamService, string message)
    {
        var payload = JsonSerializer.Serialize(new
        {
            error = "upstream_unavailable",
            upstreamService,
            message
        });

        return new ContentResult
        {
            StatusCode = StatusCodes.Status503ServiceUnavailable,
            Content = payload,
            ContentType = "application/json"
        };
    }

    private static PublicMerchantApiDto SelectPreferredMerchant(IEnumerable<PublicMerchantApiDto> merchants)
    {
        return merchants
            .OrderByDescending(CalculateMerchantProfileScore)
            .ThenByDescending(merchant => merchant.JoinedAt ?? DateTime.MinValue)
            .ThenByDescending(merchant => !string.IsNullOrWhiteSpace(merchant.ShopName))
            .ThenByDescending(merchant => !string.IsNullOrWhiteSpace(merchant.AddressSummary))
            .First();
    }

    private static int CalculateMerchantProfileScore(PublicMerchantApiDto merchant)
    {
        var score = 0;
        if (!string.IsNullOrWhiteSpace(merchant.ShopName))
        {
            score += 4;
        }

        if (!string.IsNullOrWhiteSpace(merchant.UserName))
        {
            score += 2;
        }

        if (!string.IsNullOrWhiteSpace(merchant.AddressSummary))
        {
            score += 3;
        }

        if (!string.IsNullOrWhiteSpace(merchant.Avatar))
        {
            score += 1;
        }

        if (merchant.JoinedAt.HasValue)
        {
            score += 1;
        }

        return score;
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

    private static bool IsRecommendationCandidate(CatalogProductApiDto item)
    {
        return item.ProductId > 0
            && item.Status
            && !item.IsManuallyDisabled
            && item.AvailableStock > 0;
    }

    private static double CalculateHomeRecommendationScore(CatalogProductApiDto item)
    {
        var score = 0d;
        score += item.AvailableStock switch
        {
            > 20 => 26d,
            > 10 => 20d,
            > 0 => 12d,
            _ => -12d
        };

        score += (double)item.AverageRating * 9d;
        score += Math.Log10(item.SoldCount + 1) * 14d;
        score += Math.Log10(item.ReviewCount + 1) * 8d;
        score += GetContentCompletenessScore(item);
        score += GetAttributeCoverageScore(item);
        score += HasFreshnessSignals(item) ? 8d : 0d;
        score += IsSeasonalProduct(item) ? 6d : 0d;

        if (item.CreatedDate >= DateTime.UtcNow.AddDays(-30))
        {
            score += 5d;
        }

        return score;
    }

    private static double CalculateHomeAffinityScore(CatalogProductApiDto seedProduct, CatalogProductApiDto candidate)
    {
        var score = 0d;
        if (seedProduct.ProductId == candidate.ProductId)
        {
            return 0d;
        }

        if (seedProduct.CategoryId > 0 && seedProduct.CategoryId == candidate.CategoryId)
        {
            score += 60d;
        }
        else if (!string.IsNullOrWhiteSpace(seedProduct.CategoryName)
                 && string.Equals(seedProduct.CategoryName.Trim(), candidate.CategoryName?.Trim(), StringComparison.OrdinalIgnoreCase))
        {
            score += 42d;
        }

        if (IsSameContentValue(seedProduct.Origin, candidate.Origin))
        {
            score += 14d;
        }

        if (IsSameContentValue(seedProduct.Standard, candidate.Standard))
        {
            score += 12d;
        }

        if (IsSameContentValue(seedProduct.Preservation, candidate.Preservation))
        {
            score += 10d;
        }

        if (IsSameContentValue(seedProduct.UnitName, candidate.UnitName))
        {
            score += 6d;
        }

        score += CalculateAttributeSimilarityBonus(seedProduct, candidate);
        score += CalculateWeightSimilarityBonus(seedProduct.Weight, candidate.Weight);
        score += CalculatePriceSimilarityBonus(seedProduct.Price, candidate.Price) * 0.6d;
        score += HasFreshnessSignals(candidate) ? 4d : 0d;
        return score;
    }

    private static double CalculateSimilarProductScore(
        CatalogProductApiDto seedProduct,
        CatalogProductApiDto candidate,
        OrderingSimilarProductSignalApiDto? collaborativeSignal)
    {
        var score = 0d;
        if (seedProduct.CategoryId > 0 && seedProduct.CategoryId == candidate.CategoryId)
        {
            score += 120d;
        }
        else if (!string.IsNullOrWhiteSpace(seedProduct.CategoryName)
                 && string.Equals(seedProduct.CategoryName.Trim(), candidate.CategoryName?.Trim(), StringComparison.OrdinalIgnoreCase))
        {
            score += 90d;
        }

        if (IsSameContentValue(seedProduct.Origin, candidate.Origin))
        {
            score += 24d;
        }

        if (IsSameContentValue(seedProduct.Standard, candidate.Standard))
        {
            score += 18d;
        }

        if (IsSameContentValue(seedProduct.Preservation, candidate.Preservation))
        {
            score += 14d;
        }

        if (IsSameContentValue(seedProduct.UnitName, candidate.UnitName))
        {
            score += 10d;
        }

        score += CalculateAttributeSimilarityBonus(seedProduct, candidate);
        score += CalculateWeightSimilarityBonus(seedProduct.Weight, candidate.Weight);
        score += CalculatePriceSimilarityBonus(seedProduct.Price, candidate.Price);
        score += (double)candidate.AverageRating * 4d;
        score += Math.Log10(candidate.SoldCount + 1) * 6d;
        score += Math.Log10(candidate.ReviewCount + 1) * 3d;
        score += HasFreshnessSignals(candidate) ? 4d : 0d;
        score += collaborativeSignal?.CollaborativeScore ?? 0d;
        return score;
    }

    private static RecommendationProductApiDto MapRecommendationProduct(CatalogProductApiDto item, double score, RecommendationReasonDto reason)
    {
        return new RecommendationProductApiDto
        {
            ProductId = item.ProductId,
            ProductName = item.ProductName,
            Price = item.Price,
            CategoryName = item.CategoryName,
            UnitName = item.UnitName,
            ImageFileName = item.ImageFileName,
            Origin = item.Origin,
            Standard = item.Standard,
            Preservation = item.Preservation,
            Weight = item.Weight,
            PrimarySellerId = item.PrimarySellerId,
            AverageRating = item.AverageRating,
            SoldCount = item.SoldCount,
            ReviewCount = item.ReviewCount,
            AvailableStock = item.AvailableStock,
            RecommendationScore = Math.Round(score, 2),
            RecommendationReason = reason.Text,
            RecommendationTags = reason.Tags
        };
    }

    private static RecommendationReasonDto BuildHomeRecommendationReason(
        CatalogProductApiDto item,
        HomePreferenceMatchDto? preferenceMatch,
        OrderingHomeCollaborativeCandidateApiDto? collaborativeCandidate)
    {
        var tags = new List<string>();
        if (collaborativeCandidate is not null)
        {
            if (collaborativeCandidate.CoPurchaseOrderCount > 0)
            {
                tags.Add("Hay được mua cùng mối quan tâm của bạn");
            }
            else if (collaborativeCandidate.CoClickSessionCount > 0)
            {
                tags.Add("Hay được quan tâm cùng gần đây");
            }
            else if (collaborativeCandidate.CoViewSessionCount > 0)
            {
                tags.Add("Hay được xem cùng gần đây");
            }
        }

        if (preferenceMatch is not null)
        {
            if (preferenceMatch.IsDirectMatch)
            {
                if (preferenceMatch.Signal.PurchaseCount > 0)
                {
                    tags.Add("Bạn đã mua trước đó");
                }
                else if (preferenceMatch.Signal.SearchClickCount > 0)
                {
                    tags.Add("Bạn từng bấm từ tìm kiếm");
                }
                else if (preferenceMatch.Signal.RecommendationClickCount > 0)
                {
                    tags.Add("Bạn đã mở từ gợi ý");
                }
                else if (preferenceMatch.Signal.ViewCount > 0)
                {
                    tags.Add("Bạn đã xem gần đây");
                }
            }
            else if (preferenceMatch.SeedProduct is not null)
            {
                tags.Add("Hợp với mối quan tâm gần đây");

                if (preferenceMatch.SeedProduct.CategoryId > 0 && preferenceMatch.SeedProduct.CategoryId == item.CategoryId)
                {
                    tags.Add("Cùng danh mục bạn quan tâm");
                }

                var matchedAttributeLabels = GetMatchedAttributeLabels(preferenceMatch.SeedProduct, item).Take(1);
                tags.AddRange(matchedAttributeLabels.Select(label => $"Cùng {label}"));

                if (IsSameContentValue(preferenceMatch.SeedProduct.Origin, item.Origin))
                {
                    tags.Add("Cùng xuất xứ bạn vừa xem");
                }

                if (IsSameContentValue(preferenceMatch.SeedProduct.Standard, item.Standard))
                {
                    tags.Add("Cùng chuẩn bạn quan tâm");
                }
            }
        }

        if (item.AverageRating >= 4)
        {
            tags.Add("Đánh giá tốt");
        }

        if (item.SoldCount >= 10)
        {
            tags.Add("Được mua nhiều");
        }

        if (!string.IsNullOrWhiteSpace(item.Origin))
        {
            tags.Add("Xuất xứ rõ");
        }

        if (!string.IsNullOrWhiteSpace(item.Standard))
        {
            tags.Add("Chuẩn rõ ràng");
        }

        if (HasFreshnessSignals(item))
        {
            tags.Add("Có tín hiệu tươi");
        }

        if (tags.Count == 0)
        {
            tags.Add("Đang có hàng");
        }

        var normalizedTags = tags
            .Where(tag => !string.IsNullOrWhiteSpace(tag))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .Take(3)
            .ToArray();

        return new RecommendationReasonDto
        {
            Text = string.Join(" · ", normalizedTags),
            Tags = normalizedTags
        };
    }

    private static RecommendationReasonDto BuildSimilarRecommendationReason(
        CatalogProductApiDto seedProduct,
        CatalogProductApiDto candidate,
        OrderingSimilarProductSignalApiDto? collaborativeSignal)
    {
        var tags = new List<string>();
        if (collaborativeSignal is not null)
        {
            if (collaborativeSignal.CoPurchaseOrderCount > 0)
            {
                tags.Add("Hay mua cùng");
            }
            else if (collaborativeSignal.CoClickSessionCount > 0)
            {
                tags.Add("Hay được quan tâm cùng");
            }
            else if (collaborativeSignal.CoViewSessionCount > 0)
            {
                tags.Add("Hay được xem cùng");
            }
        }

        if (seedProduct.CategoryId > 0 && seedProduct.CategoryId == candidate.CategoryId)
        {
            tags.Add("Cùng danh mục");
        }

        if (IsSameContentValue(seedProduct.Origin, candidate.Origin))
        {
            tags.Add("Cùng xuất xứ");
        }

        if (IsSameContentValue(seedProduct.Standard, candidate.Standard))
        {
            tags.Add("Cùng chuẩn");
        }

        if (IsSameContentValue(seedProduct.Preservation, candidate.Preservation))
        {
            tags.Add("Cùng cách bảo quản");
        }

        var matchedAttributes = GetMatchedAttributeLabels(seedProduct, candidate).Take(2).ToList();
        if (matchedAttributes.Count > 0)
        {
            tags.InsertRange(0, matchedAttributes.Select(label => $"Cùng {label}"));
        }

        if (CalculatePriceSimilarityBonus(seedProduct.Price, candidate.Price) >= 8d)
        {
            tags.Add("Giá gần tương đương");
        }

        if (candidate.AverageRating >= 4)
        {
            tags.Add("Đánh giá tốt");
        }

        if (tags.Count == 0)
        {
            tags.Add("Phù hợp để xem thêm");
        }

        return new RecommendationReasonDto
        {
            Text = string.Join(" · ", tags.Take(3)),
            Tags = tags.Take(3).ToArray()
        };
    }

    private static double GetContentCompletenessScore(CatalogProductApiDto item)
    {
        var score = 0d;
        if (!string.IsNullOrWhiteSpace(item.Origin))
        {
            score += 5d;
        }

        if (!string.IsNullOrWhiteSpace(item.Standard))
        {
            score += 4d;
        }

        if (!string.IsNullOrWhiteSpace(item.Preservation))
        {
            score += 4d;
        }

        if (!string.IsNullOrWhiteSpace(item.Weight))
        {
            score += 3d;
        }

        if (!string.IsNullOrWhiteSpace(item.ShortDescription))
        {
            score += 2d;
        }

        return score;
    }

    private static double GetAttributeCoverageScore(CatalogProductApiDto item)
    {
        var attributes = item.ProductAttributes?
            .Where(attribute => !string.IsNullOrWhiteSpace(attribute.ValueText))
            .Select(attribute => attribute.CategoryAttributeId)
            .Distinct()
            .Count() ?? 0;

        return Math.Min(12d, attributes * 2.5d);
    }

    private static bool IsSameContentValue(string? left, string? right)
    {
        var normalizedLeft = NormalizeText(left);
        var normalizedRight = NormalizeText(right);
        return !string.IsNullOrWhiteSpace(normalizedLeft)
            && string.Equals(normalizedLeft, normalizedRight, StringComparison.Ordinal);
    }

    private static double CalculatePriceSimilarityBonus(decimal seedPrice, decimal candidatePrice)
    {
        if (seedPrice <= 0 || candidatePrice <= 0)
        {
            return 0d;
        }

        var deltaRatio = Math.Abs((double)(candidatePrice - seedPrice) / (double)seedPrice);
        return Math.Max(0d, 18d - (deltaRatio * 36d));
    }

    private static double CalculateWeightSimilarityBonus(string? seedWeight, string? candidateWeight)
    {
        var seedValue = ExtractWeightValue(seedWeight);
        var candidateValue = ExtractWeightValue(candidateWeight);
        if (!seedValue.HasValue || !candidateValue.HasValue || seedValue.Value <= 0)
        {
            return 0d;
        }

        var deltaRatio = Math.Abs(candidateValue.Value - seedValue.Value) / seedValue.Value;
        return Math.Max(0d, 12d - (deltaRatio * 24d));
    }

    private static double CalculateAttributeSimilarityBonus(CatalogProductApiDto seedProduct, CatalogProductApiDto candidate)
    {
        var seedAttributes = BuildAttributeMap(seedProduct);
        if (seedAttributes.Count == 0)
        {
            return 0d;
        }

        var candidateAttributes = BuildAttributeMap(candidate);
        if (candidateAttributes.Count == 0)
        {
            return 0d;
        }

        var matched = 0;
        foreach (var seedAttribute in seedAttributes)
        {
            if (candidateAttributes.TryGetValue(seedAttribute.Key, out var candidateValue)
                && string.Equals(seedAttribute.Value, candidateValue, StringComparison.Ordinal))
            {
                matched++;
            }
        }

        return matched * 8d;
    }

    private static IReadOnlyDictionary<string, string> BuildAttributeMap(CatalogProductApiDto item)
    {
        return (item.ProductAttributes ?? new List<CatalogProductAttributeApiDto>())
            .Where(attribute => !string.IsNullOrWhiteSpace(attribute.AttributeKey))
            .Select(attribute => new
            {
                Key = NormalizeText(attribute.AttributeKey),
                Value = NormalizeText(attribute.NormalizedValue ?? attribute.ValueText)
            })
            .Where(attribute => !string.IsNullOrWhiteSpace(attribute.Key) && !string.IsNullOrWhiteSpace(attribute.Value))
            .GroupBy(attribute => attribute.Key)
            .ToDictionary(group => group.Key, group => group.First().Value);
    }

    private static IEnumerable<string> GetMatchedAttributeLabels(CatalogProductApiDto seedProduct, CatalogProductApiDto candidate)
    {
        var seedAttributes = (seedProduct.ProductAttributes ?? new List<CatalogProductAttributeApiDto>())
            .Where(attribute => !string.IsNullOrWhiteSpace(attribute.AttributeKey))
            .Select(attribute => new
            {
                Key = NormalizeText(attribute.AttributeKey),
                Label = attribute.DisplayName?.Trim() ?? attribute.AttributeKey?.Trim() ?? string.Empty,
                Value = NormalizeText(attribute.NormalizedValue ?? attribute.ValueText)
            })
            .Where(attribute => !string.IsNullOrWhiteSpace(attribute.Key) && !string.IsNullOrWhiteSpace(attribute.Value))
            .GroupBy(attribute => attribute.Key)
            .ToDictionary(group => group.Key, group => group.First());

        var candidateAttributes = BuildAttributeMap(candidate);
        foreach (var seedAttribute in seedAttributes.Values)
        {
            if (candidateAttributes.TryGetValue(seedAttribute.Key, out var candidateValue)
                && string.Equals(seedAttribute.Value, candidateValue, StringComparison.Ordinal))
            {
                yield return seedAttribute.Label;
            }
        }
    }

    private static double? ExtractWeightValue(string? raw)
    {
        if (string.IsNullOrWhiteSpace(raw))
        {
            return null;
        }

        var match = Regex.Match(raw, @"\d+(?:[.,]\d+)?");
        if (!match.Success)
        {
            return null;
        }

        var numeric = match.Value.Replace(',', '.');
        if (!double.TryParse(numeric, NumberStyles.AllowDecimalPoint, CultureInfo.InvariantCulture, out var parsed))
        {
            return null;
        }

        var normalized = NormalizeText(raw);
        if (normalized.Contains("kg", StringComparison.Ordinal))
        {
            return parsed * 1000d;
        }

        return parsed;
    }

    private static void HydrateCatalogProductInfo(CatalogProductApiDto item)
    {
        NormalizeCatalogProductAttributes(item.ProductAttributes);

        var firstInfo = item.ProductInfos?.FirstOrDefault();
        if (firstInfo is null)
        {
            return;
        }

        item.Weight = string.IsNullOrWhiteSpace(item.Weight) ? firstInfo.Weight?.Trim() : item.Weight?.Trim();
        item.Origin = string.IsNullOrWhiteSpace(item.Origin) ? firstInfo.Origin?.Trim() : item.Origin?.Trim();
        item.Standard = string.IsNullOrWhiteSpace(item.Standard) ? firstInfo.Standard?.Trim() : item.Standard?.Trim();
        item.Preservation = string.IsNullOrWhiteSpace(item.Preservation) ? firstInfo.Preservation?.Trim() : item.Preservation?.Trim();
    }

    private static void NormalizeCatalogProductAttributes(List<CatalogProductAttributeApiDto>? attributes)
    {
        if (attributes is null)
        {
            return;
        }

        foreach (var attribute in attributes)
        {
            attribute.DisplayName = GetPreferredAttributeDisplayName(attribute.AttributeKey, attribute.DisplayName);
            attribute.ValueText = GetPreferredAttributeValueText(attribute.AttributeKey, attribute.NormalizedValue, attribute.ValueText);
        }
    }

    private static string? GetPreferredAttributeDisplayName(string? attributeKey, string? fallbackDisplayName)
    {
        return NormalizeText(attributeKey) switch
        {
            "flavor_profile" => "Hương vị",
            "texture_profile" => "Kết cấu",
            "usage_profile" => "Gợi ý dùng",
            _ => fallbackDisplayName
        };
    }

    private static string? GetPreferredAttributeValueText(string? attributeKey, string? normalizedValue, string? fallbackValueText)
    {
        return (NormalizeText(attributeKey), NormalizeText(normalizedValue)) switch
        {
            ("flavor_profile", "cay") => "Cay",
            ("flavor_profile", "ngot_diu") => "Ngọt dịu",
            ("flavor_profile", "ngot_thanh") => "Ngọt thanh",
            ("flavor_profile", "ngot_nhe") => "Ngọt nhẹ",
            ("flavor_profile", "ngot") => "Ngọt",
            ("flavor_profile", "thanh") => "Thanh",
            ("flavor_profile", "bui") => "Bùi",
            ("flavor_profile", "tu_nhien") => "Tự nhiên",
            ("texture_profile", "gion") => "Giòn",
            ("texture_profile", "mem") => "Mềm",
            ("texture_profile", "day") => "Dày",
            ("texture_profile", "xop") => "Xốp",
            ("texture_profile", "tuoi") => "Tươi",
            ("usage_profile", "salad_an_song") => "Salad / ăn sống",
            ("usage_profile", "xao_nau_canh") => "Xào / nấu canh",
            ("usage_profile", "xao") => "Xào",
            ("usage_profile", "nau_canh") => "Nấu canh",
            ("usage_profile", "nuong") => "Nướng",
            ("usage_profile", "da_dung") => "Đa dụng",
            _ => fallbackValueText
        };
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
        public int ReviewCount { get; set; }
        public List<CatalogProductAttributeApiDto>? ProductAttributes { get; set; }
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

    private sealed class RecommendationReasonDto
    {
        public string Text { get; set; } = string.Empty;
        public string[] Tags { get; set; } = Array.Empty<string>();
    }

    private sealed class OrderingSimilarProductSignalApiDto
    {
        public int ProductId { get; set; }
        public int CoPurchaseOrderCount { get; set; }
        public int CoViewSessionCount { get; set; }
        public int CoClickSessionCount { get; set; }
        public double CollaborativeScore { get; set; }
    }

    private sealed class OrderingSearchRankingSignalApiDto
    {
        public int ProductId { get; set; }
        public int SearchClickCount { get; set; }
        public int SearchClickSessionCount { get; set; }
        public int SearchViewSessionCount { get; set; }
        public int SearchRecommendationClickCount { get; set; }
        public double HybridSearchScore { get; set; }
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

    private static bool HasFreshnessSignals(CatalogProductApiDto item)
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

    private static bool IsSeasonalProduct(CatalogProductApiDto item)
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

    private static IEnumerable<ProductSearchApiDto> ApplySort(
        IEnumerable<ProductSearchApiDto> items,
        string? sort,
        string keyword,
        IReadOnlyDictionary<int, OrderingSearchRankingSignalApiDto> searchRankingSignals)
    {
        var normalizedSort = (sort ?? "related").Trim().ToLowerInvariant();
        return normalizedSort switch
        {
            "newest" => items.OrderByDescending(item => item.ProductId),
            "bestseller" => items.OrderByDescending(item => item.SoldCount).ThenByDescending(item => item.ProductId),
            "price-asc" => items.OrderBy(item => item.Price).ThenByDescending(item => item.ProductId),
            "price-desc" => items.OrderByDescending(item => item.Price).ThenByDescending(item => item.ProductId),
            _ => items
                .OrderByDescending(item => CalculateHybridSearchScore(
                    item,
                    keyword,
                    searchRankingSignals.TryGetValue(item.ProductId, out var signal) ? signal : null))
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

    private static decimal CalculateHybridSearchScore(
        ProductSearchApiDto item,
        string keyword,
        OrderingSearchRankingSignalApiDto? rankingSignal)
    {
        var score = CalculateRelevanceScore(item, keyword);
        if (rankingSignal is not null && rankingSignal.HybridSearchScore > 0)
        {
            score += (decimal)rankingSignal.HybridSearchScore;
        }

        return score;
    }

    private async Task<IReadOnlyDictionary<int, OrderingProductStatsApiDto>> GetOrderingProductStatsAsync(IEnumerable<int> productIds)
    {
        var normalizedProductIds = productIds
            .Where(id => id > 0)
            .Distinct()
            .Take(200)
            .ToArray();

        if (normalizedProductIds.Length == 0)
        {
            return new Dictionary<int, OrderingProductStatsApiDto>();
        }

        var client = _httpClientFactory.CreateClient("Ordering");
        AttachAccessToken(client);

        var query = string.Join("&", normalizedProductIds.Select(productId => $"productIds={productId}"));

        try
        {
            var response = await client.GetAsync($"/api/orders/product-insights/stats?{query}");
            if (!response.IsSuccessStatusCode)
            {
                return new Dictionary<int, OrderingProductStatsApiDto>();
            }

            var content = await response.Content.ReadAsStringAsync();
            var items = JsonSerializer.Deserialize<List<OrderingProductStatsApiDto>>(content, JsonOptions) ?? new List<OrderingProductStatsApiDto>();
            return items
                .Where(item => item.ProductId > 0)
                .GroupBy(item => item.ProductId)
                .ToDictionary(group => group.Key, group => group.OrderByDescending(CalculateProductStatsScore).First());
        }
        catch (HttpRequestException)
        {
            return new Dictionary<int, OrderingProductStatsApiDto>();
        }
    }

    private async Task<OrderingHomePreferenceSeedResult> GetOrderingHomePreferenceSeedsAsync(
        string? sessionId,
        int limit)
    {
        var normalizedSessionId = (sessionId ?? string.Empty).Trim();
        if (string.IsNullOrWhiteSpace(normalizedSessionId))
        {
            return new OrderingHomePreferenceSeedResult();
        }

        var client = _httpClientFactory.CreateClient("Ordering");
        AttachAccessToken(client);

        var query = new List<string>
        {
            $"sessionId={Uri.EscapeDataString(normalizedSessionId)}",
            $"limit={Math.Clamp(limit, 1, 48)}"
        };

        try
        {
            var response = await client.GetAsync($"/api/orders/product-insights/home-profile?{string.Join("&", query)}");
            if (!response.IsSuccessStatusCode)
            {
                return new OrderingHomePreferenceSeedResult();
            }

            var content = await response.Content.ReadAsStringAsync();
            var items = JsonSerializer.Deserialize<List<OrderingHomePreferenceSeedApiDto>>(content, JsonOptions)
                        ?? new List<OrderingHomePreferenceSeedApiDto>();
            return new OrderingHomePreferenceSeedResult
            {
                SignalSource = GetRecommendationSignalSource(response),
                Seeds = items
                .Where(item => item.ProductId > 0 && item.PreferenceScore > 0)
                .GroupBy(item => item.ProductId)
                .ToDictionary(
                    group => group.Key,
                    group => group.OrderByDescending(item => item.PreferenceScore)
                        .ThenByDescending(item => item.PurchaseCount)
                        .ThenByDescending(item => item.SearchClickCount)
                        .ThenByDescending(item => item.RecommendationClickCount)
                        .ThenByDescending(item => item.ViewCount)
                        .ThenByDescending(item => item.LastInteractedAtUtc ?? DateTime.MinValue)
                        .First())
            };
        }
        catch (HttpRequestException)
        {
            return new OrderingHomePreferenceSeedResult();
        }
    }

    private async Task<OrderingHomeCollaborativeCandidateResult> GetOrderingHomeCollaborativeCandidatesAsync(
        string? sessionId,
        int limit)
    {
        var normalizedSessionId = (sessionId ?? string.Empty).Trim();
        if (string.IsNullOrWhiteSpace(normalizedSessionId))
        {
            return new OrderingHomeCollaborativeCandidateResult();
        }

        var client = _httpClientFactory.CreateClient("Ordering");
        AttachAccessToken(client);

        var query = new List<string>
        {
            $"sessionId={Uri.EscapeDataString(normalizedSessionId)}",
            $"limit={Math.Clamp(limit, 1, 48)}"
        };

        try
        {
            var response = await client.GetAsync($"/api/orders/product-insights/home-collaborative?{string.Join("&", query)}");
            if (!response.IsSuccessStatusCode)
            {
                return new OrderingHomeCollaborativeCandidateResult();
            }

            var content = await response.Content.ReadAsStringAsync();
            var items = JsonSerializer.Deserialize<List<OrderingHomeCollaborativeCandidateApiDto>>(content, JsonOptions)
                        ?? new List<OrderingHomeCollaborativeCandidateApiDto>();
            return new OrderingHomeCollaborativeCandidateResult
            {
                SignalSource = GetRecommendationSignalSource(response),
                Candidates = items
                .Where(item => item.ProductId > 0 && item.CollaborativeScore > 0)
                .GroupBy(item => item.ProductId)
                .ToDictionary(
                    group => group.Key,
                    group => group.OrderByDescending(item => item.CollaborativeScore)
                        .ThenByDescending(item => item.CoPurchaseOrderCount)
                        .ThenByDescending(item => item.CoClickSessionCount)
                        .ThenByDescending(item => item.CoViewSessionCount)
                        .First())
            };
        }
        catch (HttpRequestException)
        {
            return new OrderingHomeCollaborativeCandidateResult();
        }
    }

    private async Task<OrderingSimilarProductSignalResult> GetOrderingSimilarProductSignalsAsync(
        int productId,
        IEnumerable<int> candidateProductIds)
    {
        var normalizedCandidateProductIds = candidateProductIds
            .Where(id => id > 0 && id != productId)
            .Distinct()
            .Take(200)
            .ToArray();

        if (productId <= 0 || normalizedCandidateProductIds.Length == 0)
        {
            return new OrderingSimilarProductSignalResult();
        }

        var client = _httpClientFactory.CreateClient("Ordering");
        AttachAccessToken(client);

        var query = new List<string>
        {
            $"productId={productId}"
        };

        foreach (var candidateProductId in normalizedCandidateProductIds)
        {
            query.Add($"candidateProductIds={candidateProductId}");
        }

        try
        {
            var response = await client.GetAsync($"/api/orders/product-insights/similar?{string.Join("&", query)}");
            if (!response.IsSuccessStatusCode)
            {
                return new OrderingSimilarProductSignalResult();
            }

            var content = await response.Content.ReadAsStringAsync();
            var items = JsonSerializer.Deserialize<List<OrderingSimilarProductSignalApiDto>>(content, JsonOptions)
                        ?? new List<OrderingSimilarProductSignalApiDto>();
            return new OrderingSimilarProductSignalResult
            {
                SignalSource = GetRecommendationSignalSource(response),
                Signals = items
                .Where(item => item.ProductId > 0 && item.CollaborativeScore > 0)
                .GroupBy(item => item.ProductId)
                .ToDictionary(
                    group => group.Key,
                    group => group.OrderByDescending(item => item.CollaborativeScore)
                        .ThenByDescending(item => item.CoPurchaseOrderCount)
                        .ThenByDescending(item => item.CoClickSessionCount)
                        .ThenByDescending(item => item.CoViewSessionCount)
                        .First())
            };
        }
        catch (HttpRequestException)
        {
            return new OrderingSimilarProductSignalResult();
        }
    }

    private async Task<OrderingSearchRankingSignalResult> GetOrderingSearchRankingSignalsAsync(
        string keyword,
        IEnumerable<int> productIds)
    {
        var normalizedKeyword = (keyword ?? string.Empty).Trim();
        var normalizedProductIds = productIds
            .Where(id => id > 0)
            .Distinct()
            .Take(200)
            .ToArray();

        if (string.IsNullOrWhiteSpace(normalizedKeyword) || normalizedProductIds.Length == 0)
        {
            return new OrderingSearchRankingSignalResult();
        }

        var client = _httpClientFactory.CreateClient("Ordering");
        AttachAccessToken(client);

        var query = new List<string>
        {
            $"keyword={Uri.EscapeDataString(normalizedKeyword)}"
        };

        foreach (var productId in normalizedProductIds)
        {
            query.Add($"productIds={productId}");
        }

        try
        {
            var response = await client.GetAsync($"/api/orders/product-insights/search-ranking?{string.Join("&", query)}");
            if (!response.IsSuccessStatusCode)
            {
                return new OrderingSearchRankingSignalResult();
            }

            var content = await response.Content.ReadAsStringAsync();
            var items = JsonSerializer.Deserialize<List<OrderingSearchRankingSignalApiDto>>(content, JsonOptions)
                        ?? new List<OrderingSearchRankingSignalApiDto>();
            return new OrderingSearchRankingSignalResult
            {
                SignalSource = GetRecommendationSignalSource(response),
                Signals = items
                .Where(item => item.ProductId > 0 && item.HybridSearchScore > 0)
                .GroupBy(item => item.ProductId)
                .ToDictionary(
                    group => group.Key,
                    group => group.OrderByDescending(item => item.HybridSearchScore)
                        .ThenByDescending(item => item.SearchClickCount)
                        .ThenByDescending(item => item.SearchRecommendationClickCount)
                        .ThenByDescending(item => item.SearchViewSessionCount)
                        .First())
            };
        }
        catch (HttpRequestException)
        {
            return new OrderingSearchRankingSignalResult();
        }
    }

    private static bool IsHybridSearchSort(string? sort, string keyword)
    {
        if (string.IsNullOrWhiteSpace(keyword))
        {
            return false;
        }

        var normalizedSort = (sort ?? "related").Trim().ToLowerInvariant();
        return normalizedSort is "" or "related";
    }

    private static string? GetRecommendationSignalSource(HttpResponseMessage response)
    {
        return response.Headers.TryGetValues(RecommendationSignalSourceHeader, out var values)
            ? values.FirstOrDefault()?.Trim()
            : null;
    }

    private static string? BuildRecommendationSignalSource(params string?[] sources)
    {
        var normalized = sources
            .Where(value => !string.IsNullOrWhiteSpace(value))
            .Select(value => value!.Trim())
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();

        return normalized.Length == 0
            ? null
            : string.Join("+", normalized);
    }

    private static void ApplyProductStats(IEnumerable<CatalogProductApiDto> items, IReadOnlyDictionary<int, OrderingProductStatsApiDto> statsByProductId)
    {
        foreach (var item in items)
        {
            if (!statsByProductId.TryGetValue(item.ProductId, out var stats))
            {
                continue;
            }

            item.AverageRating = stats.AverageRating;
            item.SoldCount = stats.SoldCount;
            item.ReviewCount = stats.ReviewCount;
        }
    }

    private static void ApplyProductStats(IEnumerable<ProductSearchApiDto> items, IReadOnlyDictionary<int, OrderingProductStatsApiDto> statsByProductId)
    {
        foreach (var item in items)
        {
            if (!statsByProductId.TryGetValue(item.ProductId, out var stats))
            {
                continue;
            }

            item.AverageRating = stats.AverageRating;
            item.SoldCount = stats.SoldCount;
            item.ReviewCount = stats.ReviewCount;
        }
    }

    private ContentResult CreateJsonContentResult(object payload, int statusCode = StatusCodes.Status200OK)
    {
        return new ContentResult
        {
            StatusCode = statusCode,
            Content = JsonSerializer.Serialize(payload, JsonOptions),
            ContentType = "application/json"
        };
    }

    private static int CalculateProductStatsScore(OrderingProductStatsApiDto item)
    {
        var score = 0;
        if (item.AverageRating > 0)
        {
            score += 4;
        }

        if (item.ReviewCount > 0)
        {
            score += 2;
        }

        if (item.SoldCount > 0)
        {
            score += 3;
        }

        return score;
    }
}
