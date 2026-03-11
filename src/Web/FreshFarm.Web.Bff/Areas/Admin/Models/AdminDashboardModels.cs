namespace FreshFarm.Web.Bff.Areas.Admin.Models;

public sealed class AdminDashboardViewModel
{
    public decimal Gmv { get; set; }

    public decimal PlatformRevenue { get; set; }

    public int TotalOrders { get; set; }

    public int NewOrdersToday { get; set; }

    public int CancelledOrders { get; set; }

    public decimal CancelRate { get; set; }

    public int RealtimeTransactions { get; set; }

    public int TotalUsers { get; set; }

    public int NewUsersToday { get; set; }

    public int NewUsers7Days { get; set; }

    public int TotalSellers { get; set; }

    public int NewSellers7Days { get; set; }

    public int TotalBuyers { get; set; }

    public int NewBuyers7Days { get; set; }

    public int TrafficToday { get; set; }

    public int ActiveSessions { get; set; }

    public List<string> TrendLabels { get; set; } = new();

    public List<int> TrendOrders { get; set; } = new();

    public List<decimal> TrendGmv { get; set; } = new();
}

internal sealed class PlatformDashboardApiDto
{
    public decimal Gmv { get; set; }

    public decimal PlatformRevenue { get; set; }

    public int TotalOrders { get; set; }

    public int NewOrdersToday { get; set; }

    public int CancelledOrders { get; set; }

    public decimal CancelRate { get; set; }

    public int RealtimeTransactions { get; set; }

    public List<string>? TrendLabels { get; set; }

    public List<int>? TrendOrders { get; set; }

    public List<decimal>? TrendGmv { get; set; }
}

internal sealed class AdminUsersMetricsApiDto
{
    public int TotalUsers { get; set; }

    public int NewUsersToday { get; set; }

    public int NewUsers7Days { get; set; }

    public int TotalSellers { get; set; }

    public int NewSellers7Days { get; set; }

    public int TotalBuyers { get; set; }

    public int NewBuyers7Days { get; set; }

    public int TrafficToday { get; set; }

    public int ActiveSessions { get; set; }
}
