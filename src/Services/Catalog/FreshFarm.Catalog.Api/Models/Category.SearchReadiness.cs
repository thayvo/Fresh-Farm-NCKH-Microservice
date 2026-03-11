using System.Collections.Generic;

namespace FreshFarm.Catalog.Api.Models;

public partial class Category
{
    public virtual ICollection<CategoryAttribute> CategoryAttributes { get; set; } = new List<CategoryAttribute>();
}

public partial class CategoryAttribute
{
    public virtual Category Category { get; set; } = null!;

    public virtual ICollection<ProductAttributeValue> ProductAttributeValues { get; set; } = new List<ProductAttributeValue>();
}
