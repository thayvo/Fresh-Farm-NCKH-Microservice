using System; // Dùng kiểu DateTime.

namespace FreshFarm.Identity.Api.Models; // Namespace model entity.

public partial class AddressBook // Entity ánh xạ bảng AddressBook.
{
    public int AddressId { get; set; } // PK của bảng AddressBook.
    public int UserId { get; set; } // FK trỏ tới Users.UserId.
    public string RecipientName { get; set; } = string.Empty; // Người nhận.
    public string Phone { get; set; } = string.Empty; // SĐT người nhận.
    public string AddressDetail { get; set; } = string.Empty; // Địa chỉ chi tiết.
    public string? Province { get; set; } // Tỉnh/Thành.
    public string? District { get; set; } // Quận/Huyện.
    public string? Ward { get; set; } // Phường/Xã.
    public bool IsDefault { get; set; } // Cờ mặc định.
    public bool IsActive { get; set; } // Cờ active để soft delete.
    public DateTime CreatedAt { get; set; } // Ngày tạo.
    public DateTime? UpdatedAt { get; set; } // Ngày cập nhật.

    public virtual User User { get; set; } = null!; // Navigation về User.
}