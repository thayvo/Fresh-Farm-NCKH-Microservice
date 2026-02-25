using System.ComponentModel.DataAnnotations;

namespace FreshFarm.Catalog.Api.Dtos;

public sealed class CategoryUpsertRequest
{
    [Required]
    [StringLength(100)]
    public string CategoryName { get; set; } = string.Empty;

    [StringLength(500)]
    public string? Description { get; set; }

    [StringLength(255)]
    public string? ImageCategoriesName { get; set; }

    public bool IsActive { get; set; } = true;

    [StringLength(255)]
    public string? Slug { get; set; }
}
