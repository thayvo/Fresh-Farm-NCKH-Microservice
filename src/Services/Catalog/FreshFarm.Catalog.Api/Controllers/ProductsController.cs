using FreshFarm.Catalog.Api.Dtos;
using FreshFarm.Catalog.Api.Models;
using Microsoft.Data.SqlClient;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;

namespace FreshFarm.Catalog.Api.Controllers;

[ApiController]
[Route("api/[controller]")]
public sealed class ProductsController : ControllerBase
{
    private readonly FreshFarmCatalogDBContext _db;

    public ProductsController(FreshFarmCatalogDBContext db)
    {
        _db = db;
    }

    [HttpGet]
    public async Task<IActionResult> Get(
        [FromQuery] string? name,
        [FromQuery] int? sellerId = null,
        [FromQuery] int[]? categoryIds = null,
        [FromQuery] string[]? origins = null,
        [FromQuery] string[]? standards = null,
        [FromQuery] string[]? units = null)
    {
        var currentSellerId = TryGetCurrentSellerId();
        try
        {
            var result = await BuildPublicProductListQuery(
                    currentSellerId,
                    name,
                    sellerId,
                    categoryIds,
                    origins,
                    standards,
                    units,
                    includeAttributes: true)
                .ToListAsync();
            return Ok(result);
        }
        catch (SqlException ex) when (IsMissingProductAttributeValueTable(ex))
        {
            var fallbackResult = await BuildPublicProductListQuery(
                    currentSellerId,
                    name,
                    sellerId,
                    categoryIds,
                    origins,
                    standards,
                    units,
                    includeAttributes: false)
                .ToListAsync();
            return Ok(fallbackResult);
        }
    }

    [HttpGet("{id:int}")]
    public async Task<IActionResult> GetById([FromRoute] int id)
    {
        var sellerId = TryGetCurrentSellerId();
        if (sellerId.HasValue)
        {
            var owned = await _db.SellerProducts
                .AsNoTracking()
                .AnyAsync(sp => sp.ProductId == id && sp.SellerId == sellerId.Value && sp.IsActive);

            if (!owned)
            {
                return NotFound(new { message = "Không tìm thấy sản phẩm." });
            }
        }

        try
        {
            var product = await BuildPublicProductDetailQuery(id, includeAttributes: true).SingleOrDefaultAsync();
            if (product is null)
            {
                return NotFound(new { message = "Không tìm thấy sản phẩm." });
            }

            return Ok(product);
        }
        catch (SqlException ex) when (IsMissingProductAttributeValueTable(ex))
        {
            var fallbackProduct = await BuildPublicProductDetailQuery(id, includeAttributes: false).SingleOrDefaultAsync();
            if (fallbackProduct is null)
            {
                return NotFound(new { message = "Không tìm thấy sản phẩm." });
            }

            return Ok(fallbackProduct);
        }
    }

    private IQueryable<ProductPublicListDto> BuildPublicProductListQuery(
        int? currentSellerId,
        string? name,
        int? sellerId,
        int[]? categoryIds,
        string[]? origins,
        string[]? standards,
        string[]? units,
        bool includeAttributes)
    {
        var query = _db.Products
            .AsNoTracking()
            .Include(p => p.Category)
            .Include(p => p.Unit)
            .Include(p => p.ProductInfos)
            .AsQueryable();

        if (includeAttributes)
        {
            query = query.Include(p => p.ProductAttributeValues)
                .ThenInclude(value => value.CategoryAttribute);
        }

        if (currentSellerId.HasValue)
        {
            var ownedProductIds = _db.SellerProducts
                .AsNoTracking()
                .Where(sp => sp.SellerId == currentSellerId.Value && sp.IsActive)
                .Select(sp => sp.ProductId);

            query = query.Where(p => ownedProductIds.Contains(p.ProductId));
        }
        else
        {
            query = query.Where(p => p.Status && !p.IsManuallyDisabled);

            if (sellerId.HasValue && sellerId.Value > 0)
            {
                var publicSellerProductIds = _db.SellerProducts
                    .AsNoTracking()
                    .Where(sp => sp.SellerId == sellerId.Value && sp.IsActive)
                    .Select(sp => sp.ProductId);

                query = query.Where(p => publicSellerProductIds.Contains(p.ProductId));
            }
        }

        if (!string.IsNullOrWhiteSpace(name))
        {
            var keyword = name.Trim();
            query = query.Where(p =>
                p.ProductName.Contains(keyword) ||
                p.Category.CategoryName.Contains(keyword) ||
                p.ProductInfos.Any(info =>
                    (info.Origin != null && info.Origin.Contains(keyword)) ||
                    (info.Standard != null && info.Standard.Contains(keyword))));
        }

        var normalizedCategoryIds = (categoryIds ?? Array.Empty<int>())
            .Where(idValue => idValue > 0)
            .Distinct()
            .ToArray();

        if (normalizedCategoryIds.Length > 0)
        {
            query = query.Where(p => normalizedCategoryIds.Contains(p.CategoryId));
        }

        var normalizedOrigins = (origins ?? Array.Empty<string>())
            .Where(value => !string.IsNullOrWhiteSpace(value))
            .Select(value => value.Trim().ToLower())
            .Distinct()
            .ToArray();

        if (normalizedOrigins.Length > 0)
        {
            query = query.Where(p => p.ProductInfos.Any(info =>
                info.Origin != null && normalizedOrigins.Contains(info.Origin.ToLower())));
        }

        var normalizedStandards = (standards ?? Array.Empty<string>())
            .Where(value => !string.IsNullOrWhiteSpace(value))
            .Select(value => value.Trim().ToLower())
            .Distinct()
            .ToArray();

        if (normalizedStandards.Length > 0)
        {
            query = query.Where(p => p.ProductInfos.Any(info =>
                info.Standard != null && normalizedStandards.Contains(info.Standard.ToLower())));
        }

        var normalizedUnits = (units ?? Array.Empty<string>())
            .Where(value => !string.IsNullOrWhiteSpace(value))
            .Select(value => value.Trim().ToLower())
            .Distinct()
            .ToArray();

        if (normalizedUnits.Length > 0)
        {
            query = query.Where(p => normalizedUnits.Contains(p.Unit.UnitName.ToLower()));
        }

        return query
            .OrderByDescending(p => p.CreatedDate)
            .Select(p => new ProductPublicListDto
            {
                ProductId = p.ProductId,
                ProductName = p.ProductName,
                Sku = p.Sku,
                Price = p.Price,
                Status = p.Status,
                StockQuantity = p.StockQuantity,
                ReservedStock = p.ReservedStock,
                AvailableStock = Math.Max(0, p.StockQuantity - p.ReservedStock),
                OnHandStock = p.StockQuantity,
                ImageFileName = p.ImageFileName,
                CreatedDate = p.CreatedDate,
                ShortDescription = p.ShortDescription,
                LongDescription = p.LongDescription,
                IsManuallyDisabled = p.IsManuallyDisabled,
                CategoryId = p.CategoryId,
                CategoryName = p.Category.CategoryName,
                UnitId = p.UnitId,
                UnitName = p.Unit.UnitName,
                UnitSymbol = p.Unit.Symbol,
                Origin = p.ProductInfos.OrderBy(info => info.InfoId).Select(info => info.Origin).FirstOrDefault(),
                Standard = p.ProductInfos.OrderBy(info => info.InfoId).Select(info => info.Standard).FirstOrDefault(),
                Preservation = p.ProductInfos.OrderBy(info => info.InfoId).Select(info => info.Preservation).FirstOrDefault(),
                Weight = p.ProductInfos.OrderBy(info => info.InfoId).Select(info => info.Weight).FirstOrDefault(),
                ProductAttributes = includeAttributes
                    ? p.ProductAttributeValues
                        .Where(value => value.CategoryAttribute.IsActive)
                        .OrderBy(value => value.CategoryAttribute.SortOrder)
                        .ThenBy(value => value.ProductAttributeValueId)
                        .Select(value => new ProductAttributePublicDto
                        {
                            CategoryAttributeId = value.CategoryAttributeId,
                            AttributeKey = value.CategoryAttribute.AttributeKey,
                            DisplayName = value.CategoryAttribute.DisplayName,
                            ValueText = value.ValueText,
                            NormalizedValue = value.NormalizedValue
                        })
                        .ToList()
                    : new List<ProductAttributePublicDto>(),
                PrimarySellerId = _db.SellerProducts
                    .Where(sp => sp.ProductId == p.ProductId && sp.IsActive)
                    .OrderBy(sp => sp.CreatedAt)
                    .Select(sp => (int?)sp.SellerId)
                    .FirstOrDefault()
            });
    }

    private IQueryable<ProductPublicDetailDto> BuildPublicProductDetailQuery(int id, bool includeAttributes)
    {
        var query = _db.Products
            .AsNoTracking()
            .Include(p => p.Category)
            .Include(p => p.Unit)
            .Include(p => p.ProductInfos)
            .Where(p => p.ProductId == id)
            .AsQueryable();

        if (includeAttributes)
        {
            query = query.Include(p => p.ProductAttributeValues)
                .ThenInclude(value => value.CategoryAttribute);
        }

        return query.Select(product => new ProductPublicDetailDto
        {
            ProductId = product.ProductId,
            ProductName = product.ProductName,
            Sku = product.Sku,
            Price = product.Price,
            Status = product.Status,
            StockQuantity = product.StockQuantity,
            ReservedStock = product.ReservedStock,
            AvailableStock = Math.Max(0, product.StockQuantity - product.ReservedStock),
            OnHandStock = product.StockQuantity,
            ImageFileName = product.ImageFileName,
            CreatedDate = product.CreatedDate,
            ShortDescription = product.ShortDescription,
            LongDescription = product.LongDescription,
            IsManuallyDisabled = product.IsManuallyDisabled,
            CategoryId = product.CategoryId,
            CategoryName = product.Category.CategoryName,
            UnitId = product.UnitId,
            UnitName = product.Unit.UnitName,
            UnitSymbol = product.Unit.Symbol,
            PrimarySellerId = _db.SellerProducts
                .AsNoTracking()
                .Where(sp => sp.ProductId == product.ProductId && sp.IsActive)
                .OrderBy(sp => sp.CreatedAt)
                .Select(sp => (int?)sp.SellerId)
                .FirstOrDefault(),
            ProductInfos = product.ProductInfos.Select(info => new ProductInfoPublicDto
            {
                Weight = info.Weight,
                Origin = info.Origin,
                Standard = info.Standard,
                Preservation = info.Preservation
            }).ToList(),
            ProductAttributes = includeAttributes
                ? product.ProductAttributeValues
                    .Where(value => value.CategoryAttribute.IsActive)
                    .OrderBy(value => value.CategoryAttribute.SortOrder)
                    .ThenBy(value => value.ProductAttributeValueId)
                    .Select(value => new ProductAttributePublicDto
                    {
                        CategoryAttributeId = value.CategoryAttributeId,
                        AttributeKey = value.CategoryAttribute.AttributeKey,
                        DisplayName = value.CategoryAttribute.DisplayName,
                        ValueText = value.ValueText,
                        NormalizedValue = value.NormalizedValue
                    })
                    .ToList()
                : new List<ProductAttributePublicDto>()
        });
    }

    private static bool IsMissingProductAttributeValueTable(SqlException ex)
        => ex.Number == 208 && ex.Message.Contains("ProductAttributeValue", StringComparison.OrdinalIgnoreCase);

    private class ProductPublicListDto
    {
        public int ProductId { get; set; }
        public string ProductName { get; set; } = string.Empty;
        public string Sku { get; set; } = string.Empty;
        public decimal Price { get; set; }
        public bool Status { get; set; }
        public int StockQuantity { get; set; }
        public int ReservedStock { get; set; }
        public int AvailableStock { get; set; }
        public int OnHandStock { get; set; }
        public string? ImageFileName { get; set; }
        public DateTime CreatedDate { get; set; }
        public string? ShortDescription { get; set; }
        public string? LongDescription { get; set; }
        public bool IsManuallyDisabled { get; set; }
        public int CategoryId { get; set; }
        public string CategoryName { get; set; } = string.Empty;
        public int UnitId { get; set; }
        public string UnitName { get; set; } = string.Empty;
        public string? UnitSymbol { get; set; }
        public string? Origin { get; set; }
        public string? Standard { get; set; }
        public string? Preservation { get; set; }
        public string? Weight { get; set; }
        public List<ProductAttributePublicDto> ProductAttributes { get; set; } = new();
        public int? PrimarySellerId { get; set; }
    }

    private sealed class ProductPublicDetailDto : ProductPublicListDto
    {
        public List<ProductInfoPublicDto> ProductInfos { get; set; } = new();
    }

    private sealed class ProductInfoPublicDto
    {
        public string? Weight { get; set; }
        public string? Origin { get; set; }
        public string? Standard { get; set; }
        public string? Preservation { get; set; }
    }

    private sealed class ProductAttributePublicDto
    {
        public int CategoryAttributeId { get; set; }
        public string? AttributeKey { get; set; }
        public string? DisplayName { get; set; }
        public string? ValueText { get; set; }
        public string? NormalizedValue { get; set; }
    }

    [HttpPost]
    [Authorize(Policy = "SellerOrAdmin")]
    public async Task<IActionResult> Create([FromBody] ProductUpsertRequest request)
    {
        var isAdmin = User.IsInRole("Admin");
        var sellerId = TryGetCurrentSellerId();
        if (!isAdmin && !sellerId.HasValue)
        {
            return Unauthorized(new { message = "Không xác định được seller từ token." });
        }

        if (!ModelState.IsValid)
        {
            return ValidationProblem(ModelState);
        }

        var productName = request.ProductName.Trim();
        if (string.IsNullOrWhiteSpace(productName))
        {
            return BadRequest(new { message = "Tên sản phẩm không hợp lệ." });
        }

        var category = await _db.Categories
            .AsNoTracking()
            .FirstOrDefaultAsync(c => c.CategoryId == request.CategoryId && c.IsActive);
        if (category is null)
        {
            return BadRequest(new { message = "Danh mục không hợp lệ hoặc đã bị ẩn." });
        }

        var unit = await _db.Units
            .AsNoTracking()
            .FirstOrDefaultAsync(u => u.UnitId == request.UnitId && u.IsActive);
        if (unit is null)
        {
            return BadRequest(new { message = "Đơn vị tính không hợp lệ hoặc đã bị ẩn." });
        }

        var duplicateName = await _db.Products
            .AnyAsync(p => p.ProductName.ToLower() == productName.ToLower());
        if (duplicateName)
        {
            return BadRequest(new { message = "Tên sản phẩm đã tồn tại." });
        }

        var sku = string.IsNullOrWhiteSpace(request.Sku)
            ? await GenerateSkuAsync(request.CategoryId)
            : request.Sku.Trim();

        var duplicateSku = await _db.Products
            .AnyAsync(p => p.Sku.ToLower() == sku.ToLower());
        if (duplicateSku)
        {
            return BadRequest(new { message = "Mã SKU đã tồn tại." });
        }

        var product = new Product
        {
            CategoryId = request.CategoryId,
            UnitId = request.UnitId,
            ProductName = productName,
            Price = request.Price,
            Sku = sku,
            Status = request.Status,
            StockQuantity = 0,
            ReservedStock = 0,
            ImageFileName = request.ImageFileName,
            ShortDescription = (request.ShortDescription ?? string.Empty).Trim(),
            LongDescription = (request.LongDescription ?? string.Empty).Trim(),
            IsManuallyDisabled = !request.Status,
            CreatedDate = DateTime.UtcNow
        };

        _db.Products.Add(product);

        try
        {
            await _db.SaveChangesAsync();

            if (!isAdmin && sellerId.HasValue)
            {
                _db.SellerProducts.Add(new SellerProduct
                {
                    SellerId = sellerId.Value,
                    ProductId = product.ProductId,
                    IsActive = true,
                    CreatedAt = DateTime.UtcNow
                });

                await _db.SaveChangesAsync();
            }
        }
        catch (DbUpdateException ex)
        {
            return BadRequest(new { message = "Không thể tạo sản phẩm.", detail = ex.InnerException?.Message ?? ex.Message });
        }

        return CreatedAtAction(nameof(GetById), new { id = product.ProductId }, new { product.ProductId });
    }

    [HttpPut("{id:int}")]
    [Authorize(Policy = "SellerOrAdmin")]
    public async Task<IActionResult> Update([FromRoute] int id, [FromBody] ProductUpsertRequest request)
    {
        var isAdmin = User.IsInRole("Admin");
        var sellerId = TryGetCurrentSellerId();
        if (!isAdmin && !sellerId.HasValue)
        {
            return Unauthorized(new { message = "Không xác định được seller từ token." });
        }

        if (!isAdmin)
        {
            var sellerScopeId = sellerId!.Value;
            var owned = await _db.SellerProducts
                .AsNoTracking()
                .AnyAsync(sp => sp.ProductId == id && sp.SellerId == sellerScopeId && sp.IsActive);
            if (!owned)
            {
                return NotFound(new { message = "Không tìm thấy sản phẩm." });
            }
        }

        if (!ModelState.IsValid)
        {
            return ValidationProblem(ModelState);
        }

        var existing = await _db.Products.FirstOrDefaultAsync(p => p.ProductId == id);
        if (existing is null)
        {
            return NotFound(new { message = "Không tìm thấy sản phẩm." });
        }

        var productName = request.ProductName.Trim();
        if (string.IsNullOrWhiteSpace(productName))
        {
            return BadRequest(new { message = "Tên sản phẩm không hợp lệ." });
        }

        var category = await _db.Categories
            .AsNoTracking()
            .FirstOrDefaultAsync(c => c.CategoryId == request.CategoryId && c.IsActive);
        if (category is null)
        {
            return BadRequest(new { message = "Danh mục không hợp lệ hoặc đã bị ẩn." });
        }

        var unit = await _db.Units
            .AsNoTracking()
            .FirstOrDefaultAsync(u => u.UnitId == request.UnitId && u.IsActive);
        if (unit is null)
        {
            return BadRequest(new { message = "Đơn vị tính không hợp lệ hoặc đã bị ẩn." });
        }

        var duplicateName = await _db.Products
            .AnyAsync(p => p.ProductId != id && p.ProductName.ToLower() == productName.ToLower());
        if (duplicateName)
        {
            return BadRequest(new { message = "Tên sản phẩm đã tồn tại." });
        }

        var normalizedSku = string.IsNullOrWhiteSpace(request.Sku)
            ? existing.Sku
            : request.Sku.Trim();

        if (string.IsNullOrWhiteSpace(normalizedSku))
        {
            normalizedSku = await GenerateSkuAsync(request.CategoryId);
        }

        var duplicateSku = await _db.Products
            .AnyAsync(p => p.ProductId != id && p.Sku.ToLower() == normalizedSku.ToLower());
        if (duplicateSku)
        {
            return BadRequest(new { message = "Mã SKU đã tồn tại." });
        }

        existing.CategoryId = request.CategoryId;
        existing.UnitId = request.UnitId;
        existing.ProductName = productName;
        existing.Price = request.Price;
        existing.Sku = normalizedSku;
        existing.ShortDescription = (request.ShortDescription ?? string.Empty).Trim();
        existing.LongDescription = (request.LongDescription ?? string.Empty).Trim();
        existing.Status = request.Status;
        existing.IsManuallyDisabled = !request.Status;
        existing.ImageFileName = request.ImageFileName;

        try
        {
            await _db.SaveChangesAsync();
        }
        catch (DbUpdateException ex)
        {
            return BadRequest(new { message = "Không thể cập nhật sản phẩm.", detail = ex.InnerException?.Message ?? ex.Message });
        }

        return Ok(new { message = "Cập nhật sản phẩm thành công." });
    }

    [HttpDelete("{id:int}")]
    [Authorize(Policy = "SellerOrAdmin")]
    public async Task<IActionResult> Delete([FromRoute] int id)
    {
        var isAdmin = User.IsInRole("Admin");
        var sellerId = TryGetCurrentSellerId();
        if (!isAdmin && !sellerId.HasValue)
        {
            return Unauthorized(new { message = "Không xác định được seller từ token." });
        }

        SellerProduct? ownership = null;
        if (!isAdmin)
        {
            var sellerScopeId = sellerId!.Value;
            ownership = await _db.SellerProducts
                .FirstOrDefaultAsync(sp => sp.ProductId == id && sp.SellerId == sellerScopeId && sp.IsActive);
            if (ownership is null)
            {
                return NotFound(new { message = "Không tìm thấy sản phẩm." });
            }
        }

        if (!isAdmin)
        {
            // Multi-seller: nếu product đang được nhiều seller dùng, chỉ gỡ ownership của seller hiện tại.
            var activeOwnerCount = await _db.SellerProducts
                .CountAsync(sp => sp.ProductId == id && sp.IsActive);
            if (activeOwnerCount > 1)
            {
                ownership!.IsActive = false;
                ownership.UpdatedAt = DateTime.UtcNow;
                await _db.SaveChangesAsync();
                return Ok(new { message = "Đã gỡ sản phẩm khỏi danh sách của seller hiện tại." });
            }
        }

        var product = await _db.Products
            .Include(p => p.ProductInfos)
            .Include(p => p.ProductImages)
            .FirstOrDefaultAsync(p => p.ProductId == id);

        if (product is null)
        {
            return NotFound(new { message = "Không tìm thấy sản phẩm." });
        }

        if (product.ProductInfos.Count > 0)
        {
            _db.ProductInfos.RemoveRange(product.ProductInfos);
        }

        if (product.ProductImages.Count > 0)
        {
            _db.ProductImages.RemoveRange(product.ProductImages);
        }

        var ownershipRows = await _db.SellerProducts.Where(sp => sp.ProductId == id).ToListAsync();
        if (ownershipRows.Count > 0)
        {
            _db.SellerProducts.RemoveRange(ownershipRows);
        }

        _db.Products.Remove(product);

        try
        {
            await _db.SaveChangesAsync();
        }
        catch (DbUpdateException ex)
        {
            return BadRequest(new { message = "Không thể xóa sản phẩm.", detail = ex.InnerException?.Message ?? ex.Message });
        }

        return Ok(new { message = "Xóa sản phẩm thành công." });
    }

    [HttpPost("{id:int}/toggle-status")]
    [Authorize(Policy = "SellerOrAdmin")]
    public async Task<IActionResult> ToggleStatus([FromRoute] int id)
    {
        var isAdmin = User.IsInRole("Admin");
        var sellerId = TryGetCurrentSellerId();
        if (!isAdmin && !sellerId.HasValue)
        {
            return Unauthorized(new { message = "Không xác định được seller từ token." });
        }

        if (!isAdmin)
        {
            var sellerScopeId = sellerId!.Value;
            var owned = await _db.SellerProducts
                .AsNoTracking()
                .AnyAsync(sp => sp.ProductId == id && sp.SellerId == sellerScopeId && sp.IsActive);
            if (!owned)
            {
                return NotFound(new { message = "Không tìm thấy sản phẩm." });
            }
        }

        var product = await _db.Products.FirstOrDefaultAsync(p => p.ProductId == id);
        if (product is null)
        {
            return NotFound(new { message = "Không tìm thấy sản phẩm." });
        }

        if ((product.StockQuantity - product.ReservedStock) <= 0 && !product.Status)
        {
            return BadRequest(new { message = "Sản phẩm đã hết hàng, không thể bật bán." });
        }

        product.Status = !product.Status;
        product.IsManuallyDisabled = !product.Status;

        await _db.SaveChangesAsync();

        return Ok(new
        {
            message = product.Status ? "Đã bật sản phẩm" : "Đã ẩn sản phẩm",
            newStatus = product.Status
        });
    }

    private async Task<string> GenerateSkuAsync(int categoryId)
    {
        var count = await _db.Products.CountAsync(p => p.CategoryId == categoryId) + 1;
        return $"PRD-{categoryId:D3}-{count:D4}";
    }

    private int? TryGetCurrentSellerId()
    {
        if (User?.Identity?.IsAuthenticated != true || !User.IsInRole("Seller"))
        {
            return null;
        }

        var raw = User.FindFirstValue(JwtRegisteredClaimNames.Sub)
                  ?? User.FindFirstValue(ClaimTypes.NameIdentifier)
                  ?? User.FindFirstValue("sub");

        return int.TryParse(raw, out var sellerId) ? sellerId : null;
    }
}
