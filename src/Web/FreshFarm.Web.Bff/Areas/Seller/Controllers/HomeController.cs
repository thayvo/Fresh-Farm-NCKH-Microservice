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

    [HttpGet]
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
            errors.Add("Khong the tai dashboard: " + ex.Message);
        }

        if (viewModel.CategoryLabels.Count == 0)
        {
            viewModel.CategoryLabels = new List<string> { "Rau la", "Rau an hoa", "Rau an qua", "Cu va re" };
            viewModel.CategoryData = new List<decimal> { 0m, 0m, 0m, 0m };
        }

        if (errors.Count > 0)
        {
            ViewBag.ErrorMessage = string.Join(" | ", errors.Distinct());
        }

        return View(viewModel);
    }

    [HttpGet]
    public async Task<IActionResult> Profile()
    {
        var adminId = TryGetCurrentAdminId();
        if (adminId <= 0)
        {
            return RedirectToAction("Login", "AdminAccount", new { area = "Seller" });
        }

        var profile = await GetCurrentAdminProfileAsync(adminId);
        if (profile is null)
        {
            return RedirectToAction("Login", "AdminAccount", new { area = "Seller" });
        }

        return View(profile);
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Profile(ProfileViewModel model)
    {
        if (!ModelState.IsValid)
        {
            TempData["ErrorMessage"] = "Du lieu khong hop le.";
            return RedirectToAction(nameof(Profile));
        }

        var token = GetAccessToken(AccessTokenSessionKey);
        if (string.IsNullOrWhiteSpace(token))
        {
            return RedirectToAction("Login", "AdminAccount", new { area = "Seller" });
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
                TempData["ErrorMessage"] = await ReadApiErrorAsync(response, "Khong the cap nhat ho so.");
                return RedirectToAction(nameof(Profile));
            }

            TempData["SuccessMessage"] = "Cap nhat thong tin thanh cong!";
            return RedirectToAction(nameof(Profile));
        }
        catch (Exception ex)
        {
            TempData["ErrorMessage"] = "Loi cap nhat ho so: " + ex.Message;
            return RedirectToAction(nameof(Profile));
        }
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> ChangePassword(ChangePasswordViewModel model)
    {
        var adminId = TryGetCurrentAdminId();
        if (adminId <= 0)
        {
            return RedirectToAction("Login", "AdminAccount", new { area = "Seller" });
        }

        if (!ModelState.IsValid)
        {
            TempData["ErrorMessage"] = "Du lieu doi mat khau khong hop le.";
            return RedirectToAction(nameof(Profile));
        }

        model.CurrentPassword = (model.CurrentPassword ?? string.Empty).Trim();
        model.NewPassword = (model.NewPassword ?? string.Empty).Trim();
        model.ConfirmPassword = (model.ConfirmPassword ?? string.Empty).Trim();

        if (!string.Equals(model.NewPassword, model.ConfirmPassword, StringComparison.Ordinal))
        {
            TempData["ErrorMessage"] = "Mat khau xac nhan khong khop.";
            return RedirectToAction(nameof(Profile));
        }

        if (string.Equals(model.CurrentPassword, model.NewPassword, StringComparison.Ordinal))
        {
            TempData["ErrorMessage"] = "Mat khau moi khong duoc trung mat khau hien tai.";
            return RedirectToAction(nameof(Profile));
        }

        try
        {
            var identityClient = CreateAuthorizedClient("Identity");
            var adminResponse = await identityClient.GetAsync($"/auth/admin/users/{adminId}");
            if (!adminResponse.IsSuccessStatusCode)
            {
                TempData["ErrorMessage"] = await ReadApiErrorAsync(adminResponse, "Khong the tai thong tin nguoi dung.");
                return RedirectToAction(nameof(Profile));
            }

            var admin = await adminResponse.Content.ReadFromJsonAsync<AdminUserBridge>(JsonOptions);
            if (admin is null)
            {
                TempData["ErrorMessage"] = "Khong doc duoc thong tin nguoi dung.";
                return RedirectToAction(nameof(Profile));
            }

            var verifyClient = _httpClientFactory.CreateClient("Identity");
            var verifyResponse = await verifyClient.PostAsJsonAsync("/auth/login", new LoginRequestDto
            {
                Identifier = admin.UserName,
                Password = model.CurrentPassword
            });

            if (!verifyResponse.IsSuccessStatusCode)
            {
                TempData["ErrorMessage"] = "Mat khau hien tai khong dung.";
                return RedirectToAction(nameof(Profile));
            }

            if (admin.RoleId <= 0)
            {
                TempData["ErrorMessage"] = "Khong xac dinh duoc role cua tai khoan.";
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
                TempData["ErrorMessage"] = await ReadApiErrorAsync(updateResponse, "Khong the doi mat khau.");
                return RedirectToAction(nameof(Profile));
            }

            TempData["SuccessMessage"] = "Doi mat khau thanh cong!";
            return RedirectToAction(nameof(Profile));
        }
        catch (Exception ex)
        {
            TempData["ErrorMessage"] = "Loi doi mat khau: " + ex.Message;
            return RedirectToAction(nameof(Profile));
        }
    }

    private async Task<ProfileViewModel?> GetCurrentAdminProfileAsync(int adminId)
    {
        var identityClient = CreateAuthorizedClient("Identity");
        var response = await identityClient.GetAsync($"/auth/admin/users/{adminId}");
        if (!response.IsSuccessStatusCode)
        {
            TempData["ErrorMessage"] = await ReadApiErrorAsync(response, "Khong the tai ho so nguoi dung.");
            return null;
        }

        var admin = await response.Content.ReadFromJsonAsync<AdminUserBridge>(JsonOptions);
        if (admin is null)
        {
            TempData["ErrorMessage"] = "Khong doc duoc ho so nguoi dung.";
            return null;
        }

        var vm = new ProfileViewModel
        {
            UserID = admin.UserId,
            UserName = admin.UserName ?? string.Empty,
            FullName = admin.FullName ?? string.Empty,
            Email = admin.Email ?? string.Empty,
            Phone = admin.Phone ?? string.Empty,
            CreatedDate = admin.Created ?? DateTime.UtcNow,
            LastActivity = admin.LastLogin.HasValue
                ? "Dang nhap gan nhat: " + admin.LastLogin.Value.ToString("dd/MM/yyyy HH:mm")
                : "Chua co hoat dong"
        };

        var activities = new List<string>();
        if (admin.Updated.HasValue)
        {
            activities.Add($"Cap nhat ho so luc {admin.Updated.Value:dd/MM/yyyy HH:mm}");
        }

        if (admin.LastLogin.HasValue)
        {
            activities.Add($"Dang nhap gan nhat luc {admin.LastLogin.Value:dd/MM/yyyy HH:mm}");
        }

        var successMessage = TempData.Peek("SuccessMessage")?.ToString() ?? string.Empty;
        if (successMessage.Contains("mat khau", StringComparison.OrdinalIgnoreCase))
        {
            activities.Insert(0, "Vua doi mat khau thanh cong");
        }

        if (successMessage.Contains("thong tin", StringComparison.OrdinalIgnoreCase))
        {
            activities.Insert(0, "Vua cap nhat ho so thanh cong");
        }

        if (activities.Count == 0)
        {
            activities.Add("Chua co hoat dong nao gan day");
        }

        ViewBag.Activities = activities;
        ViewBag.RoleName = admin.Role?.RoleName ?? "—";
        ViewBag.Avatar = string.IsNullOrWhiteSpace(admin.Avatar) ? "no-avatar.jpg" : admin.Avatar;

        return vm;
    }

    private async Task FillStatisticsAsync(DashboardViewModel viewModel, HttpResponseMessage response, List<string> errors)
    {
        if (!response.IsSuccessStatusCode)
        {
            errors.Add(await ReadApiErrorAsync(response, "Khong the tai thong ke don hang."));
            return;
        }

        var payload = await response.Content.ReadFromJsonAsync<AdminStatisticsResponse>(JsonOptions);
        if (payload?.Success != true || payload.Data is null)
        {
            errors.Add("Du lieu thong ke don hang khong hop le.");
            return;
        }

        viewModel.MonthlyRevenue = payload.Data.MonthRevenue;
        viewModel.NewOrdersCount = payload.Data.PendingOrders;
    }

    private async Task FillOrdersReportAsync(DashboardViewModel viewModel, HttpResponseMessage response, List<string> errors)
    {
        if (!response.IsSuccessStatusCode)
        {
            errors.Add(await ReadApiErrorAsync(response, "Khong the tai bao cao don hang."));
            return;
        }

        var payload = await response.Content.ReadFromJsonAsync<OrderReportViewModel>(JsonOptions);
        if (payload is null)
        {
            errors.Add("Du lieu bao cao don hang khong hop le.");
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
            errors.Add(await ReadApiErrorAsync(response, "Khong the tai bao cao khach hang."));
            return;
        }

        var payload = await response.Content.ReadFromJsonAsync<CustomerReportViewModel>(JsonOptions);
        if (payload is null)
        {
            errors.Add("Du lieu bao cao khach hang khong hop le.");
            return;
        }

        viewModel.TotalCustomers = payload.TotalCustomers;
    }

    private async Task FillProductsReportAsync(DashboardViewModel viewModel, HttpResponseMessage response, List<string> errors)
    {
        if (!response.IsSuccessStatusCode)
        {
            errors.Add(await ReadApiErrorAsync(response, "Khong the tai bao cao san pham."));
            return;
        }

        var payload = await response.Content.ReadFromJsonAsync<ProductReportViewModel>(JsonOptions);
        if (payload is null)
        {
            errors.Add("Du lieu bao cao san pham khong hop le.");
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
            errors.Add(await ReadApiErrorAsync(response, "Khong the tai xu huong doanh thu."));
            return;
        }

        var payload = await response.Content.ReadFromJsonAsync<RevenueReportViewModel>(JsonOptions);
        if (payload is null)
        {
            errors.Add("Du lieu xu huong doanh thu khong hop le.");
            return;
        }

        viewModel.ChartLabels = payload.TrendLabels;
        viewModel.ChartData = payload.TrendData;
    }

    private async Task FillSupportStatsAsync(DashboardViewModel viewModel, HttpResponseMessage response, List<string> errors)
    {
        if (!response.IsSuccessStatusCode)
        {
            errors.Add(await ReadApiErrorAsync(response, "Khong the tai thong ke ho tro."));
            return;
        }

        var payload = await response.Content.ReadFromJsonAsync<SupportConversationsResponse>(JsonOptions);
        if (payload?.Ok != true || payload.Conversations is null)
        {
            errors.Add("Du lieu thong ke ho tro khong hop le.");
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
}
