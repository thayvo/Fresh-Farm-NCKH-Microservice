using FreshFarm.Catalog.Api.Models;
using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace FreshFarm.Catalog.Api.Controllers;

[ApiController]
[Route("api/admin/warehouse")]
[Authorize(Policy = "SellerOnly")]
public sealed class WarehouseAdminController : ControllerBase
{
    private static readonly object TransactionLock = new();
    private static readonly List<WarehouseTransactionStore> Transactions = new();
    private static int _nextTransactionId = 1;

    private readonly FreshFarmCatalogDBContext _db;

    public WarehouseAdminController(FreshFarmCatalogDBContext db)
    {
        _db = db;
    }

    [HttpGet("products")]
    public async Task<IActionResult> GetProducts(
        [FromQuery] string searchTerm = "",
        [FromQuery] string categoryFilter = "",
        [FromQuery] string stockStatusFilter = "",
        [FromQuery] int page = 1,
        [FromQuery] int pageSize = 10,
        CancellationToken cancellationToken = default)
    {
        if (page < 1)
        {
            page = 1;
        }

        if (pageSize <= 0)
        {
            pageSize = 10;
        }

        var query = ApplySellerScopeToProducts(_db.Products.AsNoTracking())
            .Include(p => p.Category)
            .AsQueryable();

        if (!string.IsNullOrWhiteSpace(searchTerm))
        {
            var term = searchTerm.Trim().ToLowerInvariant();
            query = query.Where(p =>
                p.ProductName.ToLower().Contains(term) ||
                p.Sku.ToLower().Contains(term));
        }

        if (!string.IsNullOrWhiteSpace(categoryFilter) && !string.Equals(categoryFilter, "all", StringComparison.OrdinalIgnoreCase))
        {
            var normalizedCategory = categoryFilter.Trim().ToLowerInvariant();
            query = query.Where(p => p.Category.CategoryName.ToLower() == normalizedCategory);
        }

        if (!string.IsNullOrWhiteSpace(stockStatusFilter))
        {
            switch (stockStatusFilter)
            {
                case "low-stock":
                    query = query.Where(p => (p.StockQuantity - p.ReservedStock) > 0 && (p.StockQuantity - p.ReservedStock) <= 10);
                    break;
                case "out-of-stock":
                    query = query.Where(p => (p.StockQuantity - p.ReservedStock) <= 0);
                    break;
                case "in-stock":
                    query = query.Where(p => (p.StockQuantity - p.ReservedStock) > 0);
                    break;
            }
        }

        var products = await query
            .OrderByDescending(p => p.CreatedDate)
            .Select(p => new
            {
                productId = p.ProductId,
                productName = p.ProductName,
                sku = p.Sku,
                categoryName = p.Category.CategoryName,
                imageFileName = p.ImageFileName,
                stockQuantity = p.StockQuantity,
                reservedStock = p.ReservedStock,
                availableStock = Math.Max(0, p.StockQuantity - p.ReservedStock),
                shelfQuantity = Math.Max(0, p.StockQuantity - p.ReservedStock),
                warehouseQuantity = 0,
                maxStock = Math.Max(1, Math.Max(0, p.StockQuantity - p.ReservedStock)),
                isManuallyDisabled = p.IsManuallyDisabled,
                importPrice = 0m,
                sellPrice = p.Price,
                supplierName = (string?)null,
                importDate = (DateTime?)null,
                expiryDate = (DateTime?)null,
                lastUpdatedDate = p.CreatedDate,
                notes = (string?)null,
                nearExpiryDays = p.NearExpiryDays ?? p.Category.NearExpiryDays ?? 7
            })
            .ToListAsync(cancellationToken);

        var totalItems = products.Count;
        var totalPages = (int)Math.Ceiling(totalItems / (double)pageSize);
        if (totalPages > 0 && page > totalPages)
        {
            page = totalPages;
        }

        var pageData = products
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .ToList();

        var lowStockCount = products.Count(p => p.availableStock > 0 && p.availableStock <= 10);
        var outOfStockCount = products.Count(p => p.availableStock == 0);

        return Ok(new
        {
            success = true,
            data = new
            {
                products = pageData,
                totalItems,
                totalPages,
                currentPage = page,
                pageSize,
                summary = new
                {
                    totalProducts = products.Count,
                    lowStockCount,
                    outOfStockCount,
                    totalWarehouseValue = products.Sum(p => p.stockQuantity * p.sellPrice)
                }
            }
        });
    }

    [HttpGet("products/{id:int}")]
    public async Task<IActionResult> GetProductDetails([FromRoute] int id, CancellationToken cancellationToken)
    {
        var product = await ApplySellerScopeToProducts(_db.Products)
            .AsNoTracking()
            .Include(p => p.Category)
            .FirstOrDefaultAsync(p => p.ProductId == id, cancellationToken);

        if (product is null)
        {
            return NotFound(new { message = "Khong tim thay san pham." });
        }

        return Ok(new
        {
            success = true,
            data = new
            {
                product = new
                {
                    warehouseID = 0,
                    productID = product.ProductId,
                    productName = product.ProductName,
                    sku = product.Sku,
                    categoryName = product.Category.CategoryName,
                    imageFileName = product.ImageFileName,
                    stockQuantity = product.StockQuantity,
                    reservedStock = product.ReservedStock,
                    availableStock = Math.Max(0, product.StockQuantity - product.ReservedStock),
                    shelfQuantity = Math.Max(0, product.StockQuantity - product.ReservedStock),
                    warehouseQuantity = 0,
                    maxStock = Math.Max(1, Math.Max(0, product.StockQuantity - product.ReservedStock)),
                    isManuallyDisabled = product.IsManuallyDisabled,
                    importPrice = 0m,
                    sellPrice = product.Price,
                    supplierName = (string?)null,
                    importDate = (DateTime?)null,
                    expiryDate = (DateTime?)null,
                    lastUpdatedDate = product.CreatedDate,
                    notes = (string?)null,
                    nearExpiryDays = product.NearExpiryDays ?? product.Category.NearExpiryDays ?? 7
                },
                history = Array.Empty<object>(),
                totalLots = 0,
                allLots = Array.Empty<object>()
            }
        });
    }

    [HttpGet("product-info/{id:int}")]
    public async Task<IActionResult> GetProductInfo([FromRoute] int id, CancellationToken cancellationToken)
    {
        var product = await ApplySellerScopeToProducts(_db.Products)
            .AsNoTracking()
            .Include(p => p.Category)
            .FirstOrDefaultAsync(p => p.ProductId == id, cancellationToken);

        if (product is null)
        {
            return NotFound(new { success = false, message = "Khong tim thay san pham." });
        }

        return Ok(new
        {
            success = true,
            nearExpiryDays = product.NearExpiryDays ?? product.Category.NearExpiryDays ?? 7
        });
    }

    [HttpPost("import-batch")]
    public async Task<IActionResult> ImportBatch([FromBody] List<ImportCartItemRequest>? items, CancellationToken cancellationToken)
    {
        var sellerId = TryGetSellerIdFromToken();
        if (!sellerId.HasValue)
        {
            return Unauthorized(new { success = false, message = "Khong xac dinh duoc seller." });
        }

        if (items is null || items.Count == 0)
        {
            return BadRequest(new { success = false, message = "Gio nhap trong" });
        }

        var transactionCode = "PN" + DateTime.UtcNow.ToString("yyyyMMddHHmmssfff");
        var detailRows = new List<WarehouseTransactionItemStore>();

        foreach (var item in items)
        {
            if (item.ProductId <= 0 || item.Quantity <= 0 || item.ImportPrice <= 0)
            {
                return BadRequest(new { success = false, message = "Du lieu nhap kho khong hop le." });
            }

            var product = await ApplySellerScopeToProducts(_db.Products)
                .Include(p => p.Category)
                .FirstOrDefaultAsync(p => p.ProductId == item.ProductId, cancellationToken);

            if (product is null)
            {
                return BadRequest(new { success = false, message = $"San pham #{item.ProductId} khong thuoc quyen quan ly cua ban." });
            }

            product.StockQuantity += item.Quantity;

            if (item.SellPrice.HasValue && item.SellPrice.Value > 0)
            {
                product.Price = item.SellPrice.Value;
            }

            if (item.NearExpiryDays.HasValue && item.NearExpiryDays.Value > 0)
            {
                product.NearExpiryDays = item.NearExpiryDays.Value;
            }

            product.IsManuallyDisabled = item.KeepDisabled;
            product.Status = (product.StockQuantity - product.ReservedStock) > 0 && !product.IsManuallyDisabled;

            detailRows.Add(new WarehouseTransactionItemStore
            {
                ProductId = product.ProductId,
                ProductName = product.ProductName,
                Sku = product.Sku,
                ImageFileName = product.ImageFileName ?? "no-image.png",
                Quantity = item.Quantity,
                UnitPrice = item.ImportPrice,
                Amount = item.Quantity * item.ImportPrice,
                BatchCode = string.Empty,
                ExpiryDate = ParseDateOrNull(item.ExpiryDate),
                Notes = item.Notes
            });
        }

        await _db.SaveChangesAsync(cancellationToken);

        AddTransaction(new WarehouseTransactionStore
        {
            SellerId = sellerId.Value,
            TransactionCode = transactionCode,
            TransactionType = "Import",
            TransactionDate = DateTime.UtcNow,
            TotalQuantity = detailRows.Sum(x => x.Quantity),
            TotalAmount = detailRows.Sum(x => x.Amount),
            Notes = $"Nhap {items.Count} loai san pham",
            CreatedByName = "Seller",
            CreatedAt = DateTime.UtcNow,
            Details = detailRows
        });

        return Ok(new
        {
            success = true,
            message = $"Nhap kho thanh cong {items.Count} san pham. Ma phieu: {transactionCode}"
        });
    }

    [HttpPost("export-batch")]
    public async Task<IActionResult> ExportBatch([FromBody] List<ExportCartItemRequest>? items, CancellationToken cancellationToken)
    {
        var sellerId = TryGetSellerIdFromToken();
        if (!sellerId.HasValue)
        {
            return Unauthorized(new { success = false, message = "Khong xac dinh duoc seller." });
        }

        if (items is null || items.Count == 0)
        {
            return BadRequest(new { success = false, message = "Gio xuat trong" });
        }

        var transactionCode = "PX" + DateTime.UtcNow.ToString("yyyyMMddHHmmssfff");
        var detailRows = new List<WarehouseTransactionItemStore>();

        foreach (var item in items)
        {
            if (item.ProductId <= 0 || item.Quantity <= 0)
            {
                return BadRequest(new { success = false, message = "Du lieu xuat kho khong hop le." });
            }

            var product = await ApplySellerScopeToProducts(_db.Products)
                .FirstOrDefaultAsync(p => p.ProductId == item.ProductId, cancellationToken);
            if (product is null)
            {
                return BadRequest(new { success = false, message = $"San pham #{item.ProductId} khong thuoc quyen quan ly cua ban." });
            }

            var availableStock = Math.Max(0, product.StockQuantity - product.ReservedStock);
            if (item.Quantity > availableStock)
            {
                return BadRequest(new { success = false, message = $"San pham '{product.ProductName}' chi con {availableStock} khả dụng." });
            }

            product.StockQuantity -= item.Quantity;
            product.Status = (product.StockQuantity - product.ReservedStock) > 0 && !product.IsManuallyDisabled;

            detailRows.Add(new WarehouseTransactionItemStore
            {
                ProductId = product.ProductId,
                ProductName = product.ProductName,
                Sku = product.Sku,
                ImageFileName = product.ImageFileName ?? "no-image.png",
                Quantity = item.Quantity,
                UnitPrice = product.Price,
                Amount = item.Quantity * product.Price,
                BatchCode = string.Empty,
                ExpiryDate = null,
                Notes = string.Join(". ", new[] { item.Reason?.Trim(), item.Notes?.Trim() }.Where(x => !string.IsNullOrWhiteSpace(x)))
            });
        }

        await _db.SaveChangesAsync(cancellationToken);

        AddTransaction(new WarehouseTransactionStore
        {
            SellerId = sellerId.Value,
            TransactionCode = transactionCode,
            TransactionType = "Export",
            TransactionDate = DateTime.UtcNow,
            TotalQuantity = detailRows.Sum(x => x.Quantity),
            TotalAmount = detailRows.Sum(x => x.Amount),
            Notes = $"Xuat {items.Count} loai san pham",
            CreatedByName = "Seller",
            CreatedAt = DateTime.UtcNow,
            Details = detailRows
        });

        return Ok(new
        {
            success = true,
            message = $"Xuat kho thanh cong {items.Count} san pham. Ma phieu: {transactionCode}"
        });
    }

    [HttpPost("export-expired")]
    public IActionResult ExportExpired()
    {
        return Ok(new
        {
            success = false,
            message = "He thong Catalog hien tai chua theo doi lo/HSD chi tiet de xuat het han tu dong."
        });
    }

    [HttpGet("transactions")]
    public IActionResult GetTransactions([FromQuery] string transactionType = "", [FromQuery] int page = 1, [FromQuery] int pageSize = 20)
    {
        var sellerId = TryGetSellerIdFromToken();
        if (!sellerId.HasValue)
        {
            return Unauthorized(new { success = false, message = "Khong xac dinh duoc seller." });
        }

        if (page < 1)
        {
            page = 1;
        }

        if (pageSize <= 0)
        {
            pageSize = 20;
        }

        List<WarehouseTransactionStore> snapshot;
        lock (TransactionLock)
        {
            snapshot = Transactions
                .Where(t => t.SellerId == sellerId.Value)
                .OrderByDescending(t => t.TransactionDate)
                .ToList();
        }

        if (!string.IsNullOrWhiteSpace(transactionType))
        {
            snapshot = snapshot
                .Where(t => string.Equals(t.TransactionType, transactionType, StringComparison.OrdinalIgnoreCase))
                .ToList();
        }

        var totalItems = snapshot.Count;
        var totalPages = (int)Math.Ceiling(totalItems / (double)pageSize);
        if (totalPages > 0 && page > totalPages)
        {
            page = totalPages;
        }

        var rows = snapshot
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .Select(t => new
            {
                transactionID = t.TransactionID,
                transactionCode = t.TransactionCode,
                transactionType = t.TransactionType,
                transactionDate = t.TransactionDate,
                totalQuantity = t.TotalQuantity,
                totalAmount = t.TotalAmount,
                notes = t.Notes,
                createdByName = t.CreatedByName,
                createdAt = t.CreatedAt
            })
            .ToList();

        return Ok(new
        {
            success = true,
            data = new
            {
                items = rows,
                totalItems,
                totalPages,
                currentPage = page,
                pageSize
            }
        });
    }

    [HttpGet("transactions/{id:int}")]
    public IActionResult GetTransactionDetails([FromRoute] int id)
    {
        var sellerId = TryGetSellerIdFromToken();
        if (!sellerId.HasValue)
        {
            return Unauthorized(new { success = false, message = "Khong xac dinh duoc seller." });
        }

        WarehouseTransactionStore? transaction;
        lock (TransactionLock)
        {
            transaction = Transactions.FirstOrDefault(t => t.TransactionID == id && t.SellerId == sellerId.Value);
        }

        if (transaction is null)
        {
            return NotFound(new { success = false, message = "Khong tim thay phieu." });
        }

        return Ok(new
        {
            success = true,
            data = new
            {
                transactionID = transaction.TransactionID,
                transactionCode = transaction.TransactionCode,
                transactionType = transaction.TransactionType,
                transactionDate = transaction.TransactionDate,
                totalQuantity = transaction.TotalQuantity,
                totalAmount = transaction.TotalAmount,
                notes = transaction.Notes,
                createdByName = transaction.CreatedByName,
                createdAt = transaction.CreatedAt,
                details = transaction.Details.Select(d => new
                {
                    detailID = d.DetailID,
                    productID = d.ProductId,
                    productName = d.ProductName,
                    sku = d.Sku,
                    imageFileName = d.ImageFileName,
                    quantity = d.Quantity,
                    unitPrice = d.UnitPrice,
                    amount = d.Amount,
                    batchCode = d.BatchCode,
                    expiryDate = d.ExpiryDate,
                    notes = d.Notes
                })
            }
        });
    }

    private static DateTime? ParseDateOrNull(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return null;
        }

        return DateTime.TryParse(value, out var parsed) ? parsed : null;
    }

    private static void AddTransaction(WarehouseTransactionStore transaction)
    {
        lock (TransactionLock)
        {
            transaction.TransactionID = _nextTransactionId++;

            var detailId = 1;
            foreach (var detail in transaction.Details)
            {
                detail.DetailID = detailId++;
            }

            Transactions.Add(transaction);
        }
    }

    public sealed class ImportCartItemRequest
    {
        public int ProductId { get; set; }

        public string? ProductName { get; set; }

        public int Quantity { get; set; }

        public decimal ImportPrice { get; set; }

        public decimal? SellPrice { get; set; }

        public string? SupplierName { get; set; }

        public string? ImportDate { get; set; }

        public string? ExpiryDate { get; set; }

        public int? NearExpiryDays { get; set; }

        public string? Notes { get; set; }

        public bool KeepDisabled { get; set; }
    }

    public sealed class ExportCartItemRequest
    {
        public int ProductId { get; set; }

        public string? ProductName { get; set; }

        public int Quantity { get; set; }

        public string? Reason { get; set; }

        public string? Notes { get; set; }
    }

    private sealed class WarehouseTransactionStore
    {
        public int SellerId { get; set; }

        public int TransactionID { get; set; }

        public string TransactionCode { get; set; } = string.Empty;

        public string TransactionType { get; set; } = string.Empty;

        public DateTime TransactionDate { get; set; }

        public int TotalQuantity { get; set; }

        public decimal TotalAmount { get; set; }

        public string? Notes { get; set; }

        public string? CreatedByName { get; set; }

        public DateTime CreatedAt { get; set; }

        public List<WarehouseTransactionItemStore> Details { get; set; } = new();
    }

    private int? TryGetSellerIdFromToken()
    {
        var sub = User.FindFirstValue(JwtRegisteredClaimNames.Sub)
                  ?? User.FindFirstValue(ClaimTypes.NameIdentifier)
                  ?? User.FindFirstValue("sub");

        return int.TryParse(sub, out var sellerId) ? sellerId : null;
    }

    private IQueryable<Product> ApplySellerScopeToProducts(IQueryable<Product> query)
    {
        var sellerId = TryGetSellerIdFromToken();
        if (!sellerId.HasValue)
        {
            return query.Where(_ => false);
        }

        return query.Where(p => p.SellerProducts.Any(sp => sp.SellerId == sellerId.Value && sp.IsActive));
    }

    private sealed class WarehouseTransactionItemStore
    {
        public int DetailID { get; set; }

        public int ProductId { get; set; }

        public string ProductName { get; set; } = string.Empty;

        public string Sku { get; set; } = string.Empty;

        public string ImageFileName { get; set; } = string.Empty;

        public int Quantity { get; set; }

        public decimal UnitPrice { get; set; }

        public decimal Amount { get; set; }

        public string? BatchCode { get; set; }

        public DateTime? ExpiryDate { get; set; }

        public string? Notes { get; set; }
    }
}
