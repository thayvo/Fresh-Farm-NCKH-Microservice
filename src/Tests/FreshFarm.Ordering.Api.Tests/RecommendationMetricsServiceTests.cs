using FreshFarm.Ordering.Api.Dtos;
using FreshFarm.Ordering.Api.Models;
using FreshFarm.Ordering.Api.Services;
using Xunit;

namespace FreshFarm.Ordering.Api.Tests;

public sealed class RecommendationMetricsServiceTests
{
    [Fact]
    public void NegativeFeedbackScoring_DoesNotPenalizeProductsBelowThreeExposureBuckets()
    {
        var computedAt = new DateTime(2026, 4, 29, 10, 0, 0, DateTimeKind.Utc);
        var score = RecommendationNegativeFeedbackScoring.Calculate(
            productId: 108,
            userId: 17,
            new[]
            {
                new RecommendationNegativeFeedbackExposure(computedAt.AddHours(-2), Position: 1),
                new RecommendationNegativeFeedbackExposure(computedAt.AddHours(-1), Position: 1)
            },
            Array.Empty<DateTime>(),
            computedAt);

        Assert.Equal(2, score.ImpressionNoClickCount);
        Assert.Equal(1, score.RepeatedImpressionCount);
        Assert.Equal(0, score.BounceCount);
        Assert.Equal(0d, score.PenaltyScore, precision: 4);
    }

    [Fact]
    public void NegativeFeedbackScoring_BucketsSameHourExposuresOnce()
    {
        var computedAt = new DateTime(2026, 4, 29, 10, 0, 0, DateTimeKind.Utc);
        var score = RecommendationNegativeFeedbackScoring.Calculate(
            productId: 108,
            userId: 17,
            new[]
            {
                new RecommendationNegativeFeedbackExposure(computedAt.AddMinutes(-40), Position: 1),
                new RecommendationNegativeFeedbackExposure(computedAt.AddMinutes(-30), Position: 3),
                new RecommendationNegativeFeedbackExposure(computedAt.AddMinutes(-20), Position: 8)
            },
            Array.Empty<DateTime>(),
            computedAt);

        Assert.Equal(1, score.ImpressionNoClickCount);
        Assert.Equal(0, score.RepeatedImpressionCount);
        Assert.Equal(0d, score.PenaltyScore, precision: 4);
    }

    [Fact]
    public void NegativeFeedbackScoring_UsesSoftSaturationForRepeatedExposure()
    {
        var computedAt = new DateTime(2026, 4, 29, 10, 0, 0, DateTimeKind.Utc);
        var exposures = new[]
        {
            new RecommendationNegativeFeedbackExposure(computedAt.AddHours(-5), Position: 1),
            new RecommendationNegativeFeedbackExposure(computedAt.AddHours(-4), Position: 1),
            new RecommendationNegativeFeedbackExposure(computedAt.AddHours(-3), Position: 1),
            new RecommendationNegativeFeedbackExposure(computedAt.AddHours(-2), Position: 1),
            new RecommendationNegativeFeedbackExposure(computedAt.AddHours(-1), Position: 1)
        };

        var score = RecommendationNegativeFeedbackScoring.Calculate(
            productId: 108,
            userId: 17,
            exposures,
            Array.Empty<DateTime>(),
            computedAt);

        var decays = exposures
            .Select(exposure => Math.Pow(
                0.5d,
                (computedAt - exposure.TimestampUtc).TotalDays
                    / RecommendationNegativeFeedbackScoring.NegativeSignalHalfLifeDays))
            .ToArray();
        var expected = decays.Sum(decay => RecommendationNegativeFeedbackScoring.ImpressionNoClickWeight * decay)
            + RecommendationNegativeFeedbackScoring.RepeatedImpressionWeight
            * decays.Skip(1).Average()
            * Math.Log(1d + score.RepeatedImpressionCount);

        Assert.Equal(5, score.ImpressionNoClickCount);
        Assert.Equal(4, score.RepeatedImpressionCount);
        Assert.Equal(Math.Round(expected, 4), score.PenaltyScore, precision: 4);
        Assert.InRange(score.PenaltyScore, -1.81d, -1.78d);
    }

    [Fact]
    public void NegativeFeedbackScoring_AppliesPositionAwareScaleAndMaxPenaltyCap()
    {
        var computedAt = new DateTime(2026, 4, 29, 10, 0, 0, DateTimeKind.Utc);
        var score = RecommendationNegativeFeedbackScoring.Calculate(
            productId: 108,
            userId: 17,
            new[]
            {
                new RecommendationNegativeFeedbackExposure(computedAt.AddHours(-4), Position: 1),
                new RecommendationNegativeFeedbackExposure(computedAt.AddHours(-3), Position: 2),
                new RecommendationNegativeFeedbackExposure(computedAt.AddHours(-2), Position: 3),
                new RecommendationNegativeFeedbackExposure(computedAt.AddHours(-1), Position: 6)
            },
            new[]
            {
                computedAt,
                computedAt,
                computedAt
            },
            computedAt);

        Assert.Equal(4, score.ImpressionNoClickCount);
        Assert.Equal(3, score.RepeatedImpressionCount);
        Assert.Equal(3, score.BounceCount);
        Assert.Equal(-3d, score.PenaltyScore, precision: 4);
        Assert.Equal(1d, RecommendationNegativeFeedbackScoring.ResolvePositionPenaltyScale(1));
        Assert.Equal(0.6d, RecommendationNegativeFeedbackScoring.ResolvePositionPenaltyScale(3));
        Assert.Equal(0.3d, RecommendationNegativeFeedbackScoring.ResolvePositionPenaltyScale(6));
        Assert.Equal(12d, RecommendationNegativeFeedbackScoring.NegativeSignalHalfLifeDays);
    }

    [Fact]
    public async Task TrackImpressionsAsync_NormalizesExperimentGroupAndSource()
    {
        var dao = new RecordingRecommendationMetricsDao();
        var service = new RecommendationMetricsService(dao);

        await service.TrackImpressionsAsync(
            userId: 17,
            new[]
            {
                new RecommendationImpressionMetricItemDto
                {
                    ProductId = 108,
                    Position = 1,
                    RecommendationSource = "session",
                    ExperimentGroup = "b"
                }
            });

        var impression = Assert.Single(dao.Impressions);
        Assert.Equal(17, impression.UserId);
        Assert.Equal(108, impression.ProductId);
        Assert.Equal(1, impression.Position);
        Assert.Equal("Session", impression.RecommendationSource);
        Assert.Equal("B", impression.ExperimentGroup);
    }

    [Fact]
    public async Task TrackClickAsync_NormalizesInvalidExperimentGroupToA()
    {
        var dao = new RecordingRecommendationMetricsDao();
        var service = new RecommendationMetricsService(dao);

        await service.TrackClickAsync(
            userId: -1,
            productId: 25,
            position: 0,
            experimentGroup: "invalid");

        var click = Assert.Single(dao.Clicks);
        Assert.Null(click.UserId);
        Assert.Equal(25, click.ProductId);
        Assert.Null(click.Position);
        Assert.Equal("A", click.ExperimentGroup);
    }

    [Fact]
    public async Task TrackAddToCartAsync_CopiesExperimentGroupFromMatchedImpression()
    {
        var dao = new RecordingRecommendationMetricsDao
        {
            LatestAttribution = new RecommendationMetricAttribution("Session", "B")
        };
        var service = new RecommendationMetricsService(dao);

        await service.TrackAddToCartAsync(
            userId: 17,
            productId: 108,
            position: 2,
            experimentGroup: "a");

        var addToCart = Assert.Single(dao.AddToCarts);
        Assert.Equal(17, addToCart.UserId);
        Assert.Equal(108, addToCart.ProductId);
        Assert.Equal(2, addToCart.Position);
        Assert.Equal("B", addToCart.ExperimentGroup);
        Assert.Equal(17, dao.LastAttributionUserId);
        Assert.Equal(108, dao.LastAttributionProductId);
        Assert.Equal(2, dao.LastAttributionPosition);
    }

    [Fact]
    public async Task TrackPurchaseAsync_NormalizesNegativeRevenueToZero()
    {
        var dao = new RecordingRecommendationMetricsDao();
        var service = new RecommendationMetricsService(dao);

        await service.TrackPurchaseAsync(
            userId: null,
            productId: 25,
            position: null,
            revenue: -10m,
            experimentGroup: "session");

        var purchase = Assert.Single(dao.Purchases);
        Assert.Equal(25, purchase.ProductId);
        Assert.Equal(0m, purchase.Revenue);
        Assert.Equal("A", purchase.ExperimentGroup);
    }

    [Fact]
    public async Task TrackPurchaseAsync_CopiesExperimentGroupFromMatchedImpression()
    {
        var dao = new RecordingRecommendationMetricsDao
        {
            LatestAttribution = new RecommendationMetricAttribution("Session", "B")
        };
        var service = new RecommendationMetricsService(dao);

        await service.TrackPurchaseAsync(
            userId: 42,
            productId: 25,
            position: 3,
            revenue: 120000m,
            experimentGroup: "a");

        var purchase = Assert.Single(dao.Purchases);
        Assert.Equal(42, purchase.UserId);
        Assert.Equal(25, purchase.ProductId);
        Assert.Equal(3, purchase.Position);
        Assert.Equal(120000m, purchase.Revenue);
        Assert.Equal("B", purchase.ExperimentGroup);
        Assert.Equal(42, dao.LastAttributionUserId);
        Assert.Equal(25, dao.LastAttributionProductId);
        Assert.Equal(3, dao.LastAttributionPosition);
    }

    [Fact]
    public async Task GetCtrAsync_ReturnsComparison_WhenBothStrategiesHaveEnoughData()
    {
        var dao = new RecordingRecommendationMetricsDao
        {
            CtrRows =
            {
                new RecommendationCtrMetricDto
                {
                    RecommendationSource = "ML",
                    ExperimentGroup = "A",
                    TotalImpressions = 200,
                    TotalClicks = 20,
                    CtrPercentage = 10,
                    Status = "ok"
                },
                new RecommendationCtrMetricDto
                {
                    RecommendationSource = "Session",
                    ExperimentGroup = "B",
                    TotalImpressions = 250,
                    TotalClicks = 40,
                    CtrPercentage = 16,
                    Status = "ok"
                }
            }
        };
        var service = new RecommendationMetricsService(dao);
        var fromDateUtc = new DateTime(2026, 4, 21, 0, 0, 0, DateTimeKind.Utc);
        var toDateUtc = new DateTime(2026, 4, 28, 0, 0, 0, DateTimeKind.Utc);

        var report = await service.GetCtrAsync(fromDateUtc, toDateUtc);

        Assert.Equal(fromDateUtc, report.FromDateUtc);
        Assert.Equal(toDateUtc, report.ToDateUtc);
        Assert.Equal(100, report.MinimumImpressions);
        Assert.Equal(2, report.Metrics.Count);
        Assert.Equal(6, report.Comparison.AbsoluteUpliftPercentagePoints);
        Assert.Equal(60, report.Comparison.PercentageUplift);
        Assert.Equal("ok", report.Comparison.Status);
        Assert.Equal(fromDateUtc, dao.LastFromDateUtc);
        Assert.Equal(toDateUtc, dao.LastToDateUtc);
        Assert.Equal(100, dao.LastMinimumImpressions);
    }

    [Fact]
    public async Task GetCtrAsync_MarksComparisonInsufficient_WhenAnyStrategyBelowThreshold()
    {
        var dao = new RecordingRecommendationMetricsDao
        {
            CtrRows =
            {
                new RecommendationCtrMetricDto
                {
                    RecommendationSource = "ML",
                    ExperimentGroup = "A",
                    TotalImpressions = 99,
                    TotalClicks = 10,
                    CtrPercentage = null,
                    Status = "insufficient_data"
                },
                new RecommendationCtrMetricDto
                {
                    RecommendationSource = "Session",
                    ExperimentGroup = "B",
                    TotalImpressions = 200,
                    TotalClicks = 30,
                    CtrPercentage = 15,
                    Status = "ok"
                }
            }
        };
        var service = new RecommendationMetricsService(dao);

        var report = await service.GetCtrAsync(fromDateUtc: null, toDateUtc: null);

        Assert.Equal("insufficient_data", report.Comparison.Status);
        Assert.Null(report.Comparison.AbsoluteUpliftPercentagePoints);
        Assert.Null(report.Comparison.PercentageUplift);
        Assert.True(report.ToDateUtc > report.FromDateUtc);
        Assert.InRange((report.ToDateUtc - report.FromDateUtc).TotalDays, 6.99, 7.01);
    }

    [Fact]
    public async Task GetCtrByPositionAsync_ReturnsPositionMetricsWithSameThresholdAndWindow()
    {
        var dao = new RecordingRecommendationMetricsDao
        {
            CtrByPositionRows =
            {
                new RecommendationCtrByPositionMetricDto
                {
                    Position = 1,
                    ExperimentGroup = "A",
                    RecommendationSource = "ML",
                    TotalImpressions = 200,
                    TotalClicks = 50,
                    CtrPercentage = 25,
                    Status = "ok"
                },
                new RecommendationCtrByPositionMetricDto
                {
                    Position = 1,
                    ExperimentGroup = "B",
                    RecommendationSource = "Session",
                    TotalImpressions = 200,
                    TotalClicks = 80,
                    CtrPercentage = 40,
                    Status = "ok"
                },
                new RecommendationCtrByPositionMetricDto
                {
                    Position = 2,
                    ExperimentGroup = "A",
                    RecommendationSource = "ML",
                    TotalImpressions = 80,
                    TotalClicks = 12,
                    CtrPercentage = null,
                    Status = "insufficient_data"
                }
            }
        };
        var service = new RecommendationMetricsService(dao);
        var fromDateUtc = new DateTime(2026, 4, 21, 0, 0, 0, DateTimeKind.Utc);
        var toDateUtc = new DateTime(2026, 4, 28, 0, 0, 0, DateTimeKind.Utc);

        var report = await service.GetCtrByPositionAsync(fromDateUtc, toDateUtc);

        Assert.Equal(fromDateUtc, report.FromDateUtc);
        Assert.Equal(toDateUtc, report.ToDateUtc);
        Assert.Equal(100, report.MinimumImpressions);
        Assert.Equal(3, report.Metrics.Count);
        Assert.Contains(report.Metrics, metric =>
            metric.Position == 1
            && metric.ExperimentGroup == "B"
            && metric.RecommendationSource == "Session"
            && metric.TotalClicks == 80
            && metric.CtrPercentage == 40);
        Assert.Contains(report.Metrics, metric =>
            metric.Position == 2
            && metric.ExperimentGroup == "A"
            && metric.CtrPercentage is null
            && metric.Status == "insufficient_data");
        Assert.Equal(fromDateUtc, dao.LastPositionFromDateUtc);
        Assert.Equal(toDateUtc, dao.LastPositionToDateUtc);
        Assert.Equal(100, dao.LastPositionMinimumImpressions);
    }

    [Fact]
    public async Task GetObjectiveMetricsAsync_ReturnsWindowAndProductMetrics()
    {
        var dao = new RecordingRecommendationMetricsDao
        {
            ObjectiveGlobal = new RecommendationObjectiveSummaryDto
            {
                TotalImpressions = 220,
                TotalClicks = 30,
                TotalAddToCarts = 20,
                TotalPurchases = 8,
                TotalRevenue = 220000m,
                Ctr = 0.136363636,
                AddToCartRate = 0.090909091,
                PurchaseRate = 0.036363636,
                RevenuePerImpression = 1000m,
                Status = "ok"
            },
            ObjectiveGroupRows =
            {
                new RecommendationObjectiveGroupMetricDto
                {
                    RecommendationSource = "ML",
                    ExperimentGroup = "A",
                    TotalImpressions = 120,
                    TotalPurchases = 3,
                    TotalRevenue = 60000m,
                    PurchaseRate = 0.025,
                    RevenuePerImpression = 500m,
                    Status = "ok"
                },
                new RecommendationObjectiveGroupMetricDto
                {
                    RecommendationSource = "Session",
                    ExperimentGroup = "B",
                    TotalImpressions = 100,
                    TotalPurchases = 5,
                    TotalRevenue = 160000m,
                    PurchaseRate = 0.05,
                    RevenuePerImpression = 1600m,
                    Status = "ok"
                }
            },
            ObjectiveRows =
            {
                new RecommendationObjectiveMetricDto
                {
                    ProductId = 108,
                    TotalImpressions = 100,
                    TotalClicks = 10,
                    TotalAddToCarts = 6,
                    TotalPurchases = 3,
                    TotalRevenue = 66000m,
                    Ctr = 0.1,
                    AddToCartRate = 0.06,
                    PurchaseRate = 0.03,
                    RevenuePerImpression = 660m,
                    Status = "ok"
                }
            }
        };
        var service = new RecommendationMetricsService(dao);
        var fromDateUtc = new DateTime(2026, 4, 21, 0, 0, 0, DateTimeKind.Utc);
        var toDateUtc = new DateTime(2026, 4, 28, 0, 0, 0, DateTimeKind.Utc);

        var report = await service.GetObjectiveMetricsAsync(
            fromDateUtc,
            toDateUtc,
            new[] { 108, 25 });

        Assert.Equal(fromDateUtc, report.FromDateUtc);
        Assert.Equal(toDateUtc, report.ToDateUtc);
        Assert.Equal(100, report.MinimumImpressions);
        Assert.Equal(220, report.Global.TotalImpressions);
        Assert.Equal(0.036363636, report.Global.PurchaseRate);
        Assert.Equal(2, report.Groups.Count);
        Assert.Equal(0.025, report.Comparison.PurchaseRateUplift);
        Assert.Equal(1100m, report.Comparison.RevenuePerImpressionUplift);
        Assert.Equal("ok", report.Comparison.Status);
        var metric = Assert.Single(report.Metrics);
        Assert.Equal(108, metric.ProductId);
        Assert.Equal(660m, metric.RevenuePerImpression);
        Assert.Equal(new[] { 108, 25 }, dao.LastObjectiveProductIds);
        Assert.Equal(100, dao.LastObjectiveMinimumImpressions);
    }

    private sealed class RecordingRecommendationMetricsDao : IRecommendationMetricsDao
    {
        public List<RecommendationImpression> Impressions { get; } = new();

        public List<RecommendationClick> Clicks { get; } = new();

        public List<RecommendationAddToCart> AddToCarts { get; } = new();

        public List<RecommendationPurchase> Purchases { get; } = new();

        public List<RecommendationCtrMetricDto> CtrRows { get; } = new();

        public List<RecommendationCtrByPositionMetricDto> CtrByPositionRows { get; } = new();

        public List<RecommendationObjectiveMetricDto> ObjectiveRows { get; } = new();

        public RecommendationObjectiveSummaryDto ObjectiveGlobal { get; set; } = new()
        {
            Status = "insufficient_data"
        };

        public List<RecommendationObjectiveGroupMetricDto> ObjectiveGroupRows { get; } = new();

        public List<RecommendationNegativeFeedbackMetricDto> NegativeFeedbackRows { get; } = new();

        public RecommendationMetricAttribution? LatestAttribution { get; set; }

        public int? LastAttributionUserId { get; private set; }

        public int? LastAttributionProductId { get; private set; }

        public int? LastAttributionPosition { get; private set; }

        public DateTime? LastAttributionTimestampUtc { get; private set; }

        public DateTime? LastFromDateUtc { get; private set; }

        public DateTime? LastToDateUtc { get; private set; }

        public int? LastMinimumImpressions { get; private set; }

        public DateTime? LastPositionFromDateUtc { get; private set; }

        public DateTime? LastPositionToDateUtc { get; private set; }

        public int? LastPositionMinimumImpressions { get; private set; }

        public int[] LastObjectiveProductIds { get; private set; } = Array.Empty<int>();

        public int? LastObjectiveMinimumImpressions { get; private set; }

        public int? LastNegativeFeedbackUserId { get; private set; }

        public int[] LastNegativeFeedbackProductIds { get; private set; } = Array.Empty<int>();

        public Task AddImpressionsAsync(
            IReadOnlyCollection<RecommendationImpression> impressions,
            CancellationToken cancellationToken = default)
        {
            Impressions.AddRange(impressions);
            return Task.CompletedTask;
        }

        public Task AddClickAsync(
            RecommendationClick click,
            CancellationToken cancellationToken = default)
        {
            Clicks.Add(click);
            return Task.CompletedTask;
        }

        public Task AddAddToCartAsync(
            RecommendationAddToCart addToCart,
            CancellationToken cancellationToken = default)
        {
            AddToCarts.Add(addToCart);
            return Task.CompletedTask;
        }

        public Task AddPurchaseAsync(
            RecommendationPurchase purchase,
            CancellationToken cancellationToken = default)
        {
            Purchases.Add(purchase);
            return Task.CompletedTask;
        }

        public Task<RecommendationMetricAttribution?> FindLatestAttributionAsync(
            int? userId,
            int productId,
            int? position,
            DateTime eventTimestampUtc,
            CancellationToken cancellationToken = default)
        {
            LastAttributionUserId = userId;
            LastAttributionProductId = productId;
            LastAttributionPosition = position;
            LastAttributionTimestampUtc = eventTimestampUtc;
            return Task.FromResult(LatestAttribution);
        }

        public Task<IReadOnlyList<RecommendationCtrMetricDto>> GetCtrAsync(
            DateTime fromDateUtc,
            DateTime toDateUtc,
            int minimumImpressions,
            CancellationToken cancellationToken = default)
        {
            LastFromDateUtc = fromDateUtc;
            LastToDateUtc = toDateUtc;
            LastMinimumImpressions = minimumImpressions;
            return Task.FromResult<IReadOnlyList<RecommendationCtrMetricDto>>(
                CtrRows);
        }

        public Task<IReadOnlyList<RecommendationCtrByPositionMetricDto>> GetCtrByPositionAsync(
            DateTime fromDateUtc,
            DateTime toDateUtc,
            int minimumImpressions,
            CancellationToken cancellationToken = default)
        {
            LastPositionFromDateUtc = fromDateUtc;
            LastPositionToDateUtc = toDateUtc;
            LastPositionMinimumImpressions = minimumImpressions;
            return Task.FromResult<IReadOnlyList<RecommendationCtrByPositionMetricDto>>(
                CtrByPositionRows);
        }

        public Task<IReadOnlyList<RecommendationObjectiveMetricDto>> GetObjectiveMetricsAsync(
            DateTime fromDateUtc,
            DateTime toDateUtc,
            IReadOnlyCollection<int> productIds,
            int minimumImpressions,
            CancellationToken cancellationToken = default)
        {
            LastObjectiveProductIds = productIds.ToArray();
            LastObjectiveMinimumImpressions = minimumImpressions;
            return Task.FromResult<IReadOnlyList<RecommendationObjectiveMetricDto>>(
                ObjectiveRows);
        }

        public Task<RecommendationObjectiveSummaryDto> GetObjectiveGlobalMetricsAsync(
            DateTime fromDateUtc,
            DateTime toDateUtc,
            IReadOnlyCollection<int> productIds,
            int minimumImpressions,
            CancellationToken cancellationToken = default)
        {
            return Task.FromResult(ObjectiveGlobal);
        }

        public Task<IReadOnlyList<RecommendationObjectiveGroupMetricDto>> GetObjectiveGroupMetricsAsync(
            DateTime fromDateUtc,
            DateTime toDateUtc,
            IReadOnlyCollection<int> productIds,
            int minimumImpressions,
            CancellationToken cancellationToken = default)
        {
            return Task.FromResult<IReadOnlyList<RecommendationObjectiveGroupMetricDto>>(
                ObjectiveGroupRows);
        }

        public Task<IReadOnlyList<RecommendationNegativeFeedbackMetricDto>> GetNegativeFeedbackAsync(
            int? userId,
            DateTime fromDateUtc,
            DateTime toDateUtc,
            IReadOnlyCollection<int> productIds,
            CancellationToken cancellationToken = default)
        {
            LastNegativeFeedbackUserId = userId;
            LastNegativeFeedbackProductIds = productIds.ToArray();
            return Task.FromResult<IReadOnlyList<RecommendationNegativeFeedbackMetricDto>>(
                NegativeFeedbackRows);
        }
    }
}
