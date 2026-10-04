using FreshFarm.Ordering.Api.Dtos;
using FreshFarm.Ordering.Api.Models;

namespace FreshFarm.Ordering.Api.Services;

public interface IRecommendationMetricsService
{
    Task TrackImpressionsAsync(
        int? userId,
        IReadOnlyCollection<RecommendationImpressionMetricItemDto> items,
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
        decimal? revenue,
        string? experimentGroup,
        CancellationToken cancellationToken = default);

    Task<RecommendationCtrReportDto> GetCtrAsync(
        DateTime? fromDateUtc,
        DateTime? toDateUtc,
        CancellationToken cancellationToken = default);

    Task<RecommendationCtrByPositionReportDto> GetCtrByPositionAsync(
        DateTime? fromDateUtc,
        DateTime? toDateUtc,
        CancellationToken cancellationToken = default);

    Task<RecommendationObjectiveMetricsReportDto> GetObjectiveMetricsAsync(
        DateTime? fromDateUtc,
        DateTime? toDateUtc,
        IReadOnlyCollection<int> productIds,
        CancellationToken cancellationToken = default);

    Task<RecommendationNegativeFeedbackReportDto> GetNegativeFeedbackAsync(
        int? userId,
        DateTime? fromDateUtc,
        DateTime? toDateUtc,
        IReadOnlyCollection<int> productIds,
        CancellationToken cancellationToken = default);
}

public sealed class RecommendationMetricsService : IRecommendationMetricsService
{
    private const int MaxImpressionBatchSize = 50;
    private const string MlSource = "ML";
    private const string SessionSource = "Session";
    private const string ExperimentGroupA = "A";
    private const string ExperimentGroupB = "B";
    private const int MinimumCtrImpressions = 100;
    private const int MinimumObjectiveImpressions = 100;
    private const int NegativeFeedbackDefaultLookbackDays = 14;
    private const string StatusOk = "ok";
    private const string StatusInsufficientData = "insufficient_data";
    private const string StatusBaselineZero = "baseline_zero";

    private readonly IRecommendationMetricsDao _dao;

    public RecommendationMetricsService(IRecommendationMetricsDao dao)
    {
        _dao = dao;
    }

    public async Task TrackImpressionsAsync(
        int? userId,
        IReadOnlyCollection<RecommendationImpressionMetricItemDto> items,
        CancellationToken cancellationToken = default)
    {
        if (items.Count == 0)
        {
            return;
        }

        var normalizedUserId = NormalizePositiveInt(userId);
        var timestamp = DateTime.UtcNow;
        var impressions = items
            .Where(item => item.ProductId > 0)
            .Take(MaxImpressionBatchSize)
            .Select(item => new RecommendationImpression
            {
                UserId = normalizedUserId,
                ProductId = item.ProductId,
                Position = NormalizePositiveInt(item.Position),
                RecommendationSource = NormalizeRecommendationSource(item.RecommendationSource),
                ExperimentGroup = NormalizeExperimentGroup(item.ExperimentGroup),
                Timestamp = timestamp
            })
            .ToArray();

        await _dao.AddImpressionsAsync(impressions, cancellationToken);
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

        var click = new RecommendationClick
        {
            UserId = NormalizePositiveInt(userId),
            ProductId = productId,
            Position = NormalizePositiveInt(position),
            ExperimentGroup = NormalizeExperimentGroup(experimentGroup),
            Timestamp = DateTime.UtcNow
        };

        await _dao.AddClickAsync(click, cancellationToken);
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

        var timestamp = DateTime.UtcNow;
        var normalizedUserId = NormalizePositiveInt(userId);
        var normalizedPosition = NormalizePositiveInt(position);
        var attribution = await _dao.FindLatestAttributionAsync(
            normalizedUserId,
            productId,
            normalizedPosition,
            timestamp,
            cancellationToken);

        var addToCart = new RecommendationAddToCart
        {
            UserId = normalizedUserId,
            ProductId = productId,
            Position = normalizedPosition,
            ExperimentGroup = attribution is null
                ? NormalizeExperimentGroup(experimentGroup)
                : NormalizeExperimentGroup(attribution.ExperimentGroup),
            Timestamp = timestamp
        };

        await _dao.AddAddToCartAsync(addToCart, cancellationToken);
    }

    public async Task TrackPurchaseAsync(
        int? userId,
        int productId,
        int? position,
        decimal? revenue,
        string? experimentGroup,
        CancellationToken cancellationToken = default)
    {
        if (productId <= 0)
        {
            return;
        }

        var timestamp = DateTime.UtcNow;
        var normalizedUserId = NormalizePositiveInt(userId);
        var normalizedPosition = NormalizePositiveInt(position);
        var attribution = await _dao.FindLatestAttributionAsync(
            normalizedUserId,
            productId,
            normalizedPosition,
            timestamp,
            cancellationToken);

        var purchase = new RecommendationPurchase
        {
            UserId = normalizedUserId,
            ProductId = productId,
            Position = normalizedPosition,
            Revenue = NormalizeNonNegativeMoney(revenue),
            ExperimentGroup = attribution is null
                ? NormalizeExperimentGroup(experimentGroup)
                : NormalizeExperimentGroup(attribution.ExperimentGroup),
            Timestamp = timestamp
        };

        await _dao.AddPurchaseAsync(purchase, cancellationToken);
    }

    public async Task<RecommendationCtrReportDto> GetCtrAsync(
        DateTime? fromDateUtc,
        DateTime? toDateUtc,
        CancellationToken cancellationToken = default)
    {
        var (normalizedFromDateUtc, normalizedToDateUtc) = ResolveCtrWindow(fromDateUtc, toDateUtc);
        var metrics = await _dao.GetCtrAsync(
            normalizedFromDateUtc,
            normalizedToDateUtc,
            MinimumCtrImpressions,
            cancellationToken);

        return new RecommendationCtrReportDto
        {
            FromDateUtc = normalizedFromDateUtc,
            ToDateUtc = normalizedToDateUtc,
            MinimumImpressions = MinimumCtrImpressions,
            Metrics = metrics,
            Comparison = BuildComparison(metrics)
        };
    }

    public async Task<RecommendationCtrByPositionReportDto> GetCtrByPositionAsync(
        DateTime? fromDateUtc,
        DateTime? toDateUtc,
        CancellationToken cancellationToken = default)
    {
        var (normalizedFromDateUtc, normalizedToDateUtc) = ResolveCtrWindow(fromDateUtc, toDateUtc);
        var metrics = await _dao.GetCtrByPositionAsync(
            normalizedFromDateUtc,
            normalizedToDateUtc,
            MinimumCtrImpressions,
            cancellationToken);

        return new RecommendationCtrByPositionReportDto
        {
            FromDateUtc = normalizedFromDateUtc,
            ToDateUtc = normalizedToDateUtc,
            MinimumImpressions = MinimumCtrImpressions,
            Metrics = metrics
        };
    }

    public async Task<RecommendationObjectiveMetricsReportDto> GetObjectiveMetricsAsync(
        DateTime? fromDateUtc,
        DateTime? toDateUtc,
        IReadOnlyCollection<int> productIds,
        CancellationToken cancellationToken = default)
    {
        var (normalizedFromDateUtc, normalizedToDateUtc) = ResolveCtrWindow(fromDateUtc, toDateUtc);
        var global = await _dao.GetObjectiveGlobalMetricsAsync(
            normalizedFromDateUtc,
            normalizedToDateUtc,
            productIds,
            MinimumObjectiveImpressions,
            cancellationToken);
        var groups = await _dao.GetObjectiveGroupMetricsAsync(
            normalizedFromDateUtc,
            normalizedToDateUtc,
            productIds,
            MinimumObjectiveImpressions,
            cancellationToken);
        var metrics = await _dao.GetObjectiveMetricsAsync(
            normalizedFromDateUtc,
            normalizedToDateUtc,
            productIds,
            MinimumObjectiveImpressions,
            cancellationToken);

        return new RecommendationObjectiveMetricsReportDto
        {
            FromDateUtc = normalizedFromDateUtc,
            ToDateUtc = normalizedToDateUtc,
            MinimumImpressions = MinimumObjectiveImpressions,
            Global = global,
            Groups = groups,
            Comparison = BuildObjectiveComparison(groups),
            Metrics = metrics
        };
    }

    public async Task<RecommendationNegativeFeedbackReportDto> GetNegativeFeedbackAsync(
        int? userId,
        DateTime? fromDateUtc,
        DateTime? toDateUtc,
        IReadOnlyCollection<int> productIds,
        CancellationToken cancellationToken = default)
    {
        var (normalizedFromDateUtc, normalizedToDateUtc) = ResolveWindow(
            fromDateUtc,
            toDateUtc,
            NegativeFeedbackDefaultLookbackDays);
        var metrics = await _dao.GetNegativeFeedbackAsync(
            NormalizePositiveInt(userId),
            normalizedFromDateUtc,
            normalizedToDateUtc,
            productIds,
            cancellationToken);

        return new RecommendationNegativeFeedbackReportDto
        {
            FromDateUtc = normalizedFromDateUtc,
            ToDateUtc = normalizedToDateUtc,
            UserId = NormalizePositiveInt(userId),
            Metrics = metrics
        };
    }

    private static int? NormalizePositiveInt(int? value)
    {
        return value is > 0 ? value.Value : null;
    }

    private static decimal NormalizeNonNegativeMoney(decimal? value)
    {
        return value is > 0m ? Math.Round(value.Value, 2, MidpointRounding.AwayFromZero) : 0m;
    }

    private static string NormalizeRecommendationSource(string? source)
    {
        if (string.Equals(source, SessionSource, StringComparison.OrdinalIgnoreCase))
        {
            return SessionSource;
        }

        return MlSource;
    }

    private static string NormalizeExperimentGroup(string? group)
    {
        if (string.Equals(group, ExperimentGroupB, StringComparison.OrdinalIgnoreCase))
        {
            return ExperimentGroupB;
        }

        return ExperimentGroupA;
    }

    private static DateTime? NormalizeUtcBoundary(DateTime? value)
    {
        if (!value.HasValue)
        {
            return null;
        }

        return value.Value.Kind switch
        {
            DateTimeKind.Utc => value.Value,
            DateTimeKind.Local => value.Value.ToUniversalTime(),
            _ => DateTime.SpecifyKind(value.Value, DateTimeKind.Utc)
        };
    }

    private static (DateTime FromDateUtc, DateTime ToDateUtc) ResolveCtrWindow(
        DateTime? fromDateUtc,
        DateTime? toDateUtc)
        => ResolveWindow(fromDateUtc, toDateUtc, defaultLookbackDays: 7);

    private static (DateTime FromDateUtc, DateTime ToDateUtc) ResolveWindow(
        DateTime? fromDateUtc,
        DateTime? toDateUtc,
        int defaultLookbackDays)
    {
        var normalizedToDateUtc = NormalizeUtcBoundary(toDateUtc) ?? DateTime.UtcNow;
        var normalizedFromDateUtc = NormalizeUtcBoundary(fromDateUtc) ?? normalizedToDateUtc.AddDays(-Math.Max(1, defaultLookbackDays));

        return (normalizedFromDateUtc, normalizedToDateUtc);
    }

    private static RecommendationCtrComparisonDto BuildComparison(
        IReadOnlyList<RecommendationCtrMetricDto> metrics)
    {
        var baseline = metrics.FirstOrDefault(metric =>
            string.Equals(metric.ExperimentGroup, ExperimentGroupA, StringComparison.OrdinalIgnoreCase)
            && string.Equals(metric.RecommendationSource, MlSource, StringComparison.OrdinalIgnoreCase));
        var variant = metrics.FirstOrDefault(metric =>
            string.Equals(metric.ExperimentGroup, ExperimentGroupB, StringComparison.OrdinalIgnoreCase)
            && string.Equals(metric.RecommendationSource, SessionSource, StringComparison.OrdinalIgnoreCase));
        var bothSufficient = string.Equals(baseline?.Status, StatusOk, StringComparison.OrdinalIgnoreCase)
            && string.Equals(variant?.Status, StatusOk, StringComparison.OrdinalIgnoreCase)
            && baseline?.CtrPercentage.HasValue == true
            && variant?.CtrPercentage.HasValue == true;

        double? absoluteUplift = bothSufficient
            ? Math.Round(variant!.CtrPercentage!.Value - baseline!.CtrPercentage!.Value, 4)
            : null;
        double? percentageUplift = null;
        var status = bothSufficient ? StatusOk : StatusInsufficientData;
        if (bothSufficient && baseline!.CtrPercentage!.Value > 0d)
        {
            percentageUplift = Math.Round(
                absoluteUplift!.Value * 100d / baseline.CtrPercentage.Value,
                4);
        }
        else if (bothSufficient)
        {
            status = StatusBaselineZero;
        }

        return new RecommendationCtrComparisonDto
        {
            BaselineImpressions = baseline?.TotalImpressions ?? 0,
            BaselineClicks = baseline?.TotalClicks ?? 0,
            BaselineCtrPercentage = baseline?.CtrPercentage,
            VariantImpressions = variant?.TotalImpressions ?? 0,
            VariantClicks = variant?.TotalClicks ?? 0,
            VariantCtrPercentage = variant?.CtrPercentage,
            AbsoluteUpliftPercentagePoints = absoluteUplift,
            PercentageUplift = percentageUplift,
            Status = status
        };
    }

    private static RecommendationObjectiveComparisonDto BuildObjectiveComparison(
        IReadOnlyList<RecommendationObjectiveGroupMetricDto> metrics)
    {
        var baseline = metrics.FirstOrDefault(metric =>
            string.Equals(metric.ExperimentGroup, ExperimentGroupA, StringComparison.OrdinalIgnoreCase)
            && string.Equals(metric.RecommendationSource, MlSource, StringComparison.OrdinalIgnoreCase));
        var variant = metrics.FirstOrDefault(metric =>
            string.Equals(metric.ExperimentGroup, ExperimentGroupB, StringComparison.OrdinalIgnoreCase)
            && string.Equals(metric.RecommendationSource, SessionSource, StringComparison.OrdinalIgnoreCase));
        var bothSufficient = string.Equals(baseline?.Status, StatusOk, StringComparison.OrdinalIgnoreCase)
            && string.Equals(variant?.Status, StatusOk, StringComparison.OrdinalIgnoreCase)
            && baseline?.PurchaseRate.HasValue == true
            && variant?.PurchaseRate.HasValue == true
            && baseline?.RevenuePerImpression.HasValue == true
            && variant?.RevenuePerImpression.HasValue == true;

        return new RecommendationObjectiveComparisonDto
        {
            BaselineImpressions = baseline?.TotalImpressions ?? 0,
            BaselinePurchaseRate = baseline?.PurchaseRate,
            BaselineRevenuePerImpression = baseline?.RevenuePerImpression,
            VariantImpressions = variant?.TotalImpressions ?? 0,
            VariantPurchaseRate = variant?.PurchaseRate,
            VariantRevenuePerImpression = variant?.RevenuePerImpression,
            PurchaseRateUplift = bothSufficient
                ? Math.Round(variant!.PurchaseRate!.Value - baseline!.PurchaseRate!.Value, 6)
                : null,
            RevenuePerImpressionUplift = bothSufficient
                ? Math.Round(variant!.RevenuePerImpression!.Value - baseline!.RevenuePerImpression!.Value, 6)
                : null,
            Status = bothSufficient ? StatusOk : StatusInsufficientData
        };
    }
}
