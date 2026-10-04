using FreshFarm.Web.Bff.Areas.Seller.Infrastructure;
using FreshFarm.Web.Bff.Areas.Seller.Models;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Rendering;
using System.Globalization;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;

namespace FreshFarm.Web.Bff.Areas.Admin.Controllers;

[Authorize(Policy = "AdminOnly")]
[ResponseCache(Duration = 0, Location = ResponseCacheLocation.None, NoStore = true)]
[Area("Admin")]
public sealed class ReportController : LegacySellerControllerBase
{
    private const string AccessTokenSessionKey = "ACCESS_TOKEN";

    private readonly IHttpClientFactory _httpClientFactory;

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true
    };

    public ReportController(IHttpClientFactory httpClientFactory)
    {
        _httpClientFactory = httpClientFactory;
    }

    [HttpGet]
    public async Task<IActionResult> Customer(DateTime? registerFromDate, DateTime? registerToDate, string? customerSegment)
    {
        var query = BuildQuery(new Dictionary<string, string?>
        {
            ["registerFromDate"] = registerFromDate?.ToString("yyyy-MM-dd"),
            ["registerToDate"] = registerToDate?.ToString("yyyy-MM-dd"),
            ["customerSegment"] = customerSegment
        });

        var model = await GetFromOrderingAsync<CustomerReportViewModel>($"/api/orders/admin/reports/customers{query}");
        NormalizeCustomerReport(model);
        model.RegisterFromDate = registerFromDate;
        model.RegisterToDate = registerToDate;
        model.CustomerSegment = string.IsNullOrWhiteSpace(model.CustomerSegment) ? "all" : model.CustomerSegment;

        foreach (var customer in model.TopCustomers)
        {
            customer.AvatarUrl = Url?.Action("AvatarById", "Account", new { area = "", id = customer.UserID }) ?? string.Empty;
        }

        return RenderReportView("~/Areas/Seller/Views/Report/Customer.cshtml", model);
    }

    [HttpGet]
    public async Task<IActionResult> Order(DateTime? fromDate, DateTime? toDate, string? orderStatus)
    {
        var query = BuildQuery(new Dictionary<string, string?>
        {
            ["fromDate"] = fromDate?.ToString("yyyy-MM-dd"),
            ["toDate"] = toDate?.ToString("yyyy-MM-dd"),
            ["orderStatus"] = orderStatus
        });

        var model = await GetFromOrderingAsync<OrderReportViewModel>($"/api/orders/admin/reports/orders{query}");
        NormalizeOrderReport(model);
        model.FromDate = fromDate;
        model.ToDate = toDate;
        model.OrderStatus = string.IsNullOrWhiteSpace(model.OrderStatus) ? "all" : model.OrderStatus;
        return RenderReportView("~/Areas/Seller/Views/Report/Order.cshtml", model);
    }

    [HttpGet]
    public async Task<IActionResult> Revenue(DateTime? fromDate, DateTime? toDate, string? viewBy = "day")
    {
        var query = BuildQuery(new Dictionary<string, string?>
        {
            ["fromDate"] = fromDate?.ToString("yyyy-MM-dd"),
            ["toDate"] = toDate?.ToString("yyyy-MM-dd"),
            ["viewBy"] = viewBy
        });

        var model = await GetFromOrderingAsync<RevenueReportViewModel>($"/api/orders/admin/reports/revenue{query}");
        NormalizeRevenueReport(model);
        model.FromDate = fromDate;
        model.ToDate = toDate;
        model.ViewBy = string.IsNullOrWhiteSpace(model.ViewBy) ? "day" : model.ViewBy;
        return RenderReportView("~/Areas/Seller/Views/Report/Revenue.cshtml", model);
    }

    [HttpGet]
    public async Task<IActionResult> Product(DateTime? fromDate, DateTime? toDate, string? productCategory, int page = 1)
    {
        var query = BuildQuery(new Dictionary<string, string?>
        {
            ["fromDate"] = fromDate?.ToString("yyyy-MM-dd"),
            ["toDate"] = toDate?.ToString("yyyy-MM-dd"),
            ["productCategory"] = productCategory,
            ["page"] = page.ToString()
        });

        var model = await GetFromOrderingAsync<ProductReportViewModel>($"/api/orders/admin/reports/products{query}");
        NormalizeProductReport(model);
        model.FromDate = fromDate;
        model.ToDate = toDate;
        model.ProductCategory = string.IsNullOrWhiteSpace(model.ProductCategory) ? "all" : model.ProductCategory;
        model.CurrentPage = model.CurrentPage <= 0 ? 1 : model.CurrentPage;
        model.TotalPages = model.TotalPages <= 0 ? 1 : model.TotalPages;
        return RenderReportView("~/Areas/Seller/Views/Report/Product.cshtml", model);
    }

    [HttpGet]
    public async Task<IActionResult> Shipping(
        DateTime? fromDate,
        DateTime? toDate,
        int? staffId,
        string? status,
        string? q,
        string? sort,
        int page = 1,
        int pageSize = 10)
    {
        var query = BuildQuery(new Dictionary<string, string?>
        {
            ["fromDate"] = fromDate?.ToString("yyyy-MM-dd"),
            ["toDate"] = toDate?.ToString("yyyy-MM-dd"),
            ["staffId"] = staffId?.ToString(),
            ["status"] = status,
            ["q"] = q,
            ["sort"] = sort,
            ["page"] = page.ToString(),
            ["pageSize"] = pageSize.ToString()
        });

        var model = await GetFromOrderingAsync<ShippingReportViewModel>($"/api/orders/admin/reports/shipping{query}");
        NormalizeShippingReport(model);
        model.FromDate = fromDate;
        model.ToDate = toDate;
        model.SelectedStaffId = staffId;
        model.Status = string.IsNullOrWhiteSpace(model.Status) ? "all" : model.Status;
        model.Query = q ?? string.Empty;
        model.Sort = string.IsNullOrWhiteSpace(model.Sort) ? "date_desc" : model.Sort;
        model.CurrentPage = model.CurrentPage <= 0 ? 1 : model.CurrentPage;
        model.PageSize = model.PageSize <= 0 ? 10 : model.PageSize;
        model.TotalPages = model.TotalPages <= 0 ? 1 : model.TotalPages;

        ViewBag.DeliveryStaffs = model.DeliveryStaffs
            .Select(x => new SelectListItem { Value = x.Value, Text = x.Text })
            .ToList();

        return RenderReportView("~/Areas/Seller/Views/Report/Shipping.cshtml", model);
    }

    [HttpGet]
    public async Task<IActionResult> Review(DateTime? fromDate, DateTime? toDate, string? starRating, int page = 1, int pageSize = 10)
    {
        var query = BuildQuery(new Dictionary<string, string?>
        {
            ["fromDate"] = fromDate?.ToString("yyyy-MM-dd"),
            ["toDate"] = toDate?.ToString("yyyy-MM-dd"),
            ["starRating"] = starRating,
            ["page"] = page.ToString(),
            ["pageSize"] = pageSize.ToString()
        });

        var model = await GetFromOrderingAsync<ReviewReportViewModel>($"/api/orders/admin/reports/reviews{query}");
        NormalizeReviewReport(model);
        model.FromDate = fromDate;
        model.ToDate = toDate;
        model.StarRating = string.IsNullOrWhiteSpace(model.StarRating) ? "all" : model.StarRating;
        model.Page = model.Page <= 0 ? 1 : model.Page;
        model.PageSize = model.PageSize <= 0 ? 10 : model.PageSize;
        model.TotalPages = model.TotalPages <= 0 ? 1 : model.TotalPages;

        ViewBag.Page = model.Page;
        ViewBag.PageSize = model.PageSize;
        ViewBag.TotalPages = model.TotalPages;
        ViewBag.Total = model.Total;

        return RenderReportView("~/Areas/Seller/Views/Report/Review.cshtml", model);
    }

    [HttpGet]
    public Task<IActionResult> ExportCustomerExcel(DateTime? registerFromDate, DateTime? registerToDate, string? customerSegment)
        => ProxyExportAsync(
            BuildQueryPath("/api/orders/admin/reports/customers/export", new Dictionary<string, string?>
            {
                ["registerFromDate"] = registerFromDate?.ToString("yyyy-MM-dd"),
                ["registerToDate"] = registerToDate?.ToString("yyyy-MM-dd"),
                ["customerSegment"] = customerSegment
            }),
            nameof(Customer),
            new { registerFromDate, registerToDate, customerSegment });

    [HttpGet]
    public Task<IActionResult> ExportOrderExcel(DateTime? fromDate, DateTime? toDate, string? orderStatus)
        => ProxyExportAsync(
            BuildQueryPath("/api/orders/admin/reports/orders/export", new Dictionary<string, string?>
            {
                ["fromDate"] = fromDate?.ToString("yyyy-MM-dd"),
                ["toDate"] = toDate?.ToString("yyyy-MM-dd"),
                ["orderStatus"] = orderStatus
            }),
            nameof(Order),
            new { fromDate, toDate, orderStatus });

    [HttpGet]
    public Task<IActionResult> ExportRevenueExcel(DateTime? fromDate, DateTime? toDate, string? viewBy)
        => ProxyExportAsync(
            BuildQueryPath("/api/orders/admin/reports/revenue/export", new Dictionary<string, string?>
            {
                ["fromDate"] = fromDate?.ToString("yyyy-MM-dd"),
                ["toDate"] = toDate?.ToString("yyyy-MM-dd"),
                ["viewBy"] = viewBy
            }),
            nameof(Revenue),
            new { fromDate, toDate, viewBy });

    [HttpGet]
    public Task<IActionResult> ExportProductExcel(DateTime? fromDate, DateTime? toDate, string? productCategory)
        => ProxyExportAsync(
            BuildQueryPath("/api/orders/admin/reports/products/export", new Dictionary<string, string?>
            {
                ["fromDate"] = fromDate?.ToString("yyyy-MM-dd"),
                ["toDate"] = toDate?.ToString("yyyy-MM-dd"),
                ["productCategory"] = productCategory
            }),
            nameof(Product),
            new { fromDate, toDate, productCategory });

    [HttpGet]
    public Task<IActionResult> ExportShippingExcel(DateTime? fromDate, DateTime? toDate, int? staffId, string? status, string? q)
        => ProxyExportAsync(
            BuildQueryPath("/api/orders/admin/reports/shipping/export", new Dictionary<string, string?>
            {
                ["fromDate"] = fromDate?.ToString("yyyy-MM-dd"),
                ["toDate"] = toDate?.ToString("yyyy-MM-dd"),
                ["staffId"] = staffId?.ToString(),
                ["status"] = status,
                ["q"] = q
            }),
            nameof(Shipping),
            new { fromDate, toDate, staffId, status, q });

    [HttpGet]
    public Task<IActionResult> ExportReviewExcel(DateTime? fromDate, DateTime? toDate, string? starRating)
        => ProxyExportAsync(
            BuildQueryPath("/api/orders/admin/reports/reviews/export", new Dictionary<string, string?>
            {
                ["fromDate"] = fromDate?.ToString("yyyy-MM-dd"),
                ["toDate"] = toDate?.ToString("yyyy-MM-dd"),
                ["starRating"] = starRating
            }),
            nameof(Review),
            new { fromDate, toDate, starRating });

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<JsonResult> DeleteReview(int id)
    {
        if (id <= 0)
        {
            return Json(new { success = false, message = "ID danh gia khong hop le." });
        }

        try
        {
            var client = CreateAuthorizedClient();
            var response = await client.DeleteAsync($"/api/orders/admin/reports/reviews/{id}");
            if (!response.IsSuccessStatusCode)
            {
                return Json(new { success = false, message = await ReadApiErrorAsync(response, "Khong the xoa danh gia") });
            }

            var payload = await response.Content.ReadFromJsonAsync<BasicApiResponse>(JsonOptions);
            return Json(new { success = true, message = payload?.Message ?? "Da xoa danh gia." });
        }
        catch (Exception ex)
        {
            return Json(new { success = false, message = "Loi: " + ex.Message });
        }
    }

    private IActionResult RenderReportView<TModel>(string viewPath, TModel model)
    {
        ViewData["AreaName"] = "Admin";
        ViewData["LayoutPath"] = "~/Areas/Admin/Views/Shared/_LayoutAdmin.cshtml";
        return View(viewPath, model);
    }

    private async Task<IActionResult> ProxyExportAsync(string path, string fallbackAction, object fallbackRouteValues)
    {
        try
        {
            var client = CreateAuthorizedClient();
            var response = await client.GetAsync(path);
            if (!response.IsSuccessStatusCode)
            {
                TempData["ErrorMessage"] = await ReadApiErrorAsync(response, "Khong the xuat bao cao");
                return RedirectToAction(fallbackAction, fallbackRouteValues);
            }

            var bytes = await response.Content.ReadAsByteArrayAsync();
            if (bytes.Length == 0)
            {
                TempData["ErrorMessage"] = "Khong co du lieu de xuat.";
                return RedirectToAction(fallbackAction, fallbackRouteValues);
            }

            var contentType = response.Content.Headers.ContentType?.ToString() ?? "text/csv";
            var fileName = response.Content.Headers.ContentDisposition?.FileNameStar
                           ?? response.Content.Headers.ContentDisposition?.FileName
                           ?? $"{fallbackAction.ToLowerInvariant()}-report.csv";

            return File(bytes, contentType, fileName.Trim('"'));
        }
        catch (Exception ex)
        {
            TempData["ErrorMessage"] = "Khong the xuat bao cao: " + ex.Message;
            return RedirectToAction(fallbackAction, fallbackRouteValues);
        }
    }

    private async Task<T> GetFromOrderingAsync<T>(string path) where T : new()
    {
        try
        {
            var client = CreateAuthorizedClient();
            var response = await client.GetAsync(path);
            if (!response.IsSuccessStatusCode)
            {
                ViewBag.ErrorMessage = $"Khong the tai du lieu ({(int)response.StatusCode}).";
                return new T();
            }

            return await response.Content.ReadFromJsonAsync<T>(JsonOptions) ?? new T();
        }
        catch (Exception ex)
        {
            ViewBag.ErrorMessage = ex.Message;
            return new T();
        }
    }

    private HttpClient CreateAuthorizedClient()
    {
        var client = _httpClientFactory.CreateClient("Ordering");

        client.DefaultRequestHeaders.Remove("Authorization");
        var token = GetAccessToken(AccessTokenSessionKey);
        if (!string.IsNullOrWhiteSpace(token))
        {
            client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);
        }

        return client;
    }

    private static string BuildQueryPath(string path, IDictionary<string, string?> parameters)
        => path + BuildQuery(parameters);

    private static void NormalizeCustomerReport(CustomerReportViewModel model)
    {
        model.TopCustomers = model.TopCustomers
            .Where(customer => customer.UserID > 0)
            .GroupBy(customer => customer.UserID)
            .Select(group => group
                .OrderByDescending(CalculateTopCustomerScore)
                .ThenByDescending(CalculateTopCustomerSignalLength)
                .First())
            .ToList();
    }

    private static void NormalizeOrderReport(OrderReportViewModel model)
    {
        model.RecentOrders = model.RecentOrders
            .Where(order => order.OrderID > 0)
            .GroupBy(order => order.OrderID)
            .Select(group => group
                .OrderByDescending(CalculateRecentOrderScore)
                .ThenByDescending(CalculateRecentOrderSignalLength)
                .ThenByDescending(order => order.OrderDate)
                .First())
            .ToList();
    }

    private static void NormalizeRevenueReport(RevenueReportViewModel model)
    {
        model.TopProducts = model.TopProducts
            .Where(product => HasMeaningfulValue(product.ProductName))
            .GroupBy(product => product.ProductName, StringComparer.OrdinalIgnoreCase)
            .Select(group => group
                .OrderByDescending(product => product.TotalRevenue)
                .ThenByDescending(product => product.ProductName.Length)
                .First())
            .ToList();

        model.DailyRevenues = model.DailyRevenues
            .GroupBy(day => day.Date.Date)
            .Select(group => group
                .OrderByDescending(CalculateDailyRevenueScore)
                .ThenByDescending(CalculateDailyRevenueSignalLength)
                .First())
            .ToList();
    }

    private static void NormalizeProductReport(ProductReportViewModel model)
    {
        model.TopSellingProducts = model.TopSellingProducts
            .Where(product => HasMeaningfulValue(product.ProductName))
            .GroupBy(product => product.ProductName, StringComparer.OrdinalIgnoreCase)
            .Select(group => group
                .OrderByDescending(product => product.QuantitySold)
                .ThenByDescending(product => product.ProductName.Length)
                .First())
            .ToList();

        model.CategoryRevenues = model.CategoryRevenues
            .Where(category => HasMeaningfulValue(category.CategoryName))
            .GroupBy(category => category.CategoryName, StringComparer.OrdinalIgnoreCase)
            .Select(group => group
                .OrderByDescending(category => category.Revenue)
                .ThenByDescending(category => category.CategoryName.Length)
                .First())
            .ToList();

        model.ProductPerformances = model.ProductPerformances
            .Where(product => HasMeaningfulValue(product.Sku) || HasMeaningfulValue(product.ProductName))
            .GroupBy(product => HasMeaningfulValue(product.ProductName) ? product.ProductName : product.Sku, StringComparer.OrdinalIgnoreCase)
            .Select(group => group
                .OrderByDescending(CalculateProductPerformanceScore)
                .ThenByDescending(CalculateProductPerformanceSignalLength)
                .First())
            .ToList();
    }

    private static void NormalizeShippingReport(ShippingReportViewModel model)
    {
        model.StaffPerformances = model.StaffPerformances
            .Where(staff => HasMeaningfulValue(staff.StaffName))
            .GroupBy(staff => staff.StaffName, StringComparer.OrdinalIgnoreCase)
            .Select(group => group
                .OrderByDescending(CalculateStaffPerformanceScore)
                .ThenByDescending(CalculateStaffPerformanceSignalLength)
                .First())
            .ToList();

        model.DeliveryTimeDistributions = model.DeliveryTimeDistributions
            .Where(item => HasMeaningfulValue(item.TimeRange))
            .GroupBy(item => item.TimeRange, StringComparer.OrdinalIgnoreCase)
            .Select(group => group
                .OrderByDescending(item => item.OrderCount)
                .ThenByDescending(item => item.TimeRange.Length)
                .First())
            .ToList();

        model.RecentShippings = model.RecentShippings
            .Where(item => HasMeaningfulValue(item.OrderCode))
            .GroupBy(item => item.OrderCode, StringComparer.OrdinalIgnoreCase)
            .Select(group => group
                .OrderByDescending(CalculateRecentShippingScore)
                .ThenByDescending(CalculateRecentShippingSignalLength)
                .First())
            .ToList();

        model.DeliveryStaffs = model.DeliveryStaffs
            .Where(item => HasMeaningfulValue(item.Value))
            .GroupBy(item => item.Value, StringComparer.OrdinalIgnoreCase)
            .Select(group => group
                .OrderByDescending(item => HasMeaningfulValue(item.Text))
                .ThenByDescending(item => item.Text?.Length ?? 0)
                .First())
            .ToList();
    }

    private static void NormalizeReviewReport(ReviewReportViewModel model)
    {
        model.StarDistributions = model.StarDistributions
            .Where(item => HasMeaningfulValue(item.Label))
            .GroupBy(item => item.Label, StringComparer.OrdinalIgnoreCase)
            .Select(group => group
                .OrderByDescending(item => item.Count)
                .ThenByDescending(item => item.Label.Length)
                .First())
            .ToList();

        model.TopMentionedTopics = model.TopMentionedTopics
            .Where(HasMeaningfulValue)
            .GroupBy(NormalizeTopicKey, StringComparer.Ordinal)
            .Select(group => group
                .OrderByDescending(topic => topic.Trim().Length)
                .First())
            .ToList()!;

        model.RecentReviews = model.RecentReviews
            .Where(item => item.ReviewID > 0)
            .GroupBy(item => item.ReviewID)
            .Select(group => group
                .OrderByDescending(CalculateRecentReviewScore)
                .ThenByDescending(CalculateRecentReviewSignalLength)
                .ThenByDescending(item => item.CreatedAt)
                .First())
            .ToList();
    }

    private static int CalculateTopCustomerScore(TopCustomerViewModel customer)
    {
        var score = 0;
        score += HasMeaningfulValue(customer.FullName) ? 2 : 0;
        score += customer.TotalOrders > 0 ? 1 : 0;
        score += customer.TotalSpent > 0 ? 1 : 0;
        return score;
    }

    private static int CalculateTopCustomerSignalLength(TopCustomerViewModel customer)
        => (customer.FullName?.Length ?? 0);

    private static int CalculateRecentOrderScore(OrderRecentItemViewModel order)
    {
        var score = 0;
        score += HasMeaningfulValue(order.OrderCode) ? 1 : 0;
        score += HasMeaningfulValue(order.CustomerName) ? 2 : 0;
        score += order.TotalAmount > 0 ? 1 : 0;
        score += HasMeaningfulValue(order.OrderDateFormatted) ? 1 : 0;
        score += HasMeaningfulValue(order.StatusText) ? 1 : 0;
        return score;
    }

    private static int CalculateRecentOrderSignalLength(OrderRecentItemViewModel order)
        => (order.OrderCode?.Length ?? 0)
        + (order.CustomerName?.Length ?? 0)
        + (order.OrderDateFormatted?.Length ?? 0)
        + (order.StatusText?.Length ?? 0);

    private static int CalculateDailyRevenueScore(RevenueDailyViewModel item)
        => (HasMeaningfulValue(item.DateFormatted) ? 1 : 0)
        + (item.TotalOrders > 0 ? 1 : 0)
        + (item.TotalProducts > 0 ? 1 : 0)
        + (item.Revenue > 0 ? 1 : 0)
        + (item.Profit > 0 ? 1 : 0);

    private static int CalculateDailyRevenueSignalLength(RevenueDailyViewModel item)
        => item.DateFormatted?.Length ?? 0;

    private static int CalculateProductPerformanceScore(ProductPerformanceViewModel product)
    {
        var score = 0;
        score += HasMeaningfulValue(product.ImageFileName) ? 1 : 0;
        score += HasMeaningfulValue(product.ProductName) ? 2 : 0;
        score += HasMeaningfulValue(product.Sku) ? 2 : 0;
        score += HasMeaningfulValue(product.CategoryName) ? 1 : 0;
        score += product.QuantitySold > 0 ? 1 : 0;
        score += HasMeaningfulValue(product.StockStatus) ? 1 : 0;
        score += product.TotalRevenue > 0 ? 1 : 0;
        return score;
    }

    private static int CalculateProductPerformanceSignalLength(ProductPerformanceViewModel product)
        => (product.ImageFileName?.Length ?? 0)
        + (product.ProductName?.Length ?? 0)
        + (product.Sku?.Length ?? 0)
        + (product.CategoryName?.Length ?? 0)
        + (product.StockStatus?.Length ?? 0);

    private static int CalculateStaffPerformanceScore(ShippingStaffPerformanceViewModel item)
        => (HasMeaningfulValue(item.StaffName) ? 2 : 0)
        + (item.TotalOrders > 0 ? 1 : 0)
        + (item.SuccessRate > 0 ? 1 : 0);

    private static int CalculateStaffPerformanceSignalLength(ShippingStaffPerformanceViewModel item)
        => item.StaffName?.Length ?? 0;

    private static int CalculateRecentShippingScore(ShippingRecentItemViewModel item)
    {
        var score = 0;
        score += HasMeaningfulValue(item.OrderCode) ? 1 : 0;
        score += HasMeaningfulValue(item.CustomerName) ? 2 : 0;
        score += HasMeaningfulValue(item.CustomerPhone) ? 1 : 0;
        score += HasMeaningfulValue(item.DeliveryStaffName) ? 1 : 0;
        score += HasMeaningfulValue(item.DeliveryAddress) ? 1 : 0;
        score += HasMeaningfulValue(item.ShippingDateFormatted) ? 1 : 0;
        score += HasMeaningfulValue(item.ExpectedDeliveryDateFormatted) ? 1 : 0;
        score += HasMeaningfulValue(item.StatusText) ? 1 : 0;
        score += item.ActualDeliveryDays.HasValue ? 1 : 0;
        return score;
    }

    private static int CalculateRecentShippingSignalLength(ShippingRecentItemViewModel item)
        => (item.OrderCode?.Length ?? 0)
        + (item.CustomerName?.Length ?? 0)
        + (item.CustomerPhone?.Length ?? 0)
        + (item.DeliveryStaffName?.Length ?? 0)
        + (item.DeliveryAddress?.Length ?? 0)
        + (item.ShippingDateFormatted?.Length ?? 0)
        + (item.ExpectedDeliveryDateFormatted?.Length ?? 0)
        + (item.StatusText?.Length ?? 0);

    private static int CalculateRecentReviewScore(ReviewRecentItemViewModel item)
    {
        var score = 0;
        score += item.UserID.HasValue ? 1 : 0;
        score += HasMeaningfulValue(item.CustomerName) ? 1 : 0;
        score += HasMeaningfulValue(item.ProductName) ? 2 : 0;
        score += HasMeaningfulValue(item.ProductImageFileName) ? 1 : 0;
        score += item.Rating > 0 ? 1 : 0;
        score += HasMeaningfulValue(item.StarDisplay) ? 1 : 0;
        score += HasMeaningfulValue(item.Comment) ? 1 : 0;
        score += HasMeaningfulValue(item.CreatedAtFormatted) ? 1 : 0;
        return score;
    }

    private static int CalculateRecentReviewSignalLength(ReviewRecentItemViewModel item)
        => (item.CustomerName?.Length ?? 0)
        + (item.ProductName?.Length ?? 0)
        + (item.ProductImageFileName?.Length ?? 0)
        + (item.StarDisplay?.Length ?? 0)
        + (item.Comment?.Length ?? 0)
        + (item.CreatedAtFormatted?.Length ?? 0);

    private static bool HasMeaningfulValue(string? value) => !string.IsNullOrWhiteSpace(value);

    private static string NormalizeTopicKey(string topic)
    {
        var normalized = topic.Trim().Normalize(NormalizationForm.FormD);
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

    private static string BuildQuery(IDictionary<string, string?> parameters)
    {
        var pairs = parameters
            .Where(x => !string.IsNullOrWhiteSpace(x.Value))
            .Select(x => $"{Uri.EscapeDataString(x.Key)}={Uri.EscapeDataString(x.Value!)}")
            .ToList();

        return pairs.Count == 0 ? string.Empty : "?" + string.Join("&", pairs);
    }

    private static async Task<string> ReadApiErrorAsync(HttpResponseMessage response, string fallback)
    {
        var body = await response.Content.ReadAsStringAsync();
        if (string.IsNullOrWhiteSpace(body))
        {
            return fallback;
        }

        try
        {
            using var doc = JsonDocument.Parse(body);
            var root = doc.RootElement;

            if (root.TryGetProperty("message", out var messageElement) && messageElement.ValueKind == JsonValueKind.String)
            {
                return messageElement.GetString() ?? fallback;
            }

            if (root.TryGetProperty("detail", out var detailElement) && detailElement.ValueKind == JsonValueKind.String)
            {
                return detailElement.GetString() ?? fallback;
            }
        }
        catch
        {
            // ignore parse failure
        }

        return fallback;
    }

    private sealed class BasicApiResponse
    {
        public string? Message { get; set; }
    }
}
