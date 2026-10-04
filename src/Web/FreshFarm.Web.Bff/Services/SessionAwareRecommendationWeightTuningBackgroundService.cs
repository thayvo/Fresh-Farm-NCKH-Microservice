using FreshFarm.Web.Bff.Options;
using Microsoft.Extensions.Options;

namespace FreshFarm.Web.Bff.Services;

public sealed class SessionAwareRecommendationWeightTuningBackgroundService : BackgroundService
{
    private readonly IOptionsMonitor<SessionAwareRecommendationTuningOptions> _options;
    private readonly ISessionAwareRecommendationWeightTuningService _tuningService;
    private readonly ILogger<SessionAwareRecommendationWeightTuningBackgroundService> _logger;

    public SessionAwareRecommendationWeightTuningBackgroundService(
        IOptionsMonitor<SessionAwareRecommendationTuningOptions> options,
        ISessionAwareRecommendationWeightTuningService tuningService,
        ILogger<SessionAwareRecommendationWeightTuningBackgroundService> logger)
    {
        _options = options;
        _tuningService = tuningService;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var startupOptions = _options.CurrentValue;
        if (!startupOptions.Enabled)
        {
            _logger.LogInformation("Session-aware rerank weight tuning background job is disabled.");
            return;
        }

        var startupDelay = TimeSpan.FromSeconds(Math.Max(0, startupOptions.StartupDelaySeconds));
        if (startupDelay > TimeSpan.Zero)
        {
            await Task.Delay(startupDelay, stoppingToken);
        }

        await RunSafelyAsync(stoppingToken);

        using var timer = new PeriodicTimer(TimeSpan.FromHours(Math.Max(1, _options.CurrentValue.IntervalHours)));
        while (await timer.WaitForNextTickAsync(stoppingToken))
        {
            if (!_options.CurrentValue.Enabled)
            {
                _logger.LogDebug("Session-aware rerank weight tuning cycle skipped because job is disabled.");
                continue;
            }

            await RunSafelyAsync(stoppingToken);
        }
    }

    private async Task RunSafelyAsync(CancellationToken cancellationToken)
    {
        try
        {
            var result = await _tuningService.TuneAsync(cancellationToken);
            _logger.LogInformation(
                "Session-aware rerank weight tuning cycle finished. Changed={Changed}, TopFactor={TopBefore:0.####}->{TopAfter:0.####}, MidFactor={MidBefore:0.####}->{MidAfter:0.####}, EligibleTopPositions={EligibleTopPositions}, EligibleMidPositions={EligibleMidPositions}, Reason={Reason}",
                result.Changed,
                result.TopPositionBoostFactorBefore,
                result.TopPositionBoostFactorAfter,
                result.MidPositionBoostFactorBefore,
                result.MidPositionBoostFactorAfter,
                result.EligibleTopPositionCount,
                result.EligibleMidPositionCount,
                result.Reason);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Session-aware rerank weight tuning cycle failed and will be retried on the next schedule.");
        }
    }
}
