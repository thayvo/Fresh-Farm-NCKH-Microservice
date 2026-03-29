using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using FreshFarm.Identity.Api.Controllers;
using FreshFarm.Identity.Api.Models;
using FreshFarm.Identity.Api.Services;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace FreshFarm.Identity.Api.Tests;

public sealed class AdminSettingsControllerTests
{
    [Fact]
    public async Task Get_ReturnsLatestSellerStoreSettings_WhenDuplicateRowsExist()
    {
        await using var db = CreateDbContext();
        db.Users.Add(new User
        {
            UserId = 301,
            UserName = "seller301",
            FullName = "Seller 301",
            Email = "seller301@example.com",
            Phone = "0912345678",
            EmailConfirmed = true,
            IsActive = true,
            CreatedAt = DateTime.UtcNow
        });
        await db.SaveChangesAsync();

        var latestSettings = new SellerStoreSetting
        {
            SellerStoreSettingId = 999,
            UserId = 301,
            StoreName = "Latest Shop",
            StoreAddress = "99 New Street",
            StoreEmail = "latest@example.com",
            StorePhone = "0987654321",
            IsCodenabled = true,
            BankTransferInstructions = string.Empty,
            BankAccountInfo = string.Empty,
            DefaultShippingFee = 35000m,
            FreeShippingThreshold = 600000m,
            IsEmailNewOrderEnabled = true,
            IsEmailDeliveredEnabled = true,
            IsEmailCancelledEnabled = true,
            AdminNotificationEmail = "latest@example.com",
            GhnPickupName = "Latest Pickup",
            GhnPickupPhone = "0987654322",
            GhnPickupAddress = "99 New Street",
            GhnDistrictId = 123,
            GhnWardCode = "00123",
            CreatedAt = DateTime.UtcNow.AddMinutes(-10),
            UpdatedAt = DateTime.UtcNow.AddMinutes(10)
        };

        var controller = new AdminSettingsController(
            db,
            new StubSellerStoreSettingsResolver(latestSettings))
        {
            ControllerContext = new ControllerContext
            {
                HttpContext = new DefaultHttpContext
                {
                    User = new ClaimsPrincipal(new ClaimsIdentity(
                    [
                        new Claim(JwtRegisteredClaimNames.Sub, "301"),
                        new Claim(ClaimTypes.Role, "Seller")
                    ], "TestAuth"))
                }
            }
        };

        var result = await controller.Get(CancellationToken.None);

        var ok = Assert.IsType<OkObjectResult>(result);
        var response = Assert.IsType<AdminSettingsController.AdminStoreSettingsResponse>(ok.Value);
        Assert.Equal("Latest Shop", response.StoreName);
        Assert.Equal("0987654321", response.StorePhone);
        Assert.Equal("0987654322", response.GhnPickupPhone);
        Assert.True(response.HasGhnOrigin);
    }

    private static FreshFarmIdentityDBContext CreateDbContext()
    {
        var options = new DbContextOptionsBuilder<FreshFarmIdentityDBContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString("N"))
            .Options;
        return new FreshFarmIdentityDBContext(options);
    }

    private sealed class StubSellerStoreSettingsResolver : ISellerStoreSettingsResolver
    {
        private readonly SellerStoreSetting? _sellerStoreSetting;

        public StubSellerStoreSettingsResolver(SellerStoreSetting? sellerStoreSetting)
        {
            _sellerStoreSetting = sellerStoreSetting;
        }

        public Task<SellerStoreSetting?> GetLatestForUserAsync(int userId, bool asNoTracking, CancellationToken cancellationToken = default)
        {
            return Task.FromResult(_sellerStoreSetting);
        }
    }
}
