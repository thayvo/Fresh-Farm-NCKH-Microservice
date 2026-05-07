using System.IdentityModel.Tokens.Jwt;
using System.Net;
using System.Security.Claims;
using System.Text.Json;
using FreshFarm.Ordering.Api.Controllers;
using FreshFarm.Ordering.Api.Dtos;
using FreshFarm.Ordering.Api.Models;
using FreshFarm.Ordering.Api.Options;
using FreshFarm.Ordering.Api.Services;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace FreshFarm.Ordering.Api.Tests;

public sealed class OrdersControllerTests
{
    [Fact]
    public async Task UpdateAdminOrderStatus_CreatesBuyerNotification_WhenStatusChanges()
    {
        await using var db = CreateDbContext();
        var order = CreateOrder(orderId: 200, userId: 77, status: "Pending");
        order.SellerOrders.Add(new SellerOrder
        {
            SellerOrderId = 1,
            OrderId = 200,
            SellerId = 900,
            SellerStatus = "Pending",
            CommissionRate = 0m,
            CommissionAmount = 0m,
            ShippingFee = 0m,
            SellerEarning = 100m,
            CancelledBy = string.Empty,
            CreatedAt = DateTime.UtcNow
        });

        db.Orders.Add(order);
        await db.SaveChangesAsync();

        var controller = CreateController(db, isAdmin: true);

        var result = await controller.UpdateAdminOrderStatus(200, new OrdersController.UpdateAdminOrderStatusRequest
        {
            NewStatus = "Processing"
        });

        var ok = Assert.IsType<OkObjectResult>(result);
        using var json = JsonDocument.Parse(JsonSerializer.Serialize(ok.Value));
        Assert.Equal("Processing", json.RootElement.GetProperty("status").GetString());

        var notification = await db.CustomerNotifications.SingleAsync();
        Assert.Equal(77, notification.UserId);
        Assert.Equal(200, notification.OrderId);
        Assert.Equal("order_status", notification.NotificationType);
        Assert.Contains("dang xu ly", notification.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task Create_VNPayOrder_UsesDefaultCommissionRate()
    {
        await using var db = CreateDbContext();
        var controller = CreateController(db, isAdmin: false);

        var result = await controller.Create(new CreateOrderRequest
        {
            Items =
            [
                new CreateOrderItemRequest
                {
                    ProductId = 501,
                    SellerId = 900,
                    ProductName = "Test item",
                    Quantity = 1,
                    UnitPrice = 100_000m,
                    UnitSymbol = "kg"
                }
            ],
            ShippingFee = 0m,
            OrderNote = "commission test",
            Shipping = new CreateShippingRequest
            {
                ShippingType = "HomeDelivery",
                FullName = "Buyer",
                Phone = "0123456789",
                Email = "buyer@example.com",
                AddressDetail = "123 FreshFarm"
            },
            Payment = new CreatePaymentRequest
            {
                PaymentMethod = "VNPay"
            }
        }, CancellationToken.None);

        Assert.IsType<CreatedAtActionResult>(result);

        var sellerOrder = await db.SellerOrders.SingleAsync();
        Assert.Equal(0.10m, sellerOrder.CommissionRate);
        Assert.Equal(10_000m, sellerOrder.CommissionAmount);
        Assert.Equal(90_000m, sellerOrder.SellerEarning);
    }

    private static OrdersController CreateController(FreshFarmOrderingDBContext db, bool isAdmin)
    {
        var catalogOptions = Microsoft.Extensions.Options.Options.Create(new CatalogServiceOptions
        {
            BaseUrl = "https://catalog.local",
            InternalServiceKey = "test-catalog-key"
        });
        var internalOptions = Microsoft.Extensions.Options.Options.Create(new InternalServiceAuthOptions
        {
            InternalServiceKey = "test-internal-key"
        });
        var financeOptions = Microsoft.Extensions.Options.Options.Create(new FinanceOptions
        {
            DefaultCommissionRate = 0.10m,
            CommissionRoundingDecimals = 0
        });
        var httpClientFactory = new StubHttpClientFactory();
        var catalogClient = new CatalogInventoryClient(httpClientFactory, catalogOptions);
        var orderReservationService = new OrderReservationService(db, catalogClient);
        var notificationService = new CustomerNotificationService(db);
        var commissionService = new FinanceCommissionService(db, financeOptions);

        var controller = new OrdersController(
            db,
            catalogClient,
            orderReservationService,
            notificationService,
            commissionService,
            internalOptions,
            NullLogger<OrdersController>.Instance)
        {
            ControllerContext = new ControllerContext
            {
                HttpContext = BuildHttpContext(isAdmin)
            }
        };

        return controller;
    }

    private static DefaultHttpContext BuildHttpContext(bool isAdmin)
    {
        var claims = new List<Claim>
        {
            new(JwtRegisteredClaimNames.Sub, "1")
        };

        if (isAdmin)
        {
            claims.Add(new Claim(ClaimTypes.Role, "Admin"));
        }

        var context = new DefaultHttpContext();
        context.User = new ClaimsPrincipal(new ClaimsIdentity(claims, "Test"));
        return context;
    }

    private static FreshFarmOrderingDBContext CreateDbContext()
    {
        var options = new DbContextOptionsBuilder<FreshFarmOrderingDBContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString("N"))
            .ConfigureWarnings(warnings => warnings.Ignore(InMemoryEventId.TransactionIgnoredWarning))
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

    private sealed class StubHttpClientFactory : IHttpClientFactory
    {
        public HttpClient CreateClient(string name)
            => new(new StubHttpMessageHandler())
            {
                BaseAddress = new Uri("https://catalog.local")
            };
    }

    private sealed class StubHttpMessageHandler : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent("{}")
            });
        }
    }
}
