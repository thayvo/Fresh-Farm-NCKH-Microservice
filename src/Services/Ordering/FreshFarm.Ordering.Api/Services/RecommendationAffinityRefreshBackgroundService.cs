namespace FreshFarm.Ordering.Api.Services;

public sealed class RecommendationAffinityRefreshBackgroundService : BackgroundService
{
    private static readonly TimeSpan Interval = TimeSpan.FromMinutes(20);

    private readonly IServiceScopeFactory _serviceScopeFactory;
    private readonly ILogger<RecommendationAffinityRefreshBackgroundService> _logger;

    public RecommendationAffinityRefreshBackgroundService(
        IServiceScopeFactory serviceScopeFactory,
        ILogger<RecommendationAffinityRefreshBackgroundService> logger)
    {
        _serviceScopeFactory = serviceScopeFactory;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                using var scope = _serviceScopeFactory.CreateScope();
                var affinityService = scope.ServiceProvider.GetRequiredService<RecommendationAffinityService>();
                await affinityService.RebuildAsync(stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                return;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Khong the refresh recommendation product affinity.");
            }

            await Task.Delay(Interval, stoppingToken);
        }
    }
}
