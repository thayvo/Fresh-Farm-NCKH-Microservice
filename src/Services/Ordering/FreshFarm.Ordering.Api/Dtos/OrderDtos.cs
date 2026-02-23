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

public sealed class CreateShippingRequest
{
    [Required, MaxLength(50)]
    public string ShippingType { get; init; } = "HomeDelivery";

    [Required, MaxLength(100)]
    public string FullName { get; init; } = string.Empty;

    [Required]
    [MaxLength(10)]
    [RegularExpression(@"^0\d{9}$", ErrorMessage = "Phone phải gồm 10 chữ số và bắt đầu bằng 0.")]
    public string Phone { get; init; } = string.Empty;

    [EmailAddress(ErrorMessage = "Email không đúng định dạng.")]
    [MaxLength(100)]
    public string? Email { get; init; }

    [Required, MaxLength(255)]
    public string? AddressDetail { get; init; }

    public int? ProvinceId { get; init; }
    public int? CommuneId { get; init; }
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

public sealed class OrderDetailResponse // DTO tra ve cho API chi tiet don theo id.
{
    public int OrderId { get; init; } // Ma don.
    public DateTime OrderDate { get; init; } // Ngay tao don.
    public decimal ShippingFee { get; init; } // Phi giao hang.
    public int? CouponId { get; init; } // Coupon da ap dung (neu co).
    public decimal TotalAmount { get; init; } // Tong tien don.
    public string? OrderNote { get; init; } // Ghi chu cua nguoi mua.
    public string Status { get; init; } = string.Empty; // Trang thai don.
    public string? PaymentStatus { get; init; } // Trang thai thanh toan tong quan.
    public DateTime? PaidAt { get; init; } // Thoi diem da thanh toan (neu co).
    public string? BuyerFullName { get; init; } // Snapshot ten nguoi mua/nhan.
    public string? BuyerPhone { get; init; } // Snapshot so dien thoai.
    public string? BuyerEmail { get; init; } // Snapshot email.
    public int PointsEarned { get; init; } // Diem cong.
    public int PointsRedeemed { get; init; } // Diem da dung.
    public List<OrderDetailItemResponse> Items { get; init; } = new(); // Danh sach item trong don.
    public List<OrderDetailShippingResponse> Shippings { get; init; } = new(); // Danh sach ban ghi giao nhan.
    public List<OrderDetailPaymentResponse> Payments { get; init; } = new(); // Danh sach ban ghi thanh toan.
}

public sealed class OrderDetailItemResponse // Tung dong item trong chi tiet don.
{
    public int OrderDetailId { get; init; } // Ma dong chi tiet.
    public int ProductId { get; init; } // Ma san pham.
    public int Quantity { get; init; } // So luong.
    public decimal UnitPrice { get; init; } // Don gia snapshot.
    public string? UnitSymbol { get; init; } // Don vi tinh.
}

public sealed class OrderDetailShippingResponse // Tung ban ghi shipping cua don.
{
    public int ShippingId { get; init; } // Ma shipping.
    public string? ShippingType { get; init; } // Loai giao hang.
    public string? FullName { get; init; } // Ten nguoi nhan.
    public string? Phone { get; init; } // So dien thoai nguoi nhan.
    public string? Email { get; init; } // Email nguoi nhan.
    public string? AddressDetail { get; init; } // Dia chi chi tiet.
    public int? ProvinceId { get; init; } // Ma tinh/thanh.
    public int? CommuneId { get; init; } // Ma phuong/xa.
}

public sealed class OrderDetailPaymentResponse // Tung ban ghi payment cua don.
{
    public int PaymentId { get; init; } // Ma payment.
    public string? PaymentMethod { get; init; } // Phuong thuc thanh toan.
    public string? BankName { get; init; } // Ten ngan hang.
    public string? AccountName { get; init; } // Ten chu tai khoan.
    public string? AccountNumber { get; init; } // So tai khoan.
    public string? TransactionCode { get; init; } // Ma giao dich.
    public string? PaymentStatus { get; init; } // Trang thai payment.
    public DateTime? PaymentDate { get; init; } // Thoi diem thanh toan.
}
