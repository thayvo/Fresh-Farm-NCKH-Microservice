using System.Security.Claims;
using FreshFarm.Ordering.Api.Models;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace FreshFarm.Ordering.Api.Controllers;

[ApiController]
[Route("api/orders/admin/communications")]
[Authorize(Policy = "AdminOnly")]
public sealed class CommunicationsAdminController : ControllerBase
{
    private static readonly string[] AllowedChannels = ["all", "in_app", "push", "email", "sms"];

    private static readonly string[] KnownEvents =
    [
        "all",
        "order_created",
        "order_paid",
        "order_shipped",
        "order_delivered",
        "refund_opened",
        "return_opened",
        "voucher_drop",
        "campaign_live",
        "risk_alert",
        "fresh_recall"
    ];

    private static readonly string[] AllowedAudiences = ["buyer", "seller", "admin", "all"];
    private static readonly string[] AllowedDeliveryModes = ["immediate", "batched", "manual"];
    private static readonly string[] AllowedSources = ["admin_console", "import", "customer_service"];

    private readonly FreshFarmOrderingDBContext _db;

    public CommunicationsAdminController(FreshFarmOrderingDBContext db)
    {
        _db = db;
    }

    [HttpGet("center")]
    public async Task<IActionResult> GetCenter(
        [FromQuery] string? q = null,
        [FromQuery] string? channel = null,
        [FromQuery] string? eventType = null,
        CancellationToken cancellationToken = default)
    {
        var normalizedQuery = q?.Trim();
        var normalizedChannel = Normalize(channel, AllowedChannels);
        var discoveredEvents = await LoadEventOptionsAsync(cancellationToken);
        var normalizedEvent = Normalize(eventType, discoveredEvents);

        var templates = _db.CommunicationTemplates.AsNoTracking().AsQueryable();
        var policies = _db.NotificationPolicyRules.AsNoTracking().Include(x => x.CommunicationTemplate).AsQueryable();
        var preferences = _db.NotificationPreferences.AsNoTracking().AsQueryable();

        if (!string.IsNullOrWhiteSpace(normalizedQuery))
        {
            templates = templates.Where(x =>
                x.TemplateName.Contains(normalizedQuery) ||
                x.EventType.Contains(normalizedQuery) ||
                x.Channel.Contains(normalizedQuery) ||
                (x.Subject != null && x.Subject.Contains(normalizedQuery)) ||
                x.Body.Contains(normalizedQuery));

            policies = policies.Where(x =>
                x.EventType.Contains(normalizedQuery) ||
                x.Channel.Contains(normalizedQuery) ||
                x.AudienceType.Contains(normalizedQuery) ||
                (x.Notes != null && x.Notes.Contains(normalizedQuery)));

            if (int.TryParse(normalizedQuery, out var parsedUserId) && parsedUserId > 0)
            {
                preferences = preferences.Where(x => x.UserId == parsedUserId || x.EventType.Contains(normalizedQuery) || x.Channel.Contains(normalizedQuery));
            }
            else
            {
                preferences = preferences.Where(x => x.EventType.Contains(normalizedQuery) || x.Channel.Contains(normalizedQuery) || x.Source.Contains(normalizedQuery));
            }
        }

        if (normalizedChannel != "all")
        {
            templates = templates.Where(x => x.Channel == normalizedChannel);
            policies = policies.Where(x => x.Channel == normalizedChannel);
            preferences = preferences.Where(x => x.Channel == normalizedChannel);
        }

        if (normalizedEvent != "all")
        {
            templates = templates.Where(x => x.EventType == normalizedEvent);
            policies = policies.Where(x => x.EventType == normalizedEvent);
            preferences = preferences.Where(x => x.EventType == normalizedEvent);
        }

        var since24h = DateTime.UtcNow.AddHours(-24);
        var totalTemplates = await _db.CommunicationTemplates.AsNoTracking().CountAsync(cancellationToken);
        var activeTemplates = await _db.CommunicationTemplates.AsNoTracking().CountAsync(x => x.IsActive, cancellationToken);
        var enabledPolicies = await _db.NotificationPolicyRules.AsNoTracking().CountAsync(x => x.IsEnabled, cancellationToken);
        var optedOutCount = await _db.NotificationPreferences.AsNoTracking().CountAsync(x => !x.IsOptedIn, cancellationToken);
        var updatedPreferences24h = await _db.NotificationPreferences.AsNoTracking().CountAsync(x => x.UpdatedAt >= since24h, cancellationToken);

        var templateRows = await templates
            .OrderByDescending(x => x.UpdatedAt ?? x.CreatedAt)
            .ThenByDescending(x => x.CommunicationTemplateId)
            .Take(12)
            .Select(x => new
            {
                communicationTemplateId = x.CommunicationTemplateId,
                templateName = x.TemplateName,
                eventType = x.EventType,
                channel = x.Channel,
                locale = x.Locale,
                subject = x.Subject,
                body = x.Body,
                isActive = x.IsActive,
                version = x.Version,
                lastEditedByUserId = x.LastEditedByUserId,
                createdAt = x.CreatedAt,
                updatedAt = x.UpdatedAt
            })
            .ToListAsync(cancellationToken);

        var policyRows = await policies
            .OrderByDescending(x => x.IsEnabled)
            .ThenByDescending(x => x.UpdatedAt ?? x.CreatedAt)
            .ThenByDescending(x => x.NotificationPolicyRuleId)
            .Take(20)
            .Select(x => new
            {
                notificationPolicyRuleId = x.NotificationPolicyRuleId,
                eventType = x.EventType,
                channel = x.Channel,
                audienceType = x.AudienceType,
                communicationTemplateId = x.CommunicationTemplateId,
                templateName = x.CommunicationTemplate != null ? x.CommunicationTemplate.TemplateName : null,
                isEnabled = x.IsEnabled,
                cooldownMinutes = x.CooldownMinutes,
                deliveryMode = x.DeliveryMode,
                priority = x.Priority,
                notes = x.Notes,
                createdAt = x.CreatedAt,
                updatedAt = x.UpdatedAt
            })
            .ToListAsync(cancellationToken);

        var preferenceRows = await preferences
            .OrderBy(x => x.IsOptedIn)
            .ThenByDescending(x => x.UpdatedAt)
            .ThenByDescending(x => x.NotificationPreferenceId)
            .Take(20)
            .Select(x => new
            {
                notificationPreferenceId = x.NotificationPreferenceId,
                userId = x.UserId,
                eventType = x.EventType,
                channel = x.Channel,
                isOptedIn = x.IsOptedIn,
                source = x.Source,
                createdAt = x.CreatedAt,
                updatedAt = x.UpdatedAt
            })
            .ToListAsync(cancellationToken);

        var templateOptions = await _db.CommunicationTemplates.AsNoTracking()
            .OrderBy(x => x.TemplateName)
            .Select(x => new
            {
                communicationTemplateId = x.CommunicationTemplateId,
                text = $"{x.TemplateName} ({x.Channel}/{x.EventType})"
            })
            .ToListAsync(cancellationToken);

        return Ok(new
        {
            stats = new
            {
                totalTemplates,
                activeTemplates,
                enabledPolicies,
                optedOutCount,
                updatedPreferences24h
            },
            filters = new
            {
                q = normalizedQuery ?? string.Empty,
                channel = normalizedChannel,
                eventType = normalizedEvent,
                channelOptions = BuildOptions(AllowedChannels),
                eventOptions = BuildOptions(discoveredEvents),
                audienceOptions = BuildOptions(AllowedAudiences),
                deliveryModeOptions = BuildOptions(AllowedDeliveryModes),
                sourceOptions = BuildOptions(AllowedSources),
                templates = templateOptions
            },
            templates = templateRows,
            policies = policyRows,
            preferences = preferenceRows
        });
    }

    [HttpPost("templates")]
    public async Task<IActionResult> SaveTemplate([FromBody] SaveCommunicationTemplateRequest? request, CancellationToken cancellationToken = default)
    {
        if (request is null)
        {
            return BadRequest(new { success = false, message = "Payload khong hop le." });
        }

        var templateName = request.TemplateName?.Trim();
        var eventType = NormalizeEvent(request.EventType);
        var channel = Normalize(request.Channel, AllowedChannels.Where(x => x != "all"));
        var locale = string.IsNullOrWhiteSpace(request.Locale) ? "vi-VN" : request.Locale.Trim();
        var subject = TrimOrNull(request.Subject, 200);
        var body = request.Body?.Trim();
        if (string.IsNullOrWhiteSpace(templateName) || string.IsNullOrWhiteSpace(body))
        {
            return BadRequest(new { success = false, message = "Ten template va noi dung la bat buoc." });
        }

        if (channel == "all")
        {
            return BadRequest(new { success = false, message = "Channel khong hop le." });
        }

        var actorUserId = GetActorUserId();
        var existing = request.CommunicationTemplateId > 0
            ? await _db.CommunicationTemplates.FirstOrDefaultAsync(x => x.CommunicationTemplateId == request.CommunicationTemplateId, cancellationToken)
            : null;

        var existingTemplateId = existing?.CommunicationTemplateId ?? 0;
        var duplicate = await _db.CommunicationTemplates
            .FirstOrDefaultAsync(x =>
                x.CommunicationTemplateId != existingTemplateId &&
                x.TemplateName == templateName &&
                x.EventType == eventType &&
                x.Channel == channel &&
                x.Locale == locale,
                cancellationToken);
        if (duplicate is not null)
        {
            return Conflict(new { success = false, message = "Template trung event/channel/locale/ten da ton tai." });
        }

        CommunicationTemplate persistedTemplate;
        if (existing is null)
        {
            persistedTemplate = new CommunicationTemplate
            {
                TemplateName = templateName,
                EventType = eventType,
                Channel = channel,
                Locale = locale,
                Subject = subject,
                Body = body,
                IsActive = request.IsActive,
                Version = 1,
                LastEditedByUserId = actorUserId,
                CreatedAt = DateTime.UtcNow
            };
            _db.CommunicationTemplates.Add(persistedTemplate);
        }
        else
        {
            persistedTemplate = existing;
            var current = persistedTemplate;
            current.TemplateName = templateName;
            current.EventType = eventType;
            current.Channel = channel;
            current.Locale = locale;
            current.Subject = subject;
            current.Body = body;
            current.IsActive = request.IsActive;
            current.Version = Math.Max(1, current.Version) + 1;
            current.LastEditedByUserId = actorUserId;
            current.UpdatedAt = DateTime.UtcNow;
        }
        var isCreate = existing is null;

        AdminAuditLogger.AddAction(
            _db,
            "communications_governance",
            isCreate ? "create_template" : "update_template",
            "communication_template",
            persistedTemplate.CommunicationTemplateId > 0 ? persistedTemplate.CommunicationTemplateId : null,
            $"{(isCreate ? "Tao" : "Cap nhat")} template {templateName} cho {eventType}/{channel}.",
            actorUserId,
            new { templateName, eventType, channel, locale, request.IsActive });

        await _db.SaveChangesAsync(cancellationToken);
        return Ok(new { success = true, message = isCreate ? "Da tao template thong bao." : "Da cap nhat template thong bao." });
    }

    [HttpPost("templates/{id:int}/toggle")]
    public async Task<IActionResult> ToggleTemplate([FromRoute] int id, CancellationToken cancellationToken = default)
    {
        var template = await _db.CommunicationTemplates.FirstOrDefaultAsync(x => x.CommunicationTemplateId == id, cancellationToken);
        if (template is null)
        {
            return NotFound(new { success = false, message = "Khong tim thay template." });
        }

        template.IsActive = !template.IsActive;
        template.Version = Math.Max(1, template.Version) + 1;
        template.LastEditedByUserId = GetActorUserId();
        template.UpdatedAt = DateTime.UtcNow;

        AdminAuditLogger.AddAction(
            _db,
            "communications_governance",
            template.IsActive ? "activate_template" : "deactivate_template",
            "communication_template",
            template.CommunicationTemplateId,
            $"{(template.IsActive ? "Bat" : "Tat")} template {template.TemplateName}.",
            template.LastEditedByUserId,
            new { template.TemplateName, template.EventType, template.Channel, template.IsActive });

        await _db.SaveChangesAsync(cancellationToken);
        return Ok(new { success = true, message = template.IsActive ? "Da kich hoat template." : "Da tam dung template." });
    }

    [HttpPost("policies")]
    public async Task<IActionResult> SavePolicy([FromBody] SaveNotificationPolicyRequest? request, CancellationToken cancellationToken = default)
    {
        if (request is null)
        {
            return BadRequest(new { success = false, message = "Payload khong hop le." });
        }

        var eventType = NormalizeEvent(request.EventType);
        var channel = Normalize(request.Channel, AllowedChannels.Where(x => x != "all"));
        var audienceType = Normalize(request.AudienceType, AllowedAudiences);
        var deliveryMode = Normalize(request.DeliveryMode, AllowedDeliveryModes);
        var cooldownMinutes = Math.Clamp(request.CooldownMinutes, 0, 10080);
        var priority = Math.Clamp(request.Priority, 0, 100);

        if (channel == "all")
        {
            return BadRequest(new { success = false, message = "Channel khong hop le." });
        }

        if (request.CommunicationTemplateId.HasValue && request.CommunicationTemplateId.Value > 0)
        {
            var templateExists = await _db.CommunicationTemplates.AsNoTracking()
                .AnyAsync(x => x.CommunicationTemplateId == request.CommunicationTemplateId.Value, cancellationToken);
            if (!templateExists)
            {
                return BadRequest(new { success = false, message = "Template lien ket khong ton tai." });
            }
        }

        var existing = request.NotificationPolicyRuleId > 0
            ? await _db.NotificationPolicyRules.FirstOrDefaultAsync(x => x.NotificationPolicyRuleId == request.NotificationPolicyRuleId, cancellationToken)
            : null;

        var existingPolicyId = existing?.NotificationPolicyRuleId ?? 0;
        var duplicate = await _db.NotificationPolicyRules.FirstOrDefaultAsync(x =>
            x.NotificationPolicyRuleId != existingPolicyId &&
            x.EventType == eventType &&
            x.Channel == channel &&
            x.AudienceType == audienceType,
            cancellationToken);
        if (duplicate is not null)
        {
            return Conflict(new { success = false, message = "Policy trung event/channel/audience da ton tai." });
        }

        var actorUserId = GetActorUserId();
        NotificationPolicyRule persistedPolicy;
        if (existing is null)
        {
            persistedPolicy = new NotificationPolicyRule
            {
                EventType = eventType,
                Channel = channel,
                AudienceType = audienceType,
                CommunicationTemplateId = request.CommunicationTemplateId > 0 ? request.CommunicationTemplateId : null,
                IsEnabled = request.IsEnabled,
                CooldownMinutes = cooldownMinutes,
                DeliveryMode = deliveryMode,
                Priority = priority,
                Notes = TrimOrNull(request.Notes, 1000),
                CreatedAt = DateTime.UtcNow
            };
            _db.NotificationPolicyRules.Add(persistedPolicy);
        }
        else
        {
            persistedPolicy = existing;
            var current = persistedPolicy;
            current.EventType = eventType;
            current.Channel = channel;
            current.AudienceType = audienceType;
            current.CommunicationTemplateId = request.CommunicationTemplateId > 0 ? request.CommunicationTemplateId : null;
            current.IsEnabled = request.IsEnabled;
            current.CooldownMinutes = cooldownMinutes;
            current.DeliveryMode = deliveryMode;
            current.Priority = priority;
            current.Notes = TrimOrNull(request.Notes, 1000);
            current.UpdatedAt = DateTime.UtcNow;
        }
        var isCreate = existing is null;

        AdminAuditLogger.AddAction(
            _db,
            "communications_governance",
            isCreate ? "create_policy" : "update_policy",
            "notification_policy",
            persistedPolicy.NotificationPolicyRuleId > 0 ? persistedPolicy.NotificationPolicyRuleId : null,
            $"{(isCreate ? "Tao" : "Cap nhat")} policy {eventType}/{channel}/{audienceType}.",
            actorUserId,
            new { eventType, channel, audienceType, request.CommunicationTemplateId, request.IsEnabled, cooldownMinutes, deliveryMode, priority });

        await _db.SaveChangesAsync(cancellationToken);
        return Ok(new { success = true, message = isCreate ? "Da tao notification policy." : "Da cap nhat notification policy." });
    }

    [HttpPost("policies/{id:int}/toggle")]
    public async Task<IActionResult> TogglePolicy([FromRoute] int id, CancellationToken cancellationToken = default)
    {
        var policy = await _db.NotificationPolicyRules.FirstOrDefaultAsync(x => x.NotificationPolicyRuleId == id, cancellationToken);
        if (policy is null)
        {
            return NotFound(new { success = false, message = "Khong tim thay policy." });
        }

        policy.IsEnabled = !policy.IsEnabled;
        policy.UpdatedAt = DateTime.UtcNow;
        var actorUserId = GetActorUserId();

        AdminAuditLogger.AddAction(
            _db,
            "communications_governance",
            policy.IsEnabled ? "enable_policy" : "disable_policy",
            "notification_policy",
            policy.NotificationPolicyRuleId,
            $"{(policy.IsEnabled ? "Bat" : "Tat")} policy {policy.EventType}/{policy.Channel}/{policy.AudienceType}.",
            actorUserId,
            new { policy.EventType, policy.Channel, policy.AudienceType, policy.IsEnabled });

        await _db.SaveChangesAsync(cancellationToken);
        return Ok(new { success = true, message = policy.IsEnabled ? "Da kich hoat policy." : "Da tam dung policy." });
    }

    [HttpPost("preferences")]
    public async Task<IActionResult> SavePreference([FromBody] SaveNotificationPreferenceRequest? request, CancellationToken cancellationToken = default)
    {
        if (request is null || request.UserId <= 0)
        {
            return BadRequest(new { success = false, message = "UserId khong hop le." });
        }

        var eventType = NormalizeEvent(request.EventType);
        var channel = Normalize(request.Channel, AllowedChannels.Where(x => x != "all"));
        var source = Normalize(request.Source, AllowedSources);
        if (channel == "all")
        {
            return BadRequest(new { success = false, message = "Channel khong hop le." });
        }

        var existing = await _db.NotificationPreferences.FirstOrDefaultAsync(
            x => x.UserId == request.UserId && x.EventType == eventType && x.Channel == channel,
            cancellationToken);

        var actorUserId = GetActorUserId();
        NotificationPreference persistedPreference;
        if (existing is null)
        {
            persistedPreference = new NotificationPreference
            {
                UserId = request.UserId,
                EventType = eventType,
                Channel = channel,
                IsOptedIn = request.IsOptedIn,
                Source = source,
                CreatedAt = DateTime.UtcNow,
                UpdatedAt = DateTime.UtcNow
            };
            _db.NotificationPreferences.Add(persistedPreference);
        }
        else
        {
            persistedPreference = existing;
            var current = persistedPreference;
            current.IsOptedIn = request.IsOptedIn;
            current.Source = source;
            current.UpdatedAt = DateTime.UtcNow;
        }
        var isCreate = existing is null;

        AdminAuditLogger.AddAction(
            _db,
            "communications_governance",
            isCreate ? "create_preference" : "update_preference",
            "notification_preference",
            persistedPreference.NotificationPreferenceId > 0 ? persistedPreference.NotificationPreferenceId : null,
            $"{(request.IsOptedIn ? "Opt-in" : "Opt-out")} user {request.UserId} cho {eventType}/{channel}.",
            actorUserId,
            new { request.UserId, eventType, channel, request.IsOptedIn, source });

        await _db.SaveChangesAsync(cancellationToken);
        return Ok(new { success = true, message = isCreate ? "Da tao communication preference." : "Da cap nhat communication preference." });
    }

    private async Task<List<string>> LoadEventOptionsAsync(CancellationToken cancellationToken)
    {
        var templateEvents = await _db.CommunicationTemplates.AsNoTracking().Select(x => x.EventType).ToListAsync(cancellationToken);
        var policyEvents = await _db.NotificationPolicyRules.AsNoTracking().Select(x => x.EventType).ToListAsync(cancellationToken);
        var preferenceEvents = await _db.NotificationPreferences.AsNoTracking().Select(x => x.EventType).ToListAsync(cancellationToken);
        var notificationEvents = await _db.CustomerNotifications.AsNoTracking()
            .Where(x => !string.IsNullOrWhiteSpace(x.NotificationType))
            .Select(x => x.NotificationType)
            .Distinct()
            .ToListAsync(cancellationToken);

        return KnownEvents
            .Concat(templateEvents)
            .Concat(policyEvents)
            .Concat(preferenceEvents)
            .Concat(notificationEvents)
            .Where(x => !string.IsNullOrWhiteSpace(x))
            .Select(x => x.Trim().ToLowerInvariant())
            .Distinct()
            .OrderBy(x => x == "all" ? string.Empty : x)
            .ToList();
    }

    private static IEnumerable<object> BuildOptions(IEnumerable<string> values)
        => values.Select(value => new { value, text = FormatLabel(value) });

    private static string FormatLabel(string value)
        => value switch
        {
            "all" => "Tat ca",
            "in_app" => "In-app",
            "order_created" => "Order created",
            "order_paid" => "Order paid",
            "order_shipped" => "Order shipped",
            "order_delivered" => "Order delivered",
            "refund_opened" => "Refund opened",
            "return_opened" => "Return opened",
            "voucher_drop" => "Voucher drop",
            "campaign_live" => "Chiến dịch quảng bá",
            "risk_alert" => "Risk alert",
            "fresh_recall" => "Fresh recall",
            _ => string.Join(' ', value.Split('_', StringSplitOptions.RemoveEmptyEntries)
                .Select(part => char.ToUpperInvariant(part[0]) + part[1..]))
        };

    private static string Normalize(string? value, IEnumerable<string> allowed)
    {
        var allowedList = allowed.ToList();
        var normalized = string.IsNullOrWhiteSpace(value) ? allowedList[0] : value.Trim().ToLowerInvariant();
        return allowedList.Contains(normalized) ? normalized : allowedList[0];
    }

    private static string NormalizeEvent(string? value)
        => string.IsNullOrWhiteSpace(value) ? "order_created" : value.Trim().ToLowerInvariant();

    private int? GetActorUserId()
    {
        var raw = User.FindFirstValue(ClaimTypes.NameIdentifier)
                  ?? User.FindFirstValue("sub");
        return int.TryParse(raw, out var actorUserId) && actorUserId > 0
            ? actorUserId
            : null;
    }

    private static string? TrimOrNull(string? value, int maxLength)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return null;
        }

        var trimmed = value.Trim();
        return trimmed.Length <= maxLength ? trimmed : trimmed[..maxLength];
    }

    public sealed class SaveCommunicationTemplateRequest
    {
        public int CommunicationTemplateId { get; set; }
        public string? TemplateName { get; set; }
        public string? EventType { get; set; }
        public string? Channel { get; set; }
        public string? Locale { get; set; }
        public string? Subject { get; set; }
        public string? Body { get; set; }
        public bool IsActive { get; set; } = true;
    }

    public sealed class SaveNotificationPolicyRequest
    {
        public int NotificationPolicyRuleId { get; set; }
        public string? EventType { get; set; }
        public string? Channel { get; set; }
        public string? AudienceType { get; set; }
        public int? CommunicationTemplateId { get; set; }
        public bool IsEnabled { get; set; } = true;
        public int CooldownMinutes { get; set; }
        public string? DeliveryMode { get; set; }
        public int Priority { get; set; } = 50;
        public string? Notes { get; set; }
    }

    public sealed class SaveNotificationPreferenceRequest
    {
        public int UserId { get; set; }
        public string? EventType { get; set; }
        public string? Channel { get; set; }
        public bool IsOptedIn { get; set; } = true;
        public string? Source { get; set; }
    }
}
