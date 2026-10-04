using FreshFarm.Ordering.Api.Models;
using Microsoft.EntityFrameworkCore;

namespace FreshFarm.Ordering.Api.Services;

public sealed class CustomerNotificationService
{
    private readonly FreshFarmOrderingDBContext _db;

    public CustomerNotificationService(FreshFarmOrderingDBContext db)
    {
        _db = db;
    }

    public async Task<bool> PublishOrderStatusUpdateAsync(
        Order order,
        string? previousStatus,
        string? currentStatus,
        CancellationToken cancellationToken)
    {
        var normalizedPreviousStatus = NormalizeNullable(previousStatus);
        var normalizedCurrentStatus = NormalizeNullable(currentStatus);
        if (order.UserId <= 0 || string.IsNullOrWhiteSpace(normalizedCurrentStatus))
        {
            return false;
        }

        if (string.Equals(normalizedPreviousStatus, normalizedCurrentStatus, StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        var statusText = GetOrderStatusText(normalizedCurrentStatus);
        var title = $"Don hang #{order.OrderId:D6} da cap nhat trang thai";
        var message = $"Don hang cua ban da chuyen sang trang thai {statusText}.";

        return await AddNotificationIfChangedAsync(
            order.UserId,
            order.OrderId,
            "order_status",
            title,
            message,
            cancellationToken);
    }

    public async Task<bool> PublishShippingStatusUpdateAsync(
        Order? order,
        Shipping shipping,
        string? previousGhnStatus,
        string? previousGhnStatusLabel,
        CancellationToken cancellationToken)
    {
        var resolvedOrder = order ?? shipping.Order;
        if (resolvedOrder is null || resolvedOrder.UserId <= 0)
        {
            return false;
        }

        var normalizedPreviousStatus = NormalizeNullable(previousGhnStatus);
        var normalizedCurrentStatus = NormalizeNullable(shipping.GhnStatus);
        var normalizedPreviousLabel = NormalizeNullable(previousGhnStatusLabel);
        var normalizedCurrentLabel = NormalizeNullable(shipping.GhnStatusLabel);
        if (string.IsNullOrWhiteSpace(normalizedCurrentStatus) && string.IsNullOrWhiteSpace(normalizedCurrentLabel))
        {
            return false;
        }

        if (string.Equals(normalizedPreviousStatus, normalizedCurrentStatus, StringComparison.OrdinalIgnoreCase) &&
            string.Equals(normalizedPreviousLabel, normalizedCurrentLabel, StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        var shippingText = !string.IsNullOrWhiteSpace(normalizedCurrentLabel)
            ? normalizedCurrentLabel
            : normalizedCurrentStatus ?? "dang duoc cap nhat";
        var title = $"Van chuyen don #{resolvedOrder.OrderId:D6} da duoc cap nhat";
        var message = $"Trang thai giao hang moi nhat: {shippingText}.";

        return await AddNotificationIfChangedAsync(
            resolvedOrder.UserId,
            resolvedOrder.OrderId,
            "location_update",
            title,
            message,
            cancellationToken);
    }

    private async Task<bool> AddNotificationIfChangedAsync(
        int userId,
        int orderId,
        string notificationType,
        string title,
        string message,
        CancellationToken cancellationToken)
    {
        var latest = await _db.CustomerNotifications
            .AsNoTracking()
            .Where(x => x.UserId == userId && x.OrderId == orderId && x.NotificationType == notificationType)
            .OrderByDescending(x => x.CreatedAt ?? DateTime.MinValue)
            .ThenByDescending(x => x.NotificationId)
            .Select(x => new
            {
                x.Title,
                x.Message
            })
            .FirstOrDefaultAsync(cancellationToken);

        if (latest is not null &&
            string.Equals(latest.Title, title, StringComparison.Ordinal) &&
            string.Equals(latest.Message, message, StringComparison.Ordinal))
        {
            return false;
        }

        _db.CustomerNotifications.Add(new CustomerNotification
        {
            UserId = userId,
            OrderId = orderId,
            NotificationType = notificationType,
            Title = title,
            Message = message,
            IsRead = false,
            IsPushNotification = false,
            PopupType = "none",
            PopupImageUrl = null,
            CreatedAt = DateTime.UtcNow,
            ExpiresAt = null
        });

        await _db.SaveChangesAsync(cancellationToken);
        return true;
    }

    private static string? NormalizeNullable(string? value)
    {
        return string.IsNullOrWhiteSpace(value)
            ? null
            : value.Trim();
    }

    private static string GetOrderStatusText(string status)
    {
        return status switch
        {
            "AwaitingPayment" => "cho thanh toan",
            "Pending" => "cho xu ly",
            "Processing" => "dang xu ly",
            "Ready" => "san sang giao",
            "Shipped" => "dang giao hang",
            "Delivered" => "da giao thanh cong",
            "Expired" => "da het han thanh toan",
            "Canceled" => "da bi huy",
            _ => status
        };
    }
}
