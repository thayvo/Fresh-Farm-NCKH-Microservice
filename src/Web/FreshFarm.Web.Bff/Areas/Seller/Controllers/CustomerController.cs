using FreshFarm.Web.Bff.Areas.Seller.Infrastructure;
using FreshFarm.Web.Bff.Areas.Seller.Models;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;

namespace FreshFarm.Web.Bff.Areas.Seller.Controllers;

[Authorize(Roles = "Seller")]
[ResponseCache(Duration = 0, Location = ResponseCacheLocation.None, NoStore = true)]
[Area("Seller")]
public class CustomerController : LegacySellerControllerBase
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

            var sellerCustomerIds = await GetSellerCustomerIdsAsync();
            var customers = await GetCustomersAsync(keyword: null, take: 5000, sellerCustomerIds);
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

            return View(pagedUsers);
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"Error in ManageCustomers: {ex.Message}");
            TempData["ErrorMessage"] = "Co loi xay ra khi tai danh sach khach hang";
            return View(new List<CustomerViewModel>());
        }
    }

    public IActionResult Create()
    {
        return View(new CreateCustomerViewModel());
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Create(CreateCustomerViewModel model)
    {
        if (!ModelState.IsValid)
        {
            return View(model);
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
            ModelState.AddModelError(string.Empty, await ReadApiErrorAsync(response, "Khong the them khach hang"));
            return View(model);
        }

        TempData["SuccessMessage"] = $"Them khach hang '{model.FullName}' thanh cong!";
        return RedirectToAction(nameof(ManageCustomers));
    }

    public async Task<IActionResult> Edit(int id)
    {
        try
        {
            if (!await CanAccessCustomerAsync(id))
            {
                TempData["ErrorMessage"] = "Ban khong co quyen xem khach hang nay.";
                return RedirectToAction(nameof(ManageCustomers));
            }

            var client = CreateAuthorizedClient("Identity");
            var response = await client.GetAsync($"/auth/admin/customers/{id}");
            if (!response.IsSuccessStatusCode)
            {
                TempData["ErrorMessage"] = await ReadApiErrorAsync(response, "Khong tim thay khach hang");
                return RedirectToAction(nameof(ManageCustomers));
            }

            var dto = await response.Content.ReadFromJsonAsync<IdentityCustomerDto>(JsonOptions);
            if (dto is null)
            {
                TempData["ErrorMessage"] = "Khong doc duoc du lieu khach hang";
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

            return View(model);
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"[ERROR] Load edit form: {ex.Message}");
            TempData["ErrorMessage"] = "Co loi xay ra khi tai thong tin khach hang";
            return RedirectToAction(nameof(ManageCustomers));
        }
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Edit(EditCustomerViewModel model)
    {
        if (!ModelState.IsValid)
        {
            return View(model);
        }

        if (!await CanAccessCustomerAsync(model.UserID))
        {
            TempData["ErrorMessage"] = "Ban khong co quyen cap nhat khach hang nay.";
            return RedirectToAction(nameof(ManageCustomers));
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
            ModelState.AddModelError(string.Empty, await ReadApiErrorAsync(response, "Khong the cap nhat khach hang"));
            return View(model);
        }

        TempData["SuccessMessage"] = $"Cap nhat thong tin khach hang '{model.FullName}' thanh cong!";
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
                return Json(new { success = false, message = "ID khach hang khong hop le" });
            }

            if (!await CanAccessCustomerAsync(id))
            {
                return Json(new { success = false, message = "Ban khong co quyen xoa khach hang nay." });
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
                        message = "Khong the xoa khach hang da co don hang. Ban co the vo hieu hoa tai khoan thay vi xoa."
                    });
                }
            }

            var identityClient = CreateAuthorizedClient("Identity");
            var deleteResponse = await identityClient.DeleteAsync($"/auth/admin/customers/{id}");
            if (!deleteResponse.IsSuccessStatusCode)
            {
                return Json(new { success = false, message = await ReadApiErrorAsync(deleteResponse, "Khong the xoa khach hang") });
            }

            return Json(new { success = true, message = "Xoa khach hang thanh cong" });
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"[ERROR] Delete customer: {ex.Message}");
            return Json(new { success = false, message = "Co loi xay ra khi xoa khach hang" });
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
                return Json(new { success = false, message = "Vui long nhap tu khoa tim kiem" });
            }

            var sellerCustomerIds = await GetSellerCustomerIdsAsync();
            var customers = await GetCustomersAsync(keyword, 200, sellerCustomerIds);
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
                message = $"Tim thay {results.Count} ket qua",
                data = results
            });
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"[ERROR] Search customers: {ex.Message}");
            return Json(new { success = false, message = "Co loi xay ra khi tim kiem" });
        }
    }

    private async Task<List<IdentityCustomerDto>> GetCustomersAsync(string? keyword, int take, IReadOnlyCollection<int>? sellerCustomerIds = null)
    {
        if (sellerCustomerIds is { Count: 0 })
        {
            return new List<IdentityCustomerDto>();
        }

        var query = $"take={take}";
        if (!string.IsNullOrWhiteSpace(keyword))
        {
            query += $"&keyword={Uri.EscapeDataString(keyword)}";
        }

        var client = CreateAuthorizedClient("Identity");
        var response = await client.GetAsync($"/auth/admin/customers?{query}");
        if (!response.IsSuccessStatusCode)
        {
            return new List<IdentityCustomerDto>();
        }

        var customers = await response.Content.ReadFromJsonAsync<List<IdentityCustomerDto>>(JsonOptions)
            ?? new List<IdentityCustomerDto>();

        if (sellerCustomerIds is { Count: > 0 })
        {
            var allowed = sellerCustomerIds.ToHashSet();
            customers = customers.Where(c => allowed.Contains(c.userId)).ToList();
        }

        return customers;
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

    private async Task<HashSet<int>> GetSellerCustomerIdsAsync()
    {
        var client = CreateAuthorizedClient("Ordering");
        var response = await client.GetAsync("/api/orders/admin/customers/ids");
        if (!response.IsSuccessStatusCode)
        {
            return new HashSet<int>();
        }

        var payload = await response.Content.ReadFromJsonAsync<SellerCustomerIdsResponse>(JsonOptions);
        if (payload?.success != true || payload.data is null)
        {
            return new HashSet<int>();
        }

        return payload.data.Where(id => id > 0).ToHashSet();
    }

    private async Task<bool> CanAccessCustomerAsync(int userId)
    {
        if (userId <= 0)
        {
            return false;
        }

        var sellerCustomerIds = await GetSellerCustomerIdsAsync();
        return sellerCustomerIds.Contains(userId);
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
            // ignore parse failure
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

    private sealed class CustomerOrderCheckResponse
    {
        public bool success { get; set; }

        public bool hasOrders { get; set; }
    }

    private sealed class SellerCustomerIdsResponse
    {
        public bool success { get; set; }

        public List<int>? data { get; set; }
    }

    public sealed class SearchRequest
    {
        public string? keyword { get; set; }
    }
}
