using FreshFarm.Web.Bff.Dtos;
using Xunit;

namespace FreshFarm.Web.Bff.Tests;

public sealed class SellerApplicationSummaryDtoTests
{
    [Fact]
    public void SellerReviewAlert_ReturnsRejectedAlertWithReviewNote()
    {
        var dto = new SellerApplicationSummaryDto
        {
            HasApplication = true,
            Status = "pending",
            ReviewStatus = "rejected",
            ReviewNote = "Thiếu ảnh CCCD mặt sau."
        };

        Assert.True(dto.HasSellerReviewAlert);
        Assert.Equal("warning", dto.SellerReviewAlertTone);
        Assert.Equal("Hồ sơ seller cần bổ sung", dto.SellerReviewAlertTitle);
        Assert.Contains("Thiếu ảnh CCCD mặt sau.", dto.SellerReviewAlertBody);
    }

    [Fact]
    public void SellerReviewAlert_ReturnsApprovedAlertWhenSellerApproved()
    {
        var dto = new SellerApplicationSummaryDto
        {
            HasApplication = true,
            Status = "approved",
            ReviewStatus = "approved"
        };

        Assert.True(dto.HasSellerReviewAlert);
        Assert.Equal("success", dto.SellerReviewAlertTone);
        Assert.Equal("Hồ sơ seller đã được duyệt", dto.SellerReviewAlertTitle);
        Assert.Contains("đã có quyền người bán", dto.SellerReviewAlertBody, System.StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void ReviewHistoryItem_PrefersReviewerFullNameAndMapsApproveTone()
    {
        var item = new SellerApplicationReviewHistoryItemDto
        {
            Action = "approve",
            ReviewStatus = "approved",
            ReviewStatusLabel = "Đã duyệt",
            ReviewerUserName = "admin.a",
            ReviewerFullName = "Admin A"
        };

        Assert.Equal("Đã duyệt", item.ActionLabel);
        Assert.Equal("success", item.TimelineTone);
        Assert.Equal("Admin A", item.ReviewerDisplayName);
    }

    [Fact]
    public void NotificationCount_ReturnsReviewHistoryCount_WhenHistoryExists()
    {
        var dto = new SellerApplicationSummaryDto
        {
            Status = "approved",
            ReviewStatus = "approved",
            ReviewHistory =
            [
                new SellerApplicationReviewHistoryItemDto { Action = "reject", ReviewStatus = "rejected" },
                new SellerApplicationReviewHistoryItemDto { Action = "approve", ReviewStatus = "approved" }
            ]
        };

        Assert.True(dto.HasNotificationFeed);
        Assert.Equal(2, dto.NotificationCount);
    }

    [Fact]
    public void AccountNotificationListItemDto_ReturnsSuccessTone_ForApprovedSellerUpdate()
    {
        var dto = new AccountNotificationListItemDto
        {
            NotificationType = "seller_review_update",
            Title = "Hồ sơ seller đã được duyệt",
            Message = "Tài khoản của bạn đã được duyệt."
        };

        Assert.Equal("success", dto.Tone);
    }

    [Fact]
    public void AccountNotificationListItemDto_ReturnsFriendlyTypeLabel()
    {
        var dto = new AccountNotificationListItemDto
        {
            NotificationType = "seller_review_update"
        };

        Assert.Equal("Cập nhật hồ sơ seller", dto.NotificationTypeLabel);
    }

    [Fact]
    public void AccountNotificationListItemDto_BuildsApprovedSellerPreview()
    {
        var dto = new AccountNotificationListItemDto
        {
            NotificationType = "seller_review_update",
            Title = "Hồ sơ seller đã được duyệt",
            Message = "Hồ sơ đăng ký người bán của bạn đã được FreshFarm phê duyệt."
        };

        Assert.Equal("Trạng thái hồ sơ", dto.PreviewCaption);
        Assert.Equal("Đã được duyệt", dto.PreviewValue);
        Assert.Contains("hoàn tất thiết lập", dto.PreviewHint, System.StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void AccountNotificationListItemDto_BuildsRejectedSellerPreviewFromReason()
    {
        var dto = new AccountNotificationListItemDto
        {
            NotificationType = "seller_review_update",
            Title = "Hồ sơ seller cần bổ sung",
            Message = "Đội vận hành đã yêu cầu bổ sung hồ sơ seller: Thiếu ảnh CCCD mặt sau rõ nét."
        };

        Assert.Equal("Cần bổ sung", dto.PreviewCaption);
        Assert.Equal("Thiếu ảnh CCCD mặt sau rõ nét", dto.PreviewValue);
        Assert.Contains("gửi lại hồ sơ", dto.PreviewHint, System.StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void AccountNotificationListItemDto_BuildsOrderStatusPreviewFromMessage()
    {
        var dto = new AccountNotificationListItemDto
        {
            NotificationType = "order_status",
            OrderId = 166,
            Title = "Don hang #000166 da cap nhat trang thai",
            Message = "Don hang cua ban da chuyen sang trang thai dang xu ly."
        };

        Assert.Equal("Đơn #000166", dto.ContextLabel);
        Assert.Equal("Trạng thái đơn", dto.PreviewCaption);
        Assert.Equal("dang xu ly", dto.PreviewValue);
        Assert.Contains("timeline", dto.PreviewHint, System.StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void AccountNotificationListItemDto_BuildsShippingPreviewFromMessage()
    {
        var dto = new AccountNotificationListItemDto
        {
            NotificationType = "location_update",
            OrderId = 166,
            Title = "Van chuyen don #000166 da duoc cap nhat",
            Message = "Trang thai giao hang moi nhat: San sang lay hang."
        };

        Assert.Equal("Vận chuyển mới nhất", dto.PreviewCaption);
        Assert.Equal("San sang lay hang", dto.PreviewValue);
    }

    [Fact]
    public void AccountNotificationListItemDto_ReturnsRecentLabels_ForFreshNotifications()
    {
        var veryRecent = new AccountNotificationListItemDto
        {
            CreatedAt = DateTime.UtcNow.AddMinutes(-10)
        };

        var within24Hours = new AccountNotificationListItemDto
        {
            CreatedAt = DateTime.UtcNow.AddHours(-8)
        };

        var oldNotification = new AccountNotificationListItemDto
        {
            CreatedAt = DateTime.UtcNow.AddDays(-2)
        };

        Assert.Equal("Vừa cập nhật", veryRecent.RecencyLabel);
        Assert.True(veryRecent.IsRecentNotification);
        Assert.Equal("Mới trong 24h", within24Hours.RecencyLabel);
        Assert.True(within24Hours.IsRecentNotification);
        Assert.Null(oldNotification.RecencyLabel);
        Assert.False(oldNotification.IsRecentNotification);
    }

    [Fact]
    public void AccountNotificationsPageViewModel_ComputesVisibleCountsAndActiveFilters()
    {
        var vm = new AccountNotificationsPageViewModel
        {
            Query = "đơn hàng",
            Notifications =
            [
                new AccountNotificationListItemDto { NotificationType = "order_status", IsRead = false },
                new AccountNotificationListItemDto { NotificationType = "order_status", IsRead = true },
                new AccountNotificationListItemDto { NotificationType = "seller_review_update", IsRead = false }
            ]
        };

        Assert.True(vm.HasActiveFilters);
        Assert.Equal(2, vm.GetVisibleCount("order_status"));
        Assert.Equal(1, vm.GetVisibleCount("order_status", false));
        Assert.Equal(1, vm.GetVisibleCount("seller_review_update", false));
    }

    [Fact]
    public void AccountNotificationsPageViewModel_BuildsActiveFilterChipsAndResultSummary()
    {
        var vm = new AccountNotificationsPageViewModel
        {
            Query = "đơn hàng",
            Type = "order_status",
            IsRead = false,
            RecentOnly = true,
            TotalNotifications = 12,
            Notifications =
            [
                new AccountNotificationListItemDto
                {
                    NotificationType = "order_status",
                    IsRead = false,
                    CreatedAt = DateTime.UtcNow.AddHours(-2)
                },
                new AccountNotificationListItemDto
                {
                    NotificationType = "order_status",
                    IsRead = true,
                    CreatedAt = DateTime.UtcNow.AddDays(-3)
                }
            ]
        };

        Assert.Equal(4, vm.ActiveFilterChips.Count);
        Assert.Contains(vm.ActiveFilterChips, x => x.Label == "Cập nhật đơn hàng");
        Assert.Contains(vm.ActiveFilterChips, x => x.Label == "Chưa đọc");
        Assert.Contains(vm.ActiveFilterChips, x => x.Label == "Từ khóa: đơn hàng");
        Assert.Contains(vm.ActiveFilterChips, x => x.Label == "Mới trong 24h");
        Assert.Equal(1, vm.VisibleUnreadNotifications);
        Assert.Equal(1, vm.VisibleRecentNotifications);
        Assert.Equal("Hiển thị 2 thông báo trên trang này", vm.ResultsSummary);
    }

    [Fact]
    public void AccountNotificationsPageViewModel_BuildsRecentAndDisplayedSummaries_ForFilteredResults()
    {
        var vm = new AccountNotificationsPageViewModel
        {
            Query = "seller",
            RecentOnly = true,
            TotalNotifications = 12,
            Recent24hNotifications = 4,
            Notifications =
            [
                new AccountNotificationListItemDto
                {
                    NotificationType = "seller_review_update",
                    IsRead = false,
                    CreatedAt = DateTime.UtcNow.AddHours(-3)
                },
                new AccountNotificationListItemDto
                {
                    NotificationType = "seller_review_update",
                    IsRead = true,
                    CreatedAt = DateTime.UtcNow.AddHours(-6)
                }
            ]
        };

        Assert.Equal("Có 4 cập nhật mới trong 24h trên toàn bộ trung tâm thông báo.", vm.Recent24hSummary);
        Assert.Equal("Hiển thị 2 mục sau khi áp dụng bộ lọc hiện tại.", vm.DisplayedCountSummary);
    }

    [Fact]
    public void AccountNotificationsPageViewModel_BuildsRecentAndDisplayedSummaries_ForVisibleRecentState()
    {
        var vm = new AccountNotificationsPageViewModel
        {
            TotalNotifications = 11,
            Recent24hNotifications = 1,
            Notifications =
            [
                new AccountNotificationListItemDto
                {
                    NotificationType = "order_status",
                    IsRead = false,
                    CreatedAt = DateTime.UtcNow.AddMinutes(-5),
                    Title = "Verify recent notification 2026-03-28"
                }
            ]
        };

        Assert.Equal("Có 1 cập nhật mới trong 24h trên toàn bộ trung tâm thông báo.", vm.Recent24hSummary);
        Assert.Equal("Hiển thị 1 mục ở trang hiện tại.", vm.DisplayedCountSummary);
        Assert.Equal("1 chưa đọc", vm.NotificationSections.Single().HeroSummaryText);
    }

    [Fact]
    public void AccountNotificationsPageViewModel_BuildsRecentAndDisplayedSummaries_ForEmptyRecentState()
    {
        var vm = new AccountNotificationsPageViewModel
        {
            TotalNotifications = 10,
            Recent24hNotifications = 0
        };

        Assert.Equal("Chưa có cập nhật mới nào phát sinh trong 24h gần nhất.", vm.Recent24hSummary);
        Assert.Equal("Không còn thông báo nào khớp với bộ lọc hiện tại", vm.ResultsSummary);
        Assert.Equal("Không còn thông báo nào khớp với bộ lọc hiện tại.", vm.DisplayedCountSummary);
    }

    [Fact]
    public void AccountNotificationsPageViewModel_BuildsContextualEmptyState_WhenFiltersHideNotifications()
    {
        var vm = new AccountNotificationsPageViewModel
        {
            Query = "seller",
            TotalNotifications = 8,
            SellerApplication = new SellerApplicationSummaryDto
            {
                HasApplication = true,
                ReviewStatus = "pending"
            }
        };

        Assert.False(vm.HasNotifications);
        Assert.True(vm.HasAnyNotificationsOverall);
        Assert.True(vm.HasActiveFilters);
        Assert.Equal("Không có thông báo khớp bộ lọc", vm.EmptyStateTitle);
        Assert.Contains("nới bộ lọc", vm.EmptyStateDescription, System.StringComparison.OrdinalIgnoreCase);
        Assert.Equal("Xóa bộ lọc", vm.EmptyStatePrimaryActionLabel);
        Assert.Equal("/account/notifications", vm.EmptyStatePrimaryActionUrl);
        Assert.Equal("Không còn thông báo nào khớp với bộ lọc hiện tại", vm.ResultsSummary);
    }

    [Fact]
    public void AccountNotificationsPageViewModel_BuildsFirstTimeEmptyState_ForAccountsWithoutNotifications()
    {
        var vm = new AccountNotificationsPageViewModel
        {
            SellerApplication = new SellerApplicationSummaryDto
            {
                HasApplication = false
            }
        };

        Assert.False(vm.HasAnyNotificationsOverall);
        Assert.Equal("Bạn chưa có thông báo nào", vm.EmptyStateTitle);
        Assert.Contains("đơn hàng", vm.EmptyStateDescription, System.StringComparison.OrdinalIgnoreCase);
        Assert.Equal("Đăng ký người bán", vm.EmptyStatePrimaryActionLabel);
        Assert.Equal("/account/become-seller", vm.EmptyStatePrimaryActionUrl);
    }

    [Fact]
    public void AccountNotificationsPageViewModel_GroupsNotificationsByKnownTypeOrder()
    {
        var vm = new AccountNotificationsPageViewModel
        {
            Notifications =
            [
                new AccountNotificationListItemDto { NotificationType = "location_update", IsRead = false, Title = "shipping" },
                new AccountNotificationListItemDto { NotificationType = "order_status", IsRead = false, Title = "order" },
                new AccountNotificationListItemDto { NotificationType = "seller_review_update", IsRead = false, Title = "seller" }
            ]
        };

        Assert.Equal(3, vm.NotificationSections.Count);
        Assert.Equal("Cập nhật hồ sơ seller", vm.NotificationSections[0].Label);
        Assert.Equal("Cập nhật đơn hàng", vm.NotificationSections[1].Label);
        Assert.Equal("Cập nhật vị trí giao hàng", vm.NotificationSections[2].Label);
        Assert.Equal(1, vm.NotificationSections[0].UnreadCount);
        Assert.True(vm.NotificationSections[0].IsInitiallyExpanded);
        Assert.Equal("notification-section-1", vm.NotificationSections[0].SectionKey);
    }

    [Fact]
    public void AccountNotificationsPageViewModel_PrioritizesUnreadSectionsBeforeReadOnlySections()
    {
        var vm = new AccountNotificationsPageViewModel
        {
            Notifications =
            [
                new AccountNotificationListItemDto { NotificationType = "location_update", IsRead = true, Title = "shipping" },
                new AccountNotificationListItemDto { NotificationType = "order_status", IsRead = false, Title = "order" },
                new AccountNotificationListItemDto { NotificationType = "seller_review_update", IsRead = true, Title = "seller" }
            ]
        };

        Assert.Equal(3, vm.NotificationSections.Count);
        Assert.Equal("Cập nhật đơn hàng", vm.NotificationSections[0].Label);
        Assert.True(vm.NotificationSections[0].IsInitiallyExpanded);
        Assert.False(vm.NotificationSections[1].IsInitiallyExpanded);
        Assert.False(vm.NotificationSections[2].IsInitiallyExpanded);
    }

    [Fact]
    public void AccountNotificationsPageViewModel_PrioritizesUnreadItemsWithinSection()
    {
        var vm = new AccountNotificationsPageViewModel
        {
            Notifications =
            [
                new AccountNotificationListItemDto
                {
                    NotificationType = "order_status",
                    NotificationId = 10,
                    Title = "older-unread",
                    IsRead = false,
                    CreatedAt = new DateTime(2026, 3, 22, 8, 0, 0, DateTimeKind.Utc)
                },
                new AccountNotificationListItemDto
                {
                    NotificationType = "order_status",
                    NotificationId = 11,
                    Title = "read-newer",
                    IsRead = true,
                    CreatedAt = new DateTime(2026, 3, 23, 8, 0, 0, DateTimeKind.Utc)
                },
                new AccountNotificationListItemDto
                {
                    NotificationType = "order_status",
                    NotificationId = 12,
                    Title = "newest-unread",
                    IsRead = false,
                    CreatedAt = new DateTime(2026, 3, 23, 9, 0, 0, DateTimeKind.Utc)
                }
            ]
        };

        var items = vm.NotificationSections.Single().Notifications;
        Assert.Equal("newest-unread", items[0].Title);
        Assert.Equal("older-unread", items[1].Title);
        Assert.Equal("read-newer", items[2].Title);
    }

    [Fact]
    public void AccountNotificationSectionDto_BuildsSummaryAndLastUpdatedLabel()
    {
        var section = new AccountNotificationSectionDto
        {
            UnreadCount = 2,
            Notifications =
            [
                new AccountNotificationListItemDto
                {
                    CreatedAt = DateTime.UtcNow.AddMinutes(-5)
                },
                new AccountNotificationListItemDto
                {
                    CreatedAt = DateTime.UtcNow.AddHours(-5)
                }
            ]
        };

        Assert.Equal("2 chưa đọc, 2 cập nhật trong nhóm này", section.SummaryText);
        Assert.Equal("Vừa có cập nhật", section.LastUpdatedLabel);
    }

    [Fact]
    public void AccountNotificationSectionDto_ReportsWhenReadItemsExist()
    {
        var section = new AccountNotificationSectionDto
        {
            UnreadCount = 1,
            Notifications =
            [
                new AccountNotificationListItemDto { IsRead = false },
                new AccountNotificationListItemDto { IsRead = true }
            ]
        };

        Assert.True(section.HasReadItems);
    }

    [Fact]
    public void AccountNotificationSectionDto_ReportsWhenRecentItemsExist()
    {
        var section = new AccountNotificationSectionDto
        {
            Notifications =
            [
                new AccountNotificationListItemDto { CreatedAt = DateTime.UtcNow.AddDays(-2) },
                new AccountNotificationListItemDto { CreatedAt = DateTime.UtcNow.AddHours(-2) }
            ]
        };

        Assert.True(section.HasRecentItems);
    }

    [Fact]
    public void AccountNotificationSectionDto_BuildsHeroSummaryText_FromUnreadAndRecentState()
    {
        var unreadSection = new AccountNotificationSectionDto
        {
            UnreadCount = 2,
            Notifications =
            [
                new AccountNotificationListItemDto { IsRead = false, CreatedAt = DateTime.UtcNow.AddHours(-2) },
                new AccountNotificationListItemDto { IsRead = false, CreatedAt = DateTime.UtcNow.AddDays(-2) }
            ]
        };

        var recentSection = new AccountNotificationSectionDto
        {
            Notifications =
            [
                new AccountNotificationListItemDto { IsRead = true, CreatedAt = DateTime.UtcNow.AddHours(-3) }
            ]
        };

        var oldSection = new AccountNotificationSectionDto
        {
            Notifications =
            [
                new AccountNotificationListItemDto { IsRead = true, CreatedAt = DateTime.UtcNow.AddDays(-3) }
            ]
        };

        Assert.Equal("2 chưa đọc", unreadSection.HeroSummaryText);
        Assert.Equal("1 mới trong 24h", recentSection.HeroSummaryText);
        Assert.Equal("1 cập nhật", oldSection.HeroSummaryText);
    }

    [Fact]
    public void AccountNotificationsPageViewModel_SelectsPrioritySection_ByUnreadThenRecent()
    {
        var vm = new AccountNotificationsPageViewModel
        {
            Notifications =
            [
                new AccountNotificationListItemDto
                {
                    NotificationType = "location_update",
                    IsRead = false,
                    CreatedAt = DateTime.UtcNow.AddHours(-2)
                },
                new AccountNotificationListItemDto
                {
                    NotificationType = "location_update",
                    IsRead = false,
                    CreatedAt = DateTime.UtcNow.AddHours(-3)
                },
                new AccountNotificationListItemDto
                {
                    NotificationType = "order_status",
                    IsRead = false,
                    CreatedAt = DateTime.UtcNow.AddHours(-1)
                },
                new AccountNotificationListItemDto
                {
                    NotificationType = "order_status",
                    IsRead = true,
                    CreatedAt = DateTime.UtcNow.AddMinutes(-10)
                }
            ]
        };

        var section = vm.PrioritySection;
        Assert.NotNull(section);
        Assert.Equal("Cập nhật vị trí giao hàng", section!.Label);
        Assert.Contains("đang cần bạn xử lý trước", section.PriorityHeadline, System.StringComparison.OrdinalIgnoreCase);
        Assert.Contains("2 chưa đọc", section.PrioritySummary, System.StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void AccountNotificationNavSummaryDto_FromPage_BuildsUnreadAndRecentHighlights()
    {
        var vm = new AccountNotificationsPageViewModel
        {
            UnreadNotifications = 3,
            Recent24hNotifications = 4,
            Notifications =
            [
                new AccountNotificationListItemDto
                {
                    NotificationType = "seller_review_update",
                    IsRead = false,
                    CreatedAt = DateTime.UtcNow.AddHours(-2)
                },
                new AccountNotificationListItemDto
                {
                    NotificationType = "seller_review_update",
                    IsRead = false,
                    CreatedAt = DateTime.UtcNow.AddDays(-2)
                },
                new AccountNotificationListItemDto
                {
                    NotificationType = "order_status",
                    IsRead = true,
                    CreatedAt = DateTime.UtcNow.AddHours(-3)
                }
            ]
        };

        var summary = AccountNotificationNavSummaryDto.FromPage(vm);

        Assert.Equal(3, summary.UnreadCount);
        Assert.Equal(4, summary.RecentCount);
        Assert.Equal("/account/notifications?recentOnly=true", summary.RecentNotificationsUrl);
        Assert.Equal("/account/notifications?type=seller_review_update&focusType=seller_review_update", summary.PriorityNotificationsUrl);
        Assert.Equal("Cập nhật hồ sơ seller", summary.PriorityLabel);
        Assert.Contains("2 chưa đọc", summary.PrioritySummary, System.StringComparison.OrdinalIgnoreCase);
        Assert.Equal(2, summary.Sections.Count);
        Assert.Equal("Cập nhật hồ sơ seller", summary.Sections[0].Label);
        Assert.Equal("seller_review_update", summary.Sections[0].NotificationType);
        Assert.True(summary.Sections[0].HasUnread);
        Assert.Equal("2 chưa đọc", summary.Sections[0].HeroSummaryText);
        Assert.Equal("/account/notifications?type=seller_review_update&isRead=false&focusType=seller_review_update", summary.Sections[0].NavigateUrl);
        Assert.Equal("Cập nhật đơn hàng", summary.Sections[1].Label);
        Assert.False(summary.Sections[1].HasUnread);
        Assert.Equal("/account/notifications?type=order_status&recentOnly=true&focusType=order_status", summary.Sections[1].NavigateUrl);
    }

    [Fact]
    public void AccountNotificationNavSummaryDto_FromPage_UsesRecentLink_WhenSectionHasOnlyRecentItems()
    {
        var vm = new AccountNotificationsPageViewModel
        {
            Recent24hNotifications = 1,
            Notifications =
            [
                new AccountNotificationListItemDto
                {
                    NotificationType = "order_status",
                    IsRead = true,
                    CreatedAt = DateTime.UtcNow.AddMinutes(-10)
                }
            ]
        };

        var summary = AccountNotificationNavSummaryDto.FromPage(vm);

        Assert.Single(summary.Sections);
        Assert.False(summary.Sections[0].HasUnread);
        Assert.Equal("1 mới trong 24h", summary.Sections[0].HeroSummaryText);
        Assert.Equal("/account/notifications?type=order_status&recentOnly=true&focusType=order_status", summary.Sections[0].NavigateUrl);
    }

    [Fact]
    public void AccountNotificationListItemDto_ReturnsSellerApplicationAction_ForSellerReviewUpdate()
    {
        var dto = new AccountNotificationListItemDto
        {
            NotificationId = 35,
            NotificationType = "seller_review_update"
        };

        Assert.Equal("/account/become-seller", dto.DestinationUrl);
        Assert.Equal("/account/notifications/35/open?target=%2Faccount%2Fbecome-seller", dto.ActionUrl);
        Assert.Equal("Xem hồ sơ seller", dto.ActionLabel);
    }

    [Fact]
    public void AccountNotificationListItemDto_ReturnsOrderAction_ForOrderNotifications()
    {
        var dto = new AccountNotificationListItemDto
        {
            NotificationId = 41,
            NotificationType = "order_status",
            OrderId = 123
        };

        Assert.Equal("/account/orders/123", dto.DestinationUrl);
        Assert.Equal("/account/notifications/41/open?target=%2Faccount%2Forders%2F123", dto.ActionUrl);
        Assert.Equal("Xem đơn hàng", dto.ActionLabel);
    }
}
