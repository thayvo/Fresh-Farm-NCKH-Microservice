using FreshFarm.Ordering.Api.Models;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace FreshFarm.Ordering.Api.Controllers;

[ApiController]
[Route("api/orders/admin/customers")]
[Authorize(Policy = "SellerOnly")]
public sealed class AdminCustomersController : ControllerBase
{
    private readonly FreshFarmOrderingDBContext _db;

    public AdminCustomersController(FreshFarmOrderingDBContext db)
    {
        _db = db;
    }

    [HttpGet("metrics")]
    public async Task<IActionResult> GetMetrics([FromQuery] List<int>? userIds, CancellationToken cancellationToken)
    {
        var hasFilter = userIds is { Count: > 0 };

        var query = _db.Orders.AsNoTracking();
        if (hasFilter)
        {
            query = query.Where(o => userIds!.Contains(o.UserId));
        }

        var metrics = await query
            .GroupBy(o => o.UserId)
            .Select(g => new
            {
                userId = g.Key,
                orderCount = g.Count(),
                totalSpent = g.Sum(x => x.TotalAmount)
            })
            .ToListAsync(cancellationToken);

        return Ok(new { success = true, data = metrics });
    }

    [HttpGet("{userId:int}/has-orders")]
    public async Task<IActionResult> HasOrders([FromRoute] int userId, CancellationToken cancellationToken)
    {
        var orderCount = await _db.Orders.AsNoTracking().CountAsync(o => o.UserId == userId, cancellationToken);
        var totalSpent = await _db.Orders.AsNoTracking()
            .Where(o => o.UserId == userId)
            .SumAsync(o => (decimal?)o.TotalAmount, cancellationToken) ?? 0m;

        return Ok(new
        {
            success = true,
            userId,
            hasOrders = orderCount > 0,
            orderCount,
            totalSpent
        });
    }
}
