using FreshFarm.Catalog.Api.Models;
using FreshFarm.Catalog.Api.Options;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace FreshFarm.Catalog.Api.Controllers;

[ApiController]
[ApiExplorerSettings(IgnoreApi = true)]
[Route("internal/inventory/reservations")]
public sealed class InternalInventoryReservationsController : ControllerBase
{
    private const string ServiceKeyHeaderName = "X-Service-Key";

    private readonly FreshFarmCatalogDBContext _db;
    private readonly InternalInventoryOptions _options;

    public InternalInventoryReservationsController(
        FreshFarmCatalogDBContext db,
        IOptions<InternalInventoryOptions> options)
    {
        _db = db;
        _options = options.Value;
    }

    [HttpGet("snapshots")]
    public async Task<IActionResult> GetSnapshots(
        [FromQuery] bool reservedOnly = false,
        CancellationToken cancellationToken = default)
    {
        var authFailure = EnsureAuthorized();
        if (authFailure is not null)
        {
            return authFailure;
        }

        var query = _db.Products.AsNoTracking().AsQueryable();
        if (reservedOnly)
        {
            query = query.Where(x => x.ReservedStock > 0);
        }

        var items = await query
            .OrderBy(x => x.ProductId)
            .Select(x => new InventorySnapshotResponse
            {
                ProductId = x.ProductId,
                OnHandStock = x.StockQuantity,
                ReservedStock = x.ReservedStock,
                AvailableStock = Math.Max(0, x.StockQuantity - x.ReservedStock)
            })
            .ToListAsync(cancellationToken);

        return Ok(new
        {
            success = true,
            items
        });
    }

    [HttpPost("reserve")]
    public async Task<IActionResult> Reserve(
        [FromBody] InventoryReservationMutationRequest request,
        CancellationToken cancellationToken)
    {
        var authFailure = EnsureAuthorized();
        if (authFailure is not null)
        {
            return authFailure;
        }

        var normalizedItems = NormalizeReserveItems(request.Items);
        if (normalizedItems.Count == 0)
        {
            return BadRequest(new { message = "Khong co san pham hop le de giu ton." });
        }

        var productIds = normalizedItems.Select(x => x.ProductId).Distinct().ToList();
        var sellerIds = normalizedItems.Select(x => x.SellerId).Distinct().ToList();

        var products = await _db.Products
            .Where(x => productIds.Contains(x.ProductId))
            .ToDictionaryAsync(x => x.ProductId, cancellationToken);

        var ownerships = await _db.SellerProducts
            .AsNoTracking()
            .Where(x => productIds.Contains(x.ProductId) &&
                        sellerIds.Contains(x.SellerId) &&
                        x.IsActive)
            .Select(x => new { x.ProductId, x.SellerId })
            .ToListAsync(cancellationToken);

        var ownershipSet = ownerships
            .Select(x => (x.ProductId, x.SellerId))
            .ToHashSet();

        foreach (var item in normalizedItems)
        {
            if (!products.TryGetValue(item.ProductId, out var product))
            {
                return NotFound(new { message = $"Khong tim thay san pham #{item.ProductId}." });
            }

            if (!ownershipSet.Contains((item.ProductId, item.SellerId)))
            {
                return Conflict(new { message = $"San pham #{item.ProductId} khong thuoc seller #{item.SellerId}." });
            }

            if (GetAvailableStock(product) < item.Quantity)
            {
                return Conflict(new
                {
                    message = $"San pham '{product.ProductName}' chi con {GetAvailableStock(product)}, khong du de giu {item.Quantity}."
                });
            }
        }

        await using var transaction = await _db.Database.BeginTransactionAsync(cancellationToken);
        foreach (var item in normalizedItems)
        {
            var product = products[item.ProductId];
            product.ReservedStock += item.Quantity;
            product.Status = GetAvailableStock(product) > 0 && !product.IsManuallyDisabled;
        }

        await _db.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);

        return Ok(new
        {
            success = true,
            reservedItems = normalizedItems.Count
        });
    }

    [HttpPost("release")]
    public async Task<IActionResult> Release(
        [FromBody] InventoryReservationMutationRequest request,
        CancellationToken cancellationToken)
    {
        var authFailure = EnsureAuthorized();
        if (authFailure is not null)
        {
            return authFailure;
        }

        var normalizedItems = NormalizeReleaseItems(request.Items);
        if (normalizedItems.Count == 0)
        {
            return Ok(new { success = true, releasedItems = 0 });
        }

        var productIds = normalizedItems.Select(x => x.ProductId).Distinct().ToList();
        var products = await _db.Products
            .Where(x => productIds.Contains(x.ProductId))
            .ToDictionaryAsync(x => x.ProductId, cancellationToken);

        await using var transaction = await _db.Database.BeginTransactionAsync(cancellationToken);
        foreach (var item in normalizedItems)
        {
            if (!products.TryGetValue(item.ProductId, out var product))
            {
                continue;
            }

            product.ReservedStock = Math.Max(0, product.ReservedStock - item.Quantity);
            product.Status = GetAvailableStock(product) > 0 && !product.IsManuallyDisabled;
        }

        await _db.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);

        return Ok(new
        {
            success = true,
            releasedItems = normalizedItems.Count
        });
    }

    [HttpPost("commit")]
    public async Task<IActionResult> Commit(
        [FromBody] InventoryReservationMutationRequest request,
        CancellationToken cancellationToken)
    {
        var authFailure = EnsureAuthorized();
        if (authFailure is not null)
        {
            return authFailure;
        }

        var normalizedItems = NormalizeReleaseItems(request.Items);
        if (normalizedItems.Count == 0)
        {
            return Ok(new { success = true, committedItems = 0 });
        }

        var productIds = normalizedItems.Select(x => x.ProductId).Distinct().ToList();
        var products = await _db.Products
            .Where(x => productIds.Contains(x.ProductId))
            .ToDictionaryAsync(x => x.ProductId, cancellationToken);

        await using var transaction = await _db.Database.BeginTransactionAsync(cancellationToken);
        foreach (var item in normalizedItems)
        {
            if (!products.TryGetValue(item.ProductId, out var product))
            {
                continue;
            }

            var quantity = Math.Min(item.Quantity, product.ReservedStock);
            product.ReservedStock = Math.Max(0, product.ReservedStock - quantity);
            product.StockQuantity = Math.Max(0, product.StockQuantity - quantity);
            product.Status = GetAvailableStock(product) > 0 && !product.IsManuallyDisabled;
        }

        await _db.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);

        return Ok(new
        {
            success = true,
            committedItems = normalizedItems.Count
        });
    }

    [HttpPost("consume")]
    public async Task<IActionResult> Consume(
        [FromBody] InventoryReservationMutationRequest request,
        CancellationToken cancellationToken)
    {
        var authFailure = EnsureAuthorized();
        if (authFailure is not null)
        {
            return authFailure;
        }

        var normalizedItems = NormalizeReleaseItems(request.Items);
        if (normalizedItems.Count == 0)
        {
            return Ok(new { success = true, consumedItems = 0 });
        }

        var productIds = normalizedItems.Select(x => x.ProductId).Distinct().ToList();
        var products = await _db.Products
            .Where(x => productIds.Contains(x.ProductId))
            .ToDictionaryAsync(x => x.ProductId, cancellationToken);

        foreach (var item in normalizedItems)
        {
            if (!products.TryGetValue(item.ProductId, out var product))
            {
                return NotFound(new { message = $"Khong tim thay san pham #{item.ProductId}." });
            }

            if (GetAvailableStock(product) < item.Quantity)
            {
                return Conflict(new
                {
                    message = $"San pham '{product.ProductName}' chi con {GetAvailableStock(product)}, khong du de tru {item.Quantity}."
                });
            }
        }

        await using var transaction = await _db.Database.BeginTransactionAsync(cancellationToken);
        foreach (var item in normalizedItems)
        {
            var product = products[item.ProductId];
            product.StockQuantity = Math.Max(0, product.StockQuantity - item.Quantity);
            product.Status = GetAvailableStock(product) > 0 && !product.IsManuallyDisabled;
        }

        await _db.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);

        return Ok(new
        {
            success = true,
            consumedItems = normalizedItems.Count
        });
    }

    [HttpPost("restock")]
    public async Task<IActionResult> Restock(
        [FromBody] InventoryReservationMutationRequest request,
        CancellationToken cancellationToken)
    {
        var authFailure = EnsureAuthorized();
        if (authFailure is not null)
        {
            return authFailure;
        }

        var normalizedItems = NormalizeReleaseItems(request.Items);
        if (normalizedItems.Count == 0)
        {
            return Ok(new { success = true, restockedItems = 0 });
        }

        var productIds = normalizedItems.Select(x => x.ProductId).Distinct().ToList();
        var products = await _db.Products
            .Where(x => productIds.Contains(x.ProductId))
            .ToDictionaryAsync(x => x.ProductId, cancellationToken);

        await using var transaction = await _db.Database.BeginTransactionAsync(cancellationToken);
        foreach (var item in normalizedItems)
        {
            if (!products.TryGetValue(item.ProductId, out var product))
            {
                continue;
            }

            product.StockQuantity += item.Quantity;
            product.Status = GetAvailableStock(product) > 0 && !product.IsManuallyDisabled;
        }

        await _db.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);

        return Ok(new
        {
            success = true,
            restockedItems = normalizedItems.Count
        });
    }

    [HttpPost("reconcile")]
    public async Task<IActionResult> Reconcile(
        [FromBody] InventoryReservationReconcileRequest request,
        CancellationToken cancellationToken)
    {
        var authFailure = EnsureAuthorized();
        if (authFailure is not null)
        {
            return authFailure;
        }

        var normalizedItems = (request.Items ?? new List<InventoryReservationReconcileItem>())
            .Where(x => x.ProductId > 0)
            .GroupBy(x => x.ProductId)
            .Select(g => new InventoryReservationReconcileItem
            {
                ProductId = g.Key,
                ExpectedReservedStock = Math.Max(0, g.OrderByDescending(x => x.ExpectedReservedStock).First().ExpectedReservedStock)
            })
            .ToList();

        if (normalizedItems.Count == 0)
        {
            return Ok(new { success = true, reconciledItems = 0 });
        }

        var productIds = normalizedItems.Select(x => x.ProductId).Distinct().ToList();
        var products = await _db.Products
            .Where(x => productIds.Contains(x.ProductId))
            .ToDictionaryAsync(x => x.ProductId, cancellationToken);

        var changed = new List<object>();
        await using var transaction = await _db.Database.BeginTransactionAsync(cancellationToken);
        foreach (var item in normalizedItems)
        {
            if (!products.TryGetValue(item.ProductId, out var product))
            {
                continue;
            }

            var nextReserved = Math.Min(Math.Max(0, item.ExpectedReservedStock), Math.Max(0, product.StockQuantity));
            if (product.ReservedStock == nextReserved)
            {
                continue;
            }

            changed.Add(new
            {
                productId = product.ProductId,
                oldReservedStock = product.ReservedStock,
                newReservedStock = nextReserved
            });

            product.ReservedStock = nextReserved;
            product.Status = GetAvailableStock(product) > 0 && !product.IsManuallyDisabled;
        }

        await _db.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);

        return Ok(new
        {
            success = true,
            reconciledItems = normalizedItems.Count,
            changed
        });
    }

    private IActionResult? EnsureAuthorized()
    {
        if (string.IsNullOrWhiteSpace(_options.ServiceKey))
        {
            return StatusCode(StatusCodes.Status500InternalServerError, new { message = "Internal inventory service key chua duoc cau hinh." });
        }

        if (!Request.Headers.TryGetValue(ServiceKeyHeaderName, out var providedKey) ||
            !string.Equals(providedKey.ToString(), _options.ServiceKey, StringComparison.Ordinal))
        {
            return Unauthorized(new { message = "Internal inventory authorization failed." });
        }

        return null;
    }

    private static List<ReserveInventoryMutationItem> NormalizeReserveItems(IEnumerable<InventoryReservationMutationItem>? items)
    {
        return (items ?? Array.Empty<InventoryReservationMutationItem>())
            .Where(x => x.ProductId > 0 && x.SellerId > 0 && x.Quantity > 0)
            .GroupBy(x => new { x.ProductId, x.SellerId })
            .Select(g => new ReserveInventoryMutationItem(
                g.Key.ProductId,
                g.Key.SellerId,
                g.Sum(x => x.Quantity)))
            .ToList();
    }

    private static List<ReleaseInventoryMutationItem> NormalizeReleaseItems(IEnumerable<InventoryReservationMutationItem>? items)
    {
        return (items ?? Array.Empty<InventoryReservationMutationItem>())
            .Where(x => x.ProductId > 0 && x.Quantity > 0)
            .GroupBy(x => x.ProductId)
            .Select(g => new ReleaseInventoryMutationItem(
                g.Key,
                g.Sum(x => x.Quantity)))
            .ToList();
    }

    private static int GetAvailableStock(Product product)
        => Math.Max(0, product.StockQuantity - product.ReservedStock);

    public sealed class InventoryReservationMutationRequest
    {
        public List<InventoryReservationMutationItem> Items { get; set; } = new();
    }

    public sealed class InventoryReservationMutationItem
    {
        public int ProductId { get; set; }

        public int SellerId { get; set; }

        public int Quantity { get; set; }
    }

    public sealed class InventoryReservationReconcileRequest
    {
        public List<InventoryReservationReconcileItem> Items { get; set; } = new();
    }

    public sealed class InventoryReservationReconcileItem
    {
        public int ProductId { get; set; }

        public int ExpectedReservedStock { get; set; }
    }

    public sealed class InventorySnapshotResponse
    {
        public int ProductId { get; set; }

        public int OnHandStock { get; set; }

        public int ReservedStock { get; set; }

        public int AvailableStock { get; set; }
    }

    private sealed record ReserveInventoryMutationItem(int ProductId, int SellerId, int Quantity);

    private sealed record ReleaseInventoryMutationItem(int ProductId, int Quantity);
}
