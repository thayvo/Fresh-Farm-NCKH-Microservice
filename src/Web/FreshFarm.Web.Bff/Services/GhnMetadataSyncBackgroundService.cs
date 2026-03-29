using System.Net.Http.Json;
using FreshFarm.Web.Bff.Options;
using Microsoft.Extensions.Options;

namespace FreshFarm.Web.Bff.Services;

public sealed class GhnMetadataSyncBackgroundService : BackgroundService
{
    private readonly IServiceScopeFactory _serviceScopeFactory;
    private readonly IHttpClientFactory _httpClientFactory;
    private readonly IOptionsMonitor<OrderingServiceOptions> _orderingOptions;
    private readonly IOptionsMonitor<GhnBackgroundSyncOptions> _syncOptions;
    private readonly ILogger<GhnMetadataSyncBackgroundService> _logger;

    public GhnMetadataSyncBackgroundService(
        IServiceScopeFactory serviceScopeFactory,
        IHttpClientFactory httpClientFactory,
        IOptionsMonitor<OrderingServiceOptions> orderingOptions,
        IOptionsMonitor<GhnBackgroundSyncOptions> syncOptions,
        ILogger<GhnMetadataSyncBackgroundService> logger)
    {
        _serviceScopeFactory = serviceScopeFactory;
        _httpClientFactory = httpClientFactory;
        _orderingOptions = orderingOptions;
        _syncOptions = syncOptions;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var startupOptions = _syncOptions.CurrentValue;
        if (!startupOptions.Enabled)
        {
            _logger.LogInformation("Ghn background sync dang tat qua cau hinh.");
            return;
        }

        if (string.IsNullOrWhiteSpace(_orderingOptions.CurrentValue.InternalServiceKey))
        {
            _logger.LogWarning("Ghn background sync khong the chay vi thieu Services:Ordering:InternalServiceKey.");
            return;
        }

        var startupDelaySeconds = Math.Max(0, startupOptions.StartupDelaySeconds);
        if (startupDelaySeconds > 0)
        {
            await Task.Delay(TimeSpan.FromSeconds(startupDelaySeconds), stoppingToken);
        }

        await RunSyncCycleSafelyAsync(stoppingToken);

        using var timer = new PeriodicTimer(TimeSpan.FromMinutes(Math.Max(1, startupOptions.IntervalMinutes)));
        while (await timer.WaitForNextTickAsync(stoppingToken))
        {
            await RunSyncCycleSafelyAsync(stoppingToken);
        }
    }

    private async Task RunSyncCycleSafelyAsync(CancellationToken cancellationToken)
    {
        try
        {
            await RunSyncCycleAsync(cancellationToken);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Chu ky background sync GHN bi loi va se duoc bo qua de host tiep tuc chay.");
        }
    }

    private async Task RunSyncCycleAsync(CancellationToken cancellationToken)
    {
        var syncOptions = _syncOptions.CurrentValue;
        var orderingOptions = _orderingOptions.CurrentValue;

        if (!syncOptions.Enabled || string.IsNullOrWhiteSpace(orderingOptions.InternalServiceKey))
        {
            return;
        }

        using var scope = _serviceScopeFactory.CreateScope();
        var ghnSandboxService = scope.ServiceProvider.GetRequiredService<IGhnSandboxService>();
        if (!ghnSandboxService.IsConfigured)
        {
            _logger.LogDebug("Bo qua Ghn background sync vi GHN sandbox chua duoc cau hinh.");
            return;
        }

        var candidates = await FetchCandidatesAsync(orderingOptions.InternalServiceKey, syncOptions, cancellationToken);
        if (candidates.Count == 0)
        {
            _logger.LogDebug("Khong co candidate nao can background sync GHN.");
            return;
        }

        var synced = 0;
        var failed = 0;

        foreach (var candidate in candidates)
        {
            try
            {
                var tracking = await ghnSandboxService.GetOrderTrackingAsync(new GhnSandboxOrderTrackingRequest
                {
                    OrderCode = candidate.OrderCode,
                    ClientOrderCode = candidate.ClientOrderCode ?? string.Empty
                }, cancellationToken);

                if (!tracking.Success)
                {
                    failed += 1;
                    _logger.LogWarning(
                        "Background sync GHN khong lay duoc tracking. OrderId={OrderId}, OrderCode={OrderCode}, Message={Message}",
                        candidate.OrderId,
                        candidate.OrderCode,
                        tracking.Message);
                    continue;
                }

                var persisted = await PersistMetadataAsync(orderingOptions.InternalServiceKey, candidate.OrderId, new GhnMetadataUpsertRequest
                {
                    OrderCode = tracking.OrderCode,
                    ClientOrderCode = tracking.ClientOrderCode,
                    Status = tracking.Status,
                    StatusLabel = tracking.StatusLabel,
                    TotalFee = tracking.TotalFee,
                    CreatedAt = tracking.CreatedDate?.UtcDateTime,
                    ExpectedDeliveryTime = tracking.LeadTime?.UtcDateTime,
                    LastSyncedAt = DateTime.UtcNow
                }, cancellationToken);

                if (persisted)
                {
                    synced += 1;
                }
                else
                {
                    failed += 1;
                }
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                throw;
            }
            catch (Exception ex)
            {
                failed += 1;
                _logger.LogError(
                    ex,
                    "Background sync GHN gap loi khong mong doi. OrderId={OrderId}, OrderCode={OrderCode}",
                    candidate.OrderId,
                    candidate.OrderCode);
            }
        }

        _logger.LogInformation(
            "Hoan tat chu ky background sync GHN. Candidates={Total}, Synced={Synced}, Failed={Failed}",
            candidates.Count,
            synced,
            failed);
    }

    private async Task<List<GhnSyncCandidate>> FetchCandidatesAsync(
        string internalServiceKey,
        GhnBackgroundSyncOptions syncOptions,
        CancellationToken cancellationToken)
    {
        var client = _httpClientFactory.CreateClient("Ordering");
        using var request = new HttpRequestMessage(
            HttpMethod.Get,
            $"/api/orders/admin/shippings/internal/ghn-sync-candidates?limit={Math.Clamp(syncOptions.BatchSize, 1, 50)}&staleMinutes={Math.Clamp(syncOptions.StaleMinutes, 1, 24 * 60)}");
        request.Headers.Add("X-Internal-Service-Key", internalServiceKey);

        try
        {
            using var response = await client.SendAsync(request, cancellationToken);
            if (!response.IsSuccessStatusCode)
            {
                _logger.LogWarning("Khong lay duoc danh sach candidate GHN background sync. HTTP {StatusCode}", (int)response.StatusCode);
                return new List<GhnSyncCandidate>();
            }

            var payload = await response.Content.ReadFromJsonAsync<GhnSyncCandidateEnvelope>(cancellationToken: cancellationToken);
            return payload?.Items ?? new List<GhnSyncCandidate>();
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Khong lay duoc danh sach candidate GHN background sync do loi ket noi/du lieu.");
            return new List<GhnSyncCandidate>();
        }
    }

    private async Task<bool> PersistMetadataAsync(
        string internalServiceKey,
        int orderId,
        GhnMetadataUpsertRequest request,
        CancellationToken cancellationToken)
    {
        var client = _httpClientFactory.CreateClient("Ordering");
        using var message = new HttpRequestMessage(HttpMethod.Post, $"/api/orders/admin/shippings/internal/{orderId}/ghn-metadata")
        {
            Content = JsonContent.Create(request)
        };
        message.Headers.Add("X-Internal-Service-Key", internalServiceKey);

        try
        {
            using var response = await client.SendAsync(message, cancellationToken);
            if (response.IsSuccessStatusCode)
            {
                return true;
            }

            _logger.LogWarning(
                "Khong persist duoc metadata GHN tu background sync. OrderId={OrderId}, HTTP {StatusCode}",
                orderId,
                (int)response.StatusCode);
            return false;
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(
                ex,
                "Khong persist duoc metadata GHN tu background sync do loi ket noi. OrderId={OrderId}",
                orderId);
            return false;
        }
    }

    private sealed class GhnSyncCandidateEnvelope
    {
        public bool Success { get; set; }

        public List<GhnSyncCandidate> Items { get; set; } = new();
    }

    private sealed class GhnSyncCandidate
    {
        public int OrderId { get; set; }

        public string OrderCode { get; set; } = string.Empty;

        public string? ClientOrderCode { get; set; }
    }

    private sealed class GhnMetadataUpsertRequest
    {
        public string? OrderCode { get; set; }

        public string? ClientOrderCode { get; set; }

        public string? Status { get; set; }

        public string? StatusLabel { get; set; }

        public decimal? TotalFee { get; set; }

        public DateTime? CreatedAt { get; set; }

        public DateTime? ExpectedDeliveryTime { get; set; }

        public DateTime? LastSyncedAt { get; set; }
    }
}
