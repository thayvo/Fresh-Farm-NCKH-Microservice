using System.ComponentModel.DataAnnotations;

namespace FreshFarm.Web.Bff.Areas.Seller.Models;

public sealed class SellerSettingViewModel
{
    [Required(ErrorMessage = "Ten cua hang la bat buoc")]
    [StringLength(200, ErrorMessage = "Ten cua hang toi da 200 ky tu")]
    public string StoreName { get; set; } = string.Empty;

    [Required(ErrorMessage = "Dia chi cua hang la bat buoc")]
    [StringLength(500, ErrorMessage = "Dia chi toi da 500 ky tu")]
    public string StoreAddress { get; set; } = string.Empty;

    [Required(ErrorMessage = "Email ho tro la bat buoc")]
    [EmailAddress(ErrorMessage = "Email khong hop le")]
    [StringLength(150, ErrorMessage = "Email toi da 150 ky tu")]
    public string StoreEmail { get; set; } = string.Empty;

    [Required(ErrorMessage = "So dien thoai cua hang la bat buoc")]
    [StringLength(20, ErrorMessage = "So dien thoai toi da 20 ky tu")]
    public string StorePhone { get; set; } = string.Empty;

    public bool IsCODEnabled { get; set; } = true;

    [StringLength(2000, ErrorMessage = "Huong dan chuyen khoan toi da 2000 ky tu")]
    public string? BankTransferInstructions { get; set; }

    [StringLength(2000, ErrorMessage = "Thong tin tai khoan toi da 2000 ky tu")]
    public string? BankAccountInfo { get; set; }

    [Range(0, 1_000_000_000, ErrorMessage = "Phi van chuyen mac dinh khong hop le")]
    public decimal DefaultShippingFee { get; set; } = 30000;

    [Range(0, 1_000_000_000, ErrorMessage = "Moc mien phi van chuyen khong hop le")]
    public decimal FreeShippingThreshold { get; set; } = 500000;

    public bool IsEmailNewOrderEnabled { get; set; } = true;

    public bool IsEmailDeliveredEnabled { get; set; } = true;

    public bool IsEmailCancelledEnabled { get; set; } = true;

    [Required(ErrorMessage = "Email nhan thong bao admin la bat buoc")]
    [EmailAddress(ErrorMessage = "Email admin khong hop le")]
    [StringLength(150, ErrorMessage = "Email admin toi da 150 ky tu")]
    public string AdminNotificationEmail { get; set; } = string.Empty;

    [StringLength(150, ErrorMessage = "Tên người lấy hàng GHN tối đa 150 ký tự")]
    public string? GhnPickupName { get; set; }

    [StringLength(20, ErrorMessage = "Số điện thoại GHN tối đa 20 ký tự")]
    public string? GhnPickupPhone { get; set; }

    [StringLength(500, ErrorMessage = "Địa chỉ lấy hàng GHN tối đa 500 ký tự")]
    public string? GhnPickupAddress { get; set; }

    public int? GhnProvinceId { get; set; }

    [StringLength(150, ErrorMessage = "Tên tỉnh/thành GHN tối đa 150 ký tự")]
    public string? GhnProvinceName { get; set; }

    public int? GhnDistrictId { get; set; }

    [StringLength(150, ErrorMessage = "Tên quận/huyện GHN tối đa 150 ký tự")]
    public string? GhnDistrictName { get; set; }

    [StringLength(50, ErrorMessage = "Mã phường/xã GHN tối đa 50 ký tự")]
    public string? GhnWardCode { get; set; }

    [StringLength(150, ErrorMessage = "Tên phường/xã GHN tối đa 150 ký tự")]
    public string? GhnWardName { get; set; }

    public bool HasGhnOrigin { get; set; }

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;
}

public sealed class SellerRoleViewModel
{
    public int RoleID { get; set; }

    public string RoleName { get; set; } = string.Empty;

    public bool IsActive { get; set; } = true;
}

public sealed class SellerUserViewModel
{
    public int AdminID { get; set; }

    public string UserName { get; set; } = string.Empty;

    public string FullName { get; set; } = string.Empty;

    public string Email { get; set; } = string.Empty;

    public string? Phone { get; set; }

    public string? Avatar { get; set; }

    public int RoleID { get; set; }

    public SellerRoleViewModel? Role { get; set; }

    public bool IsActive { get; set; } = true;

    public DateTime CreatedDate { get; set; } = DateTime.UtcNow;

    public DateTime? UpdatedDate { get; set; }

    public DateTime? LastLogin { get; set; }
}
