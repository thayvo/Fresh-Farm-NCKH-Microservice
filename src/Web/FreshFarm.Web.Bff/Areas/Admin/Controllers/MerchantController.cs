using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text;
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
    public async Task<IActionResult> Index(
        string? q = null,
        string? status = null,
        string? queue = null,
        string? reviewStatus = null,
        string? reviewWindow = null,
        int page = 1,
        int? selectedSellerId = null)
    {
        var model = new MerchantManagementPageViewModel
        {
            Query = q?.Trim() ?? string.Empty,
            Status = string.IsNullOrWhiteSpace(status) ? "all" : status.Trim().ToLowerInvariant(),
            Queue = string.IsNullOrWhiteSpace(queue) ? "all" : queue.Trim().ToLowerInvariant(),
            ReviewStatus = string.IsNullOrWhiteSpace(reviewStatus) ? "all" : reviewStatus.Trim().ToLowerInvariant(),
            ReviewWindow = string.IsNullOrWhiteSpace(reviewWindow) ? "all" : reviewWindow.Trim().ToLowerInvariant(),
            Page = page < 1 ? 1 : page,
            SelectedSellerId = selectedSellerId > 0 ? selectedSellerId : null
        };

        try
        {
            var client = CreateIdentityClient();
            var response = await client.GetAsync(BuildListEndpoint(model));
            if (!response.IsSuccessStatusCode)
            {
                ViewBag.Error = await ReadApiErrorAsync(response, "Không thể tải lane nhà bán hàng.");
                return View(model);
            }

            var payload = await response.Content.ReadFromJsonAsync<MerchantListApiResponse>(JsonOptions);
            if (payload is null)
            {
                ViewBag.Error = "Không đọc được dữ liệu nhà bán hàng.";
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
            model.ReviewStatus = payload.Filters?.ReviewStatus ?? model.ReviewStatus;
            model.ReviewStatusOptions = payload.Filters?.ReviewStatusOptions?.Select(x => new MerchantOptionViewModel
            {
                Value = x.Value ?? string.Empty,
                Text = x.Text ?? string.Empty
            }).ToList() ?? new List<MerchantOptionViewModel>();
            model.ReviewWindow = payload.Filters?.ReviewWindow ?? model.ReviewWindow;
            model.ReviewWindowOptions = payload.Filters?.ReviewWindowOptions?.Select(x => new MerchantOptionViewModel
            {
                Value = x.Value ?? string.Empty,
                Text = x.Text ?? string.Empty
            }).ToList() ?? new List<MerchantOptionViewModel>();
            model.RejectReasonTemplates = payload.Filters?.RejectReasonTemplates?
                .Where(x => !string.IsNullOrWhiteSpace(x))
                .Select(x => x!.Trim())
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToList() ?? new List<string>();
            model.Merchants = BuildMerchantList(payload.Merchants);

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
            ViewBag.Error = "Lỗi khi tải vòng đời nhà bán hàng: " + ex.Message;
        }

        return View(model);
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> UpdateStatus(
        int sellerId,
        bool isActive,
        string? q,
        string? status,
        string? queue,
        string? reviewStatus,
        string? reviewWindow,
        int page = 1,
        int? selectedSellerId = null)
    {
        if (sellerId <= 0)
        {
            TempData["ErrorMessage"] = "Nhà bán hàng không hợp lệ.";
            return RedirectToAction(nameof(Index), new { q, status, queue, reviewStatus, reviewWindow, page, selectedSellerId });
        }

        try
        {
            var client = CreateIdentityClient();
            var response = await client.PatchAsJsonAsync($"/auth/admin/merchants/{sellerId}/status", new { isActive });
            TempData[response.IsSuccessStatusCode ? "SuccessMessage" : "ErrorMessage"] =
                response.IsSuccessStatusCode
                    ? await ReadApiSuccessAsync(response, isActive ? "Đã mở lại nhà bán hàng." : "Đã tạm khóa nhà bán hàng.")
                    : await ReadApiErrorAsync(response, "Không thể cập nhật nhà bán hàng.");
        }
        catch (Exception ex)
        {
            TempData["ErrorMessage"] = "Lỗi khi cập nhật nhà bán hàng: " + ex.Message;
        }

        return RedirectToAction(nameof(Index), new
        {
            q,
            status,
            queue,
            reviewStatus,
            reviewWindow,
            page,
            selectedSellerId = selectedSellerId > 0 ? selectedSellerId : sellerId
        });
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Approve(
        int sellerId,
        string? q,
        string? status,
        string? queue,
        string? reviewStatus,
        string? reviewWindow,
        int page = 1,
        int? selectedSellerId = null)
    {
        if (sellerId <= 0)
        {
            TempData["ErrorMessage"] = "Nhà bán hàng không hợp lệ.";
            return RedirectToAction(nameof(Index), new { q, status, queue, reviewStatus, reviewWindow, page, selectedSellerId });
        }

        try
        {
            var client = CreateIdentityClient();
            var response = await client.PostAsync($"/auth/admin/merchants/{sellerId}/approve", content: null);
            TempData[response.IsSuccessStatusCode ? "SuccessMessage" : "ErrorMessage"] =
                response.IsSuccessStatusCode
                    ? await ReadApiSuccessAsync(response, "Đã duyệt hồ sơ và cấp quyền người bán.")
                    : await ReadApiErrorAsync(response, "Không thể duyệt hồ sơ người bán.");
        }
        catch (Exception ex)
        {
            TempData["ErrorMessage"] = "Lỗi khi duyệt hồ sơ người bán: " + ex.Message;
        }

        return RedirectToAction(nameof(Index), new
        {
            q,
            status,
            queue,
            reviewStatus,
            reviewWindow,
            page,
            selectedSellerId = selectedSellerId > 0 ? selectedSellerId : sellerId
        });
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Reject(
        int sellerId,
        string reason,
        string? q,
        string? status,
        string? queue,
        string? reviewStatus,
        string? reviewWindow,
        int page = 1,
        int? selectedSellerId = null)
    {
        if (sellerId <= 0)
        {
            TempData["ErrorMessage"] = "Nhà bán hàng không hợp lệ.";
            return RedirectToAction(nameof(Index), new { q, status, queue, reviewStatus, reviewWindow, page, selectedSellerId });
        }

        if (string.IsNullOrWhiteSpace(reason))
        {
            TempData["ErrorMessage"] = "Cần nhập lý do từ chối để nhà bán hàng biết phải bổ sung gì.";
            return RedirectToAction(nameof(Index), new { q, status, queue, reviewStatus, reviewWindow, page, selectedSellerId = selectedSellerId > 0 ? selectedSellerId : sellerId });
        }

        try
        {
            var client = CreateIdentityClient();
            var content = new StringContent(
                JsonSerializer.Serialize(new { reason = reason.Trim() }),
                Encoding.UTF8,
                "application/json");
            var response = await client.PostAsync($"/auth/admin/merchants/{sellerId}/reject", content);
            TempData[response.IsSuccessStatusCode ? "SuccessMessage" : "ErrorMessage"] =
                response.IsSuccessStatusCode
                    ? await ReadApiSuccessAsync(response, "Đã từ chối hồ sơ người bán và yêu cầu bổ sung.")
                    : await ReadApiErrorAsync(response, "Không thể từ chối hồ sơ người bán.");
        }
        catch (Exception ex)
        {
            TempData["ErrorMessage"] = "Lỗi khi từ chối hồ sơ người bán: " + ex.Message;
        }

        return RedirectToAction(nameof(Index), new
        {
            q,
            status,
            queue,
            reviewStatus,
            reviewWindow,
            page,
            selectedSellerId = selectedSellerId > 0 ? selectedSellerId : sellerId
        });
    }

    private async Task LoadDetailsAsync(HttpClient client, MerchantManagementPageViewModel model)
    {
        var response = await client.GetAsync($"/auth/admin/merchants/{model.SelectedSellerId}");
        if (!response.IsSuccessStatusCode)
        {
            ViewBag.DetailError = await ReadApiErrorAsync(response, "Không thể tải chi tiết nhà bán hàng.");
            return;
        }

        var payload = await response.Content.ReadFromJsonAsync<MerchantDetailApiDto>(JsonOptions);
        if (payload is null)
        {
            ViewBag.DetailError = "Không đọc được chi tiết nhà bán hàng.";
            return;
        }

        model.Details = new MerchantDetailViewModel
        {
            SellerId = payload.SellerId,
            IsSellerApproved = payload.IsSellerApproved,
            ReviewStatus = payload.ReviewStatus ?? "not_applied",
            ReviewStatusLabel = payload.ReviewStatusLabel ?? "Chưa có hồ sơ",
            ReviewNote = payload.ReviewNote,
            ReviewedAt = payload.ReviewedAt,
            ShopName = payload.ShopName ?? string.Empty,
            UserName = payload.UserName ?? string.Empty,
            FullName = payload.FullName ?? string.Empty,
            Email = payload.Email ?? string.Empty,
            Phone = payload.Phone,
            StoreName = payload.StoreName,
            StoreAddress = payload.StoreAddress,
            StoreEmail = payload.StoreEmail,
            StorePhone = payload.StorePhone,
            Avatar = payload.Avatar,
            IsActive = payload.IsActive,
            CreatedAt = payload.CreatedAt,
            UpdatedAt = payload.UpdatedAt,
            LastLogin = payload.LastLogin,
            ApplicationSubmittedAt = payload.ApplicationSubmittedAt,
            ApplicationUpdatedAt = payload.ApplicationUpdatedAt,
            LegalFullName = payload.LegalFullName,
            IdentityNumberMasked = payload.IdentityNumberMasked,
            IdentityIssuedDate = payload.IdentityIssuedDate,
            IdentityIssuedPlace = payload.IdentityIssuedPlace,
            TaxCode = payload.TaxCode,
            BusinessLicenseNumber = payload.BusinessLicenseNumber,
            CitizenIdFrontUrl = payload.CitizenIdFrontUrl,
            CitizenIdBackUrl = payload.CitizenIdBackUrl,
            BusinessLicenseUrl = payload.BusinessLicenseUrl,
            AdditionalDocumentUrl = payload.AdditionalDocumentUrl,
            KycNotes = payload.KycNotes,
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
            NextSteps = payload.NextSteps?.Where(x => !string.IsNullOrWhiteSpace(x)).Select(x => x!.Trim()).ToList() ?? new List<string>(),
            RejectReasonDraft = payload.ReviewNote ?? string.Empty,
            ReviewHistory = payload.ReviewHistory?.Select(x => new MerchantReviewHistoryViewModel
            {
                Action = x.Action ?? string.Empty,
                ReviewStatus = x.ReviewStatus ?? string.Empty,
                ReviewStatusLabel = x.ReviewStatusLabel ?? string.Empty,
                Note = x.Note,
                ReviewedAt = x.ReviewedAt,
                ReviewedByUserId = x.ReviewedByUserId,
                ReviewerUserName = x.ReviewerUserName,
                ReviewerFullName = x.ReviewerFullName
            }).ToList() ?? new List<MerchantReviewHistoryViewModel>()
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
            IsSellerApproved = item.IsSellerApproved,
            ReviewStatus = item.ReviewStatus ?? "not_applied",
            ReviewStatusLabel = item.ReviewStatusLabel ?? "Chưa có hồ sơ",
            ReviewNote = item.ReviewNote,
            ReviewedAt = item.ReviewedAt,
            ShopName = item.ShopName ?? string.Empty,
            UserName = item.UserName ?? string.Empty,
            FullName = item.FullName ?? string.Empty,
            Email = item.Email ?? string.Empty,
            Phone = item.Phone,
            StoreName = item.StoreName,
            StoreAddress = item.StoreAddress,
            StoreEmail = item.StoreEmail,
            StorePhone = item.StorePhone,
            Avatar = item.Avatar,
            IsActive = item.IsActive,
            CreatedAt = item.CreatedAt,
            UpdatedAt = item.UpdatedAt,
            LastLogin = item.LastLogin,
            ApplicationSubmittedAt = item.ApplicationSubmittedAt,
            ApplicationUpdatedAt = item.ApplicationUpdatedAt,
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

    private static List<MerchantListItemViewModel> BuildMerchantList(IEnumerable<MerchantListItemApiDto>? merchants)
    {
        if (merchants is null)
        {
            return new List<MerchantListItemViewModel>();
        }

        return merchants
            .Where(item => item.SellerId > 0)
            .GroupBy(item => item.SellerId)
            .Select(group => MapListItem(SelectPreferredMerchant(group)))
            .ToList();
    }

    private static MerchantListItemApiDto SelectPreferredMerchant(IEnumerable<MerchantListItemApiDto> merchants)
    {
        return merchants
            .OrderByDescending(CalculateMerchantScore)
            .ThenByDescending(CalculateMerchantSignalLength)
            .ThenByDescending(item => item.UpdatedAt ?? item.ApplicationUpdatedAt ?? item.CreatedAt)
            .First();
    }

    private static int CalculateMerchantScore(MerchantListItemApiDto merchant)
    {
        var score = 0;

        score += HasMeaningfulValue(merchant.ShopName) ? 3 : 0;
        score += HasMeaningfulValue(merchant.UserName) ? 3 : 0;
        score += HasMeaningfulValue(merchant.FullName) ? 3 : 0;
        score += HasMeaningfulValue(merchant.Email) ? 3 : 0;
        score += HasMeaningfulValue(merchant.Phone) ? 2 : 0;
        score += HasMeaningfulValue(merchant.StoreName) ? 2 : 0;
        score += HasMeaningfulValue(merchant.StoreAddress) ? 2 : 0;
        score += HasMeaningfulValue(merchant.StorePhone) ? 2 : 0;
        score += HasMeaningfulValue(merchant.AddressSummary) ? 2 : 0;
        score += HasMeaningfulValue(merchant.ReviewStatus) ? 1 : 0;
        score += HasMeaningfulValue(merchant.ReviewStatusLabel) ? 1 : 0;
        score += HasMeaningfulValue(merchant.ComplianceStatus) ? 1 : 0;
        score += HasMeaningfulValue(merchant.QueueBucket) ? 1 : 0;
        score += HasMeaningfulValue(merchant.RecommendedAction) ? 1 : 0;
        score += merchant.ProfileScore > 0 ? 1 : 0;
        score += merchant.IssueCount > 0 ? 1 : 0;
        score += merchant.Flags?.Count > 0 ? 1 : 0;

        return score;
    }

    private static int CalculateMerchantSignalLength(MerchantListItemApiDto merchant)
    {
        var values = new[]
        {
            merchant.ShopName,
            merchant.UserName,
            merchant.FullName,
            merchant.Email,
            merchant.Phone,
            merchant.StoreName,
            merchant.StoreAddress,
            merchant.StorePhone,
            merchant.AddressSummary,
            merchant.ReviewStatus,
            merchant.ReviewStatusLabel,
            merchant.ComplianceStatus,
            merchant.QueueBucket,
            merchant.RecommendedAction
        };

        return values.Sum(value => value?.Length ?? 0);
    }

    private static bool HasMeaningfulValue(string? value) => !string.IsNullOrWhiteSpace(value);

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

        if (!string.IsNullOrWhiteSpace(model.ReviewStatus))
        {
            query.Add($"reviewStatus={Uri.EscapeDataString(model.ReviewStatus)}");
        }

        if (!string.IsNullOrWhiteSpace(model.ReviewWindow))
        {
            query.Add($"reviewWindow={Uri.EscapeDataString(model.ReviewWindow)}");
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
        public string? ReviewStatus { get; set; }
        public string? ReviewWindow { get; set; }
        public List<MerchantOptionApiDto>? StatusOptions { get; set; }
        public List<MerchantOptionApiDto>? QueueOptions { get; set; }
        public List<MerchantOptionApiDto>? ReviewStatusOptions { get; set; }
        public List<MerchantOptionApiDto>? ReviewWindowOptions { get; set; }
        public List<string>? RejectReasonTemplates { get; set; }
    }

    private sealed class MerchantOptionApiDto
    {
        public string? Value { get; set; }
        public string? Text { get; set; }
    }

    private class MerchantListItemApiDto
    {
        public int SellerId { get; set; }
        public bool IsSellerApproved { get; set; }
        public string? ReviewStatus { get; set; }
        public string? ReviewStatusLabel { get; set; }
        public string? ReviewNote { get; set; }
        public DateTime? ReviewedAt { get; set; }
        public string? ShopName { get; set; }
        public string? UserName { get; set; }
        public string? FullName { get; set; }
        public string? Email { get; set; }
        public string? Phone { get; set; }
        public string? StoreName { get; set; }
        public string? StoreAddress { get; set; }
        public string? StoreEmail { get; set; }
        public string? StorePhone { get; set; }
        public string? Avatar { get; set; }
        public bool IsActive { get; set; }
        public DateTime CreatedAt { get; set; }
        public DateTime? UpdatedAt { get; set; }
        public DateTime? LastLogin { get; set; }
        public DateTime? ApplicationSubmittedAt { get; set; }
        public DateTime? ApplicationUpdatedAt { get; set; }
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
        public string? LegalFullName { get; set; }
        public string? IdentityNumberMasked { get; set; }
        public DateTime? IdentityIssuedDate { get; set; }
        public string? IdentityIssuedPlace { get; set; }
        public string? TaxCode { get; set; }
        public string? BusinessLicenseNumber { get; set; }
        public string? CitizenIdFrontUrl { get; set; }
        public string? CitizenIdBackUrl { get; set; }
        public string? BusinessLicenseUrl { get; set; }
        public string? AdditionalDocumentUrl { get; set; }
        public string? KycNotes { get; set; }
        public string? ComplianceSummary { get; set; }
        public List<string>? NextSteps { get; set; }
        public List<MerchantReviewHistoryApiDto>? ReviewHistory { get; set; }
    }

    private sealed class MerchantFlagApiDto
    {
        public string? Code { get; set; }
        public string? Label { get; set; }
        public string? Tone { get; set; }
    }

    private sealed class MerchantReviewHistoryApiDto
    {
        public string? Action { get; set; }
        public string? ReviewStatus { get; set; }
        public string? ReviewStatusLabel { get; set; }
        public string? Note { get; set; }
        public DateTime ReviewedAt { get; set; }
        public int? ReviewedByUserId { get; set; }
        public string? ReviewerUserName { get; set; }
        public string? ReviewerFullName { get; set; }
    }
}

