using FreshFarm.Web.Bff.Dtos; // Dùng DTO cart đã tạo.

namespace FreshFarm.Web.Bff.Services; // Namespace service.

public interface ICartSessionService // Hợp đồng để controller gọi mà không phụ thuộc chi tiết session/json.
{
    List<CartItemDto> GetItems(); // Lấy toàn bộ item trong giỏ.
    void SetItems(List<CartItemDto> items); // Ghi đè toàn bộ giỏ.
    void AddOrIncrease(AddToCartRequestDto request); // Thêm mới hoặc cộng dồn số lượng.
    void UpdateQuantity(int productId, int quantity); // Cập nhật số lượng 1 item.
    void Remove(int productId); // Xóa 1 item.
    void Clear(); // Xóa toàn bộ giỏ.
    CartSummaryDto BuildSummary(decimal shippingFee); // Tính subtotal/grand total.
}