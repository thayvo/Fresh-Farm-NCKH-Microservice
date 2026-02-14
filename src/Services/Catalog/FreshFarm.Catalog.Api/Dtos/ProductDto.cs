namespace FreshFarm.Catalog.Api.Dtos
{
    public class ProductDto
    {
        public int ProductId { get; set; }
        public string ProductName { get; set; } = default!;
        public double Price { get; set; }
        public string? CategoryName { get; set; }
        public string? UnitName { get; set; }


    }
}
