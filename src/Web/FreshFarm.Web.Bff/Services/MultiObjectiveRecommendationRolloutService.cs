using System.Net.Http.Json;
using System.Text.Json;
using FreshFarm.Web.Bff.Options;
using Microsoft.Extensions.Options;

namespace FreshFarm.Web.Bff.Services;

public interface IMultiObjectiveRecommendationRolloutService
{
    Task<MultiObjectiveRecommendationRolloutResult> MonitorAsync(
        CancellationToken cancellationToken = default);
}

public sealed class MultiObjectiveRecommendationRolloutService : IMultiObjectiveRecommendationRolloutService
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    private readonly IHttpClientFactory _httpClientFactory;
    private readonly IOptionsMonitor<SessionAwareRecommendationOptions> _recommendationOptions;
    private readonly IOptionsMonitor<RecommendationExperimentOptions> _experimentOptions;
    private readonly IOptionsMonitor<MultiObjectiveRecommendationRolloutOptions> _rolloutOptions;
    private readonly IOptionsMonitor<OrderingServiceOptions> _orderingOptions;
    private readonly ISessionAwareRecommendationConfigStore _configStore;
    private readonly ILogger<MultiObjectiveRecommendationRolloutService> _logger;

    public MultiObjectiveRecommendationRolloutService(
        IHttpClientFactory httpClientFactory,
        IOptionsMonitor<SessionAwareRecommendationOptions> recommendationOptions,
        IOptionsMonitor<RecommendationExperimentOptions> experimentOptions,
        IOptionsMonitor<MultiObjectiveRecommendationRolloutOptions> rolloutOptions,
        IOptionsMonitor<OrderingServiceOptions> orderingOptions,
        ISessionAwareRecommendationConfigStore configStore,
        ILogger<MultiObjectiveRecommendationRolloutService> logger)
    {
        _httpClientFactory = httpClientFactory;
        _recommendationOptions = recommendationOptions;
        _experimentOptions = experimentOptions;
        _rolloutOptions = rolloutOptions;
        _orderingOptions = orderingOptions;
        _configStore = configStore;
        _logger = logger;
    }

    public async Task<MultiObjectiveRecommendationRolloutResult> MonitorAsync(
        CancellationToken cancellationToken = default)
    {
        var options = _rolloutOptions.CurrentValue;
        var nowUtc = DateTime.UtcNow;
        var lookbackHours = Math.Clamp(options.LookbackHours, 3, 6);
        var fromDateUtc = nowUtc.AddHours(-lookbackHours);
        var minimumImpressions = Math.Max(1, options.MinimumImpressions);
        var state = await _configStore.LoadStateAsync(cancellationToken) ?? new SessionAwareRecommendationTuningState();
        var rolloutState = state.MultiObjectiveRecommendationRollout;
        var stageStartedAtUtc = ResolveStageStartedAtUtc(rolloutState, nowUtc);
        var currentRecommendationOptions = _recommendationOptions.CurrentValue;
        var currentWeights = MultiObjectiveRecommendationWeights.FromOptions(currentRecommendationOptions).Normalize();
        var currentTrafficPercent = ResolveTrafficPercent(state, options);
        var versions = NormalizeVersions(state, currentWeights, currentTrafficPercent, nowUtc);

        var report = await FetchObjectiveMetricsAsync(
            fromDateUtc,
            nowUtc,
            endpointMinimumImpressions: 1,
            options,
            cancellationToken);
        var baseline = FindMetric(report, RecommendationExperimentGroups.MlOnly, "ML");
        var variant = FindMetric(report, RecommendationExperimentGroups.SessionRerank, "Session");
        var currentStage = BuildRolloutStage(currentTrafficPercent, rolloutState.IsLocked);

        if (!HasMetricData(baseline, variant))
        {
            _logger.LogInformation(
                "Multi-objective recommendation rollout monitor skipped: insufficient data. Stage={RolloutStage}, MinImpressions={MinimumImpressions}, Traffic={TrafficPercent}, Weights={Weights}",
                currentStage,
                minimumImpressions,
                currentTrafficPercent,
                currentWeights);
            var noDataState = BuildState(
                state,
                currentRecommendationOptions,
                currentWeights,
                currentTrafficPercent,
                rolloutState.StableWindowCount,
                nowUtc,
                "insufficient_data",
                rolloutState.Runs,
                versions,
                stageStartedAtUtc,
                rolloutState.StableSinceUtc,
                rolloutState.IsLocked,
                rolloutState.LockedAtUtc);
            await _configStore.PersistStateAsync(noDataState, cancellationToken);
            return new MultiObjectiveRecommendationRolloutResult(
                Changed: false,
                Decision: "insufficient_data",
                TrafficPercentBefore: currentTrafficPercent,
                TrafficPercentAfter: currentTrafficPercent,
                WeightsBefore: currentWeights,
                WeightsAfter: currentWeights,
                Baseline: baseline,
                Variant: variant,
                RolloutStage: currentStage,
                GuardrailsEvaluated: false,
                IsLocked: rolloutState.IsLocked);
        }

        var currentRun = new MultiObjectiveRecommendationMonitorRun
        {
            ComputedAtUtc = nowUtc,
            BaselineImpressions = baseline!.TotalImpressions,
            VariantImpressions = variant!.TotalImpressions,
            BaselineCtr = baseline.Ctr,
            VariantCtr = variant.Ctr,
            BaselinePurchaseRate = baseline.PurchaseRate,
            VariantPurchaseRate = variant.PurchaseRate,
            BaselineRevenuePerImpression = baseline.RevenuePerImpression,
            VariantRevenuePerImpression = variant.RevenuePerImpression,
            Decision = "observed"
        };
        var retainedRunCount = Math.Max(6, Math.Clamp(options.SmoothingWindowHours, 3, 6));
        var runs = AppendAndTrimRuns(rolloutState.Runs, currentRun, retainedRunCount);
        var smoothingWindow = Math.Clamp(options.SmoothingWindowHours, 3, 6);
        var smoothedBaseline = SmoothMetric(baseline!, runs, useBaseline: true, smoothingWindow);
        var smoothedVariant = SmoothMetric(variant!, runs, useBaseline: false, smoothingWindow);
        var stageAge = nowUtc - stageStartedAtUtc;
        var guardrailsEligible = HasMinimumImpressions(baseline!, variant!, minimumImpressions)
            || stageAge >= TimeSpan.FromHours(Math.Clamp(options.MinimumGuardrailAgeHours, 0, 72));

        var decision = CreateDecision(
            smoothedBaseline,
            smoothedVariant,
            currentWeights,
            currentTrafficPercent,
            stageStartedAtUtc,
            rolloutState.StableSinceUtc,
            rolloutState.IsLocked,
            rolloutState.LockedAtUtc,
            guardrailsEligible,
            versions,
            options,
            nowUtc);
        currentRun.Decision = decision.Reason;

        var nextVersions = decision.ShouldAppendVersion
            ? AppendAndTrimVersions(
                versions,
                new MultiObjectiveRecommendationConfigVersion
                {
                    ComputedAtUtc = nowUtc,
                    CtrWeight = decision.WeightsAfter.CtrWeight,
                    AddToCartWeight = decision.WeightsAfter.AddToCartWeight,
                    PurchaseWeight = decision.WeightsAfter.PurchaseWeight,
                    RevenueWeight = decision.WeightsAfter.RevenueWeight,
                    TrafficPercent = decision.TrafficPercentAfter,
                    Reason = decision.Reason
                },
                Math.Max(1, options.RetainedConfigVersionCount))
            : TrimVersions(versions, Math.Max(1, options.RetainedConfigVersionCount));

        var nextState = BuildState(
            state,
            currentRecommendationOptions,
            decision.WeightsAfter,
            decision.TrafficPercentAfter,
            decision.StableWindowCount,
            nowUtc,
            decision.Reason,
            runs,
            nextVersions,
            decision.StageStartedAtUtc,
            decision.StableSinceUtc,
            decision.IsLocked,
            decision.LockedAtUtc);
        await _configStore.PersistStateAsync(nextState, cancellationToken);

        LogDecision(currentWeights, currentTrafficPercent, smoothedBaseline, smoothedVariant, decision);

        return new MultiObjectiveRecommendationRolloutResult(
            Changed: decision.Changed,
            Decision: decision.Reason,
            TrafficPercentBefore: currentTrafficPercent,
            TrafficPercentAfter: decision.TrafficPercentAfter,
            WeightsBefore: currentWeights,
            WeightsAfter: decision.WeightsAfter,
            Baseline: smoothedBaseline,
            Variant: smoothedVariant,
            RolloutStage: decision.RolloutStage,
            GuardrailsEvaluated: decision.GuardrailsEvaluated,
            IsLocked: decision.IsLocked);
    }

    private async Task<MultiObjectiveRecommendationMetricsReport?> FetchObjectiveMetricsAsync(
        DateTime fromDateUtc,
        DateTime toDateUtc,
        int endpointMinimumImpressions,
        MultiObjectiveRecommendationRolloutOptions options,
        CancellationToken cancellationToken)
    {
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(TimeSpan.FromSeconds(Math.Clamp(options.RequestTimeoutSeconds, 1, 30)));

        var requestUri = "/api/orders/recommendation-metrics/objective"
            + $"?fromDate={Uri.EscapeDataString(fromDateUtc.ToString("O"))}"
            + $"&toDate={Uri.EscapeDataString(toDateUtc.ToString("O"))}"
            + $"&minimumImpressions={Math.Max(1, endpointMinimumImpressions)}";
        using var request = new HttpRequestMessage(HttpMethod.Get, requestUri);
        var internalServiceKey = _orderingOptions.CurrentValue.InternalServiceKey;
        if (!string.IsNullOrWhiteSpace(internalServiceKey))
        {
            request.Headers.TryAddWithoutValidation("X-Internal-Service-Key", internalServiceKey);
        }

        try
        {
            var client = _httpClientFactory.CreateClient("Ordering");
            using var response = await client.SendAsync(request, timeout.Token);
            if (!response.IsSuccessStatusCode)
            {
                _logger.LogWarning(
                    "Cannot fetch objective recommendation metrics for rollout monitor. HTTP {StatusCode}",
                    (int)response.StatusCode);
                return null;
            }

            return await response.Content.ReadFromJsonAsync<MultiObjectiveRecommendationMetricsReport>(
                JsonOptions,
                timeout.Token);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Cannot fetch objective recommendation metrics for rollout monitor.");
            return null;
        }
    }

    private static MultiObjectiveRolloutDecision CreateDecision(
        MultiObjectiveRecommendationGroupMetric baseline,
        MultiObjectiveRecommendationGroupMetric variant,
        MultiObjectiveRecommendationWeights currentWeights,
        int currentTrafficPercent,
        DateTime stageStartedAtUtc,
        DateTime? currentStableSinceUtc,
        bool isLocked,
        DateTime? lockedAtUtc,
        bool guardrailsEligible,
        IReadOnlyList<MultiObjectiveRecommendationConfigVersion> versions,
        MultiObjectiveRecommendationRolloutOptions options,
        DateTime nowUtc)
    {
        var rolloutStage = BuildRolloutStage(currentTrafficPercent, isLocked);
        if (isLocked)
        {
            return new MultiObjectiveRolloutDecision(
                Changed: false,
                ShouldAppendVersion: false,
                TrafficPercentAfter: currentTrafficPercent,
                WeightsAfter: currentWeights,
                StableWindowCount: CalculateStableWindowCount(currentStableSinceUtc, nowUtc),
                Reason: "locked_monitor_only",
                StageStartedAtUtc: stageStartedAtUtc,
                StableSinceUtc: currentStableSinceUtc,
                IsLocked: true,
                LockedAtUtc: lockedAtUtc ?? nowUtc,
                RolloutStage: rolloutStage,
                GuardrailsEvaluated: false);
        }

        if (!guardrailsEligible)
        {
            return new MultiObjectiveRolloutDecision(
                Changed: false,
                ShouldAppendVersion: false,
                TrafficPercentAfter: currentTrafficPercent,
                WeightsAfter: currentWeights,
                StableWindowCount: 0,
                Reason: $"warming_up_{rolloutStage.Replace("%", "_percent", StringComparison.Ordinal)}",
                StageStartedAtUtc: stageStartedAtUtc,
                StableSinceUtc: currentStableSinceUtc,
                IsLocked: false,
                LockedAtUtc: null,
                RolloutStage: rolloutStage,
                GuardrailsEvaluated: false);
        }

        var rollbackForPurchase = DropsByMoreThan(
            variant.PurchaseRate,
            baseline.PurchaseRate,
            Math.Clamp(options.PurchaseRateDropRollbackRatio, 0.01d, 1d));
        if (rollbackForPurchase)
        {
            return CreateRollbackDecision(
                "rollback_purchase_rate_drop_gt_5_percent",
                currentWeights,
                currentTrafficPercent,
                stageStartedAtUtc,
                versions,
                nowUtc);
        }

        if (RevenueDropped(variant.RevenuePerImpression, baseline.RevenuePerImpression))
        {
            return CreateRollbackDecision(
                "rollback_revenue_per_impression_drop",
                currentWeights,
                currentTrafficPercent,
                stageStartedAtUtc,
                versions,
                nowUtc);
        }

        var ctrDrop = DropsByMoreThan(
            variant.Ctr,
            baseline.Ctr,
            Math.Clamp(options.CtrDropRevenueAdjustRatio, 0.01d, 1d));
        if (ctrDrop)
        {
            var adjustedWeights = currentWeights.ReduceRevenueWeight(
                Math.Clamp(options.RevenueWeightAdjustmentRatio, 0.01d, 1d));
            return new MultiObjectiveRolloutDecision(
                Changed: !adjustedWeights.Equals(currentWeights),
                ShouldAppendVersion: true,
                TrafficPercentAfter: currentTrafficPercent,
                WeightsAfter: adjustedWeights,
                StableWindowCount: 0,
                Reason: "adjust_revenue_weight_ctr_drop_gt_10_percent",
                StageStartedAtUtc: nowUtc,
                StableSinceUtc: null,
                IsLocked: false,
                LockedAtUtc: null,
                RolloutStage: rolloutStage,
                GuardrailsEvaluated: true);
        }

        var rampSteps = NormalizeRampSteps(options);
        var stableSinceUtc = currentStableSinceUtc ?? nowUtc;
        var stableDuration = nowUtc - stableSinceUtc;
        var nextStableWindowCount = CalculateStableWindowCount(stableSinceUtc, nowUtc);
        var nextTrafficPercent = currentTrafficPercent;
        var nextStageStartedAtUtc = stageStartedAtUtc;
        DateTime? nextStableSinceUtc = stableSinceUtc;
        var nextIsLocked = false;
        DateTime? nextLockedAtUtc = null;
        var reason = "monitor_stable";
        var shouldAppendVersion = false;
        var changed = false;

        if (currentTrafficPercent >= 100
            && stableDuration >= TimeSpan.FromHours(Math.Max(1, options.StableHoursForLock)))
        {
            changed = true;
            shouldAppendVersion = true;
            nextIsLocked = true;
            nextLockedAtUtc = nowUtc;
            reason = "lock_100_percent_stable_48h";
        }
        else if (currentTrafficPercent < 100
                 && stableDuration >= TimeSpan.FromHours(Math.Max(1, options.StableHoursForRamp)))
        {
            nextTrafficPercent = ResolveNextTrafficPercent(rampSteps, currentTrafficPercent);
            changed = nextTrafficPercent != currentTrafficPercent;
            shouldAppendVersion = changed;
            if (changed)
            {
                nextStageStartedAtUtc = nowUtc;
                nextStableSinceUtc = null;
                nextStableWindowCount = 0;
                reason = $"ramp_to_{nextTrafficPercent}_percent";
            }
        }

        return new MultiObjectiveRolloutDecision(
            Changed: changed,
            ShouldAppendVersion: shouldAppendVersion,
            TrafficPercentAfter: nextTrafficPercent,
            WeightsAfter: currentWeights,
            StableWindowCount: nextStableWindowCount,
            Reason: reason,
            StageStartedAtUtc: nextStageStartedAtUtc,
            StableSinceUtc: nextStableSinceUtc,
            IsLocked: nextIsLocked,
            LockedAtUtc: nextLockedAtUtc,
            RolloutStage: BuildRolloutStage(nextTrafficPercent, nextIsLocked),
            GuardrailsEvaluated: true);
    }

    private static MultiObjectiveRolloutDecision CreateRollbackDecision(
        string reason,
        MultiObjectiveRecommendationWeights currentWeights,
        int currentTrafficPercent,
        DateTime stageStartedAtUtc,
        IReadOnlyList<MultiObjectiveRecommendationConfigVersion> versions,
        DateTime nowUtc)
    {
        var targetVersion = versions.Count >= 2
            ? versions[^2]
            : versions.FirstOrDefault();
        if (targetVersion is null)
        {
            return new MultiObjectiveRolloutDecision(
                Changed: false,
                ShouldAppendVersion: false,
                TrafficPercentAfter: currentTrafficPercent,
                WeightsAfter: currentWeights,
                StableWindowCount: 0,
                Reason: $"{reason}_no_previous_config",
                StageStartedAtUtc: stageStartedAtUtc,
                StableSinceUtc: null,
                IsLocked: false,
                LockedAtUtc: null,
                RolloutStage: BuildRolloutStage(currentTrafficPercent, isLocked: false),
                GuardrailsEvaluated: true);
        }

        var rollbackWeights = MultiObjectiveRecommendationWeights.FromVersion(targetVersion).Normalize();
        var rollbackTrafficPercent = Math.Clamp(targetVersion.TrafficPercent, 0, 100);
        var changed = !rollbackWeights.Equals(currentWeights)
            || rollbackTrafficPercent != currentTrafficPercent;
        return new MultiObjectiveRolloutDecision(
            Changed: changed,
            ShouldAppendVersion: changed,
            TrafficPercentAfter: rollbackTrafficPercent,
            WeightsAfter: rollbackWeights,
            StableWindowCount: 0,
            Reason: reason,
            StageStartedAtUtc: changed ? nowUtc : stageStartedAtUtc,
            StableSinceUtc: null,
            IsLocked: false,
            LockedAtUtc: null,
            RolloutStage: BuildRolloutStage(rollbackTrafficPercent, isLocked: false),
            GuardrailsEvaluated: true);
    }

    private static SessionAwareRecommendationTuningState BuildState(
        SessionAwareRecommendationTuningState existingState,
        SessionAwareRecommendationOptions currentOptions,
        MultiObjectiveRecommendationWeights weights,
        int trafficPercent,
        int stableWindowCount,
        DateTime computedAtUtc,
        string reason,
        IReadOnlyList<MultiObjectiveRecommendationMonitorRun> runs,
        IReadOnlyList<MultiObjectiveRecommendationConfigVersion> versions,
        DateTime stageStartedAtUtc,
        DateTime? stableSinceUtc,
        bool isLocked,
        DateTime? lockedAtUtc)
    {
        return new SessionAwareRecommendationTuningState
        {
            SessionAwareRecommendation = new SessionAwareRecommendationWeightOverride
            {
                TopPositionBoostFactor = ResolvePositiveFactor(
                    existingState.SessionAwareRecommendation.TopPositionBoostFactor,
                    currentOptions.TopPositionBoostFactor),
                MidPositionBoostFactor = ResolvePositiveFactor(
                    existingState.SessionAwareRecommendation.MidPositionBoostFactor,
                    currentOptions.MidPositionBoostFactor),
                CtrWeight = weights.CtrWeight,
                AddToCartWeight = weights.AddToCartWeight,
                PurchaseWeight = weights.PurchaseWeight,
                RevenueWeight = weights.RevenueWeight,
                ObjectiveScoreScale = currentOptions.ObjectiveScoreScale
            },
            SessionAwareRecommendationTuning = existingState.SessionAwareRecommendationTuning,
            RecommendationExperiment = new RecommendationExperimentRolloutOverride
            {
                SessionRerankTrafficPercent = Math.Clamp(trafficPercent, 0, 100)
            },
            MultiObjectiveRecommendationRollout = new MultiObjectiveRecommendationRolloutState
            {
                TrafficPercent = Math.Clamp(trafficPercent, 0, 100),
                StableWindowCount = Math.Max(0, stableWindowCount),
                StageStartedAtUtc = stageStartedAtUtc == default ? computedAtUtc : stageStartedAtUtc,
                StableSinceUtc = stableSinceUtc,
                IsLocked = isLocked,
                LockedAtUtc = lockedAtUtc,
                ComputedAtUtc = computedAtUtc,
                Reason = reason,
                Runs = runs.ToList(),
                Versions = versions.ToList()
            }
        };
    }

    private static int ResolveStateTrafficPercent(
        SessionAwareRecommendationTuningState state,
        MultiObjectiveRecommendationRolloutOptions options)
    {
        if (state.RecommendationExperiment.SessionRerankTrafficPercent is >= 0 and <= 100)
        {
            return state.RecommendationExperiment.SessionRerankTrafficPercent.Value;
        }

        if (state.MultiObjectiveRecommendationRollout.ComputedAtUtc != default
            && state.MultiObjectiveRecommendationRollout.TrafficPercent is >= 0 and <= 100)
        {
            return state.MultiObjectiveRecommendationRollout.TrafficPercent;
        }

        return Math.Clamp(options.InitialTrafficPercent, 0, 100);
    }

    private int ResolveTrafficPercent(
        SessionAwareRecommendationTuningState state,
        MultiObjectiveRecommendationRolloutOptions options)
    {
        var configuredTrafficPercent = Math.Clamp(
            _experimentOptions.CurrentValue.SessionRerankTrafficPercent,
            0,
            100);
        if (state.RecommendationExperiment.SessionRerankTrafficPercent is >= 0 and <= 100
            || state.MultiObjectiveRecommendationRollout.ComputedAtUtc != default)
        {
            return ResolveStateTrafficPercent(state, options);
        }

        return configuredTrafficPercent;
    }

    private static DateTime ResolveStageStartedAtUtc(
        MultiObjectiveRecommendationRolloutState rolloutState,
        DateTime nowUtc)
    {
        if (rolloutState.StageStartedAtUtc != default)
        {
            return rolloutState.StageStartedAtUtc;
        }

        return rolloutState.ComputedAtUtc == default
            ? nowUtc
            : rolloutState.ComputedAtUtc;
    }

    private static string BuildRolloutStage(int trafficPercent, bool isLocked)
    {
        return isLocked ? "locked" : $"{Math.Clamp(trafficPercent, 0, 100)}%";
    }

    private static int CalculateStableWindowCount(DateTime? stableSinceUtc, DateTime nowUtc)
    {
        if (!stableSinceUtc.HasValue || stableSinceUtc.Value > nowUtc)
        {
            return 0;
        }

        return (int)Math.Floor((nowUtc - stableSinceUtc.Value).TotalHours);
    }

    private static double ResolvePositiveFactor(double configuredValue, double fallbackValue)
    {
        return configuredValue > 0 && !double.IsNaN(configuredValue) && !double.IsInfinity(configuredValue)
            ? configuredValue
            : fallbackValue;
    }

    private static MultiObjectiveRecommendationGroupMetric? FindMetric(
        MultiObjectiveRecommendationMetricsReport? report,
        string experimentGroup,
        string source)
    {
        return report?.Groups.FirstOrDefault(item =>
            string.Equals(item.ExperimentGroup, experimentGroup, StringComparison.OrdinalIgnoreCase)
            && string.Equals(item.RecommendationSource, source, StringComparison.OrdinalIgnoreCase));
    }

    private static bool HasMetricData(
        MultiObjectiveRecommendationGroupMetric? baseline,
        MultiObjectiveRecommendationGroupMetric? variant)
    {
        return baseline is not null
            && variant is not null
            && baseline.TotalImpressions > 0
            && variant.TotalImpressions > 0;
    }

    private static bool HasMinimumImpressions(
        MultiObjectiveRecommendationGroupMetric baseline,
        MultiObjectiveRecommendationGroupMetric variant,
        int minimumImpressions)
    {
        return baseline.TotalImpressions >= minimumImpressions
            && variant.TotalImpressions >= minimumImpressions;
    }

    private static MultiObjectiveRecommendationGroupMetric SmoothMetric(
        MultiObjectiveRecommendationGroupMetric rawMetric,
        IReadOnlyList<MultiObjectiveRecommendationMonitorRun> runs,
        bool useBaseline,
        int smoothingWindow)
    {
        var samples = runs
            .OrderBy(run => run.ComputedAtUtc)
            .TakeLast(Math.Clamp(smoothingWindow, 3, 6))
            .ToArray();
        return new MultiObjectiveRecommendationGroupMetric
        {
            RecommendationSource = rawMetric.RecommendationSource,
            ExperimentGroup = rawMetric.ExperimentGroup,
            TotalImpressions = rawMetric.TotalImpressions,
            TotalClicks = rawMetric.TotalClicks,
            TotalAddToCarts = rawMetric.TotalAddToCarts,
            TotalPurchases = rawMetric.TotalPurchases,
            TotalRevenue = rawMetric.TotalRevenue,
            Ctr = AverageNullable(samples.Select(run => useBaseline ? run.BaselineCtr : run.VariantCtr)) ?? rawMetric.Ctr,
            PurchaseRate = AverageNullable(samples.Select(run => useBaseline ? run.BaselinePurchaseRate : run.VariantPurchaseRate)) ?? rawMetric.PurchaseRate,
            RevenuePerImpression = AverageNullableDecimal(samples.Select(run => useBaseline ? run.BaselineRevenuePerImpression : run.VariantRevenuePerImpression))
                ?? rawMetric.RevenuePerImpression,
            AddToCartRate = rawMetric.AddToCartRate,
            Status = rawMetric.Status
        };
    }

    private static double? AverageNullable(IEnumerable<double?> values)
    {
        var validValues = values
            .Where(value => value.HasValue)
            .Select(value => value!.Value)
            .ToArray();
        return validValues.Length == 0
            ? null
            : Math.Round(validValues.Average(), 6);
    }

    private static decimal? AverageNullableDecimal(IEnumerable<decimal?> values)
    {
        var validValues = values
            .Where(value => value.HasValue)
            .Select(value => value!.Value)
            .ToArray();
        return validValues.Length == 0
            ? null
            : Math.Round(validValues.Average(), 6, MidpointRounding.AwayFromZero);
    }

    private static bool DropsByMoreThan(double? variant, double? baseline, double ratio)
    {
        return baseline is > 0
            && variant.HasValue
            && variant.Value < baseline.Value * (1d - ratio);
    }

    private static bool RevenueDropped(decimal? variant, decimal? baseline)
    {
        return baseline is > 0m
            && variant.HasValue
            && variant.Value < baseline.Value;
    }

    private static IReadOnlyList<int> NormalizeRampSteps(MultiObjectiveRecommendationRolloutOptions options)
    {
        var steps = options.RampSteps is { Length: > 0 }
            ? options.RampSteps
            : new[] { options.InitialTrafficPercent, 50, 100 };
        return steps
            .Append(options.InitialTrafficPercent)
            .Append(100)
            .Select(percent => Math.Clamp(percent, 0, 100))
            .Distinct()
            .OrderBy(percent => percent)
            .ToArray();
    }

    private static int ResolveNextTrafficPercent(IReadOnlyList<int> rampSteps, int currentTrafficPercent)
    {
        foreach (var step in rampSteps)
        {
            if (step > currentTrafficPercent)
            {
                return step;
            }
        }

        return currentTrafficPercent;
    }

    private static IReadOnlyList<MultiObjectiveRecommendationConfigVersion> NormalizeVersions(
        SessionAwareRecommendationTuningState state,
        MultiObjectiveRecommendationWeights currentWeights,
        int currentTrafficPercent,
        DateTime computedAtUtc)
    {
        var versions = state.MultiObjectiveRecommendationRollout.Versions
            .Where(version => version.RevenueWeight >= 0)
            .OrderBy(version => version.ComputedAtUtc)
            .ToList();
        if (versions.Count > 0)
        {
            return versions;
        }

        versions.Add(new MultiObjectiveRecommendationConfigVersion
        {
            ComputedAtUtc = computedAtUtc,
            CtrWeight = currentWeights.CtrWeight,
            AddToCartWeight = currentWeights.AddToCartWeight,
            PurchaseWeight = currentWeights.PurchaseWeight,
            RevenueWeight = currentWeights.RevenueWeight,
            TrafficPercent = currentTrafficPercent,
            Reason = "baseline_config"
        });
        return versions;
    }

    private static IReadOnlyList<MultiObjectiveRecommendationMonitorRun> AppendAndTrimRuns(
        IReadOnlyList<MultiObjectiveRecommendationMonitorRun> existingRuns,
        MultiObjectiveRecommendationMonitorRun nextRun,
        int retainedRunCount)
    {
        return existingRuns
            .Append(nextRun)
            .OrderBy(run => run.ComputedAtUtc)
            .TakeLast(Math.Clamp(retainedRunCount, 1, 48))
            .ToList();
    }

    private static IReadOnlyList<MultiObjectiveRecommendationConfigVersion> AppendAndTrimVersions(
        IReadOnlyList<MultiObjectiveRecommendationConfigVersion> versions,
        MultiObjectiveRecommendationConfigVersion nextVersion,
        int retainedVersionCount)
    {
        return versions
            .Append(nextVersion)
            .OrderBy(version => version.ComputedAtUtc)
            .TakeLast(Math.Clamp(retainedVersionCount, 1, 20))
            .ToList();
    }

    private static IReadOnlyList<MultiObjectiveRecommendationConfigVersion> TrimVersions(
        IReadOnlyList<MultiObjectiveRecommendationConfigVersion> versions,
        int retainedVersionCount)
    {
        return versions
            .OrderBy(version => version.ComputedAtUtc)
            .TakeLast(Math.Clamp(retainedVersionCount, 1, 20))
            .ToList();
    }

    private void LogDecision(
        MultiObjectiveRecommendationWeights currentWeights,
        int currentTrafficPercent,
        MultiObjectiveRecommendationGroupMetric baseline,
        MultiObjectiveRecommendationGroupMetric variant,
        MultiObjectiveRolloutDecision decision)
    {
        var logLevel = decision.Reason.StartsWith("rollback", StringComparison.OrdinalIgnoreCase)
                       || decision.Reason.StartsWith("adjust", StringComparison.OrdinalIgnoreCase)
            ? LogLevel.Warning
            : LogLevel.Information;
        _logger.Log(
            logLevel,
            "Multi-objective recommendation rollout monitor. Stage={RolloutStage}, GuardrailsEvaluated={GuardrailsEvaluated}, Decision={Decision}, Traffic old={OldTraffic}% new={NewTraffic}%, Weights old={OldWeights} new={NewWeights}, Baseline A/ML ctr={BaselineCtr:0.####} purchaseRate={BaselinePurchaseRate:0.####} revenuePerImpression={BaselineRevenuePerImpression}, Variant B/Session ctr={VariantCtr:0.####} purchaseRate={VariantPurchaseRate:0.####} revenuePerImpression={VariantRevenuePerImpression}",
            decision.RolloutStage,
            decision.GuardrailsEvaluated,
            decision.Reason,
            currentTrafficPercent,
            decision.TrafficPercentAfter,
            currentWeights,
            decision.WeightsAfter,
            baseline.Ctr,
            baseline.PurchaseRate,
            baseline.RevenuePerImpression,
            variant.Ctr,
            variant.PurchaseRate,
            variant.RevenuePerImpression);
    }

    private sealed record MultiObjectiveRolloutDecision(
        bool Changed,
        bool ShouldAppendVersion,
        int TrafficPercentAfter,
        MultiObjectiveRecommendationWeights WeightsAfter,
        int StableWindowCount,
        string Reason,
        DateTime StageStartedAtUtc,
        DateTime? StableSinceUtc,
        bool IsLocked,
        DateTime? LockedAtUtc,
        string RolloutStage,
        bool GuardrailsEvaluated);
}

public sealed record MultiObjectiveRecommendationRolloutResult(
    bool Changed,
    string Decision,
    int TrafficPercentBefore,
    int TrafficPercentAfter,
    MultiObjectiveRecommendationWeights WeightsBefore,
    MultiObjectiveRecommendationWeights WeightsAfter,
    MultiObjectiveRecommendationGroupMetric? Baseline,
    MultiObjectiveRecommendationGroupMetric? Variant,
    string RolloutStage,
    bool GuardrailsEvaluated,
    bool IsLocked);

public sealed record MultiObjectiveRecommendationWeights(
    double CtrWeight,
    double AddToCartWeight,
    double PurchaseWeight,
    double RevenueWeight)
{
    public static MultiObjectiveRecommendationWeights FromOptions(SessionAwareRecommendationOptions options)
    {
        return new MultiObjectiveRecommendationWeights(
            options.CtrWeight,
            options.AddToCartWeight,
            options.PurchaseWeight,
            options.RevenueWeight);
    }

    public static MultiObjectiveRecommendationWeights FromVersion(MultiObjectiveRecommendationConfigVersion version)
    {
        return new MultiObjectiveRecommendationWeights(
            version.CtrWeight,
            version.AddToCartWeight,
            version.PurchaseWeight,
            version.RevenueWeight);
    }

    public MultiObjectiveRecommendationWeights Normalize()
    {
        var ctr = ClampWeight(CtrWeight);
        var addToCart = ClampWeight(AddToCartWeight);
        var purchase = ClampWeight(PurchaseWeight);
        var revenue = ClampWeight(RevenueWeight);
        var sum = ctr + addToCart + purchase + revenue;
        if (sum <= 0)
        {
            return new MultiObjectiveRecommendationWeights(0.12, 0.08, 0.35, 0.45);
        }

        return new MultiObjectiveRecommendationWeights(
            Math.Round(ctr / sum, 4),
            Math.Round(addToCart / sum, 4),
            Math.Round(purchase / sum, 4),
            Math.Round(revenue / sum, 4));
    }

    public MultiObjectiveRecommendationWeights ReduceRevenueWeight(double ratio)
    {
        var normalized = Normalize();
        var reduction = normalized.RevenueWeight * ratio;
        var nextRevenue = normalized.RevenueWeight - reduction;
        var otherTotal = normalized.CtrWeight + normalized.AddToCartWeight + normalized.PurchaseWeight;
        if (otherTotal <= 0)
        {
            return (normalized with { RevenueWeight = Math.Round(nextRevenue, 4) }).Normalize();
        }

        return new MultiObjectiveRecommendationWeights(
            normalized.CtrWeight + reduction * normalized.CtrWeight / otherTotal,
            normalized.AddToCartWeight + reduction * normalized.AddToCartWeight / otherTotal,
            normalized.PurchaseWeight + reduction * normalized.PurchaseWeight / otherTotal,
            nextRevenue).Normalize();
    }

    public override string ToString()
    {
        return $"ctr={CtrWeight:0.####},addToCart={AddToCartWeight:0.####},purchase={PurchaseWeight:0.####},revenue={RevenueWeight:0.####}";
    }

    private static double ClampWeight(double value)
    {
        return double.IsNaN(value) || double.IsInfinity(value)
            ? 0d
            : Math.Clamp(value, 0d, 1d);
    }
}

public sealed class MultiObjectiveRecommendationMetricsReport
{
    public DateTime FromDateUtc { get; set; }

    public DateTime ToDateUtc { get; set; }

    public int MinimumImpressions { get; set; }

    public List<MultiObjectiveRecommendationGroupMetric> Groups { get; set; } = new();
}

public sealed class MultiObjectiveRecommendationGroupMetric
{
    public string RecommendationSource { get; set; } = string.Empty;

    public string ExperimentGroup { get; set; } = string.Empty;

    public long TotalImpressions { get; set; }

    public long TotalClicks { get; set; }

    public long TotalAddToCarts { get; set; }

    public long TotalPurchases { get; set; }

    public decimal TotalRevenue { get; set; }

    public double? Ctr { get; set; }

    public double? AddToCartRate { get; set; }

    public double? PurchaseRate { get; set; }

    public decimal? RevenuePerImpression { get; set; }

    public string Status { get; set; } = string.Empty;
}
