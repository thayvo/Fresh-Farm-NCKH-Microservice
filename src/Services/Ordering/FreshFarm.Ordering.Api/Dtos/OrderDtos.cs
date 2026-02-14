using System.ComponentModel.DataAnnotations; // Dung DataAnnotations de validate request ngay o API layer.

namespace FreshFarm.Ordering.Api.Dtos; // Tach DTO ra namespace rieng de controller gon va de bao tri.

public sealed class CreateOrderRequest // Request chinh de tao don hang.
{
    [MinLength(1, ErrorMessage = "Items phai co it nhat 1 dong.")] // Chan truong hop tao don rong.
    public required List<CreateOrderItemRequest> Items { get; init; } // Danh sach san pham can dat.

    [Range(0, double.MaxValue, ErrorMessage = "ShippingFee phai >= 0.")] // Chan phi am.
    public decimal? ShippingFee { get; init; } // Cho phep null de he thong tu mac dinh 0.

    [MaxLength(255)] // Giu dung schema `Orders.OrderNote` (nvarchar(255)).
    public string? OrderNote { get; init; } // Ghi chu cua khach.

    public required CreateShippingRequest Shipping { get; init; } // Bat buoc thong tin giao nhan ngay khi tao don.

    public CreatePaymentRequest? Payment { get; init; } // Payment de optional vi co don COD khong pre-pay.
}

public sealed class CreateOrderItemRequest // Tung dong san pham trong don.
{
    [Range(1, int.MaxValue, ErrorMessage = "ProductId phai > 0.")] // ProductId phai hop le.
    public int ProductId { get; init; } // Map vao `OrderDetail.ProductId`.

    [Range(1, int.MaxValue, ErrorMessage = "Quantity phai > 0.")] // So luong phai duong.
    public int Quantity { get; init; } // Map vao `OrderDetail.Quantity`.

    [Range(0, double.MaxValue, ErrorMessage = "UnitPrice phai >= 0.")] // Khong cho gia am.
    public decimal UnitPrice { get; init; } // Gia tai thoi diem dat don (snapshot).

    [MaxLength(20)] // Giu dung schema `OrderDetail.UnitSymbol`.
    public string? UnitSymbol { get; init; } // Vi du: kg, bo, hop.
}

public sealed class CreateShippingRequest // Thong tin giao nhan.
{
    [Required, MaxLength(50)] // Giu dung schema + bat buoc de xu ly nghiep vu.
    public string ShippingType { get; init; } = "HomeDelivery"; // Vi du: HomeDelivery, Express.

    [Required, MaxLength(100)] // Giu dung schema `Shipping.FullName`.
    public string FullName { get; init; } = string.Empty; // Nguoi nhan.

    [Required, MaxLength(20)] // Giu dung schema `Shipping.Phone`.
    public string Phone { get; init; } = string.Empty; // SDT nguoi nhan.

    [MaxLength(100)] // Giu dung schema `Shipping.Email`.
    public string? Email { get; init; } // Email co the khong co.

    [MaxLength(255)] // Giu dung schema `Shipping.AddressDetail`.
    public string? AddressDetail { get; init; } // Dia chi chi tiet.

    public int? ProvinceId { get; init; } // Optional vi co the cap nhat sau.
    public int? CommuneId { get; init; } // Optional vi co the cap nhat sau.
}

public sealed class CreatePaymentRequest // Thong tin thanh toan ban dau.
{
    [Required, MaxLength(50)] // Giu dung schema `Payment.PaymentMethod`.
    public string PaymentMethod { get; init; } = "COD"; // Vi du: COD, VNPay, BankTransfer.
}

public sealed class OrderListItemResponse // DTO tra ve cho API list.
{
    public int OrderId { get; init; } // Ma don.
    public DateTime OrderDate { get; init; } // Ngay tao don.
    public decimal TotalAmount { get; init; } // Tong tien.
    public string Status { get; init; } = string.Empty; // Trang thai don.
    public string? PaymentStatus { get; init; } // Trang thai thanh toan.
}