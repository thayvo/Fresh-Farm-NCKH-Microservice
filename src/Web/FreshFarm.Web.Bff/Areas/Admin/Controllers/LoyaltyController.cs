using System.Globalization;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using FreshFarm.Web.Bff.Areas.Seller.Infrastructure;
using FreshFarm.Web.Bff.Areas.Seller.Models;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace FreshFarm.Web.Bff.Areas.Admin.Controllers;

[Authorize(Policy = "AdminOnly")]
[ResponseCache(Duration = 0, Location = ResponseCacheLocation.None, NoStore = true)]
[Area("Admin")]
public sealed class LoyaltyController : LegacySellerControllerBase
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
                TempData["ErrorMessage"] = await ReadApiErrorAsync(response, "Không thể tải dashboard tích điểm.");
                return RenderLoyaltyView("Index", model);
            }

            var payload = await response.Content.ReadFromJsonAsync<LoyaltyDashboardApiResponse>(JsonOptions);
            if (payload is null)
            {
                TempData["ErrorMessage"] = "Không đọc được dữ liệu dashboard tích điểm.";
                return RenderLoyaltyView("Index", model);
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
            NormalizeDashboard(model);
        }
        catch (Exception ex)
        {
            TempData["ErrorMessage"] = "Lỗi tải dashboard tích điểm: " + ex.Message;
        }

        return RenderLoyaltyView("Index", model);
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
                TempData["ErrorMessage"] = await ReadApiErrorAsync(response, "Không thể tải danh sách thành viên tích điểm.");
                return RenderLoyaltyView("Users", model);
            }

            var payload = await response.Content.ReadFromJsonAsync<LoyaltyUsersApiResponse>(JsonOptions);
            if (payload is null)
            {
                TempData["ErrorMessage"] = "Không đọc được dữ liệu thành viên tích điểm.";
                return RenderLoyaltyView("Users", model);
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
            NormalizeUsers(model);
        }
        catch (Exception ex)
        {
            TempData["ErrorMessage"] = "Lỗi tải danh sách thành viên: " + ex.Message;
        }

        return RenderLoyaltyView("Users", model);
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
                TempData["ErrorMessage"] = await ReadApiErrorAsync(response, "Không thể tải lịch sử tích điểm.");
                return RenderLoyaltyView("History", model);
            }

            var payload = await response.Content.ReadFromJsonAsync<LoyaltyHistoryApiResponse>(JsonOptions);
            if (payload is null)
            {
                TempData["ErrorMessage"] = "Không đọc được dữ liệu lịch sử tích điểm.";
                return RenderLoyaltyView("History", model);
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
            NormalizeHistory(model);
        }
        catch (Exception ex)
        {
            TempData["ErrorMessage"] = "Lỗi tải lịch sử tích điểm: " + ex.Message;
        }

        return RenderLoyaltyView("History", model);
    }

    [HttpGet]
    public async Task<IActionResult> Config()
    {
        var model = new LoyaltyConfigVM
        {
            EarnRate = 0.01m,
            IncludeShippingFee = false,
            PaidKeywords = "Đã thanh toán;Paid;Completed"
        };

        try
        {
            var client = CreateAuthorizedClient("Ordering");
            var response = await client.GetAsync("/api/orders/admin/loyalty/config");
            if (!response.IsSuccessStatusCode)
            {
                TempData["ErrorMessage"] = await ReadApiErrorAsync(response, "Không thể tải cấu hình tích điểm.");
                return RenderLoyaltyView("Config", model);
            }

            var payload = await response.Content.ReadFromJsonAsync<LoyaltyConfigApiResponse>(JsonOptions);
            if (payload is null)
            {
                TempData["ErrorMessage"] = "Không đọc được cấu hình tích điểm.";
                return RenderLoyaltyView("Config", model);
            }

            model.EarnRate = payload.earnRate;
            model.IncludeShippingFee = payload.includeShippingFee;
            model.PaidKeywords = payload.paidKeywords ?? string.Empty;
            model.UpdatedAt = payload.updatedAt;
        }
        catch (Exception ex)
        {
            TempData["ErrorMessage"] = "Lỗi tải cấu hình tích điểm: " + ex.Message;
        }

        return RenderLoyaltyView("Config", model);
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Config(LoyaltyConfigVM input)
    {
        if (input.EarnRate < 0)
        {
            ModelState.AddModelError(nameof(input.EarnRate), "Tỷ lệ tích điểm không hợp lệ.");
        }

        if (!ModelState.IsValid)
        {
            return RenderLoyaltyView("Config", input);
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
                TempData["ErrorMessage"] = await ReadApiErrorAsync(response, "Không thể lưu cấu hình tích điểm.");
                return RenderLoyaltyView("Config", input);
            }

            var payload = await response.Content.ReadFromJsonAsync<BasicSuccessResponse>(JsonOptions);
            TempData["SuccessMessage"] = payload?.message ?? "Đã lưu cấu hình tích điểm.";
            return RedirectToAction(nameof(Config));
        }
        catch (Exception ex)
        {
            TempData["ErrorMessage"] = "Lỗi lưu cấu hình tích điểm: " + ex.Message;
            return RenderLoyaltyView("Config", input);
        }
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<JsonResult> AdjustPoints(int userId, int points, string? reason, bool includeInRank = false)
    {
        if (userId <= 0 || points == 0)
        {
            return Json(new { success = false, message = "Dữ liệu điều chỉnh điểm không hợp lệ." });
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
                return Json(new { success = false, message = await ReadApiErrorAsync(response, "Không thể điều chỉnh điểm.") });
            }

            var payload = await response.Content.ReadFromJsonAsync<BasicSuccessResponse>(JsonOptions);
            return Json(new { success = true, message = payload?.message ?? "Đã điều chỉnh điểm thành công." });
        }
        catch (Exception ex)
        {
            return Json(new { success = false, message = "Lỗi: " + ex.Message });
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
                return Json(new { success = false, message = await ReadApiErrorAsync(response, "Không thể đồng bộ điểm.") });
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
            return Json(new { success = false, message = "Lỗi: " + ex.Message });
        }
    }

    private IActionResult RenderLoyaltyView(string viewName, object model)
    {
        ViewData["AreaName"] = "Admin";
        ViewData["LayoutPath"] = "~/Areas/Admin/Views/Shared/_LayoutAdmin.cshtml";
        ViewData["LoyaltyScopeLabel"] = "Toàn sàn";
        return View($"~/Areas/Seller/Views/Loyalty/{viewName}.cshtml", model);
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

    private static void NormalizeDashboard(LoyaltyDashboardVM model)
    {
        model.TopQuarterUsers = model.TopQuarterUsers
            .Where(item => item.UserID > 0)
            .GroupBy(item => item.UserID)
            .Select(group => group
                .OrderByDescending(CalculateTopUserScore)
                .ThenByDescending(CalculateTopUserSignalLength)
                .First())
            .ToList();

        model.RecentActivities = model.RecentActivities
            .Where(item => item.UserID > 0)
            .GroupBy(BuildHistoryKey)
            .Select(group => group
                .OrderByDescending(CalculateHistoryRowScore)
                .ThenByDescending(CalculateHistoryRowSignalLength)
                .First())
            .OrderByDescending(item => item.CreatedAt)
            .ToList();
    }

    private static void NormalizeUsers(LoyaltyUsersVM model)
    {
        model.Rows = model.Rows
            .Where(item => item.UserID > 0)
            .GroupBy(item => item.UserID)
            .Select(group => group
                .OrderByDescending(CalculateUserRowScore)
                .ThenByDescending(CalculateUserRowSignalLength)
                .First())
            .ToList();
    }

    private static void NormalizeHistory(LoyaltyHistoryVM model)
    {
        model.Rows = model.Rows
            .Where(item => item.UserID > 0)
            .GroupBy(BuildHistoryKey)
            .Select(group => group
                .OrderByDescending(CalculateHistoryRowScore)
                .ThenByDescending(CalculateHistoryRowSignalLength)
                .First())
            .OrderByDescending(item => item.CreatedAt)
            .ToList();
    }

    private static string BuildHistoryKey(LoyaltyHistoryRowVM item)
        => FormattableString.Invariant(
            $"{item.CreatedAt.Ticks}|{item.UserID}|{item.Points}|{NormalizeDirectionKey(item.Direction)}");

    private static string NormalizeDirectionKey(string? direction)
        => direction?.Trim().ToLowerInvariant() ?? string.Empty;

    private static int CalculateTopUserScore(LoyaltyTopUserVM item)
        => (HasMeaningfulValue(item.FullName) ? 2 : 0)
        + (item.TotalPoints > 0 ? 1 : 0)
        + (item.QuarterPoints > 0 ? 1 : 0)
        + (HasMeaningfulValue(item.RankName) ? 1 : 0);

    private static int CalculateTopUserSignalLength(LoyaltyTopUserVM item)
        => (item.FullName?.Length ?? 0) + (item.RankName?.Length ?? 0);

    private static int CalculateUserRowScore(LoyaltyUserRowVM item)
        => (HasMeaningfulValue(item.FullName) ? 2 : 0)
        + (item.TotalPoints > 0 ? 1 : 0)
        + (item.CurrentQuarterPoints > 0 ? 1 : 0)
        + (HasMeaningfulValue(item.RankName) ? 1 : 0);

    private static int CalculateUserRowSignalLength(LoyaltyUserRowVM item)
        => (item.FullName?.Length ?? 0) + (item.RankName?.Length ?? 0);

    private static int CalculateHistoryRowScore(LoyaltyHistoryRowVM item)
        => (HasMeaningfulValue(item.UserName) ? 2 : 0)
        + (item.OrderID.HasValue && item.OrderID.Value > 0 ? 1 : 0)
        + (item.Points != 0 ? 1 : 0)
        + (HasMeaningfulValue(item.Direction) ? 1 : 0)
        + (HasMeaningfulValue(item.Reason) ? 1 : 0);

    private static int CalculateHistoryRowSignalLength(LoyaltyHistoryRowVM item)
        => (item.UserName?.Length ?? 0)
        + (item.Direction?.Length ?? 0)
        + (item.Reason?.Length ?? 0);

    private static bool HasMeaningfulValue(string? value)
        => !string.IsNullOrWhiteSpace(value);

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
