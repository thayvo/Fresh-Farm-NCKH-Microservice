using System.Text.Json;
using FreshFarm.Ordering.Api.Controllers;
using FreshFarm.Ordering.Api.Models;
using FreshFarm.Ordering.Api.Options;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace FreshFarm.Ordering.Api.Tests;

public sealed class NotificationsAdminControllerTests
{
    [Fact]
    public async Task CreateInternalNotification_PersistsCustomerNotification_WhenRequestValid()
    {
        await using var db = CreateDbContext();
        var controller = CreateController(db);
        controller.ControllerContext = new ControllerContext
        {
            HttpContext = BuildInternalHttpContext()
        };

        var result = await controller.CreateInternalNotification(new NotificationsAdminController.CreateInternalNotificationRequest
        {
            UserId = 47,
            OrderId = 0,
            NotificationType = "seller_review_update",
            Title = "Ho so nguoi ban da duoc duyet",
            Message = "Tai khoan cua ban da co quyen nguoi ban va co the bat dau van hanh gian hang.",
            IsPushNotification = false,
            CreatedAt = new DateTime(2026, 3, 23, 3, 0, 0, DateTimeKind.Utc)
        }, CancellationToken.None);

        var ok = Assert.IsType<OkObjectResult>(result);
        using var json = JsonDocument.Parse(JsonSerializer.Serialize(ok.Value));
        Assert.True(json.RootElement.GetProperty("success").GetBoolean());

        var notification = await db.CustomerNotifications.SingleAsync();
        Assert.Equal(47, notification.UserId);
        Assert.Equal(0, notification.OrderId);
        Assert.Equal("seller_review_update", notification.NotificationType);
        Assert.Equal("Ho so nguoi ban da duoc duyet", notification.Title);
        Assert.Contains("quyen nguoi ban", notification.Message);
        Assert.False(notification.IsRead ?? true);
    }

    [Fact]
    public async Task CreateInternalNotification_ReturnsUnauthorized_WhenInternalKeyMissing()
    {
        await using var db = CreateDbContext();
        var controller = CreateController(db);
        controller.ControllerContext = new ControllerContext
        {
            HttpContext = new DefaultHttpContext()
        };

        var result = await controller.CreateInternalNotification(
            new NotificationsAdminController.CreateInternalNotificationRequest
            {
                UserId = 47,
                Title = "Test",
                Message = "Test"
            },
            CancellationToken.None);

        var unauthorized = Assert.IsType<UnauthorizedObjectResult>(result);
        using var json = JsonDocument.Parse(JsonSerializer.Serialize(unauthorized.Value));
        Assert.False(json.RootElement.GetProperty("success").GetBoolean());
    }

    [Fact]
    public async Task CreateInternalNotification_ReturnsBadRequest_WhenTitleOrMessageMissing()
    {
        await using var db = CreateDbContext();
        var controller = CreateController(db);
        controller.ControllerContext = new ControllerContext
        {
            HttpContext = BuildInternalHttpContext()
        };

        var result = await controller.CreateInternalNotification(
            new NotificationsAdminController.CreateInternalNotificationRequest
            {
                UserId = 47,
                Title = " ",
                Message = null
            },
            CancellationToken.None);

        var badRequest = Assert.IsType<BadRequestObjectResult>(result);
        using var json = JsonDocument.Parse(JsonSerializer.Serialize(badRequest.Value));
        Assert.False(json.RootElement.GetProperty("success").GetBoolean());
    }

    private static NotificationsAdminController CreateController(FreshFarmOrderingDBContext db)
    {
        return new NotificationsAdminController(
            db,
            Microsoft.Extensions.Options.Options.Create(new InternalServiceAuthOptions
            {
                InternalServiceKey = "test-internal-key"
            }),
            NullLogger<NotificationsAdminController>.Instance);
    }

    private static DefaultHttpContext BuildInternalHttpContext()
    {
        var context = new DefaultHttpContext();
        context.Request.Headers["X-Internal-Service-Key"] = "test-internal-key";
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
