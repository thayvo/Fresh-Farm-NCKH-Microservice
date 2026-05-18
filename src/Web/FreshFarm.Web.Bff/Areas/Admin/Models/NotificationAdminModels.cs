using System.ComponentModel.DataAnnotations;

namespace FreshFarm.Web.Bff.Areas.Admin.Models;

public sealed class NotificationBroadcastEditorInput
{
    [Required(ErrorMessage = "Vui lòng chọn đối tượng nhận.")]
    public string TargetAudience { get; set; } = "all";

    [Range(1, int.MaxValue, ErrorMessage = "User ID phải lớn hơn 0.")]
    public int? TargetUserId { get; set; }

    [Required(ErrorMessage = "Vui lòng nhập tiêu đề.")]
    [StringLength(255, ErrorMessage = "Tiêu đề tối đa 255 ký tự.")]
    public string Title { get; set; } = string.Empty;

    [Required(ErrorMessage = "Vui lòng nhập nội dung.")]
    [StringLength(2000, ErrorMessage = "Nội dung tối đa 2000 ký tự.")]
    public string Message { get; set; } = string.Empty;

    [Required(ErrorMessage = "Vui lòng chọn loại thông báo.")]
    [StringLength(50, ErrorMessage = "Loại thông báo tối đa 50 ký tự.")]
    public string NotificationType { get; set; } = "admin_broadcast";

    public bool ShowPopup { get; set; } = true;

    [StringLength(20, ErrorMessage = "Kiểu popup tối đa 20 ký tự.")]
    public string PopupType { get; set; } = "text";

    [StringLength(1000, ErrorMessage = "URL ảnh tối đa 1000 ký tự.")]
    public string? PopupImageUrl { get; set; }

    public DateTime? ExpiresAt { get; set; }

    public string PopupTypeLabel => NormalizePopupType(PopupType, ShowPopup) switch
    {
        "image" => "Popup ảnh",
        "text" => "Popup chữ",
        _ => "Không hiện popup"
    };

    public string TargetAudienceLabel => NormalizeTargetAudience(TargetAudience) switch
    {
        "buyer" => "Người mua",
        "seller" => "Seller",
        "user" => TargetUserId.HasValue ? $"User #{TargetUserId.Value}" : "User cụ thể",
        _ => "Tất cả buyer và seller"
    };

    public static string NormalizeTargetAudience(string? value)
    {
        var normalized = value?.Trim().ToLowerInvariant();
        return normalized is "all" or "buyer" or "seller" or "user" ? normalized : "all";
    }

    public static string NormalizePopupType(string? value, bool showPopup)
    {
        if (!showPopup)
        {
            return "none";
        }

        var normalized = value?.Trim().ToLowerInvariant();
        return normalized is "image" or "text" ? normalized : "text";
    }
}

public sealed class NotificationCenterPageViewModel
{
    public string Query { get; set; } = string.Empty;

    public string Type { get; set; } = string.Empty;

    public bool? IsRead { get; set; }

    public int? UserId { get; set; }

    public int? OrderId { get; set; }

    public int Page { get; set; } = 1;

    public int PageSize { get; set; } = 12;

    public int Total { get; set; }

    public int TotalPages { get; set; } = 1;

    public int TotalNotifications { get; set; }

    public int UnreadNotifications { get; set; }

    public int PushNotifications { get; set; }

    public int Recent24hNotifications { get; set; }

    public List<string> TypeOptions { get; set; } = new();

    public List<NotificationCenterRowViewModel> Rows { get; set; } = new();
}

public sealed class NotificationCenterRowViewModel
{
    public int NotificationId { get; set; }

    public int UserId { get; set; }

    public int OrderId { get; set; }

    public string NotificationType { get; set; } = string.Empty;

    public string Title { get; set; } = string.Empty;

    public string Message { get; set; } = string.Empty;

    public bool IsRead { get; set; }

    public bool IsPushNotification { get; set; }

    public DateTime? CreatedAt { get; set; }

    public DateTime? ExpiresAt { get; set; }

    public DateTime? ReadAt { get; set; }
}

internal sealed class NotificationCenterApiResponse
{
    public int Page { get; set; }

    public int PageSize { get; set; }

    public int Total { get; set; }

    public int TotalPages { get; set; }

    public NotificationStatsApiDto? Stats { get; set; }

    public NotificationFiltersApiDto? Filters { get; set; }

    public List<NotificationRowApiDto>? Notifications { get; set; }
}

internal sealed class NotificationStatsApiDto
{
    public int TotalNotifications { get; set; }

    public int UnreadNotifications { get; set; }

    public int PushNotifications { get; set; }

    public int Recent24hNotifications { get; set; }
}

internal sealed class NotificationFiltersApiDto
{
    public string? Q { get; set; }

    public string? Type { get; set; }

    public bool? IsRead { get; set; }

    public int? UserId { get; set; }

    public int? OrderId { get; set; }

    public List<string>? TypeOptions { get; set; }
}

internal sealed class NotificationRowApiDto
{
    public int NotificationId { get; set; }

    public int UserId { get; set; }

    public int OrderId { get; set; }

    public string? NotificationType { get; set; }

    public string? Title { get; set; }

    public string? Message { get; set; }

    public bool IsRead { get; set; }

    public bool IsPushNotification { get; set; }

    public DateTime? CreatedAt { get; set; }

    public DateTime? ExpiresAt { get; set; }

    public DateTime? ReadAt { get; set; }
}
