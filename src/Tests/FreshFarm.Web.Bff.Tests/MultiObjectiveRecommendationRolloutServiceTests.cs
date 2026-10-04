using System.Net;
using System.Globalization;
using FreshFarm.Web.Bff.Options;
using FreshFarm.Web.Bff.Services;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Xunit;

namespace FreshFarm.Web.Bff.Tests;

public sealed class MultiObjectiveRecommendationRolloutServiceTests
{
    [Fact]
    public async Task MonitorAsync_ReducesRevenueWeight_WhenSessionCtrDropsBeyondGuardrail()
    {
        var configStore = new RecordingConfigStore();
        var service = CreateService(
            CreateMetricsJson(
                baselineCtr: 0.20,
                variantCtr: 0.15,
                baselinePurchaseRate: 0.10,
                variantPurchaseRate: 0.11,
                baselineRevenuePerImpression: 100m,
                variantRevenuePerImpression: 120m),
            configStore);

        var result = await service.MonitorAsync();

        Assert.True(result.Changed);
        Assert.Equal("adjust_revenue_weight_ctr_drop_gt_10_percent", result.Decision);
        Assert.NotNull(configStore.LastState);
        Assert.Equal(0.405, configStore.LastState!.SessionAwareRecommendation.RevenueWeight);
        Assert.Equal(20, configStore.LastState.RecommendationExperiment.SessionRerankTrafficPercent);
        Assert.Equal(0, configStore.LastState.MultiObjectiveRecommendationRollout.StableWindowCount);
    }

    [Fact]
    public async Task MonitorAsync_RollsBackPreviousConfig_WhenPurchaseRateDropsBeyondGuardrail()
    {
        var configStore = new RecordingConfigStore
        {
            InitialState = new SessionAwareRecommendationTuningState
            {
                SessionAwareRecommendation = new SessionAwareRecommendationWeightOverride
                {
                    TopPositionBoostFactor = 0.85,
                    MidPositionBoostFactor = 1.1,
                    CtrWeight = 0.12,
                    AddToCartWeight = 0.08,
                    PurchaseWeight = 0.35,
                    RevenueWeight = 0.45
                },
                RecommendationExperiment = new RecommendationExperimentRolloutOverride
                {
                    SessionRerankTrafficPercent = 50
                },
                MultiObjectiveRecommendationRollout = new MultiObjectiveRecommendationRolloutState
                {
                    TrafficPercent = 50,
                    ComputedAtUtc = DateTime.UtcNow.AddHours(-1),
                    Versions =
                    [
                        CreateVersion(-2, 0.10, 0.10, 0.30, 0.50, 20, "previous"),
                        CreateVersion(-1, 0.12, 0.08, 0.35, 0.45, 50, "current")
                    ]
                }
            }
        };
        var service = CreateService(
            CreateMetricsJson(
                baselineCtr: 0.20,
                variantCtr: 0.22,
                baselinePurchaseRate: 0.10,
                variantPurchaseRate: 0.09,
                baselineRevenuePerImpression: 100m,
                variantRevenuePerImpression: 120m),
            configStore);

        var result = await service.MonitorAsync();

        Assert.True(result.Changed);
        Assert.Equal("rollback_purchase_rate_drop_gt_5_percent", result.Decision);
        Assert.NotNull(configStore.LastState);
        Assert.Equal(20, configStore.LastState!.RecommendationExperiment.SessionRerankTrafficPercent);
        Assert.Equal(0.50, configStore.LastState.SessionAwareRecommendation.RevenueWeight);
        Assert.Equal(3, configStore.LastState.MultiObjectiveRecommendationRollout.Versions.Count);
    }

    [Fact]
    public async Task MonitorAsync_RampsTrafficAfterStableWindows()
    {
        var configStore = new RecordingConfigStore
        {
            InitialState = new SessionAwareRecommendationTuningState
            {
                RecommendationExperiment = new RecommendationExperimentRolloutOverride
                {
                    SessionRerankTrafficPercent = 20
                },
                MultiObjectiveRecommendationRollout = new MultiObjectiveRecommendationRolloutState
                {
                    TrafficPercent = 20,
                    StageStartedAtUtc = DateTime.UtcNow.AddHours(-25),
                    StableSinceUtc = DateTime.UtcNow.AddHours(-25),
                    ComputedAtUtc = DateTime.UtcNow.AddHours(-1)
                }
            }
        };
        var service = CreateService(
            CreateMetricsJson(
                baselineCtr: 0.20,
                variantCtr: 0.21,
                baselinePurchaseRate: 0.10,
                variantPurchaseRate: 0.11,
                baselineRevenuePerImpression: 100m,
                variantRevenuePerImpression: 110m),
            configStore);

        var result = await service.MonitorAsync();

        Assert.True(result.Changed);
        Assert.Equal("ramp_to_50_percent", result.Decision);
        Assert.NotNull(configStore.LastState);
        Assert.Equal(50, configStore.LastState!.RecommendationExperiment.SessionRerankTrafficPercent);
        Assert.Equal(0, configStore.LastState.MultiObjectiveRecommendationRollout.StableWindowCount);
        Assert.Null(configStore.LastState.MultiObjectiveRecommendationRollout.StableSinceUtc);
    }

    [Fact]
    public async Task MonitorAsync_WaitsForMinimumImpressionsOrThreeHoursBeforeGuardrails()
    {
        var configStore = new RecordingConfigStore
        {
            InitialState = new SessionAwareRecommendationTuningState
            {
                RecommendationExperiment = new RecommendationExperimentRolloutOverride
                {
                    SessionRerankTrafficPercent = 20
                },
                MultiObjectiveRecommendationRollout = new MultiObjectiveRecommendationRolloutState
                {
                    TrafficPercent = 20,
                    StageStartedAtUtc = DateTime.UtcNow.AddHours(-1),
                    ComputedAtUtc = DateTime.UtcNow.AddHours(-1)
                }
            }
        };
        var service = CreateService(
            CreateMetricsJson(
                baselineCtr: 0.20,
                variantCtr: 0.05,
                baselinePurchaseRate: 0.10,
                variantPurchaseRate: 0.01,
                baselineRevenuePerImpression: 100m,
                variantRevenuePerImpression: 50m,
                impressions: 50),
            configStore);

        var result = await service.MonitorAsync();

        Assert.False(result.Changed);
        Assert.False(result.GuardrailsEvaluated);
        Assert.Equal("warming_up_20_percent", result.Decision);
        Assert.NotNull(configStore.LastState);
        Assert.False(configStore.LastState!.MultiObjectiveRecommendationRollout.IsLocked);
        Assert.Equal(20, configStore.LastState.RecommendationExperiment.SessionRerankTrafficPercent);
        Assert.Equal(0.45, configStore.LastState.SessionAwareRecommendation.RevenueWeight);
    }

    [Fact]
    public async Task MonitorAsync_LocksRollout_WhenFullTrafficIsStableForFortyEightHours()
    {
        var configStore = new RecordingConfigStore
        {
            InitialState = new SessionAwareRecommendationTuningState
            {
                RecommendationExperiment = new RecommendationExperimentRolloutOverride
                {
                    SessionRerankTrafficPercent = 100
                },
                MultiObjectiveRecommendationRollout = new MultiObjectiveRecommendationRolloutState
                {
                    TrafficPercent = 100,
                    StageStartedAtUtc = DateTime.UtcNow.AddHours(-49),
                    StableSinceUtc = DateTime.UtcNow.AddHours(-49),
                    ComputedAtUtc = DateTime.UtcNow.AddHours(-1)
                }
            }
        };
        var service = CreateService(
            CreateMetricsJson(
                baselineCtr: 0.20,
                variantCtr: 0.22,
                baselinePurchaseRate: 0.10,
                variantPurchaseRate: 0.11,
                baselineRevenuePerImpression: 100m,
                variantRevenuePerImpression: 120m),
            configStore);

        var result = await service.MonitorAsync();

        Assert.True(result.Changed);
        Assert.True(result.IsLocked);
        Assert.Equal("lock_100_percent_stable_48h", result.Decision);
        Assert.NotNull(configStore.LastState);
        Assert.True(configStore.LastState!.MultiObjectiveRecommendationRollout.IsLocked);
        Assert.NotNull(configStore.LastState.MultiObjectiveRecommendationRollout.LockedAtUtc);
        Assert.Equal(100, configStore.LastState.RecommendationExperiment.SessionRerankTrafficPercent);
    }

    [Fact]
    public async Task MonitorAsync_DoesNotRollback_WhenRolloutIsLocked()
    {
        var configStore = new RecordingConfigStore
        {
            InitialState = new SessionAwareRecommendationTuningState
            {
                RecommendationExperiment = new RecommendationExperimentRolloutOverride
                {
                    SessionRerankTrafficPercent = 100
                },
                MultiObjectiveRecommendationRollout = new MultiObjectiveRecommendationRolloutState
                {
                    TrafficPercent = 100,
                    StageStartedAtUtc = DateTime.UtcNow.AddHours(-72),
                    StableSinceUtc = DateTime.UtcNow.AddHours(-72),
                    IsLocked = true,
                    LockedAtUtc = DateTime.UtcNow.AddHours(-2),
                    ComputedAtUtc = DateTime.UtcNow.AddHours(-1),
                    Versions =
                    [
                        CreateVersion(-2, 0.10, 0.10, 0.30, 0.50, 50, "previous"),
                        CreateVersion(-1, 0.12, 0.08, 0.35, 0.45, 100, "locked")
                    ]
                }
            }
        };
        var service = CreateService(
            CreateMetricsJson(
                baselineCtr: 0.20,
                variantCtr: 0.05,
                baselinePurchaseRate: 0.10,
                variantPurchaseRate: 0.01,
                baselineRevenuePerImpression: 100m,
                variantRevenuePerImpression: 10m),
            configStore);

        var result = await service.MonitorAsync();

        Assert.False(result.Changed);
        Assert.True(result.IsLocked);
        Assert.False(result.GuardrailsEvaluated);
        Assert.Equal("locked_monitor_only", result.Decision);
        Assert.NotNull(configStore.LastState);
        Assert.True(configStore.LastState!.MultiObjectiveRecommendationRollout.IsLocked);
        Assert.Equal(100, configStore.LastState.RecommendationExperiment.SessionRerankTrafficPercent);
        Assert.Equal(0.45, configStore.LastState.SessionAwareRecommendation.RevenueWeight);
    }

    private static MultiObjectiveRecommendationRolloutService CreateService(
        string responseJson,
        RecordingConfigStore configStore)
    {
        var httpClientFactory = new SingleClientFactory(new HttpClient(new StaticJsonHandler(responseJson))
        {
            BaseAddress = new Uri("https://ordering.test")
        });

        return new MultiObjectiveRecommendationRolloutService(
            httpClientFactory,
            new StaticOptionsMonitor<SessionAwareRecommendationOptions>(new SessionAwareRecommendationOptions
            {
                TopPositionBoostFactor = 0.85,
                MidPositionBoostFactor = 1.1,
                CtrWeight = 0.12,
                AddToCartWeight = 0.08,
                PurchaseWeight = 0.35,
                RevenueWeight = 0.45,
                ObjectiveScoreScale = 62
            }),
            new StaticOptionsMonitor<RecommendationExperimentOptions>(new RecommendationExperimentOptions
            {
                SessionRerankTrafficPercent = 20
            }),
            new StaticOptionsMonitor<MultiObjectiveRecommendationRolloutOptions>(new MultiObjectiveRecommendationRolloutOptions
            {
                StartupDelaySeconds = 0,
                IntervalMinutes = 60,
                LookbackHours = 6,
                MinimumImpressions = 200,
                MinimumGuardrailAgeHours = 3,
                SmoothingWindowHours = 3,
                InitialTrafficPercent = 20,
                RampSteps = [20, 50, 100],
                StableWindowCountForRamp = 3,
                StableHoursForRamp = 24,
                StableHoursForLock = 48,
                RetainedConfigVersionCount = 3,
                CtrDropRevenueAdjustRatio = 0.10,
                PurchaseRateDropRollbackRatio = 0.05,
                RevenueWeightAdjustmentRatio = 0.10
            }),
            new StaticOptionsMonitor<OrderingServiceOptions>(new OrderingServiceOptions()),
            configStore,
            NullLogger<MultiObjectiveRecommendationRolloutService>.Instance);
    }

    private static string CreateMetricsJson(
        double baselineCtr,
        double variantCtr,
        double baselinePurchaseRate,
        double variantPurchaseRate,
        decimal baselineRevenuePerImpression,
        decimal variantRevenuePerImpression,
        long impressions = 200)
    {
        return $$"""
        {
          "fromDateUtc": "2026-04-28T00:00:00Z",
          "toDateUtc": "2026-04-28T01:00:00Z",
          "minimumImpressions": 1,
          "groups": [
            {
              "recommendationSource": "ML",
              "experimentGroup": "A",
              "totalImpressions": {{impressions}},
              "totalClicks": 40,
              "totalAddToCarts": 20,
              "totalPurchases": 20,
              "totalRevenue": 20000,
              "ctr": {{baselineCtr.ToString(CultureInfo.InvariantCulture)}},
              "purchaseRate": {{baselinePurchaseRate.ToString(CultureInfo.InvariantCulture)}},
              "revenuePerImpression": {{baselineRevenuePerImpression.ToString(CultureInfo.InvariantCulture)}},
              "status": "ok"
            },
            {
              "recommendationSource": "Session",
              "experimentGroup": "B",
              "totalImpressions": {{impressions}},
              "totalClicks": 40,
              "totalAddToCarts": 20,
              "totalPurchases": 20,
              "totalRevenue": 20000,
              "ctr": {{variantCtr.ToString(CultureInfo.InvariantCulture)}},
              "purchaseRate": {{variantPurchaseRate.ToString(CultureInfo.InvariantCulture)}},
              "revenuePerImpression": {{variantRevenuePerImpression.ToString(CultureInfo.InvariantCulture)}},
              "status": "ok"
            }
          ]
        }
        """;
    }

    private static MultiObjectiveRecommendationConfigVersion CreateVersion(
        int hoursAgo,
        double ctrWeight,
        double addToCartWeight,
        double purchaseWeight,
        double revenueWeight,
        int trafficPercent,
        string reason)
    {
        return new MultiObjectiveRecommendationConfigVersion
        {
            ComputedAtUtc = DateTime.UtcNow.AddHours(hoursAgo),
            CtrWeight = ctrWeight,
            AddToCartWeight = addToCartWeight,
            PurchaseWeight = purchaseWeight,
            RevenueWeight = revenueWeight,
            TrafficPercent = trafficPercent,
            Reason = reason
        };
    }

    private sealed class RecordingConfigStore : ISessionAwareRecommendationConfigStore
    {
        public SessionAwareRecommendationTuningState? InitialState { get; init; }

        public SessionAwareRecommendationTuningState? LastState { get; private set; }

        public Task<SessionAwareRecommendationTuningState?> LoadStateAsync(
            CancellationToken cancellationToken = default)
        {
            return Task.FromResult(InitialState);
        }

        public Task PersistStateAsync(
            SessionAwareRecommendationTuningState state,
            CancellationToken cancellationToken = default)
        {
            LastState = state;
            return Task.CompletedTask;
        }
    }

    private sealed class StaticJsonHandler : HttpMessageHandler
    {
        private readonly string _responseJson;

        public StaticJsonHandler(string responseJson)
        {
            _responseJson = responseJson;
        }

        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(_responseJson)
            });
        }
    }

    private sealed class SingleClientFactory : IHttpClientFactory
    {
        private readonly HttpClient _client;

        public SingleClientFactory(HttpClient client)
        {
            _client = client;
        }

        public HttpClient CreateClient(string name) => _client;
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
}
