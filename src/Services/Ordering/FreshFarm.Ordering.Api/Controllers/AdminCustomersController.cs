using FreshFarm.Ordering.Api.Models;
using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace FreshFarm.Ordering.Api.Controllers;

[ApiController]
[Route("api/orders/admin/customers")]
[Authorize(Policy = "SellerOrAdmin")]
public sealed class AdminCustomersController : ControllerBase
{
    private readonly FreshFarmOrderingDBContext _db;

    public AdminCustomersController(FreshFarmOrderingDBContext db)
    {
        _db = db;
    }

    [HttpGet("ids")]
    public async Task<IActionResult> GetCustomerIds(CancellationToken cancellationToken)
    {
        var scopedOrders = BuildScopedOrdersQuery();
        var userIds = await scopedOrders
            .Select(o => o.UserId)
            .Distinct()
            .OrderBy(id => id)
            .ToListAsync(cancellationToken);

        return Ok(new { success = true, data = userIds });
    }

    [HttpGet("metrics")]
    public async Task<IActionResult> GetMetrics([FromQuery] List<int>? userIds, CancellationToken cancellationToken)
    {
        var hasFilter = userIds is { Count: > 0 };

        var query = BuildScopedOrdersQuery();
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
        var sellerOrders = BuildScopedOrdersQuery();
        var orderCount = await sellerOrders.CountAsync(o => o.UserId == userId, cancellationToken);
        var totalSpent = await sellerOrders
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

    private int? TryGetSellerIdFromToken()
    {
        var sub = User.FindFirstValue(JwtRegisteredClaimNames.Sub)
                  ?? User.FindFirstValue(ClaimTypes.NameIdentifier)
                  ?? User.FindFirstValue("sub");

        return int.TryParse(sub, out var sellerId) ? sellerId : null;
    }

    private IQueryable<Order> ApplySellerScopeToOrders(IQueryable<Order> query)
    {
        var sellerId = TryGetSellerIdFromToken();
        if (!sellerId.HasValue)
        {
            return query.Where(_ => false);
        }

        return query.Where(o => o.SellerOrders.Any(so =>
            so.SellerId == sellerId.Value &&
            so.SellerOrderItems.Any()));
    }

    private IQueryable<Order> BuildScopedOrdersQuery()
    {
        if (User.IsInRole("Admin"))
        {
            return _db.Orders.AsNoTracking();
        }

        return ApplySellerScopeToOrders(_db.Orders.AsNoTracking());
    }
}
