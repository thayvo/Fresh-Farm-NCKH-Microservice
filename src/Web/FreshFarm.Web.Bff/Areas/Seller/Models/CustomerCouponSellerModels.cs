using System.ComponentModel.DataAnnotations;

namespace FreshFarm.Web.Bff.Areas.Seller.Models;

public sealed class CustomerViewModel
{
    public int UserID { get; set; }

    public string UserName { get; set; } = string.Empty;

    public string FullName { get; set; } = string.Empty;

    public string Email { get; set; } = string.Empty;

    public string? Phone { get; set; }

    public string? Address { get; set; }

    public DateTime CreatedDate { get; set; }

    public decimal TotalSpent { get; set; }

    public int OrderCount { get; set; }

    public string AvatarUrl { get; set; } = string.Empty;

    public bool IsActive => OrderCount > 0;

    public string TotalSpentFormatted => $"{TotalSpent:N0}₫";

    public string CreatedDateFormatted => CreatedDate.ToString("dd/MM/yyyy");
}

public sealed class CreateCustomerViewModel
{
    [Required(ErrorMessage = "Tên đăng nhập là bắt buộc")]
    [StringLength(100, ErrorMessage = "Tên đăng nhập tối đa 100 ký tự")]
    [RegularExpression(@"^[a-zA-Z0-9._-]+$", ErrorMessage = "Tên đăng nhập chỉ được chứa chữ cái, số, dấu chấm, gạch dưới hoặc gạch ngang")]
    public string UserName { get; set; } = string.Empty;

    [Required(ErrorMessage = "Họ và tên là bắt buộc")]
    [StringLength(100, ErrorMessage = "Họ và tên tối đa 100 ký tự")]
    public string FullName { get; set; } = string.Empty;

    [Required(ErrorMessage = "Email là bắt buộc")]
    [EmailAddress(ErrorMessage = "Email không hợp lệ")]
    [StringLength(100, ErrorMessage = "Email tối đa 100 ký tự")]
    public string Email { get; set; } = string.Empty;

    [Required(ErrorMessage = "Số điện thoại là bắt buộc")]
    [RegularExpression(@"^\d{10}$", ErrorMessage = "Số điện thoại phải đủ 10 số")]
    public string Phone { get; set; } = string.Empty;

    [Required(ErrorMessage = "Mật khẩu là bắt buộc")]
    [MinLength(6, ErrorMessage = "Mật khẩu tối thiểu 6 ký tự")]
    public string Password { get; set; } = string.Empty;

    [Required(ErrorMessage = "Xác nhận mật khẩu là bắt buộc")]
    [Compare(nameof(Password), ErrorMessage = "Mật khẩu xác nhận không khớp")]
    public string ConfirmPassword { get; set; } = string.Empty;

    public string? Address { get; set; }
}

public sealed class EditCustomerViewModel
{
    public int UserID { get; set; }

    public string UserName { get; set; } = string.Empty;

    [Required(ErrorMessage = "Họ và tên là bắt buộc")]
    [StringLength(100, ErrorMessage = "Họ và tên tối đa 100 ký tự")]
    public string FullName { get; set; } = string.Empty;

    [Required(ErrorMessage = "Email là bắt buộc")]
    [EmailAddress(ErrorMessage = "Email không hợp lệ")]
    [StringLength(100, ErrorMessage = "Email tối đa 100 ký tự")]
    public string Email { get; set; } = string.Empty;

    [RegularExpression(@"^$|^\d{10}$", ErrorMessage = "Số điện thoại phải đủ 10 số")]
    public string? Phone { get; set; }

    public string? Address { get; set; }

    public DateTime CreatedDate { get; set; }

    [MinLength(6, ErrorMessage = "Mật khẩu mới tối thiểu 6 ký tự")]
    public string? NewPassword { get; set; }

    [Compare(nameof(NewPassword), ErrorMessage = "Xác nhận mật khẩu không khớp")]
    public string? ConfirmPassword { get; set; }
}

public sealed class CustomerSearchResultDto
{
    public int UserID { get; set; }

    public string UserName { get; set; } = string.Empty;

    public string FullName { get; set; } = string.Empty;

    public string Email { get; set; } = string.Empty;

    public string? Phone { get; set; }

    public DateTime CreatedDate { get; set; }

    public decimal TotalSpent { get; set; }

    public int OrderCount { get; set; }

    public string AvatarUrl { get; set; } = string.Empty;
}

public sealed class Coupon
{
    public int CouponID { get; set; }

    public string Code { get; set; } = string.Empty;

    public decimal DiscountValue { get; set; }

    public DateTime ExpiryDate { get; set; }

    public decimal? MinOrderValue { get; set; }

    public string DiscountType { get; set; } = "fixed";

    public bool IsActive { get; set; } = true;

    public int? UsageLimit { get; set; }

    public int UsedCount { get; set; }

    public string? Description { get; set; }

    public int? CreatedBy { get; set; }

    public DateTime CreatedDate { get; set; }

    public DateTime? UpdatedDate { get; set; }

    public decimal? MaxDiscountAmount { get; set; }

    public bool IsExpired => ExpiryDate.Date < DateTime.Today;

    public bool IsUsageLimitReached => UsageLimit.HasValue && UsedCount >= UsageLimit.Value;

    public string DiscountTypeDisplay => string.Equals(DiscountType, "percent", StringComparison.OrdinalIgnoreCase)
        ? "Phần trăm"
        : "Tiền cố định";

    public string DiscountDisplay => string.Equals(DiscountType, "percent", StringComparison.OrdinalIgnoreCase)
        ? $"{DiscountValue:0.##}%"
        : $"{DiscountValue:N0}₫";

    public string MaxDiscountDisplay =>
        string.Equals(DiscountType, "percent", StringComparison.OrdinalIgnoreCase) && MaxDiscountAmount.HasValue
            ? $"Tối đa {MaxDiscountAmount.Value:N0}₫"
            : string.Empty;

    public string UsageLimitDisplay => UsageLimit.HasValue
        ? $"{UsedCount}/{UsageLimit.Value}"
        : $"{UsedCount}/∞";

    public string StatusDisplayDetailed
    {
        get
        {
            if (!IsActive)
            {
                return "Đã vô hiệu hóa";
            }

            if (IsExpired)
            {
                return "Đã hết hạn";
            }

            if (IsUsageLimitReached)
            {
                return "Hết lượt dùng";
            }

            return "Đang hoạt động";
        }
    }

    public string StatusBadgeClass
    {
        get
        {
            if (!IsActive)
            {
                return "bg-secondary";
            }

            if (IsExpired)
            {
                return "bg-danger";
            }

            if (IsUsageLimitReached)
            {
                return "bg-warning text-dark";
            }

            return "bg-success";
        }
    }
}

public sealed class CouponUsageHistoryDto
{
    public int UsageID { get; set; }

    public DateTime UsedDate { get; set; }

    public string CustomerName { get; set; } = string.Empty;

    public string CustomerEmail { get; set; } = string.Empty;

    public int OrderID { get; set; }

    public decimal DiscountAmount { get; set; }
}

public sealed class CouponDistributionDto
{
    public int DistributionID { get; set; }

    public DateTime SentDate { get; set; }

    public string CustomerName { get; set; } = string.Empty;

    public string CustomerEmail { get; set; } = string.Empty;

    public bool IsUsed { get; set; }

    public DateTime? UsedDate { get; set; }

    public string Channel { get; set; } = string.Empty;

    public string Status { get; set; } = string.Empty;
}
