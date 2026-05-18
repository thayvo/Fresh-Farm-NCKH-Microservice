using System.ComponentModel.DataAnnotations;

namespace FreshFarm.Catalog.Api.Dtos;

public sealed class ProductUpsertRequest
{
    [Range(1, int.MaxValue)]
    public int CategoryId { get; set; }

    [Range(1, int.MaxValue)]
    public int UnitId { get; set; }

    [Required]
    [StringLength(255)]
    public string ProductName { get; set; } = string.Empty;

    [Range(typeof(decimal), "0", "79228162514264337593543950335")]
    public decimal Price { get; set; }

    [StringLength(50)]
    public string? Sku { get; set; }

    [StringLength(300)]
    public string? ShortDescription { get; set; }

    [StringLength(4000)]
    public string? LongDescription { get; set; }

    [StringLength(255)]
    public string? ImageFileName { get; set; }

    public bool Status { get; set; } = true;
}
