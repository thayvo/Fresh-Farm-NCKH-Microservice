using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Text.Json;
using FreshFarm.Ordering.Api.Controllers;
using FreshFarm.Ordering.Api.Models;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace FreshFarm.Ordering.Api.Tests;

public sealed class NotificationsControllerTests
{
    [Fact]
    public async Task GetMyNotifications_ReturnsScopedUnreadStatsAndFilters()
    {
        await using var db = CreateDbContext();
        db.CustomerNotifications.AddRange(
            new CustomerNotification
            {
                NotificationId = 1,
                UserId = 47,
                OrderId = 0,
                NotificationType = "seller_review_update",
                Title = "Ho so seller da duoc duyet",
                Message = "Ban da duoc cap quyen seller.",
                IsRead = false,
                IsPushNotification = false,
                CreatedAt = new DateTime(2026, 3, 23, 1, 0, 0, DateTimeKind.Utc)
            },
            new CustomerNotification
            {
                NotificationId = 2,
                UserId = 47,
                OrderId = 0,
                NotificationType = "seller_review_update",
                Title = "Ho so seller can bo sung",
                Message = "Can bo sung mat sau CCCD.",
                IsRead = true,
                ReadAt = new DateTime(2026, 3, 23, 2, 0, 0, DateTimeKind.Utc),
                IsPushNotification = false,
                CreatedAt = new DateTime(2026, 3, 23, 1, 30, 0, DateTimeKind.Utc)
            },
            new CustomerNotification
            {
                NotificationId = 3,
                UserId = 48,
                OrderId = 0,
                NotificationType = "order_status",
                Title = "Don hang dang giao",
                Message = "Don cua ban dang tren duong giao.",
                IsRead = false,
                IsPushNotification = true,
                CreatedAt = new DateTime(2026, 3, 23, 3, 0, 0, DateTimeKind.Utc)
            });
        await db.SaveChangesAsync();

        var controller = CreateController(db, 47);

        var result = await controller.GetMyNotifications(type: "seller_review_update", isRead: false, cancellationToken: CancellationToken.None);

        var ok = Assert.IsType<OkObjectResult>(result);
        using var json = JsonDocument.Parse(JsonSerializer.Serialize(ok.Value));
        Assert.Equal(1, json.RootElement.GetProperty("total").GetInt32());
        Assert.Equal(2, json.RootElement.GetProperty("stats").GetProperty("totalNotifications").GetInt32());
        Assert.Equal(1, json.RootElement.GetProperty("stats").GetProperty("unreadNotifications").GetInt32());
        Assert.Equal("seller_review_update", json.RootElement.GetProperty("filters").GetProperty("type").GetString());
        var notifications = json.RootElement.GetProperty("notifications").EnumerateArray().ToList();
        Assert.Single(notifications);
        Assert.Equal(47, notifications[0].GetProperty("userId").GetInt32());
    }

    [Fact]
    public async Task GetMyNotifications_FiltersRecentOnlyOnServer()
    {
        await using var db = CreateDbContext();
        db.CustomerNotifications.AddRange(
            new CustomerNotification
            {
                NotificationId = 31,
                UserId = 47,
                OrderId = 0,
                NotificationType = "location_update",
                Title = "Moi",
                Message = "Van chuyen vua cap nhat.",
                IsRead = false,
                CreatedAt = DateTime.UtcNow.AddHours(-3)
            },
            new CustomerNotification
            {
                NotificationId = 32,
                UserId = 47,
                OrderId = 0,
                NotificationType = "location_update",
                Title = "Cu",
                Message = "Van chuyen da cap nhat tu lau.",
                IsRead = false,
                CreatedAt = DateTime.UtcNow.AddDays(-3)
            });
        await db.SaveChangesAsync();

        var controller = CreateController(db, 47);

        var result = await controller.GetMyNotifications(type: "location_update", recentOnly: true, cancellationToken: CancellationToken.None);

        var ok = Assert.IsType<OkObjectResult>(result);
        using var json = JsonDocument.Parse(JsonSerializer.Serialize(ok.Value));
        Assert.True(json.RootElement.GetProperty("filters").GetProperty("recentOnly").GetBoolean());
        var notifications = json.RootElement.GetProperty("notifications").EnumerateArray().ToList();
        Assert.Single(notifications);
        Assert.Equal(31, notifications[0].GetProperty("notificationId").GetInt32());
    }

    [Fact]
    public async Task MarkAsRead_UpdatesOnlyCurrentUsersNotification()
    {
        await using var db = CreateDbContext();
        db.CustomerNotifications.AddRange(
            new CustomerNotification
            {
                NotificationId = 10,
                UserId = 47,
                OrderId = 0,
                NotificationType = "seller_review_update",
                Title = "Can bo sung",
                Message = "Bo sung giay to.",
                IsRead = false,
                CreatedAt = DateTime.UtcNow
            },
            new CustomerNotification
            {
                NotificationId = 11,
                UserId = 48,
                OrderId = 0,
                NotificationType = "seller_review_update",
                Title = "Nguoi khac",
                Message = "Khong duoc dong vao.",
                IsRead = false,
                CreatedAt = DateTime.UtcNow
            });
        await db.SaveChangesAsync();

        var controller = CreateController(db, 47);

        var result = await controller.MarkAsRead(10, CancellationToken.None);

        Assert.IsType<OkObjectResult>(result);
        Assert.True((await db.CustomerNotifications.SingleAsync(x => x.NotificationId == 10)).IsRead);
        Assert.False((await db.CustomerNotifications.SingleAsync(x => x.NotificationId == 11)).IsRead);
    }

    [Fact]
    public async Task MarkAsRead_ReturnsNotFound_WhenNotificationBelongsToAnotherUser()
    {
        await using var db = CreateDbContext();
        db.CustomerNotifications.Add(new CustomerNotification
        {
            NotificationId = 12,
            UserId = 48,
            OrderId = 0,
            NotificationType = "seller_review_update",
            Title = "Nguoi khac",
            Message = "Khong duoc thay.",
            IsRead = false,
            CreatedAt = DateTime.UtcNow
        });
        await db.SaveChangesAsync();

        var controller = CreateController(db, 47);

        var result = await controller.MarkAsRead(12, CancellationToken.None);

        Assert.IsType<NotFoundObjectResult>(result);
    }

    [Fact]
    public async Task MarkAllAsRead_OnlyMarksScopedUnreadNotifications()
    {
        await using var db = CreateDbContext();
        db.CustomerNotifications.AddRange(
            new CustomerNotification
            {
                NotificationId = 20,
                UserId = 47,
                OrderId = 0,
                NotificationType = "seller_review_update",
                Title = "Unread 1",
                Message = "Unread 1",
                IsRead = false,
                CreatedAt = DateTime.UtcNow
            },
            new CustomerNotification
            {
                NotificationId = 21,
                UserId = 47,
                OrderId = 0,
                NotificationType = "order_status",
                Title = "Unread 2",
                Message = "Unread 2",
                IsRead = false,
                CreatedAt = DateTime.UtcNow
            },
            new CustomerNotification
            {
                NotificationId = 22,
                UserId = 47,
                OrderId = 0,
                NotificationType = "seller_review_update",
                Title = "Read already",
                Message = "Read already",
                IsRead = true,
                ReadAt = DateTime.UtcNow,
                CreatedAt = DateTime.UtcNow
            },
            new CustomerNotification
            {
                NotificationId = 23,
                UserId = 48,
                OrderId = 0,
                NotificationType = "seller_review_update",
                Title = "Other user",
                Message = "Other user",
                IsRead = false,
                CreatedAt = DateTime.UtcNow
            });
        await db.SaveChangesAsync();

        var controller = CreateController(db, 47);

        var result = await controller.MarkAllAsRead(
            new NotificationsController.MarkMyNotificationsReadRequest
            {
                Type = "seller_review_update"
            },
            CancellationToken.None);

        var ok = Assert.IsType<OkObjectResult>(result);
        using var json = JsonDocument.Parse(JsonSerializer.Serialize(ok.Value));
        Assert.Equal(1, json.RootElement.GetProperty("affected").GetInt32());
        Assert.True((await db.CustomerNotifications.SingleAsync(x => x.NotificationId == 20)).IsRead);
        Assert.False((await db.CustomerNotifications.SingleAsync(x => x.NotificationId == 21)).IsRead);
        Assert.False((await db.CustomerNotifications.SingleAsync(x => x.NotificationId == 23)).IsRead);
    }

    [Fact]
    public async Task MarkAllAsRead_HonorsRecentOnlyFilter()
    {
        await using var db = CreateDbContext();
        db.CustomerNotifications.AddRange(
            new CustomerNotification
            {
                NotificationId = 40,
                UserId = 47,
                OrderId = 0,
                NotificationType = "order_status",
                Title = "Moi",
                Message = "Moi",
                IsRead = false,
                CreatedAt = DateTime.UtcNow.AddHours(-2)
            },
            new CustomerNotification
            {
                NotificationId = 41,
                UserId = 47,
                OrderId = 0,
                NotificationType = "order_status",
                Title = "Cu",
                Message = "Cu",
                IsRead = false,
                CreatedAt = DateTime.UtcNow.AddDays(-2)
            });
        await db.SaveChangesAsync();

        var controller = CreateController(db, 47);

        var result = await controller.MarkAllAsRead(
            new NotificationsController.MarkMyNotificationsReadRequest
            {
                Type = "order_status",
                RecentOnly = true
            },
            CancellationToken.None);

        var ok = Assert.IsType<OkObjectResult>(result);
        using var json = JsonDocument.Parse(JsonSerializer.Serialize(ok.Value));
        Assert.Equal(1, json.RootElement.GetProperty("affected").GetInt32());
        Assert.True((await db.CustomerNotifications.SingleAsync(x => x.NotificationId == 40)).IsRead);
        Assert.False((await db.CustomerNotifications.SingleAsync(x => x.NotificationId == 41)).IsRead);
    }

    private static NotificationsController CreateController(FreshFarmOrderingDBContext db, int userId)
    {
        var controller = new NotificationsController(db)
        {
            ControllerContext = new ControllerContext
            {
                HttpContext = BuildHttpContext(userId)
            }
        };

        return controller;
    }

    private static DefaultHttpContext BuildHttpContext(int userId)
    {
        var context = new DefaultHttpContext();
        context.User = new ClaimsPrincipal(new ClaimsIdentity(
        [
            new Claim(JwtRegisteredClaimNames.Sub, userId.ToString())
        ], "Test"));
        return context;
    }

    private static FreshFarmOrderingDBContext CreateDbContext()
    {
        var options = new DbContextOptionsBuilder<FreshFarmOrderingDBContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString("N"))
            .Options;
        return new FreshFarmOrderingDBContext(options);
    }
}
