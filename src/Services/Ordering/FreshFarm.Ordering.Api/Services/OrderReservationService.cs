using FreshFarm.Ordering.Api.Models;
using Microsoft.EntityFrameworkCore;

namespace FreshFarm.Ordering.Api.Services;

public sealed class OrderReservationService
{
    public const int PaymentHoldMinutes = 30;

    private readonly FreshFarmOrderingDBContext _db;
    private readonly CatalogInventoryClient _catalogInventoryClient;

    public OrderReservationService(
        FreshFarmOrderingDBContext db,
        CatalogInventoryClient catalogInventoryClient)
    {
        _db = db;
        _catalogInventoryClient = catalogInventoryClient;
    }

    public DateTime GetReservationExpiryUtc()
        => DateTime.UtcNow.AddMinutes(PaymentHoldMinutes);

    public async Task ExpireStaleReservationsAsync(CancellationToken cancellationToken)
    {
        var now = DateTime.UtcNow;
        var expiredOrders = await _db.Orders
            .Include(o => o.InventoryReservations)
            .Include(o => o.Payments)
            .Include(o => o.SellerOrders)
            .Where(o => o.Status == "AwaitingPayment" &&
                        o.InventoryReservations.Any(r => r.Status == "Reserved" && r.ExpiresAt <= now))
            .ToListAsync(cancellationToken);

        foreach (var order in expiredOrders)
        {
            await ReleaseReservedReservationsAsync(order, "Expired", "Expired", cancellationToken);
        }
    }

    public async Task MarkReservationsCommittedAsync(Order order, CancellationToken cancellationToken)
    {
        var reservedReservations = order.InventoryReservations
            .Where(x => string.Equals(x.Status, "Reserved", StringComparison.OrdinalIgnoreCase))
            .ToList();

        if (reservedReservations.Count > 0)
        {
            var commitItems = reservedReservations
                .GroupBy(x => x.InventoryId)
                .Select(g => new CatalogInventoryMutationItem
                {
                    ProductId = g.Key,
                    Quantity = g.Sum(x => x.Quantity)
                })
                .ToList();

            await _catalogInventoryClient.CommitReservedAsync(commitItems, cancellationToken);
        }

        var now = DateTime.UtcNow;
        foreach (var reservation in reservedReservations)
        {
            reservation.Status = "Committed";
            reservation.ExpiresAt = now;
        }

        if (string.Equals(order.Status, "AwaitingPayment", StringComparison.OrdinalIgnoreCase))
        {
            order.Status = "Pending";
        }

        foreach (var sellerOrder in order.SellerOrders
                     .Where(x => string.Equals(x.SellerStatus, "AwaitingPayment", StringComparison.OrdinalIgnoreCase)))
        {
            sellerOrder.SellerStatus = "Pending";
            sellerOrder.UpdatedAt = now;
        }

        await _db.SaveChangesAsync(cancellationToken);
    }

    public async Task ReleaseReservationsAsync(
        Order order,
        string orderStatus,
        string paymentStatus,
        CancellationToken cancellationToken)
    {
        var releasableReservations = order.InventoryReservations
            .Where(x => !string.Equals(x.Status, "Released", StringComparison.OrdinalIgnoreCase))
            .ToList();

        var reservedReservations = releasableReservations
            .Where(x => string.Equals(x.Status, "Reserved", StringComparison.OrdinalIgnoreCase))
            .ToList();

        if (reservedReservations.Count > 0)
        {
            var releaseItems = reservedReservations
                .GroupBy(x => x.InventoryId)
                .Select(g => new CatalogInventoryMutationItem
                {
                    ProductId = g.Key,
                    Quantity = g.Sum(x => x.Quantity)
                })
                .ToList();

            await _catalogInventoryClient.ReleaseAsync(releaseItems, cancellationToken);
        }

        var committedReservations = releasableReservations
            .Where(x => string.Equals(x.Status, "Committed", StringComparison.OrdinalIgnoreCase))
            .ToList();

        if (committedReservations.Count > 0)
        {
            var restockItems = committedReservations
                .GroupBy(x => x.InventoryId)
                .Select(g => new CatalogInventoryMutationItem
                {
                    ProductId = g.Key,
                    Quantity = g.Sum(x => x.Quantity)
                })
                .ToList();

            await _catalogInventoryClient.RestockOnHandAsync(restockItems, cancellationToken);
        }

        var now = DateTime.UtcNow;
        foreach (var reservation in releasableReservations)
        {
            reservation.Status = "Released";
            reservation.ExpiresAt = now;
        }

        order.Status = orderStatus;
        if (!string.Equals(order.PaymentStatus, "Paid", StringComparison.OrdinalIgnoreCase))
        {
            order.PaymentStatus = paymentStatus;
        }

        foreach (var payment in order.Payments)
        {
            if (!string.Equals(payment.PaymentStatus, "Paid", StringComparison.OrdinalIgnoreCase))
            {
                payment.PaymentStatus = paymentStatus;
                payment.PaymentDate = null;
            }
        }

        var sellerStatus = string.Equals(orderStatus, "Expired", StringComparison.OrdinalIgnoreCase)
            ? "Canceled"
            : orderStatus;

        foreach (var sellerOrder in order.SellerOrders)
        {
            sellerOrder.SellerStatus = sellerStatus;
            sellerOrder.UpdatedAt = DateTime.UtcNow;
        }

        await _db.SaveChangesAsync(cancellationToken);
    }

    public async Task ReleaseReservedReservationsAsync(
        Order order,
        string orderStatus,
        string paymentStatus,
        CancellationToken cancellationToken)
    {
        var reservedReservations = order.InventoryReservations
            .Where(x => string.Equals(x.Status, "Reserved", StringComparison.OrdinalIgnoreCase))
            .ToList();

        if (reservedReservations.Count > 0)
        {
            var releaseItems = reservedReservations
                .GroupBy(x => x.InventoryId)
                .Select(g => new CatalogInventoryMutationItem
                {
                    ProductId = g.Key,
                    Quantity = g.Sum(x => x.Quantity)
                })
                .ToList();

            await _catalogInventoryClient.ReleaseAsync(releaseItems, cancellationToken);
        }

        var now = DateTime.UtcNow;
        foreach (var reservation in reservedReservations)
        {
            reservation.Status = "Released";
            reservation.ExpiresAt = now;
        }

        order.Status = orderStatus;
        if (!string.Equals(order.PaymentStatus, "Paid", StringComparison.OrdinalIgnoreCase))
        {
            order.PaymentStatus = paymentStatus;
        }

        foreach (var payment in order.Payments)
        {
            if (!string.Equals(payment.PaymentStatus, "Paid", StringComparison.OrdinalIgnoreCase))
            {
                payment.PaymentStatus = paymentStatus;
                payment.PaymentDate = null;
            }
        }

        var sellerStatus = string.Equals(orderStatus, "Expired", StringComparison.OrdinalIgnoreCase)
            ? "Canceled"
            : orderStatus;

        foreach (var sellerOrder in order.SellerOrders)
        {
            if (string.Equals(sellerOrder.SellerStatus, "AwaitingPayment", StringComparison.OrdinalIgnoreCase) ||
                string.Equals(sellerOrder.SellerStatus, "Pending", StringComparison.OrdinalIgnoreCase) ||
                string.Equals(sellerOrder.SellerStatus, "Canceled", StringComparison.OrdinalIgnoreCase))
            {
                sellerOrder.SellerStatus = sellerStatus;
                sellerOrder.UpdatedAt = now;
            }
        }

        await _db.SaveChangesAsync(cancellationToken);
    }
}
