namespace FreshFarm.Ordering.Api.Options;

public sealed class FinanceOptions
{
    public const string SectionName = "Finance";

    public decimal DefaultCommissionRate { get; set; } = 0.10m;

    public int CommissionRoundingDecimals { get; set; } = 0;
}
