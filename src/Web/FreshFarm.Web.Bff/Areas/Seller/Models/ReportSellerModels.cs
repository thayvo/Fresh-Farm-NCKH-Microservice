using System.Globalization;

namespace FreshFarm.Web.Bff.Areas.Seller.Models;

public sealed class CustomerReportViewModel
{
    public DateTime? RegisterFromDate { get; set; }

    public DateTime? RegisterToDate { get; set; }

    public string CustomerSegment { get; set; } = "all";

    public int TotalCustomers { get; set; }

    public int NewCustomers { get; set; }

    public decimal ReturnRate { get; set; }

    public decimal AverageSpending { get; set; }

    public List<string> GrowthLabels { get; set; } = new();

    public List<int> GrowthData { get; set; } = new();

    public int NewCustomerCount { get; set; }

    public int ReturningCustomerCount { get; set; }

    public int VIPCustomerCount { get; set; }

    public int OtherCustomerCount { get; set; }

    public int TotalCustomersWithPurchases { get; set; }

    public List<TopCustomerViewModel> TopCustomers { get; set; } = new();

    public string ReturnRateFormatted => $"{ReturnRate:F1}%";

    public string AverageSpendingFormatted => string.Format(CultureInfo.InvariantCulture, "{0:N0}₫", AverageSpending);

    public int BronzeCustomerCount => OtherCustomerCount;

    public int SilverCustomerCount => NewCustomerCount;

    public int GoldCustomerCount => ReturningCustomerCount;

    public string TotalCustomersWithPurchasesFormatted =>
        (TotalCustomersWithPurchases > 0 ? TotalCustomersWithPurchases : (ReturningCustomerCount + VIPCustomerCount + OtherCustomerCount))
        .ToString("N0", CultureInfo.InvariantCulture);
}

public sealed class TopCustomerViewModel
{
    public int Rank { get; set; }

    public int UserID { get; set; }

    public string? AvatarUrl { get; set; }

    public string FullName { get; set; } = string.Empty;

    public DateTime CreatedDate { get; set; }

    public int TotalOrders { get; set; }

    public decimal TotalSpent { get; set; }

    public string CreatedDateFormatted => CreatedDate.ToString("dd/MM/yyyy");

    public string TotalSpentFormatted => string.Format(CultureInfo.InvariantCulture, "{0:N0}₫", TotalSpent);
}

public sealed class OrderReportViewModel
{
    public DateTime? FromDate { get; set; }

    public DateTime? ToDate { get; set; }

    public string OrderStatus { get; set; } = "all";

    public int TotalOrders { get; set; }

    public int SuccessOrders { get; set; }

    public int CancelledOrders { get; set; }

    public decimal CancelRate { get; set; }

    public List<string> TrendLabels { get; set; } = new();

    public List<int> TrendTotalData { get; set; } = new();

    public List<int> TrendSuccessData { get; set; } = new();

    public List<int> TrendCancelledData { get; set; } = new();

    public int StatusSuccessCount { get; set; }

    public int StatusShippingCount { get; set; }

    public int StatusPendingCount { get; set; }

    public int StatusCancelledCount { get; set; }

    public List<OrderRecentItemViewModel> RecentOrders { get; set; } = new();
}

public sealed class OrderRecentItemViewModel
{
    public int OrderID { get; set; }

    public string OrderCode { get; set; } = string.Empty;

    public string CustomerName { get; set; } = string.Empty;

    public decimal TotalAmount { get; set; }

    public DateTime OrderDate { get; set; }

    public string OrderDateFormatted { get; set; } = string.Empty;

    public string StatusBadgeClass { get; set; } = "bg-secondary";

    public string StatusText { get; set; } = "Khong xac dinh";
}

public sealed class RevenueReportViewModel
{
    public DateTime? FromDate { get; set; }

    public DateTime? ToDate { get; set; }

    public string ViewBy { get; set; } = "day";

    public decimal TotalRevenue { get; set; }

    public int TotalOrders { get; set; }

    public int TotalCustomers { get; set; }

    public decimal ReturnRate { get; set; }

    public decimal AverageOrderValue { get; set; }

    public decimal EstimatedProfit { get; set; }

    public List<string> TrendLabels { get; set; } = new();

    public List<decimal> TrendData { get; set; } = new();

    public List<RevenueTopProductViewModel> TopProducts { get; set; } = new();

    public List<RevenueDailyViewModel> DailyRevenues { get; set; } = new();
}

public sealed class RevenueTopProductViewModel
{
    public string ProductName { get; set; } = string.Empty;

    public decimal TotalRevenue { get; set; }
}

public sealed class RevenueDailyViewModel
{
    public DateTime Date { get; set; }

    public string DateFormatted { get; set; } = string.Empty;

    public int TotalOrders { get; set; }

    public int TotalProducts { get; set; }

    public decimal Revenue { get; set; }

    public decimal Profit { get; set; }
}

public sealed class ProductReportViewModel
{
    public DateTime? FromDate { get; set; }

    public DateTime? ToDate { get; set; }

    public string ProductCategory { get; set; } = "all";

    public string BestSellingProduct { get; set; } = "-";

    public int TotalProductsSold { get; set; }

    public decimal AverageRevenuePerProduct { get; set; }

    public int LowStockProducts { get; set; }

    public List<ProductTopSellingItemViewModel> TopSellingProducts { get; set; } = new();

    public List<ProductCategoryRevenueViewModel> CategoryRevenues { get; set; } = new();

    public List<ProductPerformanceViewModel> ProductPerformances { get; set; } = new();

    public int CurrentPage { get; set; } = 1;

    public int TotalPages { get; set; } = 1;
}

public sealed class ProductTopSellingItemViewModel
{
    public string ProductName { get; set; } = string.Empty;

    public int QuantitySold { get; set; }
}

public sealed class ProductCategoryRevenueViewModel
{
    public string CategoryName { get; set; } = string.Empty;

    public decimal Revenue { get; set; }
}

public sealed class ProductPerformanceViewModel
{
    public string? ImageFileName { get; set; }

    public string ProductName { get; set; } = string.Empty;

    public string Sku { get; set; } = string.Empty;

    public string CategoryName { get; set; } = string.Empty;

    public int QuantitySold { get; set; }

    public string StockBadgeClass { get; set; } = "bg-secondary";

    public string StockStatus { get; set; } = "Khong xac dinh";

    public decimal TotalRevenue { get; set; }
}

public sealed class ShippingReportViewModel
{
    public DateTime? FromDate { get; set; }

    public DateTime? ToDate { get; set; }

    public int? SelectedStaffId { get; set; }

    public string Status { get; set; } = "all";

    public string Query { get; set; } = string.Empty;

    public string Sort { get; set; } = "date_desc";

    public int CurrentPage { get; set; } = 1;

    public int PageSize { get; set; } = 10;

    public int TotalPages { get; set; } = 1;

    public int TotalRecords { get; set; }

    public int DeliveredOrders { get; set; }

    public decimal TotalShippingFee { get; set; }

    public decimal AverageDeliveryTime { get; set; }

    public decimal ReturnRate { get; set; }

    public List<ShippingStaffPerformanceViewModel> StaffPerformances { get; set; } = new();

    public List<ShippingTimeDistributionViewModel> DeliveryTimeDistributions { get; set; } = new();

    public List<ShippingRecentItemViewModel> RecentShippings { get; set; } = new();

    public List<SellerSelectOptionViewModel> DeliveryStaffs { get; set; } = new();
}

public sealed class ShippingStaffPerformanceViewModel
{
    public string StaffName { get; set; } = string.Empty;

    public int TotalOrders { get; set; }

    public decimal SuccessRate { get; set; }
}

public sealed class ShippingTimeDistributionViewModel
{
    public string TimeRange { get; set; } = string.Empty;

    public int OrderCount { get; set; }
}

public sealed class ShippingRecentItemViewModel
{
    public string OrderCode { get; set; } = string.Empty;

    public string CustomerName { get; set; } = string.Empty;

    public string CustomerPhone { get; set; } = string.Empty;

    public string DeliveryStaffName { get; set; } = string.Empty;

    public string DeliveryAddress { get; set; } = string.Empty;

    public string ShippingDateFormatted { get; set; } = string.Empty;

    public string ExpectedDeliveryDateFormatted { get; set; } = string.Empty;

    public string StatusBadgeClass { get; set; } = "bg-secondary";

    public string StatusText { get; set; } = "Khong xac dinh";

    public int? ActualDeliveryDays { get; set; }
}

public sealed class SellerSelectOptionViewModel
{
    public string Value { get; set; } = string.Empty;

    public string Text { get; set; } = string.Empty;
}

public sealed class ReviewReportViewModel
{
    public DateTime? FromDate { get; set; }

    public DateTime? ToDate { get; set; }

    public string StarRating { get; set; } = "all";

    public int TotalReviews { get; set; }

    public decimal AverageRating { get; set; }

    public int PositiveReviews { get; set; }

    public decimal PositivePercentage { get; set; }

    public int NegativeReviews { get; set; }

    public decimal NegativePercentage { get; set; }

    public List<ReviewStarDistributionViewModel> StarDistributions { get; set; } = new();

    public List<string> TopMentionedTopics { get; set; } = new();

    public List<ReviewRecentItemViewModel> RecentReviews { get; set; } = new();

    public int Page { get; set; } = 1;

    public int PageSize { get; set; } = 10;

    public int TotalPages { get; set; } = 1;

    public int Total { get; set; }
}

public sealed class ReviewStarDistributionViewModel
{
    public string Label { get; set; } = string.Empty;

    public int Count { get; set; }
}

public sealed class ReviewRecentItemViewModel
{
    public int ReviewID { get; set; }

    public int? UserID { get; set; }

    public string CustomerName { get; set; } = string.Empty;

    public string? AvatarUrl { get; set; }

    public string ProductName { get; set; } = string.Empty;

    public string? ProductImageFileName { get; set; }

    public int Rating { get; set; }

    public string StarDisplay { get; set; } = string.Empty;

    public string Comment { get; set; } = string.Empty;

    public DateTime CreatedAt { get; set; }

    public string CreatedAtFormatted { get; set; } = string.Empty;
}
