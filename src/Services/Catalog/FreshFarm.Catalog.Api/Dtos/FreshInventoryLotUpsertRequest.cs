using System.ComponentModel.DataAnnotations;

namespace FreshFarm.Catalog.Api.Dtos;

public sealed class FreshInventoryLotUpsertRequest
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

    [Range(typeof(decimal), "0", "999999999")]
    public decimal? UnitCost { get; set; }

    [Required]
    [StringLength(40)]
    public string Status { get; set; } = "active";

    [Required]
    [StringLength(40)]
    public string QualityStatus { get; set; } = "ok";

    [StringLength(2000)]
    public string? Notes { get; set; }
}
