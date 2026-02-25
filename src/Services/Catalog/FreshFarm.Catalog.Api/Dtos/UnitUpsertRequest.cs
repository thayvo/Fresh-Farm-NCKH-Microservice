using System.ComponentModel.DataAnnotations;

namespace FreshFarm.Catalog.Api.Dtos;

public sealed class UnitUpsertRequest
{
    [Required]
    [StringLength(100)]
    public string UnitName { get; set; } = string.Empty;

    [Required]
    [StringLength(20)]
    public string Symbol { get; set; } = string.Empty;

    [StringLength(500)]
    public string? Description { get; set; }

    public bool IsActive { get; set; } = true;
}
