using System.Net;
using FreshFarm.Web.Bff.Options;
using FreshFarm.Web.Bff.Services;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Xunit;

namespace FreshFarm.Web.Bff.Tests;

public sealed class SessionAwareRecommendationWeightTuningServiceTests
{
    [Fact]
    public async Task TuneAsync_AdjustsTopAndMidFactors_FromBaseline_WhenEligibleCtrDataShowsRegressionAndImprovement()
    {
        var configStore = new RecordingConfigStore();
        var service = CreateService(
            """
            {
              "fromDateUtc": "2026-04-21T00:00:00Z",
              "toDateUtc": "2026-04-28T00:00:00Z",
              "minimumImpressions": 100,
              "metrics": [
                { "position": 1, "experimentGroup": "A", "recommendationSource": "ML", "totalImpressions": 200, "totalClicks": 60, "ctrPercentage": 30, "status": "ok" },
                { "position": 1, "experimentGroup": "B", "recommendationSource": "Session", "totalImpressions": 200, "totalClicks": 40, "ctrPercentage": 20, "status": "ok" },
                { "position": 3, "experimentGroup": "A", "recommendationSource": "ML", "totalImpressions": 200, "totalClicks": 20, "ctrPercentage": 10, "status": "ok" },
                { "position": 3, "experimentGroup": "B", "recommendationSource": "Session", "totalImpressions": 200, "totalClicks": 50, "ctrPercentage": 25, "status": "ok" }
              ]
            }
            """,
            configStore,
            new SessionAwareRecommendationOptions
            {
                TopPositionBoostFactor = 0.8,
                MidPositionBoostFactor = 1.2
            });

        var result = await service.TuneAsync();

        Assert.True(result.Changed);
        Assert.Equal(0.8, result.TopPositionBoostFactorBefore);
        Assert.Equal(0.72, result.TopPositionBoostFactorAfter);
        Assert.Equal(1.2, result.MidPositionBoostFactorBefore);
        Assert.Equal(1.32, result.MidPositionBoostFactorAfter);
        Assert.Equal(1, result.EligibleTopPositionCount);
        Assert.Equal(1, result.EligibleMidPositionCount);
        Assert.NotNull(configStore.LastState);
        Assert.Equal(0.8, configStore.LastState!.SessionAwareRecommendationTuning.BaselineTopPositionBoostFactor);
        Assert.Equal(1.2, configStore.LastState.SessionAwareRecommendationTuning.BaselineMidPositionBoostFactor);
        Assert.Equal(0.72, configStore.LastState.SessionAwareRecommendation.TopPositionBoostFactor);
        Assert.Equal(1.32, configStore.LastState.SessionAwareRecommendation.MidPositionBoostFactor);
        Assert.Equal(2, configStore.LastState.SessionAwareRecommendationTuning.Versions.Count);
    }

    [Fact]
    public async Task TuneAsync_UsesMovingAverageOfLastThreeRuns_ToAvoidSingleRunNoise()
    {
        var configStore = new RecordingConfigStore
        {
            InitialState = CreateState(
                topFactor: 0.8,
                midFactor: 1.2,
                runs:
                [
                    CreateRun(daysAgo: 2, topMlCtr: 30, topSessionCtr: 10, overallSessionCtr: 10),
                    CreateRun(daysAgo: 1, topMlCtr: 30, topSessionCtr: 10, overallSessionCtr: 10)
                ],
                versions:
                [
                    CreateVersion(daysAgo: 2, topFactor: 0.8, midFactor: 1.2, reason: "baseline")
                ])
        };
        var service = CreateService(
            """
            {
              "fromDateUtc": "2026-04-21T00:00:00Z",
              "toDateUtc": "2026-04-28T00:00:00Z",
              "minimumImpressions": 100,
              "metrics": [
                { "position": 1, "experimentGroup": "A", "recommendationSource": "ML", "totalImpressions": 200, "totalClicks": 40, "ctrPercentage": 20, "status": "ok" },
                { "position": 1, "experimentGroup": "B", "recommendationSource": "Session", "totalImpressions": 200, "totalClicks": 80, "ctrPercentage": 40, "status": "ok" }
              ]
            }
            """,
            configStore,
            new SessionAwareRecommendationOptions
            {
                TopPositionBoostFactor = 0.8,
                MidPositionBoostFactor = 1.2
            });

        var result = await service.TuneAsync();

        Assert.True(result.Changed);
        Assert.Equal(0.72, result.TopPositionBoostFactorAfter);
        Assert.Contains("top_session_ctr_smoothed_decrease", result.Reason);
        Assert.NotNull(configStore.LastState);
        Assert.Equal(3, configStore.LastState!.SessionAwareRecommendationTuning.Runs.Count);
    }

    [Fact]
    public async Task TuneAsync_RollsBackToPreviousConfig_WhenSessionCtrDropsMoreThanThreshold()
    {
        var configStore = new RecordingConfigStore
        {
            InitialState = CreateState(
                topFactor: 0.88,
                midFactor: 1.32,
                runs:
                [
                    CreateRun(daysAgo: 1, topMlCtr: 20, topSessionCtr: 30, midMlCtr: 20, midSessionCtr: 30, overallSessionCtr: 30)
                ],
                versions:
                [
                    CreateVersion(daysAgo: 2, topFactor: 0.8, midFactor: 1.2, reason: "baseline"),
                    CreateVersion(daysAgo: 1, topFactor: 0.88, midFactor: 1.32, reason: "increase")
                ])
        };
        var service = CreateService(
            """
            {
              "fromDateUtc": "2026-04-21T00:00:00Z",
              "toDateUtc": "2026-04-28T00:00:00Z",
              "minimumImpressions": 100,
              "metrics": [
                { "position": 1, "experimentGroup": "A", "recommendationSource": "ML", "totalImpressions": 200, "totalClicks": 40, "ctrPercentage": 20, "status": "ok" },
                { "position": 1, "experimentGroup": "B", "recommendationSource": "Session", "totalImpressions": 200, "totalClicks": 40, "ctrPercentage": 20, "status": "ok" },
                { "position": 3, "experimentGroup": "A", "recommendationSource": "ML", "totalImpressions": 200, "totalClicks": 40, "ctrPercentage": 20, "status": "ok" },
                { "position": 3, "experimentGroup": "B", "recommendationSource": "Session", "totalImpressions": 200, "totalClicks": 40, "ctrPercentage": 20, "status": "ok" }
              ]
            }
            """,
            configStore,
            new SessionAwareRecommendationOptions
            {
                TopPositionBoostFactor = 0.88,
                MidPositionBoostFactor = 1.32
            });

        var result = await service.TuneAsync();

        Assert.True(result.Changed);
        Assert.Equal(0.8, result.TopPositionBoostFactorAfter);
        Assert.Equal(1.2, result.MidPositionBoostFactorAfter);
        Assert.Contains("rollback_ctr_drop_gt_10_percent", result.Reason);
        Assert.NotNull(configStore.LastState);
        Assert.Equal(3, configStore.LastState!.SessionAwareRecommendationTuning.Versions.Count);
        Assert.Equal(0.8, configStore.LastState.SessionAwareRecommendationTuning.Versions[^1].TopPositionBoostFactor);
        Assert.Equal(1.2, configStore.LastState.SessionAwareRecommendationTuning.Versions[^1].MidPositionBoostFactor);
    }

    [Fact]
    public async Task TuneAsync_ForcesMidFactorReduction_WhenCurrentSessionCtrIsBelowMlDespiteSmoothedImprovement()
    {
        var configStore = new RecordingConfigStore
        {
            InitialState = CreateState(
                topFactor: 0.8,
                midFactor: 1.2,
                runs:
                [
                    CreateRun(daysAgo: 2, midMlCtr: 10, midSessionCtr: 30, overallSessionCtr: 30),
                    CreateRun(daysAgo: 1, midMlCtr: 10, midSessionCtr: 30, overallSessionCtr: 30)
                ],
                versions:
                [
                    CreateVersion(daysAgo: 1, topFactor: 0.8, midFactor: 1.2, reason: "baseline")
                ])
        };
        var service = CreateService(
            """
            {
              "fromDateUtc": "2026-04-21T00:00:00Z",
              "toDateUtc": "2026-04-28T00:00:00Z",
              "minimumImpressions": 100,
              "metrics": [
                { "position": 3, "experimentGroup": "A", "recommendationSource": "ML", "totalImpressions": 200, "totalClicks": 50, "ctrPercentage": 25, "status": "ok" },
                { "position": 3, "experimentGroup": "B", "recommendationSource": "Session", "totalImpressions": 200, "totalClicks": 40, "ctrPercentage": 20, "status": "ok" }
              ]
            }
            """,
            configStore,
            new SessionAwareRecommendationOptions
            {
                TopPositionBoostFactor = 0.8,
                MidPositionBoostFactor = 1.2
            });

        var result = await service.TuneAsync();

        Assert.True(result.Changed);
        Assert.Equal(1.08, result.MidPositionBoostFactorAfter);
        Assert.Contains("mid_guardrail_session_below_ml_decrease", result.Reason);
    }

    [Fact]
    public async Task TuneAsync_DoesNotPersistState_WhenCtrDataIsBelowMinimumImpressions()
    {
        var configStore = new RecordingConfigStore();
        var service = CreateService(
            """
            {
              "fromDateUtc": "2026-04-21T00:00:00Z",
              "toDateUtc": "2026-04-28T00:00:00Z",
              "minimumImpressions": 100,
              "metrics": [
                { "position": 1, "experimentGroup": "A", "recommendationSource": "ML", "totalImpressions": 99, "totalClicks": 30, "ctrPercentage": null, "status": "insufficient_data" },
                { "position": 1, "experimentGroup": "B", "recommendationSource": "Session", "totalImpressions": 120, "totalClicks": 40, "ctrPercentage": 33.33, "status": "ok" }
              ]
            }
            """,
            configStore,
            new SessionAwareRecommendationOptions
            {
                TopPositionBoostFactor = 0.8,
                MidPositionBoostFactor = 1.2
            });

        var result = await service.TuneAsync();

        Assert.False(result.Changed);
        Assert.Equal("insufficient_data", result.Reason);
        Assert.Null(configStore.LastState);
    }

    private static SessionAwareRecommendationWeightTuningService CreateService(
        string responseJson,
        RecordingConfigStore configStore,
        SessionAwareRecommendationOptions recommendationOptions)
    {
        var httpClientFactory = new SingleClientFactory(new HttpClient(new StaticJsonHandler(responseJson))
        {
            BaseAddress = new Uri("https://ordering.test")
        });

        return new SessionAwareRecommendationWeightTuningService(
            httpClientFactory,
            new StaticOptionsMonitor<SessionAwareRecommendationOptions>(recommendationOptions),
            new StaticOptionsMonitor<SessionAwareRecommendationTuningOptions>(new SessionAwareRecommendationTuningOptions
            {
                LookbackDays = 7,
                MinimumImpressions = 100,
                MaxAdjustmentRatio = 0.10,
                SmoothingRunCount = 3,
                RetainedConfigVersionCount = 3,
                RollbackCtrDropRatio = 0.10,
                TopPositionBoostFactorMin = 0.5,
                TopPositionBoostFactorMax = 1.0,
                MidPositionBoostFactorMin = 1.0,
                MidPositionBoostFactorMax = 2.0
            }),
            new StaticOptionsMonitor<OrderingServiceOptions>(new OrderingServiceOptions()),
            configStore,
            NullLogger<SessionAwareRecommendationWeightTuningService>.Instance);
    }

    private static SessionAwareRecommendationTuningState CreateState(
        double topFactor,
        double midFactor,
        IEnumerable<SessionAwareRecommendationTuningRun> runs,
        IEnumerable<SessionAwareRecommendationConfigVersion> versions)
    {
        return new SessionAwareRecommendationTuningState
        {
            SessionAwareRecommendation = new SessionAwareRecommendationWeightOverride
            {
                TopPositionBoostFactor = topFactor,
                MidPositionBoostFactor = midFactor
            },
            SessionAwareRecommendationTuning = new SessionAwareRecommendationTuningMetadata
            {
                BaselineTopPositionBoostFactor = 0.8,
                BaselineMidPositionBoostFactor = 1.2,
                ComputedAtUtc = DateTime.UtcNow.AddDays(-1),
                Reason = "initial",
                Runs = runs.ToList(),
                Versions = versions.ToList()
            }
        };
    }

    private static SessionAwareRecommendationTuningRun CreateRun(
        int daysAgo,
        double? topMlCtr = null,
        double? topSessionCtr = null,
        double? midMlCtr = null,
        double? midSessionCtr = null,
        double? overallSessionCtr = null)
    {
        return new SessionAwareRecommendationTuningRun
        {
            ComputedAtUtc = DateTime.UtcNow.AddDays(-daysAgo),
            TopMlCtrPercentage = topMlCtr,
            TopSessionCtrPercentage = topSessionCtr,
            MidMlCtrPercentage = midMlCtr,
            MidSessionCtrPercentage = midSessionCtr,
            OverallMlCtrPercentage = topMlCtr ?? midMlCtr,
            OverallSessionCtrPercentage = overallSessionCtr ?? topSessionCtr ?? midSessionCtr,
            EligibleTopPositionCount = topMlCtr.HasValue && topSessionCtr.HasValue ? 1 : 0,
            EligibleMidPositionCount = midMlCtr.HasValue && midSessionCtr.HasValue ? 1 : 0
        };
    }

    private static SessionAwareRecommendationConfigVersion CreateVersion(
        int daysAgo,
        double topFactor,
        double midFactor,
        string reason)
    {
        return new SessionAwareRecommendationConfigVersion
        {
            ComputedAtUtc = DateTime.UtcNow.AddDays(-daysAgo),
            TopPositionBoostFactor = topFactor,
            MidPositionBoostFactor = midFactor,
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
