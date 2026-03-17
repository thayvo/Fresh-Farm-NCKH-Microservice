using System.ComponentModel.DataAnnotations.Schema;

namespace FreshFarm.Catalog.Api.Models;

public partial class Product
{
    public int ReservedStock { get; set; }

    [NotMapped]
    public int OnHandStock
    {
        get => StockQuantity;
        set => StockQuantity = value;
    }

    [NotMapped]
    public int AvailableStock => Math.Max(0, StockQuantity - ReservedStock);
}
