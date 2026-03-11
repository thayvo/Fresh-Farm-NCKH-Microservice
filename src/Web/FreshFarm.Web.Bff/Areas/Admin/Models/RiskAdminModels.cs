namespace FreshFarm.Web.Bff.Areas.Admin.Models;

public sealed class RiskCenterPageViewModel
{
    public string Query { get; set; } = string.Empty;

    public string Type { get; set; } = "all";

    public string Status { get; set; } = "all";

    public string Severity { get; set; } = "all";

    public int? SelectedCaseId { get; set; }

    public RiskOverviewViewModel Overview { get; set; } = new();

    public List<RiskOptionViewModel> TypeOptions { get; set; } = new();

    public List<RiskOptionViewModel> StatusOptions { get; set; } = new();

    public List<RiskOptionViewModel> SeverityOptions { get; set; } = new();

    public List<RiskCaseRowViewModel> Rows { get; set; } = new();

    public RiskCaseDetailViewModel? SelectedCase { get; set; }

    public RiskDecisionInputModel DecisionEditor { get; set; } = new();
}

public sealed class RiskOverviewViewModel
{
    public int TotalCases { get; set; }

    public int OpenCases { get; set; }

    public int ReviewCases { get; set; }

    public int EscalatedCases { get; set; }

    public int HighSeverityCases { get; set; }

    public int VoucherAbuseCases { get; set; }

    public int ReturnSpikeCases { get; set; }

    public int DecisionsLast7d { get; set; }

    public List<RiskRecentCaseViewModel> Recent { get; set; } = new();
}

public sealed class RiskRecentCaseViewModel
{
    public int RiskCaseId { get; set; }

    public string CaseType { get; set; } = string.Empty;

    public string Title { get; set; } = string.Empty;

    public string Status { get; set; } = string.Empty;

    public string Severity { get; set; } = string.Empty;

    public int SignalCount { get; set; }

    public DateTime? UpdatedAt { get; set; }

    public DateTime? LastSignalAt { get; set; }
}

public sealed class RiskOptionViewModel
{
    public string Value { get; set; } = string.Empty;

    public string Text { get; set; } = string.Empty;
}

public sealed class RiskCaseRowViewModel
{
    public int RiskCaseId { get; set; }

    public string CaseType { get; set; } = string.Empty;

    public string Title { get; set; } = string.Empty;

    public string? Summary { get; set; }

    public string Status { get; set; } = string.Empty;

    public string Severity { get; set; } = string.Empty;

    public int? SellerId { get; set; }

    public int? BuyerId { get; set; }

    public int? OrderId { get; set; }

    public int? CampaignId { get; set; }

    public int? VoucherCouponId { get; set; }

    public int SignalCount { get; set; }

    public bool IsEscalated { get; set; }

    public DateTime CreatedAt { get; set; }

    public DateTime? UpdatedAt { get; set; }

    public DateTime? LastSignalAt { get; set; }
}

public sealed class RiskCaseDetailViewModel
{
    public int RiskCaseId { get; set; }

    public string ReferenceKey { get; set; } = string.Empty;

    public string CaseType { get; set; } = string.Empty;

    public string Title { get; set; } = string.Empty;

    public string? Summary { get; set; }

    public string Status { get; set; } = string.Empty;

    public string Severity { get; set; } = string.Empty;

    public int? SellerId { get; set; }

    public int? BuyerId { get; set; }

    public int? OrderId { get; set; }

    public int? CampaignId { get; set; }

    public int? VoucherCouponId { get; set; }

    public string? VoucherCode { get; set; }

    public int SignalCount { get; set; }

    public bool IsEscalated { get; set; }

    public DateTime CreatedAt { get; set; }

    public DateTime? UpdatedAt { get; set; }

    public DateTime? LastSignalAt { get; set; }

    public DateTime? ReviewedAt { get; set; }

    public int? ReviewedBy { get; set; }

    public List<RiskSignalViewModel> Signals { get; set; } = new();

    public List<RiskDecisionHistoryViewModel> Decisions { get; set; } = new();

    public List<VoucherAbuseDetailViewModel> VoucherAbuseCases { get; set; } = new();

    public List<RiskOptionViewModel> DecisionOptions { get; set; } = new();
}

public sealed class RiskSignalViewModel
{
    public int RiskSignalId { get; set; }

    public string SignalType { get; set; } = string.Empty;

    public string SignalCode { get; set; } = string.Empty;

    public string Severity { get; set; } = string.Empty;

    public string Source { get; set; } = string.Empty;

    public decimal Score { get; set; }

    public string? MetadataJson { get; set; }

    public DateTime TriggeredAt { get; set; }
}

public sealed class RiskDecisionHistoryViewModel
{
    public int RiskDecisionId { get; set; }

    public string DecisionType { get; set; } = string.Empty;

    public string? Notes { get; set; }

    public int? CreatedBy { get; set; }

    public DateTime CreatedAt { get; set; }
}

public sealed class VoucherAbuseDetailViewModel
{
    public int VoucherAbuseCaseId { get; set; }

    public int CouponId { get; set; }

    public int? BuyerId { get; set; }

    public int? SellerId { get; set; }

    public int? CampaignId { get; set; }

    public int? OrderId { get; set; }

    public string AbuseType { get; set; } = string.Empty;

    public decimal? SuspectedBenefitAmount { get; set; }

    public string Status { get; set; } = string.Empty;

    public DateTime CreatedAt { get; set; }

    public DateTime? ReviewedAt { get; set; }
}

public sealed class RiskDecisionInputModel
{
    public int RiskCaseId { get; set; }

    public string DecisionType { get; set; } = "monitor";

    public string? Notes { get; set; }
}
