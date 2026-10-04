using System.Text.Json;
using System.IdentityModel.Tokens.Jwt;
using System.Net.Http.Headers;
using System.Security.Claims;
using FreshFarm.Web.Bff.Dtos;
using FreshFarm.Web.Bff.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.Extensions.DependencyInjection;

namespace FreshFarm.Web.Bff.Controllers;

[Authorize]
public sealed class CartController : Controller
{
    private const string CheckoutSelectedCartItemKeysSessionKey = "CHECKOUT_SELECTED_CART_ITEM_KEYS";
    private const string AccessTokenSessionKey = "ACCESS_TOKEN";
    private const int MaxCartRecommendationSeeds = 4;
    private const int MaxCartRecommendationRowsPerSeed = 12;
    private const int MaxCartRecommendations = 6;
    private const int MaxCatalogRecommendationLookup = 48;
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
    {
        PropertyNameCaseInsensitive = true
    };

    private readonly ICartSessionService _cart;
    private readonly IRecommendationMetricsClient _recommendationMetricsClient;
    private readonly IRecommendationExperimentService _recommendationExperimentService;
    private readonly IHttpClientFactory? _httpClientFactory;

    public CartController(ICartSessionService cart)
        : this(
            cart,
            NoopRecommendationMetricsClient.Instance,
            NoopRecommendationExperimentService.Instance,
            httpClientFactory: null)
    {
    }

    public CartController(
        ICartSessionService cart,
        IRecommendationMetricsClient recommendationMetricsClient,
        IRecommendationExperimentService recommendationExperimentService)
        : this(cart, recommendationMetricsClient, recommendationExperimentService, httpClientFactory: null)
    {
    }

    [ActivatorUtilitiesConstructor]
    public CartController(
        ICartSessionService cart,
        IRecommendationMetricsClient recommendationMetricsClient,
        IRecommendationExperimentService recommendationExperimentService,
        IHttpClientFactory? httpClientFactory)
    {
        _cart = cart;
        _recommendationMetricsClient = recommendationMetricsClient;
        _recommendationExperimentService = recommendationExperimentService;
        _httpClientFactory = httpClientFactory;
    }

    [HttpPost("/cart/checkout-selected")]
    [ValidateAntiForgeryToken]
    [EnableRateLimiting("cart-write")]
    public async Task<IActionResult> CheckoutSelected([FromForm] string[] selectedCartItemKeys)
    {
        var cartKeys = (await _cart.GetItemsAsync())
            .Select(x => x.CartItemKey)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        var validSelectedKeys = (selectedCartItemKeys ?? Array.Empty<string>())
            .Where(key => !string.IsNullOrWhiteSpace(key) && cartKeys.Contains(key.Trim()))
            .Select(key => key.Trim())
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();

        if (validSelectedKeys.Count == 0)
        {
            HttpContext.Session.Remove(CheckoutSelectedCartItemKeysSessionKey);
            TempData["CheckoutError"] = "Vui lòng chọn ít nhất 1 sản phẩm để thanh toán.";
            return RedirectToAction(nameof(Index));
        }

        HttpContext.Session.SetString(
            CheckoutSelectedCartItemKeysSessionKey,
            JsonSerializer.Serialize(validSelectedKeys));

        return RedirectToAction("Index", "Checkout");
    }

    [HttpGet("/cart")]
    public async Task<IActionResult> Index()
    {
        const decimal shippingFee = 0m;
        var vm = await _cart.BuildSummaryAsync(shippingFee);
        vm.Recommendations = await BuildCartRecommendationsAsync(vm.Items);
        return View(vm);
    }

    [HttpPost("/cart/add")]
    [ValidateAntiForgeryToken]
    [EnableRateLimiting("cart-write")]
    public async Task<IActionResult> Add([FromForm] AddToCartRequestDto request)
    {
        if (request.ProductId <= 0)
        {
            return BadRequest("ProductId không hợp lệ.");
        }

        if (request.SellerId <= 0)
        {
            return BadRequest("SellerId không hợp lệ.");
        }

        await _cart.AddOrIncreaseAsync(request);
        TrackRecommendationAddToCartFireAndForget(request);

        if (WantsJson())
        {
            var cartItems = await _cart.GetItemsAsync();
            var cartItem = cartItems.FirstOrDefault(x =>
                x.ProductId == request.ProductId &&
                x.SellerId == request.SellerId);

            return Json(new
            {
                success = true,
                message = "Đã thêm sản phẩm vào giỏ hàng.",
                cartItemCount = cartItems.Count,
                cartQuantity = cartItems.Sum(x => Math.Max(0, x.Quantity)),
                quantity = cartItem?.Quantity ?? Math.Max(1, request.Quantity),
                cartUrl = Url.Action(nameof(Index), "Cart") ?? "/cart"
            });
        }

        return RedirectToAction(nameof(Index));
    }

    [HttpPost("/cart/update")]
    [ValidateAntiForgeryToken]
    [EnableRateLimiting("cart-write")]
    public async Task<IActionResult> Update([FromForm] UpdateCartItemRequestDto request)
    {
        if (request.ProductId <= 0 && string.IsNullOrWhiteSpace(request.CartItemKey))
        {
            return BadRequest("ProductId không hợp lệ.");
        }

        await _cart.UpdateQuantityAsync(request.ProductId, request.SellerId, request.CartItemKey, request.Quantity);
        return RedirectToAction(nameof(Index));
    }

    [HttpPost("/cart/remove")]
    [ValidateAntiForgeryToken]
    [EnableRateLimiting("cart-write")]
    public async Task<IActionResult> Remove([FromForm] RemoveCartItemRequestDto request)
    {
        if (request.ProductId <= 0 && string.IsNullOrWhiteSpace(request.CartItemKey))
        {
            return BadRequest("ProductId không hợp lệ.");
        }

        await _cart.RemoveAsync(request.ProductId, request.SellerId, request.CartItemKey);
        return RedirectToAction(nameof(Index));
    }

    [HttpPost("/cart/clear")]
    [ValidateAntiForgeryToken]
    [EnableRateLimiting("cart-write")]
    public async Task<IActionResult> Clear()
    {
        await _cart.ClearAsync();
        return RedirectToAction(nameof(Index));
    }

    private bool WantsJson()
    {
        var accept = Request.Headers.Accept.ToString();
        if (!string.IsNullOrWhiteSpace(accept) &&
            accept.Contains("application/json", StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        var requestedWith = Request.Headers["X-Requested-With"].ToString();
        return string.Equals(requestedWith, "XMLHttpRequest", StringComparison.OrdinalIgnoreCase);
    }

    private void TrackRecommendationAddToCartFireAndForget(AddToCartRequestDto request)
    {
        if (request.ProductId <= 0)
        {
            return;
        }

        var userId = ResolveRecommendationUserId();
        var experimentGroup = _recommendationExperimentService.ResolveGroup(HttpContext, userId);
        _ = _recommendationMetricsClient.TrackAddToCartAsync(
            userId,
            request.ProductId,
            NormalizeRecommendationPosition(request.RecommendationPosition ?? request.Position),
            experimentGroup,
            CancellationToken.None);
    }

    private int? ResolveRecommendationUserId()
    {
        var claimValue = User.FindFirstValue(ClaimTypes.NameIdentifier)
            ?? User.FindFirstValue(JwtRegisteredClaimNames.Sub)
            ?? User.FindFirstValue("sub")
            ?? User.FindFirstValue("userId")
            ?? User.FindFirstValue("uid");

        return int.TryParse(claimValue, out var userId) && userId > 0
            ? userId
            : null;
    }

    private static int? NormalizeRecommendationPosition(int? position)
    {
        return position is > 0 ? position.Value : null;
    }

    private async Task<List<CartRecommendationDto>> BuildCartRecommendationsAsync(IReadOnlyCollection<CartItemDto>? cartItems)
    {
        if (_httpClientFactory is null || cartItems is null || cartItems.Count == 0)
        {
            return new List<CartRecommendationDto>();
        }

        var cartProductIds = cartItems
            .Where(item => item.ProductId > 0)
            .OrderByDescending(item => Math.Max(1, item.Quantity))
            .Select(item => item.ProductId)
            .Distinct()
            .ToArray();
        if (cartProductIds.Length == 0)
        {
            return new List<CartRecommendationDto>();
        }

        var cartProductIdSet = cartProductIds.ToHashSet();
        var signals = await LoadBasketAffinitySignalsAsync(cartProductIds.Take(MaxCartRecommendationSeeds), cartProductIdSet);
        if (signals.Count > 0)
        {
            var candidateIds = signals
                .Select(signal => signal.CandidateProductId)
                .Where(productId => productId > 0 && !cartProductIdSet.Contains(productId))
                .Distinct()
                .Take(MaxCatalogRecommendationLookup)
                .ToArray();
            if (candidateIds.Length > 0)
            {
                var products = await LoadCatalogProductsAsync(candidateIds);
                var merchantLookup = await LoadMerchantLookupAsync(products.Values.Select(product => product.PrimarySellerId));
                var recommendations = new List<CartRecommendationDto>();
                foreach (var signal in signals.OrderByDescending(item => item.BasketScore).ThenByDescending(item => item.CoPurchaseOrderCount))
                {
                    if (!products.TryGetValue(signal.CandidateProductId, out var product) || !IsSellableRecommendationProduct(product))
                    {
                        continue;
                    }

                    recommendations.Add(ToCartRecommendation(product, signal.ProductId, signal.CoPurchaseOrderCount, signal.BasketScore, merchantLookup));

                    if (recommendations.Count >= MaxCartRecommendations)
                    {
                        break;
                    }
                }

                if (recommendations.Count > 0)
                {
                    return recommendations;
                }
            }
        }

        return await LoadFallbackCartRecommendationsAsync(cartProductIds, cartProductIdSet);
    }

    private async Task<List<CartRecommendationDto>> LoadFallbackCartRecommendationsAsync(
        IReadOnlyCollection<int> cartProductIds,
        ISet<int> cartProductIdSet)
    {
        var seedProducts = await LoadCatalogProductsAsync(cartProductIds);
        if (seedProducts.Count == 0)
        {
            return new List<CartRecommendationDto>();
        }

        var categoryIds = seedProducts.Values
            .Select(product => product.CategoryId)
            .Where(categoryId => categoryId > 0)
            .Distinct()
            .Take(MaxCartRecommendationSeeds)
            .ToArray();
        if (categoryIds.Length == 0)
        {
            return new List<CartRecommendationDto>();
        }

        var relatedProducts = await LoadCatalogProductsByCategoriesAsync(categoryIds);
        var merchantLookup = await LoadMerchantLookupAsync(relatedProducts.Select(product => product.PrimarySellerId));
        return relatedProducts
            .Where(product => !cartProductIdSet.Contains(product.ProductId) && IsSellableRecommendationProduct(product))
            .OrderByDescending(product => seedProducts.Values.Any(seed => seed.PrimarySellerId == product.PrimarySellerId))
            .ThenByDescending(product => product.AvailableStock)
            .ThenBy(product => product.ProductName)
            .Take(MaxCartRecommendations)
            .Select(product => ToCartRecommendation(product, seedProductId: 0, coPurchaseOrderCount: 0, basketScore: 0d, merchantLookup))
            .ToList();
    }

    private async Task<List<OrderingBasketAffinityApiDto>> LoadBasketAffinitySignalsAsync(
        IEnumerable<int> seedProductIds,
        ISet<int> cartProductIds)
    {
        var client = _httpClientFactory!.CreateClient("Ordering");
        AttachAccessToken(client);
        var aggregated = new Dictionary<int, OrderingBasketAffinityApiDto>();

        foreach (var seedProductId in seedProductIds.Where(id => id > 0).Distinct())
        {
            try
            {
                var response = await client.GetAsync($"/api/orders/product-insights/basket-affinity?productId={seedProductId}&limit={MaxCartRecommendationRowsPerSeed}");
                if (!response.IsSuccessStatusCode)
                {
                    continue;
                }

                var content = await response.Content.ReadAsStringAsync();
                var items = JsonSerializer.Deserialize<List<OrderingBasketAffinityApiDto>>(content, JsonOptions)
                            ?? new List<OrderingBasketAffinityApiDto>();
                foreach (var item in items.Where(item =>
                             item.CandidateProductId > 0 &&
                             item.BasketScore > 0d &&
                             !cartProductIds.Contains(item.CandidateProductId)))
                {
                    if (!aggregated.TryGetValue(item.CandidateProductId, out var current) ||
                        item.BasketScore > current.BasketScore)
                    {
                        aggregated[item.CandidateProductId] = item;
                    }
                }
            }
            catch (Exception)
            {
                // Cart recommendations are best-effort; the cart page should stay usable if insights are unavailable.
            }
        }

        return aggregated.Values.ToList();
    }

    private async Task<Dictionary<int, CatalogProductApiDto>> LoadCatalogProductsAsync(IReadOnlyCollection<int> productIds)
    {
        var normalizedProductIds = productIds
            .Where(id => id > 0)
            .Distinct()
            .Take(MaxCatalogRecommendationLookup)
            .ToArray();
        if (normalizedProductIds.Length == 0)
        {
            return new Dictionary<int, CatalogProductApiDto>();
        }

        var client = _httpClientFactory!.CreateClient("Catalog");
        AttachAccessToken(client);
        var query = normalizedProductIds.Select(productId => $"productIds={productId}").ToList();
        query.Add($"limit={Math.Clamp(normalizedProductIds.Length, 1, MaxCatalogRecommendationLookup)}");

        try
        {
            var response = await client.GetAsync("/api/products?" + string.Join("&", query));
            if (!response.IsSuccessStatusCode)
            {
                return new Dictionary<int, CatalogProductApiDto>();
            }

            var content = await response.Content.ReadAsStringAsync();
            var products = JsonSerializer.Deserialize<List<CatalogProductApiDto>>(content, JsonOptions)
                           ?? new List<CatalogProductApiDto>();
            return products
                .Where(product => product.ProductId > 0)
                .GroupBy(product => product.ProductId)
                .ToDictionary(group => group.Key, group => group.First());
        }
        catch (Exception)
        {
            return new Dictionary<int, CatalogProductApiDto>();
        }
    }

    private async Task<List<CatalogProductApiDto>> LoadCatalogProductsByCategoriesAsync(IReadOnlyCollection<int> categoryIds)
    {
        var normalizedCategoryIds = categoryIds
            .Where(id => id > 0)
            .Distinct()
            .Take(MaxCartRecommendationSeeds)
            .ToArray();
        if (normalizedCategoryIds.Length == 0)
        {
            return new List<CatalogProductApiDto>();
        }

        var client = _httpClientFactory!.CreateClient("Catalog");
        AttachAccessToken(client);
        var query = normalizedCategoryIds.Select(categoryId => $"categoryIds={categoryId}").ToList();
        query.Add($"limit={MaxCatalogRecommendationLookup}");

        try
        {
            var response = await client.GetAsync("/api/products?" + string.Join("&", query));
            if (!response.IsSuccessStatusCode)
            {
                return new List<CatalogProductApiDto>();
            }

            var content = await response.Content.ReadAsStringAsync();
            return JsonSerializer.Deserialize<List<CatalogProductApiDto>>(content, JsonOptions)
                   ?? new List<CatalogProductApiDto>();
        }
        catch (Exception)
        {
            return new List<CatalogProductApiDto>();
        }
    }

    private static bool IsSellableRecommendationProduct(CatalogProductApiDto product)
    {
        return product.ProductId > 0 &&
               product.PrimarySellerId is > 0 &&
               product.Status &&
               !product.IsManuallyDisabled &&
               product.AvailableStock > 0;
    }

    private static CartRecommendationDto ToCartRecommendation(
        CatalogProductApiDto product,
        int seedProductId,
        int coPurchaseOrderCount,
        double basketScore,
        IReadOnlyDictionary<int, PublicMerchantApiDto>? merchantLookup)
    {
        var sellerId = product.PrimarySellerId.GetValueOrDefault();
        PublicMerchantApiDto? merchant = null;
        merchantLookup?.TryGetValue(sellerId, out merchant);
        var sellerName = merchant?.ShopName ?? merchant?.UserName;
        return new CartRecommendationDto
        {
            ProductId = product.ProductId,
            SellerId = sellerId,
            SellerName = string.IsNullOrWhiteSpace(sellerName) ? $"Nhà bán hàng #{sellerId}" : sellerName.Trim(),
            ProductName = string.IsNullOrWhiteSpace(product.ProductName) ? $"Sản phẩm #{product.ProductId}" : product.ProductName.Trim(),
            ImageFileName = product.ImageFileName?.Trim() ?? string.Empty,
            UnitPrice = product.Price < 0m ? 0m : product.Price,
            UnitSymbol = string.IsNullOrWhiteSpace(product.UnitSymbol) ? "đơn vị" : product.UnitSymbol.Trim(),
            SeedProductId = seedProductId,
            CoPurchaseOrderCount = coPurchaseOrderCount,
            BasketScore = basketScore
        };
    }

    private async Task<IReadOnlyDictionary<int, PublicMerchantApiDto>> LoadMerchantLookupAsync(IEnumerable<int?> sellerIds)
    {
        var normalizedSellerIds = sellerIds
            .Where(id => id is > 0)
            .Select(id => id!.Value)
            .Distinct()
            .ToArray();
        if (normalizedSellerIds.Length == 0)
        {
            return new Dictionary<int, PublicMerchantApiDto>();
        }

        var client = _httpClientFactory!.CreateClient("Identity");
        var query = string.Join("&", normalizedSellerIds.Select(sellerId => $"sellerIds={sellerId}"));
        try
        {
            var response = await client.GetAsync($"/auth/public/merchants?{query}");
            if (!response.IsSuccessStatusCode)
            {
                return new Dictionary<int, PublicMerchantApiDto>();
            }

            var content = await response.Content.ReadAsStringAsync();
            return DeserializePublicMerchants(content)
                .Where(merchant => merchant.SellerId > 0)
                .GroupBy(merchant => merchant.SellerId)
                .ToDictionary(group => group.Key, group => SelectPreferredMerchant(group));
        }
        catch (HttpRequestException)
        {
            return new Dictionary<int, PublicMerchantApiDto>();
        }
    }

    private static List<PublicMerchantApiDto> DeserializePublicMerchants(string content)
    {
        if (string.IsNullOrWhiteSpace(content))
        {
            return new List<PublicMerchantApiDto>();
        }

        try
        {
            var merchants = JsonSerializer.Deserialize<List<PublicMerchantApiDto>>(content, JsonOptions);
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
            var payload = JsonSerializer.Deserialize<PublicMerchantListApiDto>(content, JsonOptions);
            return payload?.Merchants ?? new List<PublicMerchantApiDto>();
        }
        catch (JsonException)
        {
            return new List<PublicMerchantApiDto>();
        }
    }

    private static PublicMerchantApiDto SelectPreferredMerchant(IEnumerable<PublicMerchantApiDto> merchants)
    {
        return merchants
            .OrderByDescending(merchant => !string.IsNullOrWhiteSpace(merchant.ShopName))
            .ThenByDescending(merchant => !string.IsNullOrWhiteSpace(merchant.UserName))
            .ThenByDescending(merchant => merchant.JoinedAt ?? DateTime.MinValue)
            .First();
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

    private sealed class NoopRecommendationExperimentService : IRecommendationExperimentService
    {
        public static readonly NoopRecommendationExperimentService Instance = new();

        public string ResolveGroup(HttpContext httpContext, int? userId)
        {
            return RecommendationExperimentGroups.SessionRerank;
        }
    }

    private sealed class OrderingBasketAffinityApiDto
    {
        public int ProductId { get; set; }
        public int CandidateProductId { get; set; }
        public int CoPurchaseOrderCount { get; set; }
        public double BasketScore { get; set; }
    }

    private sealed class CatalogProductApiDto
    {
        public int ProductId { get; set; }
        public string? ProductName { get; set; }
        public decimal Price { get; set; }
        public bool Status { get; set; }
        public int AvailableStock { get; set; }
        public string? ImageFileName { get; set; }
        public string? UnitSymbol { get; set; }
        public bool IsManuallyDisabled { get; set; }
        public int CategoryId { get; set; }
        public int? PrimarySellerId { get; set; }
    }

    private sealed class PublicMerchantApiDto
    {
        public int SellerId { get; set; }
        public string? ShopName { get; set; }
        public string? UserName { get; set; }
        public DateTime? JoinedAt { get; set; }
    }

    private sealed class PublicMerchantListApiDto
    {
        public List<PublicMerchantApiDto>? Merchants { get; set; }
    }
}
