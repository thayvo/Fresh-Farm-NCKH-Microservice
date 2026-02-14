namespace FreshFarm.Catalog.Api.Dtos
{
    public class CreateProductRequest
    {
        public int CategoryId { get; set; }
        public int UnitId { get; set; }
        public string ProductName { get; set; } = default!;
        public decimal Price { get; set; }
        public string? Sku { get; set; }

    }
}
