using System.ComponentModel.DataAnnotations; // Dùng DataAnnotation cho validate ở BFF.

namespace FreshFarm.Web.Bff.Dtos; // Namespace DTO cho BFF.

public sealed class ProfilePageViewModel // Model render cho trang Profile.
{
    public string UserName { get; set; } = string.Empty; // Username hiển thị read-only.
    public string FullName { get; set; } = string.Empty; // Họ tên hiện tại.
    public string Email { get; set; } = string.Empty; // Email hiện tại.
    public string Phone { get; set; } = string.Empty; // SĐT hiện tại.
    public List<ProfileAddressItemDto> Addresses { get; set; } = new(); // Danh sách địa chỉ user.
    public string? ReturnUrl { get; set; } // URL quay lại nếu có.
}

public sealed class ProfileUpdateRequestDto // DTO submit form cập nhật profile.
{
    [Required(ErrorMessage = "Họ và tên không được để trống.")] // FullName bắt buộc.
    [StringLength(100, ErrorMessage = "Họ và tên tối đa 100 ký tự.")] // Giới hạn độ dài fullname.
    public string FullName { get; set; } = string.Empty; // Giá trị fullname.

    [Required(ErrorMessage = "Email không được để trống.")] // Email bắt buộc.
    [EmailAddress(ErrorMessage = "Email không đúng định dạng.")] // Validate định dạng email.
    [StringLength(100, ErrorMessage = "Email tối đa 100 ký tự.")] // Giới hạn độ dài email.
    public string Email { get; set; } = string.Empty; // Giá trị email.

    [Required(ErrorMessage = "Số điện thoại không được để trống.")] // Phone bắt buộc.
    [RegularExpression(@"^0\d{9}$", ErrorMessage = "Số điện thoại phải gồm 10 chữ số và bắt đầu bằng 0.")] // Rule phone.
    public string Phone { get; set; } = string.Empty; // Giá trị phone.

    public string UserName { get; set; } = string.Empty; // UserName giữ để hiển thị lại.
    public string? ReturnUrl { get; set; } // ReturnUrl gửi ngược sau submit.
}

public sealed class ProfileAddressItemDto // DTO 1 dòng địa chỉ của user.
{
    public int AddressId { get; set; } // PK địa chỉ.
    public string RecipientName { get; set; } = string.Empty; // Người nhận.
    public string Phone { get; set; } = string.Empty; // SĐT người nhận.
    public string AddressDetail { get; set; } = string.Empty; // Địa chỉ chi tiết.
    public string? Province { get; set; } // Tỉnh/Thành.
    public string? District { get; set; } // Quận/Huyện.
    public string? Ward { get; set; } // Phường/Xã.
    public bool IsDefault { get; set; } // Cờ mặc định.
}

public sealed class UpsertProfileAddressRequestDto // DTO tạo/sửa địa chỉ.
{
    [Required(ErrorMessage = "Tên người nhận không được để trống.")] // RecipientName bắt buộc.
    [StringLength(100, ErrorMessage = "Tên người nhận tối đa 100 ký tự.")] // Giới hạn độ dài.
    public string RecipientName { get; set; } = string.Empty; // Giá trị RecipientName.

    [Required(ErrorMessage = "Số điện thoại không được để trống.")] // Phone bắt buộc.
    [RegularExpression(@"^0\d{9}$", ErrorMessage = "Số điện thoại phải gồm 10 chữ số và bắt đầu bằng 0.")] // Rule phone.
    public string Phone { get; set; } = string.Empty; // Giá trị phone.

    [Required(ErrorMessage = "Địa chỉ chi tiết không được để trống.")] // AddressDetail bắt buộc.
    [StringLength(255, ErrorMessage = "Địa chỉ chi tiết tối đa 255 ký tự.")] // Giới hạn độ dài địa chỉ.
    public string AddressDetail { get; set; } = string.Empty; // Giá trị AddressDetail.

    [StringLength(100, ErrorMessage = "Tỉnh/Thành tối đa 100 ký tự.")] // Province tối đa 100.
    public string? Province { get; set; } // Giá trị Province.

    [StringLength(100, ErrorMessage = "Quận/Huyện tối đa 100 ký tự.")] // District tối đa 100.
    public string? District { get; set; } // Giá trị District.

    [StringLength(100, ErrorMessage = "Phường/Xã tối đa 100 ký tự.")] // Ward tối đa 100.
    public string? Ward { get; set; } // Giá trị Ward.

    public bool IsDefault { get; set; } // Cờ set mặc định.
    public string? ReturnUrl { get; set; } // ReturnUrl để redirect.
}