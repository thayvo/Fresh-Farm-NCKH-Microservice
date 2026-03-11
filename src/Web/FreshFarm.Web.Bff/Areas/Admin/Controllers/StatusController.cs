using System.Globalization;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using FreshFarm.Web.Bff.Areas.Admin.Models;
using FreshFarm.Web.Bff.Areas.Seller.Infrastructure;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Rendering;

namespace FreshFarm.Web.Bff.Areas.Admin.Controllers;

[Authorize(Policy = "AdminOnly")]
[ResponseCache(Duration = 0, Location = ResponseCacheLocation.None, NoStore = true)]
[Area("Admin")]
public sealed class StatusController : LegacySellerControllerBase
{
    private const string AccessTokenSessionKey = "ACCESS_TOKEN";

    private readonly IHttpClientFactory _httpClientFactory;

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true
    };

    public StatusController(IHttpClientFactory httpClientFactory)
    {
        _httpClientFactory = httpClientFactory;
    }

    [HttpGet]
    public async Task<IActionResult> Status(string searchTerm = "", int? statusTypeID = null, int page = 1)
    {
        const int pageSize = 6;
        var model = new List<AdminStatusViewModel>();

        try
        {
            var client = CreateAuthorizedClient("Ordering");
            var response = await client.GetAsync(BuildStatusEndpoint(searchTerm, statusTypeID, page, pageSize));
            if (!response.IsSuccessStatusCode)
            {
                TempData["ErrorMessage"] = await ReadApiErrorAsync(response, "Khong the tai danh sach trang thai.");
                await ApplyStatusViewBagFallbackAsync(client, searchTerm, statusTypeID, page, pageSize);
                return View(model);
            }

            var payload = await response.Content.ReadFromJsonAsync<StatusesPageResponse>(JsonOptions);
            if (payload is null)
            {
                TempData["ErrorMessage"] = "Khong doc duoc du lieu trang thai.";
                await ApplyStatusViewBagFallbackAsync(client, searchTerm, statusTypeID, page, pageSize);
                return View(model);
            }

            model = (payload.statuses ?? new List<StatusRowDto>())
                .Select(MapStatus)
                .ToList();

            ViewBag.CurrentPage = payload.page > 0 ? payload.page : 1;
            ViewBag.TotalPages = payload.totalPages;
            ViewBag.TotalItems = payload.totalItems;
            ViewBag.PageSize = payload.pageSize > 0 ? payload.pageSize : pageSize;
            ViewBag.SearchTerm = payload.searchTerm ?? searchTerm;
            ViewBag.StatusTypeID = statusTypeID;

            var statusTypeRows = payload.statusTypes ?? await LoadStatusTypeLookupAsync(client);
            ViewBag.StatusTypes = BuildStatusTypeSelectList(statusTypeRows, statusTypeID);
        }
        catch (Exception ex)
        {
            TempData["ErrorMessage"] = "Loi tai danh sach trang thai: " + ex.Message;
            var fallbackClient = CreateAuthorizedClient("Ordering");
            await ApplyStatusViewBagFallbackAsync(fallbackClient, searchTerm, statusTypeID, page, pageSize);
        }

        return View(model);
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<JsonResult> CreateStatus(AdminStatusViewModel status)
    {
        if (string.IsNullOrWhiteSpace(status.StatusName))
        {
            return Json(new { success = false, message = "Vui long nhap ten trang thai!" });
        }

        if (status.StatusTypeID <= 0)
        {
            return Json(new { success = false, message = "Vui long chon loai trang thai!" });
        }

        try
        {
            var client = CreateAuthorizedClient("Ordering");
            var response = await client.PostAsJsonAsync("/api/orders/admin/statuses", new
            {
                statusName = status.StatusName.Trim(),
                statusTypeID = status.StatusTypeID,
                colorCode = status.ColorCode,
                note = status.Note,
                displayOrder = status.DisplayOrder
            });

            if (!response.IsSuccessStatusCode)
            {
                return Json(new { success = false, message = await ReadApiErrorAsync(response, "Khong the them trang thai.") });
            }

            var payload = await response.Content.ReadFromJsonAsync<BasicSuccessResponse>(JsonOptions);
            return Json(new { success = true, message = payload?.message ?? "Them trang thai thanh cong!" });
        }
        catch (Exception ex)
        {
            return Json(new { success = false, message = "Co loi xay ra: " + ex.Message });
        }
    }

    [HttpGet]
    public async Task<JsonResult> GetStatusDetail(int id)
    {
        if (id <= 0)
        {
            return Json(new { success = false, message = "ID trang thai khong hop le." });
        }

        try
        {
            var client = CreateAuthorizedClient("Ordering");
            var response = await client.GetAsync($"/api/orders/admin/statuses/{id}");
            if (!response.IsSuccessStatusCode)
            {
                return Json(new { success = false, message = await ReadApiErrorAsync(response, "Khong tim thay trang thai.") });
            }

            var payload = await response.Content.ReadFromJsonAsync<StatusDetailResponse>(JsonOptions);
            if (payload is null)
            {
                return Json(new { success = false, message = "Khong doc duoc chi tiet trang thai." });
            }

            return Json(new
            {
                success = true,
                data = new
                {
                    StatusID = payload.statusID,
                    StatusName = payload.statusName ?? string.Empty,
                    StatusTypeID = payload.statusTypeID,
                    ColorCode = payload.colorCode ?? "#28a745",
                    Note = payload.note ?? string.Empty,
                    DisplayOrder = payload.displayOrder
                }
            });
        }
        catch (Exception ex)
        {
            return Json(new { success = false, message = ex.Message });
        }
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<JsonResult> EditStatus(AdminStatusViewModel status)
    {
        if (status.StatusID <= 0)
        {
            return Json(new { success = false, message = "ID trang thai khong hop le." });
        }

        if (string.IsNullOrWhiteSpace(status.StatusName))
        {
            return Json(new { success = false, message = "Vui long nhap ten trang thai!" });
        }

        if (status.StatusTypeID <= 0)
        {
            return Json(new { success = false, message = "Vui long chon loai trang thai!" });
        }

        try
        {
            var client = CreateAuthorizedClient("Ordering");
            var response = await client.PutAsJsonAsync($"/api/orders/admin/statuses/{status.StatusID}", new
            {
                statusName = status.StatusName.Trim(),
                statusTypeID = status.StatusTypeID,
                colorCode = status.ColorCode,
                note = status.Note,
                displayOrder = status.DisplayOrder
            });

            if (!response.IsSuccessStatusCode)
            {
                return Json(new { success = false, message = await ReadApiErrorAsync(response, "Khong the cap nhat trang thai.") });
            }

            var payload = await response.Content.ReadFromJsonAsync<BasicSuccessResponse>(JsonOptions);
            return Json(new { success = true, message = payload?.message ?? "Cap nhat trang thai thanh cong!" });
        }
        catch (Exception ex)
        {
            return Json(new { success = false, message = "Co loi xay ra: " + ex.Message });
        }
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<JsonResult> DeleteStatus(int id)
    {
        if (id <= 0)
        {
            return Json(new { success = false, message = "ID trang thai khong hop le." });
        }

        try
        {
            var client = CreateAuthorizedClient("Ordering");
            var response = await client.DeleteAsync($"/api/orders/admin/statuses/{id}");
            if (!response.IsSuccessStatusCode)
            {
                return Json(new { success = false, message = await ReadApiErrorAsync(response, "Khong the xoa trang thai.") });
            }

            var payload = await response.Content.ReadFromJsonAsync<BasicSuccessResponse>(JsonOptions);
            return Json(new { success = true, message = payload?.message ?? "Xoa trang thai thanh cong!" });
        }
        catch (Exception ex)
        {
            return Json(new { success = false, message = "Co loi xay ra: " + ex.Message });
        }
    }

    [HttpGet]
    public async Task<IActionResult> StatusType(string searchTerm = "", int page = 1)
    {
        const int pageSize = 6;
        var model = new List<AdminStatusTypeViewModel>();

        try
        {
            var client = CreateAuthorizedClient("Ordering");
            var response = await client.GetAsync(BuildStatusTypeEndpoint(searchTerm, page, pageSize));
            if (!response.IsSuccessStatusCode)
            {
                TempData["ErrorMessage"] = await ReadApiErrorAsync(response, "Khong the tai danh sach loai trang thai.");
                SetStatusTypeViewBagDefaults(searchTerm, page, pageSize);
                return View(model);
            }

            var payload = await response.Content.ReadFromJsonAsync<StatusTypesPageResponse>(JsonOptions);
            if (payload is null)
            {
                TempData["ErrorMessage"] = "Khong doc duoc du lieu loai trang thai.";
                SetStatusTypeViewBagDefaults(searchTerm, page, pageSize);
                return View(model);
            }

            model = (payload.statusTypes ?? new List<StatusTypeRowDto>())
                .Select(MapStatusType)
                .ToList();

            ViewBag.CurrentPage = payload.page > 0 ? payload.page : 1;
            ViewBag.TotalPages = payload.totalPages;
            ViewBag.TotalItems = payload.totalItems;
            ViewBag.PageSize = payload.pageSize > 0 ? payload.pageSize : pageSize;
            ViewBag.SearchTerm = payload.searchTerm ?? searchTerm;
        }
        catch (Exception ex)
        {
            TempData["ErrorMessage"] = "Loi tai danh sach loai trang thai: " + ex.Message;
            SetStatusTypeViewBagDefaults(searchTerm, page, pageSize);
        }

        return View(model);
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<JsonResult> CreateStatusType(AdminStatusTypeViewModel statusType)
    {
        if (string.IsNullOrWhiteSpace(statusType.StatusTypeName))
        {
            return Json(new { success = false, message = "Vui long nhap ten loai trang thai!" });
        }

        try
        {
            var client = CreateAuthorizedClient("Ordering");
            var response = await client.PostAsJsonAsync("/api/orders/admin/status-types", new
            {
                statusTypeName = statusType.StatusTypeName.Trim(),
                description = statusType.Description
            });

            if (!response.IsSuccessStatusCode)
            {
                return Json(new { success = false, message = await ReadApiErrorAsync(response, "Khong the them loai trang thai.") });
            }

            var payload = await response.Content.ReadFromJsonAsync<BasicSuccessResponse>(JsonOptions);
            return Json(new { success = true, message = payload?.message ?? "Them loai trang thai thanh cong!" });
        }
        catch (Exception ex)
        {
            return Json(new { success = false, message = "Co loi xay ra: " + ex.Message });
        }
    }

    [HttpGet]
    public async Task<JsonResult> GetStatusTypeDetail(int id)
    {
        if (id <= 0)
        {
            return Json(new { success = false, message = "ID loai trang thai khong hop le." });
        }

        try
        {
            var client = CreateAuthorizedClient("Ordering");
            var response = await client.GetAsync($"/api/orders/admin/status-types/{id}");
            if (!response.IsSuccessStatusCode)
            {
                return Json(new { success = false, message = await ReadApiErrorAsync(response, "Khong tim thay loai trang thai.") });
            }

            var payload = await response.Content.ReadFromJsonAsync<StatusTypeDetailResponse>(JsonOptions);
            if (payload is null)
            {
                return Json(new { success = false, message = "Khong doc duoc chi tiet loai trang thai." });
            }

            return Json(new
            {
                success = true,
                data = new
                {
                    StatusTypeID = payload.statusTypeID,
                    StatusTypeName = payload.statusTypeName ?? string.Empty,
                    Description = payload.description ?? string.Empty
                }
            });
        }
        catch (Exception ex)
        {
            return Json(new { success = false, message = ex.Message });
        }
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<JsonResult> EditStatusType(AdminStatusTypeViewModel statusType)
    {
        if (statusType.StatusTypeID <= 0)
        {
            return Json(new { success = false, message = "ID loai trang thai khong hop le." });
        }

        if (string.IsNullOrWhiteSpace(statusType.StatusTypeName))
        {
            return Json(new { success = false, message = "Vui long nhap ten loai trang thai!" });
        }

        try
        {
            var client = CreateAuthorizedClient("Ordering");
            var response = await client.PutAsJsonAsync($"/api/orders/admin/status-types/{statusType.StatusTypeID}", new
            {
                statusTypeName = statusType.StatusTypeName.Trim(),
                description = statusType.Description
            });

            if (!response.IsSuccessStatusCode)
            {
                return Json(new { success = false, message = await ReadApiErrorAsync(response, "Khong the cap nhat loai trang thai.") });
            }

            var payload = await response.Content.ReadFromJsonAsync<BasicSuccessResponse>(JsonOptions);
            return Json(new { success = true, message = payload?.message ?? "Cap nhat loai trang thai thanh cong!" });
        }
        catch (Exception ex)
        {
            return Json(new { success = false, message = "Co loi xay ra: " + ex.Message });
        }
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<JsonResult> DeleteStatusType(int id)
    {
        if (id <= 0)
        {
            return Json(new { success = false, message = "ID loai trang thai khong hop le." });
        }

        try
        {
            var client = CreateAuthorizedClient("Ordering");
            var response = await client.DeleteAsync($"/api/orders/admin/status-types/{id}");
            if (!response.IsSuccessStatusCode)
            {
                return Json(new { success = false, message = await ReadApiErrorAsync(response, "Khong the xoa loai trang thai.") });
            }

            var payload = await response.Content.ReadFromJsonAsync<BasicSuccessResponse>(JsonOptions);
            return Json(new { success = true, message = payload?.message ?? "Xoa loai trang thai thanh cong!" });
        }
        catch (Exception ex)
        {
            return Json(new { success = false, message = "Co loi xay ra: " + ex.Message });
        }
    }

    private static AdminStatusViewModel MapStatus(StatusRowDto dto)
    {
        return new AdminStatusViewModel
        {
            StatusID = dto.statusID,
            StatusName = dto.statusName ?? string.Empty,
            StatusTypeID = dto.statusTypeID,
            ColorCode = string.IsNullOrWhiteSpace(dto.colorCode) ? "#28a745" : dto.colorCode,
            Note = dto.note ?? string.Empty,
            DisplayOrder = dto.displayOrder,
            IsActive = dto.isActive,
            CreatedDate = dto.createdDate,
            StatusType = dto.statusType is null
                ? new AdminStatusTypeViewModel()
                : new AdminStatusTypeViewModel
                {
                    StatusTypeID = dto.statusType.statusTypeID,
                    StatusTypeName = dto.statusType.statusTypeName ?? string.Empty
                }
        };
    }

    private static AdminStatusTypeViewModel MapStatusType(StatusTypeRowDto dto)
    {
        return new AdminStatusTypeViewModel
        {
            StatusTypeID = dto.statusTypeID,
            StatusTypeName = dto.statusTypeName ?? string.Empty,
            Description = dto.description ?? string.Empty,
            IsActive = dto.isActive,
            CreatedDate = dto.createdDate,
            ActiveStatusCount = dto.activeStatusCount
        };
    }

    private static string BuildStatusEndpoint(string searchTerm, int? statusTypeId, int page, int pageSize)
    {
        var endpoint = new StringBuilder($"/api/orders/admin/statuses?page={page}&pageSize={pageSize}");
        if (!string.IsNullOrWhiteSpace(searchTerm))
        {
            endpoint.Append("&searchTerm=");
            endpoint.Append(Uri.EscapeDataString(searchTerm.Trim()));
        }

        if (statusTypeId.HasValue && statusTypeId.Value > 0)
        {
            endpoint.Append("&statusTypeId=");
            endpoint.Append(statusTypeId.Value.ToString(CultureInfo.InvariantCulture));
        }

        return endpoint.ToString();
    }

    private static string BuildStatusTypeEndpoint(string searchTerm, int page, int pageSize)
    {
        var endpoint = new StringBuilder($"/api/orders/admin/status-types?page={page}&pageSize={pageSize}");
        if (!string.IsNullOrWhiteSpace(searchTerm))
        {
            endpoint.Append("&searchTerm=");
            endpoint.Append(Uri.EscapeDataString(searchTerm.Trim()));
        }

        return endpoint.ToString();
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

    private async Task ApplyStatusViewBagFallbackAsync(
        HttpClient client,
        string searchTerm,
        int? statusTypeId,
        int page,
        int pageSize)
    {
        SetStatusViewBagDefaults(searchTerm, statusTypeId, page, pageSize);
        var lookup = await LoadStatusTypeLookupAsync(client);
        ViewBag.StatusTypes = BuildStatusTypeSelectList(lookup, statusTypeId);
    }

    private void SetStatusViewBagDefaults(string searchTerm, int? statusTypeId, int page, int pageSize)
    {
        ViewBag.CurrentPage = page;
        ViewBag.TotalPages = 0;
        ViewBag.TotalItems = 0;
        ViewBag.PageSize = pageSize;
        ViewBag.SearchTerm = searchTerm;
        ViewBag.StatusTypeID = statusTypeId;
        ViewBag.StatusTypes = BuildStatusTypeSelectList(new List<StatusTypeLookupDto>(), statusTypeId);
    }

    private void SetStatusTypeViewBagDefaults(string searchTerm, int page, int pageSize)
    {
        ViewBag.CurrentPage = page;
        ViewBag.TotalPages = 0;
        ViewBag.TotalItems = 0;
        ViewBag.PageSize = pageSize;
        ViewBag.SearchTerm = searchTerm;
    }

    private static SelectList BuildStatusTypeSelectList(IEnumerable<StatusTypeLookupDto> rows, int? selectedStatusTypeId)
    {
        var items = rows
            .Select(x => new SelectListItem
            {
                Value = x.statusTypeID.ToString(CultureInfo.InvariantCulture),
                Text = x.statusTypeName ?? string.Empty
            })
            .ToList();

        items.Insert(0, new SelectListItem { Value = "", Text = "Tất cả" });
        return new SelectList(items, "Value", "Text", selectedStatusTypeId?.ToString(CultureInfo.InvariantCulture));
    }

    private async Task<List<StatusTypeLookupDto>> LoadStatusTypeLookupAsync(HttpClient client)
    {
        try
        {
            var response = await client.GetAsync("/api/orders/admin/status-types/lookup");
            if (!response.IsSuccessStatusCode)
            {
                return new List<StatusTypeLookupDto>();
            }

            return await response.Content.ReadFromJsonAsync<List<StatusTypeLookupDto>>(JsonOptions)
                ?? new List<StatusTypeLookupDto>();
        }
        catch
        {
            return new List<StatusTypeLookupDto>();
        }
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

    private sealed class StatusesPageResponse
    {
        public int page { get; set; }

        public int totalPages { get; set; }

        public int totalItems { get; set; }

        public int pageSize { get; set; }

        public string? searchTerm { get; set; }

        public int? statusTypeID { get; set; }

        public List<StatusRowDto>? statuses { get; set; }

        public List<StatusTypeLookupDto>? statusTypes { get; set; }
    }

    private sealed class StatusRowDto
    {
        public int statusID { get; set; }

        public string? statusName { get; set; }

        public int statusTypeID { get; set; }

        public string? colorCode { get; set; }

        public string? note { get; set; }

        public int displayOrder { get; set; }

        public bool isActive { get; set; }

        public DateTime createdDate { get; set; }

        public StatusTypeLookupDto? statusType { get; set; }
    }

    private sealed class StatusTypesPageResponse
    {
        public int page { get; set; }

        public int totalPages { get; set; }

        public int totalItems { get; set; }

        public int pageSize { get; set; }

        public string? searchTerm { get; set; }

        public List<StatusTypeRowDto>? statusTypes { get; set; }
    }

    private sealed class StatusTypeRowDto
    {
        public int statusTypeID { get; set; }

        public string? statusTypeName { get; set; }

        public string? description { get; set; }

        public bool isActive { get; set; }

        public DateTime createdDate { get; set; }

        public int activeStatusCount { get; set; }
    }

    private sealed class StatusTypeLookupDto
    {
        public int statusTypeID { get; set; }

        public string? statusTypeName { get; set; }
    }

    private sealed class StatusDetailResponse
    {
        public int statusID { get; set; }

        public string? statusName { get; set; }

        public int statusTypeID { get; set; }

        public string? colorCode { get; set; }

        public string? note { get; set; }

        public int displayOrder { get; set; }
    }

    private sealed class StatusTypeDetailResponse
    {
        public int statusTypeID { get; set; }

        public string? statusTypeName { get; set; }

        public string? description { get; set; }
    }

    private sealed class BasicSuccessResponse
    {
        public string? message { get; set; }
    }
}
