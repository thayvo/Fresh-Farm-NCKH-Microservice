using System.Globalization;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Security.Claims;
using System.Text.Json;
using FreshFarm.Web.Bff.Areas.Seller.Infrastructure;
using FreshFarm.Web.Bff.Areas.Seller.Models;
using FreshFarm.Web.Bff.Dtos;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace FreshFarm.Web.Bff.Areas.Seller.Controllers;

[Authorize(Roles = "Seller")]
[ResponseCache(Duration = 0, Location = ResponseCacheLocation.None, NoStore = true)]
[Area("Seller")]
public class HomeController : LegacySellerControllerBase
{
    private const string AccessTokenSessionKey = "ACCESS_TOKEN";

    private readonly IHttpClientFactory _httpClientFactory;

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true
    };

    public HomeController(IHttpClientFactory httpClientFactory)
    {
        _httpClientFactory = httpClientFactory;
    }

    [HttpGet("/Seller")]
    [HttpGet("/Seller/Home/Dashboard")]
    public async Task<IActionResult> Dashboard()
    {
        var viewModel = new DashboardViewModel();
        var errors = new List<string>();

        try
        {
            var orderingClient = CreateAuthorizedClient("Ordering");
            var today = DateTime.UtcNow.Date;
            var fromDate = today.AddDays(-6).ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
            var toDate = today.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);

            var statisticsTask = orderingClient.GetAsync("/api/orders/admin/statistics");
            var ordersReportTask = orderingClient.GetAsync("/api/orders/admin/reports/orders");
            var customersReportTask = orderingClient.GetAsync("/api/orders/admin/reports/customers");
            var productsReportTask = orderingClient.GetAsync("/api/orders/admin/reports/products?page=1");
            var revenueReportTask = orderingClient.GetAsync($"/api/orders/admin/reports/revenue?fromDate={fromDate}&toDate={toDate}&viewBy=day");
            var supportConversationsTask = orderingClient.GetAsync("/api/orders/admin/support-chat/conversations");

            await Task.WhenAll(
                statisticsTask,
                ordersReportTask,
                customersReportTask,
                productsReportTask,
                revenueReportTask,
                supportConversationsTask);

            await FillStatisticsAsync(viewModel, await statisticsTask, errors);
            await FillOrdersReportAsync(viewModel, await ordersReportTask, errors);
            await FillCustomersReportAsync(viewModel, await customersReportTask, errors);
            await FillProductsReportAsync(viewModel, await productsReportTask, errors);
            await FillRevenueReportAsync(viewModel, await revenueReportTask, errors);
            await FillSupportStatsAsync(viewModel, await supportConversationsTask, errors);
        }
        catch (Exception ex)
        {
            errors.Add("Không thể tải bảng điều khiển: " + ex.Message);
        }

        if (viewModel.CategoryLabels.Count == 0)
        {
            viewModel.CategoryLabels = new List<string> { "Rau lá", "Rau ăn hoa", "Rau ăn quả", "Củ và rễ" };
            viewModel.CategoryData = new List<decimal> { 0m, 0m, 0m, 0m };
        }

        if (errors.Count > 0)
        {
            ViewBag.ErrorMessage = string.Join(" | ", errors.Distinct());
        }

        return View(viewModel);
    }

    [HttpGet("/Seller/Profile")]
    [HttpGet("/Seller/Home/Profile")]
    public async Task<IActionResult> Profile()
    {
        var adminId = TryGetCurrentAdminId();
        if (adminId <= 0)
        {
            return RedirectToAction("Login", "SellerAccount", new { area = "Seller" });
        }

        var profile = await GetCurrentAdminProfileAsync(adminId);
        if (profile is null)
        {
            return RedirectToAction("Login", "SellerAccount", new { area = "Seller" });
        }

        return View(profile);
    }

    [HttpPost("/Seller/Profile")]
    [HttpPost("/Seller/Home/Profile")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Profile(ProfileViewModel model)
    {
        if (!ModelState.IsValid)
        {
            TempData["ErrorMessage"] = "Dữ liệu không hợp lệ.";
            return RedirectToAction(nameof(Profile));
        }

        var token = GetAccessToken(AccessTokenSessionKey);
        if (string.IsNullOrWhiteSpace(token))
        {
            return RedirectToAction("Login", "SellerAccount", new { area = "Seller" });
        }

        try
        {
            var identityClient = CreateAuthorizedClient("Identity");
            var response = await identityClient.PutAsJsonAsync("/auth/profile", new
            {
                fullName = (model.FullName ?? string.Empty).Trim(),
                email = (model.Email ?? string.Empty).Trim(),
                phone = (model.Phone ?? string.Empty).Trim()
            });

            if (!response.IsSuccessStatusCode)
            {
                TempData["ErrorMessage"] = await ReadApiErrorAsync(response, "Không thể cập nhật hồ sơ.");
                return RedirectToAction(nameof(Profile));
            }

            TempData["SuccessMessage"] = "Cập nhật thông tin thành công!";
            return RedirectToAction(nameof(Profile));
        }
        catch (Exception ex)
        {
            TempData["ErrorMessage"] = "Lỗi cập nhật hồ sơ: " + ex.Message;
            return RedirectToAction(nameof(Profile));
        }
    }

    [HttpPost("/Seller/Home/ChangePassword")]
    [HttpPost("/Seller/Profile/ChangePassword")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> ChangePassword(ChangePasswordViewModel model)
    {
        var adminId = TryGetCurrentAdminId();
        if (adminId <= 0)
        {
            return RedirectToAction("Login", "SellerAccount", new { area = "Seller" });
        }

        if (!ModelState.IsValid)
        {
            TempData["ErrorMessage"] = "Dữ liệu đổi mật khẩu không hợp lệ.";
            return RedirectToAction(nameof(Profile));
        }

        model.CurrentPassword = (model.CurrentPassword ?? string.Empty).Trim();
        model.NewPassword = (model.NewPassword ?? string.Empty).Trim();
        model.ConfirmPassword = (model.ConfirmPassword ?? string.Empty).Trim();

        if (!string.Equals(model.NewPassword, model.ConfirmPassword, StringComparison.Ordinal))
        {
            TempData["ErrorMessage"] = "Mật khẩu xác nhận không khớp.";
            return RedirectToAction(nameof(Profile));
        }

        if (string.Equals(model.CurrentPassword, model.NewPassword, StringComparison.Ordinal))
        {
            TempData["ErrorMessage"] = "Mật khẩu mới không được trùng mật khẩu hiện tại.";
            return RedirectToAction(nameof(Profile));
        }

        try
        {
            var identityClient = CreateAuthorizedClient("Identity");
            var adminResponse = await identityClient.GetAsync($"/auth/admin/users/{adminId}");
            if (!adminResponse.IsSuccessStatusCode)
            {
                TempData["ErrorMessage"] = await ReadApiErrorAsync(adminResponse, "Không thể tải thông tin người dùng.");
                return RedirectToAction(nameof(Profile));
            }

            var admin = await adminResponse.Content.ReadFromJsonAsync<AdminUserBridge>(JsonOptions);
            if (admin is null)
            {
                TempData["ErrorMessage"] = "Không đọc được thông tin người dùng.";
                return RedirectToAction(nameof(Profile));
            }

            var verifyClient = _httpClientFactory.CreateClient("Identity");
            var verifyResponse = await verifyClient.PostAsJsonAsync("/auth/login", new LoginRequestDto
            {
                Identifier = admin.UserName,
                Password = model.CurrentPassword,
                ClientLane = "Seller"
            });

            if (!verifyResponse.IsSuccessStatusCode)
            {
                TempData["ErrorMessage"] = "Mật khẩu hiện tại không đúng.";
                return RedirectToAction(nameof(Profile));
            }

            if (admin.RoleId <= 0)
            {
                TempData["ErrorMessage"] = "Không xác định được vai trò của tài khoản.";
                return RedirectToAction(nameof(Profile));
            }

            var updateResponse = await identityClient.PutAsJsonAsync($"/auth/admin/users/{adminId}", new
            {
                userName = admin.UserName,
                fullName = admin.FullName,
                email = admin.Email,
                phone = admin.Phone,
                avatar = admin.Avatar,
                isActive = admin.IsActive,
                roleId = admin.RoleId,
                newPassword = model.NewPassword
            });

            if (!updateResponse.IsSuccessStatusCode)
            {
                TempData["ErrorMessage"] = await ReadApiErrorAsync(updateResponse, "Không thể đổi mật khẩu.");
                return RedirectToAction(nameof(Profile));
            }

            TempData["SuccessMessage"] = "Đổi mật khẩu thành công!";
            return RedirectToAction(nameof(Profile));
        }
        catch (Exception ex)
        {
            TempData["ErrorMessage"] = "Lỗi đổi mật khẩu: " + ex.Message;
            return RedirectToAction(nameof(Profile));
        }
    }

    private async Task<ProfileViewModel?> GetCurrentAdminProfileAsync(int adminId)
    {
        var identityClient = CreateAuthorizedClient("Identity");
        var response = await identityClient.GetAsync("/auth/profile");
        if (!response.IsSuccessStatusCode)
        {
            TempData["ErrorMessage"] = await ReadApiErrorAsync(response, "Không thể tải hồ sơ người bán.");
            return null;
        }

        var sellerProfile = await response.Content.ReadFromJsonAsync<SellerProfileBridge>(JsonOptions);
        if (sellerProfile is null)
        {
            TempData["ErrorMessage"] = "Không đọc được hồ sơ người bán.";
            return null;
        }

        var vm = new ProfileViewModel
        {
            UserID = sellerProfile.UserId > 0 ? sellerProfile.UserId : adminId,
            UserName = sellerProfile.UserName ?? string.Empty,
            FullName = sellerProfile.FullName ?? string.Empty,
            Email = sellerProfile.Email ?? string.Empty,
            Phone = sellerProfile.Phone ?? string.Empty,
            CreatedDate = DateTime.UtcNow,
            LastActivity = "Thông tin tài khoản người bán"
        };

        var activities = new List<string>();

        var successMessage = TempData.Peek("SuccessMessage")?.ToString() ?? string.Empty;
        if (successMessage.Contains("mật khẩu", StringComparison.OrdinalIgnoreCase) ||
            successMessage.Contains("mat khau", StringComparison.OrdinalIgnoreCase))
        {
            activities.Insert(0, "Vừa đổi mật khẩu thành công");
        }

        if (successMessage.Contains("thông tin", StringComparison.OrdinalIgnoreCase) ||
            successMessage.Contains("thong tin", StringComparison.OrdinalIgnoreCase))
        {
            activities.Insert(0, "Vừa cập nhật hồ sơ thành công");
        }

        if (activities.Count == 0)
        {
            activities.Add("Thông tin tài khoản đã sẵn sàng để cập nhật");
        }

        TempData.Remove("ErrorMessage");
        ViewBag.Activities = activities;
        ViewBag.RoleName = "Người bán";
        ViewBag.Avatar = "no-avatar.jpg";

        return vm;
    }

    private async Task FillStatisticsAsync(DashboardViewModel viewModel, HttpResponseMessage response, List<string> errors)
    {
        if (!response.IsSuccessStatusCode)
        {
            errors.Add(await ReadApiErrorAsync(response, "Không thể tải thống kê đơn hàng."));
            return;
        }

        var payload = await response.Content.ReadFromJsonAsync<AdminStatisticsResponse>(JsonOptions);
        if (payload?.Success != true || payload.Data is null)
        {
            errors.Add("Dữ liệu thống kê đơn hàng không hợp lệ.");
            return;
        }

        viewModel.MonthlyRevenue = payload.Data.MonthRevenue;
        viewModel.NewOrdersCount = payload.Data.PendingOrders;
    }

    private async Task FillOrdersReportAsync(DashboardViewModel viewModel, HttpResponseMessage response, List<string> errors)
    {
        if (!response.IsSuccessStatusCode)
        {
            errors.Add(await ReadApiErrorAsync(response, "Không thể tải báo cáo đơn hàng."));
            return;
        }

        var payload = await response.Content.ReadFromJsonAsync<OrderReportViewModel>(JsonOptions);
        if (payload is null)
        {
            errors.Add("Dữ liệu báo cáo đơn hàng không hợp lệ.");
            return;
        }

        viewModel.RecentOrders = payload.RecentOrders
            .Take(5)
            .Select(o => new RecentOrderViewModel
            {
                OrderCode = o.OrderCode,
                CustomerName = o.CustomerName,
                OrderDate = o.OrderDate,
                TotalAmount = o.TotalAmount,
                Status = o.StatusText,
                StatusBadgeClass = o.StatusBadgeClass
            })
            .ToList();
    }

    private async Task FillCustomersReportAsync(DashboardViewModel viewModel, HttpResponseMessage response, List<string> errors)
    {
        if (!response.IsSuccessStatusCode)
        {
            errors.Add(await ReadApiErrorAsync(response, "Không thể tải báo cáo khách hàng."));
            return;
        }

        var payload = await response.Content.ReadFromJsonAsync<CustomerReportViewModel>(JsonOptions);
        if (payload is null)
        {
            errors.Add("Dữ liệu báo cáo khách hàng không hợp lệ.");
            return;
        }

        viewModel.TotalCustomers = payload.TotalCustomers;
    }

    private async Task FillProductsReportAsync(DashboardViewModel viewModel, HttpResponseMessage response, List<string> errors)
    {
        if (!response.IsSuccessStatusCode)
        {
            errors.Add(await ReadApiErrorAsync(response, "Không thể tải báo cáo sản phẩm."));
            return;
        }

        var payload = await response.Content.ReadFromJsonAsync<ProductReportViewModel>(JsonOptions);
        if (payload is null)
        {
            errors.Add("Dữ liệu báo cáo sản phẩm không hợp lệ.");
            return;
        }

        viewModel.LowStockProducts = payload.LowStockProducts;
        viewModel.CategoryLabels = payload.CategoryRevenues.Select(x => x.CategoryName).ToList();
        viewModel.CategoryData = payload.CategoryRevenues.Select(x => x.Revenue).ToList();
    }

    private async Task FillRevenueReportAsync(DashboardViewModel viewModel, HttpResponseMessage response, List<string> errors)
    {
        if (!response.IsSuccessStatusCode)
        {
            errors.Add(await ReadApiErrorAsync(response, "Không thể tải xu hướng doanh thu."));
            return;
        }

        var payload = await response.Content.ReadFromJsonAsync<RevenueReportViewModel>(JsonOptions);
        if (payload is null)
        {
            errors.Add("Dữ liệu xu hướng doanh thu không hợp lệ.");
            return;
        }

        viewModel.ChartLabels = payload.TrendLabels;
        viewModel.ChartData = payload.TrendData;
    }

    private async Task FillSupportStatsAsync(DashboardViewModel viewModel, HttpResponseMessage response, List<string> errors)
    {
        if (!response.IsSuccessStatusCode)
        {
            errors.Add(await ReadApiErrorAsync(response, "Không thể tải thống kê hỗ trợ."));
            return;
        }

        var payload = await response.Content.ReadFromJsonAsync<SupportConversationsResponse>(JsonOptions);
        if (payload?.Ok != true || payload.Conversations is null)
        {
            errors.Add("Dữ liệu thống kê hỗ trợ không hợp lệ.");
            return;
        }

        viewModel.OpenSupportConversations = payload.Conversations.Count(x =>
            string.Equals(x.Status, "Open", StringComparison.OrdinalIgnoreCase));
        viewModel.UnreadSupportMessages = payload.Conversations.Count(x => x.HasUnread);
    }

    private HttpClient CreateAuthorizedClient(string clientName)
    {
        var client = _httpClientFactory.CreateClient(clientName);
        client.DefaultRequestHeaders.Remove("Authorization");
        client.DefaultRequestHeaders.Authorization = null;

        var token = GetAccessToken(AccessTokenSessionKey);
        if (!string.IsNullOrWhiteSpace(token))
        {
            client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);
        }

        return client;
    }

    private int TryGetCurrentAdminId()
    {
        if (Session["ADMIN_ID"] is int adminId && adminId > 0)
        {
            return adminId;
        }

        var claimValue = User.FindFirstValue(ClaimTypes.NameIdentifier)
            ?? User.FindFirstValue(System.IdentityModel.Tokens.Jwt.JwtRegisteredClaimNames.Sub);

        return int.TryParse(claimValue, out var parsedId) ? parsedId : 0;
    }

    private static async Task<string> ReadApiErrorAsync(HttpResponseMessage response, string fallback)
    {
        try
        {
            var json = await response.Content.ReadAsStringAsync();
            if (string.IsNullOrWhiteSpace(json))
            {
                return fallback;
            }

            using var doc = JsonDocument.Parse(json);
            if (doc.RootElement.ValueKind == JsonValueKind.Object)
            {
                if (doc.RootElement.TryGetProperty("message", out var message) &&
                    message.ValueKind == JsonValueKind.String)
                {
                    return message.GetString() ?? fallback;
                }

                if (doc.RootElement.TryGetProperty("title", out var title) &&
                    title.ValueKind == JsonValueKind.String)
                {
                    return title.GetString() ?? fallback;
                }

                if (doc.RootElement.TryGetProperty("detail", out var detail) &&
                    detail.ValueKind == JsonValueKind.String)
                {
                    return detail.GetString() ?? fallback;
                }
            }

            return fallback;
        }
        catch
        {
            return fallback;
        }
    }

    private sealed class AdminStatisticsResponse
    {
        public bool Success { get; set; }

        public AdminStatisticsData? Data { get; set; }
    }

    private sealed class AdminStatisticsData
    {
        public decimal MonthRevenue { get; set; }

        public int PendingOrders { get; set; }
    }

    private sealed class SupportConversationsResponse
    {
        public bool Ok { get; set; }

        public List<SupportConversationBridge> Conversations { get; set; } = new();
    }

    private sealed class SupportConversationBridge
    {
        public bool HasUnread { get; set; }

        public string? Status { get; set; }
    }

    private sealed class AdminUserBridge
    {
        public int UserId { get; set; }

        public string UserName { get; set; } = string.Empty;

        public string FullName { get; set; } = string.Empty;

        public string Email { get; set; } = string.Empty;

        public string Phone { get; set; } = string.Empty;

        public string? Avatar { get; set; }

        public bool IsActive { get; set; }

        public int RoleId { get; set; }

        public AdminRoleBridge? Role { get; set; }

        public DateTime? LastLogin { get; set; }

        public DateTime? Created { get; set; }

        public DateTime? Updated { get; set; }
    }

    private sealed class AdminRoleBridge
    {
        public int RoleId { get; set; }

        public string RoleName { get; set; } = string.Empty;
    }

    private sealed class SellerProfileBridge
    {
        public int UserId { get; set; }

        public string UserName { get; set; } = string.Empty;

        public string FullName { get; set; } = string.Empty;

        public string Email { get; set; } = string.Empty;

        public string Phone { get; set; } = string.Empty;
    }
}
