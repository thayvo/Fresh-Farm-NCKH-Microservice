using FreshFarm.Web.Bff.Dtos;

namespace FreshFarm.Web.Bff.Services;

public interface ICartSessionService
{
    Task<List<CartItemDto>> GetItemsAsync();
    Task SetItemsAsync(List<CartItemDto> items);
    Task AddOrIncreaseAsync(AddToCartRequestDto request);
    Task UpdateQuantityAsync(int productId, int quantity);
    Task UpdateQuantityAsync(int productId, int sellerId, string? cartItemKey, int quantity);
    Task RemoveAsync(int productId);
    Task RemoveAsync(int productId, int sellerId, string? cartItemKey);
    Task ClearAsync();
    Task<CartSummaryDto> BuildSummaryAsync(decimal shippingFee);
}
