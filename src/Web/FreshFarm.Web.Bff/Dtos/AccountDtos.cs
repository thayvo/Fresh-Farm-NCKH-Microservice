using System.ComponentModel.DataAnnotations;

namespace FreshFarm.Web.Bff.Dtos
{
    public sealed class LoginRequestDto // Payload gui den Identity /auth/login.
    {
        public string Identifier { get; set; } = string.Empty; // Email hoac username.
        public string Password { get; set; } = string.Empty; // Mat khau.
    }

    public sealed class RegisterRequestDto // Payload gui den Identity /auth/register.
    {
        public string Email { get; set; } = string.Empty; // Email dang ky.
        public string UserName { get; set; } = string.Empty; // Ten dang nhap.
        public string FullName { get; set; } = string.Empty; // Ho ten.
        public string Phone { get; set; } = string.Empty; // So dien thoai.
        public string Password { get; set; } = string.Empty; // Mat khau.
        public string ConfirmPassword { get; set; } = string.Empty; // Xac nhan mat khau.
        public string RoleName { get; set; } = "Customer"; // Mac dinh role customer.
    }

    public sealed class ForgotPasswordRequestDto // Payload gui yeu cau quen mat khau.
    {
        [Required(ErrorMessage = "Email không được để trống.")]
        [EmailAddress(ErrorMessage = "Email không đúng định dạng.")]
        [StringLength(100, ErrorMessage = "Email tối đa 100 ký tự.")]
        public string Email { get; set; } = string.Empty;
    }

    public sealed class ResetPasswordRequestDto // Payload dat lai mat khau tu email reset.
    {
        [Required(ErrorMessage = "Email không được để trống.")]
        [EmailAddress(ErrorMessage = "Email không đúng định dạng.")]
        [StringLength(100, ErrorMessage = "Email tối đa 100 ký tự.")]
        public string Email { get; set; } = string.Empty;

        [Required(ErrorMessage = "Token đặt lại mật khẩu là bắt buộc.")]
        public string Token { get; set; } = string.Empty;

        [Required(ErrorMessage = "Mật khẩu mới không được để trống.")]
        [StringLength(100, MinimumLength = 6, ErrorMessage = "Mật khẩu mới phải từ 6 đến 100 ký tự.")]
        public string NewPassword { get; set; } = string.Empty;

        [Required(ErrorMessage = "Xác nhận mật khẩu không được để trống.")]
        [Compare(nameof(NewPassword), ErrorMessage = "Xác nhận mật khẩu không khớp.")]
        public string ConfirmPassword { get; set; } = string.Empty;
    }

    public sealed class AuthResponseDto // Body nhan ve tu Identity login.
    {
        public string AccessToken { get; set; } = string.Empty; // JWT.
        public DateTime ExpiredAtUtc { get; set; } // Han token.
    }

    public sealed class OrderHistoryItemDto // Item de render lich su don.
    {
        public int OrderId { get; set; } // Ma don.
        public DateTime OrderDate { get; set; } // Ngay tao.
        public decimal TotalAmount { get; set; } // Tong tien.
        public string Status { get; set; } = string.Empty; // Trang thai don.
        public string? PaymentStatus { get; set; } // Trang thai thanh toan.
    }

    public sealed class OrderDetailDto // DTO chi tiet don de render trang OrderDetail.
    {
        public int OrderId { get; set; } // Ma don.
        public DateTime OrderDate { get; set; } // Ngay dat.
        public decimal ShippingFee { get; set; } // Phi van chuyen.
        public decimal TotalAmount { get; set; } // Tong tien.
        public string? OrderNote { get; set; } // Ghi chu don.
        public string Status { get; set; } = string.Empty; // Trang thai don.
        public string? PaymentStatus { get; set; } // Trang thai thanh toan tong quan.
        public DateTime? PaidAt { get; set; } // Thoi diem da thanh toan.
        public string? BuyerFullName { get; set; } // Ten nguoi mua/nhan.
        public string? BuyerPhone { get; set; } // So dien thoai.
        public string? BuyerEmail { get; set; } // Email nguoi mua.
        public int PointsEarned { get; set; } // Diem duoc cong.
        public int PointsRedeemed { get; set; } // Diem da dung.
        public List<OrderDetailItemDto> Items { get; set; } = new(); // Danh sach san pham.
        public List<OrderDetailShippingDto> Shippings { get; set; } = new(); // Danh sach dia chi giao.
        public List<OrderDetailPaymentDto> Payments { get; set; } = new(); // Danh sach giao dich thanh toan.
    }

    public sealed class OrderDetailItemDto // Tung dong san pham trong don.
    {
        public int OrderDetailId { get; set; } // Ma dong chi tiet.
        public int ProductId { get; set; } // Ma san pham.
        public int Quantity { get; set; } // So luong.
        public decimal UnitPrice { get; set; } // Don gia.
        public string? UnitSymbol { get; set; } // Don vi tinh.
    }

    public sealed class OrderDetailShippingDto // Thong tin giao nhan cua don.
    {
        public int ShippingId { get; set; } // Ma shipping.
        public string? ShippingType { get; set; } // Kieu giao hang.
        public string? FullName { get; set; } // Ten nguoi nhan.
        public string? Phone { get; set; } // So dien thoai.
        public string? Email { get; set; } // Email.
        public string? AddressDetail { get; set; } // Dia chi.
        public int? ProvinceId { get; set; } // Ma tinh/thanh.
        public int? CommuneId { get; set; } // Ma phuong/xa.
    }

    public sealed class OrderDetailPaymentDto // Thong tin thanh toan.
    {
        public int PaymentId { get; set; } // Ma payment.
        public string? PaymentMethod { get; set; } // Phuong thuc thanh toan.
        public string? BankName { get; set; } // Ten ngan hang.
        public string? AccountName { get; set; } // Chu tai khoan.
        public string? AccountNumber { get; set; } // So tai khoan.
        public string? TransactionCode { get; set; } // Ma giao dich.
        public string? PaymentStatus { get; set; } // Trang thai payment.
        public DateTime? PaymentDate { get; set; } // Thoi diem thanh toan.
    }
}
