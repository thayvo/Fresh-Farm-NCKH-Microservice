using FreshFarm.Ordering.Api.Models;
using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace FreshFarm.Ordering.Api.Controllers;

[ApiController]
[Route("api/orders/admin/coupons")]
[Authorize(Policy = "SellerOrAdmin")]
public sealed class CouponsAdminController : ControllerBase
{
    private static readonly string[] SupportedDiscountTypes = { "fixed", "percent" };

    private readonly FreshFarmOrderingDBContext _db;

    public CouponsAdminController(FreshFarmOrderingDBContext db)
    {
        _db = db;
    }

    [HttpGet]
    public async Task<IActionResult> GetAll([FromQuery] string? scope = null, CancellationToken cancellationToken = default)
    {
        var coupons = await ApplyCouponScopeToQuery(_db.Coupons, scope)
            .AsNoTracking()
            .OrderByDescending(c => c.CreatedDate)
            .ThenByDescending(c => c.CouponId)
            .Select(c => new
            {
                couponId = c.CouponId,
                code = c.Code,
                discountValue = c.DiscountValue,
                expiryDate = c.ExpiryDate,
                minOrderValue = c.MinOrderValue,
                discountType = c.DiscountType,
                isActive = c.IsActive,
                usageLimit = c.UsageLimit,
                usedCount = c.UsedCount,
                description = c.Description,
                createdBy = c.CreatedBy,
                createdDate = c.CreatedDate,
                updatedDate = c.UpdatedDate,
                maxDiscountAmount = c.MaxDiscountAmount
            })
            .ToListAsync(cancellationToken);

        return Ok(coupons);
    }

    [HttpGet("{id:int}")]
    public async Task<IActionResult> GetById([FromRoute] int id, [FromQuery] string? scope = null, CancellationToken cancellationToken = default)
    {
        var coupon = await ApplyCouponScopeToQuery(_db.Coupons, scope)
            .AsNoTracking()
            .Where(c => c.CouponId == id)
            .Select(c => new
            {
                couponId = c.CouponId,
                code = c.Code,
                discountValue = c.DiscountValue,
                expiryDate = c.ExpiryDate,
                minOrderValue = c.MinOrderValue,
                discountType = c.DiscountType,
                isActive = c.IsActive,
                usageLimit = c.UsageLimit,
                usedCount = c.UsedCount,
                description = c.Description,
                createdBy = c.CreatedBy,
                createdDate = c.CreatedDate,
                updatedDate = c.UpdatedDate,
                maxDiscountAmount = c.MaxDiscountAmount
            })
            .FirstOrDefaultAsync(cancellationToken);

        if (coupon is null)
        {
            return NotFound(new { message = "Khong tim thay ma giam gia." });
        }

        return Ok(coupon);
    }

    [HttpPost]
    [Authorize(Policy = "AdminOnly")]
    public async Task<IActionResult> Create([FromBody] CouponUpsertRequest request, CancellationToken cancellationToken)
    {
        var actorUserId = TryGetActorUserIdFromToken();
        if (!actorUserId.HasValue)
        {
            return Unauthorized(new { message = "Khong xac dinh duoc nguoi dung." });
        }

        var normalized = NormalizeRequest(request);
        if (!normalized.isValid)
        {
            return BadRequest(new { message = normalized.error });
        }

        var duplicate = await ApplyCouponScopeToQuery(_db.Coupons, GetCreationScope())
            .AnyAsync(c => c.Code == normalized.code, cancellationToken);
        if (duplicate)
        {
            return BadRequest(new { message = "Ma giam gia da ton tai!" });
        }

        var coupon = new Coupon
        {
            Code = normalized.code,
            DiscountValue = normalized.discountValue,
            DiscountType = normalized.discountType,
            ExpiryDate = DateOnly.FromDateTime(normalized.expiryDate),
            MinOrderValue = normalized.minOrderValue,
            UsageLimit = normalized.usageLimit,
            UsedCount = 0,
            Description = normalized.description,
            MaxDiscountAmount = normalized.maxDiscountAmount,
            IsActive = normalized.isActive,
            CreatedDate = DateTime.UtcNow,
            CreatedBy = IsAdminUser() ? null : actorUserId.Value,
            UpdatedDate = null
        };

        _db.Coupons.Add(coupon);
        await _db.SaveChangesAsync(cancellationToken);

        return Ok(new { success = true, message = "Tao ma giam gia thanh cong!" });
    }

    [HttpPut("{id:int}")]
    [Authorize(Policy = "AdminOnly")]
    public async Task<IActionResult> Update([FromRoute] int id, [FromBody] CouponUpsertRequest request, [FromQuery] string? scope = null, CancellationToken cancellationToken = default)
    {
        var actorUserId = TryGetActorUserIdFromToken();
        if (!actorUserId.HasValue)
        {
            return Unauthorized(new { message = "Khong xac dinh duoc nguoi dung." });
        }

        var coupon = await ApplyCouponScopeToQuery(_db.Coupons, scope)
            .FirstOrDefaultAsync(c => c.CouponId == id, cancellationToken);
        if (coupon is null)
        {
            return NotFound(new { message = "Khong tim thay ma giam gia!" });
        }

        var normalized = NormalizeRequest(request);
        if (!normalized.isValid)
        {
            return BadRequest(new { message = normalized.error });
        }

        var duplicate = await ApplyCouponScopeToQuery(_db.Coupons, scope)
            .AnyAsync(c => c.CouponId != id && c.Code == normalized.code, cancellationToken);
        if (duplicate)
        {
            return BadRequest(new { message = "Ma giam gia da ton tai!" });
        }

        coupon.Code = normalized.code;
        coupon.DiscountValue = normalized.discountValue;
        coupon.DiscountType = normalized.discountType;
        coupon.ExpiryDate = DateOnly.FromDateTime(normalized.expiryDate);
        coupon.MinOrderValue = normalized.minOrderValue;
        coupon.UsageLimit = normalized.usageLimit;
        coupon.Description = normalized.description;
        coupon.MaxDiscountAmount = normalized.maxDiscountAmount;
        coupon.IsActive = normalized.isActive;
        if (!IsAdminUser())
        {
            coupon.CreatedBy ??= actorUserId.Value;
        }
        coupon.UpdatedDate = DateTime.UtcNow;

        await _db.SaveChangesAsync(cancellationToken);

        return Ok(new { success = true, message = "Cap nhat ma giam gia thanh cong!" });
    }

    [HttpDelete("{id:int}")]
    [Authorize(Policy = "AdminOnly")]
    public async Task<IActionResult> Delete([FromRoute] int id, [FromQuery] string? scope = null, CancellationToken cancellationToken = default)
    {
        var coupon = await ApplyCouponScopeToQuery(_db.Coupons, scope)
            .FirstOrDefaultAsync(c => c.CouponId == id, cancellationToken);
        if (coupon is null)
        {
            return NotFound(new { success = false, message = "Khong tim thay ma giam gia!" });
        }

        var usedInOrders = await _db.Orders.AsNoTracking().AnyAsync(o => o.CouponId == id, cancellationToken);
        if (usedInOrders)
        {
            return Conflict(new { success = false, message = "Khong the xoa! Ma da duoc su dung trong don hang." });
        }

        var hasUsageHistory = await _db.CouponUsageHistories.AsNoTracking().AnyAsync(h => h.CouponId == id, cancellationToken);
        if (hasUsageHistory)
        {
            return Conflict(new { success = false, message = "Khong the xoa! Ma co lich su su dung." });
        }

        var distributions = await _db.CouponDistributions.Where(d => d.CouponId == id).ToListAsync(cancellationToken);
        if (distributions.Count > 0)
        {
            _db.CouponDistributions.RemoveRange(distributions);
        }

        _db.Coupons.Remove(coupon);
        await _db.SaveChangesAsync(cancellationToken);

        return Ok(new { success = true, message = "Xoa ma giam gia thanh cong!" });
    }

    [HttpPost("{id:int}/toggle-active")]
    [Authorize(Policy = "AdminOnly")]
    public async Task<IActionResult> ToggleActive([FromRoute] int id, [FromQuery] string? scope = null, CancellationToken cancellationToken = default)
    {
        var coupon = await ApplyCouponScopeToQuery(_db.Coupons, scope)
            .FirstOrDefaultAsync(c => c.CouponId == id, cancellationToken);
        if (coupon is null)
        {
            return NotFound(new { success = false, message = "Khong tim thay ma giam gia!" });
        }

        coupon.IsActive = !coupon.IsActive;
        coupon.UpdatedDate = DateTime.UtcNow;
        await _db.SaveChangesAsync(cancellationToken);

        return Ok(new
        {
            success = true,
            message = coupon.IsActive ? "Da kich hoat ma giam gia!" : "Da vo hieu hoa ma giam gia!",
            isActive = coupon.IsActive
        });
    }

    [HttpGet("generate-code")]
    [Authorize(Policy = "AdminOnly")]
    public async Task<IActionResult> GenerateCode(CancellationToken cancellationToken)
    {
        var code = await GenerateRandomCodeAsync(cancellationToken);
        return Ok(new { code });
    }

    [HttpGet("validate")]
    public async Task<IActionResult> ValidateCoupon([FromQuery] string code, [FromQuery] decimal orderAmount, [FromQuery] string? scope = null, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(code))
        {
            return Ok(new { valid = false, message = "Vui long nhap ma giam gia" });
        }

        var normalizedCode = code.Trim().ToUpperInvariant();
        var coupon = await ApplyCouponScopeToQuery(_db.Coupons, scope)
            .AsNoTracking()
            .FirstOrDefaultAsync(c => c.Code == normalizedCode, cancellationToken);

        if (coupon is null)
        {
            return Ok(new { valid = false, message = "Ma giam gia khong ton tai" });
        }

        var today = DateOnly.FromDateTime(DateTime.Today);

        if (!coupon.IsActive)
        {
            return Ok(new { valid = false, message = "Ma giam gia da bi vo hieu hoa" });
        }

        if (coupon.ExpiryDate < today)
        {
            return Ok(new { valid = false, message = "Ma giam gia da het han" });
        }

        if (coupon.UsageLimit.HasValue && coupon.UsedCount >= coupon.UsageLimit.Value)
        {
            return Ok(new { valid = false, message = "Ma giam gia da het luot su dung" });
        }

        if (coupon.MinOrderValue.HasValue && orderAmount < coupon.MinOrderValue.Value)
        {
            return Ok(new { valid = false, message = "Don hang chua dat gia tri toi thieu" });
        }

        var discount = CalculateDiscount(coupon, orderAmount);

        return Ok(new
        {
            valid = true,
            message = "Ma hop le",
            couponId = coupon.CouponId,
            code = coupon.Code,
            discount,
            discountDisplay = $"{discount:N0}₫",
            discountType = coupon.DiscountType,
            description = coupon.Description
        });
    }

    [HttpGet("statistics")]
    public async Task<IActionResult> GetStatistics([FromQuery] string? scope = null, CancellationToken cancellationToken = default)
    {
        var today = DateOnly.FromDateTime(DateTime.Today);
        var query = ApplyCouponScopeToQuery(_db.Coupons, scope).AsNoTracking();

        var total = await query.CountAsync(cancellationToken);
        var active = await query.CountAsync(c => c.IsActive && c.ExpiryDate >= today, cancellationToken);
        var expired = await query.CountAsync(c => c.ExpiryDate < today, cancellationToken);
        var disabled = await query.CountAsync(c => !c.IsActive, cancellationToken);
        var usageLimitReached = await query
            .CountAsync(c => c.UsageLimit.HasValue && c.UsedCount >= c.UsageLimit.Value, cancellationToken);
        var totalUsed = await query.SumAsync(c => (int?)c.UsedCount, cancellationToken) ?? 0;

        return Ok(new
        {
            total,
            active,
            expired,
            disabled,
            usageLimitReached,
            totalUsed
        });
    }

    [HttpGet("{id:int}/usage-history")]
    public async Task<IActionResult> GetUsageHistory([FromRoute] int id, [FromQuery] string? scope = null, CancellationToken cancellationToken = default)
    {
        var couponExists = await ApplyCouponScopeToQuery(_db.Coupons, scope)
            .AsNoTracking()
            .AnyAsync(c => c.CouponId == id, cancellationToken);
        if (!couponExists)
        {
            return NotFound(new { message = "Khong tim thay ma giam gia." });
        }

        var history = await _db.CouponUsageHistories
            .AsNoTracking()
            .Where(h => h.CouponId == id)
            .OrderByDescending(h => h.UsedDate)
            .Select(h => new
            {
                usageID = h.UsageId,
                usedDate = h.UsedDate,
                customerName = $"U{h.UserId}",
                customerEmail = string.Empty,
                orderID = h.OrderId,
                discountAmount = h.DiscountAmount
            })
            .ToListAsync(cancellationToken);

        return Ok(history);
    }

    [HttpGet("{id:int}/distribution-list")]
    public async Task<IActionResult> GetDistributionList([FromRoute] int id, [FromQuery] string? scope = null, CancellationToken cancellationToken = default)
    {
        var couponExists = await ApplyCouponScopeToQuery(_db.Coupons, scope)
            .AsNoTracking()
            .AnyAsync(c => c.CouponId == id, cancellationToken);
        if (!couponExists)
        {
            return NotFound(new { message = "Khong tim thay ma giam gia." });
        }

        var distributions = await _db.CouponDistributions
            .AsNoTracking()
            .Where(d => d.CouponId == id)
            .OrderByDescending(d => d.SentDate)
            .Select(d => new
            {
                distributionID = d.DistributionId,
                sentDate = d.SentDate,
                customerName = $"U{d.UserId}",
                customerEmail = string.Empty,
                isUsed = d.IsUsed,
                usedDate = d.UsedDate,
                channel = d.Channel,
                status = d.Status
            })
            .ToListAsync(cancellationToken);

        return Ok(distributions);
    }

    [HttpPost("send")]
    [Authorize(Policy = "AdminOnly")]
    public async Task<IActionResult> SendToCustomers([FromBody] SendCouponRequest request, [FromQuery] string? scope = null, CancellationToken cancellationToken = default)
    {
        var actorUserId = TryGetActorUserIdFromToken();
        if (!actorUserId.HasValue)
        {
            return Unauthorized(new { success = false, message = "Khong xac dinh duoc nguoi dung." });
        }

        if (request is null || string.IsNullOrWhiteSpace(request.CouponCode))
        {
            return BadRequest(new { success = false, message = "Ma giam gia khong hop le!" });
        }

        var couponCode = request.CouponCode.Trim().ToUpperInvariant();
        var coupon = await ApplyCouponScopeToQuery(_db.Coupons, scope)
            .FirstOrDefaultAsync(c => c.Code == couponCode, cancellationToken);
        if (coupon is null)
        {
            return BadRequest(new { success = false, message = "Khong tim thay ma giam gia!" });
        }

        var today = DateOnly.FromDateTime(DateTime.Today);
        if (!coupon.IsActive)
        {
            return BadRequest(new { success = false, message = "Ma giam gia da bi vo hieu hoa!" });
        }

        if (coupon.ExpiryDate < today)
        {
            return BadRequest(new { success = false, message = "Ma giam gia da het han!" });
        }

        var customerIds = request.CustomerIds?.Distinct().Where(x => x > 0).ToList() ?? new List<int>();
        if (customerIds.Count == 0)
        {
            return BadRequest(new { success = false, message = "Khong co khach hang nao de gui!" });
        }

        var existing = await _db.CouponDistributions
            .AsNoTracking()
            .Where(d => d.CouponId == coupon.CouponId && customerIds.Contains(d.UserId))
            .Select(d => d.UserId)
            .ToListAsync(cancellationToken);

        var existingSet = existing.ToHashSet();
        var toInsert = customerIds.Where(id => !existingSet.Contains(id)).ToList();

        foreach (var userId in toInsert)
        {
            _db.CouponDistributions.Add(new CouponDistribution
            {
                CouponId = coupon.CouponId,
                UserId = userId,
                SentDate = DateTime.UtcNow,
                SentBy = actorUserId.Value,
                IsUsed = false,
                UsedDate = null,
                Channel = "Admin",
                Status = "Sent"
            });
        }

        await _db.SaveChangesAsync(cancellationToken);

        var sent = toInsert.Count;
        var skipped = customerIds.Count - sent;

        return Ok(new
        {
            success = true,
            sent,
            skipped,
            message = $"Da gui ma {couponCode} cho {sent} khach hang! (Bo qua {skipped} da nhan)"
        });
    }

    private static decimal CalculateDiscount(Coupon coupon, decimal orderAmount)
    {
        decimal discount;

        if (string.Equals(coupon.DiscountType, "percent", StringComparison.OrdinalIgnoreCase))
        {
            discount = orderAmount * coupon.DiscountValue / 100m;
            if (coupon.MaxDiscountAmount.HasValue && coupon.MaxDiscountAmount.Value > 0)
            {
                discount = Math.Min(discount, coupon.MaxDiscountAmount.Value);
            }
        }
        else
        {
            discount = coupon.DiscountValue;
        }

        return Math.Min(discount, orderAmount);
    }

    private static (bool isValid, string error, string code, decimal discountValue, string discountType, DateTime expiryDate, decimal? minOrderValue, int? usageLimit, string? description, decimal? maxDiscountAmount, bool isActive)
        NormalizeRequest(CouponUpsertRequest request)
    {
        if (request is null)
        {
            return (false, "Du lieu khong hop le.", string.Empty, 0m, string.Empty, DateTime.Today, null, null, null, null, false);
        }

        var code = (request.Code ?? string.Empty).Trim().ToUpperInvariant();
        var discountType = (request.DiscountType ?? string.Empty).Trim().ToLowerInvariant();
        var expiryDate = request.ExpiryDate.Date;
        var minOrderValue = request.MinOrderValue;
        var usageLimit = request.UsageLimit;
        var description = string.IsNullOrWhiteSpace(request.Description) ? null : request.Description.Trim();
        var maxDiscountAmount = request.MaxDiscountAmount;
        var discountValue = request.DiscountValue;
        var isActive = request.IsActive;

        if (string.IsNullOrWhiteSpace(code))
        {
            return (false, "Ma giam gia khong duoc de trong!", code, discountValue, discountType, expiryDate, minOrderValue, usageLimit, description, maxDiscountAmount, isActive);
        }

        if (!SupportedDiscountTypes.Contains(discountType))
        {
            return (false, "Loai giam gia khong hop le!", code, discountValue, discountType, expiryDate, minOrderValue, usageLimit, description, maxDiscountAmount, isActive);
        }

        if (discountType == "percent")
        {
            if (discountValue <= 0 || discountValue > 100)
            {
                return (false, "Gia tri phan tram phai lon hon 0 va khong vuot qua 100!", code, discountValue, discountType, expiryDate, minOrderValue, usageLimit, description, maxDiscountAmount, isActive);
            }

            if (maxDiscountAmount.HasValue && maxDiscountAmount.Value <= 0)
            {
                return (false, "Giam toi da phai lon hon 0!", code, discountValue, discountType, expiryDate, minOrderValue, usageLimit, description, maxDiscountAmount, isActive);
            }
        }
        else
        {
            if (discountValue < 1000)
            {
                return (false, "Gia tri giam toi thieu la 1,000₫!", code, discountValue, discountType, expiryDate, minOrderValue, usageLimit, description, maxDiscountAmount, isActive);
            }

            maxDiscountAmount = null;
        }

        if (expiryDate < DateTime.Today)
        {
            return (false, "Ngay het han phai tu hom nay tro di!", code, discountValue, discountType, expiryDate, minOrderValue, usageLimit, description, maxDiscountAmount, isActive);
        }

        if (usageLimit.HasValue && usageLimit.Value <= 0)
        {
            return (false, "Gioi han so lan su dung phai lon hon 0!", code, discountValue, discountType, expiryDate, minOrderValue, usageLimit, description, maxDiscountAmount, isActive);
        }

        if (minOrderValue.HasValue && minOrderValue.Value < 0)
        {
            return (false, "Gia tri don hang toi thieu khong hop le!", code, discountValue, discountType, expiryDate, minOrderValue, usageLimit, description, maxDiscountAmount, isActive);
        }

        return (true, string.Empty, code, discountValue, discountType, expiryDate, minOrderValue, usageLimit, description, maxDiscountAmount, isActive);
    }

    private async Task<string> GenerateRandomCodeAsync(CancellationToken cancellationToken, int length = 8)
    {
        const string chars = "ABCDEFGHIJKLMNOPQRSTUVWXYZ0123456789";
        var random = new Random();

        while (true)
        {
            var code = new string(Enumerable.Repeat(chars, length)
                .Select(s => s[random.Next(s.Length)])
                .ToArray());

            var exists = await _db.Coupons.AsNoTracking().AnyAsync(c => c.Code == code, cancellationToken);
            if (!exists)
            {
                return code;
            }
        }
    }

    private int? TryGetActorUserIdFromToken()
    {
        var sub = User.FindFirstValue(JwtRegisteredClaimNames.Sub)
                  ?? User.FindFirstValue(ClaimTypes.NameIdentifier)
                  ?? User.FindFirstValue("sub");

        return int.TryParse(sub, out var userId) ? userId : null;
    }

    private bool IsAdminUser()
    {
        return User.IsInRole("Admin");
    }

    private IQueryable<Coupon> ApplyCouponScopeToQuery(IQueryable<Coupon> query, string? scope)
    {
        if (IsAdminUser())
        {
            return NormalizeScope(scope) switch
            {
                "platform" => query.Where(c => c.CreatedBy == null),
                "seller" => query.Where(c => c.CreatedBy != null),
                _ => query
            };
        }

        var sellerId = TryGetActorUserIdFromToken();
        if (!sellerId.HasValue)
        {
            return query.Where(_ => false);
        }

        return query.Where(c => c.CreatedBy == sellerId.Value);
    }

    private string GetCreationScope()
    {
        return IsAdminUser() ? "platform" : "seller";
    }

    private static string NormalizeScope(string? scope)
    {
        if (string.IsNullOrWhiteSpace(scope))
        {
            return "all";
        }

        var normalized = scope.Trim().ToLowerInvariant();
        return normalized is "all" or "platform" or "seller" ? normalized : "all";
    }

    public sealed class CouponUpsertRequest
    {
        public string Code { get; set; } = string.Empty;

        public decimal DiscountValue { get; set; }

        public string DiscountType { get; set; } = string.Empty;

        public DateTime ExpiryDate { get; set; }

        public decimal? MinOrderValue { get; set; }

        public int? UsageLimit { get; set; }

        public string? Description { get; set; }

        public decimal? MaxDiscountAmount { get; set; }

        public bool IsActive { get; set; } = true;
    }

    public sealed class SendCouponRequest
    {
        public string CouponCode { get; set; } = string.Empty;

        public List<int>? CustomerIds { get; set; }

        public bool SendToAll { get; set; }
    }
}
