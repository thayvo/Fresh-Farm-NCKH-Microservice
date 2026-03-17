namespace FreshFarm.Web.Bff.Models;

public sealed class CheckoutPaymentResultViewModel
{
    public int? OrderId { get; set; }
    public bool IsSuccess { get; set; }
    public string Message { get; set; } = string.Empty;
}
