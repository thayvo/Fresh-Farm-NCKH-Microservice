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
    public async Task<IActionResult> Index(
        string? q = null,
        string? area = null,
        string? type = null,
        string? authQ = null,
        string? authRole = null,
        string? authOutcome = null)
    {
        var model = new AuditCenterPageViewModel
        {
            Query = q?.Trim() ?? string.Empty,
            Area = Normalize(area),
            Type = Normalize(type),
            AuthQuery = authQ?.Trim() ?? string.Empty,
            AuthRole = Normalize(authRole),
            AuthOutcome = Normalize(authOutcome)
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
            model.AreaOptions = DeduplicateOptions(payload.Filters?.AreaOptions).Select(MapOption).ToList();
            model.TypeOptions = DeduplicateOptions(payload.Filters?.TypeOptions).Select(MapOption).ToList();
            model.ActionLogs = DeduplicateActionLogs(payload.Actions).Select(x => new AdminActionLogRowViewModel
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
            }).ToList();
            model.ModerationAudits = DeduplicateModerationAudits(payload.ModerationAudits).Select(x => new ModerationAuditRowViewModel
            {
                ModerationAuditId = x.ModerationAuditId,
                AdminActionLogId = x.AdminActionLogId,
                SubjectType = x.SubjectType ?? string.Empty,
                SubjectId = x.SubjectId,
                Decision = x.Decision ?? string.Empty,
                Notes = x.Notes,
                ActorUserId = x.ActorUserId,
                CreatedAt = x.CreatedAt
            }).ToList();
            model.SettlementAudits = DeduplicateSettlementAudits(payload.SettlementAudits).Select(x => new SettlementAuditRowViewModel
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
            }).ToList();
        }
        catch (Exception ex)
        {
            ViewBag.Error = "Loi khi tai audit center: " + ex.Message;
        }

        try
        {
            var client = CreateIdentityClient();
            var response = await client.GetAsync(BuildAuthEndpoint(model));
            if (!response.IsSuccessStatusCode)
            {
                ViewBag.AuthError = await ReadApiErrorAsync(response, "Khong the tai auth audit.");
                return View(model);
            }

            var payload = await response.Content.ReadFromJsonAsync<AuthAuditApiResponse>(JsonOptions);
            if (payload is null)
            {
                ViewBag.AuthError = "Khong doc duoc du lieu auth audit.";
                return View(model);
            }

            model.AuthQuery = payload.Filters?.Q ?? model.AuthQuery;
            model.AuthRole = payload.Filters?.Role ?? model.AuthRole;
            model.AuthOutcome = payload.Filters?.Outcome ?? model.AuthOutcome;
            model.AuthStats = new AuthAuditStatsViewModel
            {
                Total24h = payload.Stats?.Total24h ?? 0,
                Success24h = payload.Stats?.Success24h ?? 0,
                Failed24h = payload.Stats?.Failed24h ?? 0,
                Locked24h = payload.Stats?.Locked24h ?? 0,
                Suspicious24h = payload.Stats?.Suspicious24h ?? 0,
                UniqueIp24h = payload.Stats?.UniqueIp24h ?? 0,
                ForeignBackoffice24h = payload.Stats?.ForeignBackoffice24h ?? 0,
                Admin24h = payload.Stats?.Admin24h ?? 0,
                Seller24h = payload.Stats?.Seller24h ?? 0,
                Customer24h = payload.Stats?.Customer24h ?? 0,
                Unknown24h = payload.Stats?.Unknown24h ?? 0
            };
            model.AuthRoleOptions = DeduplicateOptions(payload.Filters?.RoleOptions).Select(MapOption).ToList();
            model.AuthOutcomeOptions = DeduplicateOptions(payload.Filters?.OutcomeOptions).Select(MapOption).ToList();
            model.AuthAudits = DeduplicateAuthAudits(payload.Items).Select(x => new AuthAuditRowViewModel
            {
                AuthAuditLogId = x.AuthAuditLogId,
                UserId = x.UserId,
                ClientLane = x.ClientLane ?? string.Empty,
                RoleName = x.RoleName ?? string.Empty,
                Identifier = x.Identifier ?? string.Empty,
                UserName = x.UserName,
                Email = x.Email,
                EventType = x.EventType ?? string.Empty,
                Success = x.Success,
                FailureReason = x.FailureReason,
                FailedAttemptCount = x.FailedAttemptCount,
                IpAddress = x.IpAddress,
                ForwardedFor = x.ForwardedFor,
                CountryCode = x.CountryCode,
                CountryName = x.CountryName,
                RegionName = x.RegionName,
                CityName = x.CityName,
                UserAgent = x.UserAgent,
                DeviceType = x.DeviceType,
                BrowserFamily = x.BrowserFamily,
                OperatingSystem = x.OperatingSystem,
                IsSuspicious = x.IsSuspicious,
                SuspicionReasons = x.SuspicionReasons,
                OccurredAt = x.OccurredAt
            }).ToList();
        }
        catch (Exception ex)
        {
            ViewBag.AuthError = "Loi khi tai auth audit: " + ex.Message;
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

    private static string BuildAuthEndpoint(AuditCenterPageViewModel model)
    {
        var query = new List<string>
        {
            "role=" + Uri.EscapeDataString(model.AuthRole),
            "outcome=" + Uri.EscapeDataString(model.AuthOutcome)
        };

        if (!string.IsNullOrWhiteSpace(model.AuthQuery))
        {
            query.Add("q=" + Uri.EscapeDataString(model.AuthQuery));
        }

        return "/admin/auth-audit?" + string.Join("&", query);
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

        model.AuthRoleOptions =
        [
            new AuditOptionViewModel { Value = "all", Text = "Tất cả" },
            new AuditOptionViewModel { Value = "admin", Text = "Admin" },
            new AuditOptionViewModel { Value = "seller", Text = "Seller" },
            new AuditOptionViewModel { Value = "customer", Text = "Người dùng" },
            new AuditOptionViewModel { Value = "unknown", Text = "Không xác định" }
        ];

        model.AuthOutcomeOptions =
        [
            new AuditOptionViewModel { Value = "all", Text = "Tất cả" },
            new AuditOptionViewModel { Value = "success", Text = "Thành công" },
            new AuditOptionViewModel { Value = "failed", Text = "Thất bại" },
            new AuditOptionViewModel { Value = "locked", Text = "Bị khóa" },
            new AuditOptionViewModel { Value = "suspicious", Text = "Đáng ngờ" }
        ];
    }

    private static AuditOptionViewModel MapOption(AuditOptionApiModel option)
        => new()
        {
            Value = option.Value ?? string.Empty,
            Text = TranslateAuditOption(option.Value, option.Text)
        };

    private static List<AuditOptionApiModel> DeduplicateOptions(IEnumerable<AuditOptionApiModel>? options)
    {
        return options?
            .Where(option => HasMeaningfulValue(option.Value))
            .GroupBy(option => option.Value!, StringComparer.OrdinalIgnoreCase)
            .Select(group => group
                .OrderByDescending(option => HasMeaningfulValue(option.Text))
                .ThenByDescending(CalculateOptionSignalLength)
                .First())
            .ToList() ?? new List<AuditOptionApiModel>();
    }

    private static List<AdminActionLogApiModel> DeduplicateActionLogs(IEnumerable<AdminActionLogApiModel>? items)
    {
        return items?
            .Where(item => item.AdminActionLogId > 0)
            .GroupBy(item => item.AdminActionLogId)
            .Select(group => group
                .OrderByDescending(CalculateActionLogScore)
                .ThenByDescending(CalculateActionLogSignalLength)
                .ThenByDescending(item => item.CreatedAt)
                .First())
            .ToList() ?? new List<AdminActionLogApiModel>();
    }

    private static List<ModerationAuditApiModel> DeduplicateModerationAudits(IEnumerable<ModerationAuditApiModel>? items)
    {
        return items?
            .Where(item => item.ModerationAuditId > 0)
            .GroupBy(item => item.ModerationAuditId)
            .Select(group => group
                .OrderByDescending(CalculateModerationAuditScore)
                .ThenByDescending(CalculateModerationAuditSignalLength)
                .ThenByDescending(item => item.CreatedAt)
                .First())
            .ToList() ?? new List<ModerationAuditApiModel>();
    }

    private static List<SettlementAuditApiModel> DeduplicateSettlementAudits(IEnumerable<SettlementAuditApiModel>? items)
    {
        return items?
            .Where(item => item.SettlementAuditId > 0)
            .GroupBy(item => item.SettlementAuditId)
            .Select(group => group
                .OrderByDescending(CalculateSettlementAuditScore)
                .ThenByDescending(CalculateSettlementAuditSignalLength)
                .ThenByDescending(item => item.CreatedAt)
                .First())
            .ToList() ?? new List<SettlementAuditApiModel>();
    }

    private static List<AuthAuditApiModel> DeduplicateAuthAudits(IEnumerable<AuthAuditApiModel>? items)
    {
        return items?
            .Where(item => item.AuthAuditLogId > 0)
            .GroupBy(item => item.AuthAuditLogId)
            .Select(group => group
                .OrderByDescending(CalculateAuthAuditScore)
                .ThenByDescending(CalculateAuthAuditSignalLength)
                .ThenByDescending(item => item.OccurredAt)
                .First())
            .ToList() ?? new List<AuthAuditApiModel>();
    }

    private static int CalculateOptionSignalLength(AuditOptionApiModel option)
        => (option.Value?.Length ?? 0) + (option.Text?.Length ?? 0);

    private static int CalculateActionLogScore(AdminActionLogApiModel item)
    {
        var score = 0;
        score += HasMeaningfulValue(item.Area) ? 1 : 0;
        score += HasMeaningfulValue(item.ActionName) ? 1 : 0;
        score += HasMeaningfulValue(item.TargetType) ? 1 : 0;
        score += item.TargetId.HasValue ? 1 : 0;
        score += HasMeaningfulValue(item.Summary) ? 2 : 0;
        score += item.ActorUserId.HasValue ? 1 : 0;
        score += HasMeaningfulValue(item.MetadataJson) ? 1 : 0;
        return score;
    }

    private static int CalculateActionLogSignalLength(AdminActionLogApiModel item)
        => (item.Area?.Length ?? 0)
        + (item.ActionName?.Length ?? 0)
        + (item.TargetType?.Length ?? 0)
        + (item.Summary?.Length ?? 0)
        + (item.MetadataJson?.Length ?? 0);

    private static int CalculateModerationAuditScore(ModerationAuditApiModel item)
    {
        var score = 0;
        score += item.AdminActionLogId.HasValue ? 1 : 0;
        score += HasMeaningfulValue(item.SubjectType) ? 1 : 0;
        score += item.SubjectId.HasValue ? 1 : 0;
        score += HasMeaningfulValue(item.Decision) ? 2 : 0;
        score += HasMeaningfulValue(item.Notes) ? 1 : 0;
        score += item.ActorUserId.HasValue ? 1 : 0;
        return score;
    }

    private static int CalculateModerationAuditSignalLength(ModerationAuditApiModel item)
        => (item.SubjectType?.Length ?? 0) + (item.Decision?.Length ?? 0) + (item.Notes?.Length ?? 0);

    private static int CalculateSettlementAuditScore(SettlementAuditApiModel item)
    {
        var score = 0;
        score += item.AdminActionLogId.HasValue ? 1 : 0;
        score += HasMeaningfulValue(item.AuditType) ? 1 : 0;
        score += HasMeaningfulValue(item.ReferenceType) ? 1 : 0;
        score += item.ReferenceId.HasValue ? 1 : 0;
        score += item.SellerId.HasValue ? 1 : 0;
        score += item.Amount.HasValue ? 1 : 0;
        score += HasMeaningfulValue(item.Currency) ? 1 : 0;
        score += HasMeaningfulValue(item.Notes) ? 1 : 0;
        score += item.ActorUserId.HasValue ? 1 : 0;
        return score;
    }

    private static int CalculateSettlementAuditSignalLength(SettlementAuditApiModel item)
        => (item.AuditType?.Length ?? 0)
        + (item.ReferenceType?.Length ?? 0)
        + (item.Currency?.Length ?? 0)
        + (item.Notes?.Length ?? 0);

    private static int CalculateAuthAuditScore(AuthAuditApiModel item)
    {
        var score = 0;
        score += item.UserId.HasValue ? 1 : 0;
        score += HasMeaningfulValue(item.ClientLane) ? 1 : 0;
        score += HasMeaningfulValue(item.RoleName) ? 1 : 0;
        score += HasMeaningfulValue(item.Identifier) ? 1 : 0;
        score += HasMeaningfulValue(item.UserName) ? 1 : 0;
        score += HasMeaningfulValue(item.Email) ? 1 : 0;
        score += HasMeaningfulValue(item.EventType) ? 1 : 0;
        score += item.Success ? 1 : 0;
        score += HasMeaningfulValue(item.FailureReason) ? 1 : 0;
        score += item.FailedAttemptCount.HasValue ? 1 : 0;
        score += HasMeaningfulValue(item.IpAddress) ? 1 : 0;
        score += HasMeaningfulValue(item.ForwardedFor) ? 1 : 0;
        score += HasMeaningfulValue(item.CountryCode) ? 1 : 0;
        score += HasMeaningfulValue(item.CountryName) ? 1 : 0;
        score += HasMeaningfulValue(item.RegionName) ? 1 : 0;
        score += HasMeaningfulValue(item.CityName) ? 1 : 0;
        score += HasMeaningfulValue(item.UserAgent) ? 1 : 0;
        score += HasMeaningfulValue(item.DeviceType) ? 1 : 0;
        score += HasMeaningfulValue(item.BrowserFamily) ? 1 : 0;
        score += HasMeaningfulValue(item.OperatingSystem) ? 1 : 0;
        score += item.IsSuspicious ? 1 : 0;
        score += HasMeaningfulValue(item.SuspicionReasons) ? 1 : 0;
        return score;
    }

    private static int CalculateAuthAuditSignalLength(AuthAuditApiModel item)
        => (item.ClientLane?.Length ?? 0)
        + (item.RoleName?.Length ?? 0)
        + (item.Identifier?.Length ?? 0)
        + (item.UserName?.Length ?? 0)
        + (item.Email?.Length ?? 0)
        + (item.EventType?.Length ?? 0)
        + (item.FailureReason?.Length ?? 0)
        + (item.IpAddress?.Length ?? 0)
        + (item.ForwardedFor?.Length ?? 0)
        + (item.CountryCode?.Length ?? 0)
        + (item.CountryName?.Length ?? 0)
        + (item.RegionName?.Length ?? 0)
        + (item.CityName?.Length ?? 0)
        + (item.UserAgent?.Length ?? 0)
        + (item.DeviceType?.Length ?? 0)
        + (item.BrowserFamily?.Length ?? 0)
        + (item.OperatingSystem?.Length ?? 0)
        + (item.SuspicionReasons?.Length ?? 0);

    private static bool HasMeaningfulValue(string? value) => !string.IsNullOrWhiteSpace(value);

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
            "admin" => "Admin",
            "seller" => "Seller",
            "customer" => "Người dùng",
            "unknown" => "Không xác định",
            "success" => "Thành công",
            "failed" => "Thất bại",
            "locked" => "Bị khóa",
            "suspicious" => "Đáng ngờ",
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

    private sealed class AuthAuditApiResponse
    {
        public AuthAuditStatsApiModel? Stats { get; set; }

        public AuthAuditFiltersApiModel? Filters { get; set; }

        public List<AuthAuditApiModel>? Items { get; set; }
    }

    private sealed class AuthAuditStatsApiModel
    {
        public int Total24h { get; set; }

        public int Success24h { get; set; }

        public int Failed24h { get; set; }

        public int Locked24h { get; set; }

        public int Suspicious24h { get; set; }

        public int UniqueIp24h { get; set; }

        public int ForeignBackoffice24h { get; set; }

        public int Admin24h { get; set; }

        public int Seller24h { get; set; }

        public int Customer24h { get; set; }

        public int Unknown24h { get; set; }
    }

    private sealed class AuthAuditFiltersApiModel
    {
        public string? Q { get; set; }

        public string? Role { get; set; }

        public string? Outcome { get; set; }

        public List<AuditOptionApiModel>? RoleOptions { get; set; }

        public List<AuditOptionApiModel>? OutcomeOptions { get; set; }
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

    private sealed class AuthAuditApiModel
    {
        public int AuthAuditLogId { get; set; }

        public int? UserId { get; set; }

        public string? ClientLane { get; set; }

        public string? RoleName { get; set; }

        public string? Identifier { get; set; }

        public string? UserName { get; set; }

        public string? Email { get; set; }

        public string? EventType { get; set; }

        public bool Success { get; set; }

        public string? FailureReason { get; set; }

        public int? FailedAttemptCount { get; set; }

        public string? IpAddress { get; set; }

        public string? ForwardedFor { get; set; }

        public string? CountryCode { get; set; }

        public string? CountryName { get; set; }

        public string? RegionName { get; set; }

        public string? CityName { get; set; }

        public string? UserAgent { get; set; }

        public string? DeviceType { get; set; }

        public string? BrowserFamily { get; set; }

        public string? OperatingSystem { get; set; }

        public bool IsSuspicious { get; set; }

        public string? SuspicionReasons { get; set; }

        public DateTime OccurredAt { get; set; }
    }
}
