namespace FreshFarm.Ordering.Api.Models;

public sealed class SellerWithdrawal
{
    public int SellerWithdrawalId { get; set; }

    public int SellerId { get; set; }

    public decimal Amount { get; set; }

    public string BankName { get; set; } = string.Empty;

    public string BankAccountName { get; set; } = string.Empty;

    public string BankAccountNumber { get; set; } = string.Empty;

    public string Status { get; set; } = "completed";

    public string? Note { get; set; }

    public DateTime CreatedAt { get; set; }

    public DateTime? CompletedAt { get; set; }
}
