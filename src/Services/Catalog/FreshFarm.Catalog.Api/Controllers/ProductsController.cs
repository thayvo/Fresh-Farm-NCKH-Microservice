using FreshFarm.Catalog.Api.Dtos;
using FreshFarm.Catalog.Api.Models;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

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
    public async Task<IActionResult> Get([FromQuery] string? name)
    {
        var query = _db.Products
            .AsNoTracking()
            .Include(p => p.Category)
            .Include(p => p.Unit)
            .AsQueryable();

        if (!string.IsNullOrWhiteSpace(name))
        {
            var keyword = name.Trim();
            query = query.Where(p => p.ProductName.Contains(keyword));
        }

        var result = await query
            .OrderByDescending(p => p.CreatedDate)
            .Select(p => new
            {
                p.ProductId,
                p.ProductName,
                p.Sku,
                p.Price,
                p.Status,
                p.StockQuantity,
                p.ImageFileName,
                p.CreatedDate,
                p.ShortDescription,
                p.LongDescription,
                p.IsManuallyDisabled,
                p.CategoryId,
                CategoryName = p.Category.CategoryName,
                UnitId = p.UnitId,
                UnitName = p.Unit.UnitName,
                UnitSymbol = p.Unit.Symbol
            })
            .ToListAsync();

        return Ok(result);
    }

    [HttpGet("{id:int}")]
    public async Task<IActionResult> GetById([FromRoute] int id)
    {
        var product = await _db.Products
            .AsNoTracking()
            .Include(p => p.Category)
            .Include(p => p.Unit)
            .Include(p => p.ProductInfos)
            .FirstOrDefaultAsync(p => p.ProductId == id);

        if (product is null)
        {
            return NotFound(new { message = "Không tìm thấy sản phẩm." });
        }

        return Ok(new
        {
            product.ProductId,
            product.ProductName,
            product.Sku,
            product.Price,
            product.Status,
            product.StockQuantity,
            product.ImageFileName,
            product.CreatedDate,
            product.ShortDescription,
            product.LongDescription,
            product.IsManuallyDisabled,
            product.CategoryId,
            CategoryName = product.Category.CategoryName,
            UnitId = product.UnitId,
            UnitName = product.Unit.UnitName,
            UnitSymbol = product.Unit.Symbol,
            ProductInfos = product.ProductInfos.Select(info => new
            {
                info.Weight,
                info.Origin,
                info.Standard,
                info.Preservation
            })
        });
    }

    [HttpPost]
    [Authorize(Policy = "SellerOnly")]
    public async Task<IActionResult> Create([FromBody] ProductUpsertRequest request)
    {
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
        }
        catch (DbUpdateException ex)
        {
            return BadRequest(new { message = "Không thể tạo sản phẩm.", detail = ex.InnerException?.Message ?? ex.Message });
        }

        return CreatedAtAction(nameof(GetById), new { id = product.ProductId }, new { product.ProductId });
    }

    [HttpPut("{id:int}")]
    [Authorize(Policy = "SellerOnly")]
    public async Task<IActionResult> Update([FromRoute] int id, [FromBody] ProductUpsertRequest request)
    {
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
    [Authorize(Policy = "SellerOnly")]
    public async Task<IActionResult> Delete([FromRoute] int id)
    {
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
    [Authorize(Policy = "SellerOnly")]
    public async Task<IActionResult> ToggleStatus([FromRoute] int id)
    {
        var product = await _db.Products.FirstOrDefaultAsync(p => p.ProductId == id);
        if (product is null)
        {
            return NotFound(new { message = "Không tìm thấy sản phẩm." });
        }

        if (product.StockQuantity <= 0 && !product.Status)
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
}
