using System.Security.Cryptography;
using System.Text;
using FreshFarm.Ordering.Api.Models;
using FreshFarm.Ordering.Api.Options;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace FreshFarm.Ordering.Api.Controllers;

[ApiController]
[Route("api/orders/admin/notifications")]
[Authorize(Policy = "AdminOnly")]
public sealed class NotificationsAdminController : ControllerBase
{
    private readonly FreshFarmOrderingDBContext _db;
    private readonly InternalServiceAuthOptions _internalServiceAuthOptions;
    private readonly ILogger<NotificationsAdminController> _logger;

    public NotificationsAdminController(
        FreshFarmOrderingDBContext db,
        IOptions<InternalServiceAuthOptions> internalServiceAuthOptions,
        ILogger<NotificationsAdminController> logger)
    {
        _db = db;
        _internalServiceAuthOptions = internalServiceAuthOptions.Value;
        _logger = logger;
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

    [AllowAnonymous]
    [HttpPost("internal")]
    public async Task<IActionResult> CreateInternalNotification(
        [FromBody] CreateInternalNotificationRequest? request,
        CancellationToken cancellationToken = default)
    {
        if (!IsValidInternalServiceRequest())
        {
            _logger.LogWarning(
                "Customer notification internal create bi tu choi do internal service key khong hop le. UserId={UserId}",
                request?.UserId);
            return Unauthorized(new { success = false, message = "Yeu cau noi bo khong hop le." });
        }

        if (request is null)
        {
            return BadRequest(new { success = false, message = "Payload thong bao khong hop le." });
        }

        if (request.UserId <= 0)
        {
            return BadRequest(new { success = false, message = "UserId khong hop le." });
        }

        var title = NormalizeRequiredText(request.Title, 255);
        var message = NormalizeRequiredText(request.Message, 2000);
        if (string.IsNullOrWhiteSpace(title) || string.IsNullOrWhiteSpace(message))
        {
            return BadRequest(new { success = false, message = "Title va message la bat buoc." });
        }

        var notification = new CustomerNotification
        {
            UserId = request.UserId,
            OrderId = request.OrderId > 0 ? request.OrderId : 0,
            NotificationType = NormalizeNotificationType(request.NotificationType),
            Title = title,
            Message = message,
            IsRead = false,
            IsPushNotification = request.IsPushNotification,
            CreatedAt = request.CreatedAt ?? DateTime.UtcNow,
            ReadAt = null
        };

        _db.CustomerNotifications.Add(notification);
        await _db.SaveChangesAsync(cancellationToken);

        return Ok(new
        {
            success = true,
            message = "Da tao thong bao khach hang.",
            notificationId = notification.NotificationId
        });
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

    private bool IsValidInternalServiceRequest()
    {
        var configuredKey = _internalServiceAuthOptions.InternalServiceKey?.Trim();
        var incomingKey = Request.Headers["X-Internal-Service-Key"].ToString().Trim();

        if (string.IsNullOrWhiteSpace(configuredKey) || string.IsNullOrWhiteSpace(incomingKey))
        {
            return false;
        }

        var configuredBytes = Encoding.UTF8.GetBytes(configuredKey);
        var incomingBytes = Encoding.UTF8.GetBytes(incomingKey);
        return CryptographicOperations.FixedTimeEquals(configuredBytes, incomingBytes);
    }

    private static string NormalizeNotificationType(string? value)
    {
        var normalized = NormalizeRequiredText(value, 50)?.ToLowerInvariant();
        return string.IsNullOrWhiteSpace(normalized)
            ? "seller_review_update"
            : normalized;
    }

    private static string? NormalizeRequiredText(string? value, int maxLength)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return null;
        }

        var trimmed = value.Trim();
        return trimmed.Length <= maxLength
            ? trimmed
            : trimmed[..maxLength];
    }

    public sealed class MarkAllNotificationsReadRequest
    {
        public string? Q { get; set; }

        public string? Type { get; set; }

        public bool? IsRead { get; set; }

        public int? UserId { get; set; }

        public int? OrderId { get; set; }
    }

    public sealed class CreateInternalNotificationRequest
    {
        public int UserId { get; set; }

        public int OrderId { get; set; }

        public string? NotificationType { get; set; }

        public string? Title { get; set; }

        public string? Message { get; set; }

        public bool IsPushNotification { get; set; }

        public DateTime? CreatedAt { get; set; }
    }
}
