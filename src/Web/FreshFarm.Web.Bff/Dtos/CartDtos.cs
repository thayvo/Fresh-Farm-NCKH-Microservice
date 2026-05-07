namespace FreshFarm.Web.Bff.Dtos; // Đặt đúng namespace để controller/service dùng trực tiếp.

public sealed class CartItemDto // Mỗi dòng hàng trong giỏ.
{
    public int ProductId { get; set; } // Khóa để cập nhật/xóa đúng item.
    public int SellerId { get; set; } // Seller sở hữu offer đang được buyer chọn.
    public string SellerName { get; set; } = string.Empty; // Hiển thị shop trong cart/checkout.
    public string ProductName { get; set; } = string.Empty; // Hiển thị tên trên UI, không cần gọi lại Catalog khi render.
    public string ImageFileName { get; set; } = string.Empty; // Giữ tên file ảnh để cart/header preview render đúng thumbnail.
    public decimal UnitPrice { get; set; } // Chụp giá tại thời điểm thêm vào giỏ để tính tổng.
    public string UnitSymbol { get; set; } = "đơn vị"; // Ví dụ: kg, hộp, bó.
    public int Quantity { get; set; } = 1; // Mặc định 1 khi thêm mới.
    public int? RecommendationPosition { get; set; } // Vị trí recommendation nếu item được thêm từ gợi ý.
    public int? Position { get; set; } // Alias vị trí recommendation cho payload cũ/nhanh.
    public string CartItemKey => BuildCartItemKey(ProductId, SellerId); // Key ổn định cho multi-seller cart.

    public static string BuildCartItemKey(int productId, int sellerId)
    {
        return $"{Math.Max(0, productId)}:{Math.Max(0, sellerId)}";
    }
}

public sealed class CartSummaryDto // DTO trả cho màn hình cart/checkout summary.
{
    public List<CartItemDto> Items { get; set; } = new(); // Danh sách item hiện tại.
    public decimal SubTotal { get; set; } // Tổng tiền hàng chưa tính ship.
    public decimal ShippingFee { get; set; } // Phí vận chuyển.
    public decimal GrandTotal { get; set; } // Tổng thanh toán cuối cùng.
}

public sealed class AddToCartRequestDto // Payload khi thêm sản phẩm vào giỏ.
{
    public int ProductId { get; set; } // Bắt buộc để định danh item.
    public int SellerId { get; set; } // Seller của offer buyer đang chọn.
    public string SellerName { get; set; } = string.Empty; // Tên shop để render nhanh.
    public string ProductName { get; set; } = string.Empty; // Dùng để render ngay.
    public string ImageFileName { get; set; } = string.Empty; // Ảnh thumbnail hiện tại của sản phẩm để render đúng trong cart.
    public decimal UnitPrice { get; set; } // Giá hiện tại từ Home.
    public string UnitSymbol { get; set; } = "đơn vị"; // Đơn vị tính.
    public int Quantity { get; set; } = 1; // Cho phép thêm >1 nếu cần.
    public int? RecommendationPosition { get; set; } // Vị trí recommendation nếu thêm từ danh sách gợi ý.
    public int? Position { get; set; } // Alias cho payload JS cũ/nhanh.
}

public sealed class UpdateCartItemRequestDto // Payload cập nhật số lượng item.
{
    public int ProductId { get; set; } // Xác định item cần update.
    public int SellerId { get; set; } // Ghép đúng dòng nếu cùng product nhiều seller.
    public string CartItemKey { get; set; } = string.Empty; // Key đầy đủ ưu tiên hơn cặp ProductId/SellerId.
    public int Quantity { get; set; } // Số lượng mới.
}

public sealed class RemoveCartItemRequestDto // Payload xóa item khỏi giỏ.
{
    public int ProductId { get; set; } // Chỉ cần ProductId là đủ.
    public int SellerId { get; set; } // Ghép đúng seller item.
    public string CartItemKey { get; set; } = string.Empty; // Key đầy đủ ưu tiên hơn cặp ProductId/SellerId.
}
