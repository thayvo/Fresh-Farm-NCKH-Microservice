using FreshFarm.Catalog.Api.Dtos;
using FreshFarm.Catalog.Api.Models;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using System.Text.RegularExpressions;

namespace FreshFarm.Catalog.Api.Controllers;

[ApiController]
[Route("api/[controller]")]
public sealed class CategoriesController : ControllerBase
{
    private readonly FreshFarmCatalogDBContext _db;

    public CategoriesController(FreshFarmCatalogDBContext db)
    {
        _db = db;
    }

    [HttpGet]
    public async Task<IActionResult> Get([FromQuery] string? search)
    {
        var query = _db.Categories.AsNoTracking().AsQueryable();

        if (!string.IsNullOrWhiteSpace(search))
        {
            var keyword = search.Trim();
            query = query.Where(c =>
                c.CategoryName.Contains(keyword) ||
                (c.Description != null && c.Description.Contains(keyword)) ||
                (c.Slug != null && c.Slug.Contains(keyword)));
        }

        var categories = await query
            .OrderByDescending(c => c.CreatedDate)
            .Select(c => new
            {
                c.CategoryId,
                c.CategoryName,
                c.Description,
                c.ImageCategoriesName,
                c.IsActive,
                c.Slug,
                c.CreatedDate,
                c.UpdatedDate
            })
            .ToListAsync();

        return Ok(categories);
    }

    [HttpGet("{id:int}")]
    public async Task<IActionResult> GetById([FromRoute] int id)
    {
        var category = await _db.Categories
            .AsNoTracking()
            .Where(c => c.CategoryId == id)
            .Select(c => new
            {
                c.CategoryId,
                c.CategoryName,
                c.Description,
                c.ImageCategoriesName,
                c.IsActive,
                c.Slug,
                c.CreatedDate,
                c.UpdatedDate
            })
            .FirstOrDefaultAsync();

        if (category is null)
        {
            return NotFound(new { message = "Không tìm thấy danh mục." });
        }

        return Ok(category);
    }

    [HttpGet("slug/{slug}")]
    public async Task<IActionResult> GetBySlug([FromRoute] string slug)
    {
        var category = await _db.Categories
            .AsNoTracking()
            .Where(c => c.Slug == slug)
            .Select(c => new
            {
                c.CategoryId,
                c.CategoryName,
                c.Description,
                c.ImageCategoriesName,
                c.IsActive,
                c.Slug,
                c.CreatedDate,
                c.UpdatedDate
            })
            .FirstOrDefaultAsync();

        if (category is null)
        {
            return NotFound(new { message = "Không tìm thấy danh mục." });
        }

        return Ok(category);
    }

    [HttpPost]
    [Authorize(Policy = "SellerOnly")]
    public async Task<IActionResult> Create([FromBody] CategoryUpsertRequest request)
    {
        if (!ModelState.IsValid)
        {
            return ValidationProblem(ModelState);
        }

        var categoryName = request.CategoryName.Trim();
        if (string.IsNullOrWhiteSpace(categoryName))
        {
            return BadRequest(new { message = "Tên danh mục không hợp lệ." });
        }

        var duplicateName = await _db.Categories
            .AnyAsync(c => c.CategoryName.ToLower() == categoryName.ToLower());
        if (duplicateName)
        {
            return BadRequest(new { message = "Tên danh mục đã tồn tại." });
        }

        var slug = string.IsNullOrWhiteSpace(request.Slug)
            ? GenerateSlug(categoryName)
            : GenerateSlug(request.Slug.Trim());

        slug = await EnsureUniqueSlugAsync(slug, null);

        var category = new Category
        {
            CategoryName = categoryName,
            Description = request.Description?.Trim(),
            ImageCategoriesName = request.ImageCategoriesName?.Trim(),
            IsActive = request.IsActive,
            Slug = slug,
            CreatedDate = DateTime.UtcNow,
            UpdatedDate = null
        };

        _db.Categories.Add(category);

        try
        {
            await _db.SaveChangesAsync();
        }
        catch (DbUpdateException ex)
        {
            return BadRequest(new { message = "Không thể tạo danh mục.", detail = ex.InnerException?.Message ?? ex.Message });
        }

        return CreatedAtAction(nameof(GetById), new { id = category.CategoryId }, new { category.CategoryId });
    }

    [HttpPut("{id:int}")]
    [Authorize(Policy = "SellerOnly")]
    public async Task<IActionResult> Update([FromRoute] int id, [FromBody] CategoryUpsertRequest request)
    {
        if (!ModelState.IsValid)
        {
            return ValidationProblem(ModelState);
        }

        var existing = await _db.Categories.FirstOrDefaultAsync(c => c.CategoryId == id);
        if (existing is null)
        {
            return NotFound(new { message = "Không tìm thấy danh mục." });
        }

        var categoryName = request.CategoryName.Trim();
        if (string.IsNullOrWhiteSpace(categoryName))
        {
            return BadRequest(new { message = "Tên danh mục không hợp lệ." });
        }

        var duplicateName = await _db.Categories
            .AnyAsync(c => c.CategoryId != id && c.CategoryName.ToLower() == categoryName.ToLower());
        if (duplicateName)
        {
            return BadRequest(new { message = "Tên danh mục đã tồn tại." });
        }

        var slugCandidate = string.IsNullOrWhiteSpace(request.Slug)
            ? GenerateSlug(categoryName)
            : GenerateSlug(request.Slug.Trim());

        var slug = await EnsureUniqueSlugAsync(slugCandidate, id);

        existing.CategoryName = categoryName;
        existing.Description = request.Description?.Trim();
        existing.ImageCategoriesName = request.ImageCategoriesName?.Trim();
        existing.IsActive = request.IsActive;
        existing.Slug = slug;
        existing.UpdatedDate = DateTime.UtcNow;

        try
        {
            await _db.SaveChangesAsync();
        }
        catch (DbUpdateException ex)
        {
            return BadRequest(new { message = "Không thể cập nhật danh mục.", detail = ex.InnerException?.Message ?? ex.Message });
        }

        return Ok(new { message = "Cập nhật danh mục thành công." });
    }

    [HttpDelete("{id:int}")]
    [Authorize(Policy = "SellerOnly")]
    public async Task<IActionResult> Delete([FromRoute] int id)
    {
        var category = await _db.Categories.FirstOrDefaultAsync(c => c.CategoryId == id);
        if (category is null)
        {
            return NotFound(new { message = "Không tìm thấy danh mục." });
        }

        var hasProducts = await _db.Products.AnyAsync(p => p.CategoryId == id);
        if (hasProducts)
        {
            return Conflict(new { message = "Không thể xóa danh mục vì đang có sản phẩm." });
        }

        _db.Categories.Remove(category);
        await _db.SaveChangesAsync();

        return Ok(new { message = "Xóa danh mục thành công." });
    }

    private async Task<string> EnsureUniqueSlugAsync(string baseSlug, int? excludeCategoryId)
    {
        var slug = string.IsNullOrWhiteSpace(baseSlug) ? "category" : baseSlug;
        var idx = 1;

        while (await _db.Categories.AnyAsync(c => c.Slug == slug && (!excludeCategoryId.HasValue || c.CategoryId != excludeCategoryId.Value)))
        {
            slug = $"{baseSlug}-{idx}";
            idx++;
        }

        return slug;
    }

    private static string GenerateSlug(string input)
    {
        if (string.IsNullOrWhiteSpace(input))
        {
            return "category";
        }

        var slug = RemoveVietnameseTone(input.Trim().ToLowerInvariant());
        slug = Regex.Replace(slug, @"[^a-z0-9\s-]", string.Empty);
        slug = Regex.Replace(slug, @"\s+", "-");
        slug = Regex.Replace(slug, @"-+", "-");

        return slug.Trim('-');
    }

    private static string RemoveVietnameseTone(string text)
    {
        string[] signs =
        {
            "aAeEoOuUiIdDyY",
            "áàạảãâấầậẩẫăắằặẳẵ",
            "ÁÀẠẢÃÂẤẦẬẨẪĂẮẰẶẲẴ",
            "éèẹẻẽêếềệểễ",
            "ÉÈẸẺẼÊẾỀỆỂỄ",
            "óòọỏõôốồộổỗơớờợởỡ",
            "ÓÒỌỎÕÔỐỒỘỔỖƠỚỜỢỞỠ",
            "úùụủũưứừựửữ",
            "ÚÙỤỦŨƯỨỪỰỬỮ",
            "íìịỉĩ",
            "ÍÌỊỈĨ",
            "đ",
            "Đ",
            "ýỳỵỷỹ",
            "ÝỲỴỶỸ"
        };

        for (var i = 1; i < signs.Length; i++)
        {
            for (var j = 0; j < signs[i].Length; j++)
            {
                text = text.Replace(signs[i][j], signs[0][i - 1]);
            }
        }

        return text;
    }
}
