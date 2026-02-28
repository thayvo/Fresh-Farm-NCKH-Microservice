using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using FreshFarm.Ordering.Api.Models;

namespace FreshFarm.Ordering.Api.Controllers;

[ApiController]
[Route("api/orders/admin/loyalty")]
[Authorize(Policy = "SellerOnly")]
public sealed class LoyaltyAdminController : ControllerBase
{
    private readonly FreshFarmOrderingDBContext _db;

    public LoyaltyAdminController(FreshFarmOrderingDBContext db)
    {
        _db = db;
    }

    [HttpGet("dashboard")]
    public async Task<IActionResult> Dashboard(CancellationToken cancellationToken = default)
    {
        var now = DateTime.UtcNow;
        var quarter = (byte)((now.Month - 1) / 3 + 1);
        var quarterStartMonth = (quarter - 1) * 3 + 1;
        var quarterStart = new DateTime(now.Year, quarterStartMonth, 1);
        var quarterEnd = quarterStart.AddMonths(3);

        var histories = await _db.LoyaltyPointHistories
            .AsNoTracking()
            .OrderByDescending(x => x.CreatedAt)
            .ToListAsync(cancellationToken);

        var userIds = histories.Select(x => x.UserId).Distinct().ToList();
        var latestOrdersByUser = await _db.Orders
            .AsNoTracking()
            .Where(o => userIds.Contains(o.UserId))
            .GroupBy(o => o.UserId)
            .Select(g => g.OrderByDescending(x => x.OrderDate).First())
            .ToDictionaryAsync(x => x.UserId, cancellationToken);

        var totalByUser = histories
            .GroupBy(x => x.UserId)
            .ToDictionary(g => g.Key, g => g.Sum(x => x.Points));

        var quarterByUser = histories
            .Where(x => x.CreatedAt >= quarterStart && x.CreatedAt < quarterEnd)
            .GroupBy(x => x.UserId)
            .ToDictionary(g => g.Key, g => g.Sum(x => x.Points));

        var topQuarter = quarterByUser
            .OrderByDescending(x => x.Value)
            .ThenBy(x => x.Key)
            .Take(10)
            .Select(x => new
            {
                userID = x.Key,
                fullName = latestOrdersByUser.TryGetValue(x.Key, out var order) && !string.IsNullOrWhiteSpace(order.BuyerFullName)
                    ? order.BuyerFullName
                    : $"User #{x.Key}",
                totalPoints = totalByUser.TryGetValue(x.Key, out var totalPts) ? totalPts : 0,
                quarterPoints = x.Value,
                rankName = MapRankName(x.Value)
            })
            .ToList();

        var recentActivities = histories
            .Take(10)
            .Select(x => new
            {
                createdAt = x.CreatedAt,
                userID = x.UserId,
                userName = latestOrdersByUser.TryGetValue(x.UserId, out var order) && !string.IsNullOrWhiteSpace(order.BuyerFullName)
                    ? order.BuyerFullName
                    : $"User #{x.UserId}",
                orderID = x.OrderId,
                points = x.Points,
                direction = x.Direction,
                reason = x.Reason
            })
            .ToList();

        var totalEarned = histories
            .Where(x => string.Equals(x.Direction, "EARN", StringComparison.OrdinalIgnoreCase))
            .Sum(x => x.Points);
        var totalRedeemedRaw = histories
            .Where(x => string.Equals(x.Direction, "REDEEM", StringComparison.OrdinalIgnoreCase))
            .Sum(x => x.Points);

        return Ok(new
        {
            totalEarned,
            totalRedeemed = Math.Abs(totalRedeemedRaw),
            usersWithPoints = totalByUser.Values.Count(x => x > 0),
            currentYear = now.Year,
            currentQuarter = quarter,
            topQuarterUsers = topQuarter,
            recentActivities
        });
    }

    [HttpGet("users")]
    public async Task<IActionResult> Users(
        [FromQuery] string? q = null,
        [FromQuery] int page = 1,
        [FromQuery] int pageSize = 10,
        CancellationToken cancellationToken = default)
    {
        if (page < 1)
        {
            page = 1;
        }

        if (pageSize <= 0 || pageSize > 100)
        {
            pageSize = 10;
        }

        var histories = await _db.LoyaltyPointHistories
            .AsNoTracking()
            .ToListAsync(cancellationToken);

        var userIds = histories.Select(x => x.UserId).Distinct().ToList();
        var latestOrdersByUser = await _db.Orders
            .AsNoTracking()
            .Where(o => userIds.Contains(o.UserId))
            .GroupBy(o => o.UserId)
            .Select(g => g.OrderByDescending(x => x.OrderDate).First())
            .ToDictionaryAsync(x => x.UserId, cancellationToken);

        var now = DateTime.UtcNow;
        var quarter = (byte)((now.Month - 1) / 3 + 1);
        var quarterStartMonth = (quarter - 1) * 3 + 1;
        var quarterStart = new DateTime(now.Year, quarterStartMonth, 1);
        var quarterEnd = quarterStart.AddMonths(3);

        var rows = histories
            .GroupBy(x => x.UserId)
            .Select(g =>
            {
                var total = g.Sum(x => x.Points);
                var quarterPts = g.Where(x => x.CreatedAt >= quarterStart && x.CreatedAt < quarterEnd).Sum(x => x.Points);
                var fullName = latestOrdersByUser.TryGetValue(g.Key, out var order) && !string.IsNullOrWhiteSpace(order.BuyerFullName)
                    ? order.BuyerFullName
                    : $"User #{g.Key}";

                return new
                {
                    userID = g.Key,
                    fullName,
                    totalPoints = total,
                    currentQuarterPoints = quarterPts,
                    rankName = MapRankName(quarterPts)
                };
            })
            .ToList();

        if (!string.IsNullOrWhiteSpace(q))
        {
            var term = q.Trim().ToLowerInvariant();
            rows = rows.Where(x =>
                    x.fullName.ToLowerInvariant().Contains(term) ||
                    x.userID.ToString().Contains(term))
                .ToList();
        }

        rows = rows
            .OrderByDescending(x => x.currentQuarterPoints)
            .ThenByDescending(x => x.totalPoints)
            .ThenBy(x => x.fullName)
            .ToList();

        var total = rows.Count;
        var paged = rows
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .ToList();

        return Ok(new
        {
            query = q ?? string.Empty,
            page,
            pageSize,
            total,
            rows = paged
        });
    }

    [HttpGet("history")]
    public async Task<IActionResult> History(
        [FromQuery] int? userId = null,
        [FromQuery] string? direction = null,
        [FromQuery] DateTime? start = null,
        [FromQuery] DateTime? end = null,
        [FromQuery] int page = 1,
        [FromQuery] int pageSize = 20,
        CancellationToken cancellationToken = default)
    {
        if (page < 1)
        {
            page = 1;
        }

        if (pageSize <= 0 || pageSize > 200)
        {
            pageSize = 20;
        }

        var query = _db.LoyaltyPointHistories
            .AsNoTracking()
            .AsQueryable();

        if (userId.HasValue)
        {
            query = query.Where(x => x.UserId == userId.Value);
        }

        if (!string.IsNullOrWhiteSpace(direction))
        {
            query = query.Where(x => x.Direction == direction);
        }

        if (start.HasValue)
        {
            query = query.Where(x => x.CreatedAt >= start.Value);
        }

        if (end.HasValue)
        {
            query = query.Where(x => x.CreatedAt < end.Value.AddDays(1));
        }

        var total = await query.CountAsync(cancellationToken);
        var pageRows = await query
            .OrderByDescending(x => x.CreatedAt)
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .ToListAsync(cancellationToken);

        var namesByUser = await ResolveUserNamesAsync(pageRows.Select(x => x.UserId).Distinct(), cancellationToken);

        return Ok(new
        {
            userID = userId,
            direction,
            start,
            end,
            page,
            pageSize,
            total,
            rows = pageRows.Select(x => new
            {
                createdAt = x.CreatedAt,
                userID = x.UserId,
                userName = namesByUser.TryGetValue(x.UserId, out var fullName) ? fullName : $"User #{x.UserId}",
                orderID = x.OrderId,
                points = x.Points,
                direction = x.Direction,
                reason = x.Reason
            })
        });
    }

    [HttpGet("config")]
    public async Task<IActionResult> Config(CancellationToken cancellationToken = default)
    {
        var cfg = await _db.LoyaltyConfigs
            .AsNoTracking()
            .OrderByDescending(x => x.UpdatedAt)
            .FirstOrDefaultAsync(cancellationToken);

        return Ok(new
        {
            earnRate = cfg?.EarnRate ?? 0.01m,
            includeShippingFee = cfg?.IncludeShippingFee ?? false,
            paidKeywords = cfg?.PaidKeywords ?? "Da thanh toan;Paid;Completed",
            updatedAt = cfg?.UpdatedAt
        });
    }

    [HttpPut("config")]
    public async Task<IActionResult> UpdateConfig([FromBody] LoyaltyConfigRequest? request, CancellationToken cancellationToken = default)
    {
        if (request is null)
        {
            return BadRequest(new { success = false, message = "Du lieu khong hop le." });
        }

        if (request.EarnRate < 0)
        {
            return BadRequest(new { success = false, message = "EarnRate khong hop le." });
        }

        var cfg = await _db.LoyaltyConfigs
            .OrderByDescending(x => x.UpdatedAt)
            .FirstOrDefaultAsync(cancellationToken);

        if (cfg is null)
        {
            cfg = new LoyaltyConfig
            {
                EarnRate = request.EarnRate,
                IncludeShippingFee = request.IncludeShippingFee,
                PaidKeywords = request.PaidKeywords ?? string.Empty,
                CreatedAt = DateTime.UtcNow,
                UpdatedAt = DateTime.UtcNow
            };
            _db.LoyaltyConfigs.Add(cfg);
        }
        else
        {
            cfg.EarnRate = request.EarnRate;
            cfg.IncludeShippingFee = request.IncludeShippingFee;
            cfg.PaidKeywords = request.PaidKeywords ?? string.Empty;
            cfg.UpdatedAt = DateTime.UtcNow;
        }

        await _db.SaveChangesAsync(cancellationToken);
        return Ok(new { success = true, message = "Da luu cau hinh tich diem." });
    }

    [HttpPost("adjust")]
    public async Task<IActionResult> Adjust([FromBody] LoyaltyAdjustRequest? request, CancellationToken cancellationToken = default)
    {
        if (request is null || request.UserID <= 0 || request.Points == 0)
        {
            return BadRequest(new { success = false, message = "Du lieu dieu chinh diem khong hop le." });
        }

        _db.LoyaltyPointHistories.Add(new LoyaltyPointHistory
        {
            UserId = request.UserID,
            OrderId = null,
            Points = request.Points,
            Direction = "ADJUST",
            Reason = string.IsNullOrWhiteSpace(request.Reason) ? "Dieu chinh thu cong" : request.Reason.Trim(),
            CreatedAt = DateTime.UtcNow
        });

        await _db.SaveChangesAsync(cancellationToken);
        return Ok(new { success = true, message = "Da dieu chinh diem thanh cong." });
    }

    [HttpPost("sync-award")]
    public async Task<IActionResult> SyncAward([FromBody] LoyaltySyncAwardRequest? request, CancellationToken cancellationToken = default)
    {
        var cfg = await _db.LoyaltyConfigs
            .AsNoTracking()
            .OrderByDescending(x => x.UpdatedAt)
            .FirstOrDefaultAsync(cancellationToken);

        var earnRate = cfg?.EarnRate ?? 0.01m;
        var includeShippingFee = cfg?.IncludeShippingFee ?? false;
        var paidKeywords = ParsePaidKeywords(cfg?.PaidKeywords);

        var ordersQuery = _db.Orders
            .Where(o => o.PointsEarned == 0)
            .Where(o => o.Status != null && o.Status.ToLower() == "delivered");

        if (request?.Start.HasValue == true)
        {
            ordersQuery = ordersQuery.Where(o => o.OrderDate >= request.Start.Value);
        }

        if (request?.End.HasValue == true)
        {
            ordersQuery = ordersQuery.Where(o => o.OrderDate < request.End.Value.AddDays(1));
        }

        var orders = await ordersQuery
            .OrderBy(o => o.OrderId)
            .ToListAsync(cancellationToken);

        var orderIds = orders.Select(x => x.OrderId).ToHashSet();
        var existingAwardedOrderIds = await _db.LoyaltyPointHistories
            .AsNoTracking()
            .Where(x => x.OrderId.HasValue && orderIds.Contains(x.OrderId.Value))
            .Where(x => x.Direction == "EARN")
            .Select(x => x.OrderId!.Value)
            .Distinct()
            .ToListAsync(cancellationToken);

        var existingAwardedSet = existingAwardedOrderIds.ToHashSet();
        var ok = 0;
        var fail = 0;

        foreach (var order in orders)
        {
            if (existingAwardedSet.Contains(order.OrderId))
            {
                continue;
            }

            if (!IsPaid(order, paidKeywords))
            {
                continue;
            }

            var baseAmount = includeShippingFee ? order.TotalAmount : Math.Max(0m, order.TotalAmount - order.ShippingFee);
            var calculated = (int)Math.Round(baseAmount * earnRate, MidpointRounding.AwayFromZero);
            if (calculated <= 0)
            {
                continue;
            }

            try
            {
                order.PointsEarned = calculated;
                _db.LoyaltyPointHistories.Add(new LoyaltyPointHistory
                {
                    UserId = order.UserId,
                    OrderId = order.OrderId,
                    Points = calculated,
                    Direction = "EARN",
                    Reason = $"Tich diem tu don hang #{order.OrderId:D6}",
                    CreatedAt = DateTime.UtcNow
                });
                ok++;
            }
            catch
            {
                fail++;
            }
        }

        await _db.SaveChangesAsync(cancellationToken);
        return Ok(new { success = true, ok, fail });
    }

    private async Task<Dictionary<int, string>> ResolveUserNamesAsync(IEnumerable<int> userIds, CancellationToken cancellationToken)
    {
        var ids = userIds.Distinct().ToList();
        if (ids.Count == 0)
        {
            return new Dictionary<int, string>();
        }

        var latestOrders = await _db.Orders
            .AsNoTracking()
            .Where(o => ids.Contains(o.UserId))
            .GroupBy(o => o.UserId)
            .Select(g => g.OrderByDescending(x => x.OrderDate).First())
            .ToListAsync(cancellationToken);

        return latestOrders.ToDictionary(
            x => x.UserId,
            x => string.IsNullOrWhiteSpace(x.BuyerFullName) ? $"User #{x.UserId}" : x.BuyerFullName);
    }

    private static bool IsPaid(Order order, HashSet<string> paidKeywords)
    {
        if (order.PaidAt.HasValue)
        {
            return true;
        }

        var status = (order.PaymentStatus ?? string.Empty).Trim();
        if (string.IsNullOrWhiteSpace(status))
        {
            return false;
        }

        var normalized = status.ToLowerInvariant();
        if (paidKeywords.Contains(normalized))
        {
            return true;
        }

        return normalized.Contains("paid") || normalized.Contains("thanh toan");
    }

    private static HashSet<string> ParsePaidKeywords(string? raw)
    {
        var defaults = new[] { "da thanh toan", "paid", "completed" };

        var set = (raw ?? string.Join(';', defaults))
            .Split(';', StringSplitOptions.RemoveEmptyEntries)
            .Select(x => x.Trim().ToLowerInvariant())
            .Where(x => !string.IsNullOrWhiteSpace(x))
            .ToHashSet();

        foreach (var value in defaults)
        {
            set.Add(value);
        }

        return set;
    }

    private static string MapRankName(int quarterPoints)
    {
        if (quarterPoints >= 5000)
        {
            return "Diamond";
        }

        if (quarterPoints >= 3000)
        {
            return "Platinum";
        }

        if (quarterPoints >= 1500)
        {
            return "Gold";
        }

        if (quarterPoints >= 500)
        {
            return "Silver";
        }

        return "Bronze";
    }

    public sealed class LoyaltyConfigRequest
    {
        public decimal EarnRate { get; set; }

        public bool IncludeShippingFee { get; set; }

        public string? PaidKeywords { get; set; }
    }

    public sealed class LoyaltyAdjustRequest
    {
        public int UserID { get; set; }

        public int Points { get; set; }

        public string? Reason { get; set; }

        public bool IncludeInRank { get; set; }
    }

    public sealed class LoyaltySyncAwardRequest
    {
        public DateTime? Start { get; set; }

        public DateTime? End { get; set; }
    }
}
