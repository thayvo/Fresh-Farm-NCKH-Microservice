// Nguon goc: src\Services\Ordering\FreshFarm.Ordering.Api\Services\RecommendationAffinityRefreshBackgroundService.cs
// Duoc sao chep tu: D:\NCKH\DOAN\NCKH-FRESH-FARM\src\Services\Ordering\FreshFarm.Ordering.Api\Services\RecommendationAffinityRefreshBackgroundService.cs
// Thu muc hoc tap: HocGoiY

namespace FreshFarm.Ordering.Api.Services;

public sealed class RecommendationAffinityRefreshBackgroundService : BackgroundService
{
    private static readonly TimeSpan Interval = TimeSpan.FromMinutes(20);

    private readonly IServiceScopeFactory _serviceScopeFactory;
    private readonly ILogger<RecommendationAffinityRefreshBackgroundService> _logger;
    private readonly RecommendationAffinityRefreshSignal _refreshSignal;

    public RecommendationAffinityRefreshBackgroundService(
        IServiceScopeFactory serviceScopeFactory,
        ILogger<RecommendationAffinityRefreshBackgroundService> logger,
        RecommendationAffinityRefreshSignal refreshSignal)
    {
        _serviceScopeFactory = serviceScopeFactory;
        _logger = logger;
        _refreshSignal = refreshSignal;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await _refreshSignal.WaitForRefreshSignalAsync(Interval, stoppingToken);
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
        }
    }
}

