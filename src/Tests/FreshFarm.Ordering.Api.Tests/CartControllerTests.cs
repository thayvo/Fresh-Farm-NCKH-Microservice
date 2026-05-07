using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using FreshFarm.Ordering.Api.Controllers;
using FreshFarm.Ordering.Api.Dtos;
using FreshFarm.Ordering.Api.Models;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace FreshFarm.Ordering.Api.Tests;

public sealed class CartControllerTests
{
    [Fact]
    public async Task AddItem_WithMissingSellerId_ReturnsBadRequestAndDoesNotPersistCartItem()
    {
        await using var db = CreateDbContext();
        var controller = CreateController(db);

        var result = await controller.AddItem(new UpsertCartItemRequest
        {
            ProductId = 108,
            SellerId = 0,
            Quantity = 1,
            UnitPrice = 22000m
        }, CancellationToken.None);

        Assert.IsType<BadRequestObjectResult>(result);
        Assert.Empty(await db.Carts.ToListAsync());
        Assert.Empty(await db.CartItems.ToListAsync());
    }

    [Fact]
    public async Task ReplaceMine_WithMissingSellerId_ReturnsBadRequestAndDoesNotPersistCartItem()
    {
        await using var db = CreateDbContext();
        var controller = CreateController(db);

        var result = await controller.ReplaceMine(new ReplaceCartRequest
        {
            Items =
            [
                new UpsertCartItemRequest
                {
                    ProductId = 108,
                    SellerId = 0,
                    Quantity = 1,
                    UnitPrice = 22000m
                }
            ]
        }, CancellationToken.None);

        Assert.IsType<BadRequestObjectResult>(result);
        Assert.Empty(await db.Carts.ToListAsync());
        Assert.Empty(await db.CartItems.ToListAsync());
    }

    private static CartController CreateController(FreshFarmOrderingDBContext db)
    {
        return new CartController(db)
        {
            ControllerContext = new ControllerContext
            {
                HttpContext = BuildHttpContext()
            }
        };
    }

    private static DefaultHttpContext BuildHttpContext()
    {
        var context = new DefaultHttpContext();
        context.User = new ClaimsPrincipal(new ClaimsIdentity(
        [
            new Claim(JwtRegisteredClaimNames.Sub, "5")
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
