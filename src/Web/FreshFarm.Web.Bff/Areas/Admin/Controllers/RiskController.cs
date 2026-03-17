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
public sealed class RiskController : LegacySellerControllerBase
{
    private const string AccessTokenSessionKey = "ACCESS_TOKEN";

    private readonly IHttpClientFactory _httpClientFactory;

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true
    };

    public RiskController(IHttpClientFactory httpClientFactory)
    {
        _httpClientFactory = httpClientFactory;
    }

    [HttpGet]
    public async Task<IActionResult> Index(string? q = null, string? type = null, string? status = null, string? severity = null, int? riskCaseId = null)
    {
        var model = new RiskCenterPageViewModel
        {
            Query = q?.Trim() ?? string.Empty,
            Type = NormalizeOption(type),
            Status = NormalizeOption(status),
            Severity = NormalizeOption(severity),
            SelectedCaseId = riskCaseId > 0 ? riskCaseId : null
        };
        SeedFallbackOptions(model);

        try
        {
            var client = CreateOrderingClient();
            var overviewTask = client.GetAsync("/api/orders/admin/risk/overview");
            var listTask = client.GetAsync(BuildListEndpoint(model));
            await Task.WhenAll(overviewTask, listTask);

            var overviewResponse = await overviewTask;
            if (overviewResponse.IsSuccessStatusCode)
            {
                var overview = await overviewResponse.Content.ReadFromJsonAsync<RiskOverviewApiResponse>(JsonOptions);
                if (overview is not null)
                {
                    model.Overview = new RiskOverviewViewModel
                    {
                        TotalCases = overview.Stats?.TotalCases ?? 0,
                        OpenCases = overview.Stats?.OpenCases ?? 0,
                        ReviewCases = overview.Stats?.ReviewCases ?? 0,
                        EscalatedCases = overview.Stats?.EscalatedCases ?? 0,
                        HighSeverityCases = overview.Stats?.HighSeverityCases ?? 0,
                        VoucherAbuseCases = overview.Stats?.VoucherAbuseCases ?? 0,
                        ReturnSpikeCases = overview.Stats?.ReturnSpikeCases ?? 0,
                        DecisionsLast7d = overview.Stats?.DecisionsLast7d ?? 0,
                        Recent = overview.Recent?.Select(x => new RiskRecentCaseViewModel
                        {
                            RiskCaseId = x.RiskCaseId,
                            CaseType = x.CaseType ?? string.Empty,
                            Title = x.Title ?? string.Empty,
                            Status = x.Status ?? string.Empty,
                            Severity = x.Severity ?? string.Empty,
                            SignalCount = x.SignalCount,
                            UpdatedAt = x.UpdatedAt,
                            LastSignalAt = x.LastSignalAt
                        }).ToList() ?? new List<RiskRecentCaseViewModel>()
                    };
                }
            }
            else
            {
                ViewBag.Error = await ReadApiErrorAsync(overviewResponse, "Khong the tai risk overview.");
            }

            var listResponse = await listTask;
            if (!listResponse.IsSuccessStatusCode)
            {
                ViewBag.Error = await ReadApiErrorAsync(listResponse, "Khong the tai risk queue.");
                return View(model);
            }

            var payload = await listResponse.Content.ReadFromJsonAsync<RiskListApiResponse>(JsonOptions);
            if (payload is not null)
            {
                model.Query = payload.Filters?.Q ?? model.Query;
                model.Type = payload.Filters?.Type ?? model.Type;
                model.Status = payload.Filters?.Status ?? model.Status;
                model.Severity = payload.Filters?.Severity ?? model.Severity;
                model.TypeOptions = payload.Filters?.TypeOptions?.Select(MapOption).ToList() ?? model.TypeOptions;
                model.StatusOptions = payload.Filters?.StatusOptions?.Select(MapOption).ToList() ?? model.StatusOptions;
                model.SeverityOptions = payload.Filters?.SeverityOptions?.Select(MapOption).ToList() ?? model.SeverityOptions;
                model.Rows = payload.Rows?.Select(x => new RiskCaseRowViewModel
                {
                    RiskCaseId = x.RiskCaseId,
                    CaseType = x.CaseType ?? string.Empty,
                    Title = x.Title ?? string.Empty,
                    Summary = x.Summary,
                    Status = x.Status ?? string.Empty,
                    Severity = x.Severity ?? string.Empty,
                    SellerId = x.SellerId,
                    BuyerId = x.BuyerId,
                    OrderId = x.OrderId,
                    CampaignId = x.CampaignId,
                    VoucherCouponId = x.VoucherCouponId,
                    SignalCount = x.SignalCount,
                    IsEscalated = x.IsEscalated,
                    CreatedAt = x.CreatedAt,
                    UpdatedAt = x.UpdatedAt,
                    LastSignalAt = x.LastSignalAt
                }).ToList() ?? new List<RiskCaseRowViewModel>();
            }

            if (!model.SelectedCaseId.HasValue && model.Rows.Count > 0)
            {
                model.SelectedCaseId = model.Rows[0].RiskCaseId;
            }

            if (model.SelectedCaseId.HasValue)
            {
                await LoadCaseDetailsAsync(client, model, model.SelectedCaseId.Value);
            }
        }
        catch (Exception ex)
        {
            ViewBag.Error = "Loi khi tai risk center: " + ex.Message;
        }

        return View(model);
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> SyncHeuristics(string? q, string? type, string? status, string? severity, int? riskCaseId)
    {
        try
        {
            var client = CreateOrderingClient();
            var response = await client.PostAsync("/api/orders/admin/risk/sync-heuristics", null);
            TempData[response.IsSuccessStatusCode ? "Success" : "Error"] = response.IsSuccessStatusCode
                ? await ReadApiSuccessAsync(response, "Da dong bo heuristic risk center.")
                : await ReadApiErrorAsync(response, "Khong the dong bo heuristic risk center.");
        }
        catch (Exception ex)
        {
            TempData["Error"] = "Loi khi dong bo heuristic risk center: " + ex.Message;
        }

        return RedirectToAction(nameof(Index), new { q, type, status, severity, riskCaseId });
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> SubmitDecision(RiskDecisionInputModel input, string? q, string? type, string? status, string? severity)
    {
        if (input.RiskCaseId <= 0)
        {
            TempData["Error"] = "Risk case khong hop le.";
            return RedirectToAction(nameof(Index), new { q, type, status, severity });
        }

        try
        {
            var client = CreateOrderingClient();
            var response = await client.PostAsJsonAsync($"/api/orders/admin/risk/cases/{input.RiskCaseId}/decisions", input);
            TempData[response.IsSuccessStatusCode ? "Success" : "Error"] = response.IsSuccessStatusCode
                ? await ReadApiSuccessAsync(response, "Da ghi nhan quyet dinh risk case.")
                : await ReadApiErrorAsync(response, "Khong the ghi nhan quyet dinh risk case.");
        }
        catch (Exception ex)
        {
            TempData["Error"] = "Loi khi xu ly risk case: " + ex.Message;
        }

        return RedirectToAction(nameof(Index), new { q, type, status, severity, riskCaseId = input.RiskCaseId });
    }

    private async Task LoadCaseDetailsAsync(HttpClient client, RiskCenterPageViewModel model, int riskCaseId)
    {
        var response = await client.GetAsync($"/api/orders/admin/risk/cases/{riskCaseId}");
        if (!response.IsSuccessStatusCode)
        {
            ViewBag.DetailError = await ReadApiErrorAsync(response, "Khong the tai chi tiet risk case.");
            return;
        }

        var payload = await response.Content.ReadFromJsonAsync<RiskCaseDetailApiResponse>(JsonOptions);
        if (payload is null)
        {
            ViewBag.DetailError = "Khong doc duoc chi tiet risk case.";
            return;
        }

        model.SelectedCase = new RiskCaseDetailViewModel
        {
            RiskCaseId = payload.RiskCaseId,
            ReferenceKey = payload.ReferenceKey ?? string.Empty,
            CaseType = payload.CaseType ?? string.Empty,
            Title = payload.Title ?? string.Empty,
            Summary = payload.Summary,
            Status = payload.Status ?? string.Empty,
            Severity = payload.Severity ?? string.Empty,
            SellerId = payload.SellerId,
            BuyerId = payload.BuyerId,
            OrderId = payload.OrderId,
            CampaignId = payload.CampaignId,
            VoucherCouponId = payload.VoucherCouponId,
            VoucherCode = payload.VoucherCode,
            SignalCount = payload.SignalCount,
            IsEscalated = payload.IsEscalated,
            CreatedAt = payload.CreatedAt,
            UpdatedAt = payload.UpdatedAt,
            LastSignalAt = payload.LastSignalAt,
            ReviewedAt = payload.ReviewedAt,
            ReviewedBy = payload.ReviewedBy,
            Signals = payload.Signals?.Select(x => new RiskSignalViewModel
            {
                RiskSignalId = x.RiskSignalId,
                SignalType = x.SignalType ?? string.Empty,
                SignalCode = x.SignalCode ?? string.Empty,
                Severity = x.Severity ?? string.Empty,
                Source = x.Source ?? string.Empty,
                Score = x.Score,
                MetadataJson = x.MetadataJson,
                TriggeredAt = x.TriggeredAt
            }).ToList() ?? new List<RiskSignalViewModel>(),
            Decisions = payload.Decisions?.Select(x => new RiskDecisionHistoryViewModel
            {
                RiskDecisionId = x.RiskDecisionId,
                DecisionType = x.DecisionType ?? string.Empty,
                Notes = x.Notes,
                CreatedBy = x.CreatedBy,
                CreatedAt = x.CreatedAt
            }).ToList() ?? new List<RiskDecisionHistoryViewModel>(),
            VoucherAbuseCases = payload.VoucherAbuseCases?.Select(x => new VoucherAbuseDetailViewModel
            {
                VoucherAbuseCaseId = x.VoucherAbuseCaseId,
                CouponId = x.CouponId,
                BuyerId = x.BuyerId,
                SellerId = x.SellerId,
                CampaignId = x.CampaignId,
                OrderId = x.OrderId,
                AbuseType = x.AbuseType ?? string.Empty,
                SuspectedBenefitAmount = x.SuspectedBenefitAmount,
                Status = x.Status ?? string.Empty,
                CreatedAt = x.CreatedAt,
                ReviewedAt = x.ReviewedAt
            }).ToList() ?? new List<VoucherAbuseDetailViewModel>(),
            DecisionOptions = payload.DecisionOptions?.Select(MapOption).ToList() ?? new List<RiskOptionViewModel>()
        };

        model.DecisionEditor = new RiskDecisionInputModel
        {
            RiskCaseId = payload.RiskCaseId,
            DecisionType = model.SelectedCase.DecisionOptions.FirstOrDefault()?.Value ?? "monitor"
        };
    }

    private HttpClient CreateOrderingClient()
    {
        var client = _httpClientFactory.CreateClient("Ordering");
        client.DefaultRequestHeaders.Remove("Authorization");

        var token = GetAccessToken(AccessTokenSessionKey);
        if (!string.IsNullOrWhiteSpace(token))
        {
            client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);
        }

        return client;
    }

    private static string BuildListEndpoint(RiskCenterPageViewModel model)
    {
        var query = new List<string>
        {
            "type=" + Uri.EscapeDataString(model.Type),
            "status=" + Uri.EscapeDataString(model.Status),
            "severity=" + Uri.EscapeDataString(model.Severity)
        };

        if (!string.IsNullOrWhiteSpace(model.Query))
        {
            query.Add("q=" + Uri.EscapeDataString(model.Query));
        }

        return "/api/orders/admin/risk/cases?" + string.Join("&", query);
    }

    private static string NormalizeOption(string? value)
        => string.IsNullOrWhiteSpace(value) ? "all" : value.Trim().ToLowerInvariant();

    private static RiskOptionViewModel MapOption(RiskOptionApiModel option)
        => new()
        {
            Value = option.Value ?? string.Empty,
            Text = TranslateRiskOption(option.Value, option.Text)
        };

    private static string TranslateRiskOption(string? value, string? text)
        => (value ?? string.Empty).ToLowerInvariant() switch
        {
            "all" => "Tất cả",
            "voucher_abuse" => "Lạm dụng voucher",
            "return_spike" => "Tăng đột biến hoàn trả",
            "open" => "Mở",
            "pending_review" => "Chờ duyệt",
            "monitoring" => "Theo dõi",
            "resolved" => "Đã xử lý",
            "dismissed" => "Bỏ qua",
            "low" => "Thấp",
            "medium" => "Trung bình",
            "high" => "Cao",
            "critical" => "Nghiêm trọng",
            _ => text ?? string.Empty
        };

    private static void SeedFallbackOptions(RiskCenterPageViewModel model)
    {
        model.TypeOptions =
        [
            new RiskOptionViewModel { Value = "all", Text = "Tất cả" },
            new RiskOptionViewModel { Value = "voucher_abuse", Text = "Lạm dụng voucher" },
            new RiskOptionViewModel { Value = "return_spike", Text = "Tăng đột biến hoàn trả" }
        ];

        model.StatusOptions =
        [
            new RiskOptionViewModel { Value = "all", Text = "Tất cả" },
            new RiskOptionViewModel { Value = "open", Text = "Mở" },
            new RiskOptionViewModel { Value = "pending_review", Text = "Chờ duyệt" },
            new RiskOptionViewModel { Value = "monitoring", Text = "Theo dõi" },
            new RiskOptionViewModel { Value = "resolved", Text = "Đã xử lý" },
            new RiskOptionViewModel { Value = "dismissed", Text = "Bỏ qua" }
        ];

        model.SeverityOptions =
        [
            new RiskOptionViewModel { Value = "all", Text = "Tất cả" },
            new RiskOptionViewModel { Value = "low", Text = "Thấp" },
            new RiskOptionViewModel { Value = "medium", Text = "Trung bình" },
            new RiskOptionViewModel { Value = "high", Text = "Cao" },
            new RiskOptionViewModel { Value = "critical", Text = "Nghiêm trọng" }
        ];
    }

    private static async Task<string> ReadApiErrorAsync(HttpResponseMessage response, string fallback)
    {
        var body = await response.Content.ReadAsStringAsync();
        var statusPrefix = $"HTTP {(int)response.StatusCode}";
        if (string.IsNullOrWhiteSpace(body))
        {
            return $"{fallback} ({statusPrefix})";
        }

        try
        {
            using var doc = JsonDocument.Parse(body);
            if (doc.RootElement.TryGetProperty("message", out var messageElement) && messageElement.ValueKind == JsonValueKind.String)
            {
                var message = messageElement.GetString();
                return string.IsNullOrWhiteSpace(message) ? $"{fallback} ({statusPrefix})" : $"{statusPrefix}: {message}";
            }
        }
        catch
        {
        }

        return $"{fallback} ({statusPrefix})";
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
            if (doc.RootElement.TryGetProperty("message", out var messageElement) && messageElement.ValueKind == JsonValueKind.String)
            {
                return messageElement.GetString() ?? fallback;
            }
        }
        catch
        {
        }

        return fallback;
    }

    private sealed class RiskOverviewApiResponse
    {
        public RiskStatsApiModel? Stats { get; set; }

        public List<RiskRecentApiModel>? Recent { get; set; }
    }

    private sealed class RiskStatsApiModel
    {
        public int TotalCases { get; set; }

        public int OpenCases { get; set; }

        public int ReviewCases { get; set; }

        public int EscalatedCases { get; set; }

        public int HighSeverityCases { get; set; }

        public int VoucherAbuseCases { get; set; }

        public int ReturnSpikeCases { get; set; }

        public int DecisionsLast7d { get; set; }
    }

    private sealed class RiskRecentApiModel
    {
        public int RiskCaseId { get; set; }

        public string? CaseType { get; set; }

        public string? Title { get; set; }

        public string? Status { get; set; }

        public string? Severity { get; set; }

        public int SignalCount { get; set; }

        public DateTime? UpdatedAt { get; set; }

        public DateTime? LastSignalAt { get; set; }
    }

    private sealed class RiskListApiResponse
    {
        public RiskFiltersApiModel? Filters { get; set; }

        public List<RiskCaseRowApiModel>? Rows { get; set; }
    }

    private sealed class RiskFiltersApiModel
    {
        public string? Q { get; set; }

        public string? Type { get; set; }

        public string? Status { get; set; }

        public string? Severity { get; set; }

        public List<RiskOptionApiModel>? TypeOptions { get; set; }

        public List<RiskOptionApiModel>? StatusOptions { get; set; }

        public List<RiskOptionApiModel>? SeverityOptions { get; set; }
    }

    private sealed class RiskOptionApiModel
    {
        public string? Value { get; set; }

        public string? Text { get; set; }
    }

    private sealed class RiskCaseRowApiModel
    {
        public int RiskCaseId { get; set; }

        public string? CaseType { get; set; }

        public string? Title { get; set; }

        public string? Summary { get; set; }

        public string? Status { get; set; }

        public string? Severity { get; set; }

        public int? SellerId { get; set; }

        public int? BuyerId { get; set; }

        public int? OrderId { get; set; }

        public int? CampaignId { get; set; }

        public int? VoucherCouponId { get; set; }

        public int SignalCount { get; set; }

        public bool IsEscalated { get; set; }

        public DateTime CreatedAt { get; set; }

        public DateTime? UpdatedAt { get; set; }

        public DateTime? LastSignalAt { get; set; }
    }

    private sealed class RiskCaseDetailApiResponse
    {
        public int RiskCaseId { get; set; }

        public string? ReferenceKey { get; set; }

        public string? CaseType { get; set; }

        public string? Title { get; set; }

        public string? Summary { get; set; }

        public string? Status { get; set; }

        public string? Severity { get; set; }

        public int? SellerId { get; set; }

        public int? BuyerId { get; set; }

        public int? OrderId { get; set; }

        public int? CampaignId { get; set; }

        public int? VoucherCouponId { get; set; }

        public string? VoucherCode { get; set; }

        public int SignalCount { get; set; }

        public bool IsEscalated { get; set; }

        public DateTime CreatedAt { get; set; }

        public DateTime? UpdatedAt { get; set; }

        public DateTime? LastSignalAt { get; set; }

        public DateTime? ReviewedAt { get; set; }

        public int? ReviewedBy { get; set; }

        public List<RiskSignalApiModel>? Signals { get; set; }

        public List<RiskDecisionApiModel>? Decisions { get; set; }

        public List<VoucherAbuseApiModel>? VoucherAbuseCases { get; set; }

        public List<RiskOptionApiModel>? DecisionOptions { get; set; }
    }

    private sealed class RiskSignalApiModel
    {
        public int RiskSignalId { get; set; }

        public string? SignalType { get; set; }

        public string? SignalCode { get; set; }

        public string? Severity { get; set; }

        public string? Source { get; set; }

        public decimal Score { get; set; }

        public string? MetadataJson { get; set; }

        public DateTime TriggeredAt { get; set; }
    }

    private sealed class RiskDecisionApiModel
    {
        public int RiskDecisionId { get; set; }

        public string? DecisionType { get; set; }

        public string? Notes { get; set; }

        public int? CreatedBy { get; set; }

        public DateTime CreatedAt { get; set; }
    }

    private sealed class VoucherAbuseApiModel
    {
        public int VoucherAbuseCaseId { get; set; }

        public int CouponId { get; set; }

        public int? BuyerId { get; set; }

        public int? SellerId { get; set; }

        public int? CampaignId { get; set; }

        public int? OrderId { get; set; }

        public string? AbuseType { get; set; }

        public decimal? SuspectedBenefitAmount { get; set; }

        public string? Status { get; set; }

        public DateTime CreatedAt { get; set; }

        public DateTime? ReviewedAt { get; set; }
    }
}
