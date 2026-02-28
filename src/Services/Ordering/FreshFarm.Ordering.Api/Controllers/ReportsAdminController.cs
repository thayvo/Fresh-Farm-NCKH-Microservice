using FreshFarm.Ordering.Api.Models;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using System.Globalization;
using System.Text;

namespace FreshFarm.Ordering.Api.Controllers;

[ApiController]
[Route("api/orders/admin/reports")]
[Authorize(Policy = "SellerOnly")]
public sealed class ReportsAdminController : ControllerBase
{
    private static readonly string[] ShippingStaffNames =
    {
        "Nhan vien giao 01",
        "Nhan vien giao 02",
        "Nhan vien giao 03",
        "Nhan vien giao 04"
    };

    private readonly FreshFarmOrderingDBContext _db;

    public ReportsAdminController(FreshFarmOrderingDBContext db)
    {
        _db = db;
    }

    [HttpGet("customers")]
    public async Task<IActionResult> Customers(
        [FromQuery] DateTime? registerFromDate,
        [FromQuery] DateTime? registerToDate,
        [FromQuery] string? customerSegment,
        CancellationToken cancellationToken = default)
    {
        var orders = await _db.Orders
            .AsNoTracking()
            .Where(o => !registerFromDate.HasValue || o.OrderDate >= registerFromDate.Value.Date)
            .Where(o => !registerToDate.HasValue || o.OrderDate <= registerToDate.Value.Date.AddDays(1).AddTicks(-1))
            .ToListAsync(cancellationToken);

        var customerRows = orders
            .GroupBy(o => o.UserId)
            .Select(g => new
            {
                UserId = g.Key,
                FullName = g.OrderByDescending(x => x.OrderDate).Select(x => x.BuyerFullName).FirstOrDefault() ?? $"Khach {g.Key}",
                FirstOrderDate = g.Min(x => x.OrderDate),
                OrderCount = g.Count(),
                TotalSpent = g.Sum(x => x.TotalAmount)
            })
            .ToList();

        var segment = string.IsNullOrWhiteSpace(customerSegment) ? "all" : customerSegment.Trim().ToLowerInvariant();
        var threshold = DateTime.UtcNow.AddDays(-30);

        var filteredCustomers = segment switch
        {
            "new" => customerRows.Where(x => x.FirstOrderDate >= threshold).ToList(),
            "returning" => customerRows.Where(x => x.OrderCount >= 2 && x.OrderCount < 5).ToList(),
            "vip" => customerRows.Where(x => x.OrderCount >= 5 || x.TotalSpent >= 10_000_000m).ToList(),
            _ => customerRows
        };

        var totalCustomers = filteredCustomers.Count;
        var newCustomers = filteredCustomers.Count(x => x.FirstOrderDate >= threshold);
        var returningCustomers = filteredCustomers.Count(x => x.OrderCount >= 2);
        var returnRate = totalCustomers > 0 ? (decimal)returningCustomers / totalCustomers * 100m : 0m;
        var averageSpending = totalCustomers > 0 ? filteredCustomers.Sum(x => x.TotalSpent) / totalCustomers : 0m;

        var growthLabels = new List<string>();
        var growthData = new List<int>();
        var monthLookup = filteredCustomers
            .GroupBy(x => new { x.FirstOrderDate.Year, x.FirstOrderDate.Month })
            .ToDictionary(g => $"{g.Key.Year}-{g.Key.Month}", g => g.Count());

        for (var i = 6; i >= 0; i--)
        {
            var month = DateTime.UtcNow.AddMonths(-i);
            var key = $"{month.Year}-{month.Month}";
            growthLabels.Add($"Thang {month.Month}");
            growthData.Add(monthLookup.TryGetValue(key, out var value) ? value : 0);
        }

        var topCustomers = filteredCustomers
            .OrderByDescending(x => x.TotalSpent)
            .Take(10)
            .Select((x, index) => new
            {
                Rank = index + 1,
                UserID = x.UserId,
                AvatarUrl = string.Empty,
                FullName = x.FullName,
                CreatedDate = x.FirstOrderDate,
                CreatedDateFormatted = x.FirstOrderDate.ToString("dd/MM/yyyy"),
                TotalOrders = x.OrderCount,
                TotalSpent = x.TotalSpent,
                TotalSpentFormatted = string.Format(CultureInfo.InvariantCulture, "{0:N0}₫", x.TotalSpent)
            })
            .ToList();

        var model = new
        {
            RegisterFromDate = registerFromDate,
            RegisterToDate = registerToDate,
            CustomerSegment = segment,
            TotalCustomers = totalCustomers,
            NewCustomers = newCustomers,
            ReturnRate = returnRate,
            ReturnRateFormatted = $"{returnRate:F1}%",
            AverageSpending = averageSpending,
            AverageSpendingFormatted = string.Format(CultureInfo.InvariantCulture, "{0:N0}₫", averageSpending),
            GrowthLabels = growthLabels,
            GrowthData = growthData,
            NewCustomerCount = newCustomers,
            ReturningCustomerCount = filteredCustomers.Count(x => x.OrderCount >= 2 && x.OrderCount < 5),
            VIPCustomerCount = filteredCustomers.Count(x => x.OrderCount >= 5 || x.TotalSpent >= 10_000_000m),
            OtherCustomerCount = Math.Max(0, totalCustomers - newCustomers - filteredCustomers.Count(x => x.OrderCount >= 2 && x.OrderCount < 5) - filteredCustomers.Count(x => x.OrderCount >= 5 || x.TotalSpent >= 10_000_000m)),
            TopCustomers = topCustomers
        };

        return Ok(model);
    }

    [HttpGet("customers/export")]
    public async Task<IActionResult> ExportCustomers(
        [FromQuery] DateTime? registerFromDate,
        [FromQuery] DateTime? registerToDate,
        [FromQuery] string? customerSegment,
        CancellationToken cancellationToken = default)
    {
        var orders = await _db.Orders
            .AsNoTracking()
            .Where(o => !registerFromDate.HasValue || o.OrderDate >= registerFromDate.Value.Date)
            .Where(o => !registerToDate.HasValue || o.OrderDate <= registerToDate.Value.Date.AddDays(1).AddTicks(-1))
            .ToListAsync(cancellationToken);

        var customerRows = orders
            .GroupBy(o => o.UserId)
            .Select(g => new
            {
                UserId = g.Key,
                FullName = g.OrderByDescending(x => x.OrderDate).Select(x => x.BuyerFullName).FirstOrDefault() ?? $"Khach {g.Key}",
                FirstOrderDate = g.Min(x => x.OrderDate),
                OrderCount = g.Count(),
                TotalSpent = g.Sum(x => x.TotalAmount)
            })
            .ToList();

        var segment = string.IsNullOrWhiteSpace(customerSegment) ? "all" : customerSegment.Trim().ToLowerInvariant();
        var threshold = DateTime.UtcNow.AddDays(-30);
        var filteredCustomers = segment switch
        {
            "new" => customerRows.Where(x => x.FirstOrderDate >= threshold).ToList(),
            "returning" => customerRows.Where(x => x.OrderCount >= 2 && x.OrderCount < 5).ToList(),
            "vip" => customerRows.Where(x => x.OrderCount >= 5 || x.TotalSpent >= 10_000_000m).ToList(),
            _ => customerRows
        };

        var rows = filteredCustomers
            .OrderByDescending(x => x.TotalSpent)
            .Select((x, index) => new[]
            {
                (index + 1).ToString(CultureInfo.InvariantCulture),
                x.UserId.ToString(CultureInfo.InvariantCulture),
                x.FullName,
                x.FirstOrderDate.ToString("dd/MM/yyyy"),
                x.OrderCount.ToString(CultureInfo.InvariantCulture),
                x.TotalSpent.ToString("0.##", CultureInfo.InvariantCulture)
            })
            .ToList();

        return BuildCsvFile(
            $"customer-report-{DateTime.UtcNow:yyyyMMdd-HHmmss}.csv",
            new[] { "Rank", "UserID", "FullName", "CreatedDate", "TotalOrders", "TotalSpent" },
            rows);
    }

    [HttpGet("orders")]
    public async Task<IActionResult> Orders(
        [FromQuery] DateTime? fromDate,
        [FromQuery] DateTime? toDate,
        [FromQuery] string? orderStatus,
        CancellationToken cancellationToken = default)
    {
        var statusFilter = string.IsNullOrWhiteSpace(orderStatus) ? "all" : orderStatus.Trim();

        var filteredOrders = await _db.Orders
            .AsNoTracking()
            .Where(o => !fromDate.HasValue || o.OrderDate >= fromDate.Value.Date)
            .Where(o => !toDate.HasValue || o.OrderDate <= toDate.Value.Date.AddDays(1).AddTicks(-1))
            .OrderByDescending(o => o.OrderDate)
            .ToListAsync(cancellationToken);

        if (!statusFilter.Equals("all", StringComparison.OrdinalIgnoreCase))
        {
            filteredOrders = filteredOrders
                .Where(o => MapOrderStatus(o.Status).Equals(statusFilter, StringComparison.OrdinalIgnoreCase))
                .ToList();
        }

        var totalOrders = filteredOrders.Count;
        var successOrders = filteredOrders.Count(o => IsSuccessStatus(o.Status));
        var cancelledOrders = filteredOrders.Count(o => IsCancelledStatus(o.Status));
        var cancelRate = totalOrders > 0 ? (decimal)cancelledOrders / totalOrders * 100m : 0m;

        var recentOrders = filteredOrders
            .Take(20)
            .Select(o => new
            {
                OrderID = o.OrderId,
                OrderCode = BuildOrderCode(o.OrderId),
                CustomerName = string.IsNullOrWhiteSpace(o.BuyerFullName) ? $"Khach {o.UserId}" : o.BuyerFullName,
                OrderDate = o.OrderDate,
                OrderDateFormatted = o.OrderDate.ToString("dd/MM/yyyy"),
                TotalAmount = o.TotalAmount,
                StatusBadgeClass = MapStatusBadgeClass(o.Status),
                StatusText = MapStatusText(o.Status)
            })
            .ToList();

        var trendRange = Enumerable.Range(0, 7)
            .Select(i => DateTime.UtcNow.Date.AddDays(-6 + i))
            .ToList();

        var trendLabels = trendRange.Select(d => d.ToString("dd/MM")).ToList();
        var trendTotalData = trendRange.Select(d => filteredOrders.Count(o => o.OrderDate.Date == d)).ToList();
        var trendSuccessData = trendRange.Select(d => filteredOrders.Count(o => o.OrderDate.Date == d && IsSuccessStatus(o.Status))).ToList();
        var trendCancelledData = trendRange.Select(d => filteredOrders.Count(o => o.OrderDate.Date == d && IsCancelledStatus(o.Status))).ToList();

        var model = new
        {
            FromDate = fromDate,
            ToDate = toDate,
            OrderStatus = statusFilter,
            TotalOrders = totalOrders,
            SuccessOrders = successOrders,
            CancelledOrders = cancelledOrders,
            CancelRate = cancelRate,
            TrendLabels = trendLabels,
            TrendTotalData = trendTotalData,
            TrendSuccessData = trendSuccessData,
            TrendCancelledData = trendCancelledData,
            StatusSuccessCount = filteredOrders.Count(o => IsSuccessStatus(o.Status)),
            StatusShippingCount = filteredOrders.Count(o => IsShippingStatus(o.Status)),
            StatusPendingCount = filteredOrders.Count(o => IsPendingStatus(o.Status)),
            StatusCancelledCount = cancelledOrders,
            RecentOrders = recentOrders
        };

        return Ok(model);
    }

    [HttpGet("orders/export")]
    public async Task<IActionResult> ExportOrders(
        [FromQuery] DateTime? fromDate,
        [FromQuery] DateTime? toDate,
        [FromQuery] string? orderStatus,
        CancellationToken cancellationToken = default)
    {
        var statusFilter = string.IsNullOrWhiteSpace(orderStatus) ? "all" : orderStatus.Trim();

        var filteredOrders = await _db.Orders
            .AsNoTracking()
            .Where(o => !fromDate.HasValue || o.OrderDate >= fromDate.Value.Date)
            .Where(o => !toDate.HasValue || o.OrderDate <= toDate.Value.Date.AddDays(1).AddTicks(-1))
            .OrderByDescending(o => o.OrderDate)
            .ToListAsync(cancellationToken);

        if (!statusFilter.Equals("all", StringComparison.OrdinalIgnoreCase))
        {
            filteredOrders = filteredOrders
                .Where(o => MapOrderStatus(o.Status).Equals(statusFilter, StringComparison.OrdinalIgnoreCase))
                .ToList();
        }

        var rows = filteredOrders.Select(o => new[]
        {
            BuildOrderCode(o.OrderId),
            string.IsNullOrWhiteSpace(o.BuyerFullName) ? $"Khach {o.UserId}" : o.BuyerFullName,
            o.OrderDate.ToString("dd/MM/yyyy"),
            o.TotalAmount.ToString("0.##", CultureInfo.InvariantCulture),
            MapStatusText(o.Status)
        });

        return BuildCsvFile(
            $"order-report-{DateTime.UtcNow:yyyyMMdd-HHmmss}.csv",
            new[] { "OrderCode", "CustomerName", "OrderDate", "TotalAmount", "Status" },
            rows);
    }

    [HttpGet("revenue")]
    public async Task<IActionResult> Revenue(
        [FromQuery] DateTime? fromDate,
        [FromQuery] DateTime? toDate,
        [FromQuery] string? viewBy,
        CancellationToken cancellationToken = default)
    {
        var groupBy = string.IsNullOrWhiteSpace(viewBy) ? "day" : viewBy.Trim().ToLowerInvariant();

        var orders = await _db.Orders
            .AsNoTracking()
            .Where(o => !fromDate.HasValue || o.OrderDate >= fromDate.Value.Date)
            .Where(o => !toDate.HasValue || o.OrderDate <= toDate.Value.Date.AddDays(1).AddTicks(-1))
            .ToListAsync(cancellationToken);

        var orderIds = orders.Select(o => o.OrderId).ToHashSet();
        var details = await _db.OrderDetails
            .AsNoTracking()
            .Where(d => orderIds.Contains(d.OrderId))
            .ToListAsync(cancellationToken);
        details = FilterReportableDetails(details);

        var totalRevenue = orders.Sum(o => o.TotalAmount);
        var totalOrders = orders.Count;
        var totalCustomers = orders.Select(o => o.UserId).Distinct().Count();
        var returnRate = totalOrders > 0
            ? decimal.Round((decimal)orders.Count(o => IsCancelledStatus(o.Status)) / totalOrders * 100m, 2)
            : 0m;
        var averageOrderValue = totalOrders > 0 ? totalRevenue / totalOrders : 0m;
        var estimatedProfit = totalRevenue * 0.3m;

        var groupedRevenue = orders
            .GroupBy(o => GetGroupKey(o.OrderDate, groupBy))
            .OrderBy(g => g.Key.Sort)
            .Select(g => new
            {
                g.Key.Label,
                Revenue = g.Sum(x => x.TotalAmount)
            })
            .ToList();

        var trendLabels = groupedRevenue.Select(x => x.Label).ToList();
        var trendData = groupedRevenue.Select(x => decimal.Round(x.Revenue / 1_000_000m, 2)).ToList();

        var topProducts = details
            .GroupBy(d => d.ProductId)
            .Select(g => new
            {
                ProductId = g.Key,
                Revenue = g.Sum(x => x.UnitPrice * x.Quantity)
            })
            .OrderByDescending(x => x.Revenue)
            .Take(5)
            .Select(x => new
            {
                ProductName = $"San pham #{x.ProductId}",
                TotalRevenue = x.Revenue
            })
            .ToList();

        var daily = orders
            .GroupBy(o => o.OrderDate.Date)
            .OrderByDescending(g => g.Key)
            .Take(30)
            .Select(g =>
            {
                var dayOrderIds = g.Select(x => x.OrderId).ToHashSet();
                var dayDetails = details.Where(d => dayOrderIds.Contains(d.OrderId));
                var revenue = g.Sum(x => x.TotalAmount);
                var totalProducts = dayDetails.Sum(d => (long)d.Quantity);
                return new
                {
                    Date = g.Key,
                    DateFormatted = g.Key.ToString("dd/MM/yyyy"),
                    TotalOrders = g.Count(),
                    TotalProducts = ToSafeNonNegativeInt(totalProducts),
                    Revenue = revenue,
                    Profit = revenue * 0.3m
                };
            })
            .OrderBy(x => x.Date)
            .ToList();

        var model = new
        {
            FromDate = fromDate,
            ToDate = toDate,
            ViewBy = groupBy,
            TotalRevenue = totalRevenue,
            TotalOrders = totalOrders,
            TotalCustomers = totalCustomers,
            ReturnRate = returnRate,
            AverageOrderValue = averageOrderValue,
            EstimatedProfit = estimatedProfit,
            TrendLabels = trendLabels,
            TrendData = trendData,
            TopProducts = topProducts,
            DailyRevenues = daily
        };

        return Ok(model);
    }

    [HttpGet("revenue/export")]
    public async Task<IActionResult> ExportRevenue(
        [FromQuery] DateTime? fromDate,
        [FromQuery] DateTime? toDate,
        [FromQuery] string? viewBy,
        CancellationToken cancellationToken = default)
    {
        var groupBy = string.IsNullOrWhiteSpace(viewBy) ? "day" : viewBy.Trim().ToLowerInvariant();

        var orders = await _db.Orders
            .AsNoTracking()
            .Where(o => !fromDate.HasValue || o.OrderDate >= fromDate.Value.Date)
            .Where(o => !toDate.HasValue || o.OrderDate <= toDate.Value.Date.AddDays(1).AddTicks(-1))
            .ToListAsync(cancellationToken);

        var orderIds = orders.Select(o => o.OrderId).ToHashSet();
        var details = await _db.OrderDetails
            .AsNoTracking()
            .Where(d => orderIds.Contains(d.OrderId))
            .ToListAsync(cancellationToken);
        details = FilterReportableDetails(details);

        var rows = orders
            .GroupBy(o => GetGroupKey(o.OrderDate, groupBy))
            .OrderBy(g => g.Key.Sort)
            .Select(g =>
            {
                var groupedOrderIds = g.Select(x => x.OrderId).ToHashSet();
                var groupedDetails = details.Where(d => groupedOrderIds.Contains(d.OrderId));
                var revenue = g.Sum(x => x.TotalAmount);
                var groupedTotalProducts = groupedDetails.Sum(d => (long)d.Quantity);
                return new[]
                {
                    g.Key.Label,
                    g.Count().ToString(CultureInfo.InvariantCulture),
                    groupedTotalProducts.ToString(CultureInfo.InvariantCulture),
                    revenue.ToString("0.##", CultureInfo.InvariantCulture),
                    (revenue * 0.3m).ToString("0.##", CultureInfo.InvariantCulture)
                };
            });

        return BuildCsvFile(
            $"revenue-report-{DateTime.UtcNow:yyyyMMdd-HHmmss}.csv",
            new[] { "Period", "TotalOrders", "TotalProducts", "Revenue", "Profit" },
            rows);
    }

    [HttpGet("products")]
    public async Task<IActionResult> Products(
        [FromQuery] DateTime? fromDate,
        [FromQuery] DateTime? toDate,
        [FromQuery] string? productCategory,
        [FromQuery] int page = 1,
        CancellationToken cancellationToken = default)
    {
        const int pageSize = 10;
        var categoryFilter = string.IsNullOrWhiteSpace(productCategory) ? "all" : productCategory.Trim();
        if (page < 1)
        {
            page = 1;
        }

        var orders = await _db.Orders
            .AsNoTracking()
            .Where(o => !fromDate.HasValue || o.OrderDate >= fromDate.Value.Date)
            .Where(o => !toDate.HasValue || o.OrderDate <= toDate.Value.Date.AddDays(1).AddTicks(-1))
            .ToListAsync(cancellationToken);

        var orderIds = orders.Select(o => o.OrderId).ToHashSet();
        var details = await _db.OrderDetails
            .AsNoTracking()
            .Where(d => orderIds.Contains(d.OrderId))
            .ToListAsync(cancellationToken);
        details = FilterReportableDetails(details);

        var productAgg = details
            .GroupBy(d => d.ProductId)
            .Select(g =>
            {
                var quantity = g.Sum(x => (long)x.Quantity);
                var revenue = g.Sum(x => x.UnitPrice * x.Quantity);
                var categoryName = GetCategoryName(g.Key);
                var stock = GetPseudoStock(g.Key);
                return new
                {
                    ProductId = g.Key,
                    ProductName = $"San pham #{g.Key}",
                    CategoryName = categoryName,
                    QuantitySold = quantity,
                    TotalRevenue = revenue,
                    Stock = stock,
                    Sku = $"SKU-{g.Key:D5}",
                    ImageFileName = string.Empty
                };
            })
            .ToList();

        if (!categoryFilter.Equals("all", StringComparison.OrdinalIgnoreCase))
        {
            productAgg = productAgg
                .Where(x => CategoryEquals(x.CategoryName, categoryFilter))
                .ToList();
        }

        var totalProductsSold = productAgg.Sum(x => x.QuantitySold);
        var averageRevenuePerProduct = productAgg.Count > 0 ? productAgg.Average(x => x.TotalRevenue) : 0m;
        var lowStockProducts = productAgg.Count(x => x.Stock < 20);

        var topSellingProducts = productAgg
            .OrderByDescending(x => x.QuantitySold)
            .Take(10)
            .Select(x => new
            {
                ProductName = x.ProductName,
                QuantitySold = ToSafeNonNegativeInt(x.QuantitySold)
            })
            .ToList();

        var categoryRevenues = productAgg
            .GroupBy(x => x.CategoryName)
            .Select(g => new
            {
                CategoryName = g.Key,
                Revenue = g.Sum(x => x.TotalRevenue)
            })
            .OrderByDescending(x => x.Revenue)
            .ToList();

        var totalPages = Math.Max(1, (int)Math.Ceiling(productAgg.Count / (double)pageSize));
        if (page > totalPages)
        {
            page = totalPages;
        }

        var productPerformances = productAgg
            .OrderByDescending(x => x.QuantitySold)
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .Select(x => new
            {
                x.ImageFileName,
                x.ProductName,
                x.Sku,
                x.CategoryName,
                QuantitySold = ToSafeNonNegativeInt(x.QuantitySold),
                StockBadgeClass = x.Stock < 10 ? "bg-danger" : (x.Stock < 20 ? "bg-warning text-dark" : "bg-success"),
                StockStatus = x.Stock < 10 ? "Sap het" : (x.Stock < 20 ? "Thap" : "Tot"),
                x.TotalRevenue
            })
            .ToList();

        var model = new
        {
            FromDate = fromDate,
            ToDate = toDate,
            ProductCategory = categoryFilter,
            BestSellingProduct = topSellingProducts.FirstOrDefault()?.ProductName ?? "-",
            TotalProductsSold = ToSafeNonNegativeInt(totalProductsSold),
            AverageRevenuePerProduct = averageRevenuePerProduct,
            LowStockProducts = lowStockProducts,
            TopSellingProducts = topSellingProducts,
            CategoryRevenues = categoryRevenues,
            ProductPerformances = productPerformances,
            CurrentPage = page,
            TotalPages = totalPages
        };

        return Ok(model);
    }

    [HttpGet("products/export")]
    public async Task<IActionResult> ExportProducts(
        [FromQuery] DateTime? fromDate,
        [FromQuery] DateTime? toDate,
        [FromQuery] string? productCategory,
        CancellationToken cancellationToken = default)
    {
        var categoryFilter = string.IsNullOrWhiteSpace(productCategory) ? "all" : productCategory.Trim();

        var orders = await _db.Orders
            .AsNoTracking()
            .Where(o => !fromDate.HasValue || o.OrderDate >= fromDate.Value.Date)
            .Where(o => !toDate.HasValue || o.OrderDate <= toDate.Value.Date.AddDays(1).AddTicks(-1))
            .ToListAsync(cancellationToken);

        var orderIds = orders.Select(o => o.OrderId).ToHashSet();
        var details = await _db.OrderDetails
            .AsNoTracking()
            .Where(d => orderIds.Contains(d.OrderId))
            .ToListAsync(cancellationToken);
        details = FilterReportableDetails(details);

        var productAgg = details
            .GroupBy(d => d.ProductId)
            .Select(g =>
            {
                var stock = GetPseudoStock(g.Key);
                var categoryName = GetCategoryName(g.Key);
                return new
                {
                    ProductId = g.Key,
                    ProductName = $"San pham #{g.Key}",
                    Sku = $"SKU-{g.Key:D5}",
                    CategoryName = categoryName,
                    QuantitySold = g.Sum(x => (long)x.Quantity),
                    Stock = stock,
                    TotalRevenue = g.Sum(x => x.UnitPrice * x.Quantity)
                };
            })
            .ToList();

        if (!categoryFilter.Equals("all", StringComparison.OrdinalIgnoreCase))
        {
            productAgg = productAgg
                .Where(x => CategoryEquals(x.CategoryName, categoryFilter))
                .ToList();
        }

        var rows = productAgg
            .OrderByDescending(x => x.QuantitySold)
            .Select(x => new[]
            {
                x.ProductName,
                x.Sku,
                x.CategoryName,
                x.QuantitySold.ToString(CultureInfo.InvariantCulture),
                x.Stock.ToString(CultureInfo.InvariantCulture),
                x.TotalRevenue.ToString("0.##", CultureInfo.InvariantCulture)
            });

        return BuildCsvFile(
            $"product-report-{DateTime.UtcNow:yyyyMMdd-HHmmss}.csv",
            new[] { "ProductName", "Sku", "CategoryName", "QuantitySold", "Stock", "TotalRevenue" },
            rows);
    }

    [HttpGet("shipping")]
    public async Task<IActionResult> Shipping(
        [FromQuery] DateTime? fromDate,
        [FromQuery] DateTime? toDate,
        [FromQuery] int? staffId,
        [FromQuery] string? status,
        [FromQuery] string? q,
        [FromQuery] string? sort,
        [FromQuery] int page = 1,
        [FromQuery] int pageSize = 10,
        CancellationToken cancellationToken = default)
    {
        if (page < 1)
        {
            page = 1;
        }

        if (pageSize <= 0)
        {
            pageSize = 10;
        }

        var normalizedSort = string.IsNullOrWhiteSpace(sort) ? "date_desc" : sort.Trim().ToLowerInvariant();
        var normalizedStatus = string.IsNullOrWhiteSpace(status) ? "all" : status.Trim().ToLowerInvariant();
        var keyword = string.IsNullOrWhiteSpace(q) ? string.Empty : q.Trim().ToLowerInvariant();

        var orders = await _db.Orders
            .AsNoTracking()
            .Where(o => !fromDate.HasValue || o.OrderDate >= fromDate.Value.Date)
            .Where(o => !toDate.HasValue || o.OrderDate <= toDate.Value.Date.AddDays(1).AddTicks(-1))
            .ToListAsync(cancellationToken);

        var orderIds = orders.Select(x => x.OrderId).ToHashSet();
        var shippingRows = await _db.Shippings
            .AsNoTracking()
            .Where(s => orderIds.Contains(s.OrderId))
            .ToListAsync(cancellationToken);

        var shippingLookup = shippingRows
            .GroupBy(s => s.OrderId)
            .ToDictionary(g => g.Key, g => g.OrderByDescending(x => x.ShippingId).First());

        var rows = orders
            .Select(order =>
            {
                shippingLookup.TryGetValue(order.OrderId, out var shipping);
                var staffIndex = order.OrderId % ShippingStaffNames.Length;
                var shippingDate = order.OrderDate;
                var expectedDate = order.OrderDate.AddDays(2);
                int? actualDays = IsSuccessStatus(order.Status) ? Math.Max(1, (order.OrderId % 4) + 1) : null;

                return new ShippingRow
                {
                    OrderId = order.OrderId,
                    OrderCode = BuildOrderCode(order.OrderId),
                    CustomerName = string.IsNullOrWhiteSpace(order.BuyerFullName) ? $"Khach {order.UserId}" : order.BuyerFullName,
                    CustomerPhone = string.IsNullOrWhiteSpace(order.BuyerPhone) ? (shipping?.Phone ?? "-") : order.BuyerPhone,
                    DeliveryStaffId = staffIndex + 1,
                    DeliveryStaffName = ShippingStaffNames[staffIndex],
                    DeliveryAddress = shipping?.AddressDetail ?? "Chua cap nhat",
                    ShippingDate = shippingDate,
                    ExpectedDeliveryDate = expectedDate,
                    StatusRaw = order.Status,
                    StatusText = MapStatusText(order.Status),
                    StatusBadgeClass = MapStatusBadgeClass(order.Status),
                    ActualDeliveryDays = actualDays,
                    ShippingFee = order.ShippingFee
                };
            })
            .ToList();

        if (staffId.HasValue)
        {
            rows = rows.Where(x => x.DeliveryStaffId == staffId.Value).ToList();
        }

        if (!normalizedStatus.Equals("all", StringComparison.OrdinalIgnoreCase))
        {
            rows = rows.Where(x => MapStatusForFilter(x.StatusRaw).Equals(normalizedStatus, StringComparison.OrdinalIgnoreCase)).ToList();
        }

        if (!string.IsNullOrWhiteSpace(keyword))
        {
            rows = rows.Where(x =>
                    x.OrderCode.ToLowerInvariant().Contains(keyword) ||
                    x.CustomerName.ToLowerInvariant().Contains(keyword) ||
                    x.CustomerPhone.ToLowerInvariant().Contains(keyword) ||
                    x.DeliveryAddress.ToLowerInvariant().Contains(keyword))
                .ToList();
        }

        rows = normalizedSort switch
        {
            "code_asc" => rows.OrderBy(x => x.OrderCode).ToList(),
            "code_desc" => rows.OrderByDescending(x => x.OrderCode).ToList(),
            "customer_asc" => rows.OrderBy(x => x.CustomerName).ToList(),
            "customer_desc" => rows.OrderByDescending(x => x.CustomerName).ToList(),
            "staff_asc" => rows.OrderBy(x => x.DeliveryStaffName).ToList(),
            "staff_desc" => rows.OrderByDescending(x => x.DeliveryStaffName).ToList(),
            "date_asc" => rows.OrderBy(x => x.ShippingDate).ToList(),
            "date_desc" => rows.OrderByDescending(x => x.ShippingDate).ToList(),
            "expected_asc" => rows.OrderBy(x => x.ExpectedDeliveryDate).ToList(),
            "expected_desc" => rows.OrderByDescending(x => x.ExpectedDeliveryDate).ToList(),
            "status_asc" => rows.OrderBy(x => x.StatusText).ToList(),
            "status_desc" => rows.OrderByDescending(x => x.StatusText).ToList(),
            _ => rows.OrderByDescending(x => x.ShippingDate).ToList()
        };

        var totalRecords = rows.Count;
        var totalPages = Math.Max(1, (int)Math.Ceiling(totalRecords / (double)pageSize));
        if (page > totalPages)
        {
            page = totalPages;
        }

        var pagedRows = rows
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .Select(x => new
            {
                x.OrderCode,
                x.CustomerName,
                x.CustomerPhone,
                x.DeliveryStaffName,
                x.DeliveryAddress,
                ShippingDateFormatted = x.ShippingDate.ToString("dd/MM/yyyy"),
                ExpectedDeliveryDateFormatted = x.ExpectedDeliveryDate.ToString("dd/MM/yyyy"),
                x.StatusBadgeClass,
                x.StatusText,
                x.ActualDeliveryDays
            })
            .ToList();

        var deliveredOrders = rows.Count(x => IsSuccessStatus(x.StatusRaw));
        var totalShippingFee = rows.Sum(x => x.ShippingFee);
        var averageDeliveryTime = rows.Where(x => x.ActualDeliveryDays.HasValue).Any()
            ? rows.Where(x => x.ActualDeliveryDays.HasValue).Average(x => x.ActualDeliveryDays!.Value)
            : 0d;
        var returnRate = rows.Count > 0 ? (decimal)rows.Count(x => IsCancelledStatus(x.StatusRaw)) / rows.Count * 100m : 0m;

        var staffPerformances = rows
            .GroupBy(x => x.DeliveryStaffName)
            .Select(g => new
            {
                StaffName = g.Key,
                TotalOrders = g.Count(),
                SuccessRate = g.Count() > 0 ? decimal.Round((decimal)g.Count(x => IsSuccessStatus(x.StatusRaw)) / g.Count() * 100m, 2) : 0m
            })
            .OrderByDescending(x => x.TotalOrders)
            .ToList();

        var distribution = new[]
        {
            new { TimeRange = "0-1 ngay", Count = rows.Count(x => x.ActualDeliveryDays.HasValue && x.ActualDeliveryDays <= 1) },
            new { TimeRange = "2-3 ngay", Count = rows.Count(x => x.ActualDeliveryDays.HasValue && x.ActualDeliveryDays >= 2 && x.ActualDeliveryDays <= 3) },
            new { TimeRange = "4-5 ngay", Count = rows.Count(x => x.ActualDeliveryDays.HasValue && x.ActualDeliveryDays >= 4 && x.ActualDeliveryDays <= 5) },
            new { TimeRange = ">5 ngay", Count = rows.Count(x => x.ActualDeliveryDays.HasValue && x.ActualDeliveryDays > 5) },
            new { TimeRange = "Dang giao", Count = rows.Count(x => !x.ActualDeliveryDays.HasValue) }
        }
        .Select(x => new { x.TimeRange, OrderCount = x.Count })
        .ToList();

        var deliveryStaffs = ShippingStaffNames
            .Select((name, index) => new
            {
                Value = (index + 1).ToString(CultureInfo.InvariantCulture),
                Text = name
            })
            .ToList();

        var model = new
        {
            FromDate = fromDate,
            ToDate = toDate,
            SelectedStaffId = staffId,
            Status = normalizedStatus,
            Query = q ?? string.Empty,
            Sort = normalizedSort,
            CurrentPage = page,
            PageSize = pageSize,
            TotalPages = totalPages,
            TotalRecords = totalRecords,
            DeliveredOrders = deliveredOrders,
            TotalShippingFee = totalShippingFee,
            AverageDeliveryTime = decimal.Round((decimal)averageDeliveryTime, 2),
            ReturnRate = decimal.Round(returnRate, 2),
            StaffPerformances = staffPerformances,
            DeliveryTimeDistributions = distribution,
            RecentShippings = pagedRows,
            DeliveryStaffs = deliveryStaffs
        };

        return Ok(model);
    }

    [HttpGet("shipping/export")]
    public async Task<IActionResult> ExportShipping(
        [FromQuery] DateTime? fromDate,
        [FromQuery] DateTime? toDate,
        [FromQuery] int? staffId,
        [FromQuery] string? status,
        [FromQuery] string? q,
        [FromQuery] string? sort,
        CancellationToken cancellationToken = default)
    {
        var normalizedSort = string.IsNullOrWhiteSpace(sort) ? "date_desc" : sort.Trim().ToLowerInvariant();
        var normalizedStatus = string.IsNullOrWhiteSpace(status) ? "all" : status.Trim().ToLowerInvariant();
        var keyword = string.IsNullOrWhiteSpace(q) ? string.Empty : q.Trim().ToLowerInvariant();

        var orders = await _db.Orders
            .AsNoTracking()
            .Where(o => !fromDate.HasValue || o.OrderDate >= fromDate.Value.Date)
            .Where(o => !toDate.HasValue || o.OrderDate <= toDate.Value.Date.AddDays(1).AddTicks(-1))
            .ToListAsync(cancellationToken);

        var orderIds = orders.Select(x => x.OrderId).ToHashSet();
        var shippingRows = await _db.Shippings
            .AsNoTracking()
            .Where(s => orderIds.Contains(s.OrderId))
            .ToListAsync(cancellationToken);

        var shippingLookup = shippingRows
            .GroupBy(s => s.OrderId)
            .ToDictionary(g => g.Key, g => g.OrderByDescending(x => x.ShippingId).First());

        var rows = orders
            .Select(order =>
            {
                shippingLookup.TryGetValue(order.OrderId, out var shipping);
                var staffIndex = order.OrderId % ShippingStaffNames.Length;
                var shippingDate = order.OrderDate;
                var expectedDate = order.OrderDate.AddDays(2);
                int? actualDays = IsSuccessStatus(order.Status) ? Math.Max(1, (order.OrderId % 4) + 1) : null;

                return new ShippingRow
                {
                    OrderId = order.OrderId,
                    OrderCode = BuildOrderCode(order.OrderId),
                    CustomerName = string.IsNullOrWhiteSpace(order.BuyerFullName) ? $"Khach {order.UserId}" : order.BuyerFullName,
                    CustomerPhone = string.IsNullOrWhiteSpace(order.BuyerPhone) ? (shipping?.Phone ?? "-") : order.BuyerPhone,
                    DeliveryStaffId = staffIndex + 1,
                    DeliveryStaffName = ShippingStaffNames[staffIndex],
                    DeliveryAddress = shipping?.AddressDetail ?? "Chua cap nhat",
                    ShippingDate = shippingDate,
                    ExpectedDeliveryDate = expectedDate,
                    StatusRaw = order.Status,
                    StatusText = MapStatusText(order.Status),
                    StatusBadgeClass = MapStatusBadgeClass(order.Status),
                    ActualDeliveryDays = actualDays,
                    ShippingFee = order.ShippingFee
                };
            })
            .ToList();

        if (staffId.HasValue)
        {
            rows = rows.Where(x => x.DeliveryStaffId == staffId.Value).ToList();
        }

        if (!normalizedStatus.Equals("all", StringComparison.OrdinalIgnoreCase))
        {
            rows = rows.Where(x => MapStatusForFilter(x.StatusRaw).Equals(normalizedStatus, StringComparison.OrdinalIgnoreCase)).ToList();
        }

        if (!string.IsNullOrWhiteSpace(keyword))
        {
            rows = rows.Where(x =>
                    x.OrderCode.ToLowerInvariant().Contains(keyword) ||
                    x.CustomerName.ToLowerInvariant().Contains(keyword) ||
                    x.CustomerPhone.ToLowerInvariant().Contains(keyword) ||
                    x.DeliveryAddress.ToLowerInvariant().Contains(keyword))
                .ToList();
        }

        rows = normalizedSort switch
        {
            "code_asc" => rows.OrderBy(x => x.OrderCode).ToList(),
            "code_desc" => rows.OrderByDescending(x => x.OrderCode).ToList(),
            "customer_asc" => rows.OrderBy(x => x.CustomerName).ToList(),
            "customer_desc" => rows.OrderByDescending(x => x.CustomerName).ToList(),
            "staff_asc" => rows.OrderBy(x => x.DeliveryStaffName).ToList(),
            "staff_desc" => rows.OrderByDescending(x => x.DeliveryStaffName).ToList(),
            "date_asc" => rows.OrderBy(x => x.ShippingDate).ToList(),
            "date_desc" => rows.OrderByDescending(x => x.ShippingDate).ToList(),
            "expected_asc" => rows.OrderBy(x => x.ExpectedDeliveryDate).ToList(),
            "expected_desc" => rows.OrderByDescending(x => x.ExpectedDeliveryDate).ToList(),
            "status_asc" => rows.OrderBy(x => x.StatusText).ToList(),
            "status_desc" => rows.OrderByDescending(x => x.StatusText).ToList(),
            _ => rows.OrderByDescending(x => x.ShippingDate).ToList()
        };

        var csvRows = rows.Select(x => new[]
        {
            x.OrderCode,
            x.CustomerName,
            x.CustomerPhone,
            x.DeliveryStaffName,
            x.DeliveryAddress,
            x.ShippingDate.ToString("dd/MM/yyyy"),
            x.ExpectedDeliveryDate.ToString("dd/MM/yyyy"),
            x.StatusText,
            x.ActualDeliveryDays?.ToString(CultureInfo.InvariantCulture) ?? string.Empty,
            x.ShippingFee.ToString("0.##", CultureInfo.InvariantCulture)
        });

        return BuildCsvFile(
            $"shipping-report-{DateTime.UtcNow:yyyyMMdd-HHmmss}.csv",
            new[]
            {
                "OrderCode", "CustomerName", "CustomerPhone", "DeliveryStaffName", "DeliveryAddress",
                "ShippingDate", "ExpectedDeliveryDate", "Status", "ActualDeliveryDays", "ShippingFee"
            },
            csvRows);
    }

    [HttpGet("reviews")]
    public async Task<IActionResult> Reviews(
        [FromQuery] DateTime? fromDate,
        [FromQuery] DateTime? toDate,
        [FromQuery] string? starRating,
        [FromQuery] int page = 1,
        [FromQuery] int pageSize = 10,
        CancellationToken cancellationToken = default)
    {
        if (page < 1)
        {
            page = 1;
        }

        if (pageSize <= 0)
        {
            pageSize = 10;
        }

        var reviews = await _db.Reviews
            .AsNoTracking()
            .Where(r => !r.IsDeleted)
            .Where(r => r.Rating > 0)
            .Where(r => !fromDate.HasValue || r.CreatedAt >= fromDate.Value.Date)
            .Where(r => !toDate.HasValue || r.CreatedAt <= toDate.Value.Date.AddDays(1).AddTicks(-1))
            .OrderByDescending(r => r.CreatedAt)
            .ToListAsync(cancellationToken);

        var userIds = reviews.Select(r => r.UserId).Distinct().ToList();
        var latestOrdersByUser = await _db.Orders
            .AsNoTracking()
            .Where(o => userIds.Contains(o.UserId))
            .GroupBy(o => o.UserId)
            .Select(g => g.OrderByDescending(x => x.OrderDate).First())
            .ToDictionaryAsync(x => x.UserId, cancellationToken);

        var normalizedStar = string.IsNullOrWhiteSpace(starRating) ? "all" : starRating.Trim().ToLowerInvariant();
        if (!normalizedStar.Equals("all", StringComparison.OrdinalIgnoreCase) && int.TryParse(normalizedStar, out var targetStar))
        {
            reviews = reviews.Where(r => r.Rating == targetStar).ToList();
        }

        var total = reviews.Count;
        var totalPages = Math.Max(1, (int)Math.Ceiling(total / (double)pageSize));
        if (page > totalPages)
        {
            page = totalPages;
        }

        var recent = reviews
            .OrderByDescending(r => r.CreatedAt)
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .Select(r => new
            {
                ReviewID = r.ReviewId,
                UserID = r.UserId,
                CustomerName = latestOrdersByUser.TryGetValue(r.UserId, out var userOrder) && !string.IsNullOrWhiteSpace(userOrder.BuyerFullName)
                    ? userOrder.BuyerFullName
                    : $"Khach {r.UserId}",
                ProductName = $"San pham #{r.ProductId}",
                ProductImageFileName = string.Empty,
                Rating = r.Rating,
                StarDisplay = BuildStarDisplay(r.Rating),
                Comment = r.Comment ?? string.Empty,
                CreatedAt = r.CreatedAt,
                CreatedAtFormatted = r.CreatedAt.ToString("dd/MM/yyyy HH:mm")
            })
            .ToList();

        var averageRating = total > 0 ? reviews.Average(r => r.Rating) : 0d;
        var positiveReviews = reviews.Count(r => r.Rating >= 4);
        var negativeReviews = reviews.Count(r => r.Rating <= 2);
        var positivePercentage = total > 0 ? (decimal)positiveReviews / total * 100m : 0m;
        var negativePercentage = total > 0 ? (decimal)negativeReviews / total * 100m : 0m;

        var starDistributions = Enumerable.Range(1, 5)
            .Select(star => new
            {
                Label = $"{star} sao",
                Count = reviews.Count(r => r.Rating == star)
            })
            .OrderByDescending(x => x.Label)
            .ToList();

        var topTopics = reviews
            .SelectMany(r => (r.Comment ?? string.Empty)
                .Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                .Select(x => x.Trim().ToLowerInvariant()))
            .Where(x => x.Length >= 3)
            .GroupBy(x => x)
            .OrderByDescending(g => g.Count())
            .ThenBy(g => g.Key)
            .Take(10)
            .Select(g => g.Key)
            .ToList();

        var model = new
        {
            FromDate = fromDate,
            ToDate = toDate,
            StarRating = normalizedStar,
            TotalReviews = total,
            AverageRating = decimal.Round((decimal)averageRating, 1),
            PositiveReviews = positiveReviews,
            PositivePercentage = decimal.Round(positivePercentage, 1),
            NegativeReviews = negativeReviews,
            NegativePercentage = decimal.Round(negativePercentage, 1),
            StarDistributions = starDistributions,
            TopMentionedTopics = topTopics,
            RecentReviews = recent,
            Page = page,
            PageSize = pageSize,
            TotalPages = totalPages,
            Total = total
        };

        return Ok(model);
    }

    [HttpGet("reviews/export")]
    public async Task<IActionResult> ExportReviews(
        [FromQuery] DateTime? fromDate,
        [FromQuery] DateTime? toDate,
        [FromQuery] string? starRating,
        CancellationToken cancellationToken = default)
    {
        var reviews = await _db.Reviews
            .AsNoTracking()
            .Where(r => !r.IsDeleted)
            .Where(r => r.Rating > 0)
            .Where(r => !fromDate.HasValue || r.CreatedAt >= fromDate.Value.Date)
            .Where(r => !toDate.HasValue || r.CreatedAt <= toDate.Value.Date.AddDays(1).AddTicks(-1))
            .OrderByDescending(r => r.CreatedAt)
            .ToListAsync(cancellationToken);

        var userIds = reviews.Select(r => r.UserId).Distinct().ToList();
        var latestOrdersByUser = await _db.Orders
            .AsNoTracking()
            .Where(o => userIds.Contains(o.UserId))
            .GroupBy(o => o.UserId)
            .Select(g => g.OrderByDescending(x => x.OrderDate).First())
            .ToDictionaryAsync(x => x.UserId, cancellationToken);

        var normalizedStar = string.IsNullOrWhiteSpace(starRating) ? "all" : starRating.Trim().ToLowerInvariant();
        if (!normalizedStar.Equals("all", StringComparison.OrdinalIgnoreCase) && int.TryParse(normalizedStar, out var targetStar))
        {
            reviews = reviews.Where(r => r.Rating == targetStar).ToList();
        }

        var csvRows = reviews
            .OrderByDescending(r => r.CreatedAt)
            .Select(r => new[]
            {
                r.ReviewId.ToString(CultureInfo.InvariantCulture),
                r.UserId.ToString(CultureInfo.InvariantCulture),
                latestOrdersByUser.TryGetValue(r.UserId, out var userOrder) && !string.IsNullOrWhiteSpace(userOrder.BuyerFullName)
                    ? userOrder.BuyerFullName
                    : $"Khach {r.UserId}",
                $"San pham #{r.ProductId}",
                r.Rating.ToString(CultureInfo.InvariantCulture),
                r.Comment ?? string.Empty,
                r.CreatedAt.ToString("dd/MM/yyyy HH:mm")
            });

        return BuildCsvFile(
            $"review-report-{DateTime.UtcNow:yyyyMMdd-HHmmss}.csv",
            new[] { "ReviewID", "UserID", "CustomerName", "ProductName", "Rating", "Comment", "CreatedAt" },
            csvRows);
    }

    [HttpDelete("reviews/{reviewId:int}")]
    public async Task<IActionResult> DeleteReview([FromRoute] int reviewId, CancellationToken cancellationToken = default)
    {
        if (reviewId <= 0)
        {
            return BadRequest(new { message = "ID danh gia khong hop le." });
        }

        var review = await _db.Reviews.FirstOrDefaultAsync(x => x.ReviewId == reviewId, cancellationToken);
        if (review is null)
        {
            return NotFound(new { message = "Khong tim thay danh gia." });
        }

        review.IsDeleted = true;
        review.DeletedAt = DateTime.UtcNow;
        review.UpdatedAt = DateTime.UtcNow;
        await _db.SaveChangesAsync(cancellationToken);

        return Ok(new { success = true, message = "Da xoa danh gia thanh cong." });
    }

    private static string BuildOrderCode(int orderId)
        => $"ORD-{orderId:D6}";

    private static string MapOrderStatus(string? rawStatus)
    {
        if (string.IsNullOrWhiteSpace(rawStatus))
        {
            return "Pending";
        }

        var status = rawStatus.Trim().ToLowerInvariant();
        if (status.Contains("cancel"))
        {
            return "Cancelled";
        }

        if (status.Contains("deliver") || status.Contains("complete") || status.Contains("success"))
        {
            return "Completed";
        }

        if (status.Contains("ship") || status.Contains("process") || status.Contains("ready"))
        {
            return "Shipped";
        }

        return "Pending";
    }

    private static bool IsSuccessStatus(string? status)
        => MapOrderStatus(status).Equals("Completed", StringComparison.OrdinalIgnoreCase);

    private static bool IsCancelledStatus(string? status)
        => MapOrderStatus(status).Equals("Cancelled", StringComparison.OrdinalIgnoreCase);

    private static bool IsShippingStatus(string? status)
        => MapOrderStatus(status).Equals("Shipped", StringComparison.OrdinalIgnoreCase);

    private static bool IsPendingStatus(string? status)
        => MapOrderStatus(status).Equals("Pending", StringComparison.OrdinalIgnoreCase);

    private static string MapStatusText(string? rawStatus)
    {
        return MapOrderStatus(rawStatus) switch
        {
            "Completed" => "Thanh cong",
            "Cancelled" => "Da huy",
            "Shipped" => "Dang giao",
            _ => "Cho xu ly"
        };
    }

    private static string MapStatusBadgeClass(string? rawStatus)
    {
        return MapOrderStatus(rawStatus) switch
        {
            "Completed" => "bg-success",
            "Cancelled" => "bg-danger",
            "Shipped" => "bg-info",
            _ => "bg-warning text-dark"
        };
    }

    private static string MapStatusForFilter(string? rawStatus)
    {
        return MapOrderStatus(rawStatus).ToLowerInvariant() switch
        {
            "completed" => "delivered",
            "cancelled" => "canceled",
            "shipped" => "shipped",
            _ => "pending"
        };
    }

    private static string GetCategoryName(int productId)
    {
        switch (productId % 6)
        {
            case 0:
                return "Rau lá";
            case 1:
                return "Rau ăn quả";
            case 2:
                return "Củ & rễ";
            case 3:
                return "Nấm";
            case 4:
                return "Rau thơm & gia vị";
            default:
                return "Rau ăn hoa / thân / mầm";
        }
    }

    private static bool CategoryEquals(string left, string right)
    {
        return string.Equals(NormalizeCategoryKey(left), NormalizeCategoryKey(right), StringComparison.Ordinal);
    }

    private static string NormalizeCategoryKey(string value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return string.Empty;
        }

        var normalized = value.Trim().Normalize(NormalizationForm.FormD);
        var builder = new StringBuilder(normalized.Length);
        foreach (var ch in normalized)
        {
            if (CharUnicodeInfo.GetUnicodeCategory(ch) != UnicodeCategory.NonSpacingMark)
            {
                builder.Append(ch);
            }
        }

        return builder
            .ToString()
            .Normalize(NormalizationForm.FormC)
            .Replace('đ', 'd')
            .Replace('Đ', 'D')
            .ToLowerInvariant();
    }

    private static int GetPseudoStock(int productId)
        => (productId * 7) % 120;

    private static List<OrderDetail> FilterReportableDetails(List<OrderDetail> details)
        => details.Where(IsReportableDetail).ToList();

    private static bool IsReportableDetail(OrderDetail detail)
    {
        if (detail is null)
        {
            return false;
        }

        return detail.ProductId > 0
               && detail.ProductId < 100_000_000
               && detail.Quantity > 0
               && detail.Quantity <= 10_000
               && detail.UnitPrice > 0m;
    }

    private static int ToSafeNonNegativeInt(long value)
    {
        if (value <= 0)
        {
            return 0;
        }

        return value > int.MaxValue
            ? int.MaxValue
            : (int)value;
    }

    private static (string Label, long Sort) GetGroupKey(DateTime date, string viewBy)
    {
        return viewBy switch
        {
            "week" => (GetWeekLabel(date), date.Date.AddDays(-(int)date.DayOfWeek).Ticks),
            "month" => ($"{date:MM/yyyy}", new DateTime(date.Year, date.Month, 1).Ticks),
            "year" => ($"{date:yyyy}", new DateTime(date.Year, 1, 1).Ticks),
            _ => ($"{date:dd/MM}", date.Date.Ticks)
        };
    }

    private static string GetWeekLabel(DateTime date)
    {
        var cal = CultureInfo.InvariantCulture.Calendar;
        var week = cal.GetWeekOfYear(date, CalendarWeekRule.FirstFourDayWeek, DayOfWeek.Monday);
        return $"Tuan {week}";
    }

    private static string BuildStarDisplay(int rating)
    {
        var stars = new List<string>();
        for (var i = 1; i <= 5; i++)
        {
            stars.Add(i <= rating ? "<i class='bi bi-star-fill'></i>" : "<i class='bi bi-star'></i>");
        }

        return string.Join(string.Empty, stars);
    }

    private IActionResult BuildCsvFile(string fileName, IReadOnlyList<string> headers, IEnumerable<string[]> rows)
    {
        var builder = new StringBuilder();
        builder.AppendLine(string.Join(",", headers.Select(EscapeCsv)));
        foreach (var row in rows)
        {
            builder.AppendLine(string.Join(",", row.Select(EscapeCsv)));
        }

        var payload = Encoding.UTF8.GetPreamble()
            .Concat(Encoding.UTF8.GetBytes(builder.ToString()))
            .ToArray();

        return File(payload, "text/csv; charset=utf-8", fileName);
    }

    private static string EscapeCsv(string? value)
    {
        if (string.IsNullOrEmpty(value))
        {
            return string.Empty;
        }

        var normalized = value.Replace("\"", "\"\"");
        return normalized.Contains(',') || normalized.Contains('"') || normalized.Contains('\n')
            ? $"\"{normalized}\""
            : normalized;
    }

    private sealed class ShippingRow
    {
        public int OrderId { get; set; }

        public string OrderCode { get; set; } = string.Empty;

        public string CustomerName { get; set; } = string.Empty;

        public string CustomerPhone { get; set; } = string.Empty;

        public int DeliveryStaffId { get; set; }

        public string DeliveryStaffName { get; set; } = string.Empty;

        public string DeliveryAddress { get; set; } = string.Empty;

        public DateTime ShippingDate { get; set; }

        public DateTime ExpectedDeliveryDate { get; set; }

        public string? StatusRaw { get; set; }

        public string StatusText { get; set; } = string.Empty;

        public string StatusBadgeClass { get; set; } = string.Empty;

        public int? ActualDeliveryDays { get; set; }

        public decimal ShippingFee { get; set; }
    }

}
