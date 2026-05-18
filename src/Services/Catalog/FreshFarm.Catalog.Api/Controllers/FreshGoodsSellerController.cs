using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.ComponentModel.DataAnnotations;
using FreshFarm.Catalog.Api.Models;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace FreshFarm.Catalog.Api.Controllers;

[ApiController]
[Route("api/catalog/seller/fresh-ops")]
[Authorize(Policy = "SellerOnly")]
public sealed class FreshGoodsSellerController : ControllerBase
{
    private static readonly string[] AllowedLotStatuses = { "active", "held", "expired", "recalled", "depleted" };
    private static readonly string[] AllowedQualityStatuses = { "ok", "watch", "blocked" };

    private readonly FreshFarmCatalogDBContext _db;

    public FreshGoodsSellerController(FreshFarmCatalogDBContext db)
    {
        _db = db;
    }

    [HttpPost("lots")]
    public async Task<IActionResult> CreateLot([FromBody] SellerFreshInventoryLotUpsertRequest request, CancellationToken cancellationToken)
    {
        var sellerId = TryGetSellerIdFromToken();
        if (!sellerId.HasValue)
        {
            return Forbid();
        }

        if (!ModelState.IsValid)
        {
            return ValidationProblem(ModelState);
        }

        var validation = await ValidateLotAsync(request, sellerId.Value, cancellationToken);
        if (!string.IsNullOrWhiteSpace(validation))
        {
            return BadRequest(new { message = validation });
        }

        var entity = new FreshInventoryLot
        {
            ProductId = request.ProductId,
            SellerId = sellerId.Value,
            LotCode = request.LotCode.Trim(),
            TraceCode = NormalizeOptional(request.TraceCode, 120),
            FarmName = NormalizeOptional(request.FarmName, 150),
            OriginRegion = NormalizeOptional(request.OriginRegion, 150),
            HarvestedAt = request.HarvestedAt,
            PackedAt = request.PackedAt,
            ReceivedAt = request.ReceivedAt ?? DateTime.UtcNow,
            ExpiresAt = request.ExpiresAt,
            InitialQuantity = request.InitialQuantity,
            RemainingQuantity = request.RemainingQuantity <= 0 ? request.InitialQuantity : request.RemainingQuantity,
            UnitCost = request.UnitCost,
            Status = NormalizeLotStatus(request.Status),
            QualityStatus = NormalizeQualityStatus(request.QualityStatus),
            Notes = NormalizeOptional(request.Notes, 2000),
            CreatedAt = DateTime.UtcNow
        };

        _db.FreshInventoryLots.Add(entity);
        await _db.SaveChangesAsync(cancellationToken);

        return Ok(new { message = "Đã tạo lô hàng tươi.", freshInventoryLotId = entity.FreshInventoryLotId });
    }

    private async Task<string?> ValidateLotAsync(SellerFreshInventoryLotUpsertRequest request, int sellerId, CancellationToken cancellationToken)
    {
        var sellerOwnsProduct = await _db.SellerProducts
            .AsNoTracking()
            .AnyAsync(x => x.ProductId == request.ProductId && x.SellerId == sellerId && x.IsActive, cancellationToken);
        if (!sellerOwnsProduct)
        {
            return "Bạn không sở hữu sản phẩm này.";
        }

        var normalizedLotCode = request.LotCode.Trim();
        var duplicate = await _db.FreshInventoryLots
            .AsNoTracking()
            .AnyAsync(x => x.ProductId == request.ProductId && x.SellerId == sellerId && x.LotCode == normalizedLotCode, cancellationToken);
        if (duplicate)
        {
            return "Mã lô đã tồn tại cho sản phẩm này.";
        }

        if (request.ExpiresAt.HasValue && request.HarvestedAt.HasValue && request.ExpiresAt < request.HarvestedAt)
        {
            return "Hạn sử dụng không thể nhỏ hơn ngày thu hoạch.";
        }

        if (request.RemainingQuantity > request.InitialQuantity)
        {
            return "Số lượng còn lại không thể lớn hơn số lượng đầu kỳ.";
        }

        return null;
    }

    private int? TryGetSellerIdFromToken()
    {
        var sub = User.FindFirstValue(JwtRegisteredClaimNames.Sub)
                  ?? User.FindFirstValue(ClaimTypes.NameIdentifier)
                  ?? User.FindFirstValue("sub");

        return int.TryParse(sub, out var sellerId) ? sellerId : null;
    }

    private static string NormalizeLotStatus(string? value)
    {
        var normalized = string.IsNullOrWhiteSpace(value) ? "active" : value.Trim().ToLowerInvariant();
        return AllowedLotStatuses.Contains(normalized) ? normalized : "active";
    }

    private static string NormalizeQualityStatus(string? value)
    {
        var normalized = string.IsNullOrWhiteSpace(value) ? "ok" : value.Trim().ToLowerInvariant();
        return AllowedQualityStatuses.Contains(normalized) ? normalized : "ok";
    }

    private static string? NormalizeOptional(string? value, int maxLength)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return null;
        }

        var trimmed = value.Trim();
        return trimmed.Length <= maxLength ? trimmed : trimmed[..maxLength];
    }

    public sealed class SellerFreshInventoryLotUpsertRequest
    {
        [Range(1, int.MaxValue)]
        public int ProductId { get; set; }

        [Required]
        [StringLength(80)]
        public string LotCode { get; set; } = string.Empty;

        [StringLength(120)]
        public string? TraceCode { get; set; }

        [StringLength(150)]
        public string? FarmName { get; set; }

        [StringLength(150)]
        public string? OriginRegion { get; set; }

        public DateTime? HarvestedAt { get; set; }

        public DateTime? PackedAt { get; set; }

        public DateTime? ReceivedAt { get; set; }

        public DateTime? ExpiresAt { get; set; }

        [Range(1, 1000000)]
        public int InitialQuantity { get; set; }

        [Range(0, 1000000)]
        public int RemainingQuantity { get; set; }

        [Range(typeof(decimal), "0", "999999999")]
        public decimal? UnitCost { get; set; }

        [Required]
        [StringLength(40)]
        public string Status { get; set; } = "active";

        [Required]
        [StringLength(40)]
        public string QualityStatus { get; set; } = "ok";

        [StringLength(2000)]
        public string? Notes { get; set; }
    }
}
