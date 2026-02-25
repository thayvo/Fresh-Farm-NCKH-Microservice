using FreshFarm.Catalog.Api.Dtos;
using FreshFarm.Catalog.Api.Models;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace FreshFarm.Catalog.Api.Controllers;

[ApiController]
[Route("api/[controller]")]
public sealed class UnitsController : ControllerBase
{
    private readonly FreshFarmCatalogDBContext _db;

    public UnitsController(FreshFarmCatalogDBContext db)
    {
        _db = db;
    }

    [HttpGet]
    public async Task<IActionResult> Get([FromQuery] string? search)
    {
        var query = _db.Units.AsNoTracking().AsQueryable();

        if (!string.IsNullOrWhiteSpace(search))
        {
            var keyword = search.Trim();
            query = query.Where(u =>
                u.UnitName.Contains(keyword) ||
                u.Symbol.Contains(keyword) ||
                (u.Description != null && u.Description.Contains(keyword)));
        }

        var units = await query
            .OrderByDescending(u => u.CreatedDate)
            .Select(u => new
            {
                u.UnitId,
                u.UnitName,
                u.Symbol,
                u.Description,
                u.IsActive,
                u.CreatedDate
            })
            .ToListAsync();

        return Ok(units);
    }

    [HttpGet("{id:int}")]
    public async Task<IActionResult> GetById([FromRoute] int id)
    {
        var unit = await _db.Units
            .AsNoTracking()
            .Where(u => u.UnitId == id)
            .Select(u => new
            {
                u.UnitId,
                u.UnitName,
                u.Symbol,
                u.Description,
                u.IsActive,
                u.CreatedDate
            })
            .FirstOrDefaultAsync();

        if (unit is null)
        {
            return NotFound(new { message = "Không tìm thấy đơn vị tính." });
        }

        return Ok(unit);
    }

    [HttpPost]
    [Authorize(Policy = "SellerOnly")]
    public async Task<IActionResult> Create([FromBody] UnitUpsertRequest request)
    {
        if (!ModelState.IsValid)
        {
            return ValidationProblem(ModelState);
        }

        var unitName = request.UnitName.Trim();
        var symbol = request.Symbol.Trim();

        if (string.IsNullOrWhiteSpace(unitName) || string.IsNullOrWhiteSpace(symbol))
        {
            return BadRequest(new { message = "Tên đơn vị và ký hiệu không hợp lệ." });
        }

        var duplicate = await _db.Units.AnyAsync(u =>
            u.UnitName.ToLower() == unitName.ToLower() ||
            u.Symbol.ToLower() == symbol.ToLower());

        if (duplicate)
        {
            return BadRequest(new { message = "Tên đơn vị hoặc ký hiệu đã tồn tại." });
        }

        var unit = new Unit
        {
            UnitName = unitName,
            Symbol = symbol,
            Description = request.Description?.Trim(),
            IsActive = request.IsActive,
            CreatedDate = DateTime.UtcNow
        };

        _db.Units.Add(unit);

        try
        {
            await _db.SaveChangesAsync();
        }
        catch (DbUpdateException ex)
        {
            return BadRequest(new { message = "Không thể tạo đơn vị tính.", detail = ex.InnerException?.Message ?? ex.Message });
        }

        return CreatedAtAction(nameof(GetById), new { id = unit.UnitId }, new { unit.UnitId });
    }

    [HttpPut("{id:int}")]
    [Authorize(Policy = "SellerOnly")]
    public async Task<IActionResult> Update([FromRoute] int id, [FromBody] UnitUpsertRequest request)
    {
        if (!ModelState.IsValid)
        {
            return ValidationProblem(ModelState);
        }

        var existing = await _db.Units.FirstOrDefaultAsync(u => u.UnitId == id);
        if (existing is null)
        {
            return NotFound(new { message = "Không tìm thấy đơn vị tính." });
        }

        var unitName = request.UnitName.Trim();
        var symbol = request.Symbol.Trim();

        if (string.IsNullOrWhiteSpace(unitName) || string.IsNullOrWhiteSpace(symbol))
        {
            return BadRequest(new { message = "Tên đơn vị và ký hiệu không hợp lệ." });
        }

        var duplicate = await _db.Units.AnyAsync(u =>
            u.UnitId != id && (u.UnitName.ToLower() == unitName.ToLower() || u.Symbol.ToLower() == symbol.ToLower()));

        if (duplicate)
        {
            return BadRequest(new { message = "Tên đơn vị hoặc ký hiệu đã tồn tại." });
        }

        existing.UnitName = unitName;
        existing.Symbol = symbol;
        existing.Description = request.Description?.Trim();
        existing.IsActive = request.IsActive;

        try
        {
            await _db.SaveChangesAsync();
        }
        catch (DbUpdateException ex)
        {
            return BadRequest(new { message = "Không thể cập nhật đơn vị tính.", detail = ex.InnerException?.Message ?? ex.Message });
        }

        return Ok(new { message = "Cập nhật đơn vị tính thành công." });
    }

    [HttpDelete("{id:int}")]
    [Authorize(Policy = "SellerOnly")]
    public async Task<IActionResult> Delete([FromRoute] int id)
    {
        var unit = await _db.Units.FirstOrDefaultAsync(u => u.UnitId == id);
        if (unit is null)
        {
            return NotFound(new { message = "Không tìm thấy đơn vị tính." });
        }

        var hasProducts = await _db.Products.AnyAsync(p => p.UnitId == id);
        if (hasProducts)
        {
            return Conflict(new { message = "Đơn vị đang được sử dụng, không thể xóa." });
        }

        _db.Units.Remove(unit);
        await _db.SaveChangesAsync();

        return Ok(new { message = "Xóa đơn vị tính thành công." });
    }

    [HttpPost("{id:int}/toggle-status")]
    [Authorize(Policy = "SellerOnly")]
    public async Task<IActionResult> ToggleStatus([FromRoute] int id)
    {
        var unit = await _db.Units.FirstOrDefaultAsync(u => u.UnitId == id);
        if (unit is null)
        {
            return NotFound(new { message = "Không tìm thấy đơn vị tính." });
        }

        unit.IsActive = !unit.IsActive;
        await _db.SaveChangesAsync();

        return Ok(new
        {
            message = unit.IsActive ? "Đã kích hoạt đơn vị tính." : "Đã vô hiệu hóa đơn vị tính.",
            newStatus = unit.IsActive
        });
    }
}
