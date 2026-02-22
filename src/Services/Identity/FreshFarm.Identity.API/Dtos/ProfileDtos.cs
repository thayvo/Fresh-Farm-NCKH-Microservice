using System.ComponentModel.DataAnnotations; // Dùng DataAnnotation cho validate request.

namespace FreshFarm.Identity.Api.Dtos; // Namespace DTO cho Identity API.

public sealed class ProfileResponseDto // DTO trả về thông tin profile cho BFF.
{
    public int UserId { get; set; } // Id user đăng nhập hiện tại.
    public string UserName { get; set; } = string.Empty; // Username hiện tại.
    public string FullName { get; set; } = string.Empty; // Họ tên hiển thị.
    public string Email { get; set; } = string.Empty; // Email hiện tại.
    public string Phone { get; set; } = string.Empty; // SĐT hiện tại.
}

public sealed class UpdateProfileRequestDto // DTO nhận payload cập nhật profile.
{
    [Required(ErrorMessage = "Họ và tên là bắt buộc.")] // FullName bắt buộc.
    [StringLength(100, ErrorMessage = "Họ và tên tối đa 100 ký tự.")] // Giới hạn độ dài.
    public string FullName { get; set; } = string.Empty; // Giá trị họ tên.

    [Required(ErrorMessage = "Email là bắt buộc.")] // Email bắt buộc.
    [EmailAddress(ErrorMessage = "Email không đúng định dạng.")] // Validate định dạng email.
    [StringLength(100, ErrorMessage = "Email tối đa 100 ký tự.")] // Giới hạn độ dài theo DB.
    public string Email { get; set; } = string.Empty; // Giá trị email.

    [Required(ErrorMessage = "Số điện thoại là bắt buộc.")] // Phone bắt buộc.
    [RegularExpression(@"^0\d{9}$", ErrorMessage = "Số điện thoại phải gồm 10 chữ số và bắt đầu bằng 0.")] // Rule phone 10 số.
    public string Phone { get; set; } = string.Empty; // Giá trị phone.
}

public sealed class AddressResponseDto // DTO trả về 1 địa chỉ trong danh sách.
{
    public int AddressId { get; set; } // Khóa chính địa chỉ.
    public string RecipientName { get; set; } = string.Empty; // Người nhận.
    public string Phone { get; set; } = string.Empty; // SĐT người nhận.
    public string AddressDetail { get; set; } = string.Empty; // Địa chỉ chi tiết.
    public string? Province { get; set; } // Tỉnh/Thành.
    public string? District { get; set; } // Quận/Huyện.
    public string? Ward { get; set; } // Phường/Xã.
    public bool IsDefault { get; set; } // Địa chỉ mặc định hay không.
}

public sealed class UpsertAddressRequestDto // DTO dùng cho cả tạo mới và cập nhật địa chỉ.
{
    [Required(ErrorMessage = "Tên người nhận là bắt buộc.")] // RecipientName bắt buộc.
    [StringLength(100, ErrorMessage = "Tên người nhận tối đa 100 ký tự.")] // Giới hạn độ dài.
    public string RecipientName { get; set; } = string.Empty; // Giá trị người nhận.

    [Required(ErrorMessage = "Số điện thoại là bắt buộc.")] // Phone bắt buộc.
    [RegularExpression(@"^0\d{9}$", ErrorMessage = "Số điện thoại phải gồm 10 chữ số và bắt đầu bằng 0.")] // Rule phone.
    public string Phone { get; set; } = string.Empty; // Giá trị phone.

    [Required(ErrorMessage = "Địa chỉ chi tiết là bắt buộc.")] // AddressDetail bắt buộc.
    [StringLength(255, ErrorMessage = "Địa chỉ chi tiết tối đa 255 ký tự.")] // Giới hạn độ dài.
    public string AddressDetail { get; set; } = string.Empty; // Giá trị địa chỉ.

    [StringLength(100, ErrorMessage = "Tỉnh/Thành tối đa 100 ký tự.")] // Giới hạn độ dài Province.
    public string? Province { get; set; } // Giá trị Province.

    [StringLength(100, ErrorMessage = "Quận/Huyện tối đa 100 ký tự.")] // Giới hạn độ dài District.
    public string? District { get; set; } // Giá trị District.

    [StringLength(100, ErrorMessage = "Phường/Xã tối đa 100 ký tự.")] // Giới hạn độ dài Ward.
    public string? Ward { get; set; } // Giá trị Ward.

    public bool IsDefault { get; set; } // Cho phép set mặc định ngay khi tạo/sửa.
}