using FreshFarm.Web.Bff.Options;
using Microsoft.Extensions.Options;

namespace FreshFarm.Web.Bff.Services;

public sealed class MultiObjectiveRecommendationRolloutBackgroundService : BackgroundService
{
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly IOptionsMonitor<MultiObjectiveRecommendationRolloutOptions> _options;
    private readonly ILogger<MultiObjectiveRecommendationRolloutBackgroundService> _logger;

    public MultiObjectiveRecommendationRolloutBackgroundService(
        IServiceScopeFactory scopeFactory,
        IOptionsMonitor<MultiObjectiveRecommendationRolloutOptions> options,
        ILogger<MultiObjectiveRecommendationRolloutBackgroundService> logger)
    {
        _scopeFactory = scopeFactory;
        _options = options;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            var options = _options.CurrentValue;
            if (!options.Enabled)
            {
                await DelayAsync(TimeSpan.FromMinutes(5), stoppingToken);
                continue;
            }

            await DelayAsync(
                TimeSpan.FromSeconds(Math.Clamp(options.StartupDelaySeconds, 0, 3600)),
                stoppingToken);
            break;
        }

        while (!stoppingToken.IsCancellationRequested)
        {
            var options = _options.CurrentValue;
            if (options.Enabled)
            {
                await RunOnceAsync(stoppingToken);
            }

            await DelayAsync(
                TimeSpan.FromMinutes(Math.Clamp(options.IntervalMinutes, 1, 24 * 60)),
                stoppingToken);
        }
    }

    private async Task RunOnceAsync(CancellationToken cancellationToken)
    {
        try
        {
            using var scope = _scopeFactory.CreateScope();
            var service = scope.ServiceProvider.GetRequiredService<IMultiObjectiveRecommendationRolloutService>();
            var result = await service.MonitorAsync(cancellationToken);
            _logger.LogInformation(
                "Multi-objective recommendation rollout cycle completed. Stage={RolloutStage}, Locked={IsLocked}, GuardrailsEvaluated={GuardrailsEvaluated}, Changed={Changed}, Decision={Decision}, Traffic={TrafficBefore}->{TrafficAfter}, Weights={WeightsBefore}->{WeightsAfter}",
                result.RolloutStage,
                result.IsLocked,
                result.GuardrailsEvaluated,
                result.Changed,
                result.Decision,
                result.TrafficPercentBefore,
                result.TrafficPercentAfter,
                result.WeightsBefore,
                result.WeightsAfter);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Multi-objective recommendation rollout cycle failed.");
        }
    }

    private static async Task DelayAsync(TimeSpan delay, CancellationToken cancellationToken)
    {
        if (delay <= TimeSpan.Zero)
        {
            return;
        }

        await Task.Delay(delay, cancellationToken);
    }
}
