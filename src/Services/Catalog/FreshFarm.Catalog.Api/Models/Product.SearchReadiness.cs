using System.Collections.Generic;

namespace FreshFarm.Catalog.Api.Models;

public partial class Product
{
    public virtual ICollection<ProductAttributeValue> ProductAttributeValues { get; set; } = new List<ProductAttributeValue>();

    public virtual ICollection<FreshInventoryLot> FreshInventoryLots { get; set; } = new List<FreshInventoryLot>();

    public virtual ICollection<FreshQualityRecall> FreshQualityRecalls { get; set; } = new List<FreshQualityRecall>();
}
