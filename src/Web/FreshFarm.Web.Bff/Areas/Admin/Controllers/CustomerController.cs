using FreshFarm.Web.Bff.Areas.Seller.Infrastructure;
using FreshFarm.Web.Bff.Areas.Seller.Models;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;

namespace FreshFarm.Web.Bff.Areas.Admin.Controllers;

[Authorize(Policy = "AdminOnly")]
[ResponseCache(Duration = 0, Location = ResponseCacheLocation.None, NoStore = true)]
[Area("Admin")]
public sealed class CustomerController : LegacySellerControllerBase
{
    private const string AccessTokenSessionKey = "ACCESS_TOKEN";
    private readonly IHttpClientFactory _httpClientFactory;

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true
    };

    public CustomerController(IHttpClientFactory httpClientFactory)
    {
        _httpClientFactory = httpClientFactory;
    }

    public async Task<IActionResult> ManageCustomers(string sortBy = "date_desc", int page = 1, int pageSize = 10)
    {
        try
        {
            if (page < 1)
            {
                page = 1;
            }

            if (pageSize <= 0)
            {
                pageSize = 10;
            }

            var customers = await GetCustomersAsync(keyword: null, take: 5000);
            var metrics = await GetCustomerMetricsAsync(customers.Select(c => c.userId).ToList());
            var metricsByUserId = metrics.ToDictionary(m => m.userId, m => m);

            var customerModels = customers.Select(c =>
            {
                metricsByUserId.TryGetValue(c.userId, out var metric);

                return new CustomerViewModel
                {
                    UserID = c.userId,
                    UserName = c.userName ?? string.Empty,
                    FullName = c.fullName ?? string.Empty,
                    Email = c.email ?? string.Empty,
                    Phone = c.phone,
                    Address = c.address,
                    CreatedDate = c.createdAt,
                    TotalSpent = metric?.totalSpent ?? 0,
                    OrderCount = metric?.orderCount ?? 0,
                    AvatarUrl = Url.Action("AvatarById", "Account", new { area = "", id = c.userId }) ?? string.Empty
                };
            });

            customerModels = (sortBy ?? string.Empty).ToLowerInvariant() switch
            {
                "spent_desc" => customerModels.OrderByDescending(u => u.TotalSpent),
                "spent_asc" => customerModels.OrderBy(u => u.TotalSpent),
                "orders_desc" => customerModels.OrderByDescending(u => u.OrderCount),
                "orders_asc" => customerModels.OrderBy(u => u.OrderCount),
                "name_asc" => customerModels.OrderBy(u => u.FullName),
                "name_desc" => customerModels.OrderByDescending(u => u.FullName),
                "date_asc" => customerModels.OrderBy(u => u.CreatedDate),
                _ => customerModels.OrderByDescending(u => u.CreatedDate)
            };

            var orderedList = customerModels.ToList();
            var totalRecords = orderedList.Count;
            var totalPages = (int)Math.Ceiling(totalRecords / (double)pageSize);
            if (totalPages > 0 && page > totalPages)
            {
                page = totalPages;
            }

            var pagedUsers = orderedList
                .Skip((page - 1) * pageSize)
                .Take(pageSize)
                .ToList();

            ViewBag.CurrentSort = sortBy;
            ViewBag.CurrentPage = page;
            ViewBag.TotalPages = totalPages;
            ViewBag.TotalRecords = totalRecords;

            ViewData["AreaName"] = "Admin";
            ViewData["LayoutPath"] = "~/Areas/Admin/Views/Shared/_LayoutAdmin.cshtml";
            ViewData["CustomerScopeLabel"] = "Toàn sàn";

            return View("~/Areas/Seller/Views/Customer/ManageCustomers.cshtml", pagedUsers);
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"Error in Admin ManageCustomers: {ex.Message}");
            TempData["ErrorMessage"] = "Có lỗi xảy ra khi tải danh sách khách hàng";
            ViewData["AreaName"] = "Admin";
            ViewData["LayoutPath"] = "~/Areas/Admin/Views/Shared/_LayoutAdmin.cshtml";
            ViewData["CustomerScopeLabel"] = "Toàn sàn";
            return View("~/Areas/Seller/Views/Customer/ManageCustomers.cshtml", new List<CustomerViewModel>());
        }
    }

    public IActionResult Create()
    {
        ViewData["AreaName"] = "Admin";
        ViewData["LayoutPath"] = "~/Areas/Admin/Views/Shared/_LayoutAdmin.cshtml";
        ViewData["CustomerScopeLabel"] = "Toàn sàn";
        return View("~/Areas/Seller/Views/Customer/Create.cshtml", new CreateCustomerViewModel());
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Create(CreateCustomerViewModel model)
    {
        if (!ModelState.IsValid)
        {
            ViewData["AreaName"] = "Admin";
            ViewData["LayoutPath"] = "~/Areas/Admin/Views/Shared/_LayoutAdmin.cshtml";
            ViewData["CustomerScopeLabel"] = "Toàn sàn";
            return View("~/Areas/Seller/Views/Customer/Create.cshtml", model);
        }

        model.UserName = model.UserName?.Trim() ?? string.Empty;
        model.FullName = model.FullName?.Trim() ?? string.Empty;
        model.Email = model.Email?.Trim().ToLowerInvariant() ?? string.Empty;
        model.Phone = model.Phone?.Trim() ?? string.Empty;
        model.Address = model.Address?.Trim();

        var client = CreateAuthorizedClient("Identity");
        var response = await client.PostAsJsonAsync("/auth/admin/customers", new
        {
            userName = model.UserName,
            fullName = model.FullName,
            email = model.Email,
            phone = model.Phone,
            password = model.Password,
            address = model.Address
        });

        if (!response.IsSuccessStatusCode)
        {
            ModelState.AddModelError(string.Empty, await ReadApiErrorAsync(response, "Không thể thêm khách hàng"));
            ViewData["AreaName"] = "Admin";
            ViewData["LayoutPath"] = "~/Areas/Admin/Views/Shared/_LayoutAdmin.cshtml";
            ViewData["CustomerScopeLabel"] = "Toàn sàn";
            return View("~/Areas/Seller/Views/Customer/Create.cshtml", model);
        }

        TempData["SuccessMessage"] = $"Thêm khách hàng '{model.FullName}' thành công!";
        return RedirectToAction(nameof(ManageCustomers));
    }

    public async Task<IActionResult> Edit(int id)
    {
        try
        {
            var client = CreateAuthorizedClient("Identity");
            var response = await client.GetAsync($"/auth/admin/customers/{id}");
            if (!response.IsSuccessStatusCode)
            {
                TempData["ErrorMessage"] = await ReadApiErrorAsync(response, "Không tìm thấy khách hàng");
                return RedirectToAction(nameof(ManageCustomers));
            }

            var dto = await response.Content.ReadFromJsonAsync<IdentityCustomerDto>(JsonOptions);
            if (dto is null)
            {
                TempData["ErrorMessage"] = "Không đọc được dữ liệu khách hàng";
                return RedirectToAction(nameof(ManageCustomers));
            }

            var model = new EditCustomerViewModel
            {
                UserID = dto.userId,
                UserName = dto.userName ?? string.Empty,
                FullName = dto.fullName ?? string.Empty,
                Email = dto.email ?? string.Empty,
                Phone = dto.phone,
                Address = dto.address,
                CreatedDate = dto.createdAt
            };

            ViewData["AreaName"] = "Admin";
            ViewData["LayoutPath"] = "~/Areas/Admin/Views/Shared/_LayoutAdmin.cshtml";
            ViewData["CustomerScopeLabel"] = "Toàn sàn";
            return View("~/Areas/Seller/Views/Customer/Edit.cshtml", model);
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"[ERROR] Load admin edit form: {ex.Message}");
            TempData["ErrorMessage"] = "Có lỗi xảy ra khi tải thông tin khách hàng";
            return RedirectToAction(nameof(ManageCustomers));
        }
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Edit(EditCustomerViewModel model)
    {
        if (!ModelState.IsValid)
        {
            ViewData["AreaName"] = "Admin";
            ViewData["LayoutPath"] = "~/Areas/Admin/Views/Shared/_LayoutAdmin.cshtml";
            ViewData["CustomerScopeLabel"] = "Toàn sàn";
            return View("~/Areas/Seller/Views/Customer/Edit.cshtml", model);
        }

        model.FullName = model.FullName?.Trim() ?? string.Empty;
        model.Email = model.Email?.Trim().ToLowerInvariant() ?? string.Empty;
        model.Phone = model.Phone?.Trim();
        model.Address = model.Address?.Trim();

        var client = CreateAuthorizedClient("Identity");
        var response = await client.PutAsJsonAsync($"/auth/admin/customers/{model.UserID}", new
        {
            fullName = model.FullName,
            email = model.Email,
            phone = model.Phone,
            address = model.Address,
            newPassword = model.NewPassword
        });

        if (!response.IsSuccessStatusCode)
        {
            ModelState.AddModelError(string.Empty, await ReadApiErrorAsync(response, "Không thể cập nhật khách hàng"));
            ViewData["AreaName"] = "Admin";
            ViewData["LayoutPath"] = "~/Areas/Admin/Views/Shared/_LayoutAdmin.cshtml";
            ViewData["CustomerScopeLabel"] = "Toàn sàn";
            return View("~/Areas/Seller/Views/Customer/Edit.cshtml", model);
        }

        TempData["SuccessMessage"] = $"Cập nhật thông tin khách hàng '{model.FullName}' thành công!";
        return RedirectToAction(nameof(ManageCustomers));
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<JsonResult> Delete(int id)
    {
        try
        {
            if (id <= 0)
            {
                return Json(new { success = false, message = "ID khách hàng không hợp lệ" });
            }

            var orderingClient = CreateAuthorizedClient("Ordering");
            var checkResponse = await orderingClient.GetAsync($"/api/orders/admin/customers/{id}/has-orders");
            if (checkResponse.IsSuccessStatusCode)
            {
                var check = await checkResponse.Content.ReadFromJsonAsync<CustomerOrderCheckResponse>(JsonOptions);
                if (check?.hasOrders == true)
                {
                    return Json(new
                    {
                        success = false,
                        message = "Không thể xóa khách hàng đã có đơn hàng. Bạn có thể vô hiệu hóa tài khoản thay vì xóa."
                    });
                }
            }

            var identityClient = CreateAuthorizedClient("Identity");
            var deleteResponse = await identityClient.DeleteAsync($"/auth/admin/customers/{id}");
            if (!deleteResponse.IsSuccessStatusCode)
            {
                return Json(new { success = false, message = await ReadApiErrorAsync(deleteResponse, "Không thể xóa khách hàng") });
            }

            return Json(new { success = true, message = "Xóa khách hàng thành công" });
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"[ERROR] Admin delete customer: {ex.Message}");
            return Json(new { success = false, message = "Có lỗi xảy ra khi xóa khách hàng" });
        }
    }

    [HttpPost]
    public async Task<JsonResult> Search([FromBody] SearchRequest? request)
    {
        try
        {
            var keyword = request?.keyword?.Trim() ?? string.Empty;
            if (string.IsNullOrWhiteSpace(keyword))
            {
                return Json(new { success = false, message = "Vui lòng nhập từ khóa tìm kiếm" });
            }

            var customers = await GetCustomersAsync(keyword, 200);
            var metrics = await GetCustomerMetricsAsync(customers.Select(c => c.userId).ToList());
            var metricsByUserId = metrics.ToDictionary(m => m.userId, m => m);

            var results = customers
                .Select(c =>
                {
                    metricsByUserId.TryGetValue(c.userId, out var metric);
                    return new CustomerSearchResultDto
                    {
                        UserID = c.userId,
                        UserName = c.userName ?? string.Empty,
                        FullName = c.fullName ?? string.Empty,
                        Email = c.email ?? string.Empty,
                        Phone = c.phone,
                        CreatedDate = c.createdAt,
                        TotalSpent = metric?.totalSpent ?? 0,
                        OrderCount = metric?.orderCount ?? 0
                    };
                })
                .OrderByDescending(x => x.CreatedDate)
                .ToList();

            return Json(new
            {
                success = true,
                message = $"Tìm thấy {results.Count} kết quả",
                data = results
            });
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"[ERROR] Admin search customers: {ex.Message}");
            return Json(new { success = false, message = "Có lỗi xảy ra khi tìm kiếm" });
        }
    }

    private async Task<List<IdentityCustomerDto>> GetCustomersAsync(string? keyword, int take)
    {
        var query = $"take={take}";
        if (!string.IsNullOrWhiteSpace(keyword))
        {
            query += $"&keyword={Uri.EscapeDataString(keyword)}";
        }

        var client = CreateAuthorizedClient("Identity");
        var response = await client.GetAsync($"/auth/admin/customers?{query}");
        var customers = new List<IdentityCustomerDto>();

        if (response.IsSuccessStatusCode)
        {
            customers = await response.Content.ReadFromJsonAsync<List<IdentityCustomerDto>>(JsonOptions)
                ?? new List<IdentityCustomerDto>();
        }

        var fallbackCustomers = await GetFallbackCustomersFromOrdersAsync(keyword, take);
        if (fallbackCustomers.Count == 0)
        {
            return customers;
        }

        var merged = customers
            .Concat(fallbackCustomers)
            .GroupBy(x => x.userId)
            .Select(g => g
                .OrderByDescending(x => !string.IsNullOrWhiteSpace(x.fullName))
                .ThenByDescending(x => !string.IsNullOrWhiteSpace(x.email))
                .ThenByDescending(x => !string.IsNullOrWhiteSpace(x.phone))
                .First())
            .OrderByDescending(x => x.createdAt)
            .Take(take)
            .ToList();

        return merged;
    }

    private async Task<List<CustomerMetricDto>> GetCustomerMetricsAsync(List<int> userIds)
    {
        if (userIds.Count == 0)
        {
            return new List<CustomerMetricDto>();
        }

        var query = string.Join("&", userIds.Distinct().Select(id => $"userIds={id}"));

        var client = CreateAuthorizedClient("Ordering");
        var response = await client.GetAsync($"/api/orders/admin/customers/metrics?{query}");
        if (!response.IsSuccessStatusCode)
        {
            return new List<CustomerMetricDto>();
        }

        var payload = await response.Content.ReadFromJsonAsync<CustomerMetricsResponse>(JsonOptions);
        if (payload?.success != true || payload.data is null)
        {
            return new List<CustomerMetricDto>();
        }

        return payload.data;
    }

    private async Task<List<IdentityCustomerDto>> GetFallbackCustomersFromOrdersAsync(string? keyword, int take)
    {
        var orderingClient = CreateAuthorizedClient("Ordering");
        var idResponse = await orderingClient.GetAsync("/api/orders/admin/customers/ids");
        if (!idResponse.IsSuccessStatusCode)
        {
            return new List<IdentityCustomerDto>();
        }

        var idPayload = await idResponse.Content.ReadFromJsonAsync<CustomerIdsResponse>(JsonOptions);
        var userIds = idPayload?.data?
            .Where(id => id > 0)
            .Distinct()
            .Take(Math.Max(1, take))
            .ToList();

        if (userIds is not { Count: > 0 })
        {
            return new List<IdentityCustomerDto>();
        }

        var identityClient = CreateAuthorizedClient("Identity");
        var tasks = userIds.Select(async userId =>
        {
            try
            {
                var response = await identityClient.GetAsync($"/auth/admin/users/{userId}");
                if (!response.IsSuccessStatusCode)
                {
                    return null;
                }

                var dto = await response.Content.ReadFromJsonAsync<IdentityUserLookupDto>(JsonOptions);
                if (dto is null)
                {
                    return null;
                }

                return new IdentityCustomerDto
                {
                    userId = dto.userId,
                    userName = dto.userName,
                    fullName = dto.fullName,
                    email = dto.email,
                    phone = dto.phone,
                    createdAt = dto.created,
                    address = null
                };
            }
            catch
            {
                return null;
            }
        });

        var rows = (await Task.WhenAll(tasks))
            .Where(x => x is not null)
            .Cast<IdentityCustomerDto>()
            .ToList();

        if (string.IsNullOrWhiteSpace(keyword))
        {
            return rows;
        }

        var term = keyword.Trim();
        return rows
            .Where(x =>
                (!string.IsNullOrWhiteSpace(x.userName) && x.userName.Contains(term, StringComparison.OrdinalIgnoreCase)) ||
                (!string.IsNullOrWhiteSpace(x.fullName) && x.fullName.Contains(term, StringComparison.OrdinalIgnoreCase)) ||
                (!string.IsNullOrWhiteSpace(x.email) && x.email.Contains(term, StringComparison.OrdinalIgnoreCase)) ||
                (!string.IsNullOrWhiteSpace(x.phone) && x.phone.Contains(term, StringComparison.OrdinalIgnoreCase)))
            .ToList();
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

            if (root.TryGetProperty("title", out var titleElement) && titleElement.ValueKind == JsonValueKind.String)
            {
                return titleElement.GetString() ?? fallback;
            }
        }
        catch
        {
        }

        return fallback;
    }

    private sealed class IdentityCustomerDto
    {
        public int userId { get; set; }
        public string? userName { get; set; }
        public string? fullName { get; set; }
        public string? email { get; set; }
        public string? phone { get; set; }
        public DateTime createdAt { get; set; }
        public string? address { get; set; }
    }

    private sealed class CustomerMetricDto
    {
        public int userId { get; set; }
        public int orderCount { get; set; }
        public decimal totalSpent { get; set; }
    }

    private sealed class CustomerMetricsResponse
    {
        public bool success { get; set; }
        public List<CustomerMetricDto>? data { get; set; }
    }

    private sealed class CustomerIdsResponse
    {
        public bool success { get; set; }
        public List<int>? data { get; set; }
    }

    private sealed class CustomerOrderCheckResponse
    {
        public bool success { get; set; }
        public bool hasOrders { get; set; }
    }

    public sealed class SearchRequest
    {
        public string? keyword { get; set; }
    }

    private sealed class IdentityUserLookupDto
    {
        public int userId { get; set; }
        public string? userName { get; set; }
        public string? fullName { get; set; }
        public string? email { get; set; }
        public string? phone { get; set; }
        public DateTime created { get; set; }
    }
}
