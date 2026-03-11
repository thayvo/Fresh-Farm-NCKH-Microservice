using FreshFarm.Web.Bff.Areas.Admin.Models;
using FreshFarm.Web.Bff.Areas.Seller.Infrastructure;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;

namespace FreshFarm.Web.Bff.Areas.Admin.Controllers;

[Authorize(Policy = "AdminOnly")]
[ResponseCache(Duration = 0, Location = ResponseCacheLocation.None, NoStore = true)]
[Area("Admin")]
public sealed class UserController : LegacySellerControllerBase
{
    private const string AccessTokenSessionKey = "ACCESS_TOKEN";

    private readonly IHttpClientFactory _httpClientFactory;

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true
    };

    public UserController(IHttpClientFactory httpClientFactory)
    {
        _httpClientFactory = httpClientFactory;
    }

    [HttpGet]
    public async Task<IActionResult> ManageUsers(string? userType = "all", bool? isActive = null, DateTime? createdFrom = null, DateTime? createdTo = null)
    {
        return await RenderManageUsersAsync(null, userType, isActive, createdFrom, createdTo);
    }

    [HttpGet]
    public async Task<IActionResult> ManageAdmins(string? searchTerm = null, bool? isActive = null, DateTime? createdFrom = null, DateTime? createdTo = null)
    {
        return await RenderManageUsersAsync(searchTerm, "admin", isActive, createdFrom, createdTo);
    }

    [HttpGet]
    public async Task<IActionResult> ManageSellers(string? searchTerm = null, bool? isActive = null, DateTime? createdFrom = null, DateTime? createdTo = null)
    {
        return await RenderManageUsersAsync(searchTerm, "seller", isActive, createdFrom, createdTo);
    }

    [HttpGet]
    public async Task<IActionResult> ManageBuyers(string? searchTerm = null, bool? isActive = null, DateTime? createdFrom = null, DateTime? createdTo = null)
    {
        return await RenderManageUsersAsync(searchTerm, "buyer", isActive, createdFrom, createdTo);
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> SearchUsers(string? searchTerm, string? userType = "all", bool? isActive = null, DateTime? createdFrom = null, DateTime? createdTo = null)
    {
        return await RenderManageUsersAsync(searchTerm, userType, isActive, createdFrom, createdTo);
    }

    [HttpGet]
    public async Task<JsonResult> GetUserById(int id)
    {
        var client = CreateAuthorizedClient("Identity");
        var response = await client.GetAsync($"/auth/admin/users/{id}");
        if (!response.IsSuccessStatusCode)
        {
            Response.StatusCode = (int)response.StatusCode;
            var message = await ReadApiErrorAsync(response, "Khong lay duoc thong tin nguoi dung.");
            return Json(new { success = false, message });
        }

        var payload = await response.Content.ReadFromJsonAsync<AdminUserApiDto>(JsonOptions);
        if (payload is null)
        {
            Response.StatusCode = StatusCodes.Status500InternalServerError;
            return Json(new { success = false, message = "Du lieu tra ve khong hop le." });
        }

        return Json(new
        {
            success = true,
            data = MapUser(payload)
        });
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<JsonResult> CreateUser(string userName, string fullName, string email, string password, string? phone, string? avatar, bool? isActive, int? roleId)
    {
        var client = CreateAuthorizedClient("Identity");
        var request = new AdminCreateUserApiRequest
        {
            UserName = userName,
            FullName = fullName,
            Email = email,
            Password = password,
            Phone = phone,
            Avatar = avatar,
            IsActive = isActive,
            RoleId = roleId
        };

        var response = await client.PostAsJsonAsync("/auth/admin/users", request);
        if (!response.IsSuccessStatusCode)
        {
            Response.StatusCode = (int)response.StatusCode;
            var message = await ReadApiErrorAsync(response, "Khong tao duoc nguoi dung.");
            return Json(new { success = false, message });
        }

        return Json(new { success = true, message = "Them nguoi dung thanh cong." });
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<JsonResult> UpdateUser(int userId, string userName, string fullName, string email, string? phone, string? avatar, bool? isActive, int? roleId, string? newPassword = null)
    {
        var client = CreateAuthorizedClient("Identity");
        var request = new AdminUpdateUserApiRequest
        {
            UserName = userName,
            FullName = fullName,
            Email = email,
            Phone = phone,
            Avatar = avatar,
            IsActive = isActive,
            RoleId = roleId,
            NewPassword = newPassword
        };

        var response = await client.PutAsJsonAsync($"/auth/admin/users/{userId}", request);
        if (!response.IsSuccessStatusCode)
        {
            Response.StatusCode = (int)response.StatusCode;
            var message = await ReadApiErrorAsync(response, "Khong cap nhat duoc nguoi dung.");
            return Json(new { success = false, message });
        }

        return Json(new { success = true, message = "Cap nhat nguoi dung thanh cong." });
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<JsonResult> DeleteUser(int userId)
    {
        var client = CreateAuthorizedClient("Identity");
        var response = await client.DeleteAsync($"/auth/admin/users/{userId}");
        if (!response.IsSuccessStatusCode)
        {
            Response.StatusCode = (int)response.StatusCode;
            var message = await ReadApiErrorAsync(response, "Khong xoa duoc nguoi dung.");
            return Json(new { success = false, message });
        }

        return Json(new { success = true, message = "Da vo hieu hoa tai khoan nguoi dung." });
    }

    private async Task<IActionResult> RenderManageUsersAsync(string? searchTerm, string? userType, bool? isActive, DateTime? createdFrom, DateTime? createdTo)
    {
        var normalizedUserType = NormalizeUserType(userType);
        var usersTask = GetUsersAsync(searchTerm, normalizedUserType, isActive, createdFrom, createdTo);
        var rolesTask = GetRolesAsync(normalizedUserType);

        await Task.WhenAll(usersTask, rolesTask);

        var model = new AdminUserManagementPageViewModel
        {
            UserType = normalizedUserType,
            SearchTerm = searchTerm?.Trim() ?? string.Empty,
            IsActive = isActive,
            CreatedFrom = createdFrom?.Date,
            CreatedTo = createdTo?.Date,
            Roles = rolesTask.Result,
            Users = usersTask.Result
        };

        return View("ManageUsers", model);
    }

    private async Task<List<AdminUserViewModel>> GetUsersAsync(string? searchTerm, string userType, bool? isActive, DateTime? createdFrom, DateTime? createdTo)
    {
        var client = CreateAuthorizedClient("Identity");

        var query = "take=5000";
        if (!string.IsNullOrWhiteSpace(searchTerm))
        {
            query += "&search=" + Uri.EscapeDataString(searchTerm.Trim());
        }
        if (!string.Equals(userType, "all", StringComparison.OrdinalIgnoreCase))
        {
            query += "&userType=" + Uri.EscapeDataString(userType);
        }
        if (isActive.HasValue)
        {
            query += "&isActive=" + (isActive.Value ? "true" : "false");
        }
        if (createdFrom.HasValue)
        {
            query += "&createdFrom=" + Uri.EscapeDataString(createdFrom.Value.ToString("yyyy-MM-dd"));
        }
        if (createdTo.HasValue)
        {
            query += "&createdTo=" + Uri.EscapeDataString(createdTo.Value.ToString("yyyy-MM-dd"));
        }

        var response = await client.GetAsync($"/auth/admin/users?{query}");
        if (!response.IsSuccessStatusCode)
        {
            return new List<AdminUserViewModel>();
        }

        var payload = await response.Content.ReadFromJsonAsync<List<AdminUserApiDto>>(JsonOptions)
            ?? new List<AdminUserApiDto>();

        return payload.Select(MapUser).ToList();
    }

    private static string NormalizeUserType(string? userType)
    {
        if (string.IsNullOrWhiteSpace(userType))
        {
            return "all";
        }

        var normalized = userType.Trim().ToLowerInvariant();
        return normalized is "all" or "admin" or "seller" or "buyer" ? normalized : "all";
    }

    private async Task<List<AdminRoleViewModel>> GetRolesAsync(string userType)
    {
        var client = CreateAuthorizedClient("Identity");
        var response = await client.GetAsync("/auth/admin/users/roles");
        if (!response.IsSuccessStatusCode)
        {
            return new List<AdminRoleViewModel>();
        }

        var payload = await response.Content.ReadFromJsonAsync<List<AdminRoleApiDto>>(JsonOptions)
            ?? new List<AdminRoleApiDto>();

        var roles = payload
            .OrderBy(x => x.roleName)
            .Select(x => new AdminRoleViewModel
            {
                RoleID = x.roleId,
                RoleName = x.roleName ?? string.Empty,
                IsActive = x.isActive
            })
            .ToList();

        return userType switch
        {
            "admin" => roles.Where(r => string.Equals(r.RoleName, "Admin", StringComparison.OrdinalIgnoreCase)).ToList(),
            "seller" => roles.Where(r => string.Equals(r.RoleName, "Seller", StringComparison.OrdinalIgnoreCase)).ToList(),
            "buyer" => roles.Where(r => string.Equals(r.RoleName, "Customer", StringComparison.OrdinalIgnoreCase)).ToList(),
            _ => roles
        };
    }

    private HttpClient CreateAuthorizedClient(string clientName)
    {
        var client = _httpClientFactory.CreateClient(clientName);

        client.DefaultRequestHeaders.Remove("Authorization");
        var token = GetAccessToken(AccessTokenSessionKey);
        if (!string.IsNullOrWhiteSpace(token))
        {
            client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);
        }

        return client;
    }

    private static AdminUserViewModel MapUser(AdminUserApiDto dto)
    {
        return new AdminUserViewModel
        {
            AdminID = dto.userId,
            UserName = dto.userName ?? string.Empty,
            FullName = dto.fullName ?? string.Empty,
            Email = dto.email ?? string.Empty,
            Phone = dto.phone,
            Avatar = dto.avatar,
            RoleID = dto.roleId,
            Role = dto.role is null
                ? null
                : new AdminRoleViewModel
                {
                    RoleID = dto.role.roleId,
                    RoleName = dto.role.roleName ?? string.Empty,
                    IsActive = dto.role.isActive
                },
            IsActive = dto.isActive,
            LastLogin = dto.lastLogin,
            CreatedDate = dto.created,
            UpdatedDate = dto.updated
        };
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
            var root = doc.RootElement;

            if (root.ValueKind == JsonValueKind.Object)
            {
                if (root.TryGetProperty("message", out var messageProp) && messageProp.ValueKind == JsonValueKind.String)
                {
                    return messageProp.GetString() ?? fallback;
                }

                if (root.TryGetProperty("detail", out var detailProp) && detailProp.ValueKind == JsonValueKind.String)
                {
                    return detailProp.GetString() ?? fallback;
                }

                if (root.TryGetProperty("title", out var titleProp) && titleProp.ValueKind == JsonValueKind.String)
                {
                    return titleProp.GetString() ?? fallback;
                }
            }

            return json;
        }
        catch
        {
            return fallback;
        }
    }

    private sealed class AdminUserApiDto
    {
        public int userId { get; set; }

        public string? userName { get; set; }

        public string? fullName { get; set; }

        public string? email { get; set; }

        public string? phone { get; set; }

        public string? avatar { get; set; }

        public int roleId { get; set; }

        public AdminRoleApiDto? role { get; set; }

        public bool isActive { get; set; }

        public DateTime? lastLogin { get; set; }

        public DateTime created { get; set; }

        public DateTime? updated { get; set; }
    }

    private sealed class AdminRoleApiDto
    {
        public int roleId { get; set; }

        public string? roleName { get; set; }

        public bool isActive { get; set; }
    }

    private sealed class AdminCreateUserApiRequest
    {
        public string UserName { get; set; } = string.Empty;

        public string FullName { get; set; } = string.Empty;

        public string Email { get; set; } = string.Empty;

        public string Password { get; set; } = string.Empty;

        public string? Phone { get; set; }

        public string? Avatar { get; set; }

        public bool? IsActive { get; set; }

        public int? RoleId { get; set; }
    }

    private sealed class AdminUpdateUserApiRequest
    {
        public string UserName { get; set; } = string.Empty;

        public string FullName { get; set; } = string.Empty;

        public string Email { get; set; } = string.Empty;

        public string? NewPassword { get; set; }

        public string? Phone { get; set; }

        public string? Avatar { get; set; }

        public bool? IsActive { get; set; }

        public int? RoleId { get; set; }
    }
}
