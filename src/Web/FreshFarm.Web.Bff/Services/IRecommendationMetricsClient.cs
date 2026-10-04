namespace FreshFarm.Web.Bff.Services;

public interface IRecommendationMetricsClient
{
    Task TrackImpressionsAsync(
        int? userId,
        IReadOnlyCollection<RecommendationMetricImpression> impressions,
        CancellationToken cancellationToken = default);

    Task TrackClickAsync(
        int? userId,
        int productId,
        int? position,
        string? experimentGroup,
        CancellationToken cancellationToken = default);

    Task TrackAddToCartAsync(
        int? userId,
        int productId,
        int? position,
        string? experimentGroup,
        CancellationToken cancellationToken = default);

    Task TrackPurchaseAsync(
        int? userId,
        int productId,
        int? position,
        decimal revenue,
        string? experimentGroup,
        CancellationToken cancellationToken = default);

    Task<IReadOnlyDictionary<int, RecommendationObjectiveMetric>> GetObjectiveMetricsAsync(
        IReadOnlyCollection<int> productIds,
        DateTime fromDateUtc,
        DateTime toDateUtc,
        CancellationToken cancellationToken = default);

    Task<IReadOnlyDictionary<int, RecommendationNegativeFeedbackMetric>> GetNegativeFeedbackAsync(
        int? userId,
        IReadOnlyCollection<int> productIds,
        DateTime fromDateUtc,
        DateTime toDateUtc,
        CancellationToken cancellationToken = default);
}

public sealed record RecommendationMetricImpression(
    int ProductId,
    int Position,
    string RecommendationSource,
    string ExperimentGroup);

public sealed record RecommendationObjectiveMetric(
    int ProductId,
    long TotalImpressions,
    long TotalClicks,
    long TotalAddToCarts,
    long TotalPurchases,
    decimal TotalRevenue,
    double? Ctr,
    double? AddToCartRate,
    double? PurchaseRate,
    decimal? RevenuePerImpression,
    string Status);

public sealed record RecommendationNegativeFeedbackMetric(
    int ProductId,
    long ImpressionNoClickCount,
    long RepeatedImpressionCount,
    long BounceCount,
    double PenaltyScore,
    DateTime? LastSignalAtUtc,
    string Status);

public sealed class NoopRecommendationMetricsClient : IRecommendationMetricsClient
{
    public static readonly NoopRecommendationMetricsClient Instance = new();

    private NoopRecommendationMetricsClient()
    {
    }

    public Task TrackImpressionsAsync(
        int? userId,
        IReadOnlyCollection<RecommendationMetricImpression> impressions,
        CancellationToken cancellationToken = default)
    {
        return Task.CompletedTask;
    }

    public Task TrackClickAsync(
        int? userId,
        int productId,
        int? position,
        string? experimentGroup,
        CancellationToken cancellationToken = default)
    {
        return Task.CompletedTask;
    }

    public Task TrackAddToCartAsync(
        int? userId,
        int productId,
        int? position,
        string? experimentGroup,
        CancellationToken cancellationToken = default)
    {
        return Task.CompletedTask;
    }

    public Task TrackPurchaseAsync(
        int? userId,
        int productId,
        int? position,
        decimal revenue,
        string? experimentGroup,
        CancellationToken cancellationToken = default)
    {
        return Task.CompletedTask;
    }

    public Task<IReadOnlyDictionary<int, RecommendationObjectiveMetric>> GetObjectiveMetricsAsync(
        IReadOnlyCollection<int> productIds,
        DateTime fromDateUtc,
        DateTime toDateUtc,
        CancellationToken cancellationToken = default)
    {
        return Task.FromResult<IReadOnlyDictionary<int, RecommendationObjectiveMetric>>(
            new Dictionary<int, RecommendationObjectiveMetric>());
    }

    public Task<IReadOnlyDictionary<int, RecommendationNegativeFeedbackMetric>> GetNegativeFeedbackAsync(
        int? userId,
        IReadOnlyCollection<int> productIds,
        DateTime fromDateUtc,
        DateTime toDateUtc,
        CancellationToken cancellationToken = default)
    {
        return Task.FromResult<IReadOnlyDictionary<int, RecommendationNegativeFeedbackMetric>>(
            new Dictionary<int, RecommendationNegativeFeedbackMetric>());
    }
}
