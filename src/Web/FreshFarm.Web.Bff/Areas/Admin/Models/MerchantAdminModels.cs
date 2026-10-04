namespace FreshFarm.Web.Bff.Areas.Admin.Models;

public sealed class MerchantManagementPageViewModel
{
    public string Query { get; set; } = string.Empty;

    public string Status { get; set; } = "all";

    public string Queue { get; set; } = "all";

    public string ReviewStatus { get; set; } = "all";

    public string ReviewWindow { get; set; } = "all";

    public int Page { get; set; } = 1;

    public int PageSize { get; set; } = 20;

    public int Total { get; set; }

    public int TotalPages { get; set; } = 1;

    public int? SelectedSellerId { get; set; }

    public MerchantStatsViewModel Stats { get; set; } = new();

    public List<MerchantOptionViewModel> StatusOptions { get; set; } = new();

    public List<MerchantOptionViewModel> QueueOptions { get; set; } = new();

    public List<MerchantOptionViewModel> ReviewStatusOptions { get; set; } = new();

    public List<MerchantOptionViewModel> ReviewWindowOptions { get; set; } = new();

    public List<MerchantListItemViewModel> Merchants { get; set; } = new();

    public MerchantDetailViewModel? Details { get; set; }

    public List<string> RejectReasonTemplates { get; set; } = new();
}

public sealed class MerchantStatsViewModel
{
    public int TotalSellers { get; set; }

    public int ActiveSellers { get; set; }

    public int SuspendedSellers { get; set; }

    public int ReviewNeeded { get; set; }

    public int MissingAddress { get; set; }

    public int ApprovalQueue { get; set; }

    public int ProfileFixQueue { get; set; }

    public int DormantQueue { get; set; }
}

public sealed class MerchantOptionViewModel
{
    public string Value { get; set; } = string.Empty;

    public string Text { get; set; } = string.Empty;
}

public class MerchantListItemViewModel
{
    public int SellerId { get; set; }

    public bool IsSellerApproved { get; set; }

    public string ReviewStatus { get; set; } = "not_applied";

    public string ReviewStatusLabel { get; set; } = "Chưa có hồ sơ";

    public string? ReviewNote { get; set; }

    public DateTime? ReviewedAt { get; set; }

    public string ShopName { get; set; } = string.Empty;

    public string UserName { get; set; } = string.Empty;

    public string FullName { get; set; } = string.Empty;

    public string Email { get; set; } = string.Empty;

    public string? Phone { get; set; }

    public string? StoreName { get; set; }

    public string? StoreAddress { get; set; }

    public string? StoreEmail { get; set; }

    public string? StorePhone { get; set; }

    public string? Avatar { get; set; }

    public bool IsActive { get; set; }

    public DateTime CreatedAt { get; set; }

    public DateTime? UpdatedAt { get; set; }

    public DateTime? LastLogin { get; set; }

    public DateTime? ApplicationSubmittedAt { get; set; }

    public DateTime? ApplicationUpdatedAt { get; set; }

    public string AddressSummary { get; set; } = string.Empty;

    public int ProfileScore { get; set; }

    public string ComplianceStatus { get; set; } = string.Empty;

    public List<MerchantFlagViewModel> Flags { get; set; } = new();

    public int? DaysSinceLastLogin { get; set; }

    public string QueueBucket { get; set; } = string.Empty;

    public string RecommendedAction { get; set; } = string.Empty;

    public int IssueCount { get; set; }
}

public class MerchantDetailViewModel : MerchantListItemViewModel
{
    public string? AddressDetail { get; set; }

    public string? Province { get; set; }

    public string? District { get; set; }

    public string? Ward { get; set; }

    public string? LegalFullName { get; set; }

    public string? IdentityNumberMasked { get; set; }

    public DateTime? IdentityIssuedDate { get; set; }

    public string? IdentityIssuedPlace { get; set; }

    public string? TaxCode { get; set; }

    public string? BusinessLicenseNumber { get; set; }

    public string? CitizenIdFrontUrl { get; set; }

    public string? CitizenIdBackUrl { get; set; }

    public string? BusinessLicenseUrl { get; set; }

    public string? AdditionalDocumentUrl { get; set; }

    public string? KycNotes { get; set; }

    public string ComplianceSummary { get; set; } = string.Empty;

    public List<string> NextSteps { get; set; } = new();

    public string RejectReasonDraft { get; set; } = string.Empty;

    public List<MerchantReviewHistoryViewModel> ReviewHistory { get; set; } = new();
}

public sealed class MerchantFlagViewModel
{
    public string Code { get; set; } = string.Empty;

    public string Label { get; set; } = string.Empty;

    public string Tone { get; set; } = string.Empty;
}

public sealed class MerchantReviewHistoryViewModel
{
    public string Action { get; set; } = string.Empty;

    public string ReviewStatus { get; set; } = string.Empty;

    public string ReviewStatusLabel { get; set; } = string.Empty;

    public string? Note { get; set; }

    public DateTime ReviewedAt { get; set; }

    public int? ReviewedByUserId { get; set; }

    public string? ReviewerUserName { get; set; }

    public string? ReviewerFullName { get; set; }
}

