using FreshFarm.Ordering.Api.Models;
using Microsoft.EntityFrameworkCore;

namespace FreshFarm.Ordering.Api.Services;

public sealed class InventoryReconciliationService
{
    private readonly FreshFarmOrderingDBContext _db;
    private readonly OrderReservationService _orderReservationService;
    private readonly CatalogInventoryClient _catalogInventoryClient;
    private readonly ILogger<InventoryReconciliationService> _logger;

    public InventoryReconciliationService(
        FreshFarmOrderingDBContext db,
        OrderReservationService orderReservationService,
        CatalogInventoryClient catalogInventoryClient,
        ILogger<InventoryReconciliationService> logger)
    {
        _db = db;
        _orderReservationService = orderReservationService;
        _catalogInventoryClient = catalogInventoryClient;
        _logger = logger;
    }

    public async Task RunAsync(CancellationToken cancellationToken)
    {
        await _orderReservationService.ExpireStaleReservationsAsync(cancellationToken);
        await ReconcileOrderReservationStatesAsync(cancellationToken);
        await ReconcileCatalogReservedStockSafelyAsync(cancellationToken);
    }

    private async Task ReconcileCatalogReservedStockSafelyAsync(CancellationToken cancellationToken)
    {
        try
        {
            await ReconcileCatalogReservedStockAsync(cancellationToken);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (HttpRequestException ex)
        {
            _logger.LogWarning(
                "Bo qua doi soat reserved stock voi Catalog trong chu ky nay vi Catalog chua san sang: {Message}",
                ex.Message);
        }
        catch (InvalidOperationException ex)
        {
            _logger.LogWarning(
                "Bo qua doi soat reserved stock voi Catalog trong chu ky nay vi Catalog tra ve loi: {Message}",
                ex.Message);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Bo qua doi soat reserved stock voi Catalog trong chu ky nay.");
        }
    }

    private async Task ReconcileOrderReservationStatesAsync(CancellationToken cancellationToken)
    {
        var paidOrders = await _db.Orders
            .Include(o => o.InventoryReservations)
            .Include(o => o.Payments)
            .Include(o => o.SellerOrders)
            .Where(o => o.PaymentStatus == "Paid" &&
                        o.InventoryReservations.Any(r => r.Status == "Reserved"))
            .ToListAsync(cancellationToken);

        foreach (var order in paidOrders)
        {
            await _orderReservationService.MarkReservationsCommittedAsync(order, cancellationToken);
            _logger.LogInformation("Reconciled paid order {OrderId}: committed dangling reserved inventory.", order.OrderId);
        }

        var releasedOrders = await _db.Orders
            .Include(o => o.InventoryReservations)
            .Include(o => o.Payments)
            .Include(o => o.SellerOrders)
            .Where(o =>
                o.InventoryReservations.Any(r => r.Status == "Reserved") &&
                (o.Status == "Canceled" ||
                 o.Status == "Expired" ||
                 o.PaymentStatus == "Failed" ||
                 o.PaymentStatus == "Expired"))
            .ToListAsync(cancellationToken);

        foreach (var order in releasedOrders)
        {
            var paymentStatus = string.Equals(order.PaymentStatus, "Paid", StringComparison.OrdinalIgnoreCase)
                ? "Paid"
                : (order.PaymentStatus ?? "Failed");
            var orderStatus = string.IsNullOrWhiteSpace(order.Status) ? "Canceled" : order.Status;
            await _orderReservationService.ReleaseReservedReservationsAsync(order, orderStatus, paymentStatus, cancellationToken);
            _logger.LogInformation("Reconciled unpaid order {OrderId}: released dangling reserved inventory.", order.OrderId);
        }

        var orphanAwaitingPaymentOrders = await _db.Orders
            .Include(o => o.InventoryReservations)
            .Include(o => o.Payments)
            .Include(o => o.SellerOrders)
            .Where(o => o.Status == "AwaitingPayment" && !o.InventoryReservations.Any())
            .ToListAsync(cancellationToken);

        if (orphanAwaitingPaymentOrders.Count == 0)
        {
            return;
        }

        foreach (var order in orphanAwaitingPaymentOrders)
        {
            order.Status = "Expired";
            if (!string.Equals(order.PaymentStatus, "Paid", StringComparison.OrdinalIgnoreCase))
            {
                order.PaymentStatus = "Expired";
            }

            foreach (var payment in order.Payments)
            {
                if (!string.Equals(payment.PaymentStatus, "Paid", StringComparison.OrdinalIgnoreCase))
                {
                    payment.PaymentStatus = "Expired";
                    payment.PaymentDate = null;
                }
            }

            foreach (var sellerOrder in order.SellerOrders)
            {
                sellerOrder.SellerStatus = "Canceled";
                sellerOrder.UpdatedAt = DateTime.UtcNow;
            }

            _logger.LogWarning("Reconciled orphan AwaitingPayment order {OrderId}: expired because no reservation rows existed.", order.OrderId);
        }

        await _db.SaveChangesAsync(cancellationToken);
    }

    private async Task ReconcileCatalogReservedStockAsync(CancellationToken cancellationToken)
    {
        var expectedReserved = await _db.InventoryReservations
            .AsNoTracking()
            .Where(x => x.Status == "Reserved")
            .GroupBy(x => x.InventoryId)
            .Select(g => new
            {
                ProductId = g.Key,
                ExpectedReservedStock = g.Sum(x => x.Quantity)
            })
            .ToListAsync(cancellationToken);

        var expectedMap = expectedReserved.ToDictionary(x => x.ProductId, x => x.ExpectedReservedStock);
        var remoteSnapshots = await _catalogInventoryClient.GetInventorySnapshotsAsync(reservedOnly: true, cancellationToken);
        var remoteMap = remoteSnapshots.ToDictionary(x => x.ProductId, x => x.ReservedStock);

        var productIds = expectedMap.Keys
            .Union(remoteMap.Keys)
            .Distinct()
            .ToList();

        if (productIds.Count == 0)
        {
            return;
        }

        var reconcileItems = productIds
            .Select(productId => new CatalogInventoryReconcileItem
            {
                ProductId = productId,
                ExpectedReservedStock = expectedMap.GetValueOrDefault(productId)
            })
            .ToList();

        var changedCount = reconcileItems.Count(item => remoteMap.GetValueOrDefault(item.ProductId) != item.ExpectedReservedStock);
        if (changedCount == 0)
        {
            return;
        }

        await _catalogInventoryClient.ReconcileReservedAsync(reconcileItems, cancellationToken);
        _logger.LogInformation("Reconciled Catalog reserved stock for {ChangedCount} product(s).", changedCount);
    }
}
