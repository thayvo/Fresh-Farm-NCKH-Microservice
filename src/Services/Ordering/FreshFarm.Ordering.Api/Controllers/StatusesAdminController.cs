using FreshFarm.Ordering.Api.Models;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace FreshFarm.Ordering.Api.Controllers;

[ApiController]
[Route("api/orders/admin")]
[Authorize(Policy = "SellerOnly")]
public sealed class StatusesAdminController : ControllerBase
{
    private readonly FreshFarmOrderingDBContext _db;

    public StatusesAdminController(FreshFarmOrderingDBContext db)
    {
        _db = db;
    }

    [HttpGet("statuses")]
    public async Task<IActionResult> GetStatuses(
        [FromQuery] string? searchTerm = "",
        [FromQuery] int? statusTypeId = null,
        [FromQuery] int page = 1,
        [FromQuery] int pageSize = 6,
        CancellationToken cancellationToken = default)
    {
        if (page < 1)
        {
            page = 1;
        }

        if (pageSize <= 0 || pageSize > 100)
        {
            pageSize = 6;
        }

        var query = _db.Statuses
            .AsNoTracking()
            .Include(x => x.StatusType)
            .Where(x => x.IsActive)
            .AsQueryable();

        if (!string.IsNullOrWhiteSpace(searchTerm))
        {
            var term = searchTerm.Trim();
            query = query.Where(x => x.StatusName.Contains(term));
        }

        if (statusTypeId.HasValue && statusTypeId.Value > 0)
        {
            query = query.Where(x => x.StatusTypeId == statusTypeId.Value);
        }

        var totalItems = await query.CountAsync(cancellationToken);
        var totalPages = totalItems == 0 ? 0 : (int)Math.Ceiling(totalItems / (double)pageSize);
        if (totalPages > 0 && page > totalPages)
        {
            page = totalPages;
        }

        var statuses = await query
            .OrderBy(x => x.DisplayOrder)
            .ThenBy(x => x.StatusName)
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .Select(x => new
            {
                statusID = x.StatusId,
                statusName = x.StatusName,
                statusTypeID = x.StatusTypeId,
                colorCode = x.ColorCode,
                note = x.Note,
                displayOrder = x.DisplayOrder,
                isActive = x.IsActive,
                createdDate = x.CreatedDate,
                statusType = new
                {
                    statusTypeID = x.StatusType.StatusTypeId,
                    statusTypeName = x.StatusType.StatusTypeName
                }
            })
            .ToListAsync(cancellationToken);

        var statusTypes = await _db.StatusTypes
            .AsNoTracking()
            .Where(x => x.IsActive)
            .OrderBy(x => x.StatusTypeName)
            .Select(x => new
            {
                statusTypeID = x.StatusTypeId,
                statusTypeName = x.StatusTypeName
            })
            .ToListAsync(cancellationToken);

        return Ok(new
        {
            page,
            totalPages,
            totalItems,
            pageSize,
            searchTerm = searchTerm ?? string.Empty,
            statusTypeID = statusTypeId,
            statuses,
            statusTypes
        });
    }

    [HttpGet("statuses/{id:int}")]
    public async Task<IActionResult> GetStatusDetail([FromRoute] int id, CancellationToken cancellationToken = default)
    {
        var status = await _db.Statuses
            .AsNoTracking()
            .FirstOrDefaultAsync(x => x.StatusId == id, cancellationToken);
        if (status is null)
        {
            return NotFound(new { success = false, message = "Khong tim thay trang thai." });
        }

        return Ok(new
        {
            statusID = status.StatusId,
            statusName = status.StatusName,
            statusTypeID = status.StatusTypeId,
            colorCode = status.ColorCode,
            note = status.Note,
            displayOrder = status.DisplayOrder
        });
    }

    [HttpPost("statuses")]
    public async Task<IActionResult> CreateStatus([FromBody] CreateStatusRequest? request, CancellationToken cancellationToken = default)
    {
        if (request is null)
        {
            return BadRequest(new { success = false, message = "Du lieu khong hop le." });
        }

        var statusName = (request.StatusName ?? string.Empty).Trim();
        if (string.IsNullOrWhiteSpace(statusName))
        {
            return BadRequest(new { success = false, message = "Vui long nhap ten trang thai." });
        }

        if (request.StatusTypeID <= 0)
        {
            return BadRequest(new { success = false, message = "Vui long chon loai trang thai." });
        }

        var statusTypeExists = await _db.StatusTypes
            .AsNoTracking()
            .AnyAsync(x => x.StatusTypeId == request.StatusTypeID && x.IsActive, cancellationToken);
        if (!statusTypeExists)
        {
            return BadRequest(new { success = false, message = "Loai trang thai khong hop le hoac da bi xoa." });
        }

        var duplicated = await _db.Statuses
            .AsNoTracking()
            .AnyAsync(x =>
                    x.IsActive &&
                    x.StatusTypeId == request.StatusTypeID &&
                    x.StatusName == statusName,
                cancellationToken);
        if (duplicated)
        {
            return BadRequest(new { success = false, message = "Ten trang thai da ton tai trong loai nay." });
        }

        _db.Statuses.Add(new Status
        {
            StatusName = statusName,
            StatusTypeId = request.StatusTypeID,
            ColorCode = NormalizeColorCode(request.ColorCode),
            Note = string.IsNullOrWhiteSpace(request.Note) ? string.Empty : request.Note.Trim(),
            DisplayOrder = Math.Max(0, request.DisplayOrder),
            IsActive = true,
            CreatedDate = DateTime.UtcNow
        });

        await _db.SaveChangesAsync(cancellationToken);
        return Ok(new { success = true, message = "Them trang thai thanh cong!" });
    }

    [HttpPut("statuses/{id:int}")]
    public async Task<IActionResult> UpdateStatus(
        [FromRoute] int id,
        [FromBody] UpdateStatusRequest? request,
        CancellationToken cancellationToken = default)
    {
        if (request is null)
        {
            return BadRequest(new { success = false, message = "Du lieu khong hop le." });
        }

        var status = await _db.Statuses.FirstOrDefaultAsync(x => x.StatusId == id, cancellationToken);
        if (status is null)
        {
            return NotFound(new { success = false, message = "Khong tim thay trang thai." });
        }

        var statusName = (request.StatusName ?? string.Empty).Trim();
        if (string.IsNullOrWhiteSpace(statusName))
        {
            return BadRequest(new { success = false, message = "Vui long nhap ten trang thai." });
        }

        if (request.StatusTypeID <= 0)
        {
            return BadRequest(new { success = false, message = "Vui long chon loai trang thai." });
        }

        var statusTypeExists = await _db.StatusTypes
            .AsNoTracking()
            .AnyAsync(x => x.StatusTypeId == request.StatusTypeID && x.IsActive, cancellationToken);
        if (!statusTypeExists)
        {
            return BadRequest(new { success = false, message = "Loai trang thai khong hop le hoac da bi xoa." });
        }

        var duplicated = await _db.Statuses
            .AsNoTracking()
            .AnyAsync(x =>
                    x.StatusId != id &&
                    x.IsActive &&
                    x.StatusTypeId == request.StatusTypeID &&
                    x.StatusName == statusName,
                cancellationToken);
        if (duplicated)
        {
            return BadRequest(new { success = false, message = "Ten trang thai da ton tai trong loai nay." });
        }

        status.StatusName = statusName;
        status.StatusTypeId = request.StatusTypeID;
        status.ColorCode = NormalizeColorCode(request.ColorCode);
        status.Note = string.IsNullOrWhiteSpace(request.Note) ? string.Empty : request.Note.Trim();
        status.DisplayOrder = Math.Max(0, request.DisplayOrder);

        await _db.SaveChangesAsync(cancellationToken);
        return Ok(new { success = true, message = "Cap nhat trang thai thanh cong!" });
    }

    [HttpDelete("statuses/{id:int}")]
    public async Task<IActionResult> DeleteStatus([FromRoute] int id, CancellationToken cancellationToken = default)
    {
        var status = await _db.Statuses.FirstOrDefaultAsync(x => x.StatusId == id, cancellationToken);
        if (status is null)
        {
            return NotFound(new { success = false, message = "Khong tim thay trang thai." });
        }

        status.IsActive = false;
        await _db.SaveChangesAsync(cancellationToken);
        return Ok(new { success = true, message = "Xoa trang thai thanh cong!" });
    }

    [HttpGet("status-types")]
    public async Task<IActionResult> GetStatusTypes(
        [FromQuery] string? searchTerm = "",
        [FromQuery] int page = 1,
        [FromQuery] int pageSize = 6,
        CancellationToken cancellationToken = default)
    {
        if (page < 1)
        {
            page = 1;
        }

        if (pageSize <= 0 || pageSize > 100)
        {
            pageSize = 6;
        }

        var query = _db.StatusTypes
            .AsNoTracking()
            .Where(x => x.IsActive)
            .AsQueryable();

        if (!string.IsNullOrWhiteSpace(searchTerm))
        {
            var term = searchTerm.Trim();
            query = query.Where(x =>
                x.StatusTypeName.Contains(term) ||
                (x.Description != null && x.Description.Contains(term)));
        }

        var totalItems = await query.CountAsync(cancellationToken);
        var totalPages = totalItems == 0 ? 0 : (int)Math.Ceiling(totalItems / (double)pageSize);
        if (totalPages > 0 && page > totalPages)
        {
            page = totalPages;
        }

        var statusTypeRows = await query
            .OrderBy(x => x.StatusTypeName)
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .Select(x => new
            {
                statusTypeID = x.StatusTypeId,
                statusTypeName = x.StatusTypeName,
                description = x.Description,
                isActive = x.IsActive,
                createdDate = x.CreatedDate,
                activeStatusCount = x.Statuses.Count(s => s.IsActive)
            })
            .ToListAsync(cancellationToken);

        return Ok(new
        {
            page,
            totalPages,
            totalItems,
            pageSize,
            searchTerm = searchTerm ?? string.Empty,
            statusTypes = statusTypeRows
        });
    }

    [HttpGet("status-types/lookup")]
    public async Task<IActionResult> GetStatusTypeLookup(CancellationToken cancellationToken = default)
    {
        var rows = await _db.StatusTypes
            .AsNoTracking()
            .Where(x => x.IsActive)
            .OrderBy(x => x.StatusTypeName)
            .Select(x => new
            {
                statusTypeID = x.StatusTypeId,
                statusTypeName = x.StatusTypeName
            })
            .ToListAsync(cancellationToken);

        return Ok(rows);
    }

    [HttpGet("status-types/{id:int}")]
    public async Task<IActionResult> GetStatusTypeDetail([FromRoute] int id, CancellationToken cancellationToken = default)
    {
        var statusType = await _db.StatusTypes
            .AsNoTracking()
            .FirstOrDefaultAsync(x => x.StatusTypeId == id, cancellationToken);
        if (statusType is null)
        {
            return NotFound(new { success = false, message = "Khong tim thay loai trang thai." });
        }

        return Ok(new
        {
            statusTypeID = statusType.StatusTypeId,
            statusTypeName = statusType.StatusTypeName,
            description = statusType.Description
        });
    }

    [HttpPost("status-types")]
    public async Task<IActionResult> CreateStatusType([FromBody] CreateStatusTypeRequest? request, CancellationToken cancellationToken = default)
    {
        if (request is null)
        {
            return BadRequest(new { success = false, message = "Du lieu khong hop le." });
        }

        var statusTypeName = (request.StatusTypeName ?? string.Empty).Trim();
        if (string.IsNullOrWhiteSpace(statusTypeName))
        {
            return BadRequest(new { success = false, message = "Vui long nhap ten loai trang thai." });
        }

        var duplicated = await _db.StatusTypes
            .AsNoTracking()
            .AnyAsync(x => x.IsActive && x.StatusTypeName == statusTypeName, cancellationToken);
        if (duplicated)
        {
            return BadRequest(new { success = false, message = "Ten loai trang thai da ton tai!" });
        }

        _db.StatusTypes.Add(new StatusType
        {
            StatusTypeName = statusTypeName,
            Description = string.IsNullOrWhiteSpace(request.Description) ? string.Empty : request.Description.Trim(),
            IsActive = true,
            CreatedDate = DateTime.UtcNow
        });

        await _db.SaveChangesAsync(cancellationToken);
        return Ok(new { success = true, message = "Them loai trang thai thanh cong!" });
    }

    [HttpPut("status-types/{id:int}")]
    public async Task<IActionResult> UpdateStatusType(
        [FromRoute] int id,
        [FromBody] UpdateStatusTypeRequest? request,
        CancellationToken cancellationToken = default)
    {
        if (request is null)
        {
            return BadRequest(new { success = false, message = "Du lieu khong hop le." });
        }

        var statusType = await _db.StatusTypes.FirstOrDefaultAsync(x => x.StatusTypeId == id, cancellationToken);
        if (statusType is null)
        {
            return NotFound(new { success = false, message = "Khong tim thay loai trang thai." });
        }

        var statusTypeName = (request.StatusTypeName ?? string.Empty).Trim();
        if (string.IsNullOrWhiteSpace(statusTypeName))
        {
            return BadRequest(new { success = false, message = "Vui long nhap ten loai trang thai." });
        }

        var duplicated = await _db.StatusTypes
            .AsNoTracking()
            .AnyAsync(x => x.StatusTypeId != id && x.IsActive && x.StatusTypeName == statusTypeName, cancellationToken);
        if (duplicated)
        {
            return BadRequest(new { success = false, message = "Ten loai trang thai da ton tai!" });
        }

        statusType.StatusTypeName = statusTypeName;
        statusType.Description = string.IsNullOrWhiteSpace(request.Description) ? string.Empty : request.Description.Trim();

        await _db.SaveChangesAsync(cancellationToken);
        return Ok(new { success = true, message = "Cap nhat loai trang thai thanh cong!" });
    }

    [HttpDelete("status-types/{id:int}")]
    public async Task<IActionResult> DeleteStatusType([FromRoute] int id, CancellationToken cancellationToken = default)
    {
        var statusType = await _db.StatusTypes.FirstOrDefaultAsync(x => x.StatusTypeId == id, cancellationToken);
        if (statusType is null)
        {
            return NotFound(new { success = false, message = "Khong tim thay loai trang thai." });
        }

        var activeStatusCount = await _db.Statuses
            .AsNoTracking()
            .CountAsync(x => x.StatusTypeId == id && x.IsActive, cancellationToken);
        if (activeStatusCount > 0)
        {
            return BadRequest(new
            {
                success = false,
                message = $"Khong the xoa! Loai trang thai dang duoc su dung boi {activeStatusCount} trang thai."
            });
        }

        statusType.IsActive = false;
        await _db.SaveChangesAsync(cancellationToken);
        return Ok(new { success = true, message = "Xoa loai trang thai thanh cong!" });
    }

    private static string NormalizeColorCode(string? rawColorCode)
    {
        var value = (rawColorCode ?? string.Empty).Trim();
        return string.IsNullOrWhiteSpace(value) ? "#28a745" : value;
    }

    public sealed class CreateStatusRequest
    {
        public string? StatusName { get; set; }

        public int StatusTypeID { get; set; }

        public string? ColorCode { get; set; }

        public string? Note { get; set; }

        public int DisplayOrder { get; set; }
    }

    public sealed class UpdateStatusRequest
    {
        public string? StatusName { get; set; }

        public int StatusTypeID { get; set; }

        public string? ColorCode { get; set; }

        public string? Note { get; set; }

        public int DisplayOrder { get; set; }
    }

    public sealed class CreateStatusTypeRequest
    {
        public string? StatusTypeName { get; set; }

        public string? Description { get; set; }
    }

    public sealed class UpdateStatusTypeRequest
    {
        public string? StatusTypeName { get; set; }

        public string? Description { get; set; }
    }
}
