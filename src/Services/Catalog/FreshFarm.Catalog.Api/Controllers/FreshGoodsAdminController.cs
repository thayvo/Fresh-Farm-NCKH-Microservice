using FreshFarm.Catalog.Api.Dtos;
using FreshFarm.Catalog.Api.Models;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace FreshFarm.Catalog.Api.Controllers;

[ApiController]
[Route("api/catalog/admin/fresh-ops")]
[Authorize(Policy = "AdminOnly")]
public sealed class FreshGoodsAdminController : ControllerBase
{
    private static readonly string[] AllowedLotStatuses = { "active", "held", "expired", "recalled", "depleted" };
    private static readonly string[] AllowedQualityStatuses = { "ok", "watch", "blocked" };
    private static readonly string[] AllowedRecallStatuses = { "open", "investigating", "resolved" };
    private static readonly string[] AllowedSeverities = { "low", "medium", "high", "critical" };

    private readonly FreshFarmCatalogDBContext _db;

    public FreshGoodsAdminController(FreshFarmCatalogDBContext db)
    {
        _db = db;
    }

    [HttpGet("center")]
    public async Task<IActionResult> GetCenter(
        [FromQuery] string? q = null,
        [FromQuery] int? sellerId = null,
        [FromQuery] string? status = null,
        CancellationToken cancellationToken = default)
    {
        var normalizedQuery = q?.Trim();
        var normalizedStatus = NormalizeStatus(status);
        var now = DateTime.UtcNow;
        var soon = now.AddDays(3);

        var lotQuery = _db.FreshInventoryLots
            .AsNoTracking()
            .Include(x => x.Product)
            .AsQueryable();

        if (sellerId.HasValue && sellerId.Value > 0)
        {
            lotQuery = lotQuery.Where(x => x.SellerId == sellerId.Value);
        }

        if (!string.IsNullOrWhiteSpace(normalizedQuery))
        {
            lotQuery = lotQuery.Where(x =>
                x.LotCode.Contains(normalizedQuery) ||
                (x.TraceCode != null && x.TraceCode.Contains(normalizedQuery)) ||
                x.Product.ProductName.Contains(normalizedQuery) ||
                x.Product.Sku.Contains(normalizedQuery));
        }

        if (normalizedStatus != "all")
        {
            lotQuery = lotQuery.Where(x => x.Status == normalizedStatus);
        }

        var lots = await lotQuery
            .OrderBy(x => x.ExpiresAt ?? DateTime.MaxValue)
            .ThenByDescending(x => x.CreatedAt)
            .Select(x => new
            {
                x.FreshInventoryLotId,
                x.ProductId,
                ProductName = x.Product.ProductName,
                x.Product.Sku,
                x.SellerId,
                x.LotCode,
                x.TraceCode,
                x.FarmName,
                x.OriginRegion,
                x.HarvestedAt,
                x.PackedAt,
                x.ReceivedAt,
                x.ExpiresAt,
                x.InitialQuantity,
                x.RemainingQuantity,
                x.UnitCost,
                x.Status,
                x.QualityStatus,
                x.Notes,
                x.CreatedAt
            })
            .ToListAsync(cancellationToken);

        var recallQuery = _db.FreshQualityRecalls
            .AsNoTracking()
            .Include(x => x.Product)
            .Include(x => x.FreshInventoryLot)
            .AsQueryable();

        if (sellerId.HasValue && sellerId.Value > 0)
        {
            recallQuery = recallQuery.Where(x => x.SellerId == sellerId.Value);
        }

        if (!string.IsNullOrWhiteSpace(normalizedQuery))
        {
            recallQuery = recallQuery.Where(x =>
                x.RecallCode.Contains(normalizedQuery) ||
                x.Title.Contains(normalizedQuery) ||
                (x.Product != null && x.Product.ProductName.Contains(normalizedQuery)) ||
                (x.FreshInventoryLot != null && x.FreshInventoryLot.LotCode.Contains(normalizedQuery)));
        }

        var recalls = await recallQuery
            .OrderByDescending(x => x.StartedAt)
            .ThenByDescending(x => x.CreatedAt)
            .Select(x => new
            {
                x.FreshQualityRecallId,
                x.RecallCode,
                x.ProductId,
                ProductName = x.Product != null ? x.Product.ProductName : null,
                x.FreshInventoryLotId,
                LotCode = x.FreshInventoryLot != null ? x.FreshInventoryLot.LotCode : null,
                x.SellerId,
                x.RecallType,
                x.Severity,
                x.Status,
                x.Title,
                x.Reason,
                x.ActionRequired,
                x.StartedAt,
                x.ResolvedAt
            })
            .ToListAsync(cancellationToken);

        var sellerOptions = lots
            .Select(x => x.SellerId)
            .Concat(recalls.Where(x => x.SellerId.HasValue).Select(x => x.SellerId!.Value))
            .Distinct()
            .OrderBy(x => x)
            .Select(x => new { sellerId = x, text = $"Seller #{x}" })
            .ToList();

        return Ok(new
        {
            stats = new
            {
                totalLots = lots.Count,
                expiringSoonLots = lots.Count(x => x.ExpiresAt.HasValue && x.ExpiresAt.Value >= now && x.ExpiresAt.Value <= soon && x.RemainingQuantity > 0),
                expiredLots = lots.Count(x => x.ExpiresAt.HasValue && x.ExpiresAt.Value < now && x.RemainingQuantity > 0),
                openRecalls = recalls.Count(x => x.Status != "resolved"),
                traceCoverage = lots.Count == 0 ? 0 : (int)Math.Round(lots.Count(x => !string.IsNullOrWhiteSpace(x.TraceCode)) * 100d / lots.Count, MidpointRounding.AwayFromZero)
            },
            filters = new
            {
                q = normalizedQuery ?? string.Empty,
                sellerId,
                status = normalizedStatus,
                statusOptions = BuildStatusOptions(),
                sellers = sellerOptions
            },
            lots = lots.Select(x => new
            {
                x.FreshInventoryLotId,
                x.ProductId,
                x.ProductName,
                x.Sku,
                x.SellerId,
                x.LotCode,
                x.TraceCode,
                x.FarmName,
                x.OriginRegion,
                x.HarvestedAt,
                x.PackedAt,
                x.ReceivedAt,
                x.ExpiresAt,
                x.InitialQuantity,
                x.RemainingQuantity,
                x.UnitCost,
                x.Status,
                x.QualityStatus,
                x.Notes,
                x.CreatedAt,
                daysToExpiry = x.ExpiresAt.HasValue ? (int?)Math.Ceiling((x.ExpiresAt.Value - now).TotalDays) : null
            }),
            recalls = recalls,
            fefoQueue = lots
                .Where(x => x.RemainingQuantity > 0 && x.ExpiresAt.HasValue)
                .OrderBy(x => x.ExpiresAt)
                .Take(10)
                .Select(x => new
                {
                    x.FreshInventoryLotId,
                    x.ProductName,
                    x.Sku,
                    x.SellerId,
                    x.LotCode,
                    x.RemainingQuantity,
                    x.ExpiresAt,
                    daysToExpiry = (int)Math.Ceiling((x.ExpiresAt!.Value - now).TotalDays),
                    x.QualityStatus
                })
        });
    }

    [HttpPost("lots")]
    public async Task<IActionResult> CreateLot([FromBody] FreshInventoryLotUpsertRequest request, CancellationToken cancellationToken)
    {
        if (!ModelState.IsValid)
        {
            return ValidationProblem(ModelState);
        }

        var validation = await ValidateLotAsync(request, cancellationToken);
        if (!string.IsNullOrWhiteSpace(validation))
        {
            return BadRequest(new { message = validation });
        }

        var entity = new FreshInventoryLot
        {
            ProductId = request.ProductId,
            SellerId = request.SellerId,
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

        return Ok(new { message = "Da tao fresh inventory lot.", freshInventoryLotId = entity.FreshInventoryLotId });
    }

    [HttpPost("recalls")]
    public async Task<IActionResult> CreateRecall([FromBody] FreshQualityRecallUpsertRequest request, CancellationToken cancellationToken)
    {
        if (!ModelState.IsValid)
        {
            return ValidationProblem(ModelState);
        }

        var validation = await ValidateRecallAsync(request, cancellationToken);
        if (!string.IsNullOrWhiteSpace(validation))
        {
            return BadRequest(new { message = validation });
        }

        var entity = new FreshQualityRecall
        {
            RecallCode = string.IsNullOrWhiteSpace(request.RecallCode)
                ? $"RECALL-{DateTime.UtcNow:yyyyMMddHHmmss}"
                : request.RecallCode.Trim().ToUpperInvariant(),
            ProductId = request.ProductId,
            FreshInventoryLotId = request.FreshInventoryLotId,
            SellerId = request.SellerId,
            RecallType = request.RecallType.Trim().ToLowerInvariant(),
            Severity = NormalizeSeverity(request.Severity),
            Status = NormalizeRecallStatus(request.Status),
            Title = request.Title.Trim(),
            Reason = NormalizeOptional(request.Reason, 2000),
            ActionRequired = NormalizeOptional(request.ActionRequired, 2000),
            StartedAt = request.StartedAt ?? DateTime.UtcNow,
            ResolvedAt = request.ResolvedAt,
            CreatedAt = DateTime.UtcNow
        };

        _db.FreshQualityRecalls.Add(entity);
        await _db.SaveChangesAsync(cancellationToken);

        return Ok(new { message = "Da tao quality recall case.", freshQualityRecallId = entity.FreshQualityRecallId });
    }

    [HttpPost("recalls/{id:int}/resolve")]
    public async Task<IActionResult> ResolveRecall([FromRoute] int id, CancellationToken cancellationToken)
    {
        var entity = await _db.FreshQualityRecalls.FirstOrDefaultAsync(x => x.FreshQualityRecallId == id, cancellationToken);
        if (entity is null)
        {
            return NotFound(new { message = "Khong tim thay quality recall." });
        }

        entity.Status = "resolved";
        entity.ResolvedAt = DateTime.UtcNow;
        entity.UpdatedAt = DateTime.UtcNow;

        await _db.SaveChangesAsync(cancellationToken);

        return Ok(new { message = "Da resolve quality recall." });
    }

    private async Task<string?> ValidateLotAsync(FreshInventoryLotUpsertRequest request, CancellationToken cancellationToken)
    {
        var productExists = await _db.Products
            .AsNoTracking()
            .AnyAsync(x => x.ProductId == request.ProductId, cancellationToken);
        if (!productExists)
        {
            return "San pham khong ton tai.";
        }

        var sellerOwnsProduct = await _db.SellerProducts
            .AsNoTracking()
            .AnyAsync(x => x.ProductId == request.ProductId && x.SellerId == request.SellerId && x.IsActive, cancellationToken);
        if (!sellerOwnsProduct)
        {
            return "Seller khong so huu san pham nay.";
        }

        var normalizedLotCode = request.LotCode.Trim();
        var duplicate = await _db.FreshInventoryLots
            .AsNoTracking()
            .AnyAsync(x => x.ProductId == request.ProductId && x.SellerId == request.SellerId && x.LotCode == normalizedLotCode, cancellationToken);
        if (duplicate)
        {
            return "Lot code da ton tai cho seller va san pham nay.";
        }

        if (request.ExpiresAt.HasValue && request.HarvestedAt.HasValue && request.ExpiresAt < request.HarvestedAt)
        {
            return "HSD khong the nho hon ngay thu hoach.";
        }

        if (request.RemainingQuantity > request.InitialQuantity)
        {
            return "Remaining quantity khong the lon hon initial quantity.";
        }

        return null;
    }

    private async Task<string?> ValidateRecallAsync(FreshQualityRecallUpsertRequest request, CancellationToken cancellationToken)
    {
        if (!request.ProductId.HasValue && !request.FreshInventoryLotId.HasValue)
        {
            return "Recall can gan vao san pham hoac lo hang.";
        }

        if (request.ProductId.HasValue)
        {
            var productExists = await _db.Products.AsNoTracking().AnyAsync(x => x.ProductId == request.ProductId.Value, cancellationToken);
            if (!productExists)
            {
                return "San pham cua recall khong ton tai.";
            }
        }

        if (request.FreshInventoryLotId.HasValue)
        {
            var lot = await _db.FreshInventoryLots
                .AsNoTracking()
                .FirstOrDefaultAsync(x => x.FreshInventoryLotId == request.FreshInventoryLotId.Value, cancellationToken);
            if (lot is null)
            {
                return "Lo hang cua recall khong ton tai.";
            }

            if (!request.SellerId.HasValue)
            {
                request.SellerId = lot.SellerId;
            }

            if (!request.ProductId.HasValue)
            {
                request.ProductId = lot.ProductId;
            }
        }

        if (!AllowedRecallStatuses.Contains(NormalizeRecallStatus(request.Status)))
        {
            return "Recall status khong hop le.";
        }

        if (!AllowedSeverities.Contains(NormalizeSeverity(request.Severity)))
        {
            return "Severity khong hop le.";
        }

        return null;
    }

    private static IEnumerable<object> BuildStatusOptions()
        => new[]
        {
            new { value = "all", text = "Tat ca" },
            new { value = "active", text = "Active lot" },
            new { value = "held", text = "Held" },
            new { value = "expired", text = "Expired" },
            new { value = "recalled", text = "Recalled" }
        };

    private static string NormalizeStatus(string? value)
        => string.IsNullOrWhiteSpace(value) ? "all" : value.Trim().ToLowerInvariant();

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

    private static string NormalizeRecallStatus(string? value)
    {
        var normalized = string.IsNullOrWhiteSpace(value) ? "open" : value.Trim().ToLowerInvariant();
        return AllowedRecallStatuses.Contains(normalized) ? normalized : "open";
    }

    private static string NormalizeSeverity(string? value)
    {
        var normalized = string.IsNullOrWhiteSpace(value) ? "medium" : value.Trim().ToLowerInvariant();
        return AllowedSeverities.Contains(normalized) ? normalized : "medium";
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
}
