namespace FreshFarm.Web.Bff.Dtos; // Namespace DTO cho checkout flow.

public sealed class CheckoutItemInputDto // 1 dong item user dat.
{
    public int ProductId { get; set; } // Ma san pham.
    public int Quantity { get; set; } // So luong mua.
    public decimal UnitPrice { get; set; } // Don gia tai thoi diem dat.
    public string? UnitSymbol { get; set; } // Don vi tinh (kg, hop...).
}

public sealed class CheckoutSubmitRequestDto // Payload gui qua BFF -> Ordering POST /api/orders.
{
    public List<CheckoutItemInputDto> Items { get; set; } = new(); // Danh sach item.
    public decimal? ShippingFee { get; set; } // Phi ship.
    public string? OrderNote { get; set; } // Ghi chu.
    public CheckoutShippingInputDto Shipping { get; set; } = new(); // Thong tin giao nhan.
    public CheckoutPaymentInputDto? Payment { get; set; } // Phuong thuc thanh toan.
}

public sealed class CheckoutShippingInputDto // Shipping object.
{
    public string ShippingType { get; set; } = "HomeDelivery"; // Loai giao hang.
    public string FullName { get; set; } = string.Empty; // Nguoi nhan.
    public string Phone { get; set; } = string.Empty; // SDT.
    public string? Email { get; set; } // Email.
    public string? AddressDetail { get; set; } // Dia chi.
    public int? ProvinceId { get; set; } // Ma tinh.
    public int? CommuneId { get; set; } // Ma xa.
}

public sealed class CheckoutPaymentInputDto // Payment object.
{
    public string PaymentMethod { get; set; } = "COD"; // COD/VNPay/BankTransfer...
}