using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using FreshFarm.Web.Bff.Areas.Seller.Infrastructure;
using FreshFarm.Web.Bff.Areas.Seller.Models;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;

namespace FreshFarm.Web.Bff.Areas.Seller.Controllers;

[Authorize(Roles = "Seller")]
[ResponseCache(Duration = 0, Location = ResponseCacheLocation.None, NoStore = true)]
[Area("Seller")]
public class FeedbackController : LegacySellerControllerBase
{
    private const string AccessTokenSessionKey = "ACCESS_TOKEN";

    private readonly IHttpClientFactory _httpClientFactory;

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true
    };

    public FeedbackController(IHttpClientFactory httpClientFactory)
    {
        _httpClientFactory = httpClientFactory;
    }

    [HttpGet]
    public async Task<IActionResult> Index(int page = 1, int pageSize = 10, string? q = null, string? status = null)
    {
        try
        {
            if (page < 1)
            {
                page = 1;
            }

            if (pageSize < 1)
            {
                pageSize = 10;
            }

            if (pageSize > 100)
            {
                pageSize = 100;
            }

            var queryParts = new List<string> { "take=5000" };
            if (!string.IsNullOrWhiteSpace(q))
            {
                queryParts.Add("search=" + Uri.EscapeDataString(q.Trim()));
            }

            var client = CreateAuthorizedClient("Ordering");
            var response = await client.GetAsync("/api/orders/admin/feedbacks?" + string.Join("&", queryParts));
            if (!response.IsSuccessStatusCode)
            {
                TempData["ErrorMessage"] = await ReadApiErrorAsync(response, "Khong the tai danh sach phan hoi.");
                return View(new List<SellerFeedbackViewModel>());
            }

            var payload = await response.Content.ReadFromJsonAsync<List<FeedbackApiDto>>(JsonOptions)
                ?? new List<FeedbackApiDto>();

            var model = payload.Select(MapFeedback).ToList();

            if (!string.IsNullOrWhiteSpace(status))
            {
                var normalizedStatus = NormalizeStatus(status);
                model = model
                    .Where(x => string.Equals(x.Status, normalizedStatus, StringComparison.OrdinalIgnoreCase))
                    .ToList();
            }

            var totalRecords = model.Count;
            var totalPages = Math.Max(1, (int)Math.Ceiling(totalRecords / (double)pageSize));
            if (page > totalPages)
            {
                page = totalPages;
            }

            var pageItems = model
                .Skip((page - 1) * pageSize)
                .Take(pageSize)
                .ToList();

            ViewBag.CurrentPage = page;
            ViewBag.PageSize = pageSize;
            ViewBag.TotalRecords = totalRecords;
            ViewBag.TotalPages = totalPages;
            ViewBag.Query = q ?? string.Empty;
            ViewBag.Status = status ?? string.Empty;

            return View(pageItems);
        }
        catch (Exception ex)
        {
            TempData["ErrorMessage"] = "Loi tai danh sach phan hoi: " + ex.Message;
            return View(new List<SellerFeedbackViewModel>());
        }
    }

    [HttpPost]
    public async Task<JsonResult> UpdateStatus(int id, string status = "processed")
    {
        try
        {
            if (id <= 0)
            {
                return Json(new { success = false, message = "ID phan hoi khong hop le." });
            }

            var client = CreateAuthorizedClient("Ordering");
            var response = await client.PostAsJsonAsync($"/api/orders/admin/feedbacks/{id}/status", new
            {
                status = NormalizeStatus(status)
            });

            if (!response.IsSuccessStatusCode)
            {
                return Json(new { success = false, message = await ReadApiErrorAsync(response, "Khong the cap nhat trang thai phan hoi.") });
            }

            var payload = await response.Content.ReadFromJsonAsync<BasicSuccessResponse>(JsonOptions);
            return Json(new { success = true, message = payload?.message ?? "Da cap nhat trang thai phan hoi." });
        }
        catch (Exception ex)
        {
            return Json(new { success = false, message = "Loi: " + ex.Message });
        }
    }

    [HttpGet]
    public async Task<JsonResult> GetFeedback(int id)
    {
        try
        {
            if (id <= 0)
            {
                return Json(new { success = false, message = "ID phan hoi khong hop le." });
            }

            var client = CreateAuthorizedClient("Ordering");
            var response = await client.GetAsync($"/api/orders/admin/feedbacks/{id}");
            if (!response.IsSuccessStatusCode)
            {
                return Json(new
                {
                    success = false,
                    message = await ReadApiErrorAsync(response, "Khong the tai chi tiet phan hoi.")
                });
            }

            var payload = await response.Content.ReadFromJsonAsync<FeedbackApiDto>(JsonOptions);
            if (payload is null)
            {
                return Json(new { success = false, message = "Du lieu phan hoi khong hop le." });
            }

            return Json(new
            {
                success = true,
                Id = payload.id,
                SenderName = payload.senderName,
                SenderEmail = payload.senderEmail,
                SenderPhone = payload.senderPhone,
                Subject = payload.subject,
                Message = payload.message,
                CreatedAt = payload.createdAt,
                CreatedAtFormatted = payload.createdAt.ToString("dd/MM/yyyy HH:mm"),
                Status = payload.status,
                AdminNote = payload.adminNote
            });
        }
        catch (Exception ex)
        {
            return Json(new { success = false, message = "Loi: " + ex.Message });
        }
    }

    [HttpPost]
    public async Task<JsonResult> Delete(int id)
    {
        try
        {
            if (id <= 0)
            {
                return Json(new { success = false, message = "ID phan hoi khong hop le." });
            }

            var client = CreateAuthorizedClient("Ordering");
            var response = await client.DeleteAsync($"/api/orders/admin/feedbacks/{id}");
            if (!response.IsSuccessStatusCode)
            {
                return Json(new { success = false, message = await ReadApiErrorAsync(response, "Khong the xoa phan hoi.") });
            }

            var payload = await response.Content.ReadFromJsonAsync<BasicSuccessResponse>(JsonOptions);
            return Json(new { success = true, message = payload?.message ?? "Da xoa phan hoi thanh cong." });
        }
        catch (Exception ex)
        {
            return Json(new { success = false, message = "Loi: " + ex.Message });
        }
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

    private static SellerFeedbackViewModel MapFeedback(FeedbackApiDto dto)
    {
        return new SellerFeedbackViewModel
        {
            Id = dto.id,
            SenderName = dto.senderName ?? string.Empty,
            SenderEmail = dto.senderEmail ?? string.Empty,
            SenderPhone = dto.senderPhone,
            Subject = dto.subject ?? string.Empty,
            Message = dto.message ?? string.Empty,
            CreatedAt = dto.createdAt,
            Status = dto.status ?? "new",
            AdminNote = dto.adminNote
        };
    }

    private static string NormalizeStatus(string? rawStatus)
    {
        var status = rawStatus?.Trim().ToLowerInvariant();
        return status switch
        {
            "processed" => "processed",
            "resolved" => "processed",
            "done" => "processed",
            _ => "new"
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

    private sealed class FeedbackApiDto
    {
        public int id { get; set; }

        public string? senderName { get; set; }

        public string? senderEmail { get; set; }

        public string? senderPhone { get; set; }

        public string? subject { get; set; }

        public string? message { get; set; }

        public DateTime createdAt { get; set; }

        public string? status { get; set; }

        public string? adminNote { get; set; }
    }

    private sealed class BasicSuccessResponse
    {
        public bool success { get; set; }

        public string? message { get; set; }
    }
}
