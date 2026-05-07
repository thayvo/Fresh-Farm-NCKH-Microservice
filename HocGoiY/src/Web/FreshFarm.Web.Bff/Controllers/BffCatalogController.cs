// Nguon goc: src\Web\FreshFarm.Web.Bff\Controllers\BffCatalogController.cs
// Duoc sao chep tu: D:\NCKH\DOAN\NCKH-FRESH-FARM\src\Web\FreshFarm.Web.Bff\Controllers\BffCatalogController.cs
// Thu muc hoc tap: HocGoiY

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
    private const string SearchCategoryPreferenceSignalSource = "materialized_category_pref_v1";
    private const string SearchSellerPreferenceSignalSource = "materialized_seller_pref_v1";
    private const string SimilarCategoryPreferenceSignalSource = "materialized_category_pref_v1";
    private const string SimilarSellerPreferenceSignalSource = "materialized_seller_pref_v1";
    private const int HomeFirstPassSellerCap = 3;
    private const int SimilarFirstPassSellerCap = 2;
    private const int SearchFirstPassSellerCap = 2;
    private const int SearchDiscoveryWindow = 8;
    private const int FavoriteShopFirstPassSellerCap = 2;
    private const string HomeContentSignalSource = "catalog_content_v1";
    private const string SimilarContentSignalSource = "catalog_content_v1";
    private const string SearchContentSignalSource = "catalog_keyword_v1";
    private const int BuyAgainRecentWindowDays = 30;
    private const string RecommendationSignalSourceHeader = "X-Recommendation-Signal-Source";
    private const string RecommendationFallbackReasonHeader = "X-Recommendation-Fallback-Reason";
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

    private bool HasAuthenticatedOrderingContext()
    {
        return !string.IsNullOrWhiteSpace(Request.Headers.Authorization.ToString())
            || !string.IsNullOrWhiteSpace(HttpContext.Session.GetString(AccessTokenSessionKey));
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

        var url = BuildCatalogProductSearchUrl(name, categoryIds, origins, standards, units);
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
        if (items.Count == 0 && !string.IsNullOrWhiteSpace(name))
        {
            var relaxedKeyword = TryBuildRelaxedCatalogKeyword(name);
            if (!string.IsNullOrWhiteSpace(relaxedKeyword)
                && !string.Equals(relaxedKeyword, name.Trim(), StringComparison.OrdinalIgnoreCase))
            {
                var relaxedUrl = BuildCatalogProductSearchUrl(relaxedKeyword, categoryIds, origins, standards, units);
                try
                {
                    var relaxedResponse = await client.GetAsync(relaxedUrl);
                    if (relaxedResponse.IsSuccessStatusCode)
                    {
                        var relaxedContent = await relaxedResponse.Content.ReadAsStringAsync();
                        items = JsonSerializer.Deserialize<List<ProductSearchApiDto>>(relaxedContent, JsonOptions) ?? new List<ProductSearchApiDto>();
                    }
                }
                catch (HttpRequestException)
                {
                    // Keep the original empty result if the relaxed retry cannot reach Catalog.
                }
            }
        }
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

        EnrichSearchProductsWithShopData(facetedList, availableShops);

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
        var hasAuthenticatedOrderingContext = HasAuthenticatedOrderingContext();
        var userCategoryScoreResult = IsHybridSearchSort(effectiveSort, normalizedKeyword) && hasAuthenticatedOrderingContext
            ? await GetOrderingUserCategoryScoresAsync(facetedList.Select(item => item.CategoryId), 16)
            : new OrderingUserCategoryScoreResult();
        var userSellerScoreResult = IsHybridSearchSort(effectiveSort, normalizedKeyword) && hasAuthenticatedOrderingContext
            ? await GetOrderingUserSellerScoresAsync(facetedList.Select(item => item.PrimarySellerId ?? 0), 16)
            : new OrderingUserSellerScoreResult();
        var searchRankingSignals = searchRankingResult.Signals;
        var userCategoryScores = userCategoryScoreResult.Scores;
        var userSellerScores = userSellerScoreResult.Scores;
        var rankingBehaviorSignalSource = searchRankingSignals.Count > 0 ? searchRankingResult.SignalSource : null;
        var rankingPreferenceSignalSource = BuildRecommendationSignalSource(
            userCategoryScores.Count > 0 ? SearchCategoryPreferenceSignalSource : null,
            userSellerScores.Count > 0 ? SearchSellerPreferenceSignalSource : null);
        var rankingAlgorithm = IsHybridSearchSort(effectiveSort, normalizedKeyword)
            ? ((searchRankingSignals.Count > 0 || userCategoryScores.Count > 0 || userSellerScores.Count > 0)
                ? HybridSearchRankingAlgorithm
                : KeywordSearchRankingAlgorithm)
            : null;
        var rankingSignalSource = BuildRecommendationSignalSource(rankingBehaviorSignalSource, rankingPreferenceSignalSource);
        var rankingPreferenceFallbackReason = userCategoryScores.Count == 0 && userSellerScores.Count == 0 && hasAuthenticatedOrderingContext
            ? BuildCompositeFallbackReason(
                ("category", "no_materialized_user_category_score"),
                ("seller", "no_materialized_user_seller_score"))
            : null;
        string? rankingFallbackReason;
        if (!hasAuthenticatedOrderingContext)
        {
            rankingFallbackReason = searchRankingResult.FallbackReason;
        }
        else if (userCategoryScores.Count > 0 || userSellerScores.Count > 0)
        {
            rankingFallbackReason = string.Equals(rankingBehaviorSignalSource, "ad_hoc_cf_v1", StringComparison.OrdinalIgnoreCase)
                ? searchRankingResult.FallbackReason
                : null;
        }
        else
        {
            rankingFallbackReason = BuildCompositeFallbackReason(
                ("behavior", searchRankingResult.FallbackReason),
                ("preference", rankingPreferenceFallbackReason));
        }
        filtered = ApplySort(filtered, effectiveSort, normalizedKeyword, searchRankingSignals, userCategoryScores, userSellerScores);

        var filteredList = filtered.ToList();
        EnsureRecentSearchSellerDiscoveryCandidate(filteredList, searchRankingSignals, userCategoryScores, userSellerScores, pageSize);
        EnsureFavoriteSearchSellerDiscoveryCandidate(filteredList, searchRankingSignals, userCategoryScores, userSellerScores, pageSize);
        ApplySearchRecommendationReasons(filteredList, normalizedKeyword, searchRankingSignals, userCategoryScores, userSellerScores);
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
            rankingFallbackReason,
            contentSignalSource = SearchContentSignalSource,
            signalBreakdown = new SearchRankingSignalBreakdownDto
            {
                Content = SearchContentSignalSource,
                Overall = rankingSignalSource,
                OverallReason = rankingFallbackReason,
                Preference = rankingPreferenceSignalSource,
                PreferenceReason = rankingPreferenceFallbackReason
            }
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
        await EnrichCatalogProductsWithMerchantDataAsync(loadResult.Items);
        var stableSessionId = await EnsureStableRecommendationSessionIdAsync();
        var preferenceSeedResult = await GetOrderingHomePreferenceSeedsAsync(stableSessionId, 12);
        var collaborativeCandidateResult = await GetOrderingHomeCollaborativeCandidatesAsync(stableSessionId, 24);
        var preferenceSeeds = preferenceSeedResult.Seeds;
        var collaborativeCandidates = collaborativeCandidateResult.Candidates;
        var hasAuthenticatedRecommendationContext = HasAuthenticatedRecommendationContext();
        var userProductScoreResult = hasAuthenticatedRecommendationContext
            ? await GetOrderingUserProductScoresAsync(loadResult.Items.Select(item => item.ProductId), 24)
            : new OrderingUserProductScoreResult();
        var userPurchaseSeedResult = hasAuthenticatedRecommendationContext
            ? await GetOrderingUserProductScoresAsync(productIds: null, limit: 48)
            : new OrderingUserProductScoreResult();
        var userCategoryScoreResult = hasAuthenticatedRecommendationContext
            ? await GetOrderingUserCategoryScoresAsync(loadResult.Items.Select(item => item.CategoryId), 16)
            : new OrderingUserCategoryScoreResult();
        var userSellerScoreResult = hasAuthenticatedRecommendationContext
            ? await GetOrderingUserSellerScoresAsync(
                loadResult.Items.Select(item => item.PrimarySellerId.GetValueOrDefault())
                    .Where(id => id > 0),
                24)
            : new OrderingUserSellerScoreResult();
        var userProductScores = userProductScoreResult.Scores;
        var userCategoryScores = userCategoryScoreResult.Scores;
        var userSellerScores = userSellerScoreResult.Scores;
        var purchaseHistorySeeds = BuildPurchaseHistorySeeds(preferenceSeeds, userPurchaseSeedResult.Scores);
        var rankedItems = BuildHomeRecommendations(loadResult.Items, normalizedLimit, preferenceSeeds, collaborativeCandidates, userProductScores, userCategoryScores, userSellerScores);
        var hasPersonalSignals = preferenceSeeds.Count > 0 || collaborativeCandidates.Count > 0 || userProductScores.Count > 0 || userCategoryScores.Count > 0 || userSellerScores.Count > 0;
        var preferenceSignalSource = preferenceSeeds.Count > 0 ? preferenceSeedResult.SignalSource : null;
        var collaborativeSignalSource = collaborativeCandidates.Count > 0 ? collaborativeCandidateResult.SignalSource : null;
        var overallSignalSource = BuildRecommendationSignalSource(preferenceSignalSource, collaborativeSignalSource);
        var overallFallbackReason = BuildCompositeFallbackReason(
            ("preference", preferenceSeedResult.FallbackReason),
            ("collaborative", collaborativeCandidateResult.FallbackReason));
        var basketAffinityResult = hasAuthenticatedRecommendationContext
            ? await GetOrderingBasketAffinitiesAsync(
                purchaseHistorySeeds.Values
                    .Where(seed => seed.ProductId > 0 && seed.PurchaseCount > 0)
                    .OrderByDescending(seed => seed.PurchaseCount)
                    .ThenByDescending(seed => seed.LastInteractedAtUtc ?? DateTime.MinValue)
                    .Select(seed => seed.ProductId)
                    .Take(4),
                loadResult.Items.Select(item => item.ProductId),
                24)
            : new OrderingBasketAffinityResult();
        var replenishmentProfileResult = hasAuthenticatedRecommendationContext
            ? await GetOrderingReplenishmentProfilesAsync(loadResult.Items.Select(item => item.ProductId), 24)
            : new OrderingReplenishmentProfileResult();
        return Ok(new RecommendationCollectionApiDto
        {
            Placement = HomeRecommendationPlacement,
            Algorithm = hasPersonalSignals
                ? HybridHomeRecommendationAlgorithm
                : HomeRecommendationAlgorithm,
            ContentSignalSource = HomeContentSignalSource,
            SignalSource = overallSignalSource,
            FallbackReason = overallFallbackReason,
            PreferenceSignalSource = preferenceSignalSource,
            PreferenceFallbackReason = preferenceSeedResult.FallbackReason,
            CollaborativeSignalSource = collaborativeSignalSource,
            CollaborativeFallbackReason = collaborativeCandidateResult.FallbackReason,
            SignalBreakdown = new RecommendationSignalBreakdownDto
            {
                Content = HomeContentSignalSource,
                Overall = overallSignalSource,
                OverallReason = overallFallbackReason,
                Preference = preferenceSignalSource,
                PreferenceReason = preferenceSeedResult.FallbackReason,
                Collaborative = collaborativeSignalSource,
                CollaborativeReason = collaborativeCandidateResult.FallbackReason
            },
            GeneratedAtUtc = DateTime.UtcNow,
            Items = rankedItems,
            Sections = BuildHomeRecommendationSections(
                loadResult.Items,
                rankedItems,
                purchaseHistorySeeds,
                userSellerScores,
                basketAffinityResult.Signals,
                replenishmentProfileResult.Profiles,
                hasPersonalSignals)
        });
    }

    private bool HasAuthenticatedRecommendationContext()
    {
        if (!string.IsNullOrWhiteSpace(Request.Headers.Authorization.ToString()))
        {
            return true;
        }

        var token = HttpContext.Session.GetString(AccessTokenSessionKey);
        return !string.IsNullOrWhiteSpace(token);
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

        var recommendationPool = categoryScopedResult.Items.ToList();
        recommendationPool.Add(seedProduct);
        await EnrichCatalogProductsWithMerchantDataAsync(recommendationPool);

        var similarSignalResult = await GetOrderingSimilarProductSignalsAsync(
            seedProduct.ProductId,
            categoryScopedResult.Items.Select(item => item.ProductId));
        var collaborativeSignals = similarSignalResult.Signals;
        var hasAuthenticatedOrderingContext = HasAuthenticatedOrderingContext();
        var userCategoryScoreResult = hasAuthenticatedOrderingContext
            ? await GetOrderingUserCategoryScoresAsync(categoryScopedResult.Items.Select(item => item.CategoryId), 16)
            : new OrderingUserCategoryScoreResult();
        var userSellerScoreResult = hasAuthenticatedOrderingContext
            ? await GetOrderingUserSellerScoresAsync(
                categoryScopedResult.Items.Select(item => item.PrimarySellerId.GetValueOrDefault())
                    .Where(sellerId => sellerId > 0),
                16)
            : new OrderingUserSellerScoreResult();
        var userCategoryScores = userCategoryScoreResult.Scores;
        var userSellerScores = userSellerScoreResult.Scores;
        var preferenceSignalSource = BuildRecommendationSignalSource(
            userCategoryScores.Count > 0 ? SimilarCategoryPreferenceSignalSource : null,
            userSellerScores.Count > 0 ? SimilarSellerPreferenceSignalSource : null);
        var preferenceFallbackReason = userCategoryScores.Count == 0 && userSellerScores.Count == 0 && hasAuthenticatedOrderingContext
            ? BuildCompositeFallbackReason(
                ("category", "no_materialized_user_category_score"),
                ("seller", "no_materialized_user_seller_score"))
            : null;
        var collaborativeSignalSource = collaborativeSignals.Count > 0 ? similarSignalResult.SignalSource : null;
        var overallSignalSource = BuildRecommendationSignalSource(preferenceSignalSource, collaborativeSignalSource);
        string? overallFallbackReason;
        if (!hasAuthenticatedOrderingContext)
        {
            overallFallbackReason = similarSignalResult.FallbackReason;
        }
        else if (userCategoryScores.Count > 0 || userSellerScores.Count > 0 || collaborativeSignals.Count > 0)
        {
            overallFallbackReason = string.Equals(collaborativeSignalSource, "ad_hoc_cf_v1", StringComparison.OrdinalIgnoreCase)
                ? similarSignalResult.FallbackReason
                : null;
        }
        else
        {
            overallFallbackReason = BuildCompositeFallbackReason(
                ("collaborative", similarSignalResult.FallbackReason),
                ("preference", preferenceFallbackReason));
        }
        var similarItems = BuildSimilarRecommendations(
            seedProduct,
            categoryScopedResult.Items,
            Math.Clamp(limit, 1, 16),
            collaborativeSignals,
            userCategoryScores,
            userSellerScores);
        return Ok(new RecommendationCollectionApiDto
        {
            Placement = SimilarRecommendationPlacement,
            Algorithm = collaborativeSignals.Count > 0 || userCategoryScores.Count > 0 || userSellerScores.Count > 0
                ? HybridSimilarRecommendationAlgorithm
                : SimilarRecommendationAlgorithm,
            ContentSignalSource = SimilarContentSignalSource,
            SignalSource = overallSignalSource,
            FallbackReason = overallFallbackReason,
            PreferenceSignalSource = preferenceSignalSource,
            PreferenceFallbackReason = preferenceFallbackReason,
            CollaborativeSignalSource = collaborativeSignalSource,
            CollaborativeFallbackReason = similarSignalResult.FallbackReason,
            SignalBreakdown = new RecommendationSignalBreakdownDto
            {
                Content = SimilarContentSignalSource,
                Overall = overallSignalSource,
                OverallReason = overallFallbackReason,
                Preference = preferenceSignalSource,
                PreferenceReason = preferenceFallbackReason,
                Collaborative = collaborativeSignalSource,
                CollaborativeReason = similarSignalResult.FallbackReason
            },
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

        var merchantBySellerId = await GetMerchantLookupAsync(sellerIds.Select(static sellerId => (int?)sellerId));
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
        public string? SellerShopName { get; set; }
        public string? SellerAddressSummary { get; set; }
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
        public string? FallbackReason { get; set; }
        public string? PreferenceSignalSource { get; set; }
        public string? PreferenceFallbackReason { get; set; }
        public string? CollaborativeSignalSource { get; set; }
        public string? CollaborativeFallbackReason { get; set; }
        public RecommendationSignalBreakdownDto? SignalBreakdown { get; set; }
        public DateTime GeneratedAtUtc { get; set; }
        public int? SeedProductId { get; set; }
        public List<RecommendationProductApiDto> Items { get; set; } = new();
        public List<RecommendationSectionApiDto> Sections { get; set; } = new();
    }

    private sealed class RecommendationSectionApiDto
    {
        public string Id { get; set; } = string.Empty;
        public string Title { get; set; } = string.Empty;
        public string Subtitle { get; set; } = string.Empty;
        public string PillLabel { get; set; } = string.Empty;
        public List<RecommendationProductApiDto> Items { get; set; } = new();
    }

    private sealed class BuyAgainCandidateDto
    {
        public RecommendationProductApiDto Item { get; set; } = new();
        public PurchaseHistorySeedApiDto Seed { get; set; } = new();
        public double SortScore { get; set; }
    }

    private sealed class BuyWithHistoryCandidateDto
    {
        public RecommendationProductApiDto Item { get; set; } = new();
        public OrderingBasketAffinityApiDto Signal { get; set; } = new();
        public PurchaseHistorySeedApiDto? Seed { get; set; }
        public double SortScore { get; set; }
    }

    private sealed class ReplenishmentCandidateDto
    {
        public RecommendationProductApiDto Item { get; set; } = new();
        public OrderingReplenishmentProfileApiDto Profile { get; set; } = new();
        public double SortScore { get; set; }
    }

    private sealed class FavoriteShopSignalDto
    {
        public int SellerId { get; set; }
        public string SellerShopName { get; set; } = string.Empty;
        public string? SellerAddressSummary { get; set; }
        public int PurchaseCount { get; set; }
        public int SearchClickCount { get; set; }
        public int ViewCount { get; set; }
        public int DistinctPurchasedProductCount { get; set; }
        public double SellerAffinityScore { get; set; }
        public DateTime? LastInteractedAtUtc { get; set; }
    }

    private sealed class FavoriteShopCandidateDto
    {
        public RecommendationProductApiDto Item { get; set; } = new();
        public FavoriteShopSignalDto Seller { get; set; } = new();
        public bool IsPastPurchase { get; set; }
        public double SortScore { get; set; }
    }

    private sealed class RecommendationSignalBreakdownDto
    {
        public string? Content { get; set; }
        public string? Overall { get; set; }
        public string? OverallReason { get; set; }
        public string? Preference { get; set; }
        public string? PreferenceReason { get; set; }
        public string? Collaborative { get; set; }
        public string? CollaborativeReason { get; set; }
    }

    private sealed class SearchRankingSignalBreakdownDto
    {
        public string? Content { get; set; }
        public string? Overall { get; set; }
        public string? OverallReason { get; set; }
        public string? Preference { get; set; }
        public string? PreferenceReason { get; set; }
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
        public string? SellerShopName { get; set; }
        public string? SellerAddressSummary { get; set; }
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

    private sealed class OrderingUserProductScoreApiDto
    {
        public int UserId { get; set; }
        public int ProductId { get; set; }
        public int ViewCount { get; set; }
        public int SearchClickCount { get; set; }
        public int RecommendationClickCount { get; set; }
        public int PurchaseCount { get; set; }
        public double UserProductScore { get; set; }
        public DateTime? LastInteractedAtUtc { get; set; }
    }

    private sealed class OrderingUserSellerScoreApiDto
    {
        public int UserId { get; set; }
        public int SellerId { get; set; }
        public int ViewCount { get; set; }
        public int SearchClickCount { get; set; }
        public int PurchaseCount { get; set; }
        public double UserSellerScore { get; set; }
        public DateTime? LastInteractedAtUtc { get; set; }
    }

    private sealed class OrderingUserCategoryScoreApiDto
    {
        public int UserId { get; set; }
        public int CategoryId { get; set; }
        public string CategoryName { get; set; } = string.Empty;
        public int ViewCount { get; set; }
        public int SearchClickCount { get; set; }
        public int RecommendationClickCount { get; set; }
        public int PurchaseCount { get; set; }
        public double UserCategoryScore { get; set; }
        public DateTime? LastInteractedAtUtc { get; set; }
    }

    private sealed class PurchaseHistorySeedApiDto
    {
        public int ProductId { get; set; }
        public int PurchaseCount { get; set; }
        public double PreferenceScore { get; set; }
        public DateTime? LastInteractedAtUtc { get; set; }
    }

    private sealed class OrderingBasketAffinityApiDto
    {
        public int ProductId { get; set; }
        public int CandidateProductId { get; set; }
        public int CoPurchaseOrderCount { get; set; }
        public double BasketScore { get; set; }
    }

    private sealed class OrderingReplenishmentProfileApiDto
    {
        public int UserId { get; set; }
        public int ProductId { get; set; }
        public int PurchaseCount { get; set; }
        public DateTime LastPurchasedAtUtc { get; set; }
        public double AverageRepurchaseDays { get; set; }
        public DateTime? ExpectedReorderAtUtc { get; set; }
        public double ReplenishmentScore { get; set; }
    }

    private sealed class OrderingHomePreferenceSeedResult
    {
        public IReadOnlyDictionary<int, OrderingHomePreferenceSeedApiDto> Seeds { get; init; } = new Dictionary<int, OrderingHomePreferenceSeedApiDto>();
        public string? SignalSource { get; init; }
        public string? FallbackReason { get; init; }
    }

    private sealed class OrderingHomeCollaborativeCandidateResult
    {
        public IReadOnlyDictionary<int, OrderingHomeCollaborativeCandidateApiDto> Candidates { get; init; } = new Dictionary<int, OrderingHomeCollaborativeCandidateApiDto>();
        public string? SignalSource { get; init; }
        public string? FallbackReason { get; init; }
    }

    private sealed class OrderingUserProductScoreResult
    {
        public IReadOnlyDictionary<int, OrderingUserProductScoreApiDto> Scores { get; init; } = new Dictionary<int, OrderingUserProductScoreApiDto>();
    }

    private sealed class OrderingBasketAffinityResult
    {
        public IReadOnlyDictionary<int, OrderingBasketAffinityApiDto> Signals { get; init; } = new Dictionary<int, OrderingBasketAffinityApiDto>();
    }

    private sealed class OrderingUserSellerScoreResult
    {
        public IReadOnlyDictionary<int, OrderingUserSellerScoreApiDto> Scores { get; init; } = new Dictionary<int, OrderingUserSellerScoreApiDto>();
    }

    private sealed class OrderingUserCategoryScoreResult
    {
        public IReadOnlyDictionary<int, OrderingUserCategoryScoreApiDto> Scores { get; init; } = new Dictionary<int, OrderingUserCategoryScoreApiDto>();
    }

    private sealed class OrderingReplenishmentProfileResult
    {
        public IReadOnlyDictionary<int, OrderingReplenishmentProfileApiDto> Profiles { get; init; } = new Dictionary<int, OrderingReplenishmentProfileApiDto>();
    }

    private sealed class OrderingSimilarProductSignalResult
    {
        public IReadOnlyDictionary<int, OrderingSimilarProductSignalApiDto> Signals { get; init; } = new Dictionary<int, OrderingSimilarProductSignalApiDto>();
        public string? SignalSource { get; init; }
        public string? FallbackReason { get; init; }
    }

    private sealed class OrderingSearchRankingSignalResult
    {
        public IReadOnlyDictionary<int, OrderingSearchRankingSignalApiDto> Signals { get; init; } = new Dictionary<int, OrderingSearchRankingSignalApiDto>();
        public string? SignalSource { get; init; }
        public string? FallbackReason { get; init; }
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

    private sealed class LongTermPreferenceProfileDto
    {
        public int? TopCategoryId { get; set; }
        public string? TopCategoryName { get; set; }
        public string? TopOriginRegionKey { get; set; }
        public string? TopOriginLabel { get; set; }
        public bool UsesMaterializedCategoryScore { get; set; }
    }

    private sealed class HomeRecommendationRankedEntryDto
    {
        public CatalogProductApiDto Item { get; init; } = new();
        public HomePreferenceMatchDto? Match { get; init; }
        public OrderingHomeCollaborativeCandidateApiDto? CollaborativeCandidate { get; init; }
        public OrderingUserProductScoreApiDto? UserProductScore { get; init; }
        public double Score { get; init; }
        public double LongTermPreferenceProfileBoost { get; init; }
        public double LongTermSellerBoost { get; init; }
        public double RecentSellerBoost { get; init; }
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

        var merchantBySellerId = await GetMerchantLookupAsync(sellerSummaries.Select(static summary => (int?)summary.SellerId));
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
        IReadOnlyDictionary<int, OrderingHomeCollaborativeCandidateApiDto> collaborativeCandidates,
        IReadOnlyDictionary<int, OrderingUserProductScoreApiDto> userProductScores,
        IReadOnlyDictionary<int, OrderingUserCategoryScoreApiDto> userCategoryScores,
        IReadOnlyDictionary<int, OrderingUserSellerScoreApiDto> userSellerScores)
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
        var longTermPreferenceProfile = BuildLongTermPreferenceProfile(userProductScores, userCategoryScores, candidateLookup);

        var scoredItems = candidates
            .Select(item => new
            {
                Item = item,
                Match = CalculateHomePersonalizationMatch(item, activePreferenceSeeds),
                CollaborativeCandidate = collaborativeCandidates.TryGetValue(item.ProductId, out var collaborativeCandidate)
                    ? collaborativeCandidate
                    : null,
                UserProductScore = userProductScores.TryGetValue(item.ProductId, out var userProductScore)
                    ? userProductScore
                    : null,
                BaseScore = CalculateHomeRecommendationScore(item)
            })
            .Select(entry =>
            {
                var longTermPreferenceProfileBoost = CalculateLongTermPreferenceProfileBoost(entry.Item, longTermPreferenceProfile);
                var longTermSellerBoost = CalculateLongTermUserSellerBoost(entry.Item, userSellerScores);
                var recentSellerBoost = CalculateHomeRecentSellerBoost(entry.Item, userSellerScores);
                return new HomeRecommendationRankedEntryDto
                {
                    Item = entry.Item,
                    Match = entry.Match,
                    CollaborativeCandidate = entry.CollaborativeCandidate,
                    UserProductScore = entry.UserProductScore,
                    LongTermPreferenceProfileBoost = longTermPreferenceProfileBoost,
                    LongTermSellerBoost = longTermSellerBoost,
                    RecentSellerBoost = recentSellerBoost,
                Score = entry.BaseScore
                    + (entry.Match?.Score ?? 0d)
                    + (entry.CollaborativeCandidate?.CollaborativeScore ?? 0d)
                    + (entry.UserProductScore?.UserProductScore ?? 0d)
                    + CalculateLongTermUserProductBoost(entry.UserProductScore)
                    + longTermSellerBoost
                    + recentSellerBoost
                    + longTermPreferenceProfileBoost
                };
            })
            .OrderByDescending(entry => entry.Score)
            .ThenByDescending(entry => entry.CollaborativeCandidate?.CollaborativeScore ?? 0d)
            .ThenByDescending(entry => entry.UserProductScore?.UserProductScore ?? 0d)
            .ThenByDescending(entry => entry.Match?.Score ?? 0d)
            .ThenByDescending(entry => entry.Item.AverageRating)
            .ThenByDescending(entry => entry.Item.SoldCount)
            .ThenByDescending(entry => entry.Item.ProductId)
            .ToList();

        var categoryCounts = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        var sellerCounts = new Dictionary<int, int>();
        var regionCounts = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        var selected = new List<RecommendationProductApiDto>();
        var firstPassCategoryCap = GetHomeFirstPassCategoryCap(limit);
        var firstPassSellerCap = GetHomeFirstPassSellerCap(limit);
        var firstPassRegionCap = GetHomeFirstPassRegionCap(limit);

        foreach (var entry in scoredItems)
        {
            var categoryKey = string.IsNullOrWhiteSpace(entry.Item.CategoryName)
                ? $"category-{entry.Item.CategoryId}"
                : entry.Item.CategoryName!.Trim();
            var regionKey = GetOriginRegionKey(entry.Item.Origin)
                ?? GetAddressRegionKey(entry.Item.SellerAddressSummary)
                ?? $"region-{entry.Item.ProductId}";
            var currentCount = categoryCounts.TryGetValue(categoryKey, out var existingCount) ? existingCount : 0;
            if (currentCount >= firstPassCategoryCap)
            {
                continue;
            }

            if (HasReachedSellerCap(sellerCounts, entry.Item.PrimarySellerId, firstPassSellerCap))
            {
                continue;
            }

            var currentRegionCount = regionCounts.TryGetValue(regionKey, out var existingRegionCount) ? existingRegionCount : 0;
            if (currentRegionCount >= firstPassRegionCap)
            {
                continue;
            }

            categoryCounts[categoryKey] = currentCount + 1;
            IncrementSellerCount(sellerCounts, entry.Item.PrimarySellerId);
            regionCounts[regionKey] = currentRegionCount + 1;
            selected.Add(MapRecommendationProduct(
                entry.Item,
                entry.Score,
                BuildHomeRecommendationReason(
                    entry.Item,
                    entry.Match,
                    entry.CollaborativeCandidate,
                    entry.UserProductScore,
                    entry.Item.PrimarySellerId.HasValue && userSellerScores.TryGetValue(entry.Item.PrimarySellerId.Value, out var userSellerScore)
                        ? userSellerScore
                        : null,
                    longTermPreferenceProfile)));
            if (selected.Count >= limit)
            {
                EnsureLongTermDiscoveryCandidate(selected, scoredItems, userSellerScores, longTermPreferenceProfile, limit);
                EnsureRecentSellerDiscoveryCandidate(selected, scoredItems, userSellerScores, longTermPreferenceProfile, limit);
                EnsureFavoriteSellerDiscoveryCandidate(selected, scoredItems, userSellerScores, longTermPreferenceProfile, limit);
                return selected;
            }
        }

        foreach (var entry in scoredItems
                     .Where(entry => !selected.Any(item => item.ProductId == entry.Item.ProductId))
                     .OrderByDescending(entry => IsHomeSecondPassNewSeller(sellerCounts, entry.Item.PrimarySellerId))
                     .ThenByDescending(entry => IsHomeSecondPassNewRegion(regionCounts, entry.Item))
                     .ThenByDescending(entry => IsHomeSecondPassNewCategory(categoryCounts, entry.Item))
                     .ThenByDescending(entry => entry.Score)
                     .ThenByDescending(entry => entry.CollaborativeCandidate?.CollaborativeScore ?? 0d)
                     .ThenByDescending(entry => entry.Match?.Score ?? 0d)
                     .ThenByDescending(entry => entry.Item.AverageRating)
                     .ThenByDescending(entry => entry.Item.SoldCount)
                     .ThenByDescending(entry => entry.Item.ProductId))
        {
            selected.Add(MapRecommendationProduct(
                entry.Item,
                entry.Score,
                BuildHomeRecommendationReason(
                    entry.Item,
                    entry.Match,
                    entry.CollaborativeCandidate,
                    entry.UserProductScore,
                    entry.Item.PrimarySellerId.HasValue && userSellerScores.TryGetValue(entry.Item.PrimarySellerId.Value, out var userSellerScore)
                        ? userSellerScore
                        : null,
                    longTermPreferenceProfile)));
            if (selected.Count >= limit)
            {
                break;
            }
        }

        EnsureLongTermDiscoveryCandidate(selected, scoredItems, userSellerScores, longTermPreferenceProfile, limit);
        EnsureRecentSellerDiscoveryCandidate(selected, scoredItems, userSellerScores, longTermPreferenceProfile, limit);
        EnsureFavoriteSellerDiscoveryCandidate(selected, scoredItems, userSellerScores, longTermPreferenceProfile, limit);

        return selected;
    }

    private static void EnsureLongTermDiscoveryCandidate(
        List<RecommendationProductApiDto> selected,
        IReadOnlyList<HomeRecommendationRankedEntryDto> scoredItems,
        IReadOnlyDictionary<int, OrderingUserSellerScoreApiDto> userSellerScores,
        LongTermPreferenceProfileDto longTermPreferenceProfile,
        int limit)
    {
        if (limit < 4 || selected.Count == 0)
        {
            return;
        }

        if (longTermPreferenceProfile.TopCategoryId is null && string.IsNullOrWhiteSpace(longTermPreferenceProfile.TopOriginRegionKey))
        {
            return;
        }

        var selectedProductIds = selected.Select(item => item.ProductId).ToHashSet();
        if (scoredItems.Any(entry => selectedProductIds.Contains(entry.Item.ProductId) &&
                                     entry.LongTermPreferenceProfileBoost > 0d &&
                                     entry.UserProductScore is null))
        {
            return;
        }

        var discoveryCandidate = scoredItems
            .FirstOrDefault(entry => entry.LongTermPreferenceProfileBoost > 0d &&
                                     entry.UserProductScore is null &&
                                     selected.All(item => item.ProductId != entry.Item.ProductId));

        if (discoveryCandidate is null)
        {
            return;
        }

        var mappedCandidate = MapRecommendationProduct(
            discoveryCandidate.Item,
            discoveryCandidate.Score,
            BuildHomeRecommendationReason(
                discoveryCandidate.Item,
                discoveryCandidate.Match,
                discoveryCandidate.CollaborativeCandidate,
                discoveryCandidate.UserProductScore,
                discoveryCandidate.Item.PrimarySellerId.HasValue && userSellerScores.TryGetValue(discoveryCandidate.Item.PrimarySellerId.Value, out var userSellerScore)
                    ? userSellerScore
                    : null,
                longTermPreferenceProfile));

        if (selected.Count < limit)
        {
            selected.Add(mappedCandidate);
            return;
        }

        for (var index = selected.Count - 1; index >= 0; index--)
        {
            var current = selected[index];
            var currentEntry = scoredItems.FirstOrDefault(entry => entry.Item.ProductId == current.ProductId);
            if (currentEntry?.UserProductScore is null && currentEntry?.LongTermPreferenceProfileBoost > 0d)
            {
                continue;
            }

            selected[index] = mappedCandidate;
            return;
        }
    }

    private static void EnsureRecentSellerDiscoveryCandidate(
        List<RecommendationProductApiDto> selected,
        IReadOnlyList<HomeRecommendationRankedEntryDto> scoredItems,
        IReadOnlyDictionary<int, OrderingUserSellerScoreApiDto> userSellerScores,
        LongTermPreferenceProfileDto longTermPreferenceProfile,
        int limit)
    {
        if (limit < 4 || selected.Count == 0)
        {
            return;
        }

        var selectedProductIds = selected.Select(item => item.ProductId).ToHashSet();
        if (scoredItems.Any(entry => selectedProductIds.Contains(entry.Item.ProductId) &&
                                     entry.RecentSellerBoost > 0d &&
                                     entry.UserProductScore is null))
        {
            return;
        }

        var recentSellerCandidate = scoredItems
            .FirstOrDefault(entry => entry.RecentSellerBoost > 0d &&
                                     entry.UserProductScore is null &&
                                     selected.All(item => item.ProductId != entry.Item.ProductId));

        if (recentSellerCandidate is null)
        {
            return;
        }

        var mappedCandidate = MapRecommendationProduct(
            recentSellerCandidate.Item,
            recentSellerCandidate.Score,
            BuildHomeRecommendationReason(
                recentSellerCandidate.Item,
                recentSellerCandidate.Match,
                recentSellerCandidate.CollaborativeCandidate,
                recentSellerCandidate.UserProductScore,
                recentSellerCandidate.Item.PrimarySellerId.HasValue && userSellerScores.TryGetValue(recentSellerCandidate.Item.PrimarySellerId.Value, out var userSellerScore)
                    ? userSellerScore
                    : null,
                longTermPreferenceProfile));

        if (selected.Count < limit)
        {
            selected.Add(mappedCandidate);
            return;
        }

        for (var index = selected.Count - 1; index >= 0; index--)
        {
            var current = selected[index];
            var currentEntry = scoredItems.FirstOrDefault(entry => entry.Item.ProductId == current.ProductId);
            if (currentEntry is null)
            {
                continue;
            }

            if ((currentEntry.UserProductScore is null && currentEntry.LongTermPreferenceProfileBoost > 0d)
                || (currentEntry.UserProductScore is null && currentEntry.RecentSellerBoost > 0d))
            {
                continue;
            }

            selected[index] = mappedCandidate;
            return;
        }
    }

    private static void EnsureFavoriteSellerDiscoveryCandidate(
        List<RecommendationProductApiDto> selected,
        IReadOnlyList<HomeRecommendationRankedEntryDto> scoredItems,
        IReadOnlyDictionary<int, OrderingUserSellerScoreApiDto> userSellerScores,
        LongTermPreferenceProfileDto longTermPreferenceProfile,
        int limit)
    {
        if (limit < 4 || selected.Count == 0)
        {
            return;
        }

        var selectedProductIds = selected.Select(item => item.ProductId).ToHashSet();
        if (scoredItems.Any(entry => selectedProductIds.Contains(entry.Item.ProductId) &&
                                     entry.LongTermSellerBoost > 0d &&
                                     entry.RecentSellerBoost <= 0d &&
                                     entry.UserProductScore is null))
        {
            return;
        }

        var favoriteSellerCandidate = scoredItems
            .FirstOrDefault(entry => entry.LongTermSellerBoost > 0d &&
                                     entry.RecentSellerBoost <= 0d &&
                                     entry.UserProductScore is null &&
                                     selected.All(item => item.ProductId != entry.Item.ProductId));

        if (favoriteSellerCandidate is null)
        {
            return;
        }

        var mappedCandidate = MapRecommendationProduct(
            favoriteSellerCandidate.Item,
            favoriteSellerCandidate.Score,
            BuildHomeRecommendationReason(
                favoriteSellerCandidate.Item,
                favoriteSellerCandidate.Match,
                favoriteSellerCandidate.CollaborativeCandidate,
                favoriteSellerCandidate.UserProductScore,
                favoriteSellerCandidate.Item.PrimarySellerId.HasValue && userSellerScores.TryGetValue(favoriteSellerCandidate.Item.PrimarySellerId.Value, out var userSellerScore)
                    ? userSellerScore
                    : null,
                longTermPreferenceProfile));

        if (selected.Count < limit)
        {
            selected.Add(mappedCandidate);
            return;
        }

        for (var index = selected.Count - 1; index >= 0; index--)
        {
            var current = selected[index];
            var currentEntry = scoredItems.FirstOrDefault(entry => entry.Item.ProductId == current.ProductId);
            if (currentEntry is not null
                && currentEntry.UserProductScore is null
                && (currentEntry.LongTermPreferenceProfileBoost > 0d
                    || currentEntry.RecentSellerBoost > 0d
                    || currentEntry.LongTermSellerBoost > 0d))
            {
                continue;
            }

            selected[index] = mappedCandidate;
            return;
        }
    }

    private static List<RecommendationSectionApiDto> BuildHomeRecommendationSections(
        IReadOnlyList<CatalogProductApiDto> allItems,
        IReadOnlyList<RecommendationProductApiDto> rankedItems,
        IReadOnlyDictionary<int, PurchaseHistorySeedApiDto> purchaseHistorySeeds,
        IReadOnlyDictionary<int, OrderingUserSellerScoreApiDto> userSellerScores,
        IReadOnlyDictionary<int, OrderingBasketAffinityApiDto> basketAffinities,
        IReadOnlyDictionary<int, OrderingReplenishmentProfileApiDto> replenishmentProfiles,
        bool hasPersonalSignals)
    {
        var sections = new List<RecommendationSectionApiDto>();
        if (allItems.Count == 0 && rankedItems.Count == 0)
        {
            return sections;
        }

        var rankedItemLookup = rankedItems.ToDictionary(item => item.ProductId);
        var rebuyItems = BuildBuyAgainCandidates(allItems, rankedItemLookup, purchaseHistorySeeds)
            .OrderByDescending(candidate => candidate.SortScore)
            .ThenByDescending(candidate => candidate.Seed.PurchaseCount)
            .ThenByDescending(candidate => candidate.Seed.LastInteractedAtUtc ?? DateTime.MinValue)
            .ThenByDescending(candidate => candidate.Seed.PreferenceScore)
            .ThenByDescending(candidate => candidate.Item.RecommendationScore)
            .Take(6)
            .ToList();

        var rebuyIds = rebuyItems.Select(candidate => candidate.Item.ProductId).ToHashSet();
        var primaryItems = rankedItems
            .Where(item => !rebuyIds.Contains(item.ProductId))
            .Take(6)
            .ToList();

        if (primaryItems.Count == 0)
        {
            primaryItems = rankedItems.Take(6).ToList();
        }

        sections.Add(new RecommendationSectionApiDto
        {
            Id = hasPersonalSignals ? "for_you" : "today_highlights",
            Title = hasPersonalSignals ? "Dành cho bạn" : "Gợi ý cho bạn hôm nay",
            Subtitle = hasPersonalSignals
                ? "Dựa trên sản phẩm bạn vừa xem, tìm kiếm và chọn gần đây."
                : "Chọn từ danh sách nổi bật, đúng mùa và hợp xu hướng hôm nay.",
            PillLabel = hasPersonalSignals ? "Cá nhân hóa" : "Nổi bật hôm nay",
            Items = primaryItems
        });

        if (rebuyItems.Count > 0)
        {
            var rebuySectionItems = rebuyItems
                .Select(candidate => CreateBuyAgainSectionItem(candidate.Item, candidate.Seed))
                .ToList();
            sections.Add(new RecommendationSectionApiDto
            {
                Id = "buy_again",
                Title = "Mua lại từ lịch sử",
                Subtitle = BuildBuyAgainSectionSubtitle(rebuyItems),
                PillLabel = "Mua nhanh lần nữa",
                Items = rebuySectionItems
            });
        }

        var favoriteShopSignals = BuildFavoriteShopSignals(allItems, purchaseHistorySeeds, userSellerScores);
        var favoriteShopCandidates = BuildFavoriteShopCandidates(allItems, rankedItemLookup, purchaseHistorySeeds, favoriteShopSignals)
            .OrderByDescending(candidate => !candidate.IsPastPurchase)
            .ThenByDescending(candidate => candidate.SortScore)
            .ThenByDescending(candidate => candidate.Seller.SellerAffinityScore)
            .ThenByDescending(candidate => candidate.Item.RecommendationScore)
            .ToList();

        var favoriteShopUsedIds = sections
            .SelectMany(section => section.Items)
            .Select(item => item.ProductId)
            .ToHashSet();
        var recentShopItems = favoriteShopCandidates
            .Where(candidate => IsRecentSellerInteraction(candidate.Seller.LastInteractedAtUtc))
            .Where(candidate => !favoriteShopUsedIds.Contains(candidate.Item.ProductId))
            .Take(6)
            .Select(candidate => CreateFavoriteShopSectionItem(candidate.Item, candidate.Seller))
            .ToList();

        if (recentShopItems.Count == 0)
        {
            recentShopItems = favoriteShopCandidates
                .Where(candidate => IsRecentSellerInteraction(candidate.Seller.LastInteractedAtUtc))
                .Take(6)
                .Select(candidate => CreateFavoriteShopSectionItem(candidate.Item, candidate.Seller))
                .ToList();
        }

        if (recentShopItems.Count > 0)
        {
            sections.Add(new RecommendationSectionApiDto
            {
                Id = "recent_shop",
                Title = "Từ shop bạn vừa quay lại",
                Subtitle = BuildRecentShopSectionSubtitle(recentShopItems.Count),
                PillLabel = "Shop vừa ghé",
                Items = recentShopItems
            });
            favoriteShopUsedIds = sections
                .SelectMany(section => section.Items)
                .Select(item => item.ProductId)
                .ToHashSet();
        }

        var recentShopSellerIds = recentShopItems
            .Select(item => item.PrimarySellerId)
            .Where(static sellerId => sellerId.HasValue && sellerId.Value > 0)
            .Select(static sellerId => sellerId!.Value)
            .ToHashSet();

        var favoriteShopItems = BuildFavoriteShopSectionItems(
            favoriteShopCandidates,
            favoriteShopUsedIds,
            recentShopSellerIds,
            6);

        if (favoriteShopItems.Count == 0)
        {
            favoriteShopItems = BuildFavoriteShopSectionItems(
                favoriteShopCandidates,
                null,
                recentShopSellerIds,
                6);
        }

        if (favoriteShopItems.Count == 0)
        {
            favoriteShopItems = BuildFavoriteShopSectionItems(
                favoriteShopCandidates,
                null,
                null,
                6);
        }

        if (favoriteShopItems.Count > 0)
        {
            var recentFavoriteShopCount = favoriteShopSignals.Values.Count(signal => IsRecentSellerInteraction(signal.LastInteractedAtUtc));
            sections.Add(new RecommendationSectionApiDto
            {
                Id = "favorite_shop",
                Title = "Từ shop bạn hay mua",
                Subtitle = BuildFavoriteShopSectionSubtitle(favoriteShopItems.Count, favoriteShopSignals.Count, recentFavoriteShopCount),
                PillLabel = "Shop quen thuộc",
                Items = favoriteShopItems
            });
        }

        var buyWithHistoryItems = BuildBuyWithHistoryCandidates(allItems, rankedItemLookup, purchaseHistorySeeds, basketAffinities)
            .Where(candidate => !sections.SelectMany(section => section.Items).Any(item => item.ProductId == candidate.Item.ProductId))
            .OrderByDescending(candidate => candidate.SortScore)
            .ThenByDescending(candidate => candidate.Signal.BasketScore)
            .ThenByDescending(candidate => candidate.Item.RecommendationScore)
            .Take(6)
            .Select(candidate => CreateBuyWithHistorySectionItem(candidate.Item, candidate.Signal, candidate.Seed))
            .ToList();

        if (buyWithHistoryItems.Count > 0)
        {
            sections.Add(new RecommendationSectionApiDto
            {
                Id = "buy_with_history",
                Title = "Mua kèm từ lịch sử",
                Subtitle = BuildBuyWithHistorySectionSubtitle(buyWithHistoryItems.Count),
                PillLabel = "Hay mua cùng",
                Items = buyWithHistoryItems
            });
        }

        var replenishCandidates = BuildReplenishmentCandidates(allItems, rankedItemLookup, replenishmentProfiles)
            .OrderByDescending(candidate => candidate.SortScore)
            .ThenByDescending(candidate => candidate.Profile.ReplenishmentScore)
            .ThenByDescending(candidate => candidate.Item.RecommendationScore)
            .ToList();
        var usedSectionIds = sections
            .SelectMany(section => section.Items)
            .Select(item => item.ProductId)
            .ToHashSet();
        var replenishItems = replenishCandidates
            .Where(candidate => !usedSectionIds.Contains(candidate.Item.ProductId))
            .Take(6)
            .Select(candidate => CreateReplenishmentSectionItem(candidate.Item, candidate.Profile))
            .ToList();

        if (replenishItems.Count == 0)
        {
            replenishItems = replenishCandidates
                .Take(6)
                .Select(candidate => CreateReplenishmentSectionItem(candidate.Item, candidate.Profile))
                .ToList();
        }

        if (replenishItems.Count > 0)
        {
            sections.Add(new RecommendationSectionApiDto
            {
                Id = "replenish_soon",
                Title = "Đến kỳ mua lại",
                Subtitle = BuildReplenishmentSectionSubtitle(replenishItems.Count),
                PillLabel = "Sắp cần mua thêm",
                Items = replenishItems
            });
        }

        var usedIds = sections
            .SelectMany(section => section.Items)
            .Select(item => item.ProductId)
            .ToHashSet();

        var seasonalLocalItems = rankedItems
            .Where(item => !usedIds.Contains(item.ProductId))
            .Where(IsSeasonalOrLocalRecommendation)
            .Take(6)
            .ToList();

        if (seasonalLocalItems.Count > 0)
        {
            sections.Add(new RecommendationSectionApiDto
            {
                Id = "seasonal_local",
                Title = "Đúng mùa gần bạn",
                Subtitle = "Ưu tiên nông sản đang đúng mùa, đúng vùng trồng hoặc cùng khu vực giao nhanh.",
                PillLabel = "Theo mùa & địa phương",
                Items = seasonalLocalItems
            });
        }

        return sections;
    }

    private static List<BuyAgainCandidateDto> BuildBuyAgainCandidates(
        IReadOnlyList<CatalogProductApiDto> allItems,
        IReadOnlyDictionary<int, RecommendationProductApiDto> rankedItemLookup,
        IReadOnlyDictionary<int, PurchaseHistorySeedApiDto> purchaseHistorySeeds)
    {
        if (purchaseHistorySeeds.Count == 0)
        {
            return new List<BuyAgainCandidateDto>();
        }

        var itemLookup = allItems
            .Where(IsRecommendationCandidate)
            .GroupBy(item => item.ProductId)
            .ToDictionary(group => group.Key, group => group.First());

        var candidates = new List<BuyAgainCandidateDto>();
        foreach (var seed in purchaseHistorySeeds.Values.Where(seed => seed.ProductId > 0 && seed.PurchaseCount > 0))
        {
            if (rankedItemLookup.TryGetValue(seed.ProductId, out var rankedItem))
            {
                if (itemLookup.TryGetValue(seed.ProductId, out var rankedCatalogItem))
                {
                    rankedItem = MergeRecommendationItemForBuyAgain(rankedItem, rankedCatalogItem);
                }

                candidates.Add(new BuyAgainCandidateDto
                {
                    Item = rankedItem,
                    Seed = seed,
                    SortScore = CalculateBuyAgainSortScore(rankedItem, seed)
                });
                continue;
            }

            if (!itemLookup.TryGetValue(seed.ProductId, out var item))
            {
                continue;
            }

            candidates.Add(new BuyAgainCandidateDto
            {
                Item = MapRecommendationProduct(
                    item,
                    CalculateHomeRecommendationScore(item) + seed.PreferenceScore + (seed.PurchaseCount * 20d),
                    new RecommendationReasonDto
                    {
                        Text = string.Empty,
                        Tags = Array.Empty<string>()
                    }),
                Seed = seed,
                SortScore = CalculateBuyAgainSortScore(item, seed)
            });
        }

        return candidates;
    }

    private static IReadOnlyDictionary<int, FavoriteShopSignalDto> BuildFavoriteShopSignals(
        IReadOnlyList<CatalogProductApiDto> allItems,
        IReadOnlyDictionary<int, PurchaseHistorySeedApiDto> purchaseHistorySeeds,
        IReadOnlyDictionary<int, OrderingUserSellerScoreApiDto> userSellerScores)
    {
        if (purchaseHistorySeeds.Count == 0 && userSellerScores.Count == 0)
        {
            return new Dictionary<int, FavoriteShopSignalDto>();
        }

        var itemLookup = allItems
            .Where(item => item.ProductId > 0 && item.PrimarySellerId.HasValue && item.PrimarySellerId.Value > 0)
            .GroupBy(item => item.ProductId)
            .ToDictionary(group => group.Key, group => group.First());
        var grouped = new Dictionary<int, FavoriteShopSignalDto>();

        foreach (var seed in purchaseHistorySeeds.Values.Where(seed => seed.ProductId > 0 && seed.PurchaseCount > 0))
        {
            if (!itemLookup.TryGetValue(seed.ProductId, out var item)
                || !item.PrimarySellerId.HasValue
                || item.PrimarySellerId.Value <= 0)
            {
                continue;
            }

            var sellerId = item.PrimarySellerId.Value;
            if (!grouped.TryGetValue(sellerId, out var signal))
            {
                signal = new FavoriteShopSignalDto
                {
                    SellerId = sellerId,
                    SellerShopName = item.SellerShopName ?? $"FreshFarm Seller {sellerId}",
                    SellerAddressSummary = item.SellerAddressSummary,
                    PurchaseCount = 0,
                    SearchClickCount = 0,
                    ViewCount = 0,
                    DistinctPurchasedProductCount = 0,
                    SellerAffinityScore = 0d,
                    LastInteractedAtUtc = null
                };
                grouped[sellerId] = signal;
            }

            signal.PurchaseCount += Math.Max(seed.PurchaseCount, 1);
            signal.DistinctPurchasedProductCount += 1;
            signal.SellerAffinityScore += seed.PreferenceScore + (seed.PurchaseCount * 28d);
            signal.LastInteractedAtUtc = GetLatestInteraction(signal.LastInteractedAtUtc, seed.LastInteractedAtUtc);
        }

        foreach (var sellerScore in userSellerScores.Values.Where(score => score.SellerId > 0 && score.UserSellerScore > 0d))
        {
            if (!grouped.TryGetValue(sellerScore.SellerId, out var signal))
            {
                var item = allItems.FirstOrDefault(candidate => candidate.PrimarySellerId == sellerScore.SellerId);
                signal = new FavoriteShopSignalDto
                {
                    SellerId = sellerScore.SellerId,
                    SellerShopName = item?.SellerShopName ?? $"FreshFarm Seller {sellerScore.SellerId}",
                    SellerAddressSummary = item?.SellerAddressSummary,
                    PurchaseCount = 0,
                    SearchClickCount = 0,
                    ViewCount = 0,
                    DistinctPurchasedProductCount = 0,
                    SellerAffinityScore = 0d,
                    LastInteractedAtUtc = null
                };
                grouped[sellerScore.SellerId] = signal;
            }

            signal.SearchClickCount = Math.Max(signal.SearchClickCount, sellerScore.SearchClickCount);
            signal.ViewCount = Math.Max(signal.ViewCount, sellerScore.ViewCount);
            signal.SellerAffinityScore += sellerScore.UserSellerScore + CalculateLongTermUserSellerSignalBoost(sellerScore);
            signal.LastInteractedAtUtc = GetLatestInteraction(signal.LastInteractedAtUtc, sellerScore.LastInteractedAtUtc);
        }

        return grouped;
    }

    private static List<FavoriteShopCandidateDto> BuildFavoriteShopCandidates(
        IReadOnlyList<CatalogProductApiDto> allItems,
        IReadOnlyDictionary<int, RecommendationProductApiDto> rankedItemLookup,
        IReadOnlyDictionary<int, PurchaseHistorySeedApiDto> purchaseHistorySeeds,
        IReadOnlyDictionary<int, FavoriteShopSignalDto> favoriteShopSignals)
    {
        if (favoriteShopSignals.Count == 0)
        {
            return new List<FavoriteShopCandidateDto>();
        }

        var purchaseProductIds = purchaseHistorySeeds.Keys.ToHashSet();
        var candidates = new List<FavoriteShopCandidateDto>();

        foreach (var catalogItem in allItems.Where(IsRecommendationCandidate))
        {
            if (!catalogItem.PrimarySellerId.HasValue
                || catalogItem.PrimarySellerId.Value <= 0
                || !favoriteShopSignals.TryGetValue(catalogItem.PrimarySellerId.Value, out var sellerSignal))
            {
                continue;
            }

            rankedItemLookup.TryGetValue(catalogItem.ProductId, out var rankedItem);
            var baseItem = rankedItem is not null
                ? MergeRecommendationItemForBuyAgain(rankedItem, catalogItem)
                : MapRecommendationProduct(
                    catalogItem,
                    CalculateHomeRecommendationScore(catalogItem) + sellerSignal.SellerAffinityScore,
                    new RecommendationReasonDto { Text = string.Empty, Tags = Array.Empty<string>() });

            var isPastPurchase = purchaseProductIds.Contains(catalogItem.ProductId);
            var sortScore =
                sellerSignal.SellerAffinityScore
                + CalculateFavoriteShopRecencyBoost(sellerSignal)
                + baseItem.RecommendationScore
                + (baseItem.AvailableStock > 0 ? 12d : -28d)
                + (baseItem.AverageRating >= 4m ? 8d : 0d)
                + (baseItem.SoldCount > 0 ? Math.Log10(baseItem.SoldCount + 1) * 6d : 0d)
                + (IsOriginAlignedWithSellerRegion(baseItem) ? 8d : 0d)
                + (HasFastReorderDeliverySignal(baseItem.SellerAddressSummary) ? 4d : 0d)
                + Math.Min(sellerSignal.DistinctPurchasedProductCount * 3d, 12d)
                - (isPastPurchase ? 18d : 0d);

            candidates.Add(new FavoriteShopCandidateDto
            {
                Item = baseItem,
                Seller = sellerSignal,
                IsPastPurchase = isPastPurchase,
                SortScore = sortScore
            });
        }

        return candidates
            .GroupBy(candidate => candidate.Item.ProductId)
            .Select(group => group
                .OrderByDescending(candidate => !candidate.IsPastPurchase)
                .ThenByDescending(candidate => candidate.SortScore)
                .ThenByDescending(candidate => candidate.Seller.LastInteractedAtUtc ?? DateTime.MinValue)
                .ThenByDescending(candidate => candidate.Seller.SellerAffinityScore)
                .First())
            .ToList();
    }

    private static List<BuyWithHistoryCandidateDto> BuildBuyWithHistoryCandidates(
        IReadOnlyList<CatalogProductApiDto> allItems,
        IReadOnlyDictionary<int, RecommendationProductApiDto> rankedItemLookup,
        IReadOnlyDictionary<int, PurchaseHistorySeedApiDto> purchaseHistorySeeds,
        IReadOnlyDictionary<int, OrderingBasketAffinityApiDto> basketAffinities)
    {
        if (basketAffinities.Count == 0)
        {
            return new List<BuyWithHistoryCandidateDto>();
        }

        var itemLookup = allItems
            .Where(IsRecommendationCandidate)
            .GroupBy(item => item.ProductId)
            .ToDictionary(group => group.Key, group => group.First());
        var seedLookup = purchaseHistorySeeds;
        var candidates = new List<BuyWithHistoryCandidateDto>();

        foreach (var signal in basketAffinities.Values
                     .Where(signal => signal.CandidateProductId > 0 && signal.BasketScore > 0d)
                     .OrderByDescending(signal => signal.BasketScore)
                     .ThenByDescending(signal => signal.CoPurchaseOrderCount))
        {
            if (!itemLookup.TryGetValue(signal.CandidateProductId, out var catalogItem))
            {
                continue;
            }

            rankedItemLookup.TryGetValue(signal.CandidateProductId, out var rankedItem);
            var baseItem = rankedItem is not null
                ? MergeRecommendationItemForBuyAgain(rankedItem, catalogItem)
                : MapRecommendationProduct(
                    catalogItem,
                    CalculateHomeRecommendationScore(catalogItem) + signal.BasketScore,
                    new RecommendationReasonDto { Text = string.Empty, Tags = Array.Empty<string>() });

            seedLookup.TryGetValue(signal.ProductId, out var seed);
            var sortScore =
                signal.BasketScore
                + (seed?.PurchaseCount ?? 0) * 16d
                + (baseItem.AvailableStock > 0 ? 12d : -24d)
                + (baseItem.AverageRating >= 4 ? 8d : 0d)
                + (baseItem.SoldCount > 0 ? Math.Log10(baseItem.SoldCount + 1) * 6d : 0d);

            candidates.Add(new BuyWithHistoryCandidateDto
            {
                Item = baseItem,
                Signal = signal,
                Seed = seed,
                SortScore = sortScore
            });
        }

        return candidates
            .GroupBy(candidate => candidate.Item.ProductId)
            .Select(group => group
                .OrderByDescending(candidate => candidate.SortScore)
                .ThenByDescending(candidate => candidate.Signal.BasketScore)
                .ThenByDescending(candidate => candidate.Signal.CoPurchaseOrderCount)
                .First())
            .ToList();
    }

    private static List<ReplenishmentCandidateDto> BuildReplenishmentCandidates(
        IReadOnlyList<CatalogProductApiDto> allItems,
        IReadOnlyDictionary<int, RecommendationProductApiDto> rankedItemLookup,
        IReadOnlyDictionary<int, OrderingReplenishmentProfileApiDto> replenishmentProfiles)
    {
        if (replenishmentProfiles.Count == 0)
        {
            return new List<ReplenishmentCandidateDto>();
        }

        var itemLookup = allItems
            .Where(IsRecommendationCandidate)
            .GroupBy(item => item.ProductId)
            .ToDictionary(group => group.Key, group => group.First());
        var candidates = new List<ReplenishmentCandidateDto>();

        foreach (var profile in replenishmentProfiles.Values
                     .Where(profile => profile.ProductId > 0 && profile.ReplenishmentScore > 0d)
                     .OrderByDescending(profile => profile.ReplenishmentScore)
                     .ThenByDescending(profile => profile.PurchaseCount))
        {
            if (!itemLookup.TryGetValue(profile.ProductId, out var catalogItem))
            {
                continue;
            }

            rankedItemLookup.TryGetValue(profile.ProductId, out var rankedItem);
            var baseItem = rankedItem is not null
                ? MergeRecommendationItemForBuyAgain(rankedItem, catalogItem)
                : MapRecommendationProduct(
                    catalogItem,
                    CalculateHomeRecommendationScore(catalogItem) + profile.ReplenishmentScore,
                    new RecommendationReasonDto { Text = string.Empty, Tags = Array.Empty<string>() });

            var sortScore =
                profile.ReplenishmentScore
                + (baseItem.AvailableStock > 0 ? 12d : -24d)
                + (baseItem.AverageRating >= 4 ? 8d : 0d)
                + (baseItem.SoldCount > 0 ? Math.Log10(baseItem.SoldCount + 1) * 6d : 0d)
                + CalculateReplenishmentClarityBoost(profile);

            candidates.Add(new ReplenishmentCandidateDto
            {
                Item = baseItem,
                Profile = profile,
                SortScore = sortScore
            });
        }

        return candidates;
    }

    private static IReadOnlyDictionary<int, PurchaseHistorySeedApiDto> BuildPurchaseHistorySeeds(
        IReadOnlyDictionary<int, OrderingHomePreferenceSeedApiDto> preferenceSeeds,
        IReadOnlyDictionary<int, OrderingUserProductScoreApiDto> userProductScores)
    {
        var merged = new Dictionary<int, PurchaseHistorySeedApiDto>();

        foreach (var seed in preferenceSeeds.Values.Where(seed => seed.ProductId > 0 && seed.PurchaseCount > 0))
        {
            merged[seed.ProductId] = new PurchaseHistorySeedApiDto
            {
                ProductId = seed.ProductId,
                PurchaseCount = seed.PurchaseCount,
                PreferenceScore = seed.PreferenceScore,
                LastInteractedAtUtc = seed.LastInteractedAtUtc
            };
        }

        foreach (var score in userProductScores.Values.Where(score => score.ProductId > 0 && score.PurchaseCount > 0))
        {
            if (!merged.TryGetValue(score.ProductId, out var existing))
            {
                merged[score.ProductId] = new PurchaseHistorySeedApiDto
                {
                    ProductId = score.ProductId,
                    PurchaseCount = score.PurchaseCount,
                    PreferenceScore = score.UserProductScore,
                    LastInteractedAtUtc = score.LastInteractedAtUtc
                };
                continue;
            }

            existing.PurchaseCount = Math.Max(existing.PurchaseCount, score.PurchaseCount);
            existing.PreferenceScore = Math.Max(existing.PreferenceScore, score.UserProductScore);
            existing.LastInteractedAtUtc = GetLatestInteraction(existing.LastInteractedAtUtc, score.LastInteractedAtUtc);
        }

        return merged;
    }

    private static DateTime? GetLatestInteraction(DateTime? current, DateTime? candidate)
    {
        if (!current.HasValue)
        {
            return candidate;
        }

        if (!candidate.HasValue)
        {
            return current;
        }

        return candidate > current ? candidate : current;
    }

    private static double CalculateBuyAgainSortScore(
        RecommendationProductApiDto item,
        PurchaseHistorySeedApiDto seed)
    {
        var score = (seed.PurchaseCount * 100d) + seed.PreferenceScore;
        score += item.AvailableStock switch
        {
            > 20 => 28d,
            > 5 => 18d,
            > 0 => 10d,
            _ => -30d
        };
        score += Math.Log10(item.SoldCount + 1) * 8d;
        score += (double)item.AverageRating * 4d;

        if (seed.LastInteractedAtUtc.HasValue)
        {
            var daysAgo = (DateTime.UtcNow - seed.LastInteractedAtUtc.Value).TotalDays;
            if (daysAgo <= BuyAgainRecentWindowDays)
            {
                score += 120d;
            }
            else if (daysAgo <= 90d)
            {
                score += 40d;
            }
        }

        if (IsOriginAlignedWithSellerRegion(item))
        {
            score += 18d;
        }

        if (HasFastReorderDeliverySignal(item.SellerAddressSummary))
        {
            score += 10d;
        }

        return score;
    }

    private static double CalculateBuyAgainSortScore(
        CatalogProductApiDto item,
        PurchaseHistorySeedApiDto seed)
    {
        return CalculateBuyAgainSortScore(
            new RecommendationProductApiDto
            {
                ProductId = item.ProductId,
                AverageRating = item.AverageRating,
                SoldCount = item.SoldCount,
                AvailableStock = item.AvailableStock,
                Origin = item.Origin,
                SellerAddressSummary = item.SellerAddressSummary
            },
            seed);
    }

    private static string BuildBuyAgainSectionSubtitle(IReadOnlyList<BuyAgainCandidateDto> items)
    {
        var itemCount = items.Count;
        var hasRecentPurchase = items.Any(item =>
            item.Seed.LastInteractedAtUtc.HasValue
            && (DateTime.UtcNow - item.Seed.LastInteractedAtUtc.Value).TotalDays <= BuyAgainRecentWindowDays);

        if (hasRecentPurchase)
        {
            return itemCount switch
            {
                <= 0 => "Những món bạn mua gần đây vẫn đang sẵn để đặt lại nhanh.",
                1 => "Món bạn mua gần đây vẫn đang sẵn để đặt lại nhanh hôm nay.",
                _ => $"{itemCount} món bạn mua gần đây vẫn đang sẵn để đặt lại nhanh hôm nay."
            };
        }

        return itemCount switch
        {
            <= 0 => "Những món bạn từng mua và hiện vẫn đang sẵn để đặt lại nhanh.",
            1 => "Món bạn từng mua vẫn đang sẵn để đặt lại nhanh hôm nay.",
            _ => $"{itemCount} món bạn từng mua vẫn đang sẵn để đặt lại nhanh hôm nay."
        };
    }

    private static RecommendationProductApiDto CreateBuyAgainSectionItem(
        RecommendationProductApiDto item,
        PurchaseHistorySeedApiDto seed)
    {
        var tags = new List<string>(4);
        if (seed.PurchaseCount > 1)
        {
            tags.Add($"Bạn đã mua {seed.PurchaseCount} lần");
        }
        else
        {
            tags.Add("Bạn đã mua trước đây");
        }

        if (seed.LastInteractedAtUtc.HasValue
            && (DateTime.UtcNow - seed.LastInteractedAtUtc.Value).TotalDays <= BuyAgainRecentWindowDays)
        {
            tags.Add("Mới mua gần đây");
        }

        if (item.AvailableStock > 0)
        {
            tags.Add("Sẵn để đặt lại ngay");
        }

        if (IsOriginAlignedWithSellerRegion(item))
        {
            tags.Add("Cùng vùng trồng & giao");
        }
        else if (HasFastReorderDeliverySignal(item.SellerAddressSummary))
        {
            tags.Add("Hợp giao nhanh khi đặt lại");
        }

        if (!string.IsNullOrWhiteSpace(item.SellerShopName))
        {
            tags.Add("Từ shop bạn từng đặt");
        }

        if (item.SoldCount > 0)
        {
            tags.Add("Vẫn đang bán tốt");
        }

        if (item.AvailableStock > 0 && !tags.Contains("Sẵn để đặt lại ngay", StringComparer.OrdinalIgnoreCase))
        {
            tags.Add("Có thể đặt lại nhanh");
        }

        if (item.AverageRating >= 4)
        {
            tags.Add("Đánh giá tốt");
        }

        return CloneRecommendationItem(item, string.Join(" · ", tags.Take(3)), tags);
    }

    private static string BuildBuyWithHistorySectionSubtitle(int itemCount)
    {
        return itemCount switch
        {
            <= 0 => "Những món thường đi cùng đơn trước của bạn để thêm nhanh vào giỏ.",
            1 => "Một món thường đi cùng đơn trước của bạn đang sẵn để thêm nhanh.",
            _ => $"{itemCount} món thường đi cùng đơn trước của bạn đang sẵn để thêm nhanh."
        };
    }

    private static string BuildFavoriteShopSectionSubtitle(int itemCount, int shopCount, int recentShopCount)
    {
        var emphasizeRecent = recentShopCount > 0;
        if (shopCount <= 1)
        {
            return itemCount switch
            {
                <= 0 => emphasizeRecent
                    ? "Những món nổi bật từ shop bạn vừa quay lại gần đây."
                    : "Những món nổi bật từ shop bạn từng quay lại nhiều lần.",
                1 => emphasizeRecent
                    ? "Một món nổi bật từ shop bạn vừa quay lại gần đây."
                    : "Một món nổi bật từ shop bạn đã quay lại nhiều lần.",
                _ => emphasizeRecent
                    ? $"{itemCount} món nổi bật từ shop bạn vừa quay lại gần đây."
                    : $"{itemCount} món nổi bật từ shop bạn đã quay lại nhiều lần."
            };
        }

        return itemCount switch
        {
            <= 0 => emphasizeRecent
                ? "Những món nổi bật từ các shop bạn vừa quay lại gần đây."
                : "Những món nổi bật từ các shop bạn hay quay lại.",
            1 => emphasizeRecent
                ? "Một món nổi bật từ các shop bạn vừa quay lại gần đây."
                : "Một món nổi bật từ các shop bạn hay quay lại.",
            _ => emphasizeRecent
                ? $"{itemCount} món nổi bật từ {recentShopCount} shop bạn vừa quay lại gần đây."
                : $"{itemCount} món nổi bật từ {shopCount} shop bạn hay quay lại."
        };
    }

    private static string BuildRecentShopSectionSubtitle(int itemCount)
    {
        return itemCount switch
        {
            <= 0 => "Những món nổi bật từ shop bạn vừa quay lại gần đây.",
            1 => "Một món nổi bật từ shop bạn vừa quay lại gần đây.",
            _ => $"{itemCount} món nổi bật từ shop bạn vừa quay lại gần đây."
        };
    }

    private static RecommendationProductApiDto CreateBuyWithHistorySectionItem(
        RecommendationProductApiDto item,
        OrderingBasketAffinityApiDto signal,
        PurchaseHistorySeedApiDto? seed)
    {
        var tags = new List<string>(6);
        if (signal.CoPurchaseOrderCount > 1)
        {
            tags.Add($"Hay mua cùng {signal.CoPurchaseOrderCount} đơn");
        }
        else
        {
            tags.Add("Hay mua cùng đơn trước");
        }

        if (seed is not null && seed.PurchaseCount > 0)
        {
            tags.Add("Đi cùng món bạn từng mua");
        }

        if (item.AvailableStock > 0)
        {
            tags.Add("Sẵn để thêm vào giỏ");
        }

        if (IsOriginAlignedWithSellerRegion(item))
        {
            tags.Add("Cùng vùng trồng & giao");
        }
        else if (HasFastReorderDeliverySignal(item.SellerAddressSummary))
        {
            tags.Add("Hợp giao nhanh cùng đơn");
        }

        if (!string.IsNullOrWhiteSpace(item.SellerShopName))
        {
            tags.Add("Từ shop bạn hay mua");
        }

        if (item.AverageRating >= 4)
        {
            tags.Add("Đánh giá tốt");
        }

        return CloneRecommendationItem(item, string.Join(" · ", tags.Take(3)), tags);
    }

    private static RecommendationProductApiDto CreateFavoriteShopSectionItem(
        RecommendationProductApiDto item,
        FavoriteShopSignalDto seller)
    {
        var tags = new List<string>(6);
        tags.Add(IsRecentSellerInteraction(seller.LastInteractedAtUtc)
            ? "Từ shop bạn vừa quay lại"
            : "Từ shop bạn hay mua");

        if (seller.DistinctPurchasedProductCount > 1)
        {
            tags.Add($"Bạn đã mua {seller.DistinctPurchasedProductCount} món từ shop này");
        }
        else
        {
            tags.Add("Bạn đã quay lại shop này");
        }

        if (seller.LastInteractedAtUtc.HasValue)
        {
            var daysAgo = Math.Max(0, (int)Math.Floor((DateTime.UtcNow - seller.LastInteractedAtUtc.Value).TotalDays));
            if (daysAgo <= 14)
            {
                tags.Add(daysAgo <= 1 ? "Bạn vừa ghé shop này gần đây" : $"Bạn đã quay lại {daysAgo} ngày trước");
            }
        }

        if (item.AvailableStock > 0)
        {
            tags.Add("Sẵn để thêm vào giỏ");
        }

        if (IsOriginAlignedWithSellerRegion(item))
        {
            tags.Add("Cùng vùng trồng & giao");
        }
        else if (HasFastReorderDeliverySignal(item.SellerAddressSummary))
        {
            tags.Add("Hợp giao nhanh từ shop quen");
        }

        if (item.AverageRating >= 4m)
        {
            tags.Add("Đánh giá tốt");
        }

        if (item.SoldCount > 0)
        {
            tags.Add("Vẫn đang bán tốt");
        }

        return CloneRecommendationItem(item, string.Join(" · ", tags.Take(3)), tags);
    }

    private static string BuildReplenishmentSectionSubtitle(int itemCount)
    {
        return itemCount switch
        {
            <= 0 => "Những món có thể đang đến kỳ mua thêm để bạn chốt đơn nhanh hơn.",
            1 => "Một món có thể đang đến kỳ mua thêm hôm nay.",
            _ => $"{itemCount} món có thể đang đến kỳ mua thêm hôm nay."
        };
    }

    private static RecommendationProductApiDto CreateReplenishmentSectionItem(
        RecommendationProductApiDto item,
        OrderingReplenishmentProfileApiDto profile)
    {
        var tags = new List<string>(6);
        var daysSinceLastPurchase = Math.Max(0, (DateTime.UtcNow - profile.LastPurchasedAtUtc).TotalDays);
        tags.Add(BuildReplenishmentTimingTag(profile, daysSinceLastPurchase));

        var cadenceTag = BuildReplenishmentCadenceTag(profile);
        if (!string.IsNullOrWhiteSpace(cadenceTag))
        {
            tags.Add(cadenceTag);
        }

        tags.Add(BuildReplenishmentRecencyTag(daysSinceLastPurchase));

        if (item.AvailableStock > 0)
        {
            tags.Add("Sẵn để đặt lại ngay");
        }

        if (IsOriginAlignedWithSellerRegion(item))
        {
            tags.Add("Cùng vùng trồng & giao");
        }

        return CloneRecommendationItem(item, string.Join(" · ", tags.Take(3)), tags);
    }

    private static double CalculateReplenishmentClarityBoost(OrderingReplenishmentProfileApiDto profile)
    {
        var boost = 0d;

        if (profile.AverageRepurchaseDays >= 2d && profile.AverageRepurchaseDays <= 45d)
        {
            boost += 12d;
        }
        else if (profile.AverageRepurchaseDays > 0d && profile.AverageRepurchaseDays < 2d)
        {
            boost -= 10d;
        }

        if (profile.ExpectedReorderAtUtc.HasValue)
        {
            var distanceDays = Math.Abs((profile.ExpectedReorderAtUtc.Value - DateTime.UtcNow).TotalDays);
            boost += Math.Max(0d, 10d - Math.Min(distanceDays, 10d));
        }

        return boost;
    }

    private static string BuildReplenishmentTimingTag(OrderingReplenishmentProfileApiDto profile, double daysSinceLastPurchase)
    {
        if (profile.ExpectedReorderAtUtc.HasValue)
        {
            var daysToReorder = Math.Round((profile.ExpectedReorderAtUtc.Value - DateTime.UtcNow).TotalDays);
            if (daysToReorder <= 0)
            {
                return "Có thể đang đến kỳ mua lại";
            }

            if (daysToReorder <= 2)
            {
                return "Có thể sắp cần mua thêm";
            }

            if (daysToReorder <= 7)
            {
                return "Sắp đến kỳ mua lại";
            }
        }

        return daysSinceLastPurchase <= 14d
            ? "Mới mua gần đây"
            : "Có thể sắp cần mua thêm";
    }

    private static string? BuildReplenishmentCadenceTag(OrderingReplenishmentProfileApiDto profile)
    {
        if (profile.AverageRepurchaseDays >= 2d)
        {
            return $"Chu kỳ khoảng {Math.Round(profile.AverageRepurchaseDays)} ngày";
        }

        if (profile.PurchaseCount >= 3)
        {
            return "Bạn mua món này khá thường xuyên";
        }

        return null;
    }

    private static string BuildReplenishmentRecencyTag(double daysSinceLastPurchase)
    {
        var roundedDays = Math.Max(1, (int)Math.Round(daysSinceLastPurchase));
        return roundedDays <= 30
            ? $"Mới mua {roundedDays} ngày trước"
            : $"Lần mua gần nhất {roundedDays} ngày trước";
    }

    private static RecommendationProductApiDto MergeRecommendationItemForBuyAgain(
        RecommendationProductApiDto rankedItem,
        CatalogProductApiDto catalogItem)
    {
        rankedItem.SellerShopName = string.IsNullOrWhiteSpace(rankedItem.SellerShopName)
            ? catalogItem.SellerShopName
            : rankedItem.SellerShopName;
        rankedItem.SellerAddressSummary = string.IsNullOrWhiteSpace(rankedItem.SellerAddressSummary)
            ? catalogItem.SellerAddressSummary
            : rankedItem.SellerAddressSummary;
        rankedItem.Origin = string.IsNullOrWhiteSpace(rankedItem.Origin)
            ? catalogItem.Origin
            : rankedItem.Origin;
        rankedItem.AvailableStock = rankedItem.AvailableStock > 0
            ? rankedItem.AvailableStock
            : catalogItem.AvailableStock;
        rankedItem.SoldCount = rankedItem.SoldCount > 0
            ? rankedItem.SoldCount
            : catalogItem.SoldCount;
        rankedItem.AverageRating = rankedItem.AverageRating > 0
            ? rankedItem.AverageRating
            : catalogItem.AverageRating;
        rankedItem.ReviewCount = rankedItem.ReviewCount > 0
            ? rankedItem.ReviewCount
            : catalogItem.ReviewCount;

        return rankedItem;
    }

    private static bool HasFastReorderDeliverySignal(string? addressSummary)
    {
        var scopeKey = GetDeliveryScopeKey(addressSummary);
        return string.Equals(scopeKey, "district-level", StringComparison.OrdinalIgnoreCase)
            || string.Equals(scopeKey, "ward-level", StringComparison.OrdinalIgnoreCase);
    }

    private static RecommendationProductApiDto CloneRecommendationItem(
        RecommendationProductApiDto source,
        string recommendationReason,
        IEnumerable<string> recommendationTags)
    {
        return new RecommendationProductApiDto
        {
            ProductId = source.ProductId,
            ProductName = source.ProductName,
            Price = source.Price,
            CategoryName = source.CategoryName,
            UnitName = source.UnitName,
            ImageFileName = source.ImageFileName,
            Origin = source.Origin,
            Standard = source.Standard,
            Preservation = source.Preservation,
            Weight = source.Weight,
            PrimarySellerId = source.PrimarySellerId,
            SellerShopName = source.SellerShopName,
            SellerAddressSummary = source.SellerAddressSummary,
            AverageRating = source.AverageRating,
            SoldCount = source.SoldCount,
            ReviewCount = source.ReviewCount,
            AvailableStock = source.AvailableStock,
            RecommendationScore = source.RecommendationScore,
            RecommendationReason = recommendationReason,
            RecommendationTags = recommendationTags
                .Where(tag => !string.IsNullOrWhiteSpace(tag))
                .Select(tag => tag.Trim())
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .Take(6)
                .ToArray()
        };
    }

    private static bool IsSeasonalOrLocalRecommendation(RecommendationProductApiDto item)
    {
        if (item.RecommendationTags.Any(tag =>
                tag.Contains("mùa", StringComparison.OrdinalIgnoreCase)
                || tag.Contains("vùng", StringComparison.OrdinalIgnoreCase)
                || tag.Contains("địa phương", StringComparison.OrdinalIgnoreCase)
                || tag.Contains("dia phuong", StringComparison.OrdinalIgnoreCase)))
        {
            return true;
        }

        return item.RecommendationReason.Contains("mùa", StringComparison.OrdinalIgnoreCase)
               || item.RecommendationReason.Contains("vùng", StringComparison.OrdinalIgnoreCase)
               || item.RecommendationReason.Contains("địa phương", StringComparison.OrdinalIgnoreCase)
               || item.RecommendationReason.Contains("dia phuong", StringComparison.OrdinalIgnoreCase)
               || item.RecommendationReason.Contains("Từ ", StringComparison.OrdinalIgnoreCase);
    }

    private static int GetHomeFirstPassCategoryCap(int limit)
    {
        return limit <= 4 ? 1 : 2;
    }

    private static int GetHomeFirstPassSellerCap(int limit)
    {
        return limit <= 4 ? 1 : HomeFirstPassSellerCap;
    }

    private static int GetFavoriteShopFirstPassSellerCap(int limit)
    {
        return limit <= 4 ? 1 : FavoriteShopFirstPassSellerCap;
    }

    private static int GetHomeFirstPassRegionCap(int limit)
    {
        return limit <= 4 ? 1 : 2;
    }

    private static bool IsHomeSecondPassNewSeller(IDictionary<int, int> sellerCounts, int? primarySellerId)
    {
        if (!primarySellerId.HasValue || primarySellerId.Value <= 0)
        {
            return false;
        }

        return !sellerCounts.ContainsKey(primarySellerId.Value);
    }

    private static bool IsHomeSecondPassNewRegion(IDictionary<string, int> regionCounts, CatalogProductApiDto item)
    {
        var regionKey = GetOriginRegionKey(item.Origin)
            ?? GetAddressRegionKey(item.SellerAddressSummary);

        return !string.IsNullOrWhiteSpace(regionKey) && !regionCounts.ContainsKey(regionKey);
    }

    private static bool IsHomeSecondPassNewCategory(IDictionary<string, int> categoryCounts, CatalogProductApiDto item)
    {
        var categoryKey = string.IsNullOrWhiteSpace(item.CategoryName)
            ? $"category-{item.CategoryId}"
            : item.CategoryName.Trim();

        return !categoryCounts.ContainsKey(categoryKey);
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
        IReadOnlyDictionary<int, OrderingSimilarProductSignalApiDto> collaborativeSignals,
        IReadOnlyDictionary<int, OrderingUserCategoryScoreApiDto> userCategoryScores,
        IReadOnlyDictionary<int, OrderingUserSellerScoreApiDto> userSellerScores)
    {
        var rankedCandidates = items
            .Where(item => item.ProductId != seedProduct.ProductId)
            .Where(IsRecommendationCandidate)
            .Select(item =>
            {
                collaborativeSignals.TryGetValue(item.ProductId, out var signal);
                userCategoryScores.TryGetValue(item.CategoryId, out var categoryScore);
                OrderingUserSellerScoreApiDto? sellerScore = null;
                if (item.PrimarySellerId.HasValue && item.PrimarySellerId.Value > 0)
                {
                    userSellerScores.TryGetValue(item.PrimarySellerId.Value, out sellerScore);
                }

                return new
                {
                    Item = item,
                    CollaborativeSignal = signal,
                    UserCategoryScore = categoryScore,
                    UserSellerScore = sellerScore,
                    Score = CalculateSimilarProductScore(seedProduct, item, signal, categoryScore, sellerScore)
                };
            })
            .Where(entry => entry.Score > 0)
            .OrderByDescending(entry => entry.Score)
            .ThenByDescending(entry => entry.CollaborativeSignal?.CollaborativeScore ?? 0d)
            .ThenByDescending(entry => entry.Item.AverageRating)
            .ThenByDescending(entry => entry.Item.SoldCount)
            .ThenByDescending(entry => entry.Item.ProductId)
            .ToList();

        var selected = new List<RecommendationProductApiDto>();
        var sellerCounts = new Dictionary<int, int>();
        var originCounts = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        var firstPassSellerCap = GetSimilarFirstPassSellerCap(limit);
        var firstPassOriginCap = GetSimilarFirstPassOriginCap(limit);

        foreach (var entry in rankedCandidates)
        {
            if (HasReachedSellerCap(sellerCounts, entry.Item.PrimarySellerId, firstPassSellerCap))
            {
                continue;
            }

            var originKey = GetSimilarOriginDiversityKey(entry.Item);
            if (HasReachedOriginCap(originCounts, originKey, firstPassOriginCap))
            {
                continue;
            }

            IncrementSellerCount(sellerCounts, entry.Item.PrimarySellerId);
            IncrementOriginCount(originCounts, originKey);
            selected.Add(MapRecommendationProduct(
                entry.Item,
                entry.Score,
                BuildSimilarRecommendationReason(seedProduct, entry.Item, entry.CollaborativeSignal, entry.UserCategoryScore, entry.UserSellerScore)));
            if (selected.Count >= limit)
            {
                return selected;
            }
        }

        foreach (var entry in rankedCandidates
                     .OrderByDescending(entry => IsSimilarSecondPassNewSeller(sellerCounts, entry.Item.PrimarySellerId))
                     .ThenByDescending(entry => IsSimilarSecondPassNewOrigin(originCounts, entry.Item))
                     .ThenByDescending(entry => entry.Score)
                     .ThenByDescending(entry => entry.CollaborativeSignal?.CollaborativeScore ?? 0d)
                     .ThenByDescending(entry => entry.Item.AverageRating)
                     .ThenByDescending(entry => entry.Item.SoldCount)
                     .ThenByDescending(entry => entry.Item.ProductId))
        {
            if (selected.Any(item => item.ProductId == entry.Item.ProductId))
            {
                continue;
            }

            var remainingOriginKey = GetSimilarOriginDiversityKey(entry.Item);
            selected.Add(MapRecommendationProduct(
                entry.Item,
                entry.Score,
                BuildSimilarRecommendationReason(seedProduct, entry.Item, entry.CollaborativeSignal, entry.UserCategoryScore, entry.UserSellerScore)));
            IncrementSellerCount(sellerCounts, entry.Item.PrimarySellerId);
            IncrementOriginCount(originCounts, remainingOriginKey);
            if (selected.Count >= limit)
            {
                break;
            }
        }

        return selected;
    }

    private static bool IsSimilarSecondPassNewSeller(IDictionary<int, int> sellerCounts, int? primarySellerId)
    {
        if (!primarySellerId.HasValue || primarySellerId.Value <= 0)
        {
            return false;
        }

        return !sellerCounts.ContainsKey(primarySellerId.Value);
    }

    private static bool IsSimilarSecondPassNewOrigin(IDictionary<string, int> originCounts, CatalogProductApiDto item)
    {
        var originKey = GetSimilarOriginDiversityKey(item);
        return !string.IsNullOrWhiteSpace(originKey) && !originCounts.ContainsKey(originKey);
    }

    private static int GetSimilarFirstPassSellerCap(int limit)
    {
        return limit <= 4 ? 1 : SimilarFirstPassSellerCap;
    }

    private static int GetSimilarFirstPassOriginCap(int limit)
    {
        return limit <= 4 ? 1 : 2;
    }

    private static bool HasReachedSellerCap(IDictionary<int, int> sellerCounts, int? primarySellerId, int cap)
    {
        if (!primarySellerId.HasValue || primarySellerId.Value <= 0)
        {
            return false;
        }

        return sellerCounts.TryGetValue(primarySellerId.Value, out var currentCount) && currentCount >= cap;
    }

    private static void IncrementSellerCount(IDictionary<int, int> sellerCounts, int? primarySellerId)
    {
        if (!primarySellerId.HasValue || primarySellerId.Value <= 0)
        {
            return;
        }

        sellerCounts[primarySellerId.Value] = sellerCounts.TryGetValue(primarySellerId.Value, out var currentCount)
            ? currentCount + 1
            : 1;
    }

    private static List<RecommendationProductApiDto> BuildFavoriteShopSectionItems(
        IReadOnlyList<FavoriteShopCandidateDto> candidates,
        IReadOnlySet<int>? usedProductIds,
        IReadOnlySet<int>? excludedSellerIds,
        int limit)
    {
        if (candidates.Count == 0 || limit <= 0)
        {
            return new List<RecommendationProductApiDto>();
        }

        var eligibleCandidates = candidates
            .Where(candidate => usedProductIds is null || !usedProductIds.Contains(candidate.Item.ProductId))
            .Where(candidate => excludedSellerIds is null
                || excludedSellerIds.Count == 0
                || !excludedSellerIds.Contains(candidate.Seller.SellerId))
            .ToList();

        if (eligibleCandidates.Count == 0)
        {
            return new List<RecommendationProductApiDto>();
        }

        var firstPassSellerCap = GetFavoriteShopFirstPassSellerCap(limit);
        var sellerCounts = new Dictionary<int, int>();
        var selectedProductIds = new HashSet<int>();
        var items = new List<RecommendationProductApiDto>(limit);

        foreach (var candidate in eligibleCandidates)
        {
            if (items.Count >= limit)
            {
                break;
            }

            if (HasReachedSellerCap(sellerCounts, candidate.Seller.SellerId, firstPassSellerCap))
            {
                continue;
            }

            items.Add(CreateFavoriteShopSectionItem(candidate.Item, candidate.Seller));
            selectedProductIds.Add(candidate.Item.ProductId);
            IncrementSellerCount(sellerCounts, candidate.Seller.SellerId);
        }

        if (items.Count >= limit)
        {
            return items;
        }

        foreach (var candidate in eligibleCandidates)
        {
            if (items.Count >= limit)
            {
                break;
            }

            if (!selectedProductIds.Add(candidate.Item.ProductId))
            {
                continue;
            }

            items.Add(CreateFavoriteShopSectionItem(candidate.Item, candidate.Seller));
        }

        return items;
    }

    private static bool HasReachedOriginCap(IDictionary<string, int> originCounts, string originKey, int cap)
    {
        if (string.IsNullOrWhiteSpace(originKey))
        {
            return false;
        }

        return originCounts.TryGetValue(originKey, out var currentCount) && currentCount >= cap;
    }

    private static void IncrementOriginCount(IDictionary<string, int> originCounts, string originKey)
    {
        if (string.IsNullOrWhiteSpace(originKey))
        {
            return;
        }

        originCounts[originKey] = originCounts.TryGetValue(originKey, out var currentCount)
            ? currentCount + 1
            : 1;
    }

    private static string GetSimilarOriginDiversityKey(CatalogProductApiDto item)
    {
        var normalizedOrigin = NormalizeText(item.Origin);
        if (!string.IsNullOrWhiteSpace(normalizedOrigin))
        {
            return normalizedOrigin;
        }

        return GetOriginRegionKey(item.Origin)
            ?? GetAddressRegionKey(item.SellerAddressSummary)
            ?? $"origin-{item.ProductId}";
    }

    private static IReadOnlyDictionary<int, PublicMerchantApiDto> BuildMerchantLookup(IEnumerable<PublicMerchantApiDto> merchants)
    {
        return merchants
            .Where(merchant => merchant.SellerId > 0)
            .GroupBy(merchant => merchant.SellerId)
            .ToDictionary(group => group.Key, group => SelectPreferredMerchant(group));
    }

    private async Task EnrichCatalogProductsWithMerchantDataAsync(IEnumerable<CatalogProductApiDto> items)
    {
        var itemList = items.ToList();
        if (itemList.Count == 0)
        {
            return;
        }

        var merchantBySellerId = await GetMerchantLookupAsync(itemList.Select(item => item.PrimarySellerId));
        if (merchantBySellerId.Count == 0)
        {
            return;
        }

        foreach (var item in itemList)
        {
            if (!item.PrimarySellerId.HasValue
                || item.PrimarySellerId.Value <= 0
                || !merchantBySellerId.TryGetValue(item.PrimarySellerId.Value, out var merchant))
            {
                continue;
            }

            item.SellerShopName = string.IsNullOrWhiteSpace(item.SellerShopName)
                ? merchant.ShopName ?? merchant.UserName
                : item.SellerShopName;
            item.SellerAddressSummary = string.IsNullOrWhiteSpace(item.SellerAddressSummary)
                ? merchant.AddressSummary
                : item.SellerAddressSummary;
        }
    }

    private async Task<IReadOnlyDictionary<int, PublicMerchantApiDto>> GetMerchantLookupAsync(IEnumerable<int?> sellerIds)
    {
        var normalizedSellerIds = sellerIds
            .Where(id => id.HasValue && id.Value > 0)
            .Select(id => id!.Value)
            .Distinct()
            .ToList();

        if (normalizedSellerIds.Count == 0)
        {
            return new Dictionary<int, PublicMerchantApiDto>();
        }

        var identityClient = _httpClientFactory.CreateClient("Identity");
        var merchantQuery = string.Join("&", normalizedSellerIds.Select(idValue => $"sellerIds={idValue}"));
        try
        {
            var merchantResponse = await identityClient.GetAsync($"/auth/public/merchants?{merchantQuery}");
            if (!merchantResponse.IsSuccessStatusCode)
            {
                return new Dictionary<int, PublicMerchantApiDto>();
            }

            var merchantContent = await merchantResponse.Content.ReadAsStringAsync();
            return BuildMerchantLookup(DeserializePublicMerchants(merchantContent));
        }
        catch (HttpRequestException)
        {
            return new Dictionary<int, PublicMerchantApiDto>();
        }
    }

    private static List<PublicMerchantApiDto> DeserializePublicMerchants(string merchantContent)
    {
        if (string.IsNullOrWhiteSpace(merchantContent))
        {
            return new List<PublicMerchantApiDto>();
        }

        try
        {
            var merchants = JsonSerializer.Deserialize<List<PublicMerchantApiDto>>(merchantContent, JsonOptions);
            if (merchants is not null)
            {
                return merchants;
            }
        }
        catch (JsonException)
        {
        }

        try
        {
            var merchantPayload = JsonSerializer.Deserialize<PublicMerchantListApiDto>(merchantContent, JsonOptions);
            return merchantPayload?.Merchants ?? new List<PublicMerchantApiDto>();
        }
        catch (JsonException)
        {
            return new List<PublicMerchantApiDto>();
        }
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
        score += CalculateHomeColdStartContextScore(item);

        if (item.CreatedDate >= DateTime.UtcNow.AddDays(-30))
        {
            score += 5d;
        }

        return score;
    }

    private static double CalculateLongTermUserProductBoost(OrderingUserProductScoreApiDto? userProductScore)
    {
        if (userProductScore is null || userProductScore.UserProductScore <= 0d)
        {
            return 0d;
        }

        var boost =
            Math.Log10(userProductScore.UserProductScore + 1d) * 18d
            + (userProductScore.PurchaseCount * 24d)
            + (userProductScore.SearchClickCount * 10d)
            + (userProductScore.RecommendationClickCount * 6d)
            + Math.Min(userProductScore.ViewCount, 6) * 2d;

        if (userProductScore.LastInteractedAtUtc.HasValue)
        {
            var daysAgo = (DateTime.UtcNow - userProductScore.LastInteractedAtUtc.Value).TotalDays;
            if (daysAgo <= 7d)
            {
                boost += 28d;
            }
            else if (daysAgo <= 30d)
            {
                boost += 16d;
            }
            else if (daysAgo <= 90d)
            {
                boost += 8d;
            }
        }

        return boost;
    }

    private static double CalculateLongTermUserSellerBoost(
        CatalogProductApiDto item,
        IReadOnlyDictionary<int, OrderingUserSellerScoreApiDto> userSellerScores)
    {
        if (!item.PrimarySellerId.HasValue
            || item.PrimarySellerId.Value <= 0
            || !userSellerScores.TryGetValue(item.PrimarySellerId.Value, out var sellerScore)
            || sellerScore.UserSellerScore <= 0d)
        {
            return 0d;
        }

        return CalculateLongTermUserSellerSignalBoost(sellerScore);
    }

    private static double CalculateHomeRecentSellerBoost(
        CatalogProductApiDto item,
        IReadOnlyDictionary<int, OrderingUserSellerScoreApiDto> userSellerScores)
    {
        if (!item.PrimarySellerId.HasValue
            || item.PrimarySellerId.Value <= 0
            || !userSellerScores.TryGetValue(item.PrimarySellerId.Value, out var sellerScore)
            || sellerScore.UserSellerScore <= 0d
            || !IsRecentSellerInteraction(sellerScore.LastInteractedAtUtc))
        {
            return 0d;
        }

        var boost = sellerScore.PurchaseCount > 0 ? 22d : 14d;
        if (sellerScore.SearchClickCount > 0)
        {
            boost += 6d;
        }

        if (sellerScore.ViewCount >= 3)
        {
            boost += 4d;
        }

        return boost;
    }

    private static double CalculateLongTermUserSellerSignalBoost(OrderingUserSellerScoreApiDto? sellerScore)
    {
        if (sellerScore is null || sellerScore.UserSellerScore <= 0d)
        {
            return 0d;
        }

        var boost =
            Math.Log10(sellerScore.UserSellerScore + 1d) * 14d
            + (sellerScore.PurchaseCount * 12d)
            + (sellerScore.SearchClickCount * 6d)
            + Math.Min(sellerScore.ViewCount, 6) * 1.5d;

        if (sellerScore.LastInteractedAtUtc.HasValue)
        {
            var daysAgo = (DateTime.UtcNow - sellerScore.LastInteractedAtUtc.Value).TotalDays;
            if (daysAgo <= 7d)
            {
                boost += 18d;
            }
            else if (daysAgo <= 30d)
            {
                boost += 10d;
            }
            else if (daysAgo <= 90d)
            {
                boost += 4d;
            }
        }

        return boost;
    }

    private static double CalculateFavoriteShopRecencyBoost(FavoriteShopSignalDto sellerSignal)
    {
        if (!IsRecentSellerInteraction(sellerSignal.LastInteractedAtUtc))
        {
            return 0d;
        }

        var boost = sellerSignal.PurchaseCount > 0 ? 26d : 14d;
        if (sellerSignal.SearchClickCount > 0)
        {
            boost += 6d;
        }

        if (sellerSignal.ViewCount >= 3)
        {
            boost += 4d;
        }

        return boost;
    }

    private static bool IsRecentSellerInteraction(DateTime? lastInteractedAtUtc)
    {
        if (!lastInteractedAtUtc.HasValue)
        {
            return false;
        }

        return (DateTime.UtcNow - lastInteractedAtUtc.Value).TotalDays <= 21d;
    }

    private static double CalculateLongTermUserCategoryBoost(OrderingUserCategoryScoreApiDto? categoryScore)
    {
        if (categoryScore is null || categoryScore.UserCategoryScore <= 0d)
        {
            return 0d;
        }

        var boost =
            Math.Min(categoryScore.UserCategoryScore / 18d, 42d)
            + (categoryScore.PurchaseCount * 4d)
            + (categoryScore.SearchClickCount * 2d)
            + (categoryScore.RecommendationClickCount * 1.5d)
            + Math.Min(categoryScore.ViewCount, 8);

        if (categoryScore.LastInteractedAtUtc.HasValue)
        {
            var ageDays = Math.Max(0d, (DateTime.UtcNow - categoryScore.LastInteractedAtUtc.Value).TotalDays);
            boost += Math.Max(0d, 10d - Math.Min(ageDays, 10d));
        }

        return boost;
    }

    private static LongTermPreferenceProfileDto BuildLongTermPreferenceProfile(
        IReadOnlyDictionary<int, OrderingUserProductScoreApiDto> userProductScores,
        IReadOnlyDictionary<int, OrderingUserCategoryScoreApiDto> userCategoryScores,
        IReadOnlyDictionary<int, CatalogProductApiDto> candidateLookup)
    {
        if ((userProductScores.Count == 0 && userCategoryScores.Count == 0) || candidateLookup.Count == 0)
        {
            return new LongTermPreferenceProfileDto();
        }

        var categoryScores = new Dictionary<int, double>();
        var categoryLabels = new Dictionary<int, string>( );
        var originScores = new Dictionary<string, double>(StringComparer.OrdinalIgnoreCase);
        var originLabels = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

        foreach (var userProductScore in userProductScores.Values.Where(score => score.ProductId > 0 && score.UserProductScore > 0d))
        {
            if (!candidateLookup.TryGetValue(userProductScore.ProductId, out var product))
            {
                continue;
            }

            var contribution = userProductScore.UserProductScore + CalculateLongTermUserProductBoost(userProductScore);
            if (contribution <= 0d)
            {
                continue;
            }

            if (product.CategoryId > 0)
            {
                categoryScores[product.CategoryId] = categoryScores.TryGetValue(product.CategoryId, out var categoryScore)
                    ? categoryScore + contribution
                    : contribution;
                if (!string.IsNullOrWhiteSpace(product.CategoryName))
                {
                    categoryLabels[product.CategoryId] = product.CategoryName.Trim();
                }
            }

            var originRegionKey = GetOriginRegionKey(product.Origin);
            if (!string.IsNullOrWhiteSpace(originRegionKey))
            {
                originScores[originRegionKey] = originScores.TryGetValue(originRegionKey, out var originScore)
                    ? originScore + contribution
                    : contribution;
                originLabels[originRegionKey] = !string.IsNullOrWhiteSpace(product.Origin)
                    ? GetShortLocationLabel(product.Origin)
                    : originRegionKey;
            }
        }

        foreach (var userCategoryScore in userCategoryScores.Values.Where(score => score.CategoryId > 0 && score.UserCategoryScore > 0d))
        {
            var contribution = userCategoryScore.UserCategoryScore + CalculateLongTermUserCategoryBoost(userCategoryScore);
            if (contribution <= 0d)
            {
                continue;
            }

            categoryScores[userCategoryScore.CategoryId] = categoryScores.TryGetValue(userCategoryScore.CategoryId, out var categoryScore)
                ? Math.Max(categoryScore, contribution)
                : contribution;

            if (!string.IsNullOrWhiteSpace(userCategoryScore.CategoryName))
            {
                categoryLabels[userCategoryScore.CategoryId] = userCategoryScore.CategoryName.Trim();
            }
        }

        var topCategory = categoryScores
            .OrderByDescending(pair => pair.Value)
            .ThenBy(pair => pair.Key)
            .FirstOrDefault();
        var topOrigin = originScores
            .OrderByDescending(pair => pair.Value)
            .ThenBy(pair => pair.Key, StringComparer.OrdinalIgnoreCase)
            .FirstOrDefault();

        return new LongTermPreferenceProfileDto
        {
            TopCategoryId = topCategory.Key > 0 ? topCategory.Key : null,
            TopCategoryName = topCategory.Key > 0 && categoryLabels.TryGetValue(topCategory.Key, out var categoryName)
                ? categoryName
                : null,
            TopOriginRegionKey = !string.IsNullOrWhiteSpace(topOrigin.Key) ? topOrigin.Key : null,
            TopOriginLabel = !string.IsNullOrWhiteSpace(topOrigin.Key) && originLabels.TryGetValue(topOrigin.Key, out var originLabel)
                ? originLabel
                : null,
            UsesMaterializedCategoryScore = topCategory.Key > 0 && userCategoryScores.ContainsKey(topCategory.Key)
        };
    }

    private static double CalculateLongTermPreferenceProfileBoost(CatalogProductApiDto item, LongTermPreferenceProfileDto profile)
    {
        var boost = 0d;

        if (profile.TopCategoryId.HasValue && profile.TopCategoryId.Value > 0 && item.CategoryId == profile.TopCategoryId.Value)
        {
            boost += 26d;
        }

        var originRegionKey = GetOriginRegionKey(item.Origin);
        if (!string.IsNullOrWhiteSpace(profile.TopOriginRegionKey)
            && !string.IsNullOrWhiteSpace(originRegionKey)
            && string.Equals(profile.TopOriginRegionKey, originRegionKey, StringComparison.OrdinalIgnoreCase))
        {
            boost += 18d;
        }

        if (boost > 0d
            && profile.TopCategoryId.HasValue
            && profile.TopCategoryId.Value > 0
            && item.CategoryId == profile.TopCategoryId.Value
            && !string.IsNullOrWhiteSpace(profile.TopOriginRegionKey)
            && !string.IsNullOrWhiteSpace(originRegionKey)
            && string.Equals(profile.TopOriginRegionKey, originRegionKey, StringComparison.OrdinalIgnoreCase))
        {
            boost += 8d;
        }

        return boost;
    }

    private static double CalculateHomeAffinityScore(CatalogProductApiDto seedProduct, CatalogProductApiDto candidate)
    {
        var score = 0d;
        if (seedProduct.ProductId == candidate.ProductId)
        {
            return 0d;
        }

        var exactOriginMatch = IsSameContentValue(seedProduct.Origin, candidate.Origin);
        var regionalOriginMatch = !exactOriginMatch && IsSameRegionalOrigin(seedProduct.Origin, candidate.Origin);
        var sameSellerRegion = IsSameRegionalAddress(seedProduct.SellerAddressSummary, candidate.SellerAddressSummary);
        var sameLocalDelivery = HasSameLocalDeliveryAffinity(seedProduct.SellerAddressSummary, candidate.SellerAddressSummary);

        if (seedProduct.CategoryId > 0 && seedProduct.CategoryId == candidate.CategoryId)
        {
            score += 60d;
        }
        else if (!string.IsNullOrWhiteSpace(seedProduct.CategoryName)
                 && string.Equals(seedProduct.CategoryName.Trim(), candidate.CategoryName?.Trim(), StringComparison.OrdinalIgnoreCase))
        {
            score += 42d;
        }

        if (exactOriginMatch)
        {
            score += 14d;
        }
        else if (regionalOriginMatch)
        {
            score += 8d;
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

        if (sameSellerRegion)
        {
            score += exactOriginMatch || regionalOriginMatch ? 10d : 5d;
        }

        if (sameLocalDelivery)
        {
            score += exactOriginMatch || regionalOriginMatch ? 6d : 2d;
        }

        score += CalculateAttributeSimilarityBonus(seedProduct, candidate);
        score += CalculateWeightSimilarityBonus(seedProduct.Weight, candidate.Weight);
        score += CalculatePriceSimilarityBonus(seedProduct.Price, candidate.Price) * 0.6d;
        score += HasFreshnessSignals(candidate) ? 4d : 0d;
        score += IsSeasonalProduct(candidate) ? 4d : 0d;
        return score;
    }

    private static double CalculateSimilarProductScore(
        CatalogProductApiDto seedProduct,
        CatalogProductApiDto candidate,
        OrderingSimilarProductSignalApiDto? collaborativeSignal,
        OrderingUserCategoryScoreApiDto? userCategoryScore,
        OrderingUserSellerScoreApiDto? userSellerScore)
    {
        var score = 0d;
        var exactOriginMatch = IsSameContentValue(seedProduct.Origin, candidate.Origin);
        var regionalOriginMatch = !exactOriginMatch && IsSameRegionalOrigin(seedProduct.Origin, candidate.Origin);
        var sameSellerRegion = IsSameRegionalAddress(seedProduct.SellerAddressSummary, candidate.SellerAddressSummary);
        var sameLocalDelivery = HasSameLocalDeliveryAffinity(seedProduct.SellerAddressSummary, candidate.SellerAddressSummary);
        if (seedProduct.CategoryId > 0 && seedProduct.CategoryId == candidate.CategoryId)
        {
            score += 120d;
        }
        else if (!string.IsNullOrWhiteSpace(seedProduct.CategoryName)
                 && string.Equals(seedProduct.CategoryName.Trim(), candidate.CategoryName?.Trim(), StringComparison.OrdinalIgnoreCase))
        {
            score += 90d;
        }

        if (exactOriginMatch)
        {
            score += 24d;
        }
        else if (regionalOriginMatch)
        {
            score += 14d;
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

        if (sameSellerRegion)
        {
            score += exactOriginMatch || regionalOriginMatch ? 18d : 9d;
        }

        if (sameLocalDelivery)
        {
            score += exactOriginMatch || regionalOriginMatch ? 10d : 4d;
        }

        score += CalculateAttributeSimilarityBonus(seedProduct, candidate);
        score += CalculateWeightSimilarityBonus(seedProduct.Weight, candidate.Weight);
        score += CalculatePriceSimilarityBonus(seedProduct.Price, candidate.Price);
        score += (double)candidate.AverageRating * 4d;
        score += Math.Log10(candidate.SoldCount + 1) * 6d;
        score += Math.Log10(candidate.ReviewCount + 1) * 3d;
        score += HasFreshnessSignals(candidate) ? 4d : 0d;
        score += IsSeasonalProduct(candidate) ? 6d : 0d;
        score += collaborativeSignal?.CollaborativeScore ?? 0d;
        score += CalculateSimilarCategoryPreferenceBoost(candidate, userCategoryScore);
        score += CalculateSimilarSellerPreferenceBoost(candidate, userSellerScore);
        return score;
    }

    private static double CalculateSimilarCategoryPreferenceBoost(
        CatalogProductApiDto candidate,
        OrderingUserCategoryScoreApiDto? userCategoryScore)
    {
        if (userCategoryScore is null
            || userCategoryScore.CategoryId <= 0
            || userCategoryScore.UserCategoryScore <= 0d
            || candidate.CategoryId != userCategoryScore.CategoryId)
        {
            return 0d;
        }

        return CalculateLongTermUserCategoryBoost(userCategoryScore) * 0.45d;
    }

    private static double CalculateSimilarSellerPreferenceBoost(
        CatalogProductApiDto candidate,
        OrderingUserSellerScoreApiDto? userSellerScore)
    {
        if (userSellerScore is null
            || userSellerScore.SellerId <= 0
            || userSellerScore.UserSellerScore <= 0d
            || !candidate.PrimarySellerId.HasValue
            || candidate.PrimarySellerId.Value != userSellerScore.SellerId)
        {
            return 0d;
        }

        var boost = CalculateLongTermUserSellerSignalBoost(userSellerScore) * 0.9d;
        if (HasSameLocalDeliveryAffinity(candidate.Origin, candidate.SellerAddressSummary))
        {
            boost += 4d;
        }

        return boost;
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
            SellerShopName = item.SellerShopName,
            SellerAddressSummary = item.SellerAddressSummary,
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
        OrderingHomeCollaborativeCandidateApiDto? collaborativeCandidate,
        OrderingUserProductScoreApiDto? userProductScore,
        OrderingUserSellerScoreApiDto? userSellerScore,
        LongTermPreferenceProfileDto longTermPreferenceProfile)
    {
        var tags = new List<string>();
        if (userProductScore is not null && userProductScore.UserProductScore > 0d)
        {
            if (userProductScore.PurchaseCount >= 3)
            {
                tags.Add("Bạn quay lại món này nhiều lần");
            }
            else if (userProductScore.PurchaseCount > 0)
            {
                tags.Add("Bạn từng mua món này");
            }
            else if (userProductScore.SearchClickCount > 0)
            {
                tags.Add("Bạn từng tìm và chọn món này");
            }
            else if (userProductScore.RecommendationClickCount > 0)
            {
                tags.Add("Bạn từng mở món này từ gợi ý");
            }
            else if (userProductScore.ViewCount >= 3)
            {
                tags.Add("Bạn hay xem món này");
            }

            if (userProductScore.LastInteractedAtUtc.HasValue
                && (DateTime.UtcNow - userProductScore.LastInteractedAtUtc.Value).TotalDays <= 30d)
            {
                tags.Add("Bạn vừa quan tâm gần đây");
            }
        }

        if (longTermPreferenceProfile.TopCategoryId.HasValue
            && longTermPreferenceProfile.TopCategoryId.Value > 0
            && item.CategoryId == longTermPreferenceProfile.TopCategoryId.Value)
        {
            tags.Add(!string.IsNullOrWhiteSpace(longTermPreferenceProfile.TopCategoryName)
                ? $"Hợp nhóm {longTermPreferenceProfile.TopCategoryName}"
                : "Hợp nhóm bạn hay quay lại");
        }

        var itemOriginRegionKey = GetOriginRegionKey(item.Origin);
        if (!string.IsNullOrWhiteSpace(longTermPreferenceProfile.TopOriginRegionKey)
            && !string.IsNullOrWhiteSpace(itemOriginRegionKey)
            && string.Equals(longTermPreferenceProfile.TopOriginRegionKey, itemOriginRegionKey, StringComparison.OrdinalIgnoreCase))
        {
            tags.Add(!string.IsNullOrWhiteSpace(longTermPreferenceProfile.TopOriginLabel)
                ? $"Đúng vùng {longTermPreferenceProfile.TopOriginLabel} bạn hay chọn"
                : "Đúng vùng nông sản bạn hay chọn");
        }

        if (userSellerScore is not null && userSellerScore.UserSellerScore > 0d)
        {
            if (userSellerScore.PurchaseCount >= 1 && IsRecentSellerInteraction(userSellerScore.LastInteractedAtUtc))
            {
                tags.Add("Từ shop bạn vừa quay lại");
            }
            else if (userSellerScore.PurchaseCount >= 3)
            {
                tags.Add("Từ shop bạn hay quay lại");
            }
            else if (userSellerScore.SearchClickCount > 0 || userSellerScore.ViewCount >= 3)
            {
                tags.Add("Từ shop bạn hay quan tâm");
            }
        }

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

                if (IsSameContentValue(preferenceMatch.SeedProduct.Origin, item.Origin))
                {
                    tags.Add("Cùng xuất xứ bạn vừa xem");
                }
                else if (IsSameRegionalOrigin(preferenceMatch.SeedProduct.Origin, item.Origin))
                {
                    tags.Add("Cùng vùng nông sản bạn vừa xem");
                }

                if (IsSameRegionalAddress(preferenceMatch.SeedProduct.SellerAddressSummary, item.SellerAddressSummary))
                {
                    tags.Add("Từ shop cùng vùng bạn vừa xem");
                }

                if (HasSameLocalDeliveryAffinity(preferenceMatch.SeedProduct.SellerAddressSummary, item.SellerAddressSummary))
                {
                    tags.Add("Hợp giao nhanh trong khu vực");
                }

                if (preferenceMatch.SeedProduct.CategoryId > 0 && preferenceMatch.SeedProduct.CategoryId == item.CategoryId)
                {
                    tags.Add("Cùng danh mục bạn quan tâm");
                }

                var matchedAttributeLabels = GetMatchedAttributeLabels(preferenceMatch.SeedProduct, item).Take(1);
                tags.AddRange(matchedAttributeLabels.Select(label => $"Cùng {label}"));

                if (IsSameContentValue(preferenceMatch.SeedProduct.Standard, item.Standard))
                {
                    tags.Add("Cùng chuẩn bạn quan tâm");
                }
            }
        }

        if (IsSeasonalProduct(item) && IsOriginAlignedWithSellerRegion(item))
        {
            tags.Add("Đúng mùa ở vùng trồng này");
        }
        else if (IsSeasonalProduct(item))
        {
            tags.Add("Đang đúng mùa");
        }

        if (item.SoldCount >= 10)
        {
            tags.Add("Được chọn nhiều");
        }

        if (item.AverageRating >= 4)
        {
            tags.Add("Đánh giá tốt");
        }

        if (!string.IsNullOrWhiteSpace(item.Origin))
        {
            tags.Add($"Từ {GetShortLocationLabel(item.Origin)}");
        }

        if (!string.IsNullOrWhiteSpace(item.CategoryName))
        {
            tags.Add($"Nổi bật trong {item.CategoryName.Trim()}");
        }

        if (!string.IsNullOrWhiteSpace(item.Standard))
        {
            tags.Add($"Chuẩn {item.Standard.Trim()}");
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
        OrderingSimilarProductSignalApiDto? collaborativeSignal,
        OrderingUserCategoryScoreApiDto? userCategoryScore,
        OrderingUserSellerScoreApiDto? userSellerScore)
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

        if (userSellerScore is not null
            && userSellerScore.SellerId > 0
            && userSellerScore.UserSellerScore > 0d
            && candidate.PrimarySellerId.HasValue
            && candidate.PrimarySellerId.Value == userSellerScore.SellerId)
        {
            if (userSellerScore.PurchaseCount >= 2)
            {
                tags.Add("Từ shop bạn hay quay lại");
            }
            else if (userSellerScore.SearchClickCount > 0)
            {
                tags.Add("Từ shop bạn hay chọn");
            }
            else if (userSellerScore.ViewCount >= 2)
            {
                tags.Add("Từ shop bạn hay xem");
            }
        }

        if (userCategoryScore is not null
            && userCategoryScore.CategoryId > 0
            && userCategoryScore.UserCategoryScore > 0d
            && candidate.CategoryId == userCategoryScore.CategoryId)
        {
            if (userCategoryScore.PurchaseCount >= 2)
            {
                tags.Add(!string.IsNullOrWhiteSpace(userCategoryScore.CategoryName)
                    ? $"Bạn hay mua nhóm {userCategoryScore.CategoryName}"
                    : "Bạn hay mua nhóm này");
            }
            else if (userCategoryScore.SearchClickCount > 0 || userCategoryScore.RecommendationClickCount > 0)
            {
                tags.Add(!string.IsNullOrWhiteSpace(userCategoryScore.CategoryName)
                    ? $"Hợp nhóm {userCategoryScore.CategoryName} bạn hay chọn"
                    : "Hợp nhóm bạn hay chọn");
            }
            else if (userCategoryScore.ViewCount >= 2)
            {
                tags.Add(!string.IsNullOrWhiteSpace(userCategoryScore.CategoryName)
                    ? $"Bạn hay xem nhóm {userCategoryScore.CategoryName}"
                    : "Bạn hay xem nhóm này");
            }
        }

        if (IsSameContentValue(seedProduct.Origin, candidate.Origin))
        {
            tags.Add("Cùng xuất xứ");
        }
        else if (IsSameRegionalOrigin(seedProduct.Origin, candidate.Origin))
        {
            tags.Add("Cùng vùng trồng");
        }
        else if (!string.IsNullOrWhiteSpace(candidate.Origin))
        {
            tags.Add($"Từ {GetShortLocationLabel(candidate.Origin)}");
        }

        if (IsSeasonalProduct(candidate) && IsOriginAlignedWithSellerRegion(candidate))
        {
            tags.Add("Đúng mùa ở vùng trồng này");
        }
        else if (IsSeasonalProduct(candidate))
        {
            tags.Add("Đang đúng mùa");
        }

        if (IsSameRegionalAddress(seedProduct.SellerAddressSummary, candidate.SellerAddressSummary))
        {
            tags.Add("Shop cùng vùng");
        }

        if (HasSameLocalDeliveryAffinity(seedProduct.SellerAddressSummary, candidate.SellerAddressSummary))
        {
            tags.Add("Cùng vùng giao nhanh");
        }

        var matchedAttributes = GetMatchedAttributeLabels(seedProduct, candidate).Take(2).ToList();
        if (matchedAttributes.Count > 0)
        {
            tags.AddRange(matchedAttributes.Select(label => $"Cùng {label}"));
        }

        if (seedProduct.CategoryId > 0 && seedProduct.CategoryId == candidate.CategoryId)
        {
            tags.Add("Cùng danh mục");
        }

        if (IsSameContentValue(seedProduct.Standard, candidate.Standard))
        {
            tags.Add("Cùng chuẩn");
        }

        if (IsSameContentValue(seedProduct.Preservation, candidate.Preservation))
        {
            tags.Add("Cùng cách bảo quản");
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

    private static string GetShortLocationLabel(string location)
    {
        if (string.IsNullOrWhiteSpace(location))
        {
            return string.Empty;
        }

        var firstSegment = location
            .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .FirstOrDefault();

        return string.IsNullOrWhiteSpace(firstSegment)
            ? location.Trim()
            : firstSegment;
    }

    private static bool IsSameRegionalOrigin(string? leftOrigin, string? rightOrigin)
    {
        var leftRegion = GetOriginRegionKey(leftOrigin);
        var rightRegion = GetOriginRegionKey(rightOrigin);

        return !string.IsNullOrWhiteSpace(leftRegion)
            && string.Equals(leftRegion, rightRegion, StringComparison.OrdinalIgnoreCase);
    }

    private static bool IsSearchLocationMatchExact(string keyword, params string?[] locations)
    {
        var normalizedKeyword = NormalizeText(keyword);
        if (string.IsNullOrWhiteSpace(normalizedKeyword))
        {
            return false;
        }

        foreach (var location in locations)
        {
            foreach (var phrase in GetLocationMatchPhrases(location))
            {
                if (normalizedKeyword.Contains(phrase, StringComparison.Ordinal))
                {
                    return true;
                }
            }
        }

        return false;
    }

    private static bool IsSearchLocationMatchRegional(string keyword, params string?[] locations)
    {
        var keywordRegion = GetLocationRegionKey(keyword);
        if (string.IsNullOrWhiteSpace(keywordRegion))
        {
            return false;
        }

        return locations.Any(location =>
            !string.IsNullOrWhiteSpace(location)
            && string.Equals(keywordRegion, GetLocationRegionKey(location), StringComparison.OrdinalIgnoreCase));
    }

    private static IEnumerable<string> GetLocationMatchPhrases(string? location)
    {
        if (string.IsNullOrWhiteSpace(location))
        {
            yield break;
        }

        var segments = location
            .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Where(static segment => segment.Length >= 4)
            .Select(NormalizeText)
            .Where(static segment => !string.IsNullOrWhiteSpace(segment))
            .Distinct(StringComparer.Ordinal);

        foreach (var segment in segments)
        {
            yield return segment;
        }

        var shortLabel = NormalizeText(GetShortLocationLabel(location));
        if (!string.IsNullOrWhiteSpace(shortLabel))
        {
            yield return shortLabel;
        }
    }

    private static bool IsSeasonAwareKeyword(string keyword)
    {
        var normalizedKeyword = NormalizeText(keyword);
        if (string.IsNullOrWhiteSpace(normalizedKeyword))
        {
            return false;
        }

        return ContainsAny(
                normalizedKeyword,
                "mua",
                "mua vu",
                "theo mua",
                "nam",
                "mang tay")
            || ContainsAny(
                normalizedKeyword,
                "vai",
                "man",
                "dao",
                "chom chom",
                "nhan",
                "sau rieng",
                "mit",
                "buoi",
                "cam",
                "quyt",
                "hong",
                "dua hau",
                "dua leo",
                "bi do",
                "sup lo",
                "ca rot",
                "su hao");
    }

    private static string BuildCatalogProductSearchUrl(
        string? name,
        int[]? categoryIds,
        string[]? origins,
        string[]? standards,
        string[]? units)
    {
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

        return query.Count == 0
            ? "/api/products"
            : "/api/products?" + string.Join("&", query);
    }

    private static string? TryBuildRelaxedCatalogKeyword(string keyword)
    {
        if (string.IsNullOrWhiteSpace(keyword))
        {
            return null;
        }

        var originalTokens = keyword
            .Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        if (originalTokens.Length <= 1)
        {
            return null;
        }

        var normalizedTokens = originalTokens
            .Select(NormalizeText)
            .ToArray();
        var keptTokens = new List<string>(originalTokens.Length);
        var singleStopTokens = new HashSet<string>(StringComparer.Ordinal)
        {
            "tai",
            "o",
            "tu",
            "vung",
            "mien",
            "theo",
            "mua",
            "vu",
            "dia",
            "phuong"
        };
        var phrase3 = new HashSet<string>(StringComparer.Ordinal)
        {
            "ho chi minh"
        };
        var phrase2 = new HashSet<string>(StringComparer.Ordinal)
        {
            "da lat",
            "lam dong",
            "can tho",
            "long an",
            "tien giang",
            "an giang",
            "dong thap",
            "vinh long",
            "hau giang",
            "soc trang",
            "bac lieu",
            "ca mau",
            "kien giang",
            "ben tre",
            "tra vinh",
            "dong nai",
            "binh duong",
            "tay ninh",
            "vung tau",
            "dak lak",
            "dak nong",
            "gia lai",
            "kon tum",
            "mien tay",
            "mien trung",
            "mien bac",
            "tay nguyen",
            "mua vu",
            "theo mua"
        };

        for (var index = 0; index < originalTokens.Length; index++)
        {
            if (index + 2 < normalizedTokens.Length)
            {
                var phrase = string.Join(' ', normalizedTokens[index], normalizedTokens[index + 1], normalizedTokens[index + 2]);
                if (phrase3.Contains(phrase))
                {
                    index += 2;
                    continue;
                }
            }

            if (index + 1 < normalizedTokens.Length)
            {
                var phrase = $"{normalizedTokens[index]} {normalizedTokens[index + 1]}";
                if (phrase2.Contains(phrase))
                {
                    index += 1;
                    continue;
                }
            }

            if (singleStopTokens.Contains(normalizedTokens[index]))
            {
                continue;
            }

            keptTokens.Add(originalTokens[index]);
        }

        if (keptTokens.Count == 0 || keptTokens.Count == originalTokens.Length)
        {
            return null;
        }

        var relaxedKeyword = string.Join(' ', keptTokens).Trim();
        return string.IsNullOrWhiteSpace(relaxedKeyword)
            ? null
            : relaxedKeyword;
    }

    private static bool IsSameRegionalAddress(string? leftAddress, string? rightAddress)
    {
        var leftRegion = GetAddressRegionKey(leftAddress);
        var rightRegion = GetAddressRegionKey(rightAddress);

        return !string.IsNullOrWhiteSpace(leftRegion)
            && string.Equals(leftRegion, rightRegion, StringComparison.OrdinalIgnoreCase);
    }

    private static bool HasSameLocalDeliveryAffinity(string? leftAddress, string? rightAddress)
    {
        var leftScope = GetDeliveryScopeKey(leftAddress);
        var rightScope = GetDeliveryScopeKey(rightAddress);
        if (string.IsNullOrWhiteSpace(leftScope)
            || string.IsNullOrWhiteSpace(rightScope)
            || !string.Equals(leftScope, rightScope, StringComparison.OrdinalIgnoreCase)
            || string.Equals(leftScope, "province-level", StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        return IsSameRegionalAddress(leftAddress, rightAddress);
    }

    private static double CalculateHomeColdStartContextScore(CatalogProductApiDto item)
    {
        var score = 0d;
        var isSeasonal = IsSeasonalProduct(item);
        var hasFreshnessSignals = HasFreshnessSignals(item);
        var isOriginAlignedWithSellerRegion = IsOriginAlignedWithSellerRegion(item);

        if (isSeasonal)
        {
            score += 8d;
        }

        if (isOriginAlignedWithSellerRegion)
        {
            score += 7d;
        }

        if (isSeasonal && hasFreshnessSignals)
        {
            score += 5d;
        }

        if (isSeasonal && isOriginAlignedWithSellerRegion)
        {
            score += 4d;
        }

        return score;
    }

    private static bool IsOriginAlignedWithSellerRegion(CatalogProductApiDto item)
    {
        var originRegion = GetOriginRegionKey(item.Origin);
        var sellerRegion = GetAddressRegionKey(item.SellerAddressSummary);

        return !string.IsNullOrWhiteSpace(originRegion)
            && string.Equals(originRegion, sellerRegion, StringComparison.OrdinalIgnoreCase);
    }

    private static bool IsOriginAlignedWithSellerRegion(RecommendationProductApiDto item)
    {
        var originRegion = GetOriginRegionKey(item.Origin);
        var sellerRegion = GetAddressRegionKey(item.SellerAddressSummary);

        return !string.IsNullOrWhiteSpace(originRegion)
            && string.Equals(originRegion, sellerRegion, StringComparison.OrdinalIgnoreCase);
    }

    private static string? GetOriginRegionKey(string? origin)
    {
        return GetLocationRegionKey(origin);
    }

    private static string? GetAddressRegionKey(string? addressSummary)
    {
        return GetLocationRegionKey(addressSummary);
    }

    private static string? GetLocationRegionKey(string? location)
    {
        var normalized = NormalizeText(location);
        if (string.IsNullOrWhiteSpace(normalized))
        {
            return null;
        }

        if (ContainsAny(normalized, "da lat", "lam dong", "gia lai", "dak lak", "dac lak", "dak nong", "kon tum"))
        {
            return "tay-nguyen";
        }

        if (ContainsAny(normalized, "can tho", "an giang", "dong thap", "vinh long", "hau giang", "soc trang", "bac lieu", "ca mau", "kien giang", "tien giang", "ben tre", "tra vinh", "long an"))
        {
            return "mien-tay";
        }

        if (ContainsAny(normalized, "ho chi minh", "sai gon", "binh duong", "dong nai", "tay ninh", "ba ria", "vung tau", "binh phuoc"))
        {
            return "dong-nam-bo";
        }

        if (ContainsAny(normalized, "da nang", "hue", "thua thien", "quang nam", "quang ngai", "binh dinh", "phu yen", "khanh hoa", "ninh thuan", "binh thuan", "quang binh", "quang tri", "thanh hoa", "nghe an", "ha tinh"))
        {
            return "mien-trung";
        }

        if (ContainsAny(normalized, "ha noi", "hai phong", "quang ninh", "bac ninh", "thai nguyen", "nam dinh", "ninh binh", "hai duong", "hung yen", "bac giang", "ha nam"))
        {
            return "mien-bac";
        }

        return GetShortLocationLabel(location ?? string.Empty).ToLowerInvariant();
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
        public string? SellerShopName { get; set; }
        public string? SellerAddressSummary { get; set; }
        public int ReviewCount { get; set; }
        public string? RecommendationReason { get; set; }
        public string[] RecommendationTags { get; set; } = Array.Empty<string>();
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

        return builder
            .ToString()
            .Normalize(NormalizationForm.FormC)
            .Replace('đ', 'd')
            .Replace('Đ', 'D');
    }

    private static IEnumerable<ProductSearchApiDto> ApplySort(
        IEnumerable<ProductSearchApiDto> items,
        string? sort,
        string keyword,
        IReadOnlyDictionary<int, OrderingSearchRankingSignalApiDto> searchRankingSignals,
        IReadOnlyDictionary<int, OrderingUserCategoryScoreApiDto> userCategoryScores,
        IReadOnlyDictionary<int, OrderingUserSellerScoreApiDto> userSellerScores)
    {
        var normalizedSort = (sort ?? "related").Trim().ToLowerInvariant();
        return normalizedSort switch
        {
            "newest" => items.OrderByDescending(item => item.ProductId),
            "bestseller" => items.OrderByDescending(item => item.SoldCount).ThenByDescending(item => item.ProductId),
            "price-asc" => items.OrderBy(item => item.Price).ThenByDescending(item => item.ProductId),
            "price-desc" => items.OrderByDescending(item => item.Price).ThenByDescending(item => item.ProductId),
            _ => DiversifySearchResultsFirstPass(
                items
                    .OrderByDescending(item => CalculateHybridSearchScore(
                        item,
                        keyword,
                        searchRankingSignals.TryGetValue(item.ProductId, out var signal) ? signal : null,
                        userCategoryScores.TryGetValue(item.CategoryId, out var categoryScore) ? categoryScore : null,
                        item.PrimarySellerId.HasValue && userSellerScores.TryGetValue(item.PrimarySellerId.Value, out var sellerScore) ? sellerScore : null))
                    .ThenByDescending(item => item.ProductId)
                    .ToList(),
                SearchFirstPassSellerCap)
        };
    }

    private static IEnumerable<ProductSearchApiDto> DiversifySearchResultsFirstPass(
        IReadOnlyList<ProductSearchApiDto> rankedItems,
        int sellerCap)
    {
        if (rankedItems.Count == 0 || sellerCap <= 0)
        {
            return rankedItems;
        }

        var sellerCounts = new Dictionary<int, int>();
        var selected = new List<ProductSearchApiDto>(rankedItems.Count);

        foreach (var item in rankedItems)
        {
            if (HasReachedSellerCap(sellerCounts, item.PrimarySellerId, sellerCap))
            {
                continue;
            }

            IncrementSellerCount(sellerCounts, item.PrimarySellerId);
            selected.Add(item);
        }

        foreach (var item in rankedItems)
        {
            if (selected.Any(existing => existing.ProductId == item.ProductId))
            {
                continue;
            }

            selected.Add(item);
        }

        return selected;
    }

    private static void EnsureRecentSearchSellerDiscoveryCandidate(
        List<ProductSearchApiDto> rankedItems,
        IReadOnlyDictionary<int, OrderingSearchRankingSignalApiDto> searchRankingSignals,
        IReadOnlyDictionary<int, OrderingUserCategoryScoreApiDto> userCategoryScores,
        IReadOnlyDictionary<int, OrderingUserSellerScoreApiDto> userSellerScores,
        int pageSize)
    {
        var compactLimit = Math.Clamp(pageSize, 1, 48);
        if (rankedItems.Count == 0 || compactLimit < 4)
        {
            return;
        }

        var firstPage = rankedItems.Take(compactLimit).ToList();
        if (firstPage.Any(item => HasRecentSearchSellerSignal(item, userSellerScores)))
        {
            return;
        }

        var candidate = rankedItems
            .Skip(compactLimit)
            .FirstOrDefault(item =>
                HasRecentSearchSellerSignal(item, userSellerScores)
                && !searchRankingSignals.ContainsKey(item.ProductId));

        if (candidate is null)
        {
            return;
        }

        for (var index = compactLimit - 1; index >= 0; index--)
        {
            var current = rankedItems[index];
            if (HasRecentSearchSellerSignal(current, userSellerScores) || searchRankingSignals.ContainsKey(current.ProductId))
            {
                continue;
            }

            rankedItems.Remove(candidate);
            rankedItems.Insert(index, candidate);
            return;
        }
    }

    private static void EnsureFavoriteSearchSellerDiscoveryCandidate(
        List<ProductSearchApiDto> rankedItems,
        IReadOnlyDictionary<int, OrderingSearchRankingSignalApiDto> searchRankingSignals,
        IReadOnlyDictionary<int, OrderingUserCategoryScoreApiDto> userCategoryScores,
        IReadOnlyDictionary<int, OrderingUserSellerScoreApiDto> userSellerScores,
        int pageSize)
    {
        var pageLimit = Math.Clamp(pageSize, 1, 48);
        var discoveryWindow = Math.Min(pageLimit, SearchDiscoveryWindow);
        if (rankedItems.Count == 0 || discoveryWindow < 4)
        {
            return;
        }

        var firstPage = rankedItems.Take(discoveryWindow).ToList();
        if (firstPage.Any(item => HasFavoriteSearchSellerSignal(item, userSellerScores)))
        {
            return;
        }

        var candidate = rankedItems
            .Skip(discoveryWindow)
            .FirstOrDefault(item =>
                HasFavoriteSearchSellerSignal(item, userSellerScores)
                && !searchRankingSignals.ContainsKey(item.ProductId)
                && !HasRecentSearchSellerSignal(item, userSellerScores));

        if (candidate is null)
        {
            return;
        }

        for (var index = discoveryWindow - 1; index >= 0; index--)
        {
            var current = rankedItems[index];
            if (HasRecentSearchSellerSignal(current, userSellerScores)
                || HasFavoriteSearchSellerSignal(current, userSellerScores)
                || searchRankingSignals.ContainsKey(current.ProductId))
            {
                continue;
            }

            rankedItems.Remove(candidate);
            rankedItems.Insert(index, candidate);
            return;
        }
    }

    private static bool HasRecentSearchSellerSignal(
        ProductSearchApiDto item,
        IReadOnlyDictionary<int, OrderingUserSellerScoreApiDto> userSellerScores)
    {
        return item.PrimarySellerId.HasValue
            && userSellerScores.TryGetValue(item.PrimarySellerId.Value, out var sellerScore)
            && sellerScore.UserSellerScore > 0d
            && IsRecentSellerInteraction(sellerScore.LastInteractedAtUtc);
    }

    private static bool HasFavoriteSearchSellerSignal(
        ProductSearchApiDto item,
        IReadOnlyDictionary<int, OrderingUserSellerScoreApiDto> userSellerScores)
    {
        return item.PrimarySellerId.HasValue
            && userSellerScores.TryGetValue(item.PrimarySellerId.Value, out var sellerScore)
            && sellerScore.UserSellerScore > 0d;
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
        OrderingSearchRankingSignalApiDto? rankingSignal,
        OrderingUserCategoryScoreApiDto? userCategoryScore,
        OrderingUserSellerScoreApiDto? userSellerScore)
    {
        var score = CalculateRelevanceScore(item, keyword);
        if (rankingSignal is not null && rankingSignal.HybridSearchScore > 0)
        {
            score += (decimal)rankingSignal.HybridSearchScore;
        }

        score += CalculateSearchCategoryPreferenceScore(item, userCategoryScore);
        score += CalculateSearchSellerPreferenceScore(item, userSellerScore);
        score += CalculateSearchContextScore(item, keyword);
        return score;
    }

    private static decimal CalculateSearchCategoryPreferenceScore(
        ProductSearchApiDto item,
        OrderingUserCategoryScoreApiDto? userCategoryScore)
    {
        if (userCategoryScore is null
            || userCategoryScore.CategoryId <= 0
            || userCategoryScore.UserCategoryScore <= 0d
            || item.CategoryId != userCategoryScore.CategoryId)
        {
            return 0m;
        }

        var score = 12m;
        score += Math.Min((decimal)userCategoryScore.UserCategoryScore / 50m, 24m);
        score += Math.Min(userCategoryScore.PurchaseCount * 4m, 16m);
        score += Math.Min(userCategoryScore.SearchClickCount * 2m, 10m);
        score += Math.Min(userCategoryScore.RecommendationClickCount * 1.5m, 6m);
        score += Math.Min(userCategoryScore.ViewCount, 5m);

        if (userCategoryScore.LastInteractedAtUtc.HasValue)
        {
            var ageDays = Math.Max(0d, (DateTime.UtcNow - userCategoryScore.LastInteractedAtUtc.Value).TotalDays);
            score += Math.Max(0m, 8m - (decimal)Math.Min(ageDays, 8d));
        }

        return score;
    }

    private static decimal CalculateSearchSellerPreferenceScore(
        ProductSearchApiDto item,
        OrderingUserSellerScoreApiDto? userSellerScore)
    {
        if (userSellerScore is null
            || userSellerScore.SellerId <= 0
            || userSellerScore.UserSellerScore <= 0d
            || !item.PrimarySellerId.HasValue
            || item.PrimarySellerId.Value != userSellerScore.SellerId)
        {
            return 0m;
        }

        var score = 16m;
        score += Math.Min((decimal)userSellerScore.UserSellerScore / 36m, 30m);
        score += Math.Min(userSellerScore.PurchaseCount * 8m, 32m);
        score += Math.Min(userSellerScore.SearchClickCount * 3m, 12m);
        score += Math.Min(userSellerScore.ViewCount * 1.5m, 9m);
        score += CalculateRecentSearchSellerBoost(userSellerScore);

        if (userSellerScore.PurchaseCount >= 2)
        {
            score += 10m;
        }

        if (userSellerScore.LastInteractedAtUtc.HasValue)
        {
            var ageDays = Math.Max(0d, (DateTime.UtcNow - userSellerScore.LastInteractedAtUtc.Value).TotalDays);
            score += Math.Max(0m, 9m - (decimal)Math.Min(ageDays, 9d));
        }

        if (HasSameLocalDeliveryAffinity(item.Origin, item.SellerAddressSummary))
        {
            score += 3m;
        }

        return score;
    }

    private static decimal CalculateRecentSearchSellerBoost(OrderingUserSellerScoreApiDto? userSellerScore)
    {
        if (userSellerScore is null || !IsRecentSellerInteraction(userSellerScore.LastInteractedAtUtc))
        {
            return 0m;
        }

        var boost = userSellerScore.PurchaseCount > 0 ? 12m : 7m;
        if (userSellerScore.SearchClickCount > 0)
        {
            boost += 4m;
        }

        if (userSellerScore.ViewCount >= 3)
        {
            boost += 3m;
        }

        return boost;
    }

    private static decimal CalculateSearchContextScore(ProductSearchApiDto item, string keyword)
    {
        if (string.IsNullOrWhiteSpace(keyword))
        {
            return 0;
        }

        var originExactMatch = IsSearchLocationMatchExact(keyword, item.Origin);
        var sellerExactMatch = IsSearchLocationMatchExact(keyword, item.SellerAddressSummary);
        var originRegionalMatch = !originExactMatch && IsSearchLocationMatchRegional(keyword, item.Origin);
        var sellerRegionalMatch = !sellerExactMatch && IsSearchLocationMatchRegional(keyword, item.SellerAddressSummary);
        decimal score = 0;
        if (originExactMatch)
        {
            score += 40;
        }
        else if (sellerExactMatch)
        {
            score += 14;
        }
        else if (originRegionalMatch)
        {
            score += 20;
        }
        else if (sellerRegionalMatch)
        {
            score += 8;
        }

        // For location-intent queries, prefer products actually from that region over
        // products that only happen to be sold by a nearby/local shop.
        if ((sellerExactMatch || sellerRegionalMatch) && !(originExactMatch || originRegionalMatch))
        {
            score -= sellerExactMatch ? 4 : 2;
        }

        if (IsSeasonalProduct(item) && IsSeasonAwareKeyword(keyword))
        {
            score += 10;
        }

        if (HasSameLocalDeliveryAffinity(item.Origin, item.SellerAddressSummary))
        {
            score += 4;
        }

        return score;
    }

    private static void EnrichSearchProductsWithShopData(
        IEnumerable<ProductSearchApiDto> items,
        IEnumerable<AvailableShopFacetDto> shops)
    {
        var shopMap = shops
            .Where(shop => shop.SellerId > 0)
            .GroupBy(shop => shop.SellerId)
            .ToDictionary(group => group.Key, group => group.First());

        foreach (var item in items)
        {
            if (!item.PrimarySellerId.HasValue
                || item.PrimarySellerId.Value <= 0
                || !shopMap.TryGetValue(item.PrimarySellerId.Value, out var shop))
            {
                continue;
            }

            item.SellerShopName = string.IsNullOrWhiteSpace(item.SellerShopName)
                ? shop.ShopName
                : item.SellerShopName;
            item.SellerAddressSummary = string.IsNullOrWhiteSpace(item.SellerAddressSummary)
                ? shop.AddressSummary
                : item.SellerAddressSummary;
        }
    }

    private static void ApplySearchRecommendationReasons(
        IEnumerable<ProductSearchApiDto> items,
        string keyword,
        IReadOnlyDictionary<int, OrderingSearchRankingSignalApiDto> searchRankingSignals,
        IReadOnlyDictionary<int, OrderingUserCategoryScoreApiDto> userCategoryScores,
        IReadOnlyDictionary<int, OrderingUserSellerScoreApiDto> userSellerScores)
    {
        foreach (var item in items)
        {
            var rankingSignal = searchRankingSignals.TryGetValue(item.ProductId, out var signal)
                ? signal
                : null;
            var categoryScore = userCategoryScores.TryGetValue(item.CategoryId, out var userCategoryScore)
                ? userCategoryScore
                : null;
            var sellerScore = item.PrimarySellerId.HasValue && userSellerScores.TryGetValue(item.PrimarySellerId.Value, out var userSellerScore)
                ? userSellerScore
                : null;
            var reason = BuildSearchRecommendationReason(item, keyword, rankingSignal, categoryScore, sellerScore);
            item.RecommendationReason = reason.Text;
            item.RecommendationTags = reason.Tags;
        }
    }

    private static RecommendationReasonDto BuildSearchRecommendationReason(
        ProductSearchApiDto item,
        string keyword,
        OrderingSearchRankingSignalApiDto? rankingSignal,
        OrderingUserCategoryScoreApiDto? categoryScore,
        OrderingUserSellerScoreApiDto? sellerScore)
    {
        var tags = new List<string>(6);

        if (rankingSignal is not null)
        {
            if (rankingSignal.SearchClickSessionCount > 0)
            {
                tags.Add("Hay được bấm với từ khóa này");
            }

            if (rankingSignal.SearchRecommendationClickCount > 0)
            {
                tags.Add("Hay được mở từ gợi ý tương tự");
            }

            if (rankingSignal.SearchViewSessionCount > 0)
            {
                tags.Add("Hay được xem sau tìm kiếm này");
            }
        }

        if (sellerScore is not null
            && sellerScore.SellerId > 0
            && sellerScore.UserSellerScore > 0d
            && item.PrimarySellerId.HasValue
            && item.PrimarySellerId.Value == sellerScore.SellerId)
        {
            if (sellerScore.PurchaseCount >= 1 && IsRecentSellerInteraction(sellerScore.LastInteractedAtUtc))
            {
                tags.Add("Từ shop bạn vừa quay lại");
            }
            else if (sellerScore.PurchaseCount >= 2)
            {
                tags.Add("Từ shop bạn hay quay lại");
            }
            else if (sellerScore.SearchClickCount > 0)
            {
                tags.Add("Từ shop bạn hay chọn khi tìm");
            }
            else if (sellerScore.ViewCount >= 2)
            {
                tags.Add("Từ shop bạn hay xem");
            }
        }

        if (categoryScore is not null
            && categoryScore.CategoryId > 0
            && categoryScore.UserCategoryScore > 0d
            && item.CategoryId == categoryScore.CategoryId)
        {
            if (categoryScore.PurchaseCount >= 2)
            {
                tags.Add(!string.IsNullOrWhiteSpace(categoryScore.CategoryName)
                    ? $"Bạn hay mua nhóm {categoryScore.CategoryName}"
                    : "Bạn hay mua nhóm này");
            }
            else if (categoryScore.SearchClickCount > 0 || categoryScore.RecommendationClickCount > 0)
            {
                tags.Add(!string.IsNullOrWhiteSpace(categoryScore.CategoryName)
                    ? $"Hợp nhóm {categoryScore.CategoryName} bạn hay chọn"
                    : "Hợp nhóm bạn hay chọn");
            }
            else if (categoryScore.ViewCount >= 2)
            {
                tags.Add(!string.IsNullOrWhiteSpace(categoryScore.CategoryName)
                    ? $"Bạn hay xem nhóm {categoryScore.CategoryName}"
                    : "Bạn hay xem nhóm này");
            }
        }

        if (IsSearchLocationMatchExact(keyword, item.Origin))
        {
            tags.Add("Đúng nông sản vùng bạn tìm");
        }
        else if (IsSearchLocationMatchExact(keyword, item.SellerAddressSummary))
        {
            tags.Add("Shop ở đúng địa phương bạn tìm");
        }
        else if (IsSearchLocationMatchRegional(keyword, item.Origin))
        {
            tags.Add("Cùng vùng nông sản bạn tìm");
        }
        else if (IsSearchLocationMatchRegional(keyword, item.SellerAddressSummary))
        {
            tags.Add("Shop cùng vùng bạn tìm");
        }

        if (IsSeasonalProduct(item) && IsSeasonAwareKeyword(keyword))
        {
            tags.Add("Đang đúng mùa");
        }

        if (item.SoldCount > 0)
        {
            tags.Add("Được chọn nhiều");
        }

        if (item.AverageRating >= 4)
        {
            tags.Add("Đánh giá tốt");
        }

        if (!string.IsNullOrWhiteSpace(item.Origin))
        {
            tags.Add($"Từ {GetShortLocationLabel(item.Origin)}");
        }

        if (tags.Count == 0)
        {
            tags.Add("Phù hợp với tìm kiếm của bạn");
        }

        return new RecommendationReasonDto
        {
            Text = string.Join(" · ", tags.Take(3)),
            Tags = tags.Take(3).ToArray()
        };
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

    private async Task<OrderingUserProductScoreResult> GetOrderingUserProductScoresAsync(
        IEnumerable<int>? productIds,
        int limit)
    {
        var normalizedProductIds = (productIds ?? Array.Empty<int>())
            .Where(id => id > 0)
            .Distinct()
            .Take(200)
            .ToArray();

        var client = _httpClientFactory.CreateClient("Ordering");
        AttachAccessToken(client);

        var query = new List<string>
        {
            $"limit={Math.Clamp(limit, 1, 96)}"
        };
        foreach (var productId in normalizedProductIds)
        {
            query.Add($"productIds={productId}");
        }

        try
        {
            var response = await client.GetAsync($"/api/orders/product-insights/user-product?{string.Join("&", query)}");
            if (!response.IsSuccessStatusCode)
            {
                return new OrderingUserProductScoreResult();
            }

            var content = await response.Content.ReadAsStringAsync();
            var items = JsonSerializer.Deserialize<List<OrderingUserProductScoreApiDto>>(content, JsonOptions)
                        ?? new List<OrderingUserProductScoreApiDto>();
            return new OrderingUserProductScoreResult
            {
                Scores = items
                    .Where(item => item.ProductId > 0 && item.UserProductScore > 0d)
                    .GroupBy(item => item.ProductId)
                    .ToDictionary(
                        group => group.Key,
                        group => group.OrderByDescending(item => item.UserProductScore)
                            .ThenByDescending(item => item.PurchaseCount)
                            .ThenByDescending(item => item.SearchClickCount)
                            .ThenByDescending(item => item.RecommendationClickCount)
                            .ThenByDescending(item => item.ViewCount)
                            .ThenByDescending(item => item.LastInteractedAtUtc ?? DateTime.MinValue)
                            .First())
            };
        }
        catch (Exception)
        {
            return new OrderingUserProductScoreResult();
        }
    }

    private async Task<OrderingUserSellerScoreResult> GetOrderingUserSellerScoresAsync(
        IEnumerable<int>? sellerIds,
        int limit)
    {
        var normalizedSellerIds = (sellerIds ?? Array.Empty<int>())
            .Where(id => id > 0)
            .Distinct()
            .Take(200)
            .ToArray();

        var client = _httpClientFactory.CreateClient("Ordering");
        AttachAccessToken(client);

        var query = new List<string>
        {
            $"limit={Math.Clamp(limit, 1, 96)}"
        };
        foreach (var sellerId in normalizedSellerIds)
        {
            query.Add($"sellerIds={sellerId}");
        }

        try
        {
            var response = await client.GetAsync($"/api/orders/product-insights/user-seller?{string.Join("&", query)}");
            if (!response.IsSuccessStatusCode)
            {
                return new OrderingUserSellerScoreResult();
            }

            var content = await response.Content.ReadAsStringAsync();
            var items = JsonSerializer.Deserialize<List<OrderingUserSellerScoreApiDto>>(content, JsonOptions)
                        ?? new List<OrderingUserSellerScoreApiDto>();
            return new OrderingUserSellerScoreResult
            {
                Scores = items
                    .Where(item => item.SellerId > 0 && item.UserSellerScore > 0d)
                    .GroupBy(item => item.SellerId)
                    .ToDictionary(
                        group => group.Key,
                        group => group.OrderByDescending(item => item.UserSellerScore)
                            .ThenByDescending(item => item.PurchaseCount)
                            .ThenByDescending(item => item.SearchClickCount)
                            .ThenByDescending(item => item.ViewCount)
                            .ThenByDescending(item => item.LastInteractedAtUtc ?? DateTime.MinValue)
                            .First())
            };
        }
        catch (Exception)
        {
            return new OrderingUserSellerScoreResult();
        }
    }

    private async Task<OrderingUserCategoryScoreResult> GetOrderingUserCategoryScoresAsync(
        IEnumerable<int>? categoryIds,
        int limit)
    {
        var normalizedCategoryIds = (categoryIds ?? Array.Empty<int>())
            .Where(id => id > 0)
            .Distinct()
            .Take(200)
            .ToArray();

        var client = _httpClientFactory.CreateClient("Ordering");
        AttachAccessToken(client);

        var query = new List<string>
        {
            $"limit={Math.Clamp(limit, 1, 96)}"
        };
        foreach (var categoryId in normalizedCategoryIds)
        {
            query.Add($"categoryIds={categoryId}");
        }

        try
        {
            var response = await client.GetAsync($"/api/orders/product-insights/user-category?{string.Join("&", query)}");
            if (!response.IsSuccessStatusCode)
            {
                return new OrderingUserCategoryScoreResult();
            }

            var content = await response.Content.ReadAsStringAsync();
            var items = JsonSerializer.Deserialize<List<OrderingUserCategoryScoreApiDto>>(content, JsonOptions)
                        ?? new List<OrderingUserCategoryScoreApiDto>();
            return new OrderingUserCategoryScoreResult
            {
                Scores = items
                    .Where(item => item.CategoryId > 0 && item.UserCategoryScore > 0d)
                    .GroupBy(item => item.CategoryId)
                    .ToDictionary(
                        group => group.Key,
                        group => group.OrderByDescending(item => item.UserCategoryScore)
                            .ThenByDescending(item => item.PurchaseCount)
                            .ThenByDescending(item => item.SearchClickCount)
                            .ThenByDescending(item => item.RecommendationClickCount)
                            .ThenByDescending(item => item.ViewCount)
                            .ThenByDescending(item => item.LastInteractedAtUtc ?? DateTime.MinValue)
                            .First())
            };
        }
        catch (Exception)
        {
            return new OrderingUserCategoryScoreResult();
        }
    }

    private async Task<OrderingBasketAffinityResult> GetOrderingBasketAffinitiesAsync(
        IEnumerable<int> seedProductIds,
        IEnumerable<int> candidateProductIds,
        int limit)
    {
        var normalizedSeedProductIds = seedProductIds
            .Where(id => id > 0)
            .Distinct()
            .Take(4)
            .ToArray();
        var normalizedCandidateProductIds = candidateProductIds
            .Where(id => id > 0)
            .Distinct()
            .Take(200)
            .ToArray();

        if (normalizedSeedProductIds.Length == 0 || normalizedCandidateProductIds.Length == 0)
        {
            return new OrderingBasketAffinityResult();
        }

        var client = _httpClientFactory.CreateClient("Ordering");
        AttachAccessToken(client);
        var aggregated = new Dictionary<int, OrderingBasketAffinityApiDto>();

        foreach (var seedProductId in normalizedSeedProductIds)
        {
            var query = new List<string>
            {
                $"productId={seedProductId}",
                $"limit={Math.Clamp(limit, 1, 96)}"
            };
            foreach (var candidateProductId in normalizedCandidateProductIds.Where(id => id != seedProductId))
            {
                query.Add($"candidateProductIds={candidateProductId}");
            }

            try
            {
                var response = await client.GetAsync($"/api/orders/product-insights/basket-affinity?{string.Join("&", query)}");
                if (!response.IsSuccessStatusCode)
                {
                    continue;
                }

                var content = await response.Content.ReadAsStringAsync();
                var items = JsonSerializer.Deserialize<List<OrderingBasketAffinityApiDto>>(content, JsonOptions)
                            ?? new List<OrderingBasketAffinityApiDto>();
                foreach (var item in items.Where(item => item.CandidateProductId > 0 && item.BasketScore > 0d))
                {
                    if (!aggregated.TryGetValue(item.CandidateProductId, out var current)
                        || item.BasketScore > current.BasketScore)
                    {
                        aggregated[item.CandidateProductId] = item;
                    }
                }
            }
            catch (Exception)
            {
                // Ignore per-seed basket lookup failures to keep homepage recommendation resilient.
            }
        }

        return new OrderingBasketAffinityResult
        {
            Signals = aggregated
        };
    }

    private async Task<OrderingReplenishmentProfileResult> GetOrderingReplenishmentProfilesAsync(
        IEnumerable<int> productIds,
        int limit)
    {
        var normalizedProductIds = productIds
            .Where(id => id > 0)
            .Distinct()
            .Take(200)
            .ToArray();
        if (normalizedProductIds.Length == 0)
        {
            return new OrderingReplenishmentProfileResult();
        }

        var client = _httpClientFactory.CreateClient("Ordering");
        AttachAccessToken(client);

        var query = new List<string>
        {
            $"limit={Math.Clamp(limit, 1, 96)}"
        };
        foreach (var productId in normalizedProductIds)
        {
            query.Add($"productIds={productId}");
        }

        try
        {
            var response = await client.GetAsync($"/api/orders/product-insights/replenishment-profile?{string.Join("&", query)}");
            if (!response.IsSuccessStatusCode)
            {
                return new OrderingReplenishmentProfileResult();
            }

            var content = await response.Content.ReadAsStringAsync();
            var items = JsonSerializer.Deserialize<List<OrderingReplenishmentProfileApiDto>>(content, JsonOptions)
                        ?? new List<OrderingReplenishmentProfileApiDto>();
            return new OrderingReplenishmentProfileResult
            {
                Profiles = items
                    .Where(item => item.ProductId > 0 && item.ReplenishmentScore > 0d)
                    .GroupBy(item => item.ProductId)
                    .ToDictionary(
                        group => group.Key,
                        group => group.OrderByDescending(item => item.ReplenishmentScore)
                            .ThenByDescending(item => item.PurchaseCount)
                            .ThenByDescending(item => item.LastPurchasedAtUtc)
                            .First())
            };
        }
        catch (Exception)
        {
            return new OrderingReplenishmentProfileResult();
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
                FallbackReason = GetRecommendationFallbackReason(response),
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
                FallbackReason = GetRecommendationFallbackReason(response),
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
                FallbackReason = GetRecommendationFallbackReason(response),
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
                FallbackReason = GetRecommendationFallbackReason(response),
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

    private static string? GetRecommendationFallbackReason(HttpResponseMessage response)
    {
        return response.Headers.TryGetValues(RecommendationFallbackReasonHeader, out var values)
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

    private static string? BuildCompositeFallbackReason(params (string Scope, string? Reason)[] segments)
    {
        var normalized = segments
            .Where(segment => !string.IsNullOrWhiteSpace(segment.Reason))
            .Select(segment => $"{segment.Scope}:{segment.Reason!.Trim()}")
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

