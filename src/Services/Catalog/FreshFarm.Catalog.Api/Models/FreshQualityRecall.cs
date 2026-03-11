using System;

namespace FreshFarm.Catalog.Api.Models;

public partial class FreshQualityRecall
{
    public int FreshQualityRecallId { get; set; }

    public string RecallCode { get; set; } = string.Empty;

    public int? ProductId { get; set; }

    public int? FreshInventoryLotId { get; set; }

    public int? SellerId { get; set; }

    public string RecallType { get; set; } = string.Empty;

    public string Severity { get; set; } = string.Empty;

    public string Status { get; set; } = string.Empty;

    public string Title { get; set; } = string.Empty;

    public string? Reason { get; set; }

    public string? ActionRequired { get; set; }

    public DateTime StartedAt { get; set; }

    public DateTime? ResolvedAt { get; set; }

    public DateTime CreatedAt { get; set; }

    public DateTime? UpdatedAt { get; set; }

    public virtual FreshInventoryLot? FreshInventoryLot { get; set; }

    public virtual Product? Product { get; set; }
}
