using FreshFarm.Catalog.Api.Models;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;

namespace FreshFarm.Catalog.Api.Controllers;

[ApiController]
[Route("api/products/{productId:int}/offers")]
public sealed class ProductOffersController : ControllerBase
{
    private readonly FreshFarmCatalogDBContext _db;

    public ProductOffersController(FreshFarmCatalogDBContext db)
    {
        _db = db;
    }

    [HttpGet]
    public async Task<IActionResult> GetOffers([FromRoute] int productId)
    {
        if (productId <= 0)
        {
            return BadRequest(new { message = "Sản phẩm không hợp lệ." });
        }

        var sellerId = TryGetCurrentSellerId();
        var product = await _db.Products
            .AsNoTracking()
            .FirstOrDefaultAsync(p => p.ProductId == productId);

        if (product is null)
        {
            return NotFound(new { message = "Không tìm thấy sản phẩm." });
        }

        if (sellerId.HasValue)
        {
            var owned = await _db.SellerProducts
                .AsNoTracking()
                .AnyAsync(sp => sp.ProductId == productId && sp.SellerId == sellerId.Value && sp.IsActive);

            if (!owned)
            {
                return NotFound(new { message = "Không tìm thấy sản phẩm." });
            }
        }
        else if (!product.Status || product.IsManuallyDisabled)
        {
            return NotFound(new { message = "Không tìm thấy sản phẩm." });
        }

        var offers = await _db.SellerProducts
            .AsNoTracking()
            .Where(sp => sp.ProductId == productId && sp.IsActive)
            .OrderBy(sp => sp.CreatedAt)
            .Select(sp => new
            {
                sp.SellerId,
                sp.ProductId,
                ActiveSinceUtc = sp.CreatedAt
            })
            .ToListAsync();

        return Ok(offers);
    }

    private int? TryGetCurrentSellerId()
    {
        if (User?.Identity?.IsAuthenticated != true || !User.IsInRole("Seller"))
        {
            return null;
        }

        var raw = User.FindFirstValue(JwtRegisteredClaimNames.Sub)
                  ?? User.FindFirstValue(ClaimTypes.NameIdentifier)
                  ?? User.FindFirstValue("sub");

        return int.TryParse(raw, out var sellerId) ? sellerId : null;
    }
}
