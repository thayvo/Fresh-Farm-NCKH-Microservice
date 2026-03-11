using System.ComponentModel.DataAnnotations;

namespace FreshFarm.Web.Bff.Areas.Admin.Models;

public sealed class CatalogReadinessPageViewModel
{
    public string Query { get; set; } = string.Empty;

    public int? CategoryId { get; set; }

    public string State { get; set; } = "all";

    public CatalogReadinessStatsViewModel Stats { get; set; } = new();

    public List<CatalogReadinessOptionViewModel> CategoryOptions { get; set; } = new();

    public List<CatalogReadinessOptionViewModel> StateOptions { get; set; } = new();

    public List<CatalogAttributeRowViewModel> Attributes { get; set; } = new();

    public List<CatalogProductReadinessRowViewModel> ReadinessRows { get; set; } = new();

    public CatalogAttributeEditorInput NewAttribute { get; set; } = new();
}

public sealed class CatalogReadinessStatsViewModel
{
    public int TotalCategories { get; set; }

    public int TotalAttributes { get; set; }

    public int RequiredAttributes { get; set; }

    public int ProductsReady { get; set; }

    public int ProductsMissing { get; set; }

    public int FacetAttributes { get; set; }
}

public sealed class CatalogReadinessOptionViewModel
{
    public string Value { get; set; } = string.Empty;

    public string Text { get; set; } = string.Empty;

    public bool IsActive { get; set; } = true;
}

public sealed class CatalogAttributeRowViewModel
{
    public int CategoryAttributeId { get; set; }

    public int CategoryId { get; set; }

    public string CategoryName { get; set; } = string.Empty;

    public string AttributeKey { get; set; } = string.Empty;

    public string DisplayName { get; set; } = string.Empty;

    public string InputType { get; set; } = string.Empty;

    public bool IsRequired { get; set; }

    public bool IsFacet { get; set; }

    public int SortOrder { get; set; }

    public string? Placeholder { get; set; }

    public bool IsActive { get; set; }

    public DateTime CreatedAt { get; set; }

    public DateTime? UpdatedAt { get; set; }
}

public sealed class CatalogProductReadinessRowViewModel
{
    public int ProductId { get; set; }

    public string ProductName { get; set; } = string.Empty;

    public string Sku { get; set; } = string.Empty;

    public bool Status { get; set; }

    public int CategoryId { get; set; }

    public string CategoryName { get; set; } = string.Empty;

    public DateTime CreatedDate { get; set; }

    public int TotalRequired { get; set; }

    public int ReadyCount { get; set; }

    public int MissingCount { get; set; }

    public int ReadinessScore { get; set; }

    public List<string> MissingAttributes { get; set; } = new();
}

public sealed class CatalogAttributeEditorInput
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
