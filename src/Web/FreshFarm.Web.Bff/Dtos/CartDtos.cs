namespace FreshFarm.Web.Bff.Dtos; // Đặt đúng namespace để controller/service dùng trực tiếp.

public sealed class CartItemDto // Mỗi dòng hàng trong giỏ.
{
    public int ProductId { get; set; } // Khóa để cập nhật/xóa đúng item.
    public string ProductName { get; set; } = string.Empty; // Hiển thị tên trên UI, không cần gọi lại Catalog khi render.
    public decimal UnitPrice { get; set; } // Chụp giá tại thời điểm thêm vào giỏ để tính tổng.
    public string UnitSymbol { get; set; } = "đơn vị"; // Ví dụ: kg, hộp, bó.
    public int Quantity { get; set; } = 1; // Mặc định 1 khi thêm mới.
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
    public string ProductName { get; set; } = string.Empty; // Dùng để render ngay.
    public decimal UnitPrice { get; set; } // Giá hiện tại từ Home.
    public string UnitSymbol { get; set; } = "đơn vị"; // Đơn vị tính.
    public int Quantity { get; set; } = 1; // Cho phép thêm >1 nếu cần.
}

public sealed class UpdateCartItemRequestDto // Payload cập nhật số lượng item.
{
    public int ProductId { get; set; } // Xác định item cần update.
    public int Quantity { get; set; } // Số lượng mới.
}

public sealed class RemoveCartItemRequestDto // Payload xóa item khỏi giỏ.
{
    public int ProductId { get; set; } // Chỉ cần ProductId là đủ.
}