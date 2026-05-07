namespace FreshFarm.Web.Bff.Areas.Admin.Models;

public sealed class FinanceConsolePageViewModel
{
    public string Section { get; set; } = "payouts";

    public string Query { get; set; } = string.Empty;

    public string Status { get; set; } = "all";

    public string FollowUpBucket { get; set; } = "all";

    public int? SellerId { get; set; }

    public int Page { get; set; } = 1;

    public int PageSize { get; set; } = 12;

    public int Total { get; set; }

    public int TotalPages { get; set; } = 1;

    public string Scope { get; set; } = string.Empty;

    public bool ShowSellerFilter { get; set; }

    public FinanceConsoleStatsViewModel Stats { get; set; } = new();

    public FinanceSectionCountsViewModel SectionCounts { get; set; } = new();

    public List<FinanceOptionViewModel> SellerOptions { get; set; } = new();

    public List<FinanceOptionViewModel> StatusOptions { get; set; } = new();

    public List<FinanceConsoleRowViewModel> Rows { get; set; } = new();

    public List<FinanceOwnerSummaryViewModel> OwnerSummary { get; set; } = new();

    public string BulkActionName { get; set; } = string.Empty;
}

public sealed class FinanceConsoleStatsViewModel
{
    public decimal GrossMerchandiseValue { get; set; }

    public decimal CapturedPayments { get; set; }

    public decimal PlatformCommission { get; set; }

    public decimal SellerEarning { get; set; }

    public decimal PendingPayoutAmount { get; set; }

    public decimal RefundedAmount { get; set; }

    public int OpenReturns { get; set; }

    public int OpenRefunds { get; set; }

    public int SellerCount { get; set; }

    public int OverdueFollowUps { get; set; }

    public int DueSoonFollowUps { get; set; }

    public int NoFollowUpCount { get; set; }
}

public sealed class FinanceSectionCountsViewModel
{
    public int Payouts { get; set; }

    public int Refunds { get; set; }

    public int Returns { get; set; }
}

public sealed class FinanceOptionViewModel
{
    public string Value { get; set; } = string.Empty;

    public string Text { get; set; } = string.Empty;
}

public sealed class FinanceOwnerSummaryViewModel
{
    public string OwnerLabel { get; set; } = string.Empty;

    public int ItemCount { get; set; }

    public int OverdueCount { get; set; }

    public int DueSoonCount { get; set; }

    public int NoFollowUpCount { get; set; }
}

public sealed class FinanceConsoleRowViewModel
{
    public int RecordId { get; set; }

    public int? OrderId { get; set; }

    public int? SellerId { get; set; }

    public string SellerLabel { get; set; } = string.Empty;

    public int? OrderCount { get; set; }

    public decimal? AmountGross { get; set; }

    public decimal? FeeAmount { get; set; }

    public decimal? AmountNet { get; set; }

    public decimal? RefundAmount { get; set; }

    public string Status { get; set; } = string.Empty;

    public string Method { get; set; } = string.Empty;

    public string Provider { get; set; } = string.Empty;

    public string ReferenceCode { get; set; } = string.Empty;

    public string ReasonCode { get; set; } = string.Empty;

    public string Resolution { get; set; } = string.Empty;

    public string ItemName { get; set; } = string.Empty;

    public DateTime? CreatedAt { get; set; }

    public DateTime? ScheduledAt { get; set; }

    public DateTime? ProcessedAt { get; set; }

    public DateTime? PaidAt { get; set; }

    public string ReconciliationStatus { get; set; } = string.Empty;

    public string NextStep { get; set; } = string.Empty;

    public string AssignedOwner { get; set; } = string.Empty;

    public DateTime? ExportedAt { get; set; }

    public DateTime? FollowUpAt { get; set; }

    public string FollowUpNote { get; set; } = string.Empty;

    public DateTime? ReminderSentAt { get; set; }

    public string ReminderNote { get; set; } = string.Empty;

    public string PriorityKey { get; set; } = string.Empty;

    public string PriorityLabel { get; set; } = string.Empty;

    public string PriorityReason { get; set; } = string.Empty;

    public string LastActionSummary { get; set; } = string.Empty;
}

internal sealed class FinanceConsoleApiResponse
{
    public string? Scope { get; set; }

    public string? Section { get; set; }

    public int Page { get; set; }

    public int PageSize { get; set; }

    public int Total { get; set; }

    public int TotalPages { get; set; }

    public FinanceConsoleStatsApiDto? Stats { get; set; }

    public FinanceSectionCountsApiDto? SectionCounts { get; set; }

    public FinanceFiltersApiDto? Filters { get; set; }

    public List<FinanceConsoleRowApiDto>? Rows { get; set; }

    public List<FinanceOwnerSummaryApiDto>? OwnerSummary { get; set; }
}

internal sealed class FinanceConsoleStatsApiDto
{
    public decimal GrossMerchandiseValue { get; set; }

    public decimal CapturedPayments { get; set; }

    public decimal PlatformCommission { get; set; }

    public decimal SellerEarning { get; set; }

    public decimal PendingPayoutAmount { get; set; }

    public decimal RefundedAmount { get; set; }

    public int OpenReturns { get; set; }

    public int OpenRefunds { get; set; }

    public int SellerCount { get; set; }

    public int OverdueFollowUps { get; set; }

    public int DueSoonFollowUps { get; set; }

    public int NoFollowUpCount { get; set; }
}

internal sealed class FinanceSectionCountsApiDto
{
    public int Payouts { get; set; }

    public int Refunds { get; set; }

    public int Returns { get; set; }
}

internal sealed class FinanceFiltersApiDto
{
    public string? Q { get; set; }

    public string? Status { get; set; }

    public string? FollowUpBucket { get; set; }

    public int? SellerId { get; set; }

    public List<FinanceOptionApiDto>? SellerOptions { get; set; }

    public List<FinanceOptionApiDto>? StatusOptions { get; set; }
}

internal sealed class FinanceOptionApiDto
{
    public string? Value { get; set; }

    public string? Text { get; set; }
}

internal sealed class FinanceOwnerSummaryApiDto
{
    public string? OwnerLabel { get; set; }

    public int ItemCount { get; set; }

    public int OverdueCount { get; set; }

    public int DueSoonCount { get; set; }

    public int NoFollowUpCount { get; set; }
}

internal sealed class FinanceConsoleRowApiDto
{
    public int RecordId { get; set; }

    public int? OrderId { get; set; }

    public int? SellerId { get; set; }

    public string? SellerLabel { get; set; }

    public int? OrderCount { get; set; }

    public decimal? AmountGross { get; set; }

    public decimal? FeeAmount { get; set; }

    public decimal? AmountNet { get; set; }

    public decimal? RefundAmount { get; set; }

    public string? Status { get; set; }

    public string? Method { get; set; }

    public string? Provider { get; set; }

    public string? ReferenceCode { get; set; }

    public string? ReasonCode { get; set; }

    public string? Resolution { get; set; }

    public string? ItemName { get; set; }

    public DateTime? CreatedAt { get; set; }

    public DateTime? ScheduledAt { get; set; }

    public DateTime? ProcessedAt { get; set; }

    public DateTime? PaidAt { get; set; }

    public string? ReconciliationStatus { get; set; }

    public string? NextStep { get; set; }

    public string? AssignedOwner { get; set; }

    public DateTime? ExportedAt { get; set; }

    public DateTime? FollowUpAt { get; set; }

    public string? FollowUpNote { get; set; }

    public DateTime? ReminderSentAt { get; set; }

    public string? ReminderNote { get; set; }

    public string? PriorityKey { get; set; }

    public string? PriorityLabel { get; set; }

    public string? PriorityReason { get; set; }

    public string? LastActionSummary { get; set; }
}

