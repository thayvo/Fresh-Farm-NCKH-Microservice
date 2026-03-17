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
public sealed class AuditController : LegacySellerControllerBase
{
    private const string AccessTokenSessionKey = "ACCESS_TOKEN";

    private readonly IHttpClientFactory _httpClientFactory;

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true
    };

    public AuditController(IHttpClientFactory httpClientFactory)
    {
        _httpClientFactory = httpClientFactory;
    }

    [HttpGet]
    public async Task<IActionResult> Index(string? q = null, string? area = null, string? type = null)
    {
        var model = new AuditCenterPageViewModel
        {
            Query = q?.Trim() ?? string.Empty,
            Area = Normalize(area),
            Type = Normalize(type)
        };
        SeedFallbackOptions(model);

        try
        {
            var client = CreateOrderingClient();
            var response = await client.GetAsync(BuildEndpoint(model));
            if (!response.IsSuccessStatusCode)
            {
                ViewBag.Error = await ReadApiErrorAsync(response, "Khong the tai audit center.");
                return View(model);
            }

            var payload = await response.Content.ReadFromJsonAsync<AuditCenterApiResponse>(JsonOptions);
            if (payload is null)
            {
                ViewBag.Error = "Khong doc duoc du lieu audit center.";
                return View(model);
            }

            model.Query = payload.Filters?.Q ?? model.Query;
            model.Area = payload.Filters?.Area ?? model.Area;
            model.Type = payload.Filters?.Type ?? model.Type;
            model.Stats = new AuditCenterStatsViewModel
            {
                TotalActionLogs = payload.Stats?.TotalActionLogs ?? 0,
                Recent24hActions = payload.Stats?.Recent24hActions ?? 0,
                ModerationCount = payload.Stats?.ModerationCount ?? 0,
                SettlementCount = payload.Stats?.SettlementCount ?? 0,
                SettlementAmount7d = payload.Stats?.SettlementAmount7d ?? 0m
            };
            model.AreaOptions = payload.Filters?.AreaOptions?.Select(MapOption).ToList() ?? model.AreaOptions;
            model.TypeOptions = payload.Filters?.TypeOptions?.Select(MapOption).ToList() ?? model.TypeOptions;
            model.ActionLogs = payload.Actions?.Select(x => new AdminActionLogRowViewModel
            {
                AdminActionLogId = x.AdminActionLogId,
                Area = x.Area ?? string.Empty,
                ActionName = x.ActionName ?? string.Empty,
                TargetType = x.TargetType ?? string.Empty,
                TargetId = x.TargetId,
                Summary = x.Summary ?? string.Empty,
                ActorUserId = x.ActorUserId,
                MetadataJson = x.MetadataJson,
                CreatedAt = x.CreatedAt
            }).ToList() ?? new List<AdminActionLogRowViewModel>();
            model.ModerationAudits = payload.ModerationAudits?.Select(x => new ModerationAuditRowViewModel
            {
                ModerationAuditId = x.ModerationAuditId,
                AdminActionLogId = x.AdminActionLogId,
                SubjectType = x.SubjectType ?? string.Empty,
                SubjectId = x.SubjectId,
                Decision = x.Decision ?? string.Empty,
                Notes = x.Notes,
                ActorUserId = x.ActorUserId,
                CreatedAt = x.CreatedAt
            }).ToList() ?? new List<ModerationAuditRowViewModel>();
            model.SettlementAudits = payload.SettlementAudits?.Select(x => new SettlementAuditRowViewModel
            {
                SettlementAuditId = x.SettlementAuditId,
                AdminActionLogId = x.AdminActionLogId,
                AuditType = x.AuditType ?? string.Empty,
                ReferenceType = x.ReferenceType ?? string.Empty,
                ReferenceId = x.ReferenceId,
                SellerId = x.SellerId,
                Amount = x.Amount,
                Currency = x.Currency ?? string.Empty,
                Notes = x.Notes,
                ActorUserId = x.ActorUserId,
                CreatedAt = x.CreatedAt
            }).ToList() ?? new List<SettlementAuditRowViewModel>();
        }
        catch (Exception ex)
        {
            ViewBag.Error = "Loi khi tai audit center: " + ex.Message;
        }

        return View(model);
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

    private static string BuildEndpoint(AuditCenterPageViewModel model)
    {
        var query = new List<string>
        {
            "area=" + Uri.EscapeDataString(model.Area),
            "type=" + Uri.EscapeDataString(model.Type)
        };

        if (!string.IsNullOrWhiteSpace(model.Query))
        {
            query.Add("q=" + Uri.EscapeDataString(model.Query));
        }

        return "/api/orders/admin/audit/center?" + string.Join("&", query);
    }

    private static string Normalize(string? value)
        => string.IsNullOrWhiteSpace(value) ? "all" : value.Trim().ToLowerInvariant();

    private static void SeedFallbackOptions(AuditCenterPageViewModel model)
    {
        model.AreaOptions =
        [
            new AuditOptionViewModel { Value = "all", Text = "Tất cả" },
            new AuditOptionViewModel { Value = "campaign_center", Text = "Trung tâm chiến dịch" },
            new AuditOptionViewModel { Value = "risk_center", Text = "Trung tâm rủi ro" }
        ];

        model.TypeOptions =
        [
            new AuditOptionViewModel { Value = "all", Text = "Tất cả" },
            new AuditOptionViewModel { Value = "campaign", Text = "Chiến dịch" },
            new AuditOptionViewModel { Value = "ads_wallet", Text = "Ví quảng cáo" },
            new AuditOptionViewModel { Value = "ads_campaign", Text = "Chiến dịch quảng cáo" },
            new AuditOptionViewModel { Value = "risk_case", Text = "Ca rủi ro" },
            new AuditOptionViewModel { Value = "risk_center", Text = "Trung tâm rủi ro" }
        ];
    }

    private static AuditOptionViewModel MapOption(AuditOptionApiModel option)
        => new()
        {
            Value = option.Value ?? string.Empty,
            Text = TranslateAuditOption(option.Value, option.Text)
        };

    private static string TranslateAuditOption(string? value, string? text)
        => (value ?? string.Empty).ToLowerInvariant() switch
        {
            "all" => "Tất cả",
            "campaign_center" => "Trung tâm chiến dịch",
            "risk_center" => "Trung tâm rủi ro",
            "campaign" => "Chiến dịch",
            "ads_wallet" => "Ví quảng cáo",
            "ads_campaign" => "Chiến dịch quảng cáo",
            "risk_case" => "Ca rủi ro",
            _ => text ?? string.Empty
        };

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

    private sealed class AuditCenterApiResponse
    {
        public AuditStatsApiModel? Stats { get; set; }

        public AuditFiltersApiModel? Filters { get; set; }

        public List<AdminActionLogApiModel>? Actions { get; set; }

        public List<ModerationAuditApiModel>? ModerationAudits { get; set; }

        public List<SettlementAuditApiModel>? SettlementAudits { get; set; }
    }

    private sealed class AuditStatsApiModel
    {
        public int TotalActionLogs { get; set; }

        public int Recent24hActions { get; set; }

        public int ModerationCount { get; set; }

        public int SettlementCount { get; set; }

        public decimal SettlementAmount7d { get; set; }
    }

    private sealed class AuditFiltersApiModel
    {
        public string? Q { get; set; }

        public string? Area { get; set; }

        public string? Type { get; set; }

        public List<AuditOptionApiModel>? AreaOptions { get; set; }

        public List<AuditOptionApiModel>? TypeOptions { get; set; }
    }

    private sealed class AuditOptionApiModel
    {
        public string? Value { get; set; }

        public string? Text { get; set; }
    }

    private sealed class AdminActionLogApiModel
    {
        public int AdminActionLogId { get; set; }

        public string? Area { get; set; }

        public string? ActionName { get; set; }

        public string? TargetType { get; set; }

        public int? TargetId { get; set; }

        public string? Summary { get; set; }

        public int? ActorUserId { get; set; }

        public string? MetadataJson { get; set; }

        public DateTime CreatedAt { get; set; }
    }

    private sealed class ModerationAuditApiModel
    {
        public int ModerationAuditId { get; set; }

        public int? AdminActionLogId { get; set; }

        public string? SubjectType { get; set; }

        public int? SubjectId { get; set; }

        public string? Decision { get; set; }

        public string? Notes { get; set; }

        public int? ActorUserId { get; set; }

        public DateTime CreatedAt { get; set; }
    }

    private sealed class SettlementAuditApiModel
    {
        public int SettlementAuditId { get; set; }

        public int? AdminActionLogId { get; set; }

        public string? AuditType { get; set; }

        public string? ReferenceType { get; set; }

        public int? ReferenceId { get; set; }

        public int? SellerId { get; set; }

        public decimal? Amount { get; set; }

        public string? Currency { get; set; }

        public string? Notes { get; set; }

        public int? ActorUserId { get; set; }

        public DateTime CreatedAt { get; set; }
    }
}
