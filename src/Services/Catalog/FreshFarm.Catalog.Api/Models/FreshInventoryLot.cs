using System;

namespace FreshFarm.Catalog.Api.Models;

public partial class FreshInventoryLot
{
    public int FreshInventoryLotId { get; set; }

    public int ProductId { get; set; }

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

    public DateTime CreatedAt { get; set; }

    public DateTime? UpdatedAt { get; set; }

    public virtual Product Product { get; set; } = null!;
}
