using System.Text;
using FreshFarm.Web.Bff.Options;
using FreshFarm.Web.Bff.Services;
using Microsoft.Extensions.Caching.Distributed;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Xunit;

namespace FreshFarm.Web.Bff.Tests;

public sealed class SessionAwareRecommendationRerankerTests
{
    [Fact]
    public void SessionAwareRecommendationOptions_DefaultObjectiveWeights_AreBusinessOptimizedAndNormalized()
    {
        var options = new SessionAwareRecommendationOptions();
        var sum = options.CtrWeight
            + options.AddToCartWeight
            + options.PurchaseWeight
            + options.RevenueWeight;

        Assert.Equal(0.12, options.CtrWeight);
        Assert.Equal(0.08, options.AddToCartWeight);
        Assert.Equal(0.35, options.PurchaseWeight);
        Assert.Equal(0.45, options.RevenueWeight);
        Assert.InRange(sum, 0.9999, 1.0001);
    }

    [Fact]
    public async Task RerankAsync_AppliesPositionAwareSessionBoostFactors()
    {
        var cache = new RecordingDistributedCache();
        cache.SetString(
            "recommendation:session:17",
            """
            {
              "recentSearches": [
                { "keyword": "ca chua", "timestamp": "2026-04-28T09:00:00Z" }
              ],
              "recentClicks": []
            }
            """);
        var reranker = CreateReranker(
            cache,
            new SessionAwareRecommendationOptions
            {
                SearchKeywordBoost = 100,
                TopPositionBoostFactor = 0.5,
                MidPositionBoostFactor = 2,
                InStockBoost = 0,
                OutOfStockPenalty = 0,
                NearbyShopBoost = 0
            });

        var results = await reranker.RerankAsync(
            userId: 17,
            new[]
            {
                new SessionAwareRecommendationCandidate
                {
                    ProductId = 1,
                    ProductName = "Ca chua do",
                    AvailableStock = 10,
                    BaseScore = 100
                },
                new SessionAwareRecommendationCandidate
                {
                    ProductId = 2,
                    ProductName = "Rau muong",
                    AvailableStock = 10,
                    BaseScore = 95
                },
                new SessionAwareRecommendationCandidate
                {
                    ProductId = 3,
                    ProductName = "Ca chua bi",
                    AvailableStock = 10,
                    BaseScore = 90
                }
            });

        Assert.Equal(3, results[0].Product.ProductId);
        Assert.Equal(490, results[0].FinalScore);
        Assert.Contains("position_session_boost_factor:2", results[0].AppliedSignals);
        Assert.Equal(1, results[1].Product.ProductId);
        Assert.Equal(200, results[1].FinalScore);
        Assert.Contains("position_session_boost_factor:0.5", results[1].AppliedSignals);
    }

    [Fact]
    public async Task RerankAsync_KeepsBaseScores_WhenSessionCacheIsEmpty()
    {
        var reranker = CreateReranker(
            new RecordingDistributedCache(),
            new SessionAwareRecommendationOptions
            {
                SearchKeywordBoost = 100,
                TopPositionBoostFactor = 0,
                MidPositionBoostFactor = 3,
                InStockBoost = 0,
                OutOfStockPenalty = 0,
                NearbyShopBoost = 0
            });

        var results = await reranker.RerankAsync(
            userId: 17,
            new[]
            {
                new SessionAwareRecommendationCandidate
                {
                    ProductId = 1,
                    ProductName = "Ca chua",
                    AvailableStock = 10,
                    BaseScore = 80
                },
                new SessionAwareRecommendationCandidate
                {
                    ProductId = 2,
                    ProductName = "Rau muong",
                    AvailableStock = 10,
                    BaseScore = 95
                }
            });

        Assert.Equal(2, results[0].Product.ProductId);
        Assert.Equal(95, results[0].FinalScore);
        Assert.Empty(results[0].AppliedSignals);
        Assert.Equal(1, results[1].Product.ProductId);
        Assert.Equal(80, results[1].FinalScore);
        Assert.Empty(results[1].AppliedSignals);
    }

    [Fact]
    public async Task RerankAsync_KeepsBaseScores_WhenSessionSignalsDoNotMatchCandidates()
    {
        var metricsClient = new FakeRecommendationMetricsClient(new Dictionary<int, RecommendationObjectiveMetric>
        {
            [2] = new(
                ProductId: 2,
                TotalImpressions: 100,
                TotalClicks: 20,
                TotalAddToCarts: 8,
                TotalPurchases: 4,
                TotalRevenue: 120000m,
                Ctr: 0.2,
                AddToCartRate: 0.08,
                PurchaseRate: 0.04,
                RevenuePerImpression: 1200m,
                Status: "ok")
        });
        var cache = CreateCacheWithUnrelatedSessionSignal(userId: 17);
        var reranker = CreateReranker(
            cache,
            new SessionAwareRecommendationOptions
            {
                ObjectiveScoreScale = 100,
                InStockBoost = 50,
                NearbyShopBoost = 50
            },
            metricsClient);

        var results = await reranker.RerankAsync(
            userId: 17,
            new[]
            {
                new SessionAwareRecommendationCandidate
                {
                    ProductId = 1,
                    ProductName = "Rau muong",
                    CategoryName = "Rau la",
                    PrimarySellerId = 1,
                    AvailableStock = 10,
                    BaseScore = 100
                },
                new SessionAwareRecommendationCandidate
                {
                    ProductId = 2,
                    ProductName = "Ca chua",
                    CategoryName = "Rau an qua",
                    PrimarySellerId = 2,
                    AvailableStock = 10,
                    BaseScore = 90
                }
            });

        Assert.Equal(1, results[0].Product.ProductId);
        Assert.Equal(100, results[0].FinalScore);
        Assert.Empty(results[0].AppliedSignals);
        Assert.Equal(2, results[1].Product.ProductId);
        Assert.Equal(90, results[1].FinalScore);
        Assert.Empty(results[1].AppliedSignals);
    }

    [Fact]
    public async Task RerankAsync_AppliesNormalizedMultiObjectiveScore_WhenMetricsExist()
    {
        var metricsClient = new FakeRecommendationMetricsClient(new Dictionary<int, RecommendationObjectiveMetric>
        {
            [1] = new(
                ProductId: 1,
                TotalImpressions: 100,
                TotalClicks: 5,
                TotalAddToCarts: 2,
                TotalPurchases: 1,
                TotalRevenue: 20000m,
                Ctr: 0.05,
                AddToCartRate: 0.02,
                PurchaseRate: 0.01,
                RevenuePerImpression: 200m,
                Status: "ok"),
            [2] = new(
                ProductId: 2,
                TotalImpressions: 100,
                TotalClicks: 20,
                TotalAddToCarts: 8,
                TotalPurchases: 4,
                TotalRevenue: 120000m,
                Ctr: 0.2,
                AddToCartRate: 0.08,
                PurchaseRate: 0.04,
                RevenuePerImpression: 1200m,
                Status: "ok")
        });
        var cache = CreateCacheWithActionableSearchSignal(userId: 17);
        var reranker = CreateReranker(
            cache,
            new SessionAwareRecommendationOptions
            {
                SearchKeywordBoost = 0,
                CtrWeight = 0.1,
                AddToCartWeight = 0.2,
                PurchaseWeight = 0.3,
                RevenueWeight = 0.4,
                ObjectiveScoreScale = 100,
                InStockBoost = 0,
                OutOfStockPenalty = 0,
                NearbyShopBoost = 0
            },
            metricsClient);

        var results = await reranker.RerankAsync(
            userId: 17,
            new[]
            {
                new SessionAwareRecommendationCandidate
                {
                    ProductId = 1,
                    ProductName = "Rau muong",
                    AvailableStock = 10,
                    BaseScore = 100
                },
                new SessionAwareRecommendationCandidate
                {
                    ProductId = 2,
                    ProductName = "Ca chua",
                    AvailableStock = 10,
                    BaseScore = 90
                }
            });

        Assert.Equal(2, results[0].Product.ProductId);
        Assert.Equal(190, results[0].FinalScore);
        Assert.Contains("objective_ctr:1", results[0].AppliedSignals);
        Assert.Contains("objective_add_to_cart:1", results[0].AppliedSignals);
        Assert.Contains("objective_purchase:1", results[0].AppliedSignals);
        Assert.Contains("objective_revenue:1", results[0].AppliedSignals);
        Assert.Equal(1, results[1].Product.ProductId);
        Assert.Equal(100, results[1].FinalScore);
    }

    [Fact]
    public async Task RerankAsync_ClampsAndNormalizesObjectiveWeights()
    {
        var metricsClient = new FakeRecommendationMetricsClient(new Dictionary<int, RecommendationObjectiveMetric>
        {
            [1] = new(
                ProductId: 1,
                TotalImpressions: 100,
                TotalClicks: 5,
                TotalAddToCarts: 2,
                TotalPurchases: 1,
                TotalRevenue: 20000m,
                Ctr: 0.05,
                AddToCartRate: 0.02,
                PurchaseRate: 0.01,
                RevenuePerImpression: 200m,
                Status: "ok"),
            [2] = new(
                ProductId: 2,
                TotalImpressions: 100,
                TotalClicks: 20,
                TotalAddToCarts: 8,
                TotalPurchases: 4,
                TotalRevenue: 120000m,
                Ctr: 0.2,
                AddToCartRate: 0.08,
                PurchaseRate: 0.04,
                RevenuePerImpression: 1200m,
                Status: "ok")
        });
        var cache = CreateCacheWithActionableSearchSignal(userId: 17);
        var reranker = CreateReranker(
            cache,
            new SessionAwareRecommendationOptions
            {
                SearchKeywordBoost = 0,
                CtrWeight = 10,
                AddToCartWeight = 10,
                PurchaseWeight = 10,
                RevenueWeight = 10,
                ObjectiveScoreScale = 40,
                InStockBoost = 0,
                OutOfStockPenalty = 0,
                NearbyShopBoost = 0
            },
            metricsClient);

        var results = await reranker.RerankAsync(
            userId: 17,
            new[]
            {
                new SessionAwareRecommendationCandidate
                {
                    ProductId = 1,
                    ProductName = "Rau muong",
                    AvailableStock = 10,
                    BaseScore = 100
                },
                new SessionAwareRecommendationCandidate
                {
                    ProductId = 2,
                    ProductName = "Ca chua",
                    AvailableStock = 10,
                    BaseScore = 90
                }
            });

        Assert.Equal(2, results[0].Product.ProductId);
        Assert.Equal(130, results[0].FinalScore);
        Assert.Contains("objective_score:40", results[0].AppliedSignals);
    }

    private static SessionAwareRecommendationReranker CreateReranker(
        RecordingDistributedCache cache,
        SessionAwareRecommendationOptions options)
    {
        return CreateReranker(cache, options, NoopRecommendationMetricsClient.Instance);
    }

    private static RecordingDistributedCache CreateCacheWithUnrelatedSessionSignal(int userId)
    {
        var cache = new RecordingDistributedCache();
        cache.SetString(
            $"recommendation:session:{userId}",
            """
            {
              "recentSearches": [],
              "recentClicks": [
                {
                  "productId": 999999,
                  "sellerId": null,
                  "categoryName": "Khac",
                  "timestamp": "2026-04-29T00:00:00Z"
                }
              ]
            }
            """);
        return cache;
    }

    private static RecordingDistributedCache CreateCacheWithActionableSearchSignal(int userId)
    {
        var cache = new RecordingDistributedCache();
        cache.SetString(
            $"recommendation:session:{userId}",
            """
            {
              "recentSearches": [
                {
                  "keyword": "ca chua",
                  "timestamp": "2026-04-29T00:00:00Z"
                }
              ],
              "recentClicks": []
            }
            """);
        return cache;
    }

    private static SessionAwareRecommendationReranker CreateReranker(
        RecordingDistributedCache cache,
        SessionAwareRecommendationOptions options,
        IRecommendationMetricsClient metricsClient)
    {
        return new SessionAwareRecommendationReranker(
            cache,
            new StaticOptionsMonitor<SessionAwareRecommendationOptions>(options),
            metricsClient,
            NullLogger<SessionAwareRecommendationReranker>.Instance);
    }

    private sealed class StaticOptionsMonitor<T> : IOptionsMonitor<T>
    {
        public StaticOptionsMonitor(T currentValue)
        {
            CurrentValue = currentValue;
        }

        public T CurrentValue { get; }

        public T Get(string? name) => CurrentValue;

        public IDisposable? OnChange(Action<T, string?> listener) => null;
    }

    private sealed class RecordingDistributedCache : IDistributedCache
    {
        private readonly Dictionary<string, byte[]> _values = new(StringComparer.Ordinal);

        public byte[]? Get(string key)
        {
            return _values.TryGetValue(key, out var value) ? value : null;
        }

        public Task<byte[]?> GetAsync(string key, CancellationToken token = default)
        {
            return Task.FromResult(Get(key));
        }

        public void Set(string key, byte[] value, DistributedCacheEntryOptions options)
        {
            _values[key] = value;
        }

        public Task SetAsync(
            string key,
            byte[] value,
            DistributedCacheEntryOptions options,
            CancellationToken token = default)
        {
            Set(key, value, options);
            return Task.CompletedTask;
        }

        public void SetString(string key, string value)
        {
            Set(key, Encoding.UTF8.GetBytes(value), new DistributedCacheEntryOptions());
        }

        public void Refresh(string key)
        {
        }

        public Task RefreshAsync(string key, CancellationToken token = default)
        {
            return Task.CompletedTask;
        }

        public void Remove(string key)
        {
            _values.Remove(key);
        }

        public Task RemoveAsync(string key, CancellationToken token = default)
        {
            Remove(key);
            return Task.CompletedTask;
        }
    }

    private sealed class FakeRecommendationMetricsClient : IRecommendationMetricsClient
    {
        private readonly IReadOnlyDictionary<int, RecommendationObjectiveMetric> _metrics;

        public FakeRecommendationMetricsClient(IReadOnlyDictionary<int, RecommendationObjectiveMetric> metrics)
        {
            _metrics = metrics;
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
            var requested = productIds.ToHashSet();
            return Task.FromResult<IReadOnlyDictionary<int, RecommendationObjectiveMetric>>(
                _metrics
                    .Where(item => requested.Contains(item.Key))
                    .ToDictionary(item => item.Key, item => item.Value));
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
}
