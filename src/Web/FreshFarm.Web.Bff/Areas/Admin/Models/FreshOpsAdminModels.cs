using System.ComponentModel.DataAnnotations;

namespace FreshFarm.Web.Bff.Areas.Admin.Models;

public sealed class FreshOpsPageViewModel
{
    public string Query { get; set; } = string.Empty;

    public int? SellerId { get; set; }

    public string Status { get; set; } = "all";

    public FreshOpsStatsViewModel Stats { get; set; } = new();

    public List<FreshOpsOptionViewModel> SellerOptions { get; set; } = new();

    public List<FreshOpsOptionViewModel> StatusOptions { get; set; } = new();

    public List<FreshLotRowViewModel> Lots { get; set; } = new();

    public List<FreshRecallRowViewModel> Recalls { get; set; } = new();

    public List<FreshFefoRowViewModel> FefoQueue { get; set; } = new();

    public FreshLotEditorInput NewLot { get; set; } = new();

    public FreshRecallEditorInput NewRecall { get; set; } = new();
}

public sealed class FreshOpsStatsViewModel
{
    public int TotalLots { get; set; }

    public int ExpiringSoonLots { get; set; }

    public int ExpiredLots { get; set; }

    public int OpenRecalls { get; set; }

    public int TraceCoverage { get; set; }
}

public sealed class FreshOpsOptionViewModel
{
    public string Value { get; set; } = string.Empty;

    public string Text { get; set; } = string.Empty;
}

public sealed class FreshLotRowViewModel
{
    public int FreshInventoryLotId { get; set; }

    public int ProductId { get; set; }

    public string ProductName { get; set; } = string.Empty;

    public string Sku { get; set; } = string.Empty;

    public int SellerId { get; set; }

    public string LotCode { get; set; } = string.Empty;

    public string? TraceCode { get; set; }

    public string? FarmName { get; set; }

    public string? OriginRegion { get; set; }

    public DateTime? HarvestedAt { get; set; }

    public DateTime? PackedAt { get; set; }

    public DateTime ReceivedAt { get; set; }

    public DateTime? ExpiresAt { get; set; }

    public int InitialQuantity { get; set; }

    public int RemainingQuantity { get; set; }

    public decimal? UnitCost { get; set; }

    public string Status { get; set; } = string.Empty;

    public string QualityStatus { get; set; } = string.Empty;

    public string? Notes { get; set; }

    public int? DaysToExpiry { get; set; }
}

public sealed class FreshRecallRowViewModel
{
    public int FreshQualityRecallId { get; set; }

    public string RecallCode { get; set; } = string.Empty;

    public int? ProductId { get; set; }

    public string? ProductName { get; set; }

    public int? FreshInventoryLotId { get; set; }

    public string? LotCode { get; set; }

    public int? SellerId { get; set; }

    public string RecallType { get; set; } = string.Empty;

    public string Severity { get; set; } = string.Empty;

    public string Status { get; set; } = string.Empty;

    public string Title { get; set; } = string.Empty;

    public string? Reason { get; set; }

    public string? ActionRequired { get; set; }

    public DateTime StartedAt { get; set; }

    public DateTime? ResolvedAt { get; set; }
}

public sealed class FreshFefoRowViewModel
{
    public int FreshInventoryLotId { get; set; }

    public string ProductName { get; set; } = string.Empty;

    public string Sku { get; set; } = string.Empty;

    public int SellerId { get; set; }

    public string LotCode { get; set; } = string.Empty;

    public int RemainingQuantity { get; set; }

    public DateTime ExpiresAt { get; set; }

    public int DaysToExpiry { get; set; }

    public string QualityStatus { get; set; } = string.Empty;
}

public sealed class FreshLotEditorInput
{
    [Range(1, int.MaxValue)]
    public int ProductId { get; set; }

    [Range(1, int.MaxValue)]
    public int SellerId { get; set; }

    [Required]
    [StringLength(80)]
    public string LotCode { get; set; } = string.Empty;

    [StringLength(120)]
    public string? TraceCode { get; set; }

    [StringLength(150)]
    public string? FarmName { get; set; }

    [StringLength(150)]
    public string? OriginRegion { get; set; }

    public DateTime? HarvestedAt { get; set; }

    public DateTime? PackedAt { get; set; }

    public DateTime? ReceivedAt { get; set; }

    public DateTime? ExpiresAt { get; set; }

    [Range(1, 1000000)]
    public int InitialQuantity { get; set; }

    [Range(0, 1000000)]
    public int RemainingQuantity { get; set; }

    public decimal? UnitCost { get; set; }

    [Required]
    public string Status { get; set; } = "active";

    [Required]
    public string QualityStatus { get; set; } = "ok";

    public string? Notes { get; set; }
}

public sealed class FreshRecallEditorInput
{
    public string? RecallCode { get; set; }

    public int? ProductId { get; set; }

    public int? FreshInventoryLotId { get; set; }

    public int? SellerId { get; set; }

    [Required]
    public string RecallType { get; set; } = "quality_issue";

    [Required]
    public string Severity { get; set; } = "medium";

    [Required]
    public string Status { get; set; } = "open";

    [Required]
    public string Title { get; set; } = string.Empty;

    public string? Reason { get; set; }

    public string? ActionRequired { get; set; }

    public DateTime? StartedAt { get; set; }
}
