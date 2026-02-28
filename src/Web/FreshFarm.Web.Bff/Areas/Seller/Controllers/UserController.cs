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
public class UserController : LegacySellerControllerBase
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
    public async Task<IActionResult> ManageUsers()
    {
        return await RenderManageUsersAsync(searchTerm: null);
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> SearchUsers(string searchTerm)
    {
        return await RenderManageUsersAsync(searchTerm);
    }

    [HttpGet]
    public async Task<JsonResult> GetUserById(int id)
    {
        try
        {
            if (id <= 0)
            {
                return Json(new { success = false, message = "ID nguoi dung khong hop le" });
            }

            var client = CreateAuthorizedClient("Identity");
            var response = await client.GetAsync($"/auth/admin/users/{id}");
            if (!response.IsSuccessStatusCode)
            {
                return Json(new { success = false, message = await ReadApiErrorAsync(response, "Khong tim thay quan tri vien") });
            }

            var user = await response.Content.ReadFromJsonAsync<AdminUserApiDto>(JsonOptions);
            if (user is null)
            {
                return Json(new { success = false, message = "Khong doc duoc thong tin quan tri vien" });
            }

            return Json(new
            {
                success = true,
                data = new
                {
                    userId = user.userId,
                    userName = user.userName,
                    fullName = user.fullName,
                    email = user.email,
                    phone = user.phone,
                    avatar = user.avatar,
                    roleId = user.roleId,
                    roleName = user.role?.roleName,
                    isActive = user.isActive,
                    lastLogin = user.lastLogin?.ToString("yyyy-MM-ddTHH:mm"),
                    created = user.created.ToString("yyyy-MM-ddTHH:mm"),
                    updated = user.updated?.ToString("yyyy-MM-ddTHH:mm")
                }
            });
        }
        catch (Exception ex)
        {
            return Json(new { success = false, message = "Loi: " + ex.Message });
        }
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<JsonResult> CreateUser(string userName, string fullName, string email, string password, string? phone, string? avatar, bool? isActive, int? roleId)
    {
        try
        {
            userName = (userName ?? string.Empty).Trim();
            fullName = (fullName ?? string.Empty).Trim();
            email = (email ?? string.Empty).Trim();
            password = (password ?? string.Empty).Trim();
            phone = string.IsNullOrWhiteSpace(phone) ? null : phone.Trim();
            avatar = string.IsNullOrWhiteSpace(avatar) ? null : avatar.Trim();

            if (string.IsNullOrWhiteSpace(userName))
            {
                return Json(new { success = false, message = "Ten dang nhap khong duoc de trong!" });
            }

            if (string.IsNullOrWhiteSpace(fullName))
            {
                return Json(new { success = false, message = "Ho ten khong duoc de trong!" });
            }

            if (string.IsNullOrWhiteSpace(email))
            {
                return Json(new { success = false, message = "Email khong duoc de trong!" });
            }

            if (string.IsNullOrWhiteSpace(password))
            {
                return Json(new { success = false, message = "Mat khau khong duoc de trong!" });
            }

            if (!roleId.HasValue || roleId.Value <= 0)
            {
                return Json(new { success = false, message = "Vui long chon quyen cho nguoi dung!" });
            }

            var client = CreateAuthorizedClient("Identity");
            var response = await client.PostAsJsonAsync("/auth/admin/users", new
            {
                userName,
                fullName,
                email,
                password,
                phone,
                avatar,
                isActive = isActive ?? true,
                roleId
            });

            if (!response.IsSuccessStatusCode)
            {
                return Json(new { success = false, message = await ReadApiErrorAsync(response, "Khong the them quan tri vien") });
            }

            return Json(new { success = true, message = "Them quan tri vien thanh cong!" });
        }
        catch (Exception ex)
        {
            return Json(new { success = false, message = "Loi: " + ex.Message });
        }
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<JsonResult> UpdateUser(int userId, string userName, string fullName, string email, string? phone, string? avatar, bool? isActive, int? roleId, string? newPassword = null)
    {
        try
        {
            userName = (userName ?? string.Empty).Trim();
            fullName = (fullName ?? string.Empty).Trim();
            email = (email ?? string.Empty).Trim();
            phone = string.IsNullOrWhiteSpace(phone) ? null : phone.Trim();
            avatar = string.IsNullOrWhiteSpace(avatar) ? null : avatar.Trim();
            newPassword = string.IsNullOrWhiteSpace(newPassword) ? null : newPassword.Trim();

            if (userId <= 0)
            {
                return Json(new { success = false, message = "ID nguoi dung khong hop le" });
            }

            if (string.IsNullOrWhiteSpace(userName))
            {
                return Json(new { success = false, message = "Ten dang nhap khong duoc de trong!" });
            }

            if (string.IsNullOrWhiteSpace(fullName))
            {
                return Json(new { success = false, message = "Ho ten khong duoc de trong!" });
            }

            if (string.IsNullOrWhiteSpace(email))
            {
                return Json(new { success = false, message = "Email khong duoc de trong!" });
            }

            if (!roleId.HasValue || roleId.Value <= 0)
            {
                return Json(new { success = false, message = "Vui long chon quyen cho nguoi dung!" });
            }

            var client = CreateAuthorizedClient("Identity");
            var response = await client.PutAsJsonAsync($"/auth/admin/users/{userId}", new
            {
                userName,
                fullName,
                email,
                phone,
                avatar,
                isActive,
                roleId,
                newPassword
            });

            if (!response.IsSuccessStatusCode)
            {
                return Json(new { success = false, message = await ReadApiErrorAsync(response, "Khong the cap nhat quan tri vien") });
            }

            return Json(new { success = true, message = "Cap nhat quan tri vien thanh cong!" });
        }
        catch (Exception ex)
        {
            return Json(new { success = false, message = "Loi: " + ex.Message });
        }
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<JsonResult> DeleteUser(int userId)
    {
        try
        {
            if (userId <= 0)
            {
                return Json(new { success = false, message = "ID nguoi dung khong hop le" });
            }

            var client = CreateAuthorizedClient("Identity");
            var response = await client.DeleteAsync($"/auth/admin/users/{userId}");
            if (!response.IsSuccessStatusCode)
            {
                return Json(new { success = false, message = await ReadApiErrorAsync(response, "Khong the xoa quan tri vien") });
            }

            return Json(new { success = true, message = "Xoa quan tri vien thanh cong!" });
        }
        catch (Exception ex)
        {
            return Json(new { success = false, message = "Loi: " + ex.Message });
        }
    }

    private async Task<IActionResult> RenderManageUsersAsync(string? searchTerm)
    {
        var usersTask = GetUsersAsync(searchTerm);
        var rolesTask = GetRolesAsync();

        await Task.WhenAll(usersTask, rolesTask);

        ViewBag.Roles = rolesTask.Result;
        ViewBag.SearchTerm = searchTerm ?? string.Empty;

        return View("ManageUsers", usersTask.Result);
    }

    private async Task<List<SellerUserViewModel>> GetUsersAsync(string? searchTerm)
    {
        var client = CreateAuthorizedClient("Identity");

        var query = "take=5000";
        if (!string.IsNullOrWhiteSpace(searchTerm))
        {
            query += "&search=" + Uri.EscapeDataString(searchTerm.Trim());
        }

        var response = await client.GetAsync($"/auth/admin/users?{query}");
        if (!response.IsSuccessStatusCode)
        {
            return new List<SellerUserViewModel>();
        }

        var payload = await response.Content.ReadFromJsonAsync<List<AdminUserApiDto>>(JsonOptions)
            ?? new List<AdminUserApiDto>();

        return payload.Select(MapUser).ToList();
    }

    private async Task<List<SellerRoleViewModel>> GetRolesAsync()
    {
        var client = CreateAuthorizedClient("Identity");
        var response = await client.GetAsync("/auth/admin/users/roles");
        if (!response.IsSuccessStatusCode)
        {
            return new List<SellerRoleViewModel>();
        }

        var payload = await response.Content.ReadFromJsonAsync<List<AdminRoleApiDto>>(JsonOptions)
            ?? new List<AdminRoleApiDto>();

        return payload
            .OrderBy(x => x.roleName)
            .Select(x => new SellerRoleViewModel
            {
                RoleID = x.roleId,
                RoleName = x.roleName ?? string.Empty,
                IsActive = x.isActive
            })
            .ToList();
    }

    private HttpClient CreateAuthorizedClient(string clientName)
    {
        var client = _httpClientFactory.CreateClient(clientName);

        client.DefaultRequestHeaders.Remove("Authorization");
        var token = HttpContext.Session.GetString(AccessTokenSessionKey);
        if (!string.IsNullOrWhiteSpace(token))
        {
            client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);
        }

        return client;
    }

    private static SellerUserViewModel MapUser(AdminUserApiDto dto)
    {
        return new SellerUserViewModel
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
                : new SellerRoleViewModel
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
}
