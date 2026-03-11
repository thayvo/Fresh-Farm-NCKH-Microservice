using System.ComponentModel.DataAnnotations;

namespace FreshFarm.Catalog.Api.Dtos;

public sealed class CategoryAttributeUpsertRequest
{
    [Range(1, int.MaxValue)]
    public int CategoryId { get; set; }

    [Required]
    [StringLength(80)]
    public string AttributeKey { get; set; } = string.Empty;

    [Required]
    [StringLength(120)]
    public string DisplayName { get; set; } = string.Empty;

    [Required]
    [StringLength(30)]
    public string InputType { get; set; } = "text";

    public bool IsRequired { get; set; }

    public bool IsFacet { get; set; }

    [Range(0, 999)]
    public int SortOrder { get; set; }

    [StringLength(255)]
    public string? Placeholder { get; set; }

    public bool IsActive { get; set; } = true;
}
