namespace FreshFarm.Web.Bff.Areas.Admin.Models;

public sealed class CommunicationGovernancePageViewModel
{
    public string Query { get; set; } = string.Empty;

    public string Channel { get; set; } = "all";

    public string EventType { get; set; } = "all";

    public CommunicationGovernanceStatsViewModel Stats { get; set; } = new();

    public List<CommunicationOptionViewModel> ChannelOptions { get; set; } = new();

    public List<CommunicationOptionViewModel> EventOptions { get; set; } = new();

    public List<CommunicationOptionViewModel> AudienceOptions { get; set; } = new();

    public List<CommunicationOptionViewModel> DeliveryModeOptions { get; set; } = new();

    public List<CommunicationOptionViewModel> SourceOptions { get; set; } = new();

    public List<CommunicationTemplateOptionViewModel> TemplateOptions { get; set; } = new();

    public List<CommunicationTemplateRowViewModel> Templates { get; set; } = new();

    public List<CommunicationPolicyRowViewModel> Policies { get; set; } = new();

    public List<CommunicationPreferenceRowViewModel> Preferences { get; set; } = new();

    public CommunicationTemplateEditorInput NewTemplate { get; set; } = new();

    public CommunicationPolicyEditorInput NewPolicy { get; set; } = new();

    public CommunicationPreferenceEditorInput NewPreference { get; set; } = new();
}

public sealed class CommunicationGovernanceStatsViewModel
{
    public int TotalTemplates { get; set; }
    public int ActiveTemplates { get; set; }
    public int EnabledPolicies { get; set; }
    public int OptedOutCount { get; set; }
    public int UpdatedPreferences24h { get; set; }
}

public sealed class CommunicationOptionViewModel
{
    public string Value { get; set; } = string.Empty;

    public string Text { get; set; } = string.Empty;
}

public sealed class CommunicationTemplateOptionViewModel
{
    public int CommunicationTemplateId { get; set; }

    public string Text { get; set; } = string.Empty;
}

public sealed class CommunicationTemplateRowViewModel
{
    public int CommunicationTemplateId { get; set; }
    public string TemplateName { get; set; } = string.Empty;
    public string EventType { get; set; } = string.Empty;
    public string Channel { get; set; } = string.Empty;
    public string Locale { get; set; } = string.Empty;
    public string? Subject { get; set; }
    public string Body { get; set; } = string.Empty;
    public bool IsActive { get; set; }
    public int Version { get; set; }
    public int? LastEditedByUserId { get; set; }
    public DateTime CreatedAt { get; set; }
    public DateTime? UpdatedAt { get; set; }
}

public sealed class CommunicationPolicyRowViewModel
{
    public int NotificationPolicyRuleId { get; set; }
    public string EventType { get; set; } = string.Empty;
    public string Channel { get; set; } = string.Empty;
    public string AudienceType { get; set; } = string.Empty;
    public int? CommunicationTemplateId { get; set; }
    public string? TemplateName { get; set; }
    public bool IsEnabled { get; set; }
    public int CooldownMinutes { get; set; }
    public string DeliveryMode { get; set; } = string.Empty;
    public int Priority { get; set; }
    public string? Notes { get; set; }
    public DateTime CreatedAt { get; set; }
    public DateTime? UpdatedAt { get; set; }
}

public sealed class CommunicationPreferenceRowViewModel
{
    public int NotificationPreferenceId { get; set; }
    public int UserId { get; set; }
    public string EventType { get; set; } = string.Empty;
    public string Channel { get; set; } = string.Empty;
    public bool IsOptedIn { get; set; }
    public string Source { get; set; } = string.Empty;
    public DateTime CreatedAt { get; set; }
    public DateTime UpdatedAt { get; set; }
}

public sealed class CommunicationTemplateEditorInput
{
    public int CommunicationTemplateId { get; set; }
    public string TemplateName { get; set; } = string.Empty;
    public string EventType { get; set; } = "order_created";
    public string Channel { get; set; } = "in_app";
    public string Locale { get; set; } = "vi-VN";
    public string? Subject { get; set; }
    public string Body { get; set; } = string.Empty;
    public bool IsActive { get; set; } = true;
}

public sealed class CommunicationPolicyEditorInput
{
    public int NotificationPolicyRuleId { get; set; }
    public string EventType { get; set; } = "order_created";
    public string Channel { get; set; } = "in_app";
    public string AudienceType { get; set; } = "buyer";
    public int? CommunicationTemplateId { get; set; }
    public bool IsEnabled { get; set; } = true;
    public int CooldownMinutes { get; set; }
    public string DeliveryMode { get; set; } = "immediate";
    public int Priority { get; set; } = 50;
    public string? Notes { get; set; }
}

public sealed class CommunicationPreferenceEditorInput
{
    public int UserId { get; set; }
    public string EventType { get; set; } = "order_created";
    public string Channel { get; set; } = "in_app";
    public bool IsOptedIn { get; set; } = true;
    public string Source { get; set; } = "admin_console";
}
