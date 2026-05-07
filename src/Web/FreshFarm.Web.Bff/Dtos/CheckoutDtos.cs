namespace FreshFarm.Web.Bff.Dtos; // Namespace DTO cho checkout flow.
using System.ComponentModel.DataAnnotations;

public sealed class CheckoutItemInputDto // 1 dong item user dat.
{
    public int ProductId { get; set; } // Ma san pham.
    public int SellerId { get; set; } // Seller cua offer dang duoc checkout.
    public string? SellerName { get; set; } // Ten shop de render nhanh tren checkout.
    public string? ProductName { get; set; } // Snapshot ten san pham de Ordering luu SellerOrderItem de doc.
    public string? CartItemKey { get; set; } // Key on dinh cua dong cart multi-seller.
    public int Quantity { get; set; } // So luong mua.
    public decimal UnitPrice { get; set; } // Don gia tai thoi diem dat.
    public string? UnitSymbol { get; set; } // Don vi tinh (kg, hop...).
    public int? RecommendationPosition { get; set; } // Vi tri recommendation neu item den tu goi y.
    public int? Position { get; set; } // Alias vi tri recommendation cho payload don gian.
}

public sealed class CheckoutSubmitRequestDto // Payload gui qua BFF -> Ordering POST /api/orders.
{
    public List<CheckoutItemInputDto> Items { get; set; } = new(); // Danh sach item.
    public List<CheckoutSellerShippingInputDto> SellerShippingBreakdowns { get; set; } = new(); // Phi ship da tinh theo tung seller.
    public decimal? ShippingFee { get; set; } // Phi ship.
    public string? OrderNote { get; set; } // Ghi chu.
    public bool SaveAddressAsDefault { get; set; } // Neu co luu dia chi moi thi co dat mac dinh hay khong.
    public CheckoutShippingInputDto Shipping { get; set; } = new(); // Thong tin giao nhan.
    public CheckoutPaymentInputDto? Payment { get; set; } // Phuong thuc thanh toan.
}

public sealed class CheckoutSellerShippingInputDto
{
    public int SellerId { get; set; }
    public string? SellerName { get; set; }
    public decimal ShippingFee { get; set; }
    public string? ServiceName { get; set; }
    public string? ShippingOriginLabel { get; set; }
}

public sealed class CheckoutShippingFeePreviewRequestDto
{
    public string AddressMode { get; set; } = "new";
    public int? SelectedAddressId { get; set; }
    public List<CheckoutItemInputDto> Items { get; set; } = new();
    public CheckoutShippingInputDto Shipping { get; set; } = new();
}

public sealed class CheckoutShippingFeePreviewResultDto
{
    public bool Success { get; set; }
    public bool RequiresAddress { get; set; }
    public decimal ShippingFee { get; set; }
    public string Message { get; set; } = string.Empty;
    public string ServiceName { get; set; } = string.Empty;
    public List<CheckoutSellerShippingFeeBreakdownDto> SellerBreakdowns { get; set; } = new();
}

public sealed class CheckoutSellerShippingFeeBreakdownDto
{
    public int SellerId { get; set; }
    public string SellerName { get; set; } = string.Empty;
    public int ItemCount { get; set; }
    public int ProductCount { get; set; }
    public decimal ShippingFee { get; set; }
    public string ServiceName { get; set; } = string.Empty;
    public string ShippingOriginLabel { get; set; } = string.Empty;
}

public sealed class CheckoutShippingInputDto
{
    [Required(ErrorMessage = "Loại giao hàng là bắt buộc.")]
    [StringLength(50, ErrorMessage = "Loại giao hàng tối đa 50 ký tự.")]
    public string ShippingType { get; set; } = "HomeDelivery";

    [Required(ErrorMessage = "Họ và tên người nhận là bắt buộc.")]
    [StringLength(100, ErrorMessage = "Họ và tên tối đa 100 ký tự.")]
    public string FullName { get; set; } = string.Empty;

    [Required(ErrorMessage = "Số điện thoại là bắt buộc.")]
    [RegularExpression(@"^0\d{9}$", ErrorMessage = "Số điện thoại phải gồm 10 chữ số và bắt đầu bằng 0.")]
    public string Phone { get; set; } = string.Empty;

    [EmailAddress(ErrorMessage = "Email không đúng định dạng.")]
    [StringLength(100, ErrorMessage = "Email tối đa 100 ký tự.")]
    public string? Email { get; set; }

    [Required(ErrorMessage = "Địa chỉ chi tiết là bắt buộc.")]
    [StringLength(255, ErrorMessage = "Địa chỉ chi tiết tối đa 255 ký tự.")]
    public string? AddressDetail { get; set; }

    public int? ProvinceId { get; set; }
    public string? ProvinceName { get; set; }
    public int? DistrictId { get; set; }
    public string? DistrictName { get; set; }
    public int? CommuneId { get; set; }
    public string? WardCode { get; set; }
    public string? WardName { get; set; }
}


public sealed class CheckoutPaymentInputDto // Payment object.
{
    public string PaymentMethod { get; set; } = "COD"; // COD/VNPay/BankTransfer...
}
