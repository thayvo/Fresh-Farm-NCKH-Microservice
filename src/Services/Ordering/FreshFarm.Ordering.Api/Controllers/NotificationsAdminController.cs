using FreshFarm.Ordering.Api.Models;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace FreshFarm.Ordering.Api.Controllers;

[ApiController]
[Route("api/orders/admin/notifications")]
[Authorize(Policy = "AdminOnly")]
public sealed class NotificationsAdminController : ControllerBase
{
    private readonly FreshFarmOrderingDBContext _db;

    public NotificationsAdminController(FreshFarmOrderingDBContext db)
    {
        _db = db;
    }

    [HttpGet]
    public async Task<IActionResult> GetNotifications(
        [FromQuery] string? q = null,
        [FromQuery] string? type = null,
        [FromQuery] bool? isRead = null,
        [FromQuery] int? userId = null,
        [FromQuery] int? orderId = null,
        [FromQuery] int page = 1,
        [FromQuery] int pageSize = 12,
        CancellationToken cancellationToken = default)
    {
        if (page < 1)
        {
            page = 1;
        }

        if (pageSize <= 0 || pageSize > 100)
        {
            pageSize = 12;
        }

        var baseQuery = _db.CustomerNotifications
            .AsNoTracking()
            .AsQueryable();

        var filteredQuery = ApplyFilters(baseQuery, q, type, isRead, userId, orderId);

        var total = await filteredQuery.CountAsync(cancellationToken);
        var totalAll = await baseQuery.CountAsync(cancellationToken);
        var unread = await baseQuery.CountAsync(x => !(x.IsRead ?? false), cancellationToken);
        var push = await baseQuery.CountAsync(x => x.IsPushNotification ?? false, cancellationToken);
        var recent24h = await baseQuery.CountAsync(
            x => x.CreatedAt.HasValue && x.CreatedAt.Value >= DateTime.UtcNow.AddHours(-24),
            cancellationToken);
        var typeOptions = await baseQuery
            .Where(x => !string.IsNullOrWhiteSpace(x.NotificationType))
            .Select(x => x.NotificationType)
            .Distinct()
            .OrderBy(x => x)
            .ToListAsync(cancellationToken);

        var totalPages = total == 0 ? 1 : (int)Math.Ceiling(total / (double)pageSize);
        if (page > totalPages)
        {
            page = totalPages;
        }

        var rows = await filteredQuery
            .OrderByDescending(x => x.CreatedAt ?? DateTime.MinValue)
            .ThenByDescending(x => x.NotificationId)
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .Select(x => new
            {
                notificationId = x.NotificationId,
                userId = x.UserId,
                orderId = x.OrderId,
                notificationType = x.NotificationType,
                title = x.Title,
                message = x.Message,
                isRead = x.IsRead ?? false,
                isPushNotification = x.IsPushNotification ?? false,
                createdAt = x.CreatedAt,
                readAt = x.ReadAt
            })
            .ToListAsync(cancellationToken);

        return Ok(new
        {
            page,
            pageSize,
            total,
            totalPages,
            stats = new
            {
                totalNotifications = totalAll,
                unreadNotifications = unread,
                pushNotifications = push,
                recent24hNotifications = recent24h
            },
            filters = new
            {
                q = q ?? string.Empty,
                type = type ?? string.Empty,
                isRead,
                userId,
                orderId,
                typeOptions
            },
            notifications = rows
        });
    }

    [HttpPost("{id:int}/mark-read")]
    public async Task<IActionResult> MarkAsRead([FromRoute] int id, CancellationToken cancellationToken = default)
    {
        var notification = await _db.CustomerNotifications.FirstOrDefaultAsync(x => x.NotificationId == id, cancellationToken);
        if (notification is null)
        {
            return NotFound(new { success = false, message = "Không tìm thấy thông báo." });
        }

        if (!(notification.IsRead ?? false))
        {
            notification.IsRead = true;
            notification.ReadAt = DateTime.UtcNow;
            await _db.SaveChangesAsync(cancellationToken);
        }

        return Ok(new { success = true, message = "Đã đánh dấu đã đọc." });
    }

    [HttpPost("mark-all-read")]
    public async Task<IActionResult> MarkAllAsRead([FromBody] MarkAllNotificationsReadRequest? request, CancellationToken cancellationToken = default)
    {
        var query = ApplyFilters(
            _db.CustomerNotifications.AsQueryable(),
            request?.Q,
            request?.Type,
            request?.IsRead,
            request?.UserId,
            request?.OrderId);

        var targets = await query
            .Where(x => !(x.IsRead ?? false))
            .ToListAsync(cancellationToken);

        if (targets.Count == 0)
        {
            return Ok(new { success = true, message = "Không có thông báo chưa đọc trong phạm vi hiện tại.", affected = 0 });
        }

        var now = DateTime.UtcNow;
        foreach (var item in targets)
        {
            item.IsRead = true;
            item.ReadAt = now;
        }

        await _db.SaveChangesAsync(cancellationToken);
        return Ok(new { success = true, message = $"Đã đánh dấu đã đọc {targets.Count} thông báo.", affected = targets.Count });
    }

    private static IQueryable<CustomerNotification> ApplyFilters(
        IQueryable<CustomerNotification> query,
        string? q,
        string? type,
        bool? isRead,
        int? userId,
        int? orderId)
    {
        if (!string.IsNullOrWhiteSpace(q))
        {
            var term = q.Trim();
            query = query.Where(x =>
                x.Title.Contains(term) ||
                x.Message.Contains(term) ||
                x.NotificationType.Contains(term));
        }

        if (!string.IsNullOrWhiteSpace(type))
        {
            var normalizedType = type.Trim();
            query = query.Where(x => x.NotificationType == normalizedType);
        }

        if (isRead.HasValue)
        {
            query = query.Where(x => (x.IsRead ?? false) == isRead.Value);
        }

        if (userId.HasValue && userId.Value > 0)
        {
            query = query.Where(x => x.UserId == userId.Value);
        }

        if (orderId.HasValue && orderId.Value > 0)
        {
            query = query.Where(x => x.OrderId == orderId.Value);
        }

        return query;
    }

    public sealed class MarkAllNotificationsReadRequest
    {
        public string? Q { get; set; }

        public string? Type { get; set; }

        public bool? IsRead { get; set; }

        public int? UserId { get; set; }

        public int? OrderId { get; set; }
    }
}
