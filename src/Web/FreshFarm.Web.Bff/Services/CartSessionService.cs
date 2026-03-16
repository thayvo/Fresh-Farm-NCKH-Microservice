using System.Text.Json; // Dùng để serialize/deserialize list item vào session.
using FreshFarm.Web.Bff.Dtos; // Dùng DTO cart.

namespace FreshFarm.Web.Bff.Services; // Namespace service.

public sealed class CartSessionService : ICartSessionService // Triển khai interface để inject qua DI.
{
    private const string CartSessionKey = "CART_ITEMS"; // Key cố định trong session, tránh sai chính tả khi đọc/ghi.
    private readonly IHttpContextAccessor _httpContextAccessor; // Truy cập HttpContext từ service lớp giữa.

    public CartSessionService(IHttpContextAccessor httpContextAccessor) // Inject accessor qua DI.
    {
        _httpContextAccessor = httpContextAccessor; // Gán vào field để dùng xuyên suốt.
    }

    private ISession Session => // Property helper lấy session hiện tại.
        _httpContextAccessor.HttpContext?.Session // Đọc Session từ HttpContext hiện tại.
        ?? throw new InvalidOperationException("Session chưa sẵn sàng."); // Fail-fast nếu gọi sai ngữ cảnh.

    public List<CartItemDto> GetItems() // Đọc giỏ hàng từ session.
    {
        var json = Session.GetString(CartSessionKey); // Lấy chuỗi JSON đang lưu.
        if (string.IsNullOrWhiteSpace(json)) // Nếu chưa có dữ liệu.
        {
            return new List<CartItemDto>(); // Trả list rỗng thay vì null để tránh null-check khắp nơi.
        }

        return JsonSerializer.Deserialize<List<CartItemDto>>(json) // Parse JSON về list item.
               ?? new List<CartItemDto>(); // Nếu parse lỗi vẫn trả list rỗng an toàn.
    }

    public void SetItems(List<CartItemDto> items) // Ghi list item vào session.
    {
        var json = JsonSerializer.Serialize(items); // Convert list -> JSON.
        Session.SetString(CartSessionKey, json); // Lưu vào session.
    }

    public void AddOrIncrease(AddToCartRequestDto request) // Thêm mới hoặc cộng dồn.
    {
        var items = GetItems(); // Lấy trạng thái giỏ hiện tại.
        var quantity = request.Quantity <= 0 ? 1 : request.Quantity; // Chuẩn hóa quantity để tránh 0/âm.
        var sellerId = request.SellerId > 0 ? request.SellerId : 0;
        var existing = items.FirstOrDefault(x => x.ProductId == request.ProductId && NormalizeSellerId(x.SellerId) == sellerId); // Tìm item theo product + seller.
        if (existing is null) // Nếu chưa có trong giỏ.
        {
            items.Add(new CartItemDto // Tạo item mới.
            {
                ProductId = request.ProductId, // Gán mã sản phẩm.
                SellerId = sellerId, // Gắn seller để hỗ trợ multi-seller cart.
                SellerName = request.SellerName, // Gắn tên shop để render nhanh.
                ProductName = request.ProductName, // Gán tên để render.
                UnitPrice = request.UnitPrice, // Gán đơn giá.
                UnitSymbol = string.IsNullOrWhiteSpace(request.UnitSymbol) ? "đơn vị" : request.UnitSymbol, // Fallback đơn vị.
                Quantity = quantity // Gán số lượng đã chuẩn hóa.
            });
        }
        else // Nếu đã có item.
        {
            existing.Quantity += quantity; // Cộng dồn số lượng thay vì tạo dòng trùng.
            existing.SellerId = sellerId; // Đồng bộ seller nếu request đã có.
            existing.UnitPrice = request.UnitPrice; // Cập nhật theo giá mới nhất trên Home.
            if (!string.IsNullOrWhiteSpace(request.SellerName)) existing.SellerName = request.SellerName; // Đồng bộ tên shop nếu có.
            if (!string.IsNullOrWhiteSpace(request.ProductName)) existing.ProductName = request.ProductName; // Đồng bộ tên nếu có.
            if (!string.IsNullOrWhiteSpace(request.UnitSymbol)) existing.UnitSymbol = request.UnitSymbol; // Đồng bộ đơn vị nếu có.
        }

        SetItems(items); // Ghi lại cart sau khi thay đổi.
    }

    public void UpdateQuantity(int productId, int quantity) // Cập nhật quantity.
    {
        UpdateQuantity(productId, 0, string.Empty, quantity); // Backward-compatible path cho code cũ.
    }

    public void Remove(int productId) // Xóa item theo productId.
    {
        Remove(productId, 0, string.Empty); // Backward-compatible path cho code cũ.
    }

    public void UpdateQuantity(int productId, int sellerId, string? cartItemKey, int quantity)
    {
        var items = GetItems();
        var existing = FindItem(items, productId, sellerId, cartItemKey);
        if (existing is null) return;

        if (quantity <= 0)
        {
            items.Remove(existing);
        }
        else
        {
            existing.Quantity = quantity;
        }

        SetItems(items);
    }

    public void Remove(int productId, int sellerId, string? cartItemKey)
    {
        var items = GetItems();
        var existing = FindItem(items, productId, sellerId, cartItemKey);
        if (existing is null) return;

        items.Remove(existing);
        SetItems(items);
    }

    public void Clear() // Xóa toàn bộ giỏ.
    {
        Session.Remove(CartSessionKey); // Xóa key khỏi session cho sạch.
    }

    public CartSummaryDto BuildSummary(decimal shippingFee) // Tính tổng tiền.
    {
        var items = GetItems(); // Lấy item hiện tại.
        var subTotal = items.Sum(x => x.UnitPrice * x.Quantity); // Cộng từng dòng item.

        return new CartSummaryDto // Trả DTO tổng hợp cho controller/view.
        {
            Items = items, // Danh sách item hiện tại.
            SubTotal = subTotal, // Tổng tiền hàng.
            ShippingFee = shippingFee, // Phí ship đưa từ ngoài vào.
            GrandTotal = subTotal + shippingFee // Tổng cuối cùng.
        };
    }

    private static CartItemDto? FindItem(IEnumerable<CartItemDto> items, int productId, int sellerId, string? cartItemKey)
    {
        var normalizedKey = (cartItemKey ?? string.Empty).Trim();
        if (!string.IsNullOrWhiteSpace(normalizedKey))
        {
            return items.FirstOrDefault(x => string.Equals(x.CartItemKey, normalizedKey, StringComparison.OrdinalIgnoreCase));
        }

        var normalizedSellerId = NormalizeSellerId(sellerId);
        return items.FirstOrDefault(x => x.ProductId == productId && NormalizeSellerId(x.SellerId) == normalizedSellerId);
    }

    private static int NormalizeSellerId(int sellerId)
    {
        return sellerId > 0 ? sellerId : 0;
    }
}
