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
                ViewBag.Error = await ReadApiMessageAsync(response, "Khong the tai communications governance.");
                return View(model);
            }

            var payload = await response.Content.ReadFromJsonAsync<CommunicationGovernanceApiResponse>(JsonOptions);
            if (payload is null)
            {
                ViewBag.Error = "Khong doc duoc du lieu communications governance.";
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
            model.ChannelOptions = payload.Filters?.ChannelOptions?.Select(MapOption).ToList() ?? model.ChannelOptions;
            model.EventOptions = payload.Filters?.EventOptions?.Select(MapOption).ToList() ?? model.EventOptions;
            model.AudienceOptions = payload.Filters?.AudienceOptions?.Select(MapOption).ToList() ?? model.AudienceOptions;
            model.DeliveryModeOptions = payload.Filters?.DeliveryModeOptions?.Select(MapOption).ToList() ?? model.DeliveryModeOptions;
            model.SourceOptions = payload.Filters?.SourceOptions?.Select(MapOption).ToList() ?? model.SourceOptions;
            model.TemplateOptions = payload.Filters?.Templates?.Select(x => new CommunicationTemplateOptionViewModel
            {
                CommunicationTemplateId = x.CommunicationTemplateId,
                Text = x.Text ?? string.Empty
            }).ToList() ?? new List<CommunicationTemplateOptionViewModel>();
            model.Templates = payload.Templates?.Select(x => new CommunicationTemplateRowViewModel
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
            }).ToList() ?? new List<CommunicationTemplateRowViewModel>();
            model.Policies = payload.Policies?.Select(x => new CommunicationPolicyRowViewModel
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
            }).ToList() ?? new List<CommunicationPolicyRowViewModel>();
            model.Preferences = payload.Preferences?.Select(x => new CommunicationPreferenceRowViewModel
            {
                NotificationPreferenceId = x.NotificationPreferenceId,
                UserId = x.UserId,
                EventType = x.EventType ?? string.Empty,
                Channel = x.Channel ?? string.Empty,
                IsOptedIn = x.IsOptedIn,
                Source = x.Source ?? string.Empty,
                CreatedAt = x.CreatedAt,
                UpdatedAt = x.UpdatedAt
            }).ToList() ?? new List<CommunicationPreferenceRowViewModel>();
        }
        catch (Exception ex)
        {
            ViewBag.Error = "Loi khi tai communications governance: " + ex.Message;
        }

        return View(model);
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> SaveTemplate(CommunicationTemplateEditorInput input, string? q = null, string? channel = null, string? eventType = null)
    {
        await PostToOrderingAsync("/api/orders/admin/communications/templates", input, "Da luu template thong bao.", "Khong the luu template thong bao.");
        return RedirectToAction(nameof(Index), new { q, channel = Normalize(channel), eventType = Normalize(eventType) });
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> ToggleTemplate(int id, string? q = null, string? channel = null, string? eventType = null)
    {
        await PostToOrderingAsync($"/api/orders/admin/communications/templates/{id}/toggle", payload: null, "Da cap nhat trang thai template.", "Khong the cap nhat template.");
        return RedirectToAction(nameof(Index), new { q, channel = Normalize(channel), eventType = Normalize(eventType) });
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> SavePolicy(CommunicationPolicyEditorInput input, string? q = null, string? channel = null, string? eventType = null)
    {
        await PostToOrderingAsync("/api/orders/admin/communications/policies", input, "Da luu notification policy.", "Khong the luu notification policy.");
        return RedirectToAction(nameof(Index), new { q, channel = Normalize(channel), eventType = Normalize(eventType) });
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> TogglePolicy(int id, string? q = null, string? channel = null, string? eventType = null)
    {
        await PostToOrderingAsync($"/api/orders/admin/communications/policies/{id}/toggle", payload: null, "Da cap nhat trang thai policy.", "Khong the cap nhat policy.");
        return RedirectToAction(nameof(Index), new { q, channel = Normalize(channel), eventType = Normalize(eventType) });
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> SavePreference(CommunicationPreferenceEditorInput input, string? q = null, string? channel = null, string? eventType = null)
    {
        await PostToOrderingAsync("/api/orders/admin/communications/preferences", input, "Da luu preference opt-in/out.", "Khong the luu preference opt-in/out.");
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

    private static string Normalize(string? value)
        => string.IsNullOrWhiteSpace(value) ? "all" : value.Trim().ToLowerInvariant();

    private static void SeedFallbackOptions(CommunicationGovernancePageViewModel model)
    {
        model.ChannelOptions =
        [
            new CommunicationOptionViewModel { Value = "all", Text = "Tat ca" },
            new CommunicationOptionViewModel { Value = "in_app", Text = "In-app" },
            new CommunicationOptionViewModel { Value = "push", Text = "Push" },
            new CommunicationOptionViewModel { Value = "email", Text = "Email" },
            new CommunicationOptionViewModel { Value = "sms", Text = "SMS" }
        ];
        model.EventOptions =
        [
            new CommunicationOptionViewModel { Value = "all", Text = "Tat ca" },
            new CommunicationOptionViewModel { Value = "order_created", Text = "Order created" },
            new CommunicationOptionViewModel { Value = "order_paid", Text = "Order paid" },
            new CommunicationOptionViewModel { Value = "order_shipped", Text = "Order shipped" },
            new CommunicationOptionViewModel { Value = "fresh_recall", Text = "Fresh recall" }
        ];
        model.AudienceOptions =
        [
            new CommunicationOptionViewModel { Value = "buyer", Text = "Buyer" },
            new CommunicationOptionViewModel { Value = "seller", Text = "Seller" },
            new CommunicationOptionViewModel { Value = "admin", Text = "Admin" },
            new CommunicationOptionViewModel { Value = "all", Text = "Tat ca" }
        ];
        model.DeliveryModeOptions =
        [
            new CommunicationOptionViewModel { Value = "immediate", Text = "Immediate" },
            new CommunicationOptionViewModel { Value = "batched", Text = "Batched" },
            new CommunicationOptionViewModel { Value = "manual", Text = "Manual" }
        ];
        model.SourceOptions =
        [
            new CommunicationOptionViewModel { Value = "admin_console", Text = "Admin console" },
            new CommunicationOptionViewModel { Value = "import", Text = "Import" },
            new CommunicationOptionViewModel { Value = "customer_service", Text = "Customer service" }
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
