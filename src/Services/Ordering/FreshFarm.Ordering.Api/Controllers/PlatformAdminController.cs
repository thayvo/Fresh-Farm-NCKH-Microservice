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
            var totalOrdersTask = _db.Orders
                .AsNoTracking()
                .CountAsync(cancellationToken);

            var newOrdersTodayTask = _db.Orders
                .AsNoTracking()
                .CountAsync(o => o.OrderDate >= todayStart && o.OrderDate < nextDay, cancellationToken);

            var cancelledOrdersTask = _db.Orders
                .AsNoTracking()
                .CountAsync(o =>
                        o.Status == "Canceled"
                     || o.Status == "Cancelled"
                     || o.Status == "Failed"
                     || o.Status == "Returned",
                    cancellationToken);

            var gmvTask = _db.Orders
                .AsNoTracking()
                .SumAsync(o => (decimal?)o.TotalAmount, cancellationToken);

            var realtimeTransactionsTask = _db.Orders
                .AsNoTracking()
                .CountAsync(o => o.OrderDate >= now.AddMinutes(-15), cancellationToken);

            var groupedSevenDaysTask = _db.Orders
                .AsNoTracking()
                .Where(o => o.OrderDate >= sevenDaysStart && o.OrderDate < nextDay)
                .GroupBy(o => o.OrderDate.Date)
                .Select(g => new
                {
                    Day = g.Key,
                    Orders = g.Count(),
                    Gmv = g.Sum(x => x.TotalAmount)
                })
                .ToListAsync(cancellationToken);

            await Task.WhenAll(
                totalOrdersTask,
                newOrdersTodayTask,
                cancelledOrdersTask,
                gmvTask,
                realtimeTransactionsTask,
                groupedSevenDaysTask);

            var totalOrders = totalOrdersTask.Result;
            var cancelledOrders = cancelledOrdersTask.Result;
            var gmv = gmvTask.Result ?? 0m;
            var platformRevenue = gmv > 0m ? decimal.Round(gmv * 0.03m, 2) : 0m;
            var cancelRate = totalOrders > 0
                ? decimal.Round(cancelledOrders * 100m / totalOrders, 2)
                : 0m;

            var groupedLookup = groupedSevenDaysTask.Result
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
                newOrdersToday = newOrdersTodayTask.Result,
                cancelledOrders,
                cancelRate,
                realtimeTransactions = realtimeTransactionsTask.Result,
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
}
