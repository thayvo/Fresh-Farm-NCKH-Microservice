using System;

namespace FreshFarm.Catalog.Api.Models;

public partial class ProductAttributeValue
{
    public int ProductAttributeValueId { get; set; }

    public int ProductId { get; set; }

    public int CategoryAttributeId { get; set; }

    public string ValueText { get; set; } = null!;

    public string? NormalizedValue { get; set; }

    public string? Source { get; set; }

    public DateTime CreatedAt { get; set; }

    public DateTime? UpdatedAt { get; set; }

    public virtual CategoryAttribute CategoryAttribute { get; set; } = null!;

    public virtual Product Product { get; set; } = null!;
}
