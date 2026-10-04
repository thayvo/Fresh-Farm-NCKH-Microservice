using System.Net.Http.Json;
using System.Text.Json;

namespace FreshFarm.Web.Bff.Services;

public sealed class RecommendationMetricsClient : IRecommendationMetricsClient
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);
    private static readonly TimeSpan RequestTimeout = TimeSpan.FromSeconds(1);
    private static readonly TimeSpan ObjectiveMetricsTimeout = TimeSpan.FromMilliseconds(20);
    private static readonly TimeSpan NegativeFeedbackTimeout = TimeSpan.FromMilliseconds(20);

    private readonly IHttpClientFactory _httpClientFactory;
    private readonly ILogger<RecommendationMetricsClient> _logger;

    public RecommendationMetricsClient(
        IHttpClientFactory httpClientFactory,
        ILogger<RecommendationMetricsClient> logger)
    {
        _httpClientFactory = httpClientFactory;
        _logger = logger;
    }

    public async Task TrackImpressionsAsync(
        int? userId,
        IReadOnlyCollection<RecommendationMetricImpression> impressions,
        CancellationToken cancellationToken = default)
    {
        if (impressions.Count == 0)
        {
            return;
        }

        var payload = new
        {
            userId = NormalizePositiveInt(userId),
            items = impressions
                .Where(item => item.ProductId > 0 && item.Position > 0)
                .Select(item => new
                {
                    productId = item.ProductId,
                    position = item.Position,
                    recommendationSource = NormalizeRecommendationSource(item.RecommendationSource),
                    experimentGroup = RecommendationExperimentGroups.Normalize(item.ExperimentGroup)
                })
                .ToArray()
        };

        if (payload.items.Length == 0)
        {
            return;
        }

        await PostMetricAsync("/api/orders/recommendation-metrics/impressions", payload, cancellationToken);
    }

    public async Task TrackClickAsync(
        int? userId,
        int productId,
        int? position,
        string? experimentGroup,
        CancellationToken cancellationToken = default)
    {
        if (productId <= 0)
        {
            return;
        }

        var payload = new
        {
            userId = NormalizePositiveInt(userId),
            productId,
            position = NormalizePositiveInt(position),
            experimentGroup = RecommendationExperimentGroups.Normalize(experimentGroup)
        };

        await PostMetricAsync("/api/orders/recommendation-metrics/click", payload, cancellationToken);
    }

    public async Task TrackAddToCartAsync(
        int? userId,
        int productId,
        int? position,
        string? experimentGroup,
        CancellationToken cancellationToken = default)
    {
        if (productId <= 0)
        {
            return;
        }

        var payload = new
        {
            userId = NormalizePositiveInt(userId),
            productId,
            position = NormalizePositiveInt(position),
            experimentGroup = RecommendationExperimentGroups.Normalize(experimentGroup)
        };

        await PostMetricAsync("/api/orders/recommendation-metrics/add-to-cart", payload, cancellationToken);
    }

    public async Task TrackPurchaseAsync(
        int? userId,
        int productId,
        int? position,
        decimal revenue,
        string? experimentGroup,
        CancellationToken cancellationToken = default)
    {
        if (productId <= 0)
        {
            return;
        }

        var payload = new
        {
            userId = NormalizePositiveInt(userId),
            productId,
            position = NormalizePositiveInt(position),
            revenue = revenue > 0m ? Math.Round(revenue, 2, MidpointRounding.AwayFromZero) : 0m,
            experimentGroup = RecommendationExperimentGroups.Normalize(experimentGroup)
        };

        await PostMetricAsync("/api/orders/recommendation-metrics/purchase", payload, cancellationToken);
    }

    public async Task<IReadOnlyDictionary<int, RecommendationObjectiveMetric>> GetObjectiveMetricsAsync(
        IReadOnlyCollection<int> productIds,
        DateTime fromDateUtc,
        DateTime toDateUtc,
        CancellationToken cancellationToken = default)
    {
        var normalizedProductIds = productIds
            .Where(productId => productId > 0)
            .Distinct()
            .Take(500)
            .ToArray();
        if (normalizedProductIds.Length == 0)
        {
            return new Dictionary<int, RecommendationObjectiveMetric>();
        }

        var query = string.Join("&", normalizedProductIds.Select(productId => $"productIds={productId}"));
        query += $"&fromDate={Uri.EscapeDataString(fromDateUtc.ToUniversalTime().ToString("O"))}";
        query += $"&toDate={Uri.EscapeDataString(toDateUtc.ToUniversalTime().ToString("O"))}";

        try
        {
            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            timeout.CancelAfter(ObjectiveMetricsTimeout);

            var client = _httpClientFactory.CreateClient("Ordering");
            var report = await client.GetFromJsonAsync<RecommendationObjectiveMetricsReport>(
                $"/api/orders/recommendation-metrics/objective?{query}",
                JsonOptions,
                timeout.Token);

            return report?.Metrics?
                .Where(metric => metric.ProductId > 0)
                .GroupBy(metric => metric.ProductId)
                .ToDictionary(
                    group => group.Key,
                    group => group.First())
                ?? new Dictionary<int, RecommendationObjectiveMetric>();
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            _logger.LogDebug("Recommendation objective metrics request timed out.");
        }
        catch (Exception ex)
        {
            _logger.LogDebug(ex, "Recommendation objective metrics request failed.");
        }

        return new Dictionary<int, RecommendationObjectiveMetric>();
    }

    public async Task<IReadOnlyDictionary<int, RecommendationNegativeFeedbackMetric>> GetNegativeFeedbackAsync(
        int? userId,
        IReadOnlyCollection<int> productIds,
        DateTime fromDateUtc,
        DateTime toDateUtc,
        CancellationToken cancellationToken = default)
    {
        var normalizedProductIds = productIds
            .Where(productId => productId > 0)
            .Distinct()
            .Take(500)
            .ToArray();
        if (normalizedProductIds.Length == 0)
        {
            return new Dictionary<int, RecommendationNegativeFeedbackMetric>();
        }

        var queryParts = normalizedProductIds.Select(productId => $"productIds={productId}").ToList();
        if (userId is > 0)
        {
            queryParts.Add($"userId={userId.Value}");
        }

        queryParts.Add($"fromDate={Uri.EscapeDataString(fromDateUtc.ToUniversalTime().ToString("O"))}");
        queryParts.Add($"toDate={Uri.EscapeDataString(toDateUtc.ToUniversalTime().ToString("O"))}");
        var query = string.Join("&", queryParts);

        try
        {
            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            timeout.CancelAfter(NegativeFeedbackTimeout);

            var client = _httpClientFactory.CreateClient("Ordering");
            var report = await client.GetFromJsonAsync<RecommendationNegativeFeedbackReport>(
                $"/api/orders/recommendation-metrics/negative-feedback?{query}",
                JsonOptions,
                timeout.Token);

            return report?.Metrics?
                .Where(metric => metric.ProductId > 0 && metric.PenaltyScore < 0d)
                .GroupBy(metric => metric.ProductId)
                .ToDictionary(
                    group => group.Key,
                    group => group.First())
                ?? new Dictionary<int, RecommendationNegativeFeedbackMetric>();
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            _logger.LogDebug("Recommendation negative feedback request timed out.");
        }
        catch (Exception ex)
        {
            _logger.LogDebug(ex, "Recommendation negative feedback request failed.");
        }

        return new Dictionary<int, RecommendationNegativeFeedbackMetric>();
    }

    private async Task PostMetricAsync(
        string url,
        object payload,
        CancellationToken cancellationToken)
    {
        try
        {
            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            timeout.CancelAfter(RequestTimeout);

            var client = _httpClientFactory.CreateClient("Ordering");
            using var response = await client.PostAsJsonAsync(url, payload, JsonOptions, timeout.Token);
            if (!response.IsSuccessStatusCode)
            {
                _logger.LogDebug(
                    "Recommendation metrics endpoint returned {StatusCode} for {Url}.",
                    (int)response.StatusCode,
                    url);
            }
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            _logger.LogDebug("Recommendation metrics request timed out for {Url}.", url);
        }
        catch (Exception ex)
        {
            _logger.LogDebug(ex, "Recommendation metrics request failed for {Url}.", url);
        }
    }

    private static int? NormalizePositiveInt(int? value)
    {
        return value is > 0 ? value.Value : null;
    }

    private static string NormalizeRecommendationSource(string? source)
    {
        return string.Equals(source, "Session", StringComparison.OrdinalIgnoreCase)
            ? "Session"
            : "ML";
    }

    private sealed class RecommendationObjectiveMetricsReport
    {
        public List<RecommendationObjectiveMetric> Metrics { get; set; } = new();
    }

    private sealed class RecommendationNegativeFeedbackReport
    {
        public List<RecommendationNegativeFeedbackMetric> Metrics { get; set; } = new();
    }
}
