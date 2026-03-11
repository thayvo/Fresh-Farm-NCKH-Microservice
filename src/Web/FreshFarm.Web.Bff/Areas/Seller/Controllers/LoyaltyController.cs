using System.Globalization;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text;
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
public class LoyaltyController : LegacySellerControllerBase
{
    private const string AccessTokenSessionKey = "ACCESS_TOKEN";

    private readonly IHttpClientFactory _httpClientFactory;

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true
    };

    public LoyaltyController(IHttpClientFactory httpClientFactory)
    {
        _httpClientFactory = httpClientFactory;
    }

    [HttpGet]
    public async Task<IActionResult> Index()
    {
        var model = new LoyaltyDashboardVM();

        try
        {
            var client = CreateAuthorizedClient("Ordering");
            var response = await client.GetAsync("/api/orders/admin/loyalty/dashboard");
            if (!response.IsSuccessStatusCode)
            {
                TempData["ErrorMessage"] = await ReadApiErrorAsync(response, "Khong the tai dashboard tich diem.");
                return View(model);
            }

            var payload = await response.Content.ReadFromJsonAsync<LoyaltyDashboardApiResponse>(JsonOptions);
            if (payload is null)
            {
                TempData["ErrorMessage"] = "Khong doc duoc du lieu dashboard tich diem.";
                return View(model);
            }

            model.TotalEarned = payload.totalEarned;
            model.TotalRedeemed = payload.totalRedeemed;
            model.UsersWithPoints = payload.usersWithPoints;
            model.CurrentYear = payload.currentYear;
            model.CurrentQuarter = payload.currentQuarter;
            model.TopQuarterUsers = (payload.topQuarterUsers ?? new List<LoyaltyTopUserApiDto>())
                .Select(x => new LoyaltyTopUserVM
                {
                    UserID = x.userID,
                    FullName = x.fullName ?? string.Empty,
                    TotalPoints = x.totalPoints,
                    QuarterPoints = x.quarterPoints,
                    RankName = x.rankName ?? string.Empty
                })
                .ToList();
            model.RecentActivities = (payload.recentActivities ?? new List<LoyaltyHistoryRowApiDto>())
                .Select(x => new LoyaltyHistoryRowVM
                {
                    CreatedAt = x.createdAt,
                    UserID = x.userID,
                    UserName = x.userName ?? string.Empty,
                    OrderID = x.orderID,
                    Points = x.points,
                    Direction = x.direction ?? string.Empty,
                    Reason = x.reason ?? string.Empty
                })
                .ToList();
        }
        catch (Exception ex)
        {
            TempData["ErrorMessage"] = "Loi tai dashboard tich diem: " + ex.Message;
        }

        return View(model);
    }

    [HttpGet]
    public async Task<IActionResult> Users(string? q = null, int page = 1, int pageSize = 10)
    {
        if (page < 1)
        {
            page = 1;
        }

        if (pageSize <= 0 || pageSize > 100)
        {
            pageSize = 10;
        }

        var model = new LoyaltyUsersVM
        {
            Query = q ?? string.Empty,
            Page = page,
            PageSize = pageSize
        };

        try
        {
            var client = CreateAuthorizedClient("Ordering");
            var endpoint = BuildUsersEndpoint(q, page, pageSize);
            var response = await client.GetAsync(endpoint);
            if (!response.IsSuccessStatusCode)
            {
                TempData["ErrorMessage"] = await ReadApiErrorAsync(response, "Khong the tai danh sach thanh vien tich diem.");
                return View(model);
            }

            var payload = await response.Content.ReadFromJsonAsync<LoyaltyUsersApiResponse>(JsonOptions);
            if (payload is null)
            {
                TempData["ErrorMessage"] = "Khong doc duoc du lieu thanh vien tich diem.";
                return View(model);
            }

            model.Query = payload.query ?? string.Empty;
            model.Page = payload.page;
            model.PageSize = payload.pageSize;
            model.Total = payload.total;
            model.Rows = (payload.rows ?? new List<LoyaltyUserRowApiDto>())
                .Select(x => new LoyaltyUserRowVM
                {
                    UserID = x.userID,
                    FullName = x.fullName ?? string.Empty,
                    TotalPoints = x.totalPoints,
                    CurrentQuarterPoints = x.currentQuarterPoints,
                    RankName = x.rankName ?? string.Empty
                })
                .ToList();
        }
        catch (Exception ex)
        {
            TempData["ErrorMessage"] = "Loi tai danh sach thanh vien: " + ex.Message;
        }

        return View(model);
    }

    [HttpGet]
    public async Task<IActionResult> History(
        int? userId = null,
        string? direction = null,
        DateTime? start = null,
        DateTime? end = null,
        int page = 1,
        int pageSize = 20)
    {
        if (page < 1)
        {
            page = 1;
        }

        if (pageSize <= 0 || pageSize > 200)
        {
            pageSize = 20;
        }

        var model = new LoyaltyHistoryVM
        {
            UserID = userId,
            Direction = direction ?? string.Empty,
            Start = start,
            End = end,
            Page = page,
            PageSize = pageSize
        };

        try
        {
            var client = CreateAuthorizedClient("Ordering");
            var endpoint = BuildHistoryEndpoint(userId, direction, start, end, page, pageSize);
            var response = await client.GetAsync(endpoint);
            if (!response.IsSuccessStatusCode)
            {
                TempData["ErrorMessage"] = await ReadApiErrorAsync(response, "Khong the tai lich su tich diem.");
                return View(model);
            }

            var payload = await response.Content.ReadFromJsonAsync<LoyaltyHistoryApiResponse>(JsonOptions);
            if (payload is null)
            {
                TempData["ErrorMessage"] = "Khong doc duoc du lieu lich su tich diem.";
                return View(model);
            }

            model.UserID = payload.userID;
            model.Direction = payload.direction ?? string.Empty;
            model.Start = payload.start;
            model.End = payload.end;
            model.Page = payload.page;
            model.PageSize = payload.pageSize;
            model.Total = payload.total;
            model.Rows = (payload.rows ?? new List<LoyaltyHistoryRowApiDto>())
                .Select(x => new LoyaltyHistoryRowVM
                {
                    CreatedAt = x.createdAt,
                    UserID = x.userID,
                    UserName = x.userName ?? string.Empty,
                    OrderID = x.orderID,
                    Points = x.points,
                    Direction = x.direction ?? string.Empty,
                    Reason = x.reason ?? string.Empty
                })
                .ToList();
        }
        catch (Exception ex)
        {
            TempData["ErrorMessage"] = "Loi tai lich su tich diem: " + ex.Message;
        }

        return View(model);
    }

    [HttpGet]
    public async Task<IActionResult> Config()
    {
        var model = new LoyaltyConfigVM
        {
            EarnRate = 0.01m,
            IncludeShippingFee = false,
            PaidKeywords = "Da thanh toan;Paid;Completed"
        };

        try
        {
            var client = CreateAuthorizedClient("Ordering");
            var response = await client.GetAsync("/api/orders/admin/loyalty/config");
            if (!response.IsSuccessStatusCode)
            {
                TempData["ErrorMessage"] = await ReadApiErrorAsync(response, "Khong the tai cau hinh tich diem.");
                return View(model);
            }

            var payload = await response.Content.ReadFromJsonAsync<LoyaltyConfigApiResponse>(JsonOptions);
            if (payload is null)
            {
                TempData["ErrorMessage"] = "Khong doc duoc cau hinh tich diem.";
                return View(model);
            }

            model.EarnRate = payload.earnRate;
            model.IncludeShippingFee = payload.includeShippingFee;
            model.PaidKeywords = payload.paidKeywords ?? string.Empty;
            model.UpdatedAt = payload.updatedAt;
        }
        catch (Exception ex)
        {
            TempData["ErrorMessage"] = "Loi tai cau hinh tich diem: " + ex.Message;
        }

        return View(model);
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Config(LoyaltyConfigVM input)
    {
        if (input.EarnRate < 0)
        {
            ModelState.AddModelError(nameof(input.EarnRate), "EarnRate khong hop le.");
        }

        if (!ModelState.IsValid)
        {
            return View(input);
        }

        try
        {
            var client = CreateAuthorizedClient("Ordering");
            var response = await client.PutAsJsonAsync("/api/orders/admin/loyalty/config", new
            {
                earnRate = input.EarnRate,
                includeShippingFee = input.IncludeShippingFee,
                paidKeywords = input.PaidKeywords ?? string.Empty
            });

            if (!response.IsSuccessStatusCode)
            {
                TempData["ErrorMessage"] = await ReadApiErrorAsync(response, "Khong the luu cau hinh tich diem.");
                return View(input);
            }

            var payload = await response.Content.ReadFromJsonAsync<BasicSuccessResponse>(JsonOptions);
            TempData["SuccessMessage"] = payload?.message ?? "Da luu cau hinh tich diem.";
            return RedirectToAction(nameof(Config));
        }
        catch (Exception ex)
        {
            TempData["ErrorMessage"] = "Loi luu cau hinh tich diem: " + ex.Message;
            return View(input);
        }
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<JsonResult> AdjustPoints(int userId, int points, string? reason, bool includeInRank = false)
    {
        if (userId <= 0 || points == 0)
        {
            return Json(new { success = false, message = "Du lieu dieu chinh diem khong hop le." });
        }

        try
        {
            var client = CreateAuthorizedClient("Ordering");
            var response = await client.PostAsJsonAsync("/api/orders/admin/loyalty/adjust", new
            {
                userID = userId,
                points,
                reason,
                includeInRank
            });

            if (!response.IsSuccessStatusCode)
            {
                return Json(new { success = false, message = await ReadApiErrorAsync(response, "Khong the dieu chinh diem.") });
            }

            var payload = await response.Content.ReadFromJsonAsync<BasicSuccessResponse>(JsonOptions);
            return Json(new { success = true, message = payload?.message ?? "Da dieu chinh diem thanh cong." });
        }
        catch (Exception ex)
        {
            return Json(new { success = false, message = "Loi: " + ex.Message });
        }
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<JsonResult> SyncAward(DateTime? start, DateTime? end)
    {
        try
        {
            var client = CreateAuthorizedClient("Ordering");
            var response = await client.PostAsJsonAsync("/api/orders/admin/loyalty/sync-award", new { start, end });
            if (!response.IsSuccessStatusCode)
            {
                return Json(new { success = false, message = await ReadApiErrorAsync(response, "Khong the dong bo diem.") });
            }

            var payload = await response.Content.ReadFromJsonAsync<SyncAwardResponse>(JsonOptions);
            return Json(new
            {
                success = payload?.success ?? true,
                ok = payload?.ok ?? 0,
                fail = payload?.fail ?? 0,
                message = payload?.message ?? string.Empty
            });
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

    private static string BuildUsersEndpoint(string? query, int page, int pageSize)
    {
        var endpoint = new StringBuilder($"/api/orders/admin/loyalty/users?page={page}&pageSize={pageSize}");
        if (!string.IsNullOrWhiteSpace(query))
        {
            endpoint.Append("&q=");
            endpoint.Append(Uri.EscapeDataString(query.Trim()));
        }

        return endpoint.ToString();
    }

    private static string BuildHistoryEndpoint(
        int? userId,
        string? direction,
        DateTime? start,
        DateTime? end,
        int page,
        int pageSize)
    {
        var endpoint = new StringBuilder($"/api/orders/admin/loyalty/history?page={page}&pageSize={pageSize}");

        if (userId.HasValue && userId.Value > 0)
        {
            endpoint.Append("&userId=");
            endpoint.Append(userId.Value);
        }

        if (!string.IsNullOrWhiteSpace(direction))
        {
            endpoint.Append("&direction=");
            endpoint.Append(Uri.EscapeDataString(direction.Trim()));
        }

        if (start.HasValue)
        {
            endpoint.Append("&start=");
            endpoint.Append(Uri.EscapeDataString(start.Value.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture)));
        }

        if (end.HasValue)
        {
            endpoint.Append("&end=");
            endpoint.Append(Uri.EscapeDataString(end.Value.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture)));
        }

        return endpoint.ToString();
    }

    private sealed class LoyaltyDashboardApiResponse
    {
        public int totalEarned { get; set; }

        public int totalRedeemed { get; set; }

        public int usersWithPoints { get; set; }

        public int currentYear { get; set; }

        public byte currentQuarter { get; set; }

        public List<LoyaltyTopUserApiDto>? topQuarterUsers { get; set; }

        public List<LoyaltyHistoryRowApiDto>? recentActivities { get; set; }
    }

    private sealed class LoyaltyTopUserApiDto
    {
        public int userID { get; set; }

        public string? fullName { get; set; }

        public int totalPoints { get; set; }

        public int quarterPoints { get; set; }

        public string? rankName { get; set; }
    }

    private sealed class LoyaltyUsersApiResponse
    {
        public string? query { get; set; }

        public int page { get; set; }

        public int pageSize { get; set; }

        public int total { get; set; }

        public List<LoyaltyUserRowApiDto>? rows { get; set; }
    }

    private sealed class LoyaltyUserRowApiDto
    {
        public int userID { get; set; }

        public string? fullName { get; set; }

        public int totalPoints { get; set; }

        public int currentQuarterPoints { get; set; }

        public string? rankName { get; set; }
    }

    private sealed class LoyaltyHistoryApiResponse
    {
        public int? userID { get; set; }

        public string? direction { get; set; }

        public DateTime? start { get; set; }

        public DateTime? end { get; set; }

        public int page { get; set; }

        public int pageSize { get; set; }

        public int total { get; set; }

        public List<LoyaltyHistoryRowApiDto>? rows { get; set; }
    }

    private sealed class LoyaltyHistoryRowApiDto
    {
        public DateTime createdAt { get; set; }

        public int userID { get; set; }

        public string? userName { get; set; }

        public int? orderID { get; set; }

        public int points { get; set; }

        public string? direction { get; set; }

        public string? reason { get; set; }
    }

    private sealed class LoyaltyConfigApiResponse
    {
        public decimal earnRate { get; set; }

        public bool includeShippingFee { get; set; }

        public string? paidKeywords { get; set; }

        public DateTime? updatedAt { get; set; }
    }

    private sealed class BasicSuccessResponse
    {
        public bool success { get; set; }

        public string? message { get; set; }
    }

    private sealed class SyncAwardResponse
    {
        public bool success { get; set; }

        public int ok { get; set; }

        public int fail { get; set; }

        public string? message { get; set; }
    }
}
