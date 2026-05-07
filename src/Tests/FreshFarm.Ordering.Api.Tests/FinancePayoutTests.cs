using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Text.Json;
using FreshFarm.Ordering.Api.Controllers;
using FreshFarm.Ordering.Api.Models;
using FreshFarm.Ordering.Api.Services;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Xunit;

namespace FreshFarm.Ordering.Api.Tests;

public sealed class FinancePayoutTests
{
    [Fact]
    public async Task GetConsole_ReturnsCommissionAndSellerEarning()
    {
        await using var db = CreateDbContext();
        SeedPaidSellerOrder(db, orderId: 1, sellerId: 10, commissionAmount: 10_000m, sellerEarning: 90_000m);
        await db.SaveChangesAsync();

        var controller = CreateFinanceController(db, isAdmin: true, subjectId: 1);
        var result = await controller.GetConsole(cancellationToken: CancellationToken.None);

        var ok = Assert.IsType<OkObjectResult>(result);
        using var json = JsonDocument.Parse(JsonSerializer.Serialize(ok.Value));
        var stats = json.RootElement.GetProperty("stats");
        Assert.Equal(100_000m, stats.GetProperty("grossMerchandiseValue").GetDecimal());
        Assert.Equal(10_000m, stats.GetProperty("platformCommission").GetDecimal());
        Assert.Equal(90_000m, stats.GetProperty("sellerEarning").GetDecimal());
    }

    [Fact]
    public async Task GeneratePendingPayouts_CreatesGrossFeeNetItems()
    {
        await using var db = CreateDbContext();
        SeedPaidSellerOrder(db, orderId: 1, sellerId: 10, commissionAmount: 10_000m, sellerEarning: 90_000m);
        await db.SaveChangesAsync();

        var service = new PayoutGenerationService(db);
        var result = await service.GeneratePendingPayoutsAsync(cancellationToken: CancellationToken.None);

        Assert.Equal(1, result.CreatedPayoutCount);
        Assert.Equal(1, result.CreatedPayoutItemCount);
        Assert.Equal(100_000m, result.AmountGross);
        Assert.Equal(10_000m, result.FeeAmount);
        Assert.Equal(90_000m, result.AmountNet);

        var payout = await db.Payouts.Include(x => x.PayoutItems).SingleAsync();
        Assert.Equal("pending", payout.Status);
        Assert.Equal(100_000m, payout.AmountGross);
        Assert.Equal(10_000m, payout.FeeAmount);
        Assert.Equal(90_000m, payout.AmountNet);
        Assert.Equal(90_000m, payout.PayoutItems.Single().Amount);

        var duplicateAttempt = await service.GeneratePendingPayoutsAsync(cancellationToken: CancellationToken.None);
        Assert.Equal(0, duplicateAttempt.CreatedPayoutItemCount);
    }

    [Fact]
    public async Task GeneratePendingPayouts_SkipsUnpaidCancelledRefundedReturnedAndCodOrders()
    {
        await using var db = CreateDbContext();
        SeedPaidSellerOrder(db, orderId: 1, sellerId: 10, commissionAmount: 1_000m, sellerEarning: 9_000m, orderPaymentStatus: "Pending", includeTransaction: false);
        SeedPaidSellerOrder(db, orderId: 2, sellerId: 10, commissionAmount: 1_000m, sellerEarning: 9_000m, orderStatus: "Canceled");
        var refundedTxn = SeedPaidSellerOrder(db, orderId: 3, sellerId: 10, commissionAmount: 1_000m, sellerEarning: 9_000m);
        db.RefundTransactions.Add(new RefundTransaction
        {
            RefundId = 300,
            PaymentTxnId = refundedTxn.PaymentTxnId,
            PaymentTxn = refundedTxn,
            Amount = 10_000m,
            Status = "approved",
            Channel = "VNPay",
            ReferenceCode = "RF300",
            CreatedAt = DateTime.UtcNow
        });
        SeedPaidSellerOrder(db, orderId: 4, sellerId: 10, commissionAmount: 1_000m, sellerEarning: 9_000m, paymentMethod: "COD");
        var returnedSellerOrder = SeedPaidSellerOrder(db, orderId: 5, sellerId: 10, commissionAmount: 1_000m, sellerEarning: 9_000m).Order.SellerOrders.Single();
        var returnedItem = new SellerOrderItem
        {
            SellerOrderItemId = 500,
            SellerOrderId = returnedSellerOrder.SellerOrderId,
            SellerOrder = returnedSellerOrder,
            ListingId = 500,
            ProductId = 500,
            Quantity = 1,
            UnitPrice = 10_000m,
            DiscountAmount = 0m,
            SnapshotName = "Returned item",
            SnapshotAttributes = "{}",
            FulfillmentType = "SellerShip"
        };
        db.SellerOrderItems.Add(returnedItem);
        db.ReturnRequests.Add(new ReturnRequest
        {
            ReturnId = 500,
            SellerOrderItemId = returnedItem.SellerOrderItemId,
            SellerOrderItem = returnedItem,
            SellerId = 10,
            ReasonCode = "quality",
            Description = "pending return",
            Photos = string.Empty,
            Status = "pending",
            Resolution = string.Empty,
            RequestedAt = DateTime.UtcNow,
            RefundAmount = 10_000m
        });
        await db.SaveChangesAsync();

        var result = await new PayoutGenerationService(db).GeneratePendingPayoutsAsync(cancellationToken: CancellationToken.None);

        Assert.Equal(0, result.CreatedPayoutCount);
        Assert.Empty(db.Payouts);
    }

    [Fact]
    public async Task ReleasePendingPayout_RecordsManualPaidAudit()
    {
        await using var db = CreateDbContext();
        var payout = new Payout
        {
            PayoutId = 1,
            SellerId = 10,
            AmountGross = 100_000m,
            FeeAmount = 10_000m,
            AmountNet = 90_000m,
            Status = "pending",
            CreatedAt = DateTime.UtcNow
        };
        db.Payouts.Add(payout);
        await db.SaveChangesAsync();

        var controller = CreateFinanceController(db, isAdmin: true, subjectId: 1);
        var result = await controller.TakeAction(new FinanceAdminController.FinanceActionRequest
        {
            Section = "payouts",
            RecordId = payout.PayoutId,
            ActionName = "release",
            Note = "paid by manual bank transfer outside system"
        }, CancellationToken.None);

        Assert.IsType<OkObjectResult>(result);
        Assert.Equal("paid", payout.Status);
        Assert.NotNull(payout.PaidAt);

        var audit = await db.SettlementAudits.SingleAsync();
        Assert.Equal("manual_payout_recorded", audit.AuditType);
        Assert.Contains("manual_payout_recorded", audit.Notes, StringComparison.OrdinalIgnoreCase);
        Assert.Equal(90_000m, audit.Amount);
    }

    [Fact]
    public async Task SellerConsole_DoesNotExposeOtherSellerData()
    {
        await using var db = CreateDbContext();
        SeedPaidSellerOrder(db, orderId: 1, sellerId: 10, commissionAmount: 10_000m, sellerEarning: 90_000m);
        SeedPaidSellerOrder(db, orderId: 2, sellerId: 20, commissionAmount: 20_000m, sellerEarning: 180_000m);
        await db.SaveChangesAsync();

        var controller = CreateFinanceController(db, isAdmin: false, subjectId: 10);
        var result = await controller.GetConsole(cancellationToken: CancellationToken.None);

        var ok = Assert.IsType<OkObjectResult>(result);
        using var json = JsonDocument.Parse(JsonSerializer.Serialize(ok.Value));
        var stats = json.RootElement.GetProperty("stats");
        Assert.Equal(100_000m, stats.GetProperty("grossMerchandiseValue").GetDecimal());
        Assert.Equal(10_000m, stats.GetProperty("platformCommission").GetDecimal());
        Assert.Equal(1, stats.GetProperty("sellerCount").GetInt32());
    }

    private static PaymentTransaction SeedPaidSellerOrder(
        FreshFarmOrderingDBContext db,
        int orderId,
        int sellerId,
        decimal commissionAmount,
        decimal sellerEarning,
        string orderStatus = "Delivered",
        string sellerStatus = "Delivered",
        string orderPaymentStatus = "Paid",
        string paymentMethod = "VNPay",
        bool includeTransaction = true)
    {
        var gross = commissionAmount + sellerEarning;
        var order = new Order
        {
            OrderId = orderId,
            UserId = 100 + orderId,
            OrderDate = DateTime.UtcNow,
            ShippingFee = 0m,
            TotalAmount = gross,
            OrderNote = string.Empty,
            Status = orderStatus,
            PaymentStatus = orderPaymentStatus,
            PaidAt = orderPaymentStatus.Equals("Paid", StringComparison.OrdinalIgnoreCase) ? DateTime.UtcNow : null,
            BuyerFullName = "Buyer",
            BuyerPhone = "0123456789",
            BuyerEmail = "buyer@example.com",
            PointsEarned = 0,
            PointsRedeemed = 0
        };

        var sellerOrder = new SellerOrder
        {
            SellerOrderId = orderId,
            OrderId = orderId,
            SellerId = sellerId,
            SellerStatus = sellerStatus,
            CommissionRate = gross == 0m ? 0m : commissionAmount / gross,
            CommissionAmount = commissionAmount,
            ShippingFee = 0m,
            SellerEarning = sellerEarning,
            CancelledBy = string.Empty,
            CreatedAt = DateTime.UtcNow,
            Order = order
        };
        order.SellerOrders.Add(sellerOrder);
        order.Payments.Add(new Payment
        {
            PaymentId = orderId,
            OrderId = orderId,
            Order = order,
            PaymentMethod = paymentMethod,
            PaymentStatus = orderPaymentStatus,
            PaymentDate = orderPaymentStatus.Equals("Paid", StringComparison.OrdinalIgnoreCase) ? DateTime.UtcNow : null
        });

        var transaction = new PaymentTransaction
        {
            PaymentTxnId = orderId,
            OrderId = orderId,
            Order = order,
            Provider = paymentMethod,
            Method = paymentMethod,
            Amount = gross,
            Currency = "VND",
            Status = orderPaymentStatus.Equals("Paid", StringComparison.OrdinalIgnoreCase) ? "Succeeded" : "Pending",
            ProviderRef = $"TXN{orderId}",
            PaidAt = orderPaymentStatus.Equals("Paid", StringComparison.OrdinalIgnoreCase) ? DateTime.UtcNow : null,
            CreatedAt = DateTime.UtcNow
        };

        if (includeTransaction)
        {
            order.PaymentTransactions.Add(transaction);
        }

        db.Orders.Add(order);
        return transaction;
    }

    private static FinanceAdminController CreateFinanceController(FreshFarmOrderingDBContext db, bool isAdmin, int subjectId)
    {
        var controller = new FinanceAdminController(
            db,
            new PayoutGenerationService(db),
            new ManualPayoutTransferProvider())
        {
            ControllerContext = new ControllerContext
            {
                HttpContext = BuildHttpContext(isAdmin, subjectId)
            }
        };

        return controller;
    }

    private static DefaultHttpContext BuildHttpContext(bool isAdmin, int subjectId)
    {
        var claims = new List<Claim>
        {
            new(JwtRegisteredClaimNames.Sub, subjectId.ToString())
        };

        claims.Add(new Claim(ClaimTypes.Role, isAdmin ? "Admin" : "Seller"));

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
}
