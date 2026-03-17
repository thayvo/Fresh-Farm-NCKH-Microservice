using System.Net.Http.Json;
using FreshFarm.Ordering.Api.Options;
using Microsoft.Extensions.Options;

namespace FreshFarm.Ordering.Api.Services;

public sealed class CatalogInventoryClient
{
    private const string ServiceKeyHeaderName = "X-Service-Key";

    private readonly IHttpClientFactory _httpClientFactory;
    private readonly CatalogServiceOptions _options;

    public CatalogInventoryClient(
        IHttpClientFactory httpClientFactory,
        IOptions<CatalogServiceOptions> options)
    {
        _httpClientFactory = httpClientFactory;
        _options = options.Value;
    }

    public Task ReserveAsync(IEnumerable<CatalogInventoryMutationItem> items, CancellationToken cancellationToken)
        => SendAsync("/internal/inventory/reservations/reserve", items, cancellationToken);

    public Task ReleaseAsync(IEnumerable<CatalogInventoryMutationItem> items, CancellationToken cancellationToken)
        => SendAsync("/internal/inventory/reservations/release", items, cancellationToken);

    public Task CommitReservedAsync(IEnumerable<CatalogInventoryMutationItem> items, CancellationToken cancellationToken)
        => SendAsync("/internal/inventory/reservations/commit", items, cancellationToken);

    public Task ConsumeOnHandAsync(IEnumerable<CatalogInventoryMutationItem> items, CancellationToken cancellationToken)
        => SendAsync("/internal/inventory/reservations/consume", items, cancellationToken);

    public Task RestockOnHandAsync(IEnumerable<CatalogInventoryMutationItem> items, CancellationToken cancellationToken)
        => SendAsync("/internal/inventory/reservations/restock", items, cancellationToken);

    public async Task<List<CatalogInventorySnapshotItem>> GetInventorySnapshotsAsync(bool reservedOnly, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(_options.InternalServiceKey))
        {
            throw new InvalidOperationException("Catalog internal service key chua duoc cau hinh.");
        }

        var client = _httpClientFactory.CreateClient("Catalog");
        client.DefaultRequestHeaders.Remove(ServiceKeyHeaderName);
        client.DefaultRequestHeaders.Add(ServiceKeyHeaderName, _options.InternalServiceKey);

        var response = await client.GetAsync($"/internal/inventory/reservations/snapshots?reservedOnly={reservedOnly.ToString().ToLowerInvariant()}", cancellationToken);
        if (!response.IsSuccessStatusCode)
        {
            var error = await response.Content.ReadAsStringAsync(cancellationToken);
            throw new InvalidOperationException(string.IsNullOrWhiteSpace(error)
                ? "Catalog inventory snapshot request that bai."
                : error);
        }

        var payload = await response.Content.ReadFromJsonAsync<CatalogInventorySnapshotResponse>(cancellationToken: cancellationToken);
        return payload?.Items ?? new List<CatalogInventorySnapshotItem>();
    }

    public async Task ReconcileReservedAsync(IEnumerable<CatalogInventoryReconcileItem> items, CancellationToken cancellationToken)
    {
        var normalizedItems = items
            .Where(x => x.ProductId > 0)
            .ToList();

        if (normalizedItems.Count == 0)
        {
            return;
        }

        if (string.IsNullOrWhiteSpace(_options.InternalServiceKey))
        {
            throw new InvalidOperationException("Catalog internal service key chua duoc cau hinh.");
        }

        var client = _httpClientFactory.CreateClient("Catalog");
        client.DefaultRequestHeaders.Remove(ServiceKeyHeaderName);
        client.DefaultRequestHeaders.Add(ServiceKeyHeaderName, _options.InternalServiceKey);

        var response = await client.PostAsJsonAsync("/internal/inventory/reservations/reconcile", new CatalogInventoryReconcileRequest
        {
            Items = normalizedItems
        }, cancellationToken);

        if (response.IsSuccessStatusCode)
        {
            return;
        }

        var error = await response.Content.ReadAsStringAsync(cancellationToken);
        throw new InvalidOperationException(string.IsNullOrWhiteSpace(error)
            ? "Catalog inventory reconcile request that bai."
            : error);
    }

    private async Task SendAsync(string path, IEnumerable<CatalogInventoryMutationItem> items, CancellationToken cancellationToken)
    {
        var normalizedItems = items
            .Where(x => x.ProductId > 0 && x.Quantity > 0)
            .ToList();

        if (normalizedItems.Count == 0)
        {
            return;
        }

        if (string.IsNullOrWhiteSpace(_options.InternalServiceKey))
        {
            throw new InvalidOperationException("Catalog internal service key chua duoc cau hinh.");
        }

        var client = _httpClientFactory.CreateClient("Catalog");
        client.DefaultRequestHeaders.Remove(ServiceKeyHeaderName);
        client.DefaultRequestHeaders.Add(ServiceKeyHeaderName, _options.InternalServiceKey);

        var response = await client.PostAsJsonAsync(path, new CatalogInventoryMutationRequest
        {
            Items = normalizedItems
        }, cancellationToken);

        if (response.IsSuccessStatusCode)
        {
            return;
        }

        var error = await response.Content.ReadAsStringAsync(cancellationToken);
        throw new InvalidOperationException(string.IsNullOrWhiteSpace(error)
            ? "Catalog inventory request that bai."
            : error);
    }
}

public sealed class CatalogInventoryMutationItem
{
    public int ProductId { get; init; }

    public int SellerId { get; init; }

    public int Quantity { get; init; }
}

internal sealed class CatalogInventoryMutationRequest
{
    public List<CatalogInventoryMutationItem> Items { get; init; } = new();
}

public sealed class CatalogInventoryReconcileItem
{
    public int ProductId { get; init; }

    public int ExpectedReservedStock { get; init; }
}

internal sealed class CatalogInventoryReconcileRequest
{
    public List<CatalogInventoryReconcileItem> Items { get; init; } = new();
}

internal sealed class CatalogInventorySnapshotResponse
{
    public List<CatalogInventorySnapshotItem>? Items { get; init; }
}

public sealed class CatalogInventorySnapshotItem
{
    public int ProductId { get; init; }

    public int OnHandStock { get; init; }

    public int ReservedStock { get; init; }

    public int AvailableStock { get; init; }
}
