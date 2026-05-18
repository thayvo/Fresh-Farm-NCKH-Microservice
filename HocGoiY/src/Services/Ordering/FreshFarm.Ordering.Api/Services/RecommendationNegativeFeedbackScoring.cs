using FreshFarm.Ordering.Api.Models;
using Microsoft.EntityFrameworkCore;

namespace FreshFarm.Ordering.Api.Services;

public sealed record RecommendationNegativeFeedbackScore(
    int ProductId,
    int? UserId,
    long ImpressionNoClickCount,
    long RepeatedImpressionCount,
    long BounceCount,
    double PenaltyScore,
    DateTime? LastSignalAtUtc);

public sealed record RecommendationNegativeFeedbackExposure(DateTime TimestampUtc, int? Position);

public static class RecommendationNegativeFeedbackScoring
{
    public const double ImpressionNoClickWeight = -0.2d;
    public const double RepeatedImpressionWeight = -0.5d;
    public const double BounceWeight = -0.7d;
    public const int MinimumImpressionBucketsForPenalty = 3;
    public const double NegativeSignalHalfLifeDays = 12d;

    private const double MaxPenaltyScore = -3d;
    private static readonly TimeSpan ClickAttributionWindow = TimeSpan.FromHours(24);

    public static async Task<IReadOnlyList<RecommendationNegativeFeedbackScore>> LoadScoresAsync(
        FreshFarmOrderingDBContext db,
        DateTime fromDateUtc,
        DateTime toDateUtc,
        DateTime computedAtUtc,
        int? userId,
        IReadOnlyCollection<int>? productIds,
        CancellationToken cancellationToken = default)
    {
        var normalizedUserId = userId is > 0 ? userId.Value : (int?)null;
        var normalizedProductIds = productIds?
            .Where(productId => productId > 0)
            .Distinct()
            .Take(500)
            .ToArray() ?? Array.Empty<int>();

        var impressionsQuery = db.RecommendationImpressions
            .AsNoTracking()
            .Where(item =>
                item.Timestamp >= fromDateUtc
                && item.Timestamp < toDateUtc
                && item.UserId.HasValue
                && item.UserId.Value > 0
                && item.ProductId > 0);

        if (normalizedUserId.HasValue)
        {
            impressionsQuery = impressionsQuery.Where(item => item.UserId == normalizedUserId.Value);
        }

        if (normalizedProductIds.Length > 0)
        {
            impressionsQuery = impressionsQuery.Where(item => normalizedProductIds.Contains(item.ProductId));
        }

        var impressions = await impressionsQuery
            .Select(item => new NegativeImpression(
                item.UserId!.Value,
                item.ProductId,
                item.Position,
                item.Timestamp))
            .ToListAsync(cancellationToken);
        if (impressions.Count == 0)
        {
            return Array.Empty<RecommendationNegativeFeedbackScore>();
        }

        var clicksQuery = db.RecommendationClicks
            .AsNoTracking()
            .Where(item =>
                item.Timestamp >= fromDateUtc
                && item.Timestamp < toDateUtc
                && item.UserId.HasValue
                && item.UserId.Value > 0
                && item.ProductId > 0);

        if (normalizedUserId.HasValue)
        {
            clicksQuery = clicksQuery.Where(item => item.UserId == normalizedUserId.Value);
        }

        if (normalizedProductIds.Length > 0)
        {
            clicksQuery = clicksQuery.Where(item => normalizedProductIds.Contains(item.ProductId));
        }

        var clicksByKey = (await clicksQuery
                .Select(item => new NegativeClick(
                    item.UserId!.Value,
                    item.ProductId,
                    item.Position,
                    item.Timestamp))
                .ToListAsync(cancellationToken))
            .GroupBy(item => new NegativeSignalKey(item.UserId, item.ProductId, item.Position))
            .ToDictionary(
                group => group.Key,
                group => group
                    .Select(item => item.Timestamp)
                    .OrderBy(item => item)
                    .ToArray());

        return impressions
            .Where(impression => !HasMatchingClick(impression, clicksByKey))
            .GroupBy(impression => new { impression.UserId, impression.ProductId })
            .Select(group =>
            {
                var exposures = group
                    .Select(item => new RecommendationNegativeFeedbackExposure(item.Timestamp, item.Position))
                    .OrderBy(item => item.TimestampUtc)
                    .ToArray();
                return Calculate(
                    group.Key.ProductId,
                    group.Key.UserId,
                    exposures,
                    Array.Empty<DateTime>(),
                    computedAtUtc);
            })
            .Where(score => score.PenaltyScore < 0d)
            .OrderBy(item => item.UserId)
            .ThenBy(item => item.ProductId)
            .ToArray();
    }

    public static async Task<IReadOnlyDictionary<(int UserId, int ProductId), RecommendationNegativeFeedbackScore>>
        LoadUserProductScoreMapAsync(
            FreshFarmOrderingDBContext db,
            DateTime fromDateUtc,
            DateTime toDateUtc,
            DateTime computedAtUtc,
            CancellationToken cancellationToken = default)
    {
        var scores = await LoadScoresAsync(
            db,
            fromDateUtc,
            toDateUtc,
            computedAtUtc,
            userId: null,
            productIds: null,
            cancellationToken);

        return scores
            .Where(score => score.UserId.HasValue)
            .ToDictionary(score => (score.UserId!.Value, score.ProductId));
    }

    public static RecommendationNegativeFeedbackScore Calculate(
        int productId,
        int? userId,
        IReadOnlyList<DateTime> impressionNoClickTimestampsUtc,
        IReadOnlyList<DateTime> bounceTimestampsUtc,
        DateTime computedAtUtc)
        => Calculate(
            productId,
            userId,
            impressionNoClickTimestampsUtc
                .Select(timestamp => new RecommendationNegativeFeedbackExposure(timestamp, Position: null))
                .ToArray(),
            bounceTimestampsUtc,
            computedAtUtc);

    public static RecommendationNegativeFeedbackScore Calculate(
        int productId,
        int? userId,
        IReadOnlyList<RecommendationNegativeFeedbackExposure> impressionNoClickExposures,
        IReadOnlyList<DateTime> bounceTimestampsUtc,
        DateTime computedAtUtc)
    {
        var orderedNoClickExposures = impressionNoClickExposures
            .GroupBy(item => ToHourlyBucketUtc(item.TimestampUtc))
            .Select(group => group
                .OrderByDescending(item => ResolvePositionPenaltyScale(item.Position))
                .ThenBy(item => item.Position ?? int.MaxValue)
                .ThenByDescending(item => item.TimestampUtc)
                .First())
            .OrderBy(item => item.TimestampUtc)
            .ToArray();
        var orderedBounceTimestamps = bounceTimestampsUtc
            .OrderBy(item => item)
            .ToArray();

        var penalty = 0d;
        if (orderedNoClickExposures.Length >= MinimumImpressionBucketsForPenalty)
        {
            var repeatedExposureFactors = new List<double>(Math.Max(0, orderedNoClickExposures.Length - 1));
            for (var index = 0; index < orderedNoClickExposures.Length; index++)
            {
                var exposure = orderedNoClickExposures[index];
                var decay = CalculateDecay(exposure.TimestampUtc, computedAtUtc);
                var positionScale = ResolvePositionPenaltyScale(exposure.Position);
                var exposureFactor = positionScale * decay;
                penalty += ImpressionNoClickWeight * exposureFactor;
                if (index > 0)
                {
                    repeatedExposureFactors.Add(exposureFactor);
                }
            }

            if (repeatedExposureFactors.Count > 0)
            {
                var repeatedExposureBuckets = repeatedExposureFactors.Count;
                var averageRepeatedExposureFactor = repeatedExposureFactors.Average();
                penalty += RepeatedImpressionWeight
                    * averageRepeatedExposureFactor
                    * Math.Log(1d + repeatedExposureBuckets);
            }
        }

        foreach (var bounceTimestamp in orderedBounceTimestamps)
        {
            penalty += BounceWeight * CalculateDecay(bounceTimestamp, computedAtUtc);
        }

        penalty = Math.Max(penalty, MaxPenaltyScore);

        DateTime? lastSignalAtUtc = null;
        if (orderedNoClickExposures.Length > 0)
        {
            lastSignalAtUtc = orderedNoClickExposures[^1].TimestampUtc;
        }

        if (orderedBounceTimestamps.Length > 0
            && (!lastSignalAtUtc.HasValue || orderedBounceTimestamps[^1] > lastSignalAtUtc.Value))
        {
            lastSignalAtUtc = orderedBounceTimestamps[^1];
        }

        return new RecommendationNegativeFeedbackScore(
            productId,
            userId,
            orderedNoClickExposures.LongLength,
            Math.Max(0, orderedNoClickExposures.LongLength - 1),
            orderedBounceTimestamps.LongLength,
            Math.Round(penalty, 4),
            lastSignalAtUtc);
    }

    public static double ResolvePositionPenaltyScale(int? position)
    {
        if (!position.HasValue || position.Value <= 0)
        {
            return 0.3d;
        }

        if (position.Value <= 2)
        {
            return 1d;
        }

        if (position.Value <= 5)
        {
            return 0.6d;
        }

        return 0.3d;
    }

    private static bool HasMatchingClick(
        NegativeImpression impression,
        IReadOnlyDictionary<NegativeSignalKey, DateTime[]> clicksByKey)
    {
        var key = new NegativeSignalKey(impression.UserId, impression.ProductId, impression.Position);
        if (!clicksByKey.TryGetValue(key, out var clickTimestamps))
        {
            return false;
        }

        var clickWindowEnd = impression.Timestamp.Add(ClickAttributionWindow);
        return clickTimestamps.Any(timestamp =>
            timestamp >= impression.Timestamp
            && timestamp <= clickWindowEnd);
    }

    private static double CalculateDecay(DateTime signalAtUtc, DateTime computedAtUtc)
    {
        var ageDays = Math.Max(0d, (computedAtUtc - signalAtUtc).TotalDays);
        return Math.Pow(0.5d, ageDays / NegativeSignalHalfLifeDays);
    }

    private static DateTime ToHourlyBucketUtc(DateTime timestamp)
    {
        var utc = timestamp.Kind == DateTimeKind.Local
            ? timestamp.ToUniversalTime()
            : DateTime.SpecifyKind(timestamp, DateTimeKind.Utc);

        return new DateTime(
            utc.Year,
            utc.Month,
            utc.Day,
            utc.Hour,
            minute: 0,
            second: 0,
            DateTimeKind.Utc);
    }

    private sealed record NegativeSignalKey(int UserId, int ProductId, int? Position);

    private sealed record NegativeImpression(int UserId, int ProductId, int? Position, DateTime Timestamp);

    private sealed record NegativeClick(int UserId, int ProductId, int? Position, DateTime Timestamp);
}
