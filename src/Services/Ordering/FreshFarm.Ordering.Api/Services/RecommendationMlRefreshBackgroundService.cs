using FreshFarm.Ordering.Api.Options;
using Microsoft.Extensions.Options;

namespace FreshFarm.Ordering.Api.Services;

public sealed class RecommendationMlRefreshBackgroundService : BackgroundService
{
    private static readonly TimeSpan MinimumInterval = TimeSpan.FromMinutes(15);

    private readonly IServiceScopeFactory _serviceScopeFactory;
    private readonly IOptionsMonitor<RecommendationMlOptions> _options;
    private readonly ILogger<RecommendationMlRefreshBackgroundService> _logger;

    public RecommendationMlRefreshBackgroundService(
        IServiceScopeFactory serviceScopeFactory,
        IOptionsMonitor<RecommendationMlOptions> options,
        ILogger<RecommendationMlRefreshBackgroundService> logger)
    {
        _serviceScopeFactory = serviceScopeFactory;
        _options = options;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            var currentOptions = _options.CurrentValue;
            var interval = TimeSpan.FromMinutes(Math.Max(1, currentOptions.TrainingIntervalMinutes));
            if (interval < MinimumInterval)
            {
                interval = MinimumInterval;
            }

            try
            {
                await Task.Delay(interval, stoppingToken);
                if (!_options.CurrentValue.Enabled)
                {
                    continue;
                }

                using var scope = _serviceScopeFactory.CreateScope();
                var trainingService = scope.ServiceProvider.GetRequiredService<RecommendationMlTrainingService>();
                await trainingService.RebuildUserProductScoresAsync(
                    force: false,
                    materializeUserProductScoresOverride: null,
                    stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                return;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Khong the refresh ML.NET recommendation model.");
            }
        }
    }
}
