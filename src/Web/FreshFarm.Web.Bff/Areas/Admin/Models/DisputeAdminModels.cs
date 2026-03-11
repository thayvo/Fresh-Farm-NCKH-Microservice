namespace FreshFarm.Web.Bff.Areas.Admin.Models;

public sealed class DisputeCenterPageViewModel
{
    public string Query { get; set; } = string.Empty;
    public string Section { get; set; } = "all";
    public string Status { get; set; } = "all";
    public int? SellerId { get; set; }
    public int Page { get; set; } = 1;
    public int PageSize { get; set; } = 12;
    public int Total { get; set; }
    public int TotalPages { get; set; } = 1;
    public string SelectedCaseType { get; set; } = string.Empty;
    public int? SelectedCaseId { get; set; }
    public DisputeCenterStatsViewModel Stats { get; set; } = new();
    public List<DisputeOptionViewModel> SectionOptions { get; set; } = new();
    public List<DisputeOptionViewModel> StatusOptions { get; set; } = new();
    public List<DisputeOptionViewModel> SellerOptions { get; set; } = new();
    public List<DisputeQueueRowViewModel> Rows { get; set; } = new();
    public DisputeCaseDetailsViewModel? Details { get; set; }
}

public sealed class DisputeCenterStatsViewModel
{
    public int TotalCases { get; set; }
    public int OpenCases { get; set; }
    public int PendingCases { get; set; }
    public int ResolvedCases { get; set; }
    public int BreachedCases { get; set; }
    public int SupportCases { get; set; }
    public int ReturnCases { get; set; }
    public int RefundCases { get; set; }
}

public sealed class DisputeOptionViewModel
{
    public string Value { get; set; } = string.Empty;
    public string Text { get; set; } = string.Empty;
}

public sealed class DisputeQueueRowViewModel
{
    public string CaseType { get; set; } = string.Empty;
    public int CaseId { get; set; }
    public string Title { get; set; } = string.Empty;
    public string Summary { get; set; } = string.Empty;
    public string DisplayStatus { get; set; } = string.Empty;
    public string QueueStatus { get; set; } = string.Empty;
    public int? SellerId { get; set; }
    public string SellerLabel { get; set; } = string.Empty;
    public int BuyerId { get; set; }
    public string BuyerName { get; set; } = string.Empty;
    public string BuyerPhone { get; set; } = string.Empty;
    public int? OrderId { get; set; }
    public decimal? Amount { get; set; }
    public int UnreadCount { get; set; }
    public bool IsSlaBreached { get; set; }
    public DateTime CreatedAt { get; set; }
    public DateTime? UpdatedAt { get; set; }
}

public sealed class DisputeCaseDetailsViewModel
{
    public string CaseType { get; set; } = string.Empty;
    public int CaseId { get; set; }
    public string Title { get; set; } = string.Empty;
    public string Summary { get; set; } = string.Empty;
    public string DisplayStatus { get; set; } = string.Empty;
    public string QueueStatus { get; set; } = string.Empty;
    public bool IsSlaBreached { get; set; }
    public DateTime? CreatedAt { get; set; }
    public DateTime? UpdatedAt { get; set; }
    public DisputePersonViewModel? Buyer { get; set; }
    public DisputeSellerViewModel? Seller { get; set; }
    public DisputeOrderViewModel? Order { get; set; }
    public DisputeAfterSalesViewModel? AfterSales { get; set; }
    public List<DisputeTimelineItemViewModel> Timeline { get; set; } = new();
    public List<DisputeMessageViewModel> Messages { get; set; } = new();
    public List<DisputeOrderViewModel> RelatedOrders { get; set; } = new();
    public List<DisputeRelatedCaseViewModel> RelatedCases { get; set; } = new();
}

public sealed class DisputePersonViewModel
{
    public int UserId { get; set; }
    public string FullName { get; set; } = string.Empty;
    public string Phone { get; set; } = string.Empty;
    public string Email { get; set; } = string.Empty;
}

public sealed class DisputeSellerViewModel
{
    public int? SellerId { get; set; }
    public string SellerLabel { get; set; } = string.Empty;
}

public sealed class DisputeOrderViewModel
{
    public int OrderId { get; set; }
    public decimal TotalAmount { get; set; }
    public string Status { get; set; } = string.Empty;
    public DateTime? OrderDate { get; set; }
}

public sealed class DisputeAfterSalesViewModel
{
    public string ReasonCode { get; set; } = string.Empty;
    public string Resolution { get; set; } = string.Empty;
    public decimal? RefundAmount { get; set; }
    public decimal? Amount { get; set; }
    public string Channel { get; set; } = string.Empty;
    public string Method { get; set; } = string.Empty;
    public string PaymentStatus { get; set; } = string.Empty;
    public string ReferenceCode { get; set; } = string.Empty;
}

public sealed class DisputeTimelineItemViewModel
{
    public string Label { get; set; } = string.Empty;
    public DateTime? Value { get; set; }
    public string Tone { get; set; } = string.Empty;
}

public sealed class DisputeMessageViewModel
{
    public string Sender { get; set; } = string.Empty;
    public string Content { get; set; } = string.Empty;
    public DateTime? CreatedAt { get; set; }
    public bool IsRead { get; set; }
}

public sealed class DisputeRelatedCaseViewModel
{
    public string CaseType { get; set; } = string.Empty;
    public int CaseId { get; set; }
    public string Title { get; set; } = string.Empty;
    public string Status { get; set; } = string.Empty;
    public decimal? Amount { get; set; }
    public DateTime? CreatedAt { get; set; }
}

internal sealed class DisputeQueueApiResponse
{
    public int Page { get; set; }
    public int PageSize { get; set; }
    public int Total { get; set; }
    public int TotalPages { get; set; }
    public DisputeStatsApiDto? Stats { get; set; }
    public DisputeFiltersApiDto? Filters { get; set; }
    public List<DisputeQueueRowApiDto>? Rows { get; set; }
}

internal sealed class DisputeStatsApiDto
{
    public int TotalCases { get; set; }
    public int OpenCases { get; set; }
    public int PendingCases { get; set; }
    public int ResolvedCases { get; set; }
    public int BreachedCases { get; set; }
    public int SupportCases { get; set; }
    public int ReturnCases { get; set; }
    public int RefundCases { get; set; }
}

internal sealed class DisputeFiltersApiDto
{
    public string? Q { get; set; }
    public string? Section { get; set; }
    public string? Status { get; set; }
    public int? SellerId { get; set; }
    public List<DisputeOptionApiDto>? SellerOptions { get; set; }
    public List<DisputeOptionApiDto>? StatusOptions { get; set; }
    public List<DisputeOptionApiDto>? SectionOptions { get; set; }
}

internal sealed class DisputeOptionApiDto
{
    public string? Value { get; set; }
    public string? Text { get; set; }
}

internal sealed class DisputeQueueRowApiDto
{
    public string? CaseType { get; set; }
    public int CaseId { get; set; }
    public string? Title { get; set; }
    public string? Summary { get; set; }
    public string? DisplayStatus { get; set; }
    public string? QueueStatus { get; set; }
    public int? SellerId { get; set; }
    public string? SellerLabel { get; set; }
    public int BuyerId { get; set; }
    public string? BuyerName { get; set; }
    public string? BuyerPhone { get; set; }
    public int? OrderId { get; set; }
    public decimal? Amount { get; set; }
    public int UnreadCount { get; set; }
    public bool IsSlaBreached { get; set; }
    public DateTime CreatedAt { get; set; }
    public DateTime? UpdatedAt { get; set; }
}

internal sealed class DisputeDetailsApiDto
{
    public string? CaseType { get; set; }
    public int CaseId { get; set; }
    public string? Title { get; set; }
    public string? Summary { get; set; }
    public string? DisplayStatus { get; set; }
    public string? QueueStatus { get; set; }
    public bool IsSlaBreached { get; set; }
    public DateTime? CreatedAt { get; set; }
    public DateTime? UpdatedAt { get; set; }
    public DisputePersonApiDto? Buyer { get; set; }
    public DisputeSellerApiDto? Seller { get; set; }
    public DisputeOrderApiDto? Order { get; set; }
    public DisputeAfterSalesApiDto? AfterSales { get; set; }
    public List<DisputeTimelineApiDto>? Timeline { get; set; }
    public List<DisputeMessageApiDto>? Messages { get; set; }
    public List<DisputeOrderApiDto>? RelatedOrders { get; set; }
    public List<DisputeRelatedCaseApiDto>? RelatedCases { get; set; }
}

internal sealed class DisputePersonApiDto
{
    public int UserId { get; set; }
    public string? FullName { get; set; }
    public string? Phone { get; set; }
    public string? Email { get; set; }
}

internal sealed class DisputeSellerApiDto
{
    public int? SellerId { get; set; }
    public string? SellerLabel { get; set; }
}

internal sealed class DisputeOrderApiDto
{
    public int OrderId { get; set; }
    public decimal TotalAmount { get; set; }
    public string? Status { get; set; }
    public DateTime? OrderDate { get; set; }
}

internal sealed class DisputeAfterSalesApiDto
{
    public string? ReasonCode { get; set; }
    public string? Resolution { get; set; }
    public decimal? RefundAmount { get; set; }
    public decimal? Amount { get; set; }
    public string? Channel { get; set; }
    public string? Method { get; set; }
    public string? PaymentStatus { get; set; }
    public string? ReferenceCode { get; set; }
}

internal sealed class DisputeTimelineApiDto
{
    public string? Label { get; set; }
    public DateTime? Value { get; set; }
    public string? Tone { get; set; }
}

internal sealed class DisputeMessageApiDto
{
    public string? Sender { get; set; }
    public string? Content { get; set; }
    public DateTime? CreatedAt { get; set; }
    public bool IsRead { get; set; }
}

internal sealed class DisputeRelatedCaseApiDto
{
    public string? CaseType { get; set; }
    public int CaseId { get; set; }
    public string? Title { get; set; }
    public string? Status { get; set; }
    public decimal? Amount { get; set; }
    public DateTime? CreatedAt { get; set; }
}
