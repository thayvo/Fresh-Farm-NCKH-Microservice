using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using FreshFarm.Web.Bff.Areas.Admin.Models;
using FreshFarm.Web.Bff.Areas.Seller.Infrastructure;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace FreshFarm.Web.Bff.Areas.Admin.Controllers;

[Authorize(Policy = "AdminOnly")]
[ResponseCache(Duration = 0, Location = ResponseCacheLocation.None, NoStore = true)]
[Area("Admin")]
public sealed class MerchantController : LegacySellerControllerBase
{
    private const string AccessTokenSessionKey = "ACCESS_TOKEN";

    private readonly IHttpClientFactory _httpClientFactory;

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true
    };

    public MerchantController(IHttpClientFactory httpClientFactory)
    {
        _httpClientFactory = httpClientFactory;
    }

    [HttpGet]
    public async Task<IActionResult> Index(string? q = null, string? status = null, string? queue = null, int page = 1, int? selectedSellerId = null)
    {
        var model = new MerchantManagementPageViewModel
        {
            Query = q?.Trim() ?? string.Empty,
            Status = string.IsNullOrWhiteSpace(status) ? "all" : status.Trim().ToLowerInvariant(),
            Queue = string.IsNullOrWhiteSpace(queue) ? "all" : queue.Trim().ToLowerInvariant(),
            Page = page < 1 ? 1 : page,
            SelectedSellerId = selectedSellerId > 0 ? selectedSellerId : null
        };

        try
        {
            var client = CreateIdentityClient();
            var response = await client.GetAsync(BuildListEndpoint(model));
            if (!response.IsSuccessStatusCode)
            {
                ViewBag.Error = await ReadApiErrorAsync(response, "Không thể tải lane merchant compliance.");
                return View(model);
            }

            var payload = await response.Content.ReadFromJsonAsync<MerchantListApiResponse>(JsonOptions);
            if (payload is null)
            {
                ViewBag.Error = "Không đọc được dữ liệu merchant compliance.";
                return View(model);
            }

            model.Page = payload.Page <= 0 ? model.Page : payload.Page;
            model.PageSize = payload.PageSize <= 0 ? model.PageSize : payload.PageSize;
            model.Total = payload.Total;
            model.TotalPages = payload.TotalPages <= 0 ? 1 : payload.TotalPages;
            model.Stats = new MerchantStatsViewModel
            {
                TotalSellers = payload.Stats?.TotalSellers ?? 0,
                ActiveSellers = payload.Stats?.ActiveSellers ?? 0,
                SuspendedSellers = payload.Stats?.SuspendedSellers ?? 0,
                ReviewNeeded = payload.Stats?.ReviewNeeded ?? 0,
                MissingAddress = payload.Stats?.MissingAddress ?? 0,
                ApprovalQueue = payload.Stats?.ApprovalQueue ?? 0,
                ProfileFixQueue = payload.Stats?.ProfileFixQueue ?? 0,
                DormantQueue = payload.Stats?.DormantQueue ?? 0
            };
            model.StatusOptions = payload.Filters?.StatusOptions?.Select(x => new MerchantOptionViewModel
            {
                Value = x.Value ?? string.Empty,
                Text = x.Text ?? string.Empty
            }).ToList() ?? new List<MerchantOptionViewModel>();
            model.Queue = payload.Filters?.Queue ?? model.Queue;
            model.QueueOptions = payload.Filters?.QueueOptions?.Select(x => new MerchantOptionViewModel
            {
                Value = x.Value ?? string.Empty,
                Text = x.Text ?? string.Empty
            }).ToList() ?? new List<MerchantOptionViewModel>();
            model.Merchants = payload.Merchants?.Select(MapListItem).ToList() ?? new List<MerchantListItemViewModel>();

            if (!model.SelectedSellerId.HasValue && model.Merchants.Count > 0)
            {
                model.SelectedSellerId = model.Merchants[0].SellerId;
            }

            if (model.SelectedSellerId.HasValue)
            {
                await LoadDetailsAsync(client, model);
            }
        }
        catch (Exception ex)
        {
            ViewBag.Error = "Lỗi khi tải merchant compliance: " + ex.Message;
        }

        return View(model);
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> UpdateStatus(int sellerId, bool isActive, string? q, string? status, string? queue, int page = 1, int? selectedSellerId = null)
    {
        if (sellerId <= 0)
        {
            TempData["ErrorMessage"] = "Seller không hợp lệ.";
            return RedirectToAction(nameof(Index), new { q, status, queue, page, selectedSellerId });
        }

        try
        {
            var client = CreateIdentityClient();
            var response = await client.PatchAsJsonAsync($"/auth/admin/merchants/{sellerId}/status", new { isActive });
            TempData[response.IsSuccessStatusCode ? "SuccessMessage" : "ErrorMessage"] =
                response.IsSuccessStatusCode
                    ? await ReadApiSuccessAsync(response, isActive ? "Đã mở lại seller." : "Đã tạm khóa seller.")
                    : await ReadApiErrorAsync(response, "Không thể cập nhật seller.");
        }
        catch (Exception ex)
        {
            TempData["ErrorMessage"] = "Lỗi khi cập nhật seller: " + ex.Message;
        }

        return RedirectToAction(nameof(Index), new
        {
            q,
            status,
            queue,
            page,
            selectedSellerId = selectedSellerId > 0 ? selectedSellerId : sellerId
        });
    }

    private async Task LoadDetailsAsync(HttpClient client, MerchantManagementPageViewModel model)
    {
        var response = await client.GetAsync($"/auth/admin/merchants/{model.SelectedSellerId}");
        if (!response.IsSuccessStatusCode)
        {
            ViewBag.DetailError = await ReadApiErrorAsync(response, "Không thể tải chi tiết seller.");
            return;
        }

        var payload = await response.Content.ReadFromJsonAsync<MerchantDetailApiDto>(JsonOptions);
        if (payload is null)
        {
            ViewBag.DetailError = "Không đọc được chi tiết seller.";
            return;
        }

        model.Details = new MerchantDetailViewModel
        {
            SellerId = payload.SellerId,
            ShopName = payload.ShopName ?? string.Empty,
            UserName = payload.UserName ?? string.Empty,
            FullName = payload.FullName ?? string.Empty,
            Email = payload.Email ?? string.Empty,
            Phone = payload.Phone,
            Avatar = payload.Avatar,
            IsActive = payload.IsActive,
            CreatedAt = payload.CreatedAt,
            UpdatedAt = payload.UpdatedAt,
            LastLogin = payload.LastLogin,
            AddressSummary = payload.AddressSummary ?? string.Empty,
            AddressDetail = payload.AddressDetail,
            Province = payload.Province,
            District = payload.District,
            Ward = payload.Ward,
            ProfileScore = payload.ProfileScore,
            ComplianceStatus = payload.ComplianceStatus ?? string.Empty,
            Flags = payload.Flags?.Select(MapFlag).ToList() ?? new List<MerchantFlagViewModel>(),
            DaysSinceLastLogin = payload.DaysSinceLastLogin,
            QueueBucket = payload.QueueBucket ?? string.Empty,
            RecommendedAction = payload.RecommendedAction ?? string.Empty,
            IssueCount = payload.IssueCount,
            ComplianceSummary = payload.ComplianceSummary ?? string.Empty,
            NextSteps = payload.NextSteps?.Where(x => !string.IsNullOrWhiteSpace(x)).Select(x => x!.Trim()).ToList() ?? new List<string>()
        };
    }

    private HttpClient CreateIdentityClient()
    {
        var client = _httpClientFactory.CreateClient("Identity");
        client.DefaultRequestHeaders.Remove("Authorization");

        var token = GetAccessToken(AccessTokenSessionKey);
        if (!string.IsNullOrWhiteSpace(token))
        {
            client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);
        }

        return client;
    }

    private static MerchantListItemViewModel MapListItem(MerchantListItemApiDto item)
    {
        return new MerchantListItemViewModel
        {
            SellerId = item.SellerId,
            ShopName = item.ShopName ?? string.Empty,
            UserName = item.UserName ?? string.Empty,
            FullName = item.FullName ?? string.Empty,
            Email = item.Email ?? string.Empty,
            Phone = item.Phone,
            Avatar = item.Avatar,
            IsActive = item.IsActive,
            CreatedAt = item.CreatedAt,
            UpdatedAt = item.UpdatedAt,
            LastLogin = item.LastLogin,
            AddressSummary = item.AddressSummary ?? string.Empty,
            ProfileScore = item.ProfileScore,
            ComplianceStatus = item.ComplianceStatus ?? string.Empty,
            Flags = item.Flags?.Select(MapFlag).ToList() ?? new List<MerchantFlagViewModel>(),
            DaysSinceLastLogin = item.DaysSinceLastLogin,
            QueueBucket = item.QueueBucket ?? string.Empty,
            RecommendedAction = item.RecommendedAction ?? string.Empty,
            IssueCount = item.IssueCount
        };
    }

    private static MerchantFlagViewModel MapFlag(MerchantFlagApiDto item)
    {
        return new MerchantFlagViewModel
        {
            Code = item.Code ?? string.Empty,
            Label = item.Label ?? string.Empty,
            Tone = item.Tone ?? string.Empty
        };
    }

    private static string BuildListEndpoint(MerchantManagementPageViewModel model)
    {
        var query = new List<string>
        {
            $"page={model.Page}",
            $"pageSize={model.PageSize}"
        };

        if (!string.IsNullOrWhiteSpace(model.Query))
        {
            query.Add($"search={Uri.EscapeDataString(model.Query)}");
        }

        if (!string.IsNullOrWhiteSpace(model.Status))
        {
            query.Add($"status={Uri.EscapeDataString(model.Status)}");
        }

        if (!string.IsNullOrWhiteSpace(model.Queue))
        {
            query.Add($"queue={Uri.EscapeDataString(model.Queue)}");
        }

        return "/auth/admin/merchants?" + string.Join("&", query);
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
            if (doc.RootElement.ValueKind == JsonValueKind.Object)
            {
                if (doc.RootElement.TryGetProperty("message", out var messageElement) &&
                    messageElement.ValueKind == JsonValueKind.String &&
                    !string.IsNullOrWhiteSpace(messageElement.GetString()))
                {
                    return messageElement.GetString()!;
                }

                if (doc.RootElement.TryGetProperty("title", out var titleElement) &&
                    titleElement.ValueKind == JsonValueKind.String &&
                    !string.IsNullOrWhiteSpace(titleElement.GetString()))
                {
                    return titleElement.GetString()!;
                }
            }
        }
        catch
        {
        }

        return body;
    }

    private static async Task<string> ReadApiSuccessAsync(HttpResponseMessage response, string fallback)
    {
        var body = await response.Content.ReadAsStringAsync();
        if (string.IsNullOrWhiteSpace(body))
        {
            return fallback;
        }

        try
        {
            using var doc = JsonDocument.Parse(body);
            if (doc.RootElement.ValueKind == JsonValueKind.Object &&
                doc.RootElement.TryGetProperty("message", out var messageElement) &&
                messageElement.ValueKind == JsonValueKind.String &&
                !string.IsNullOrWhiteSpace(messageElement.GetString()))
            {
                return messageElement.GetString()!;
            }
        }
        catch
        {
        }

        return fallback;
    }

    private sealed class MerchantListApiResponse
    {
        public int Page { get; set; }
        public int PageSize { get; set; }
        public int Total { get; set; }
        public int TotalPages { get; set; }
        public MerchantStatsApiDto? Stats { get; set; }
        public MerchantFiltersApiDto? Filters { get; set; }
        public List<MerchantListItemApiDto>? Merchants { get; set; }
    }

    private sealed class MerchantStatsApiDto
    {
        public int TotalSellers { get; set; }
        public int ActiveSellers { get; set; }
        public int SuspendedSellers { get; set; }
        public int ReviewNeeded { get; set; }
        public int MissingAddress { get; set; }
        public int ApprovalQueue { get; set; }
        public int ProfileFixQueue { get; set; }
        public int DormantQueue { get; set; }
    }

    private sealed class MerchantFiltersApiDto
    {
        public string? Search { get; set; }
        public string? Status { get; set; }
        public string? Queue { get; set; }
        public List<MerchantOptionApiDto>? StatusOptions { get; set; }
        public List<MerchantOptionApiDto>? QueueOptions { get; set; }
    }

    private sealed class MerchantOptionApiDto
    {
        public string? Value { get; set; }
        public string? Text { get; set; }
    }

    private class MerchantListItemApiDto
    {
        public int SellerId { get; set; }
        public string? ShopName { get; set; }
        public string? UserName { get; set; }
        public string? FullName { get; set; }
        public string? Email { get; set; }
        public string? Phone { get; set; }
        public string? Avatar { get; set; }
        public bool IsActive { get; set; }
        public DateTime CreatedAt { get; set; }
        public DateTime? UpdatedAt { get; set; }
        public DateTime? LastLogin { get; set; }
        public string? AddressSummary { get; set; }
        public int ProfileScore { get; set; }
        public string? ComplianceStatus { get; set; }
        public List<MerchantFlagApiDto>? Flags { get; set; }
        public int? DaysSinceLastLogin { get; set; }
        public string? QueueBucket { get; set; }
        public string? RecommendedAction { get; set; }
        public int IssueCount { get; set; }
    }

    private class MerchantDetailApiDto : MerchantListItemApiDto
    {
        public string? AddressDetail { get; set; }
        public string? Province { get; set; }
        public string? District { get; set; }
        public string? Ward { get; set; }
        public string? ComplianceSummary { get; set; }
        public List<string>? NextSteps { get; set; }
    }

    private sealed class MerchantFlagApiDto
    {
        public string? Code { get; set; }
        public string? Label { get; set; }
        public string? Tone { get; set; }
    }
}

