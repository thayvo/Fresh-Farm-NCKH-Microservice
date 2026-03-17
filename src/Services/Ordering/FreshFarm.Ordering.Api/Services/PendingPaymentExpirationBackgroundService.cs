namespace FreshFarm.Ordering.Api.Services;

public sealed class PendingPaymentExpirationBackgroundService : BackgroundService
{
    private static readonly TimeSpan Interval = TimeSpan.FromMinutes(1);

    private readonly IServiceScopeFactory _serviceScopeFactory;
    private readonly ILogger<PendingPaymentExpirationBackgroundService> _logger;

    public PendingPaymentExpirationBackgroundService(
        IServiceScopeFactory serviceScopeFactory,
        ILogger<PendingPaymentExpirationBackgroundService> logger)
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
                var reconciliationService = scope.ServiceProvider.GetRequiredService<InventoryReconciliationService>();
                await reconciliationService.RunAsync(stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                return;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Khong the chay inventory reconciliation job.");
            }

            await Task.Delay(Interval, stoppingToken);
        }
    }
}
