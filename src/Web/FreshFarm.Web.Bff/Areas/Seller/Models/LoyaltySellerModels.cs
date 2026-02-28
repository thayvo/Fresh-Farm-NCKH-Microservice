namespace FreshFarm.Web.Bff.Areas.Seller.Models;

public sealed class LoyaltyDashboardVM
{
    public int TotalEarned { get; set; }

    public int TotalRedeemed { get; set; }

    public int UsersWithPoints { get; set; }

    public int CurrentYear { get; set; }

    public byte CurrentQuarter { get; set; }

    public List<LoyaltyTopUserVM> TopQuarterUsers { get; set; } = new();

    public List<LoyaltyHistoryRowVM> RecentActivities { get; set; } = new();
}

public sealed class LoyaltyTopUserVM
{
    public int UserID { get; set; }

    public string FullName { get; set; } = string.Empty;

    public int TotalPoints { get; set; }

    public int QuarterPoints { get; set; }

    public string RankName { get; set; } = string.Empty;
}

public sealed class LoyaltyUsersVM
{
    public string Query { get; set; } = string.Empty;

    public int Page { get; set; }

    public int PageSize { get; set; }

    public int Total { get; set; }

    public List<LoyaltyUserRowVM> Rows { get; set; } = new();
}

public sealed class LoyaltyUserRowVM
{
    public int UserID { get; set; }

    public string FullName { get; set; } = string.Empty;

    public int TotalPoints { get; set; }

    public int CurrentQuarterPoints { get; set; }

    public string RankName { get; set; } = string.Empty;
}

public sealed class LoyaltyHistoryVM
{
    public int? UserID { get; set; }

    public string Direction { get; set; } = string.Empty;

    public DateTime? Start { get; set; }

    public DateTime? End { get; set; }

    public int Page { get; set; }

    public int PageSize { get; set; }

    public int Total { get; set; }

    public List<LoyaltyHistoryRowVM> Rows { get; set; } = new();
}

public sealed class LoyaltyHistoryRowVM
{
    public DateTime CreatedAt { get; set; }

    public int UserID { get; set; }

    public string UserName { get; set; } = string.Empty;

    public int? OrderID { get; set; }

    public int Points { get; set; }

    public string Direction { get; set; } = string.Empty;

    public string Reason { get; set; } = string.Empty;
}

public sealed class LoyaltyConfigVM
{
    public decimal EarnRate { get; set; }

    public bool IncludeShippingFee { get; set; }

    public string PaidKeywords { get; set; } = string.Empty;

    public DateTime? UpdatedAt { get; set; }
}
