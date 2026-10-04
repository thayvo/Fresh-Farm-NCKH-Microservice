using System.ComponentModel.DataAnnotations;
using System.Text.RegularExpressions;
using Microsoft.AspNetCore.Http;

namespace FreshFarm.Web.Bff.Dtos;

public sealed class ProfilePageViewModel
{
    public string UserName { get; set; } = string.Empty;
    public string FullName { get; set; } = string.Empty;
    public string Email { get; set; } = string.Empty;
    public string Phone { get; set; } = string.Empty;
    public SellerApplicationSummaryDto SellerApplication { get; set; } = new();
    public List<ProfileAddressItemDto> Addresses { get; set; } = new();
    public string? ReturnUrl { get; set; }
}

public sealed class AccountNotificationsPageViewModel
{
    private static readonly string[] NotificationSectionOrder =
    [
        "seller_review_update",
        "order_status",
        "location_update"
    ];

    public SellerApplicationSummaryDto SellerApplication { get; set; } = new();
    public string Query { get; set; } = string.Empty;
    public string Type { get; set; } = string.Empty;
    public bool? IsRead { get; set; }
    public bool RecentOnly { get; set; }
    public string FocusType { get; set; } = string.Empty;
    public int Page { get; set; } = 1;
    public int PageSize { get; set; } = 12;
    public int Total { get; set; }
    public int TotalPages { get; set; } = 1;
    public int TotalNotifications { get; set; }
    public int UnreadNotifications { get; set; }
    public int PushNotifications { get; set; }
    public int Recent24hNotifications { get; set; }
    public List<string> TypeOptions { get; set; } = new();
    public List<AccountNotificationListItemDto> Notifications { get; set; } = new();
    public string? ReturnUrl { get; set; }

    public bool HasNotifications => Notifications.Count > 0;
    public bool HasActiveFilters => !string.IsNullOrWhiteSpace(Query) || !string.IsNullOrWhiteSpace(Type) || IsRead.HasValue || RecentOnly;
    public bool HasAnyNotificationsOverall => TotalNotifications > 0;
    public int VisibleUnreadNotifications => Notifications.Count(x => !x.IsRead);
    public int VisibleRecentNotifications => Notifications.Count(x => x.IsRecentNotification);
    public string Recent24hSummary =>
        Recent24hNotifications > 0
            ? $"Có {Recent24hNotifications} cập nhật mới trong 24h trên toàn bộ trung tâm thông báo."
            : "Chưa có cập nhật mới nào phát sinh trong 24h gần nhất.";
    public string DisplayedCountSummary =>
        HasNotifications
            ? HasActiveFilters
                ? $"Hiển thị {Notifications.Count} mục sau khi áp dụng bộ lọc hiện tại."
                : $"Hiển thị {Notifications.Count} mục ở trang hiện tại."
            : ResultsSummary + ".";
    public string ResultsSummary =>
        HasNotifications
            ? $"Hiển thị {Notifications.Count} thông báo trên trang này"
            : HasAnyNotificationsOverall
                ? "Không còn thông báo nào khớp với bộ lọc hiện tại"
                : "Tài khoản của bạn chưa phát sinh thông báo nào";

    public string EmptyStateTitle =>
        HasAnyNotificationsOverall
            ? HasActiveFilters
                ? "Không có thông báo khớp bộ lọc"
                : "Chưa có thông báo trên trang này"
            : "Bạn chưa có thông báo nào";

    public string EmptyStateDescription
    {
        get
        {
            if (!HasAnyNotificationsOverall)
            {
                return sellerPrompt();
            }

            if (HasActiveFilters)
            {
                return "Hãy nới bộ lọc hoặc chuyển sang xem tất cả để không bỏ lỡ cập nhật quan trọng.";
            }

            return "Khi có cập nhật về đơn hàng, vận chuyển hoặc hồ sơ người bán, bạn sẽ thấy tất cả tại đây.";

            string sellerPrompt() =>
                SellerApplication.HasApplication
                    ? "Khi hồ sơ người bán hoặc đơn hàng có thay đổi, FreshFarm sẽ hiển thị tại trung tâm thông báo này."
                    : "Khi đơn hàng, vận chuyển hoặc hồ sơ người bán có cập nhật, FreshFarm sẽ hiển thị tại đây.";
        }
    }

    public string EmptyStatePrimaryActionLabel =>
        HasActiveFilters
            ? "Xóa bộ lọc"
            : SellerApplication.HasApplication || SellerApplication.IsSellerApproved
                ? "Xem hồ sơ người bán"
                : "Đăng ký người bán";

    public string EmptyStatePrimaryActionUrl =>
        HasActiveFilters
            ? "/account/notifications"
            : "/account/become-seller";

    public IReadOnlyList<AccountNotificationFilterChipDto> ActiveFilterChips
    {
        get
        {
            var chips = new List<AccountNotificationFilterChipDto>();
            if (!string.IsNullOrWhiteSpace(Type))
            {
                chips.Add(new AccountNotificationFilterChipDto
                {
                    Label = GetNotificationTypeLabel(Type),
                    Tone = "dark"
                });
            }

            if (IsRead == false)
            {
                chips.Add(new AccountNotificationFilterChipDto
                {
                    Label = "Chưa đọc",
                    Tone = "danger"
                });
            }
            else if (IsRead == true)
            {
                chips.Add(new AccountNotificationFilterChipDto
                {
                    Label = "Đã đọc",
                    Tone = "secondary"
                });
            }

            if (!string.IsNullOrWhiteSpace(Query))
            {
                chips.Add(new AccountNotificationFilterChipDto
                {
                    Label = $"Từ khóa: {Query.Trim()}",
                    Tone = "light border"
                });
            }

            if (RecentOnly)
            {
                chips.Add(new AccountNotificationFilterChipDto
                {
                    Label = "Mới trong 24h",
                    Tone = "warning"
                });
            }

            return chips;
        }
    }

    public IReadOnlyList<AccountNotificationSectionDto> NotificationSections =>
        Notifications
            .GroupBy(x => string.IsNullOrWhiteSpace(x.NotificationType) ? "account_update" : x.NotificationType, StringComparer.OrdinalIgnoreCase)
            .OrderByDescending(group => group.Any(x => !x.IsRead))
            .ThenBy(group => GetSectionOrder(group.Key))
            .ThenBy(group => AccountNotificationListItemDto.GetNotificationTypeLabel(group.Key), StringComparer.CurrentCulture)
            .Select((group, index) =>
            {
                var items = group
                    .OrderBy(x => x.IsRead)
                    .ThenByDescending(x => x.CreatedAt ?? DateTime.MinValue)
                    .ThenByDescending(x => x.NotificationId)
                    .ToList();
                return new AccountNotificationSectionDto
                {
                    SectionKey = $"notification-section-{index + 1}",
                    NotificationType = group.Key,
                    Label = AccountNotificationListItemDto.GetNotificationTypeLabel(group.Key),
                    UnreadCount = items.Count(x => !x.IsRead),
                    IsInitiallyExpanded = items.Any(x => !x.IsRead) || index == 0,
                    Notifications = items
                };
            })
            .ToList();

    public AccountNotificationSectionDto? PrioritySection =>
        NotificationSections
            .OrderByDescending(x => x.UnreadCount)
            .ThenByDescending(x => x.RecentCount)
            .ThenByDescending(x => x.LatestCreatedAt ?? DateTime.MinValue)
            .ThenByDescending(x => x.TotalCount)
            .FirstOrDefault(x => x.UnreadCount > 0 || x.RecentCount > 0 || x.TotalCount > 0);

    public int GetVisibleCount(string? type = null, bool? isRead = null)
    {
        IEnumerable<AccountNotificationListItemDto> query = Notifications;
        if (!string.IsNullOrWhiteSpace(type))
        {
            query = query.Where(x => string.Equals(x.NotificationType, type, StringComparison.OrdinalIgnoreCase));
        }

        if (isRead.HasValue)
        {
            query = query.Where(x => x.IsRead == isRead.Value);
        }

        return query.Count();
    }

    public string BuildNotificationsUrl(
        string? type = null,
        bool? isRead = null,
        bool? recentOnly = null,
        string? focusType = null,
        int? page = null)
    {
        var query = new List<string>();
        if (!string.IsNullOrWhiteSpace(Query))
        {
            query.Add($"q={Uri.EscapeDataString(Query)}");
        }

        if (!string.IsNullOrWhiteSpace(type))
        {
            query.Add($"type={Uri.EscapeDataString(type)}");
        }

        if (isRead.HasValue)
        {
            query.Add($"isRead={isRead.Value.ToString().ToLowerInvariant()}");
        }

        if (recentOnly ?? RecentOnly)
        {
            query.Add("recentOnly=true");
        }

        var resolvedFocusType = focusType;
        if (string.IsNullOrWhiteSpace(resolvedFocusType) && !string.IsNullOrWhiteSpace(FocusType))
        {
            resolvedFocusType = FocusType;
        }

        if (!string.IsNullOrWhiteSpace(resolvedFocusType))
        {
            query.Add($"focusType={Uri.EscapeDataString(resolvedFocusType)}");
        }

        query.Add($"page={page ?? 1}");

        if (!string.IsNullOrWhiteSpace(ReturnUrl))
        {
            query.Add($"returnUrl={Uri.EscapeDataString(ReturnUrl)}");
        }

        return "/account/notifications" + (query.Count > 0 ? "?" + string.Join("&", query) : string.Empty);
    }

    private static int GetSectionOrder(string notificationType)
    {
        var index = Array.FindIndex(
            NotificationSectionOrder,
            x => string.Equals(x, notificationType, StringComparison.OrdinalIgnoreCase));
        return index >= 0 ? index : NotificationSectionOrder.Length;
    }

    private static string GetNotificationTypeLabel(string? notificationType) =>
        AccountNotificationListItemDto.GetNotificationTypeLabel(notificationType);
}

public sealed class AccountNotificationFilterChipDto
{
    public string Label { get; set; } = string.Empty;
    public string Tone { get; set; } = "light border";
}

public sealed class AccountNotificationSectionDto
{
    public string SectionKey { get; set; } = string.Empty;
    public string NotificationType { get; set; } = string.Empty;
    public string Label { get; set; } = string.Empty;
    public int UnreadCount { get; set; }
    public bool IsInitiallyExpanded { get; set; } = true;
    public List<AccountNotificationListItemDto> Notifications { get; set; } = new();
    public int TotalCount => Notifications.Count;
    public bool HasReadItems => TotalCount > UnreadCount;
    public bool HasRecentItems => Notifications.Any(x => x.IsRecentNotification);
    public int RecentCount => Notifications.Count(x => x.IsRecentNotification);
    public DateTime? LatestCreatedAt => Notifications
        .Where(x => x.CreatedAt.HasValue)
        .Select(x => x.CreatedAt)
        .Max();

    public string? LastUpdatedLabel
    {
        get
        {
            if (!LatestCreatedAt.HasValue)
            {
                return null;
            }

            var age = DateTime.UtcNow - LatestCreatedAt.Value;
            if (age <= TimeSpan.FromMinutes(30))
            {
                return "Vừa có cập nhật";
            }

            if (age <= TimeSpan.FromHours(24))
            {
                return "Có cập nhật trong 24h";
            }

            return $"Cập nhật gần nhất {LatestCreatedAt.Value.ToLocalTime():dd/MM HH:mm}";
        }
    }

    public string SummaryText =>
        UnreadCount > 0
            ? $"{UnreadCount} chưa đọc, {TotalCount} cập nhật trong nhóm này"
            : $"{TotalCount} cập nhật trong nhóm này";

    public string PriorityHeadline =>
        UnreadCount > 0
            ? $"{Label} đang cần bạn xử lý trước"
            : HasRecentItems
                ? $"{Label} vừa có cập nhật mới"
                : $"{Label} là nhóm hoạt động gần đây";

    public string PrioritySummary
    {
        get
        {
            if (UnreadCount > 0)
            {
                return RecentCount > 0
                    ? $"{UnreadCount} chưa đọc, {RecentCount} cập nhật còn mới trong 24h."
                    : $"{UnreadCount} chưa đọc và nên được ưu tiên xử lý trước.";
            }

            if (RecentCount > 0)
            {
                return $"{RecentCount} cập nhật mới trong 24h đang chờ bạn xem lại.";
            }

            return SummaryText;
        }
    }

    public string HeroSummaryText =>
        UnreadCount > 0
            ? $"{UnreadCount} chưa đọc"
            : RecentCount > 0
                ? $"{RecentCount} mới trong 24h"
                : $"{TotalCount} cập nhật";
}

public sealed class AccountNotificationSectionViewModel
{
    public AccountNotificationsPageViewModel Page { get; set; } = new();
    public AccountNotificationSectionDto Section { get; set; } = new();
}

public sealed class SellerApplicationSummaryDto
{
    public bool HasApplication { get; set; }
    public bool IsSellerApproved { get; set; }
    public string Status { get; set; } = "not_applied";
    public string StatusLabel { get; set; } = "Chưa đăng ký";
    public string ReviewStatus { get; set; } = "not_applied";
    public string ReviewStatusLabel { get; set; } = "Chưa có hồ sơ";
    public string ReviewNote { get; set; } = string.Empty;
    public DateTime? ReviewedAtUtc { get; set; }
    public string StoreName { get; set; } = string.Empty;
    public string StoreAddress { get; set; } = string.Empty;
    public string StoreEmail { get; set; } = string.Empty;
    public string StorePhone { get; set; } = string.Empty;
    public string BankAccountInfo { get; set; } = string.Empty;
    public string BankTransferInstructions { get; set; } = string.Empty;
    public string AdminNotificationEmail { get; set; } = string.Empty;
    public string PickupName { get; set; } = string.Empty;
    public string PickupPhone { get; set; } = string.Empty;
    public string PickupAddress { get; set; } = string.Empty;
    public DateTime? SubmittedAtUtc { get; set; }
    public DateTime? UpdatedAtUtc { get; set; }
    public string Message { get; set; } = string.Empty;
    public SellerKycSummaryDto Kyc { get; set; } = new();
    public List<SellerApplicationReviewHistoryItemDto> ReviewHistory { get; set; } = new();

    public bool HasSellerReviewAlert =>
        string.Equals(Status, "approved", StringComparison.OrdinalIgnoreCase) ||
        string.Equals(ReviewStatus, "rejected", StringComparison.OrdinalIgnoreCase) ||
        string.Equals(ReviewStatus, "pending", StringComparison.OrdinalIgnoreCase);

    public string SellerReviewAlertTone =>
        string.Equals(Status, "approved", StringComparison.OrdinalIgnoreCase)
            ? "success"
            : string.Equals(ReviewStatus, "rejected", StringComparison.OrdinalIgnoreCase)
                ? "warning"
                : "info";

    public string SellerReviewAlertTitle =>
        string.Equals(Status, "approved", StringComparison.OrdinalIgnoreCase)
            ? "Hồ sơ người bán đã được duyệt"
            : string.Equals(ReviewStatus, "rejected", StringComparison.OrdinalIgnoreCase)
                ? "Hồ sơ người bán cần bổ sung"
                : "Hồ sơ người bán đang chờ duyệt";

    public string SellerReviewAlertBody
    {
        get
        {
            if (string.Equals(Status, "approved", StringComparison.OrdinalIgnoreCase))
            {
                return "Tài khoản của bạn đã có quyền người bán. Bạn có thể vào hồ sơ người bán để hoàn tất vận hành gian hàng.";
            }

            if (string.Equals(ReviewStatus, "rejected", StringComparison.OrdinalIgnoreCase))
            {
                return string.IsNullOrWhiteSpace(ReviewNote)
                    ? "Đội vận hành đã yêu cầu bạn cập nhật lại hồ sơ người bán trước khi duyệt tiếp."
                    : $"Đội vận hành đã yêu cầu bổ sung hồ sơ người bán: {ReviewNote.Trim()}";
            }

            return HasApplication
                ? "Hồ sơ người bán của bạn đang được FreshFarm rà soát. Bạn sẽ thấy cập nhật mới nhất ngay trong tài khoản."
                : "Bạn có thể gửi hồ sơ người bán để đội vận hành bắt đầu rà soát.";
        }
    }

    public bool HasNotificationFeed => HasSellerReviewAlert || ReviewHistory.Count > 0;

    public int NotificationCount =>
        ReviewHistory.Count > 0
            ? ReviewHistory.Count
            : HasSellerReviewAlert
                ? 1
                : 0;
}

public sealed class AccountNotificationNavSummaryDto
{
    public int UnreadCount { get; set; }
    public int RecentCount { get; set; }
    public string PriorityNotificationType { get; set; } = string.Empty;
    public string PriorityLabel { get; set; } = string.Empty;
    public string PrioritySummary { get; set; } = string.Empty;
    public List<AccountNotificationNavSectionSummaryDto> Sections { get; set; } = new();

    public bool HasHighlights => Sections.Count > 0;
    public string NotificationsUrl => BuildUrl();
    public string RecentNotificationsUrl => BuildUrl(recentOnly: true);
    public string PriorityNotificationsUrl =>
        string.IsNullOrWhiteSpace(PriorityNotificationType)
            ? NotificationsUrl
            : BuildUrl(type: PriorityNotificationType, focusType: PriorityNotificationType);

    public static AccountNotificationNavSummaryDto FromPage(AccountNotificationsPageViewModel? page)
    {
        if (page is null)
        {
            return new AccountNotificationNavSummaryDto();
        }

        return new AccountNotificationNavSummaryDto
        {
            UnreadCount = page.UnreadNotifications,
            RecentCount = page.Recent24hNotifications,
            PriorityNotificationType = page.PrioritySection?.NotificationType ?? string.Empty,
            PriorityLabel = page.PrioritySection?.Label ?? string.Empty,
            PrioritySummary = page.PrioritySection?.PrioritySummary ?? string.Empty,
            Sections = page.NotificationSections
                .Where(x => x.UnreadCount > 0 || x.RecentCount > 0)
                .Take(3)
                .Select(x => new AccountNotificationNavSectionSummaryDto
                {
                    SectionKey = x.SectionKey,
                    NotificationType = x.NotificationType,
                    Label = x.Label,
                    HeroSummaryText = x.HeroSummaryText,
                    HasUnread = x.UnreadCount > 0,
                    NavigateUrl = BuildUrl(
                        type: x.NotificationType,
                        isRead: x.UnreadCount > 0 ? false : null,
                        recentOnly: x.UnreadCount <= 0 && x.RecentCount > 0,
                        focusType: x.NotificationType)
                })
                .ToList()
        };
    }

    private static string BuildUrl(string? type = null, bool? isRead = null, bool recentOnly = false, string? focusType = null)
    {
        var query = new List<string>();
        if (!string.IsNullOrWhiteSpace(type))
        {
            query.Add($"type={Uri.EscapeDataString(type)}");
        }

        if (isRead.HasValue)
        {
            query.Add($"isRead={isRead.Value.ToString().ToLowerInvariant()}");
        }

        if (recentOnly)
        {
            query.Add("recentOnly=true");
        }

        if (!string.IsNullOrWhiteSpace(focusType))
        {
            query.Add($"focusType={Uri.EscapeDataString(focusType)}");
        }

        return "/account/notifications" + (query.Count > 0 ? "?" + string.Join("&", query) : string.Empty);
    }
}

public sealed class AccountNotificationNavSectionSummaryDto
{
    public string SectionKey { get; set; } = string.Empty;
    public string NotificationType { get; set; } = string.Empty;
    public string Label { get; set; } = string.Empty;
    public string HeroSummaryText { get; set; } = string.Empty;
    public bool HasUnread { get; set; }
    public string NavigateUrl { get; set; } = "/account/notifications";
}

public sealed class AccountNotificationListItemDto
{
    private static readonly Regex OrderStatusMessageRegex = new(
        @"trang thai\s+(?<status>.+?)(?:[.!?]|$)",
        RegexOptions.IgnoreCase | RegexOptions.Compiled | RegexOptions.CultureInvariant);
    private static readonly Regex ShippingStatusMessageRegex = new(
        @"moi nhat:\s*(?<status>.+?)(?:[.!?]|$)",
        RegexOptions.IgnoreCase | RegexOptions.Compiled | RegexOptions.CultureInvariant);

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

    public string Tone =>
        string.Equals(NotificationType, "seller_review_update", StringComparison.OrdinalIgnoreCase)
            ? (Title.Contains("duyệt", StringComparison.OrdinalIgnoreCase) || Message.Contains("được duyệt", StringComparison.OrdinalIgnoreCase)
                ? "success"
                : Title.Contains("bổ sung", StringComparison.OrdinalIgnoreCase) || Message.Contains("bổ sung", StringComparison.OrdinalIgnoreCase)
                    ? "warning"
                    : "info")
            : string.Equals(NotificationType, "order_status", StringComparison.OrdinalIgnoreCase)
                ? "info"
                : "secondary";

    public string NotificationTypeLabel =>
        GetNotificationTypeLabel(NotificationType);

    public static string GetNotificationTypeLabel(string? notificationType) =>
        string.Equals(notificationType, "seller_review_update", StringComparison.OrdinalIgnoreCase)
            ? "Cập nhật hồ sơ người bán"
            : string.Equals(notificationType, "order_status", StringComparison.OrdinalIgnoreCase)
                ? "Cập nhật đơn hàng"
                : string.Equals(notificationType, "location_update", StringComparison.OrdinalIgnoreCase)
                    ? "Cập nhật vị trí giao hàng"
                    : string.IsNullOrWhiteSpace(notificationType)
                        ? "Thông báo tài khoản"
                        : notificationType.Replace("_", " ", StringComparison.Ordinal).Trim();

    public string DestinationUrl =>
        string.Equals(NotificationType, "seller_review_update", StringComparison.OrdinalIgnoreCase)
            ? "/account/become-seller"
            : string.Equals(NotificationType, "order_status", StringComparison.OrdinalIgnoreCase) && OrderId > 0
                ? $"/account/orders/{OrderId}"
                : string.Equals(NotificationType, "location_update", StringComparison.OrdinalIgnoreCase) && OrderId > 0
                    ? $"/account/orders/{OrderId}"
                    : OrderId > 0
                    ? $"/account/orders/{OrderId}"
                    : "/account/notifications";

    public string ActionUrl =>
        NotificationId > 0
            ? $"/account/notifications/{NotificationId}/open?target={Uri.EscapeDataString(DestinationUrl)}"
            : DestinationUrl;

    public string ActionLabel =>
        string.Equals(NotificationType, "seller_review_update", StringComparison.OrdinalIgnoreCase)
            ? "Xem hồ sơ người bán"
            : string.Equals(NotificationType, "order_status", StringComparison.OrdinalIgnoreCase) ||
              string.Equals(NotificationType, "location_update", StringComparison.OrdinalIgnoreCase)
                ? "Xem đơn hàng"
                : OrderId > 0
                    ? "Xem chi tiết"
                    : "Mở thông báo";

    public string ContextLabel =>
        string.Equals(NotificationType, "seller_review_update", StringComparison.OrdinalIgnoreCase)
            ? "Hồ sơ người bán"
            : OrderId > 0
                ? $"Đơn #{OrderId:D6}"
                : "Tài khoản FreshFarm";

    public string PreviewCaption =>
        string.Equals(NotificationType, "seller_review_update", StringComparison.OrdinalIgnoreCase)
            ? IsSellerApprovalUpdate
                ? "Trạng thái hồ sơ"
                : IsSellerRejectUpdate
                    ? "Cần bổ sung"
                    : "Hành động"
            : string.Equals(NotificationType, "order_status", StringComparison.OrdinalIgnoreCase)
                ? "Trạng thái đơn"
                : string.Equals(NotificationType, "location_update", StringComparison.OrdinalIgnoreCase)
                    ? "Vận chuyển mới nhất"
                    : "Nội dung chính";

    public string PreviewValue
    {
        get
        {
            if (string.Equals(NotificationType, "seller_review_update", StringComparison.OrdinalIgnoreCase))
            {
                if (IsSellerApprovalUpdate)
                {
                    return "Đã được duyệt";
                }

                if (IsSellerRejectUpdate)
                {
                    return TryExtractSellerRejectReason() ?? Title.Trim();
                }

                return Title.Trim();
            }

            if (string.Equals(NotificationType, "order_status", StringComparison.OrdinalIgnoreCase))
            {
                var parsedStatus = TryExtractRegexValue(OrderStatusMessageRegex, Message);
                return string.IsNullOrWhiteSpace(parsedStatus) ? Title.Trim() : parsedStatus;
            }

            if (string.Equals(NotificationType, "location_update", StringComparison.OrdinalIgnoreCase))
            {
                var shippingStatus = TryExtractRegexValue(ShippingStatusMessageRegex, Message);
                return string.IsNullOrWhiteSpace(shippingStatus) ? Title.Trim() : shippingStatus;
            }

            return string.IsNullOrWhiteSpace(Message)
                ? Title.Trim()
                : Message.Trim();
        }
    }

    public string PreviewHint =>
        string.Equals(NotificationType, "seller_review_update", StringComparison.OrdinalIgnoreCase)
            ? IsSellerApprovalUpdate
                ? "Mở hồ sơ người bán để hoàn tất thiết lập và bắt đầu vận hành gian hàng."
                : IsSellerRejectUpdate
                    ? "Mở hồ sơ người bán để cập nhật giấy tờ và gửi lại hồ sơ."
                    : "Mở hồ sơ để xem yêu cầu mới nhất từ đội vận hành."
            : string.Equals(NotificationType, "order_status", StringComparison.OrdinalIgnoreCase)
                ? "Mở chi tiết đơn để xem timeline và sản phẩm liên quan."
                : string.Equals(NotificationType, "location_update", StringComparison.OrdinalIgnoreCase)
                    ? "Mở đơn để xem lịch sử giao hàng và trạng thái GHN."
                    : "Mở thông báo để xem thêm chi tiết.";

    public bool IsRecentNotification =>
        CreatedAt.HasValue && CreatedAt.Value >= DateTime.UtcNow.AddHours(-24);

    public string? RecencyLabel
    {
        get
        {
            if (!CreatedAt.HasValue)
            {
                return null;
            }

            var age = DateTime.UtcNow - CreatedAt.Value;
            if (age <= TimeSpan.FromMinutes(30))
            {
                return "Vừa cập nhật";
            }

            if (age <= TimeSpan.FromHours(24))
            {
                return "Mới trong 24h";
            }

            return null;
        }
    }

    private bool IsSellerApprovalUpdate =>
        string.Equals(NotificationType, "seller_review_update", StringComparison.OrdinalIgnoreCase) &&
        (Title.Contains("duyệt", StringComparison.OrdinalIgnoreCase) ||
         Message.Contains("được duyệt", StringComparison.OrdinalIgnoreCase) ||
         Message.Contains("da duoc duyet", StringComparison.OrdinalIgnoreCase));

    private bool IsSellerRejectUpdate =>
        string.Equals(NotificationType, "seller_review_update", StringComparison.OrdinalIgnoreCase) &&
        (Title.Contains("bổ sung", StringComparison.OrdinalIgnoreCase) ||
         Title.Contains("bo sung", StringComparison.OrdinalIgnoreCase) ||
         Message.Contains("bổ sung", StringComparison.OrdinalIgnoreCase) ||
         Message.Contains("bo sung", StringComparison.OrdinalIgnoreCase));

    private static string? TryExtractRegexValue(Regex regex, string? input)
    {
        if (string.IsNullOrWhiteSpace(input))
        {
            return null;
        }

        var match = regex.Match(input);
        if (!match.Success)
        {
            return null;
        }

        return match.Groups["status"].Value.Trim();
    }

    private string? TryExtractSellerRejectReason()
    {
        if (string.IsNullOrWhiteSpace(Message))
        {
            return null;
        }

        var normalizedMessage = Message.Trim();
        var separators = new[]
        {
            ":",
            "Lý do:",
            "Ly do:",
            "Nội dung:",
            "Noi dung:"
        };

        foreach (var separator in separators)
        {
            var index = normalizedMessage.IndexOf(separator, StringComparison.OrdinalIgnoreCase);
            if (index < 0)
            {
                continue;
            }

            var candidate = normalizedMessage[(index + separator.Length)..].Trim();
            if (!string.IsNullOrWhiteSpace(candidate))
            {
                return candidate.Trim().TrimEnd('.');
            }
        }

        var markers = new[] { "bổ sung hồ sơ người bán", "bổ sung hồ sơ seller" };
        foreach (var marker in markers)
        {
            var markerIndex = normalizedMessage.IndexOf(marker, StringComparison.OrdinalIgnoreCase);
            if (markerIndex < 0)
            {
                continue;
            }

            var candidate = normalizedMessage[(markerIndex + marker.Length)..].Trim();
            if (candidate.StartsWith(":", StringComparison.Ordinal))
            {
                candidate = candidate[1..].Trim();
            }

            if (!string.IsNullOrWhiteSpace(candidate))
            {
                return candidate.Trim().TrimEnd('.');
            }
        }

        return normalizedMessage.TrimEnd('.');
    }
}

public sealed class AccountNotificationsApiResponseDto
{
    public int Page { get; set; }
    public int PageSize { get; set; }
    public int Total { get; set; }
    public int TotalPages { get; set; }
    public AccountNotificationsStatsDto? Stats { get; set; }
    public AccountNotificationsFiltersDto? Filters { get; set; }
    public List<AccountNotificationListItemDto>? Notifications { get; set; }
}

public sealed class AccountNotificationsStatsDto
{
    public int TotalNotifications { get; set; }
    public int UnreadNotifications { get; set; }
    public int PushNotifications { get; set; }
    public int Recent24hNotifications { get; set; }
}

public sealed class AccountNotificationsFiltersDto
{
    public string? Q { get; set; }
    public string? Type { get; set; }
    public bool? IsRead { get; set; }
    public bool RecentOnly { get; set; }
    public List<string>? TypeOptions { get; set; }
}

public sealed class SellerApplicationReviewHistoryItemDto
{
    public string Action { get; set; } = string.Empty;
    public string ActionLabel =>
        string.Equals(Action, "approve", StringComparison.OrdinalIgnoreCase)
            ? "Đã duyệt"
            : string.Equals(Action, "reject", StringComparison.OrdinalIgnoreCase)
                ? "Yêu cầu bổ sung"
                : "Đã cập nhật";
    public string ReviewStatus { get; set; } = string.Empty;
    public string ReviewStatusLabel { get; set; } = string.Empty;
    public string Note { get; set; } = string.Empty;
    public DateTime ReviewedAtUtc { get; set; }
    public int? ReviewedByUserId { get; set; }
    public string ReviewerUserName { get; set; } = string.Empty;
    public string ReviewerFullName { get; set; } = string.Empty;
    public string ReviewerDisplayName =>
        !string.IsNullOrWhiteSpace(ReviewerFullName)
            ? ReviewerFullName.Trim()
            : !string.IsNullOrWhiteSpace(ReviewerUserName)
                ? ReviewerUserName.Trim()
                : "FreshFarm";
    public string TimelineTone =>
        string.Equals(ReviewStatus, "approved", StringComparison.OrdinalIgnoreCase)
            ? "success"
            : string.Equals(ReviewStatus, "rejected", StringComparison.OrdinalIgnoreCase)
                ? "warning"
                : "secondary";
}

public sealed class SellerKycSummaryDto
{
    public bool HasKycProfile { get; set; }
    public bool HasIdentityDocuments { get; set; }
    public bool HasBusinessLicense { get; set; }
    public string ReviewStatus { get; set; } = "not_applied";
    public string ReviewStatusLabel { get; set; } = "Chưa có hồ sơ";
    public string ReviewNote { get; set; } = string.Empty;
    public DateTime? ReviewedAtUtc { get; set; }
    public string LegalFullName { get; set; } = string.Empty;
    public string IdentityNumber { get; set; } = string.Empty;
    public string IdentityNumberMasked { get; set; } = string.Empty;
    public DateTime? IdentityIssuedDate { get; set; }
    public string IdentityIssuedPlace { get; set; } = string.Empty;
    public string TaxCode { get; set; } = string.Empty;
    public string BusinessLicenseNumber { get; set; } = string.Empty;
    public string CitizenIdFrontUrl { get; set; } = string.Empty;
    public string CitizenIdBackUrl { get; set; } = string.Empty;
    public string BusinessLicenseUrl { get; set; } = string.Empty;
    public string AdditionalDocumentUrl { get; set; } = string.Empty;
    public string Notes { get; set; } = string.Empty;
}

public sealed class BecomeSellerPageViewModel
{
    public SellerApplicationSummaryDto Current { get; set; } = new();
    public BecomeSellerRequestDto Form { get; set; } = new();
    public string? ReturnUrl { get; set; }
    public IFormFile? CitizenIdFrontFile { get; set; }
    public IFormFile? CitizenIdBackFile { get; set; }
    public IFormFile? BusinessLicenseFile { get; set; }
    public IFormFile? AdditionalDocumentFile { get; set; }
}

public sealed class BecomeSellerRequestDto
{
    [Required(ErrorMessage = "Tên cửa hàng không được để trống.")]
    [StringLength(255, ErrorMessage = "Tên cửa hàng tối đa 255 ký tự.")]
    public string StoreName { get; set; } = string.Empty;

    [Required(ErrorMessage = "Địa chỉ hoạt động không được để trống.")]
    [StringLength(500, ErrorMessage = "Địa chỉ hoạt động tối đa 500 ký tự.")]
    public string StoreAddress { get; set; } = string.Empty;

    [Required(ErrorMessage = "Email liên hệ không được để trống.")]
    [EmailAddress(ErrorMessage = "Email liên hệ không đúng định dạng.")]
    [StringLength(100, ErrorMessage = "Email liên hệ tối đa 100 ký tự.")]
    public string StoreEmail { get; set; } = string.Empty;

    [Required(ErrorMessage = "Số điện thoại cửa hàng không được để trống.")]
    [RegularExpression(@"^(0\d{9}|\+84\d{9})$", ErrorMessage = "Số điện thoại phải là số di động Việt Nam hợp lệ.")]
    public string StorePhone { get; set; } = string.Empty;

    [StringLength(500, ErrorMessage = "Thông tin tài khoản ngân hàng tối đa 500 ký tự.")]
    public string? BankAccountInfo { get; set; }

    [StringLength(1000, ErrorMessage = "Hướng dẫn chuyển khoản tối đa 1000 ký tự.")]
    public string? BankTransferInstructions { get; set; }

    [EmailAddress(ErrorMessage = "Email nhận thông báo không đúng định dạng.")]
    [StringLength(100, ErrorMessage = "Email nhận thông báo tối đa 100 ký tự.")]
    public string? AdminNotificationEmail { get; set; }

    [StringLength(150, ErrorMessage = "Tên người lấy hàng tối đa 150 ký tự.")]
    public string? PickupName { get; set; }

    [RegularExpression(@"^(0\d{9}|\+84\d{9})$", ErrorMessage = "Số điện thoại lấy hàng phải là số di động Việt Nam hợp lệ.")]
    public string? PickupPhone { get; set; }

    [StringLength(500, ErrorMessage = "Địa chỉ lấy hàng tối đa 500 ký tự.")]
    public string? PickupAddress { get; set; }

    [Required(ErrorMessage = "Họ tên trên CCCD là bắt buộc.")]
    [StringLength(150, ErrorMessage = "Họ tên pháp lý tối đa 150 ký tự.")]
    public string LegalFullName { get; set; } = string.Empty;

    [Required(ErrorMessage = "Số CCCD/CMND là bắt buộc.")]
    [StringLength(50, ErrorMessage = "Số CCCD/CMND tối đa 50 ký tự.")]
    public string IdentityNumber { get; set; } = string.Empty;

    [Required(ErrorMessage = "Ngày cấp CCCD/CMND là bắt buộc.")]
    public DateTime? IdentityIssuedDate { get; set; }

    [Required(ErrorMessage = "Nơi cấp CCCD/CMND là bắt buộc.")]
    [StringLength(255, ErrorMessage = "Nơi cấp CCCD/CMND tối đa 255 ký tự.")]
    public string IdentityIssuedPlace { get; set; } = string.Empty;

    [StringLength(50, ErrorMessage = "Mã số thuế tối đa 50 ký tự.")]
    public string? TaxCode { get; set; }

    [StringLength(100, ErrorMessage = "Số giấy phép kinh doanh tối đa 100 ký tự.")]
    public string? BusinessLicenseNumber { get; set; }

    [StringLength(1000, ErrorMessage = "Ghi chú hồ sơ tối đa 1000 ký tự.")]
    public string? Notes { get; set; }

    public string RecaptchaToken { get; set; } = string.Empty;

    public string? CitizenIdFrontUrl { get; set; }
    public string? CitizenIdBackUrl { get; set; }
    public string? BusinessLicenseUrl { get; set; }
    public string? AdditionalDocumentUrl { get; set; }
    public string? ReturnUrl { get; set; }
}

public sealed class ProfileUpdateRequestDto
{
    [Required(ErrorMessage = "Họ và tên không được để trống.")]
    [StringLength(100, ErrorMessage = "Họ và tên tối đa 100 ký tự.")]
    public string FullName { get; set; } = string.Empty;

    [Required(ErrorMessage = "Email không được để trống.")]
    [EmailAddress(ErrorMessage = "Email không đúng định dạng.")]
    [StringLength(100, ErrorMessage = "Email tối đa 100 ký tự.")]
    public string Email { get; set; } = string.Empty;

    [Required(ErrorMessage = "Số điện thoại không được để trống.")]
    [RegularExpression(@"^0\d{9}$", ErrorMessage = "Số điện thoại phải gồm 10 chữ số và bắt đầu bằng 0.")]
    public string Phone { get; set; } = string.Empty;

    public string UserName { get; set; } = string.Empty;
    public string? ReturnUrl { get; set; }
}

public sealed class ProfileAddressItemDto
{
    public int AddressId { get; set; }
    public string RecipientName { get; set; } = string.Empty;
    public string Phone { get; set; } = string.Empty;
    public string AddressDetail { get; set; } = string.Empty;
    public string? Province { get; set; }
    public string? District { get; set; }
    public string? Ward { get; set; }
    public bool IsDefault { get; set; }
}

public sealed class UpsertProfileAddressRequestDto
{
    [Required(ErrorMessage = "Tên người nhận không được để trống.")]
    [StringLength(100, ErrorMessage = "Tên người nhận tối đa 100 ký tự.")]
    public string RecipientName { get; set; } = string.Empty;

    [Required(ErrorMessage = "Số điện thoại không được để trống.")]
    [RegularExpression(@"^0\d{9}$", ErrorMessage = "Số điện thoại phải gồm 10 chữ số và bắt đầu bằng 0.")]
    public string Phone { get; set; } = string.Empty;

    [Required(ErrorMessage = "Địa chỉ chi tiết không được để trống.")]
    [StringLength(255, ErrorMessage = "Địa chỉ chi tiết tối đa 255 ký tự.")]
    public string AddressDetail { get; set; } = string.Empty;

    [StringLength(100, ErrorMessage = "Tỉnh/Thành tối đa 100 ký tự.")]
    public string? Province { get; set; }

    [StringLength(100, ErrorMessage = "Quận/Huyện tối đa 100 ký tự.")]
    public string? District { get; set; }

    [StringLength(100, ErrorMessage = "Phường/Xã tối đa 100 ký tự.")]
    public string? Ward { get; set; }

    public bool IsDefault { get; set; }
    public string? ReturnUrl { get; set; }
}
