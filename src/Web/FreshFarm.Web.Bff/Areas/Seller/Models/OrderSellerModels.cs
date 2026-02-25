namespace FreshFarm.Web.Bff.Areas.Seller.Models;

public sealed class SellerOrderInvoiceViewModel
{
    public int OrderID { get; set; }

    public DateTime OrderDate { get; set; }

    public string Status { get; set; } = string.Empty;

    public string? PaymentStatus { get; set; }

    public string? OrderNote { get; set; }

    public string? CustomerName { get; set; }

    public string? BuyerFullName { get; set; }

    public string? BuyerPhone { get; set; }

    public string? BuyerEmail { get; set; }

    public string? Address { get; set; }

    public decimal ShippingFee { get; set; }

    public decimal TotalAmount { get; set; }

    public List<SellerOrderInvoiceItemViewModel> OrderDetails { get; set; } = new();
}

public sealed class SellerOrderInvoiceItemViewModel
{
    public int ProductID { get; set; }

    public string ProductName { get; set; } = string.Empty;

    public int Quantity { get; set; }

    public decimal UnitPrice { get; set; }

    public string? UnitSymbol { get; set; }
}
