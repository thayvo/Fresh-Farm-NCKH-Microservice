using FreshFarm.Ordering.Api.Models;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace FreshFarm.Ordering.Api.Controllers;

[ApiController]
[Route("api/orders/admin/feedbacks")]
[Authorize(Policy = "SellerOnly")]
public sealed class FeedbacksAdminController : ControllerBase
{
    private readonly FreshFarmOrderingDBContext _db;

    public FeedbacksAdminController(FreshFarmOrderingDBContext db)
    {
        _db = db;
    }

    [HttpGet]
    public async Task<IActionResult> Get([FromQuery] string? search = null, [FromQuery] int take = 500, CancellationToken cancellationToken = default)
    {
        if (take <= 0)
        {
            take = 500;
        }

        if (take > 5000)
        {
            take = 5000;
        }

        var query = _db.ContactMessages
            .AsNoTracking()
            .Where(x => !x.IsDeleted);

        if (!string.IsNullOrWhiteSpace(search))
        {
            var term = search.Trim().ToLowerInvariant();
            query = query.Where(x =>
                x.SenderName.ToLower().Contains(term) ||
                x.SenderEmail.ToLower().Contains(term) ||
                (x.Subject != null && x.Subject.ToLower().Contains(term)) ||
                x.Message.ToLower().Contains(term));
        }

        var rows = await query
            .OrderByDescending(x => x.CreatedAt)
            .Take(take)
            .Select(x => new
            {
                id = x.Id,
                senderName = x.SenderName,
                senderEmail = x.SenderEmail,
                senderPhone = x.SenderPhone,
                subject = x.Subject,
                message = x.Message,
                createdAt = x.CreatedAt,
                status = x.Status,
                adminNote = x.AdminNote
            })
            .ToListAsync(cancellationToken);

        return Ok(rows);
    }

    [HttpGet("{id:int}")]
    public async Task<IActionResult> GetById([FromRoute] int id, CancellationToken cancellationToken = default)
    {
        if (id <= 0)
        {
            return BadRequest(new { success = false, message = "ID phan hoi khong hop le." });
        }

        var row = await _db.ContactMessages
            .AsNoTracking()
            .Where(x => x.Id == id && !x.IsDeleted)
            .Select(x => new
            {
                id = x.Id,
                senderName = x.SenderName,
                senderEmail = x.SenderEmail,
                senderPhone = x.SenderPhone,
                subject = x.Subject,
                message = x.Message,
                createdAt = x.CreatedAt,
                status = x.Status,
                adminNote = x.AdminNote
            })
            .FirstOrDefaultAsync(cancellationToken);

        if (row is null)
        {
            return NotFound(new { success = false, message = "Khong tim thay phan hoi." });
        }

        return Ok(row);
    }

    [HttpPost("{id:int}/status")]
    public async Task<IActionResult> UpdateStatus([FromRoute] int id, [FromBody] UpdateFeedbackStatusRequest? request, CancellationToken cancellationToken = default)
    {
        if (id <= 0)
        {
            return BadRequest(new { success = false, message = "ID phan hoi khong hop le." });
        }

        var feedback = await _db.ContactMessages.FirstOrDefaultAsync(x => x.Id == id && !x.IsDeleted, cancellationToken);
        if (feedback is null)
        {
            return NotFound(new { success = false, message = "Khong tim thay phan hoi." });
        }

        feedback.Status = NormalizeStatus(request?.Status);
        if (!string.IsNullOrWhiteSpace(request?.AdminNote))
        {
            feedback.AdminNote = request.AdminNote.Trim();
        }

        if (feedback.Status == "processed")
        {
            feedback.ProcessedAt ??= DateTime.UtcNow;
        }

        await _db.SaveChangesAsync(cancellationToken);
        return Ok(new { success = true, message = "Da cap nhat trang thai phan hoi." });
    }

    [HttpDelete("{id:int}")]
    public async Task<IActionResult> Delete([FromRoute] int id, CancellationToken cancellationToken = default)
    {
        if (id <= 0)
        {
            return BadRequest(new { success = false, message = "ID phan hoi khong hop le." });
        }

        var feedback = await _db.ContactMessages.FirstOrDefaultAsync(x => x.Id == id && !x.IsDeleted, cancellationToken);
        if (feedback is null)
        {
            return NotFound(new { success = false, message = "Phan hoi khong ton tai hoac da bi xoa." });
        }

        feedback.IsDeleted = true;
        feedback.DeletedAt = DateTime.UtcNow;
        await _db.SaveChangesAsync(cancellationToken);

        return Ok(new { success = true, message = "Da xoa phan hoi thanh cong." });
    }

    private static string NormalizeStatus(string? rawStatus)
    {
        var status = rawStatus?.Trim().ToLowerInvariant();
        return status switch
        {
            "processed" => "processed",
            "resolved" => "processed",
            "done" => "processed",
            "new" => "new",
            _ => "new"
        };
    }

    public sealed class UpdateFeedbackStatusRequest
    {
        public string? Status { get; set; }

        public string? AdminNote { get; set; }
    }
}
