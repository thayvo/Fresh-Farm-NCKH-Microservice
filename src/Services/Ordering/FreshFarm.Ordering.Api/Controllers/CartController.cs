using FreshFarm.Ordering.Api.Dtos;
using FreshFarm.Ordering.Api.Models;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;

namespace FreshFarm.Ordering.Api.Controllers;

[ApiController]
[Route("api/cart")]
[Authorize]
public sealed class CartController : ControllerBase
{
    private readonly FreshFarmOrderingDBContext _db;

    public CartController(FreshFarmOrderingDBContext db)
    {
        _db = db;
    }

    [HttpGet("me")]
    public async Task<IActionResult> GetMine(CancellationToken cancellationToken)
    {
        var userId = TryGetUserIdFromToken();
        if (userId is null)
        {
            return Unauthorized("Token khong co claim user id hop le.");
        }

        var items = await GetCartItemsQuery(userId.Value)
            .OrderBy(x => x.CartItemId)
            .Select(item => new CartItemResponse
            {
                ProductId = item.ProductId,
                SellerId = item.SellerId,
                SellerName = item.SnapshotSellerName ?? string.Empty,
                ProductName = item.SnapshotProductName ?? string.Empty,
                ImageFileName = item.SnapshotImageFileName ?? string.Empty,
                UnitPrice = item.UnitPrice,
                UnitSymbol = item.SnapshotUnitSymbol ?? "don vi",
                Quantity = item.Quantity
            })
            .ToListAsync(cancellationToken);

        return Ok(new CartResponse
        {
            Items = items
        });
    }

    [HttpPut("me")]
    public async Task<IActionResult> ReplaceMine([FromBody] ReplaceCartRequest? request, CancellationToken cancellationToken)
    {
        var userId = TryGetUserIdFromToken();
        if (userId is null)
        {
            return Unauthorized("Token khong co claim user id hop le.");
        }

        var incomingItems = request?.Items ?? new List<UpsertCartItemRequest>();
        if (incomingItems.Any(HasInvalidSellerId))
        {
            return BadRequest(new { message = "SellerId cua san pham trong gio hang phai > 0." });
        }

        var normalizedItems = NormalizeItems(incomingItems);
        var cart = await EnsureCartAsync(userId.Value, cancellationToken);
        var existingItems = await _db.CartItems
            .Where(x => x.CartId == cart.CartId)
            .ToListAsync(cancellationToken);

        var incomingLookup = normalizedItems.ToDictionary(
            item => BuildKey(item.ProductId, item.SellerId),
            item => item,
            StringComparer.OrdinalIgnoreCase);

        foreach (var existing in existingItems.ToList())
        {
            var key = BuildKey(existing.ProductId, existing.SellerId);
            if (!incomingLookup.TryGetValue(key, out var incoming))
            {
                _db.CartItems.Remove(existing);
                continue;
            }

            ApplySnapshot(existing, incoming);
            existing.Quantity = incoming.Quantity;
            incomingLookup.Remove(key);
        }

        foreach (var incoming in incomingLookup.Values)
        {
            _db.CartItems.Add(CreateCartItem(cart.CartId, incoming));
        }

        await _db.SaveChangesAsync(cancellationToken);
        return await GetMine(cancellationToken);
    }

    [HttpPost("items/add")]
    public async Task<IActionResult> AddItem([FromBody] UpsertCartItemRequest request, CancellationToken cancellationToken)
    {
        var userId = TryGetUserIdFromToken();
        if (userId is null)
        {
            return Unauthorized("Token khong co claim user id hop le.");
        }

        var normalized = NormalizeItem(request);
        if (normalized is null)
        {
            return BadRequest(new { message = "Thong tin san pham trong gio hang khong hop le." });
        }

        var cart = await EnsureCartAsync(userId.Value, cancellationToken);
        var existing = await _db.CartItems.FirstOrDefaultAsync(
            x => x.CartId == cart.CartId &&
                 x.ProductId == normalized.ProductId &&
                 x.SellerId == normalized.SellerId,
            cancellationToken);

        if (existing is null)
        {
            _db.CartItems.Add(CreateCartItem(cart.CartId, normalized));
        }
        else
        {
            ApplySnapshot(existing, normalized);
            existing.Quantity += normalized.Quantity;
        }

        await _db.SaveChangesAsync(cancellationToken);
        return await GetMine(cancellationToken);
    }

    [HttpPost("items/update")]
    public async Task<IActionResult> UpdateQuantity([FromBody] UpdateCartItemQuantityRequest request, CancellationToken cancellationToken)
    {
        var userId = TryGetUserIdFromToken();
        if (userId is null)
        {
            return Unauthorized("Token khong co claim user id hop le.");
        }

        var cart = await EnsureCartAsync(userId.Value, cancellationToken);
        if (request.ProductId <= 0 || request.SellerId <= 0)
        {
            return BadRequest(new { message = "ProductId va SellerId phai > 0." });
        }

        var existing = await _db.CartItems.FirstOrDefaultAsync(
            x => x.CartId == cart.CartId &&
                 x.ProductId == request.ProductId &&
                 x.SellerId == request.SellerId,
            cancellationToken);

        if (existing is null)
        {
            return NotFound(new { message = "Khong tim thay san pham trong gio hang." });
        }

        if (request.Quantity <= 0)
        {
            _db.CartItems.Remove(existing);
        }
        else
        {
            existing.Quantity = request.Quantity;
        }

        await _db.SaveChangesAsync(cancellationToken);
        return await GetMine(cancellationToken);
    }

    [HttpPost("items/remove")]
    public async Task<IActionResult> RemoveItem([FromBody] RemoveCartItemRequest request, CancellationToken cancellationToken)
    {
        var userId = TryGetUserIdFromToken();
        if (userId is null)
        {
            return Unauthorized("Token khong co claim user id hop le.");
        }

        var cart = await FindCartAsync(userId.Value, cancellationToken);
        if (cart is null)
        {
            return await GetMine(cancellationToken);
        }

        if (request.ProductId <= 0 || request.SellerId <= 0)
        {
            return BadRequest(new { message = "ProductId va SellerId phai > 0." });
        }

        var existing = await _db.CartItems.FirstOrDefaultAsync(
            x => x.CartId == cart.CartId &&
                 x.ProductId == request.ProductId &&
                 x.SellerId == request.SellerId,
            cancellationToken);

        if (existing is not null)
        {
            _db.CartItems.Remove(existing);
            await _db.SaveChangesAsync(cancellationToken);
        }

        return await GetMine(cancellationToken);
    }

    [HttpPost("clear")]
    public async Task<IActionResult> Clear(CancellationToken cancellationToken)
    {
        var userId = TryGetUserIdFromToken();
        if (userId is null)
        {
            return Unauthorized("Token khong co claim user id hop le.");
        }

        var cart = await FindCartAsync(userId.Value, cancellationToken);
        if (cart is not null)
        {
            var existingItems = await _db.CartItems.Where(x => x.CartId == cart.CartId).ToListAsync(cancellationToken);
            if (existingItems.Count > 0)
            {
                _db.CartItems.RemoveRange(existingItems);
                await _db.SaveChangesAsync(cancellationToken);
            }
        }

        return Ok(new CartResponse());
    }

    private async Task<Cart?> FindCartAsync(int userId, CancellationToken cancellationToken)
    {
        return await _db.Carts.FirstOrDefaultAsync(x => x.UserId == userId, cancellationToken);
    }

    private async Task<Cart> EnsureCartAsync(int userId, CancellationToken cancellationToken)
    {
        var cart = await FindCartAsync(userId, cancellationToken);
        if (cart is not null)
        {
            return cart;
        }

        cart = new Cart
        {
            UserId = userId,
            CreatedDate = DateTime.UtcNow
        };

        _db.Carts.Add(cart);
        await _db.SaveChangesAsync(cancellationToken);
        return cart;
    }

    private IQueryable<CartItem> GetCartItemsQuery(int userId)
    {
        return _db.CartItems
            .AsNoTracking()
            .Where(x => x.Cart.UserId == userId);
    }

    private static CartItem CreateCartItem(int cartId, UpsertCartItemRequest item)
    {
        var cartItem = new CartItem
        {
            CartId = cartId,
            ProductId = item.ProductId,
            SellerId = item.SellerId,
            Quantity = item.Quantity,
            UnitPrice = item.UnitPrice
        };

        ApplySnapshot(cartItem, item);
        return cartItem;
    }

    private static void ApplySnapshot(CartItem cartItem, UpsertCartItemRequest item)
    {
        cartItem.SellerId = item.SellerId;
        cartItem.UnitPrice = item.UnitPrice;
        cartItem.SnapshotSellerName = string.IsNullOrWhiteSpace(item.SellerName) ? null : item.SellerName.Trim();
        cartItem.SnapshotProductName = string.IsNullOrWhiteSpace(item.ProductName) ? null : item.ProductName.Trim();
        cartItem.SnapshotImageFileName = string.IsNullOrWhiteSpace(item.ImageFileName) ? null : item.ImageFileName.Trim();
        cartItem.SnapshotUnitSymbol = string.IsNullOrWhiteSpace(item.UnitSymbol) ? null : item.UnitSymbol.Trim();
    }

    private static List<UpsertCartItemRequest> NormalizeItems(IEnumerable<UpsertCartItemRequest> items)
    {
        return items
            .Select(NormalizeItem)
            .Where(item => item is not null)
            .GroupBy(item => BuildKey(item!.ProductId, item.SellerId), StringComparer.OrdinalIgnoreCase)
            .Select(group =>
            {
                var first = group.First()!;
                var quantity = group.Sum(x => Math.Max(1, x!.Quantity));
                first.Quantity = Math.Max(1, quantity);
                return first;
            })
            .Cast<UpsertCartItemRequest>()
            .ToList();
    }

    private static UpsertCartItemRequest? NormalizeItem(UpsertCartItemRequest? item)
    {
        if (item is null || item.ProductId <= 0 || item.SellerId <= 0)
        {
            return null;
        }

        return new UpsertCartItemRequest
        {
            ProductId = item.ProductId,
            SellerId = item.SellerId,
            SellerName = item.SellerName?.Trim() ?? string.Empty,
            ProductName = item.ProductName?.Trim() ?? string.Empty,
            ImageFileName = item.ImageFileName?.Trim() ?? string.Empty,
            UnitPrice = item.UnitPrice < 0m ? 0m : item.UnitPrice,
            UnitSymbol = item.UnitSymbol?.Trim() ?? string.Empty,
            Quantity = Math.Max(1, item.Quantity)
        };
    }

    private static string BuildKey(int productId, int sellerId)
    {
        return $"{Math.Max(0, productId)}:{Math.Max(0, sellerId)}";
    }

    private static bool HasInvalidSellerId(UpsertCartItemRequest? item)
    {
        return item is not null && item.ProductId > 0 && item.SellerId <= 0;
    }

    private int? TryGetUserIdFromToken()
    {
        var sub = User.FindFirstValue(JwtRegisteredClaimNames.Sub)
                  ?? User.FindFirstValue(ClaimTypes.NameIdentifier)
                  ?? User.FindFirstValue("sub");

        return int.TryParse(sub, out var userId) ? userId : null;
    }
}
