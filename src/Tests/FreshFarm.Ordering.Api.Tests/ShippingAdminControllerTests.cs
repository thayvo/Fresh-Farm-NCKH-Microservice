using System.Text.Json;
using FreshFarm.Ordering.Api.Controllers;
using FreshFarm.Ordering.Api.Models;
using FreshFarm.Ordering.Api.Options;
using FreshFarm.Ordering.Api.Services;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Xunit;

namespace FreshFarm.Ordering.Api.Tests;

public sealed class ShippingAdminControllerTests
{
    [Fact]
    public async Task GetInternalGhnSyncCandidates_ReturnsOnlyEligibleRows_InStaleOrder()
    {
        await using var db = CreateDbContext();
        var now = DateTime.UtcNow;

        db.Orders.AddRange(
            CreateOrder(1, "Pending"),
            CreateOrder(2, "Pending"),
            CreateOrder(3, "Canceled"),
            CreateOrder(4, "Pending"));

        db.Shippings.AddRange(
            CreateShipping(1, "GHN-OLD", ghnLastSyncedAt: now.AddHours(-3), ghnStatus: null),
            CreateShipping(2, "GHN-TERMINAL", ghnLastSyncedAt: now.AddHours(-5), ghnStatus: "delivered"),
            CreateShipping(3, "GHN-CANCELED", ghnLastSyncedAt: now.AddHours(-4), ghnStatus: null),
            CreateShipping(4, "GHN-NEWER", ghnLastSyncedAt: now.AddMinutes(-30), ghnStatus: null));

        await db.SaveChangesAsync();

        var controller = CreateController(db);
        controller.ControllerContext = new ControllerContext
        {
            HttpContext = BuildInternalHttpContext()
        };

        var result = await controller.GetInternalGhnSyncCandidates(limit: 10, staleMinutes: 20, cancellationToken: CancellationToken.None);

        var ok = Assert.IsType<OkObjectResult>(result);
        using var json = JsonDocument.Parse(JsonSerializer.Serialize(ok.Value));
        var items = json.RootElement.GetProperty("items");

        Assert.Equal(2, items.GetArrayLength());
        Assert.Equal(1, items[0].GetProperty("OrderId").GetInt32());
        Assert.Equal("GHN-OLD", items[0].GetProperty("OrderCode").GetString());
        Assert.Equal(4, items[1].GetProperty("OrderId").GetInt32());
        Assert.Equal("GHN-NEWER", items[1].GetProperty("OrderCode").GetString());
    }

    [Fact]
    public async Task UpsertGhnMetadataInternalByCode_UpdatesMatchingShipping()
    {
        await using var db = CreateDbContext();
        db.Orders.Add(CreateOrder(10, "Pending"));
        db.Shippings.Add(CreateShipping(10, "GHN-CODE", clientOrderCode: "CLIENT-CODE"));
        await db.SaveChangesAsync();

        var controller = CreateController(db);
        controller.ControllerContext = new ControllerContext
        {
            HttpContext = BuildInternalHttpContext()
        };

        var result = await controller.UpsertGhnMetadataInternalByCode(new ShippingAdminController.GhnMetadataByCodeUpsertRequest
        {
            OrderCode = "GHN-CODE",
            ClientOrderCode = "CLIENT-CODE",
            Status = "ready_to_pick",
            StatusLabel = "San sang lay hang",
            TotalFee = 20900,
            CreatedAt = new DateTime(2026, 3, 22, 2, 43, 32, DateTimeKind.Utc),
            ExpectedDeliveryTime = new DateTime(2026, 3, 23, 16, 59, 59, DateTimeKind.Utc),
            LastSyncedAt = new DateTime(2026, 3, 22, 2, 45, 43, DateTimeKind.Utc)
        }, CancellationToken.None);

        var ok = Assert.IsType<OkObjectResult>(result);
        using var json = JsonDocument.Parse(JsonSerializer.Serialize(ok.Value));
        Assert.True(json.RootElement.GetProperty("success").GetBoolean());

        var shipping = await db.Shippings.SingleAsync(x => x.OrderId == 10);
        Assert.Equal("ready_to_pick", shipping.GhnStatus);
        Assert.Equal("San sang lay hang", shipping.GhnStatusLabel);
        Assert.Equal(20900, shipping.GhnTotalFee);
        Assert.Equal(new DateTime(2026, 3, 22, 2, 43, 32, DateTimeKind.Utc), shipping.GhnCreatedAt);
        Assert.Equal(new DateTime(2026, 3, 23, 16, 59, 59, DateTimeKind.Utc), shipping.GhnExpectedDeliveryTime);
        Assert.Equal(new DateTime(2026, 3, 22, 2, 45, 43, DateTimeKind.Utc), shipping.GhnLastSyncedAt);
        var notification = await db.CustomerNotifications.SingleAsync();
        Assert.Equal(10, notification.OrderId);
        Assert.Equal(1, notification.UserId);
        Assert.Equal("location_update", notification.NotificationType);
        Assert.Contains("San sang lay hang", notification.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task GetInternalGhnSyncCandidates_ReturnsUnauthorized_WhenInternalKeyMissing()
    {
        await using var db = CreateDbContext();
        var controller = CreateController(db);
        controller.ControllerContext = new ControllerContext
        {
            HttpContext = new DefaultHttpContext()
        };

        var result = await controller.GetInternalGhnSyncCandidates(limit: 10, staleMinutes: 20, cancellationToken: CancellationToken.None);

        var unauthorized = Assert.IsType<UnauthorizedObjectResult>(result);
        using var json = JsonDocument.Parse(JsonSerializer.Serialize(unauthorized.Value));
        Assert.False(json.RootElement.GetProperty("success").GetBoolean());
    }

    [Fact]
    public async Task UpsertGhnMetadataInternalByCode_ReturnsBadRequest_WhenCodesMissing()
    {
        await using var db = CreateDbContext();
        var controller = CreateController(db);
        controller.ControllerContext = new ControllerContext
        {
            HttpContext = BuildInternalHttpContext()
        };

        var result = await controller.UpsertGhnMetadataInternalByCode(
            new ShippingAdminController.GhnMetadataByCodeUpsertRequest
            {
                OrderCode = " ",
                ClientOrderCode = null
            },
            CancellationToken.None);

        var badRequest = Assert.IsType<BadRequestObjectResult>(result);
        using var json = JsonDocument.Parse(JsonSerializer.Serialize(badRequest.Value));
        Assert.False(json.RootElement.GetProperty("success").GetBoolean());
    }

    [Fact]
    public async Task UpsertGhnMetadataInternalByCode_ReturnsNotFound_WhenShippingDoesNotExist()
    {
        await using var db = CreateDbContext();
        db.Orders.Add(CreateOrder(20, "Pending"));
        await db.SaveChangesAsync();

        var controller = CreateController(db);
        controller.ControllerContext = new ControllerContext
        {
            HttpContext = BuildInternalHttpContext()
        };

        var result = await controller.UpsertGhnMetadataInternalByCode(
            new ShippingAdminController.GhnMetadataByCodeUpsertRequest
            {
                OrderCode = "GHN-MISSING",
                ClientOrderCode = "CLIENT-MISSING"
            },
            CancellationToken.None);

        var notFound = Assert.IsType<NotFoundObjectResult>(result);
        using var json = JsonDocument.Parse(JsonSerializer.Serialize(notFound.Value));
        Assert.False(json.RootElement.GetProperty("success").GetBoolean());
    }

    private static ShippingAdminController CreateController(FreshFarmOrderingDBContext db)
    {
        return new ShippingAdminController(
            db,
            new CustomerNotificationService(db),
            Microsoft.Extensions.Options.Options.Create(new InternalServiceAuthOptions
            {
                InternalServiceKey = "test-internal-key"
            }),
            NullLogger<ShippingAdminController>.Instance);
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

    private static Order CreateOrder(int orderId, string status)
    {
        return new Order
        {
            OrderId = orderId,
            UserId = 1,
            OrderDate = DateTime.UtcNow,
            ShippingFee = 0,
            TotalAmount = 100,
            OrderNote = string.Empty,
            Status = status,
            PaymentStatus = "Pending",
            BuyerFullName = "Test Buyer",
            BuyerPhone = "0123456789",
            BuyerEmail = "buyer@example.com",
            PointsEarned = 0,
            PointsRedeemed = 0
        };
    }

    private static Shipping CreateShipping(
        int orderId,
        string orderCode,
        DateTime? ghnLastSyncedAt = null,
        string? ghnStatus = null,
        string? clientOrderCode = null)
    {
        return new Shipping
        {
            ShippingId = orderId,
            OrderId = orderId,
            ShippingType = "HomeDelivery",
            FullName = "Test Receiver",
            Phone = "0123456789",
            Email = "receiver@example.com",
            AddressDetail = "Address",
            GhnOrderCode = orderCode,
            GhnClientOrderCode = clientOrderCode,
            GhnStatus = ghnStatus,
            GhnLastSyncedAt = ghnLastSyncedAt,
            Order = null!
        };
    }
}
