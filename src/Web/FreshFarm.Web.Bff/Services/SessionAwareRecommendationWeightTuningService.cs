using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;
using FreshFarm.Web.Bff.Options;
using Microsoft.Extensions.Options;

namespace FreshFarm.Web.Bff.Services;

public interface ISessionAwareRecommendationWeightTuningService
{
    Task<SessionAwareRecommendationWeightTuningResult> TuneAsync(
        CancellationToken cancellationToken = default);
}

public interface ISessionAwareRecommendationConfigStore
{
    Task<SessionAwareRecommendationTuningState?> LoadStateAsync(
        CancellationToken cancellationToken = default);

    Task PersistStateAsync(
        SessionAwareRecommendationTuningState state,
        CancellationToken cancellationToken = default);
}

public sealed class SessionAwareRecommendationWeightTuningService : ISessionAwareRecommendationWeightTuningService
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
    {
        WriteIndented = true,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
    };

    private readonly IHttpClientFactory _httpClientFactory;
    private readonly IOptionsMonitor<SessionAwareRecommendationOptions> _recommendationOptions;
    private readonly IOptionsMonitor<SessionAwareRecommendationTuningOptions> _tuningOptions;
    private readonly IOptionsMonitor<OrderingServiceOptions> _orderingOptions;
    private readonly ISessionAwareRecommendationConfigStore _configStore;
    private readonly ILogger<SessionAwareRecommendationWeightTuningService> _logger;

    public SessionAwareRecommendationWeightTuningService(
        IHttpClientFactory httpClientFactory,
        IOptionsMonitor<SessionAwareRecommendationOptions> recommendationOptions,
        IOptionsMonitor<SessionAwareRecommendationTuningOptions> tuningOptions,
        IOptionsMonitor<OrderingServiceOptions> orderingOptions,
        ISessionAwareRecommendationConfigStore configStore,
        ILogger<SessionAwareRecommendationWeightTuningService> logger)
    {
        _httpClientFactory = httpClientFactory;
        _recommendationOptions = recommendationOptions;
        _tuningOptions = tuningOptions;
        _orderingOptions = orderingOptions;
        _configStore = configStore;
        _logger = logger;
    }

    public async Task<SessionAwareRecommendationWeightTuningResult> TuneAsync(
        CancellationToken cancellationToken = default)
    {
        var tuningOptions = _tuningOptions.CurrentValue;
        var currentWeights = _recommendationOptions.CurrentValue;
        var state = await _configStore.LoadStateAsync(cancellationToken);
        var nowUtc = DateTime.UtcNow;
        var baselineTopFactor = ResolveBaselineFactor(
            state?.SessionAwareRecommendationTuning.BaselineTopPositionBoostFactor,
            currentWeights.TopPositionBoostFactor,
            tuningOptions.TopPositionBoostFactorMin,
            tuningOptions.TopPositionBoostFactorMax);
        var baselineMidFactor = ResolveBaselineFactor(
            state?.SessionAwareRecommendationTuning.BaselineMidPositionBoostFactor,
            currentWeights.MidPositionBoostFactor,
            tuningOptions.MidPositionBoostFactorMin,
            tuningOptions.MidPositionBoostFactorMax);

        var toDateUtc = nowUtc;
        var fromDateUtc = toDateUtc.AddDays(-Math.Clamp(tuningOptions.LookbackDays, 1, 90));
        var metrics = await FetchCtrByPositionAsync(fromDateUtc, toDateUtc, tuningOptions, cancellationToken);
        var comparisons = BuildEligibleComparisons(metrics, Math.Max(1, tuningOptions.MinimumImpressions));

        if (comparisons.Count == 0)
        {
            _logger.LogInformation(
                "Session-aware rerank weight tuning skipped: no eligible positions with at least {MinimumImpressions} impressions per strategy.",
                tuningOptions.MinimumImpressions);
            return SessionAwareRecommendationWeightTuningResult.NoChange(
                currentWeights.TopPositionBoostFactor,
                currentWeights.MidPositionBoostFactor,
                "insufficient_data");
        }

        var topComparisons = comparisons.Where(item => item.Position is >= 1 and <= 2).ToArray();
        var midComparisons = comparisons.Where(item => item.Position >= 3).ToArray();
        var currentRun = BuildTuningRun(nowUtc, topComparisons, midComparisons, comparisons);
        var previousRuns = NormalizeRuns(state);
        var smoothingRunCount = Math.Clamp(tuningOptions.SmoothingRunCount, 1, 30);
        var retainedVersionCount = Math.Clamp(tuningOptions.RetainedConfigVersionCount, 1, 30);
        var smoothedRuns = AppendAndTrimRuns(previousRuns, currentRun, smoothingRunCount);
        var versions = NormalizeVersions(state, currentWeights, nowUtc);

        foreach (var comparison in comparisons)
        {
            _logger.LogInformation(
                "CTR position comparison. Position={Position}, MlCtr={MlCtr:0.####}, SessionCtr={SessionCtr:0.####}, Delta={Delta:0.####}, MlImpressions={MlImpressions}, SessionImpressions={SessionImpressions}",
                comparison.Position,
                comparison.MlCtrPercentage,
                comparison.SessionCtrPercentage,
                comparison.SessionCtrPercentage - comparison.MlCtrPercentage,
                comparison.MlImpressions,
                comparison.SessionImpressions);
        }

        var rollback = TryCreateRollbackDecision(
            versions,
            previousRuns,
            currentRun,
            currentWeights,
            tuningOptions);
        if (rollback is not null)
        {
            var rollbackVersions = AppendAndTrimVersions(
                versions,
                new SessionAwareRecommendationConfigVersion
                {
                    ComputedAtUtc = nowUtc,
                    TopPositionBoostFactor = rollback.TopPositionBoostFactor,
                    MidPositionBoostFactor = rollback.MidPositionBoostFactor,
                    Reason = rollback.Reason
                },
                retainedVersionCount);
            var rollbackState = BuildState(
                rollback.TopPositionBoostFactor,
                rollback.MidPositionBoostFactor,
                baselineTopFactor,
                baselineMidFactor,
                nowUtc,
                rollback.Reason,
                smoothedRuns,
                rollbackVersions,
                state);
            await _configStore.PersistStateAsync(rollbackState, cancellationToken);

            _logger.LogWarning(
                "Session-aware rerank weights rolled back. TopFactor oldFactor={TopOld:0.####} newFactor={TopNew:0.####} reason={TopReason}; MidFactor oldFactor={MidOld:0.####} newFactor={MidNew:0.####} reason={MidReason}; PreviousSessionCtr={PreviousSessionCtr:0.####}; CurrentSessionCtr={CurrentSessionCtr:0.####}",
                currentWeights.TopPositionBoostFactor,
                rollback.TopPositionBoostFactor,
                rollback.Reason,
                currentWeights.MidPositionBoostFactor,
                rollback.MidPositionBoostFactor,
                rollback.Reason,
                rollback.PreviousSessionCtrPercentage,
                rollback.CurrentSessionCtrPercentage);

            return new SessionAwareRecommendationWeightTuningResult(
                Changed: true,
                TopPositionBoostFactorBefore: currentWeights.TopPositionBoostFactor,
                TopPositionBoostFactorAfter: rollback.TopPositionBoostFactor,
                MidPositionBoostFactorBefore: currentWeights.MidPositionBoostFactor,
                MidPositionBoostFactorAfter: rollback.MidPositionBoostFactor,
                EligibleTopPositionCount: currentRun.EligibleTopPositionCount,
                EligibleMidPositionCount: currentRun.EligibleMidPositionCount,
                Reason: rollback.Reason,
                PositionComparisons: comparisons);
        }

        var topAverage = BuildMovingAverageSegment("top", previousRuns, currentRun, smoothingRunCount);
        var midAverage = BuildMovingAverageSegment("mid", previousRuns, currentRun, smoothingRunCount);
        var currentTopSummary = SummarizeSegment(topComparisons);
        var currentMidSummary = SummarizeSegment(midComparisons);
        var topDecision = CalculateSegmentDecision(
            "top",
            currentWeights.TopPositionBoostFactor,
            baselineTopFactor,
            currentTopSummary,
            topAverage,
            tuningOptions.TopPositionBoostFactorMin,
            tuningOptions.TopPositionBoostFactorMax,
            tuningOptions,
            forceReduceWhenSessionBelowMl: false);
        var midDecision = CalculateSegmentDecision(
            "mid",
            currentWeights.MidPositionBoostFactor,
            baselineMidFactor,
            currentMidSummary,
            midAverage,
            tuningOptions.MidPositionBoostFactorMin,
            tuningOptions.MidPositionBoostFactorMax,
            tuningOptions,
            forceReduceWhenSessionBelowMl: true);

        var changed = topDecision.Changed || midDecision.Changed;
        var reason = $"{topDecision.Reason};{midDecision.Reason}";
        var persistedVersions = changed
            ? AppendAndTrimVersions(
                versions,
                new SessionAwareRecommendationConfigVersion
                {
                    ComputedAtUtc = nowUtc,
                    TopPositionBoostFactor = topDecision.NewFactor,
                    MidPositionBoostFactor = midDecision.NewFactor,
                    Reason = reason
                },
                retainedVersionCount)
            : TrimVersions(versions, retainedVersionCount);
        var nextState = BuildState(
            topDecision.NewFactor,
            midDecision.NewFactor,
            baselineTopFactor,
            baselineMidFactor,
            nowUtc,
            reason,
            smoothedRuns,
            persistedVersions,
            state);
        await _configStore.PersistStateAsync(nextState, cancellationToken);

        if (!changed)
        {
            _logger.LogInformation(
                "Session-aware rerank weight tuning completed without factor changes. TopFactor oldFactor={TopOld:0.####} newFactor={TopNew:0.####} reason={TopReason}; MidFactor oldFactor={MidOld:0.####} newFactor={MidNew:0.####} reason={MidReason}",
                currentWeights.TopPositionBoostFactor,
                topDecision.NewFactor,
                topDecision.Reason,
                currentWeights.MidPositionBoostFactor,
                midDecision.NewFactor,
                midDecision.Reason);
            return new SessionAwareRecommendationWeightTuningResult(
                Changed: false,
                TopPositionBoostFactorBefore: currentWeights.TopPositionBoostFactor,
                TopPositionBoostFactorAfter: topDecision.NewFactor,
                MidPositionBoostFactorBefore: currentWeights.MidPositionBoostFactor,
                MidPositionBoostFactorAfter: midDecision.NewFactor,
                EligibleTopPositionCount: topDecision.EligiblePositionCount,
                EligibleMidPositionCount: midDecision.EligiblePositionCount,
                Reason: reason,
                PositionComparisons: comparisons);
        }

        _logger.LogInformation(
            "Session-aware rerank weights adjusted. TopFactor oldFactor={TopOld:0.####} newFactor={TopNew:0.####} reason={TopReason}; MidFactor oldFactor={MidOld:0.####} newFactor={MidNew:0.####} reason={MidReason}",
            currentWeights.TopPositionBoostFactor,
            topDecision.NewFactor,
            topDecision.Reason,
            currentWeights.MidPositionBoostFactor,
            midDecision.NewFactor,
            midDecision.Reason);

        return new SessionAwareRecommendationWeightTuningResult(
            Changed: true,
            TopPositionBoostFactorBefore: currentWeights.TopPositionBoostFactor,
            TopPositionBoostFactorAfter: topDecision.NewFactor,
            MidPositionBoostFactorBefore: currentWeights.MidPositionBoostFactor,
            MidPositionBoostFactorAfter: midDecision.NewFactor,
            EligibleTopPositionCount: topDecision.EligiblePositionCount,
            EligibleMidPositionCount: midDecision.EligiblePositionCount,
            Reason: reason,
            PositionComparisons: comparisons);
    }

    private async Task<IReadOnlyList<RecommendationCtrByPositionMetricDto>> FetchCtrByPositionAsync(
        DateTime fromDateUtc,
        DateTime toDateUtc,
        SessionAwareRecommendationTuningOptions tuningOptions,
        CancellationToken cancellationToken)
    {
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(TimeSpan.FromSeconds(Math.Clamp(tuningOptions.RequestTimeoutSeconds, 1, 30)));

        var client = _httpClientFactory.CreateClient("Ordering");
        var requestUri = "/api/orders/recommendation-metrics/ctr-by-position"
            + $"?fromDate={Uri.EscapeDataString(fromDateUtc.ToString("O"))}"
            + $"&toDate={Uri.EscapeDataString(toDateUtc.ToString("O"))}";
        using var request = new HttpRequestMessage(HttpMethod.Get, requestUri);
        var internalServiceKey = _orderingOptions.CurrentValue.InternalServiceKey;
        if (!string.IsNullOrWhiteSpace(internalServiceKey))
        {
            request.Headers.TryAddWithoutValidation("X-Internal-Service-Key", internalServiceKey);
        }

        try
        {
            using var response = await client.SendAsync(request, timeout.Token);
            if (!response.IsSuccessStatusCode)
            {
                _logger.LogWarning(
                    "Cannot fetch CTR-by-position data for rerank tuning. HTTP {StatusCode}",
                    (int)response.StatusCode);
                return Array.Empty<RecommendationCtrByPositionMetricDto>();
            }

            var payload = await response.Content.ReadFromJsonAsync<RecommendationCtrByPositionReportDto>(
                JsonOptions,
                timeout.Token);
            return payload is null
                ? Array.Empty<RecommendationCtrByPositionMetricDto>()
                : payload.Metrics;
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Cannot fetch CTR-by-position data for rerank tuning.");
            return Array.Empty<RecommendationCtrByPositionMetricDto>();
        }
    }

    private static IReadOnlyList<PositionCtrComparison> BuildEligibleComparisons(
        IReadOnlyList<RecommendationCtrByPositionMetricDto> metrics,
        int minimumImpressions)
    {
        var groups = metrics
            .Where(item => item.Position > 0)
            .GroupBy(item => item.Position)
            .OrderBy(group => group.Key);
        var comparisons = new List<PositionCtrComparison>();

        foreach (var group in groups)
        {
            var ml = group.FirstOrDefault(item =>
                string.Equals(item.ExperimentGroup, "A", StringComparison.OrdinalIgnoreCase)
                && string.Equals(item.RecommendationSource, "ML", StringComparison.OrdinalIgnoreCase));
            var session = group.FirstOrDefault(item =>
                string.Equals(item.ExperimentGroup, "B", StringComparison.OrdinalIgnoreCase)
                && string.Equals(item.RecommendationSource, "Session", StringComparison.OrdinalIgnoreCase));

            if (ml is null
                || session is null
                || ml.TotalImpressions < minimumImpressions
                || session.TotalImpressions < minimumImpressions)
            {
                continue;
            }

            comparisons.Add(new PositionCtrComparison(
                Position: group.Key,
                MlImpressions: ml.TotalImpressions,
                MlClicks: ml.TotalClicks,
                MlCtrPercentage: CalculateCtrPercentage(ml.TotalClicks, ml.TotalImpressions),
                SessionImpressions: session.TotalImpressions,
                SessionClicks: session.TotalClicks,
                SessionCtrPercentage: CalculateCtrPercentage(session.TotalClicks, session.TotalImpressions)));
        }

        return comparisons;
    }

    private static SegmentAdjustmentDecision CalculateSegmentDecision(
        string segment,
        double currentFactor,
        double baselineFactor,
        SegmentCtrSummary? currentSummary,
        SegmentCtrSummary? movingAverageSummary,
        double minimumFactor,
        double maximumFactor,
        SessionAwareRecommendationTuningOptions options,
        bool forceReduceWhenSessionBelowMl)
    {
        if (currentSummary is null || movingAverageSummary is null)
        {
            return SegmentAdjustmentDecision.NoChange(segment, currentFactor, "insufficient_data");
        }

        var reasonSuffix = "session_ctr_smoothed";
        var direction = CompareCtr(movingAverageSummary.SessionCtrPercentage, movingAverageSummary.MlCtrPercentage);
        if (forceReduceWhenSessionBelowMl
            && CompareCtr(currentSummary.SessionCtrPercentage, currentSummary.MlCtrPercentage) < 0)
        {
            direction = -1;
            reasonSuffix = "guardrail_session_below_ml";
        }

        if (direction == 0)
        {
            return SegmentAdjustmentDecision.NoChange(
                segment,
                currentFactor,
                "no_smoothed_ctr_delta",
                currentSummary.EligiblePositionCount,
                movingAverageSummary.MlCtrPercentage,
                movingAverageSummary.SessionCtrPercentage);
        }

        var maxAdjustmentRatio = Math.Clamp(options.MaxAdjustmentRatio, 0d, 0.10d);
        var adjustment = direction > 0
            ? maxAdjustmentRatio
            : -maxAdjustmentRatio;
        var newFactor = Math.Clamp(
            Math.Round(baselineFactor * (1d + adjustment), 4),
            Math.Min(minimumFactor, maximumFactor),
            Math.Max(minimumFactor, maximumFactor));
        var changed = Math.Abs(newFactor - currentFactor) > 0.0001d;
        var directionLabel = direction > 0
            ? "increase"
            : "decrease";
        var reason = $"{segment}_{reasonSuffix}_{directionLabel}";

        return new SegmentAdjustmentDecision(
            Segment: segment,
            Changed: changed,
            OldFactor: currentFactor,
            NewFactor: newFactor,
            EligiblePositionCount: currentSummary.EligiblePositionCount,
            MlCtrPercentage: movingAverageSummary.MlCtrPercentage,
            SessionCtrPercentage: movingAverageSummary.SessionCtrPercentage,
            Reason: changed ? reason : $"{reason}_already_applied");
    }

    private static int CompareCtr(double left, double right)
    {
        return Math.Abs(left - right) < 0.0001d
            ? 0
            : left.CompareTo(right);
    }

    private static double CalculateCtrPercentage(long clicks, long impressions)
    {
        return impressions <= 0
            ? 0d
            : Math.Round(clicks * 100d / impressions, 4);
    }

    private static SessionAwareRecommendationTuningRun BuildTuningRun(
        DateTime computedAtUtc,
        IReadOnlyList<PositionCtrComparison> topComparisons,
        IReadOnlyList<PositionCtrComparison> midComparisons,
        IReadOnlyList<PositionCtrComparison> allComparisons)
    {
        var topSummary = SummarizeSegment(topComparisons);
        var midSummary = SummarizeSegment(midComparisons);
        var overallSummary = SummarizeSegment(allComparisons);

        return new SessionAwareRecommendationTuningRun
        {
            ComputedAtUtc = computedAtUtc,
            TopMlCtrPercentage = topSummary?.MlCtrPercentage,
            TopSessionCtrPercentage = topSummary?.SessionCtrPercentage,
            MidMlCtrPercentage = midSummary?.MlCtrPercentage,
            MidSessionCtrPercentage = midSummary?.SessionCtrPercentage,
            OverallMlCtrPercentage = overallSummary?.MlCtrPercentage,
            OverallSessionCtrPercentage = overallSummary?.SessionCtrPercentage,
            EligibleTopPositionCount = topSummary?.EligiblePositionCount ?? 0,
            EligibleMidPositionCount = midSummary?.EligiblePositionCount ?? 0
        };
    }

    private static SegmentCtrSummary? SummarizeSegment(
        IReadOnlyList<PositionCtrComparison> comparisons)
    {
        if (comparisons.Count == 0)
        {
            return null;
        }

        return new SegmentCtrSummary(
            comparisons.Count,
            CalculateCtrPercentage(comparisons.Sum(item => item.MlClicks), comparisons.Sum(item => item.MlImpressions)),
            CalculateCtrPercentage(comparisons.Sum(item => item.SessionClicks), comparisons.Sum(item => item.SessionImpressions)));
    }

    private static SegmentCtrSummary? BuildMovingAverageSegment(
        string segment,
        IReadOnlyList<SessionAwareRecommendationTuningRun> previousRuns,
        SessionAwareRecommendationTuningRun currentRun,
        int smoothingRunCount)
    {
        if (!HasSegmentCtr(currentRun, segment))
        {
            return null;
        }

        var samples = previousRuns
            .Append(currentRun)
            .Where(run => HasSegmentCtr(run, segment))
            .OrderBy(run => run.ComputedAtUtc)
            .TakeLast(smoothingRunCount)
            .ToArray();
        if (samples.Length == 0)
        {
            return null;
        }

        return new SegmentCtrSummary(
            segment.Equals("top", StringComparison.OrdinalIgnoreCase)
                ? currentRun.EligibleTopPositionCount
                : currentRun.EligibleMidPositionCount,
            Math.Round(samples.Average(run => GetMlCtr(run, segment)!.Value), 4),
            Math.Round(samples.Average(run => GetSessionCtr(run, segment)!.Value), 4));
    }

    private static bool HasSegmentCtr(SessionAwareRecommendationTuningRun run, string segment)
    {
        return GetMlCtr(run, segment).HasValue && GetSessionCtr(run, segment).HasValue;
    }

    private static double? GetMlCtr(SessionAwareRecommendationTuningRun run, string segment)
    {
        return segment.Equals("top", StringComparison.OrdinalIgnoreCase)
            ? run.TopMlCtrPercentage
            : run.MidMlCtrPercentage;
    }

    private static double? GetSessionCtr(SessionAwareRecommendationTuningRun run, string segment)
    {
        return segment.Equals("top", StringComparison.OrdinalIgnoreCase)
            ? run.TopSessionCtrPercentage
            : run.MidSessionCtrPercentage;
    }

    private static IReadOnlyList<SessionAwareRecommendationTuningRun> NormalizeRuns(
        SessionAwareRecommendationTuningState? state)
    {
        return state?.SessionAwareRecommendationTuning.Runs
            .Where(HasAnyCtr)
            .OrderBy(run => run.ComputedAtUtc)
            .ToArray()
            ?? Array.Empty<SessionAwareRecommendationTuningRun>();
    }

    private static List<SessionAwareRecommendationTuningRun> AppendAndTrimRuns(
        IReadOnlyList<SessionAwareRecommendationTuningRun> previousRuns,
        SessionAwareRecommendationTuningRun currentRun,
        int smoothingRunCount)
    {
        return previousRuns
            .Append(currentRun)
            .Where(HasAnyCtr)
            .OrderBy(run => run.ComputedAtUtc)
            .TakeLast(smoothingRunCount)
            .ToList();
    }

    private static bool HasAnyCtr(SessionAwareRecommendationTuningRun run)
    {
        return run.TopMlCtrPercentage.HasValue
            || run.TopSessionCtrPercentage.HasValue
            || run.MidMlCtrPercentage.HasValue
            || run.MidSessionCtrPercentage.HasValue
            || run.OverallMlCtrPercentage.HasValue
            || run.OverallSessionCtrPercentage.HasValue;
    }

    private static IReadOnlyList<SessionAwareRecommendationConfigVersion> NormalizeVersions(
        SessionAwareRecommendationTuningState? state,
        SessionAwareRecommendationOptions currentWeights,
        DateTime computedAtUtc)
    {
        var versions = state?.SessionAwareRecommendationTuning.Versions
            .Where(version => version.TopPositionBoostFactor > 0 && version.MidPositionBoostFactor > 0)
            .OrderBy(version => version.ComputedAtUtc)
            .ToList()
            ?? new List<SessionAwareRecommendationConfigVersion>();

        if (versions.Count > 0)
        {
            return versions;
        }

        if (state?.SessionAwareRecommendation.TopPositionBoostFactor > 0
            && state.SessionAwareRecommendation.MidPositionBoostFactor > 0)
        {
            versions.Add(new SessionAwareRecommendationConfigVersion
            {
                ComputedAtUtc = state.SessionAwareRecommendationTuning.ComputedAtUtc == default
                    ? computedAtUtc
                    : state.SessionAwareRecommendationTuning.ComputedAtUtc,
                TopPositionBoostFactor = state.SessionAwareRecommendation.TopPositionBoostFactor,
                MidPositionBoostFactor = state.SessionAwareRecommendation.MidPositionBoostFactor,
                Reason = string.IsNullOrWhiteSpace(state.SessionAwareRecommendationTuning.Reason)
                    ? "loaded_config"
                    : state.SessionAwareRecommendationTuning.Reason
            });
            return versions;
        }

        versions.Add(new SessionAwareRecommendationConfigVersion
        {
            ComputedAtUtc = computedAtUtc,
            TopPositionBoostFactor = currentWeights.TopPositionBoostFactor,
            MidPositionBoostFactor = currentWeights.MidPositionBoostFactor,
            Reason = "baseline_config"
        });
        return versions;
    }

    private static IReadOnlyList<SessionAwareRecommendationConfigVersion> AppendAndTrimVersions(
        IReadOnlyList<SessionAwareRecommendationConfigVersion> versions,
        SessionAwareRecommendationConfigVersion nextVersion,
        int retainedVersionCount)
    {
        return versions
            .Append(nextVersion)
            .OrderBy(version => version.ComputedAtUtc)
            .TakeLast(retainedVersionCount)
            .ToList();
    }

    private static IReadOnlyList<SessionAwareRecommendationConfigVersion> TrimVersions(
        IReadOnlyList<SessionAwareRecommendationConfigVersion> versions,
        int retainedVersionCount)
    {
        return versions
            .OrderBy(version => version.ComputedAtUtc)
            .TakeLast(retainedVersionCount)
            .ToList();
    }

    private static RollbackDecision? TryCreateRollbackDecision(
        IReadOnlyList<SessionAwareRecommendationConfigVersion> versions,
        IReadOnlyList<SessionAwareRecommendationTuningRun> previousRuns,
        SessionAwareRecommendationTuningRun currentRun,
        SessionAwareRecommendationOptions currentWeights,
        SessionAwareRecommendationTuningOptions options)
    {
        if (versions.Count < 2 || !currentRun.OverallSessionCtrPercentage.HasValue)
        {
            return null;
        }

        var previousRun = previousRuns
            .Where(run => run.OverallSessionCtrPercentage.HasValue)
            .OrderBy(run => run.ComputedAtUtc)
            .LastOrDefault();
        if (previousRun?.OverallSessionCtrPercentage is not > 0)
        {
            return null;
        }

        var rollbackThreshold = Math.Clamp(options.RollbackCtrDropRatio, 0.01d, 1d);
        var currentSessionCtr = currentRun.OverallSessionCtrPercentage.Value;
        var previousSessionCtr = previousRun.OverallSessionCtrPercentage.Value;
        if (currentSessionCtr >= previousSessionCtr * (1d - rollbackThreshold))
        {
            return null;
        }

        var targetVersion = versions[^2];
        if (Math.Abs(targetVersion.TopPositionBoostFactor - currentWeights.TopPositionBoostFactor) < 0.0001d
            && Math.Abs(targetVersion.MidPositionBoostFactor - currentWeights.MidPositionBoostFactor) < 0.0001d)
        {
            return null;
        }

        var thresholdPercent = Math.Round(rollbackThreshold * 100d, 0);
        return new RollbackDecision(
            targetVersion.TopPositionBoostFactor,
            targetVersion.MidPositionBoostFactor,
            $"rollback_ctr_drop_gt_{thresholdPercent:0}_percent",
            previousSessionCtr,
            currentSessionCtr);
    }

    private static double ResolveBaselineFactor(
        double? storedBaseline,
        double currentFactor,
        double minimumFactor,
        double maximumFactor)
    {
        var candidate = storedBaseline is > 0 && !double.IsNaN(storedBaseline.Value) && !double.IsInfinity(storedBaseline.Value)
            ? storedBaseline.Value
            : currentFactor;
        return Math.Clamp(
            candidate,
            Math.Min(minimumFactor, maximumFactor),
            Math.Max(minimumFactor, maximumFactor));
    }

    private static SessionAwareRecommendationTuningState BuildState(
        double topPositionBoostFactor,
        double midPositionBoostFactor,
        double baselineTopPositionBoostFactor,
        double baselineMidPositionBoostFactor,
        DateTime computedAtUtc,
        string reason,
        IReadOnlyList<SessionAwareRecommendationTuningRun> runs,
        IReadOnlyList<SessionAwareRecommendationConfigVersion> versions,
        SessionAwareRecommendationTuningState? existingState)
    {
        return new SessionAwareRecommendationTuningState
        {
            SessionAwareRecommendation = new SessionAwareRecommendationWeightOverride
            {
                TopPositionBoostFactor = topPositionBoostFactor,
                MidPositionBoostFactor = midPositionBoostFactor,
                CtrWeight = existingState?.SessionAwareRecommendation.CtrWeight,
                AddToCartWeight = existingState?.SessionAwareRecommendation.AddToCartWeight,
                PurchaseWeight = existingState?.SessionAwareRecommendation.PurchaseWeight,
                RevenueWeight = existingState?.SessionAwareRecommendation.RevenueWeight,
                ObjectiveScoreScale = existingState?.SessionAwareRecommendation.ObjectiveScoreScale
            },
            SessionAwareRecommendationTuning = new SessionAwareRecommendationTuningMetadata
            {
                BaselineTopPositionBoostFactor = baselineTopPositionBoostFactor,
                BaselineMidPositionBoostFactor = baselineMidPositionBoostFactor,
                ComputedAtUtc = computedAtUtc,
                Reason = reason,
                Runs = runs.ToList(),
                Versions = versions.ToList()
            },
            RecommendationExperiment = existingState?.RecommendationExperiment ?? new RecommendationExperimentRolloutOverride(),
            MultiObjectiveRecommendationRollout = existingState?.MultiObjectiveRecommendationRollout
                ?? new MultiObjectiveRecommendationRolloutState()
        };
    }

    private sealed record SegmentCtrSummary(
        int EligiblePositionCount,
        double MlCtrPercentage,
        double SessionCtrPercentage);

    private sealed record SegmentAdjustmentDecision(
        string Segment,
        bool Changed,
        double OldFactor,
        double NewFactor,
        int EligiblePositionCount,
        double MlCtrPercentage,
        double SessionCtrPercentage,
        string Reason)
    {
        public static SegmentAdjustmentDecision NoChange(
            string segment,
            double currentFactor,
            string reason,
            int eligiblePositionCount = 0,
            double mlCtrPercentage = 0,
            double sessionCtrPercentage = 0)
        {
            return new SegmentAdjustmentDecision(
                segment,
                Changed: false,
                OldFactor: currentFactor,
                NewFactor: currentFactor,
                EligiblePositionCount: eligiblePositionCount,
                MlCtrPercentage: mlCtrPercentage,
                SessionCtrPercentage: sessionCtrPercentage,
                Reason: $"{segment}_{reason}");
        }
    }

    private sealed record RollbackDecision(
        double TopPositionBoostFactor,
        double MidPositionBoostFactor,
        string Reason,
        double PreviousSessionCtrPercentage,
        double CurrentSessionCtrPercentage);
}

public sealed class JsonSessionAwareRecommendationConfigStore : ISessionAwareRecommendationConfigStore
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
    {
        WriteIndented = true,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
    };

    private readonly IHostEnvironment _environment;
    private readonly IOptionsMonitor<SessionAwareRecommendationTuningOptions> _options;
    private readonly ILogger<JsonSessionAwareRecommendationConfigStore> _logger;

    public JsonSessionAwareRecommendationConfigStore(
        IHostEnvironment environment,
        IOptionsMonitor<SessionAwareRecommendationTuningOptions> options,
        ILogger<JsonSessionAwareRecommendationConfigStore> logger)
    {
        _environment = environment;
        _options = options;
        _logger = logger;
    }

    public async Task<SessionAwareRecommendationTuningState?> LoadStateAsync(
        CancellationToken cancellationToken = default)
    {
        var path = ResolveConfigPath(_options.CurrentValue.ConfigOverridePath);
        if (!File.Exists(path))
        {
            return null;
        }

        try
        {
            await using var stream = File.OpenRead(path);
            return await JsonSerializer.DeserializeAsync<SessionAwareRecommendationTuningState>(
                stream,
                JsonOptions,
                cancellationToken);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            _logger.LogWarning(ex, "Cannot read session-aware recommendation tuning config from {Path}. Starting with current options.", path);
            return null;
        }
    }

    public async Task PersistStateAsync(
        SessionAwareRecommendationTuningState state,
        CancellationToken cancellationToken = default)
    {
        var path = ResolveConfigPath(_options.CurrentValue.ConfigOverridePath);
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        var temporaryPath = $"{path}.{Guid.NewGuid():N}.tmp";
        await File.WriteAllTextAsync(
            temporaryPath,
            JsonSerializer.Serialize(state, JsonOptions),
            cancellationToken);
        File.Move(temporaryPath, path, overwrite: true);

        _logger.LogInformation(
            "Persisted session-aware recommendation tuning config to {Path}. TopFactor={TopFactor:0.####}, MidFactor={MidFactor:0.####}, BaselineTopFactor={BaselineTopFactor:0.####}, BaselineMidFactor={BaselineMidFactor:0.####}, Versions={VersionCount}",
            path,
            state.SessionAwareRecommendation.TopPositionBoostFactor,
            state.SessionAwareRecommendation.MidPositionBoostFactor,
            state.SessionAwareRecommendationTuning.BaselineTopPositionBoostFactor,
            state.SessionAwareRecommendationTuning.BaselineMidPositionBoostFactor,
            state.SessionAwareRecommendationTuning.Versions.Count);
    }

    private string ResolveConfigPath(string configuredPath)
    {
        var path = string.IsNullOrWhiteSpace(configuredPath)
            ? "App_Data/session-aware-rerank-tuning.json"
            : configuredPath.Trim();

        return Path.IsPathRooted(path)
            ? path
            : Path.Combine(_environment.ContentRootPath, path);
    }
}

public sealed class SessionAwareRecommendationTuningState
{
    public SessionAwareRecommendationWeightOverride SessionAwareRecommendation { get; set; } = new();

    public SessionAwareRecommendationTuningMetadata SessionAwareRecommendationTuning { get; set; } = new();

    public RecommendationExperimentRolloutOverride RecommendationExperiment { get; set; } = new();

    public MultiObjectiveRecommendationRolloutState MultiObjectiveRecommendationRollout { get; set; } = new();
}

public sealed class SessionAwareRecommendationWeightOverride
{
    public double TopPositionBoostFactor { get; set; }

    public double MidPositionBoostFactor { get; set; }

    public double? CtrWeight { get; set; }

    public double? AddToCartWeight { get; set; }

    public double? PurchaseWeight { get; set; }

    public double? RevenueWeight { get; set; }

    public double? ObjectiveScoreScale { get; set; }
}

public sealed class RecommendationExperimentRolloutOverride
{
    public int? SessionRerankTrafficPercent { get; set; }
}

public sealed class MultiObjectiveRecommendationRolloutState
{
    public int TrafficPercent { get; set; }

    public int StableWindowCount { get; set; }

    public DateTime StageStartedAtUtc { get; set; }

    public DateTime? StableSinceUtc { get; set; }

    public bool IsLocked { get; set; }

    public DateTime? LockedAtUtc { get; set; }

    public DateTime ComputedAtUtc { get; set; }

    public string Reason { get; set; } = string.Empty;

    public List<MultiObjectiveRecommendationMonitorRun> Runs { get; set; } = new();

    public List<MultiObjectiveRecommendationConfigVersion> Versions { get; set; } = new();
}

public sealed class MultiObjectiveRecommendationMonitorRun
{
    public DateTime ComputedAtUtc { get; set; }

    public long BaselineImpressions { get; set; }

    public long VariantImpressions { get; set; }

    public double? BaselineCtr { get; set; }

    public double? VariantCtr { get; set; }

    public double? BaselinePurchaseRate { get; set; }

    public double? VariantPurchaseRate { get; set; }

    public decimal? BaselineRevenuePerImpression { get; set; }

    public decimal? VariantRevenuePerImpression { get; set; }

    public string Decision { get; set; } = string.Empty;
}

public sealed class MultiObjectiveRecommendationConfigVersion
{
    public DateTime ComputedAtUtc { get; set; }

    public double CtrWeight { get; set; }

    public double AddToCartWeight { get; set; }

    public double PurchaseWeight { get; set; }

    public double RevenueWeight { get; set; }

    public int TrafficPercent { get; set; }

    public string Reason { get; set; } = string.Empty;
}

public sealed class SessionAwareRecommendationTuningMetadata
{
    public double BaselineTopPositionBoostFactor { get; set; }

    public double BaselineMidPositionBoostFactor { get; set; }

    public DateTime ComputedAtUtc { get; set; }

    public string Reason { get; set; } = string.Empty;

    public List<SessionAwareRecommendationTuningRun> Runs { get; set; } = new();

    public List<SessionAwareRecommendationConfigVersion> Versions { get; set; } = new();
}

public sealed class SessionAwareRecommendationTuningRun
{
    public DateTime ComputedAtUtc { get; set; }

    public double? TopMlCtrPercentage { get; set; }

    public double? TopSessionCtrPercentage { get; set; }

    public double? MidMlCtrPercentage { get; set; }

    public double? MidSessionCtrPercentage { get; set; }

    public double? OverallMlCtrPercentage { get; set; }

    public double? OverallSessionCtrPercentage { get; set; }

    public int EligibleTopPositionCount { get; set; }

    public int EligibleMidPositionCount { get; set; }
}

public sealed class SessionAwareRecommendationConfigVersion
{
    public DateTime ComputedAtUtc { get; set; }

    public double TopPositionBoostFactor { get; set; }

    public double MidPositionBoostFactor { get; set; }

    public string Reason { get; set; } = string.Empty;
}

public sealed record SessionAwareRecommendationWeightTuningResult(
    bool Changed,
    double TopPositionBoostFactorBefore,
    double TopPositionBoostFactorAfter,
    double MidPositionBoostFactorBefore,
    double MidPositionBoostFactorAfter,
    int EligibleTopPositionCount,
    int EligibleMidPositionCount,
    string Reason,
    IReadOnlyList<PositionCtrComparison> PositionComparisons)
{
    public static SessionAwareRecommendationWeightTuningResult NoChange(
        double topPositionBoostFactor,
        double midPositionBoostFactor,
        string reason)
    {
        return new SessionAwareRecommendationWeightTuningResult(
            Changed: false,
            TopPositionBoostFactorBefore: topPositionBoostFactor,
            TopPositionBoostFactorAfter: topPositionBoostFactor,
            MidPositionBoostFactorBefore: midPositionBoostFactor,
            MidPositionBoostFactorAfter: midPositionBoostFactor,
            EligibleTopPositionCount: 0,
            EligibleMidPositionCount: 0,
            Reason: reason,
            PositionComparisons: Array.Empty<PositionCtrComparison>());
    }
}

public sealed record PositionCtrComparison(
    int Position,
    long MlImpressions,
    long MlClicks,
    double MlCtrPercentage,
    long SessionImpressions,
    long SessionClicks,
    double SessionCtrPercentage);

public sealed class RecommendationCtrByPositionReportDto
{
    public DateTime FromDateUtc { get; set; }

    public DateTime ToDateUtc { get; set; }

    public int MinimumImpressions { get; set; }

    public List<RecommendationCtrByPositionMetricDto> Metrics { get; set; } = new();
}

public sealed class RecommendationCtrByPositionMetricDto
{
    public int Position { get; set; }

    public string ExperimentGroup { get; set; } = string.Empty;

    public string RecommendationSource { get; set; } = string.Empty;

    public long TotalImpressions { get; set; }

    public long TotalClicks { get; set; }

    public double? CtrPercentage { get; set; }

    public string Status { get; set; } = string.Empty;
}
