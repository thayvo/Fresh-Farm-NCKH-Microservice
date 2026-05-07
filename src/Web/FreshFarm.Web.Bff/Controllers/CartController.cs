using System.Text.Json;
using System.IdentityModel.Tokens.Jwt;
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

    private readonly ICartSessionService _cart;
    private readonly IRecommendationMetricsClient _recommendationMetricsClient;
    private readonly IRecommendationExperimentService _recommendationExperimentService;

    public CartController(ICartSessionService cart)
        : this(
            cart,
            NoopRecommendationMetricsClient.Instance,
            NoopRecommendationExperimentService.Instance)
    {
    }

    [ActivatorUtilitiesConstructor]
    public CartController(
        ICartSessionService cart,
        IRecommendationMetricsClient recommendationMetricsClient,
        IRecommendationExperimentService recommendationExperimentService)
    {
        _cart = cart;
        _recommendationMetricsClient = recommendationMetricsClient;
        _recommendationExperimentService = recommendationExperimentService;
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

    private sealed class NoopRecommendationExperimentService : IRecommendationExperimentService
    {
        public static readonly NoopRecommendationExperimentService Instance = new();

        public string ResolveGroup(HttpContext httpContext, int? userId)
        {
            return RecommendationExperimentGroups.SessionRerank;
        }
    }
}
