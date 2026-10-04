using FreshFarm.Ordering.Api.Models;
using FreshFarm.Ordering.Api.Services;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace FreshFarm.Ordering.Api.Tests;

public sealed class CustomerNotificationServiceTests
{
    [Fact]
    public async Task PublishOrderStatusUpdateAsync_CreatesNotification_WhenStatusChanges()
    {
        await using var db = CreateDbContext();
        var service = new CustomerNotificationService(db);
        var order = CreateOrder(orderId: 120, userId: 47, status: "Processing");

        var created = await service.PublishOrderStatusUpdateAsync(order, "Pending", order.Status, CancellationToken.None);

        Assert.True(created);
        var notification = await db.CustomerNotifications.SingleAsync();
        Assert.Equal(47, notification.UserId);
        Assert.Equal(120, notification.OrderId);
        Assert.Equal("order_status", notification.NotificationType);
        Assert.Contains("trang thai", notification.Title, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("dang xu ly", notification.Message, StringComparison.OrdinalIgnoreCase);
        Assert.False(notification.IsRead ?? true);
    }

    [Fact]
    public async Task PublishOrderStatusUpdateAsync_Skips_WhenStatusUnchanged()
    {
        await using var db = CreateDbContext();
        var service = new CustomerNotificationService(db);
        var order = CreateOrder(orderId: 121, userId: 47, status: "Pending");

        var created = await service.PublishOrderStatusUpdateAsync(order, "Pending", order.Status, CancellationToken.None);

        Assert.False(created);
        Assert.Empty(db.CustomerNotifications);
    }

    [Fact]
    public async Task PublishShippingStatusUpdateAsync_CreatesNotification_WhenGhnStatusChanges()
    {
        await using var db = CreateDbContext();
        var service = new CustomerNotificationService(db);
        var order = CreateOrder(orderId: 130, userId: 52, status: "Shipped");
        var shipping = CreateShipping(order, "ready_to_pick", "San sang lay hang");

        var created = await service.PublishShippingStatusUpdateAsync(order, shipping, "sorting", "Dang phan loai", CancellationToken.None);

        Assert.True(created);
        var notification = await db.CustomerNotifications.SingleAsync();
        Assert.Equal("location_update", notification.NotificationType);
        Assert.Equal(130, notification.OrderId);
        Assert.Contains("San sang lay hang", notification.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task PublishShippingStatusUpdateAsync_SkipsDuplicateLatestNotification()
    {
        await using var db = CreateDbContext();
        db.CustomerNotifications.Add(new CustomerNotification
        {
            NotificationId = 1,
            UserId = 52,
            OrderId = 131,
            NotificationType = "location_update",
            Title = "Van chuyen don #000131 da duoc cap nhat",
            Message = "Trang thai giao hang moi nhat: San sang lay hang.",
            IsRead = false,
            CreatedAt = DateTime.UtcNow
        });
        await db.SaveChangesAsync();

        var service = new CustomerNotificationService(db);
        var order = CreateOrder(orderId: 131, userId: 52, status: "Shipped");
        var shipping = CreateShipping(order, "ready_to_pick", "San sang lay hang");

        var created = await service.PublishShippingStatusUpdateAsync(order, shipping, "sorting", "Dang phan loai", CancellationToken.None);

        Assert.False(created);
        Assert.Single(db.CustomerNotifications);
    }

    private static FreshFarmOrderingDBContext CreateDbContext()
    {
        var options = new DbContextOptionsBuilder<FreshFarmOrderingDBContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString("N"))
            .Options;
        return new FreshFarmOrderingDBContext(options);
    }

    private static Order CreateOrder(int orderId, int userId, string status)
    {
        return new Order
        {
            OrderId = orderId,
            UserId = userId,
            OrderDate = DateTime.UtcNow,
            ShippingFee = 0m,
            TotalAmount = 100m,
            OrderNote = string.Empty,
            Status = status,
            PaymentStatus = "Pending",
            BuyerFullName = "Buyer",
            BuyerPhone = "0123456789",
            BuyerEmail = "buyer@example.com",
            PointsEarned = 0,
            PointsRedeemed = 0
        };
    }

    private static Shipping CreateShipping(Order order, string ghnStatus, string ghnStatusLabel)
    {
        return new Shipping
        {
            ShippingId = order.OrderId,
            OrderId = order.OrderId,
            ShippingType = "HomeDelivery",
            FullName = "Receiver",
            Phone = "0123456789",
            Email = "receiver@example.com",
            AddressDetail = "Address",
            GhnStatus = ghnStatus,
            GhnStatusLabel = ghnStatusLabel,
            Order = order
        };
    }
}
