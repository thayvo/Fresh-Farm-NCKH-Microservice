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
public sealed class CommunicationController : LegacySellerControllerBase
{
    private const string AccessTokenSessionKey = "ACCESS_TOKEN";
    private readonly IHttpClientFactory _httpClientFactory;

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true
    };

    public CommunicationController(IHttpClientFactory httpClientFactory)
    {
        _httpClientFactory = httpClientFactory;
    }

    [HttpGet]
    public async Task<IActionResult> Index(string? q = null, string? channel = null, string? eventType = null)
    {
        var model = new CommunicationGovernancePageViewModel
        {
            Query = q?.Trim() ?? string.Empty,
            Channel = Normalize(channel),
            EventType = Normalize(eventType)
        };
        SeedFallbackOptions(model);

        try
        {
            var client = CreateOrderingClient();
            var response = await client.GetAsync(BuildEndpoint(model));
            if (!response.IsSuccessStatusCode)
            {
                ViewBag.Error = await ReadApiMessageAsync(response, "Không thể tải trung tâm quản trị truyền thông.");
                return View(model);
            }

            var payload = await response.Content.ReadFromJsonAsync<CommunicationGovernanceApiResponse>(JsonOptions);
            if (payload is null)
            {
                ViewBag.Error = "Không đọc được dữ liệu trung tâm quản trị truyền thông.";
                return View(model);
            }

            model.Query = payload.Filters?.Q ?? model.Query;
            model.Channel = payload.Filters?.Channel ?? model.Channel;
            model.EventType = payload.Filters?.EventType ?? model.EventType;
            model.Stats = new CommunicationGovernanceStatsViewModel
            {
                TotalTemplates = payload.Stats?.TotalTemplates ?? 0,
                ActiveTemplates = payload.Stats?.ActiveTemplates ?? 0,
                EnabledPolicies = payload.Stats?.EnabledPolicies ?? 0,
                OptedOutCount = payload.Stats?.OptedOutCount ?? 0,
                UpdatedPreferences24h = payload.Stats?.UpdatedPreferences24h ?? 0
            };
            model.ChannelOptions = DeduplicateOptions(payload.Filters?.ChannelOptions).Select(MapOption).ToList();
            model.EventOptions = DeduplicateOptions(payload.Filters?.EventOptions).Select(MapOption).ToList();
            model.AudienceOptions = DeduplicateOptions(payload.Filters?.AudienceOptions).Select(MapOption).ToList();
            model.DeliveryModeOptions = DeduplicateOptions(payload.Filters?.DeliveryModeOptions).Select(MapOption).ToList();
            model.SourceOptions = DeduplicateOptions(payload.Filters?.SourceOptions).Select(MapOption).ToList();
            model.TemplateOptions = DeduplicateTemplateOptions(payload.Filters?.Templates).Select(x => new CommunicationTemplateOptionViewModel
            {
                CommunicationTemplateId = x.CommunicationTemplateId,
                Text = x.Text ?? string.Empty
            }).ToList();
            model.Templates = DeduplicateTemplates(payload.Templates).Select(x => new CommunicationTemplateRowViewModel
            {
                CommunicationTemplateId = x.CommunicationTemplateId,
                TemplateName = x.TemplateName ?? string.Empty,
                EventType = x.EventType ?? string.Empty,
                Channel = x.Channel ?? string.Empty,
                Locale = x.Locale ?? string.Empty,
                Subject = x.Subject,
                Body = x.Body ?? string.Empty,
                IsActive = x.IsActive,
                Version = x.Version,
                LastEditedByUserId = x.LastEditedByUserId,
                CreatedAt = x.CreatedAt,
                UpdatedAt = x.UpdatedAt
            }).ToList();
            model.Policies = DeduplicatePolicies(payload.Policies).Select(x => new CommunicationPolicyRowViewModel
            {
                NotificationPolicyRuleId = x.NotificationPolicyRuleId,
                EventType = x.EventType ?? string.Empty,
                Channel = x.Channel ?? string.Empty,
                AudienceType = x.AudienceType ?? string.Empty,
                CommunicationTemplateId = x.CommunicationTemplateId,
                TemplateName = x.TemplateName,
                IsEnabled = x.IsEnabled,
                CooldownMinutes = x.CooldownMinutes,
                DeliveryMode = x.DeliveryMode ?? string.Empty,
                Priority = x.Priority,
                Notes = x.Notes,
                CreatedAt = x.CreatedAt,
                UpdatedAt = x.UpdatedAt
            }).ToList();
            model.Preferences = DeduplicatePreferences(payload.Preferences).Select(x => new CommunicationPreferenceRowViewModel
            {
                NotificationPreferenceId = x.NotificationPreferenceId,
                UserId = x.UserId,
                EventType = x.EventType ?? string.Empty,
                Channel = x.Channel ?? string.Empty,
                IsOptedIn = x.IsOptedIn,
                Source = x.Source ?? string.Empty,
                CreatedAt = x.CreatedAt,
                UpdatedAt = x.UpdatedAt
            }).ToList();
        }
        catch (Exception ex)
        {
            ViewBag.Error = "Lỗi khi tải trung tâm quản trị truyền thông: " + ex.Message;
        }

        return View(model);
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> SaveTemplate(CommunicationTemplateEditorInput input, string? q = null, string? channel = null, string? eventType = null)
    {
        await PostToOrderingAsync("/api/orders/admin/communications/templates", input, "Đã lưu mẫu thông báo.", "Không thể lưu mẫu thông báo.");
        return RedirectToAction(nameof(Index), new { q, channel = Normalize(channel), eventType = Normalize(eventType) });
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> ToggleTemplate(int id, string? q = null, string? channel = null, string? eventType = null)
    {
        await PostToOrderingAsync($"/api/orders/admin/communications/templates/{id}/toggle", payload: null, "Đã cập nhật trạng thái mẫu thông báo.", "Không thể cập nhật mẫu thông báo.");
        return RedirectToAction(nameof(Index), new { q, channel = Normalize(channel), eventType = Normalize(eventType) });
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> SavePolicy(CommunicationPolicyEditorInput input, string? q = null, string? channel = null, string? eventType = null)
    {
        await PostToOrderingAsync("/api/orders/admin/communications/policies", input, "Đã lưu chính sách thông báo.", "Không thể lưu chính sách thông báo.");
        return RedirectToAction(nameof(Index), new { q, channel = Normalize(channel), eventType = Normalize(eventType) });
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> TogglePolicy(int id, string? q = null, string? channel = null, string? eventType = null)
    {
        await PostToOrderingAsync($"/api/orders/admin/communications/policies/{id}/toggle", payload: null, "Đã cập nhật trạng thái chính sách.", "Không thể cập nhật chính sách.");
        return RedirectToAction(nameof(Index), new { q, channel = Normalize(channel), eventType = Normalize(eventType) });
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> SavePreference(CommunicationPreferenceEditorInput input, string? q = null, string? channel = null, string? eventType = null)
    {
        await PostToOrderingAsync("/api/orders/admin/communications/preferences", input, "Đã lưu tùy chọn nhận thông báo.", "Không thể lưu tùy chọn nhận thông báo.");
        return RedirectToAction(nameof(Index), new { q, channel = Normalize(channel), eventType = Normalize(eventType) });
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

    private static string BuildEndpoint(CommunicationGovernancePageViewModel model)
    {
        var parts = new List<string>();
        if (!string.IsNullOrWhiteSpace(model.Query))
        {
            parts.Add("q=" + Uri.EscapeDataString(model.Query));
        }

        if (!string.IsNullOrWhiteSpace(model.Channel))
        {
            parts.Add("channel=" + Uri.EscapeDataString(model.Channel));
        }

        if (!string.IsNullOrWhiteSpace(model.EventType))
        {
            parts.Add("eventType=" + Uri.EscapeDataString(model.EventType));
        }

        return "/api/orders/admin/communications/center" + (parts.Count == 0 ? string.Empty : "?" + string.Join("&", parts));
    }

    private async Task PostToOrderingAsync(string endpoint, object? payload, string successMessage, string errorMessage)
    {
        try
        {
            var client = CreateOrderingClient();
            var response = payload is null
                ? await client.PostAsync(endpoint, null)
                : await client.PostAsJsonAsync(endpoint, payload);

            TempData[response.IsSuccessStatusCode ? "SuccessMessage" : "ErrorMessage"] =
                response.IsSuccessStatusCode
                    ? await ReadApiMessageAsync(response, successMessage)
                    : await ReadApiMessageAsync(response, errorMessage);
        }
        catch (Exception ex)
        {
            TempData["ErrorMessage"] = errorMessage + " " + ex.Message;
        }
    }

    private static CommunicationOptionViewModel MapOption(CommunicationOptionApiModel option)
        => new()
        {
            Value = option.Value ?? string.Empty,
            Text = option.Text ?? string.Empty
        };

    private static List<CommunicationOptionApiModel> DeduplicateOptions(IEnumerable<CommunicationOptionApiModel>? options)
    {
        return options?
            .Where(option => HasMeaningfulValue(option.Value))
            .GroupBy(option => option.Value!, StringComparer.OrdinalIgnoreCase)
            .Select(group => group
                .OrderByDescending(option => HasMeaningfulValue(option.Text))
                .ThenByDescending(CalculateOptionSignalLength)
                .First())
            .ToList() ?? new List<CommunicationOptionApiModel>();
    }

    private static List<CommunicationTemplateOptionApiModel> DeduplicateTemplateOptions(IEnumerable<CommunicationTemplateOptionApiModel>? options)
    {
        return options?
            .Where(option => option.CommunicationTemplateId > 0)
            .GroupBy(option => option.CommunicationTemplateId)
            .Select(group => group
                .OrderByDescending(option => HasMeaningfulValue(option.Text))
                .ThenByDescending(CalculateTemplateOptionSignalLength)
                .First())
            .ToList() ?? new List<CommunicationTemplateOptionApiModel>();
    }

    private static List<CommunicationTemplateApiModel> DeduplicateTemplates(IEnumerable<CommunicationTemplateApiModel>? templates)
    {
        return templates?
            .Where(template => template.CommunicationTemplateId > 0)
            .GroupBy(template => template.CommunicationTemplateId)
            .Select(group => group
                .OrderByDescending(CalculateTemplateScore)
                .ThenByDescending(CalculateTemplateSignalLength)
                .ThenByDescending(template => template.UpdatedAt ?? template.CreatedAt)
                .First())
            .ToList() ?? new List<CommunicationTemplateApiModel>();
    }

    private static List<CommunicationPolicyApiModel> DeduplicatePolicies(IEnumerable<CommunicationPolicyApiModel>? policies)
    {
        return policies?
            .Where(policy => policy.NotificationPolicyRuleId > 0)
            .GroupBy(policy => policy.NotificationPolicyRuleId)
            .Select(group => group
                .OrderByDescending(CalculatePolicyScore)
                .ThenByDescending(CalculatePolicySignalLength)
                .ThenByDescending(policy => policy.UpdatedAt ?? policy.CreatedAt)
                .First())
            .ToList() ?? new List<CommunicationPolicyApiModel>();
    }

    private static List<CommunicationPreferenceApiModel> DeduplicatePreferences(IEnumerable<CommunicationPreferenceApiModel>? preferences)
    {
        return preferences?
            .Where(preference => preference.NotificationPreferenceId > 0)
            .GroupBy(preference => preference.NotificationPreferenceId)
            .Select(group => group
                .OrderByDescending(CalculatePreferenceScore)
                .ThenByDescending(CalculatePreferenceSignalLength)
                .ThenByDescending(preference => preference.UpdatedAt)
                .First())
            .ToList() ?? new List<CommunicationPreferenceApiModel>();
    }

    private static int CalculateOptionSignalLength(CommunicationOptionApiModel option)
        => (option.Value?.Length ?? 0) + (option.Text?.Length ?? 0);

    private static int CalculateTemplateOptionSignalLength(CommunicationTemplateOptionApiModel option)
        => option.Text?.Length ?? 0;

    private static int CalculateTemplateScore(CommunicationTemplateApiModel template)
    {
        var score = 0;
        score += HasMeaningfulValue(template.TemplateName) ? 2 : 0;
        score += HasMeaningfulValue(template.EventType) ? 1 : 0;
        score += HasMeaningfulValue(template.Channel) ? 1 : 0;
        score += HasMeaningfulValue(template.Locale) ? 1 : 0;
        score += HasMeaningfulValue(template.Subject) ? 1 : 0;
        score += HasMeaningfulValue(template.Body) ? 2 : 0;
        score += template.IsActive ? 1 : 0;
        score += template.Version > 0 ? 1 : 0;
        score += template.LastEditedByUserId.HasValue ? 1 : 0;
        return score;
    }

    private static int CalculateTemplateSignalLength(CommunicationTemplateApiModel template)
        => (template.TemplateName?.Length ?? 0)
        + (template.EventType?.Length ?? 0)
        + (template.Channel?.Length ?? 0)
        + (template.Locale?.Length ?? 0)
        + (template.Subject?.Length ?? 0)
        + (template.Body?.Length ?? 0);

    private static int CalculatePolicyScore(CommunicationPolicyApiModel policy)
    {
        var score = 0;
        score += HasMeaningfulValue(policy.EventType) ? 1 : 0;
        score += HasMeaningfulValue(policy.Channel) ? 1 : 0;
        score += HasMeaningfulValue(policy.AudienceType) ? 1 : 0;
        score += policy.CommunicationTemplateId.HasValue ? 1 : 0;
        score += HasMeaningfulValue(policy.TemplateName) ? 1 : 0;
        score += policy.IsEnabled ? 1 : 0;
        score += policy.CooldownMinutes > 0 ? 1 : 0;
        score += HasMeaningfulValue(policy.DeliveryMode) ? 1 : 0;
        score += policy.Priority > 0 ? 1 : 0;
        score += HasMeaningfulValue(policy.Notes) ? 1 : 0;
        return score;
    }

    private static int CalculatePolicySignalLength(CommunicationPolicyApiModel policy)
        => (policy.EventType?.Length ?? 0)
        + (policy.Channel?.Length ?? 0)
        + (policy.AudienceType?.Length ?? 0)
        + (policy.TemplateName?.Length ?? 0)
        + (policy.DeliveryMode?.Length ?? 0)
        + (policy.Notes?.Length ?? 0);

    private static int CalculatePreferenceScore(CommunicationPreferenceApiModel preference)
    {
        var score = 0;
        score += preference.UserId > 0 ? 1 : 0;
        score += HasMeaningfulValue(preference.EventType) ? 1 : 0;
        score += HasMeaningfulValue(preference.Channel) ? 1 : 0;
        score += preference.IsOptedIn ? 1 : 0;
        score += HasMeaningfulValue(preference.Source) ? 1 : 0;
        return score;
    }

    private static int CalculatePreferenceSignalLength(CommunicationPreferenceApiModel preference)
        => (preference.EventType?.Length ?? 0)
        + (preference.Channel?.Length ?? 0)
        + (preference.Source?.Length ?? 0);

    private static bool HasMeaningfulValue(string? value) => !string.IsNullOrWhiteSpace(value);

    private static string Normalize(string? value)
        => string.IsNullOrWhiteSpace(value) ? "all" : value.Trim().ToLowerInvariant();

    private static void SeedFallbackOptions(CommunicationGovernancePageViewModel model)
    {
        model.ChannelOptions =
        [
            new CommunicationOptionViewModel { Value = "all", Text = "Tất cả" },
            new CommunicationOptionViewModel { Value = "in_app", Text = "In-app" },
            new CommunicationOptionViewModel { Value = "push", Text = "Push" },
            new CommunicationOptionViewModel { Value = "email", Text = "Email" },
            new CommunicationOptionViewModel { Value = "sms", Text = "SMS" }
        ];
        model.EventOptions =
        [
            new CommunicationOptionViewModel { Value = "all", Text = "Tất cả" },
            new CommunicationOptionViewModel { Value = "order_created", Text = "Đơn hàng được tạo" },
            new CommunicationOptionViewModel { Value = "order_paid", Text = "Đơn hàng đã thanh toán" },
            new CommunicationOptionViewModel { Value = "order_shipped", Text = "Đơn hàng đã giao vận" },
            new CommunicationOptionViewModel { Value = "fresh_recall", Text = "Thu hồi hàng tươi" }
        ];
        model.AudienceOptions =
        [
            new CommunicationOptionViewModel { Value = "buyer", Text = "Người mua" },
            new CommunicationOptionViewModel { Value = "seller", Text = "Người bán" },
            new CommunicationOptionViewModel { Value = "admin", Text = "Admin" },
            new CommunicationOptionViewModel { Value = "all", Text = "Tất cả" }
        ];
        model.DeliveryModeOptions =
        [
            new CommunicationOptionViewModel { Value = "immediate", Text = "Gửi ngay" },
            new CommunicationOptionViewModel { Value = "batched", Text = "Gửi theo lô" },
            new CommunicationOptionViewModel { Value = "manual", Text = "Thủ công" }
        ];
        model.SourceOptions =
        [
            new CommunicationOptionViewModel { Value = "admin_console", Text = "Bảng điều khiển admin" },
            new CommunicationOptionViewModel { Value = "import", Text = "Nhập liệu" },
            new CommunicationOptionViewModel { Value = "customer_service", Text = "Chăm sóc khách hàng" }
        ];
    }

    private static async Task<string> ReadApiMessageAsync(HttpResponseMessage response, string fallback)
    {
        var body = await response.Content.ReadAsStringAsync();
        if (string.IsNullOrWhiteSpace(body))
        {
            return fallback;
        }

        try
        {
            using var doc = JsonDocument.Parse(body);
            var root = doc.RootElement;
            if (root.TryGetProperty("message", out var message) && message.ValueKind == JsonValueKind.String)
            {
                return message.GetString() ?? fallback;
            }
        }
        catch
        {
        }

        return fallback;
    }

    private sealed class CommunicationGovernanceApiResponse
    {
        public CommunicationGovernanceStatsApiModel? Stats { get; set; }
        public CommunicationGovernanceFiltersApiModel? Filters { get; set; }
        public List<CommunicationTemplateApiModel>? Templates { get; set; }
        public List<CommunicationPolicyApiModel>? Policies { get; set; }
        public List<CommunicationPreferenceApiModel>? Preferences { get; set; }
    }

    private sealed class CommunicationGovernanceStatsApiModel
    {
        public int TotalTemplates { get; set; }
        public int ActiveTemplates { get; set; }
        public int EnabledPolicies { get; set; }
        public int OptedOutCount { get; set; }
        public int UpdatedPreferences24h { get; set; }
    }

    private sealed class CommunicationGovernanceFiltersApiModel
    {
        public string? Q { get; set; }
        public string? Channel { get; set; }
        public string? EventType { get; set; }
        public List<CommunicationOptionApiModel>? ChannelOptions { get; set; }
        public List<CommunicationOptionApiModel>? EventOptions { get; set; }
        public List<CommunicationOptionApiModel>? AudienceOptions { get; set; }
        public List<CommunicationOptionApiModel>? DeliveryModeOptions { get; set; }
        public List<CommunicationOptionApiModel>? SourceOptions { get; set; }
        public List<CommunicationTemplateOptionApiModel>? Templates { get; set; }
    }

    private sealed class CommunicationOptionApiModel
    {
        public string? Value { get; set; }
        public string? Text { get; set; }
    }

    private sealed class CommunicationTemplateOptionApiModel
    {
        public int CommunicationTemplateId { get; set; }
        public string? Text { get; set; }
    }

    private sealed class CommunicationTemplateApiModel
    {
        public int CommunicationTemplateId { get; set; }
        public string? TemplateName { get; set; }
        public string? EventType { get; set; }
        public string? Channel { get; set; }
        public string? Locale { get; set; }
        public string? Subject { get; set; }
        public string? Body { get; set; }
        public bool IsActive { get; set; }
        public int Version { get; set; }
        public int? LastEditedByUserId { get; set; }
        public DateTime CreatedAt { get; set; }
        public DateTime? UpdatedAt { get; set; }
    }

    private sealed class CommunicationPolicyApiModel
    {
        public int NotificationPolicyRuleId { get; set; }
        public string? EventType { get; set; }
        public string? Channel { get; set; }
        public string? AudienceType { get; set; }
        public int? CommunicationTemplateId { get; set; }
        public string? TemplateName { get; set; }
        public bool IsEnabled { get; set; }
        public int CooldownMinutes { get; set; }
        public string? DeliveryMode { get; set; }
        public int Priority { get; set; }
        public string? Notes { get; set; }
        public DateTime CreatedAt { get; set; }
        public DateTime? UpdatedAt { get; set; }
    }

    private sealed class CommunicationPreferenceApiModel
    {
        public int NotificationPreferenceId { get; set; }
        public int UserId { get; set; }
        public string? EventType { get; set; }
        public string? Channel { get; set; }
        public bool IsOptedIn { get; set; }
        public string? Source { get; set; }
        public DateTime CreatedAt { get; set; }
        public DateTime UpdatedAt { get; set; }
    }
}
