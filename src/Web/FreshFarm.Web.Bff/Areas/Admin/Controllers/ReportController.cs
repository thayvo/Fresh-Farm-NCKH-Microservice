using FreshFarm.Web.Bff.Areas.Seller.Infrastructure;
using FreshFarm.Web.Bff.Areas.Seller.Models;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Rendering;
using System.Net.Http.Headers;
using System.Net.Http.Json;
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
        model.RegisterFromDate = registerFromDate;
        model.RegisterToDate = registerToDate;
        model.CustomerSegment = string.IsNullOrWhiteSpace(model.CustomerSegment) ? "all" : model.CustomerSegment;

        foreach (var customer in model.TopCustomers)
        {
            customer.AvatarUrl = Url.Action("AvatarById", "Account", new { area = "", id = customer.UserID }) ?? string.Empty;
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
