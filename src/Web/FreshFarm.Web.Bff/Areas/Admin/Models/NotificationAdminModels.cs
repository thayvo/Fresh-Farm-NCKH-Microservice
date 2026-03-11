namespace FreshFarm.Web.Bff.Areas.Admin.Models;

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

    public DateTime? ReadAt { get; set; }
}
