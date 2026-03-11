using System.ComponentModel.DataAnnotations;

namespace FreshFarm.Catalog.Api.Dtos;

public sealed class FreshQualityRecallUpsertRequest
{
    [StringLength(50)]
    public string? RecallCode { get; set; }

    public int? ProductId { get; set; }

    public int? FreshInventoryLotId { get; set; }

    public int? SellerId { get; set; }

    [Required]
    [StringLength(50)]
    public string RecallType { get; set; } = "quality_issue";

    [Required]
    [StringLength(20)]
    public string Severity { get; set; } = "medium";

    [Required]
    [StringLength(40)]
    public string Status { get; set; } = "open";

    [Required]
    [StringLength(200)]
    public string Title { get; set; } = string.Empty;

    [StringLength(2000)]
    public string? Reason { get; set; }

    [StringLength(2000)]
    public string? ActionRequired { get; set; }

    public DateTime? StartedAt { get; set; }

    public DateTime? ResolvedAt { get; set; }
}
