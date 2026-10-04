using FreshFarm.Ordering.Api.Models;
using Microsoft.EntityFrameworkCore;

namespace FreshFarm.Ordering.Api.Services;

public sealed record PayoutGenerationResult(
    int CreatedPayoutCount,
    int CreatedPayoutItemCount,
    decimal AmountGross,
    decimal FeeAmount,
    decimal AmountNet);

public sealed class PayoutGenerationService
{
    private static readonly string[] CompletedOrderStatuses = ["delivered", "completed", "complete", "finished"];
    private static readonly string[] BlockedOrderStatuses = ["cancelled", "canceled", "expired", "refunded", "returned"];
    private static readonly string[] PaidPaymentStatuses = ["paid", "captured", "succeeded", "success", "settled"];
    private static readonly string[] FailedStatuses = ["failed", "rejected", "cancelled", "canceled", "error"];
    private static readonly string[] IgnoredReturnStatuses = ["rejected", "cancelled", "canceled", "denied"];

    private readonly FreshFarmOrderingDBContext _db;

    public PayoutGenerationService(FreshFarmOrderingDBContext db)
    {
        _db = db;
    }

    public async Task<PayoutGenerationResult> GeneratePendingPayoutsAsync(int? sellerId = null, CancellationToken cancellationToken = default)
    {
        var scopedSellerId = sellerId > 0 ? sellerId : null;

        await using var transaction = await _db.Database.BeginTransactionAsync(cancellationToken);

        var candidateQuery = _db.SellerOrders
            .Include(x => x.Order)
                .ThenInclude(x => x.Payments)
            .Include(x => x.Order)
                .ThenInclude(x => x.PaymentTransactions)
            .Include(x => x.PayoutItems)
            .Where(x => !x.PayoutItems.Any());

        if (scopedSellerId.HasValue)
        {
            candidateQuery = candidateQuery.Where(x => x.SellerId == scopedSellerId.Value);
        }

        var candidates = await candidateQuery.ToListAsync(cancellationToken);
        if (candidates.Count == 0)
        {
            await transaction.CommitAsync(cancellationToken);
            return new PayoutGenerationResult(0, 0, 0m, 0m, 0m);
        }

        var sellerOrderIds = candidates.Select(x => x.SellerOrderId).ToArray();
        var orderIds = candidates.Select(x => x.OrderId).Distinct().ToArray();

        var refundedOrderIds = await _db.RefundTransactions
            .AsNoTracking()
            .Where(x =>
                orderIds.Contains(x.PaymentTxn.OrderId) &&
                !FailedStatuses.Contains((x.Status ?? string.Empty).ToLower()))
            .Select(x => x.PaymentTxn.OrderId)
            .Distinct()
            .ToListAsync(cancellationToken);

        var returnBlockedSellerOrderIds = await _db.ReturnRequests
            .AsNoTracking()
            .Where(x =>
                sellerOrderIds.Contains(x.SellerOrderItem.SellerOrderId) &&
                !IgnoredReturnStatuses.Contains((x.Status ?? string.Empty).ToLower()))
            .Select(x => x.SellerOrderItem.SellerOrderId)
            .Distinct()
            .ToListAsync(cancellationToken);

        var existingPayoutSellerOrderIds = await _db.PayoutItems
            .AsNoTracking()
            .Where(x => sellerOrderIds.Contains(x.SellerOrderId))
            .Select(x => x.SellerOrderId)
            .ToListAsync(cancellationToken);

        var refundedOrderIdSet = refundedOrderIds.ToHashSet();
        var returnBlockedSellerOrderIdSet = returnBlockedSellerOrderIds.ToHashSet();
        var existingPayoutSellerOrderIdSet = existingPayoutSellerOrderIds.ToHashSet();

        var eligibleSellerOrders = candidates
            .Where(x =>
                IsCompleted(x) &&
                IsPaidOnline(x.Order) &&
                !refundedOrderIdSet.Contains(x.OrderId) &&
                !returnBlockedSellerOrderIdSet.Contains(x.SellerOrderId) &&
                !existingPayoutSellerOrderIdSet.Contains(x.SellerOrderId))
            .ToList();

        var createdPayouts = 0;
        var createdPayoutItems = 0;
        var totalGross = 0m;
        var totalFee = 0m;
        var totalNet = 0m;
        var now = DateTime.UtcNow;

        foreach (var sellerGroup in eligibleSellerOrders.GroupBy(x => x.SellerId))
        {
            var items = sellerGroup.ToList();
            var amountGross = items.Sum(GetGrossAmount);
            var feeAmount = items.Sum(x => x.CommissionAmount);
            var amountNet = items.Sum(x => x.SellerEarning);

            if (amountNet <= 0m)
            {
                continue;
            }

            var payout = new Payout
            {
                SellerId = sellerGroup.Key,
                AmountGross = amountGross,
                FeeAmount = feeAmount,
                AmountNet = amountNet,
                Status = "pending",
                ScheduledAt = null,
                PaidAt = null,
                PaymentTxnId = null,
                CreatedAt = now
            };

            foreach (var sellerOrder in items)
            {
                payout.PayoutItems.Add(new PayoutItem
                {
                    SellerOrderId = sellerOrder.SellerOrderId,
                    Amount = sellerOrder.SellerEarning,
                    Note = "auto_payout_generation"
                });
            }

            _db.Payouts.Add(payout);
            createdPayouts++;
            createdPayoutItems += items.Count;
            totalGross += amountGross;
            totalFee += feeAmount;
            totalNet += amountNet;
        }

        if (createdPayouts > 0)
        {
            await _db.SaveChangesAsync(cancellationToken);
        }

        await transaction.CommitAsync(cancellationToken);
        return new PayoutGenerationResult(createdPayouts, createdPayoutItems, totalGross, totalFee, totalNet);
    }

    private static bool IsCompleted(SellerOrder sellerOrder)
    {
        var orderStatus = Normalize(sellerOrder.Order?.Status);
        var sellerStatus = Normalize(sellerOrder.SellerStatus);

        if (BlockedOrderStatuses.Contains(orderStatus) || BlockedOrderStatuses.Contains(sellerStatus))
        {
            return false;
        }

        return CompletedOrderStatuses.Contains(orderStatus) || CompletedOrderStatuses.Contains(sellerStatus);
    }

    private static bool IsPaidOnline(Order order)
    {
        if (order is null)
        {
            return false;
        }

        var hasCodPayment = order.Payments.Any(x => IsCod(x.PaymentMethod));
        var hasOnlinePaidPayment = order.Payments.Any(x =>
            !IsCod(x.PaymentMethod) &&
            (PaidPaymentStatuses.Contains(Normalize(x.PaymentStatus)) || x.PaymentDate.HasValue));
        var hasOnlinePaidTransaction = order.PaymentTransactions.Any(x =>
            !IsCod(x.Method) &&
            !IsCod(x.Provider) &&
            (PaidPaymentStatuses.Contains(Normalize(x.Status)) || x.PaidAt.HasValue));

        if (hasCodPayment && !hasOnlinePaidPayment && !hasOnlinePaidTransaction)
        {
            return false;
        }

        return !IsCod(order.PaymentStatus) &&
            (PaidPaymentStatuses.Contains(Normalize(order.PaymentStatus)) || hasOnlinePaidPayment || hasOnlinePaidTransaction);
    }

    private static decimal GetGrossAmount(SellerOrder sellerOrder)
        => sellerOrder.SellerEarning + sellerOrder.CommissionAmount;

    private static bool IsCod(string? value)
        => string.Equals(value?.Trim(), "COD", StringComparison.OrdinalIgnoreCase);

    private static string Normalize(string? value)
        => (value ?? string.Empty).Trim().ToLowerInvariant();
}
