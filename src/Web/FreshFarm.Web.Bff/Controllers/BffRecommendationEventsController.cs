using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Text.Json;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.DependencyInjection;
using FreshFarm.Web.Bff.Services;

namespace FreshFarm.Web.Bff.Controllers;

[AllowAnonymous]
[ApiController]
[Route("bff/events")]
public sealed class BffRecommendationEventsController : ControllerBase
{
    private const string AccessTokenSessionKey = "ACCESS_TOKEN";
    private const string RecommendationSessionMarkerKey = "__recommendation_session_initialized";
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    private readonly IHttpClientFactory _httpClientFactory;
    private readonly IRecommendationMetricsClient _recommendationMetricsClient;
    private readonly IRecommendationExperimentService _recommendationExperimentService;

    public BffRecommendationEventsController(IHttpClientFactory httpClientFactory)
        : this(
            httpClientFactory,
            NoopRecommendationMetricsClient.Instance,
            NoopRecommendationExperimentService.Instance)
    {
    }

    public BffRecommendationEventsController(
        IHttpClientFactory httpClientFactory,
        IRecommendationMetricsClient recommendationMetricsClient)
        : this(
            httpClientFactory,
            recommendationMetricsClient,
            NoopRecommendationExperimentService.Instance)
    {
    }

    [ActivatorUtilitiesConstructor]
    public BffRecommendationEventsController(
        IHttpClientFactory httpClientFactory,
        IRecommendationMetricsClient recommendationMetricsClient,
        IRecommendationExperimentService recommendationExperimentService)
    {
        _httpClientFactory = httpClientFactory;
        _recommendationMetricsClient = recommendationMetricsClient;
        _recommendationExperimentService = recommendationExperimentService;
    }

    [HttpPost("product-view")]
    public async Task<IActionResult> TrackProductView([FromBody] TrackProductViewRequest? request)
    {
        var sessionId = await EnsureStableSessionIdAsync();
        return await ProxyTrackAsync(
            "/api/orders/recommendation-events/product-view",
            new
            {
                sessionId,
                productId = request?.ProductId ?? 0,
                sellerId = request?.SellerId,
                sourcePage = request?.SourcePage,
                sourceModule = request?.SourceModule
            },
            "Không thể ghi nhận lượt xem sản phẩm.");
    }

    [HttpPost("search")]
    public async Task<IActionResult> TrackSearch([FromBody] TrackSearchRequest? request)
    {
        var sessionId = await EnsureStableSessionIdAsync();
        return await ProxyTrackAsync(
            "/api/orders/recommendation-events/search",
            new
            {
                sessionId,
                keyword = request?.Keyword,
                filters = request?.Filters,
                resultCount = request?.ResultCount ?? 0
            },
            "Không thể ghi nhận sự kiện tìm kiếm.");
    }

    [HttpPost("search-click")]
    public async Task<IActionResult> TrackSearchClick([FromBody] TrackSearchClickRequest? request)
    {
        var sessionId = await EnsureStableSessionIdAsync();
        return await ProxyTrackAsync(
            "/api/orders/recommendation-events/search-click",
            new
            {
                sessionId,
                searchEventId = request?.SearchEventId,
                productId = request?.ProductId ?? 0,
                sellerId = request?.SellerId,
                rank = request?.Rank ?? 0
            },
            "Không thể ghi nhận lượt mở sản phẩm từ tìm kiếm.");
    }

    [HttpPost("recommendation-impression")]
    public async Task<IActionResult> TrackRecommendationImpression([FromBody] TrackRecommendationImpressionRequest? request)
    {
        var sessionId = await EnsureStableSessionIdAsync();
        return await ProxyTrackAsync(
            "/api/orders/recommendation-events/recommendation-impression",
            new
            {
                sessionId,
                placement = request?.Placement,
                recommendationRunId = request?.RecommendationRunId,
                productId = request?.ProductId ?? 0,
                rank = request?.Rank ?? 0,
                algorithm = request?.Algorithm
            },
            "Không thể ghi nhận impression recommendation.");
    }

    [HttpPost("recommendation-click")]
    public async Task<IActionResult> TrackRecommendationClick([FromBody] TrackRecommendationClickRequest? request)
    {
        var sessionId = await EnsureStableSessionIdAsync();
        TrackRecommendationMetricClickFireAndForget(request);

        return await ProxyTrackAsync(
            "/api/orders/recommendation-events/recommendation-click",
            new
            {
                sessionId,
                recommendationImpressionEventId = request?.RecommendationImpressionEventId,
                productId = request?.ProductId ?? 0,
                placement = request?.Placement,
                algorithm = request?.Algorithm
            },
            "Không thể ghi nhận click recommendation.");
    }

    [HttpPost("recommendation-add-to-cart")]
    public IActionResult TrackRecommendationAddToCart([FromBody] TrackRecommendationAddToCartRequest? request)
    {
        if (request is null || request.ProductId <= 0)
        {
            return BadRequest(new { ok = false, message = "ProductId không hợp lệ." });
        }

        var userId = ResolveRecommendationUserId();
        var experimentGroup = _recommendationExperimentService.ResolveGroup(HttpContext, userId);
        _ = _recommendationMetricsClient.TrackAddToCartAsync(
            userId,
            request.ProductId,
            NormalizeRecommendationPosition(request.Position ?? request.Rank),
            experimentGroup,
            CancellationToken.None);

        return Accepted(new { ok = true });
    }

    [HttpPost("recommendation-purchase")]
    public IActionResult TrackRecommendationPurchase([FromBody] TrackRecommendationPurchaseRequest? request)
    {
        if (request is null || request.ProductId <= 0)
        {
            return BadRequest(new { ok = false, message = "ProductId không hợp lệ." });
        }

        var userId = ResolveRecommendationUserId();
        var experimentGroup = _recommendationExperimentService.ResolveGroup(HttpContext, userId);
        _ = _recommendationMetricsClient.TrackPurchaseAsync(
            userId,
            request.ProductId,
            NormalizeRecommendationPosition(request.Position ?? request.Rank),
            request.Revenue,
            experimentGroup,
            CancellationToken.None);

        return Accepted(new { ok = true });
    }

    private void TrackRecommendationMetricClickFireAndForget(TrackRecommendationClickRequest? request)
    {
        if (request is null || request.ProductId <= 0)
        {
            return;
        }

        var userId = ResolveRecommendationUserId();
        var experimentGroup = _recommendationExperimentService.ResolveGroup(HttpContext, userId);
        _ = _recommendationMetricsClient.TrackClickAsync(
            userId,
            request.ProductId,
            NormalizeRecommendationPosition(request.Position ?? request.Rank),
            experimentGroup,
            CancellationToken.None);
    }

    private sealed class NoopRecommendationExperimentService : IRecommendationExperimentService
    {
        public static readonly NoopRecommendationExperimentService Instance = new();

        public string ResolveGroup(HttpContext httpContext, int? userId)
        {
            return RecommendationExperimentGroups.SessionRerank;
        }
    }

    private async Task<IActionResult> ProxyTrackAsync(string url, object payload, string fallbackMessage)
    {
        var client = CreateOrderingClient();
        var response = await client.PostAsJsonAsync(url, payload, JsonOptions);
        var body = await response.Content.ReadAsStringAsync();

        if (response.IsSuccessStatusCode)
        {
            return new ContentResult
            {
                Content = body,
                ContentType = "application/json",
                StatusCode = (int)response.StatusCode
            };
        }

        var message = ExtractMessage(body) ?? fallbackMessage;
        return new ObjectResult(new { ok = false, message })
        {
            StatusCode = (int)response.StatusCode
        };
    }

    private HttpClient CreateOrderingClient()
    {
        var client = _httpClientFactory.CreateClient("Ordering");
        client.DefaultRequestHeaders.Remove("Authorization");

        var token = HttpContext.Session.GetString(AccessTokenSessionKey);
        if (!string.IsNullOrWhiteSpace(token))
        {
            client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);
        }

        return client;
    }

    private int? ResolveRecommendationUserId()
    {
        var claimValue = User.FindFirstValue(ClaimTypes.NameIdentifier)
            ?? User.FindFirstValue(JwtRegisteredClaimNames.Sub)
            ?? User.FindFirstValue("sub")
            ?? User.FindFirstValue("userId")
            ?? User.FindFirstValue("uid");

        if (int.TryParse(claimValue, out var userId) && userId > 0)
        {
            return userId;
        }

        return ResolveRecommendationUserIdFromJwt(HttpContext.Session.GetString(AccessTokenSessionKey));
    }

    private static int? ResolveRecommendationUserIdFromJwt(string? token)
    {
        if (string.IsNullOrWhiteSpace(token))
        {
            return null;
        }

        try
        {
            var jwt = new JwtSecurityTokenHandler().ReadJwtToken(token);
            var claimValue = jwt.Subject
                ?? jwt.Claims.FirstOrDefault(claim =>
                    string.Equals(claim.Type, ClaimTypes.NameIdentifier, StringComparison.OrdinalIgnoreCase)
                    || string.Equals(claim.Type, JwtRegisteredClaimNames.Sub, StringComparison.OrdinalIgnoreCase)
                    || string.Equals(claim.Type, "sub", StringComparison.OrdinalIgnoreCase)
                    || string.Equals(claim.Type, "userId", StringComparison.OrdinalIgnoreCase)
                    || string.Equals(claim.Type, "uid", StringComparison.OrdinalIgnoreCase))?.Value;

            return int.TryParse(claimValue, out var userId) && userId > 0 ? userId : null;
        }
        catch (ArgumentException)
        {
            return null;
        }
    }

    private static int? NormalizeRecommendationPosition(int? position)
    {
        return position is > 0 ? position.Value : null;
    }

    private async Task<string> EnsureStableSessionIdAsync()
    {
        await HttpContext.Session.LoadAsync();
        if (!HttpContext.Session.TryGetValue(RecommendationSessionMarkerKey, out _))
        {
            HttpContext.Session.SetString(RecommendationSessionMarkerKey, "1");
        }

        return HttpContext.Session.Id;
    }

    private static string? ExtractMessage(string? body)
    {
        if (string.IsNullOrWhiteSpace(body))
        {
            return null;
        }

        try
        {
            using var document = JsonDocument.Parse(body);
            if (document.RootElement.ValueKind != JsonValueKind.Object)
            {
                return body;
            }

            if (document.RootElement.TryGetProperty("message", out var messageProperty) &&
                messageProperty.ValueKind == JsonValueKind.String)
            {
                return messageProperty.GetString();
            }
        }
        catch
        {
            return body;
        }

        return body;
    }

    public sealed class TrackProductViewRequest
    {
        public int ProductId { get; set; }

        public int? SellerId { get; set; }

        public string? SourcePage { get; set; }

        public string? SourceModule { get; set; }
    }

    public sealed class TrackSearchRequest
    {
        public string? Keyword { get; set; }

        public JsonElement? Filters { get; set; }

        public int ResultCount { get; set; }
    }

    public sealed class TrackSearchClickRequest
    {
        public int? SearchEventId { get; set; }

        public int ProductId { get; set; }

        public int? SellerId { get; set; }

        public int Rank { get; set; }
    }

    public sealed class TrackRecommendationImpressionRequest
    {
        public string? Placement { get; set; }

        public string? RecommendationRunId { get; set; }

        public int ProductId { get; set; }

        public int Rank { get; set; }

        public string? Algorithm { get; set; }
    }

    public sealed class TrackRecommendationClickRequest
    {
        public int? RecommendationImpressionEventId { get; set; }

        public int ProductId { get; set; }

        public int? Position { get; set; }

        public int? Rank { get; set; }

        public string? Placement { get; set; }

        public string? Algorithm { get; set; }
    }

    public sealed class TrackRecommendationAddToCartRequest
    {
        public int ProductId { get; set; }

        public int? Position { get; set; }

        public int? Rank { get; set; }
    }

    public sealed class TrackRecommendationPurchaseRequest
    {
        public int ProductId { get; set; }

        public int? Position { get; set; }

        public int? Rank { get; set; }

        public decimal Revenue { get; set; }
    }
}
