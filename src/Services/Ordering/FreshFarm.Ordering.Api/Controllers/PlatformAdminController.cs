using FreshFarm.Ordering.Api.Models;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace FreshFarm.Ordering.Api.Controllers;

[ApiController]
[Route("api/orders/admin/platform")]
[Authorize(Policy = "AdminOnly")]
public sealed class PlatformAdminController : ControllerBase
{
    private static readonly string[] ReconciliationBlockedOrderStatuses = ["cancelled", "canceled", "expired", "refunded", "returned"];
    private static readonly string[] RefundFailedStatuses = ["failed", "rejected", "cancelled", "canceled", "error"];
    private static readonly string[] ReturnRejectedStatuses = ["rejected", "cancelled", "canceled", "denied"];

    private readonly FreshFarmOrderingDBContext _db;

    public PlatformAdminController(FreshFarmOrderingDBContext db)
    {
        _db = db;
    }

    [HttpGet("dashboard")]
    public async Task<IActionResult> Dashboard(CancellationToken cancellationToken = default)
    {
        var trendLabels = new List<string>();
        var trendOrders = new List<int>();
        var trendGmv = new List<decimal>();
        var warnings = new List<string>();

        var now = DateTime.UtcNow;
        var todayStart = now.Date;
        var nextDay = todayStart.AddDays(1);
        var sevenDaysStart = todayStart.AddDays(-6);

        for (var i = 0; i < 7; i++)
        {
            var day = sevenDaysStart.AddDays(i);
            trendLabels.Add(day.ToString("dd/MM"));
            trendOrders.Add(0);
            trendGmv.Add(0m);
        }

        try
        {
            var totalOrders = await _db.Orders
                .AsNoTracking()
                .CountAsync(cancellationToken);

            var newOrdersToday = await _db.Orders
                .AsNoTracking()
                .CountAsync(o => o.OrderDate >= todayStart && o.OrderDate < nextDay, cancellationToken);

            var cancelledOrders = await _db.Orders
                .AsNoTracking()
                .CountAsync(o =>
                        o.Status == "Canceled"
                     || o.Status == "Cancelled"
                     || o.Status == "Failed"
                     || o.Status == "Returned",
                    cancellationToken);

            var commissionEstimateSellerOrdersQuery = BuildCommissionEstimateSellerOrdersQuery();
            var gmv = await commissionEstimateSellerOrdersQuery.SumAsync(
                x => (decimal?)(x.SellerEarning + x.CommissionAmount),
                cancellationToken) ?? 0m;

            var platformRevenue = await commissionEstimateSellerOrdersQuery
                .SumAsync(x => (decimal?)x.CommissionAmount, cancellationToken) ?? 0m;

            var realtimeTransactions = await _db.Orders
                .AsNoTracking()
                .CountAsync(o => o.OrderDate >= now.AddMinutes(-15), cancellationToken);

            var groupedSevenDays = await commissionEstimateSellerOrdersQuery
                .AsNoTracking()
                .Where(x => x.Order != null && x.Order.OrderDate >= sevenDaysStart && x.Order.OrderDate < nextDay)
                .GroupBy(x => x.Order!.OrderDate.Date)
                .Select(g => new
                {
                    Day = g.Key,
                    Orders = g.Count(),
                    Gmv = g.Sum(x => x.SellerEarning + x.CommissionAmount)
                })
                .ToListAsync(cancellationToken);

            var cancelRate = totalOrders > 0
                ? decimal.Round(cancelledOrders * 100m / totalOrders, 2)
                : 0m;

            var groupedLookup = groupedSevenDays
                .ToDictionary(x => x.Day, x => (x.Orders, x.Gmv));

            for (var i = 0; i < 7; i++)
            {
                var day = sevenDaysStart.AddDays(i);
                if (groupedLookup.TryGetValue(day, out var value))
                {
                    trendOrders[i] = value.Orders;
                    trendGmv[i] = decimal.Round(value.Gmv, 2);
                }
            }

            return Ok(new
            {
                gmv,
                platformRevenue,
                totalOrders,
                newOrdersToday,
                cancelledOrders,
                cancelRate,
                realtimeTransactions,
                trendLabels,
                trendOrders,
                trendGmv,
                warnings
            });
        }
        catch (Exception ex)
        {
            warnings.Add($"KPI dashboard fallback: {ex.GetType().Name}");
            return Ok(new
            {
                gmv = 0m,
                platformRevenue = 0m,
                totalOrders = 0,
                newOrdersToday = 0,
                cancelledOrders = 0,
                cancelRate = 0m,
                realtimeTransactions = 0,
                trendLabels,
                trendOrders,
                trendGmv,
                warnings
            });
        }
    }

    private IQueryable<SellerOrder> BuildCommissionEstimateSellerOrdersQuery()
    {
        var refundedOrderIdsQuery = _db.RefundTransactions
            .AsNoTracking()
            .Where(x => !RefundFailedStatuses.Contains((x.Status ?? string.Empty).ToLower()))
            .Select(x => x.PaymentTxn.OrderId);
        var returnBlockedSellerOrderIdsQuery = _db.ReturnRequests
            .AsNoTracking()
            .Where(x => !ReturnRejectedStatuses.Contains((x.Status ?? string.Empty).ToLower()))
            .Select(x => x.SellerOrderItem.SellerOrderId);

        return _db.SellerOrders
            .AsNoTracking()
            .Where(x =>
                x.Order != null &&
                !ReconciliationBlockedOrderStatuses.Contains((x.Order.Status ?? string.Empty).ToLower()) &&
                !ReconciliationBlockedOrderStatuses.Contains((x.SellerStatus ?? string.Empty).ToLower()) &&
                !refundedOrderIdsQuery.Contains(x.OrderId) &&
                !returnBlockedSellerOrderIdsQuery.Contains(x.SellerOrderId));
    }
}
