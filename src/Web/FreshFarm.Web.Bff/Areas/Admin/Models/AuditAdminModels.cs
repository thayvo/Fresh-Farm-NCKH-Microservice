namespace FreshFarm.Web.Bff.Areas.Admin.Models;

public sealed class AuditCenterPageViewModel
{
    public string Query { get; set; } = string.Empty;

    public string Area { get; set; } = "all";

    public string Type { get; set; } = "all";

    public string AuthQuery { get; set; } = string.Empty;

    public string AuthRole { get; set; } = "all";

    public string AuthOutcome { get; set; } = "all";

    public AuditCenterStatsViewModel Stats { get; set; } = new();

    public AuthAuditStatsViewModel AuthStats { get; set; } = new();

    public List<AuditOptionViewModel> AreaOptions { get; set; } = new();

    public List<AuditOptionViewModel> TypeOptions { get; set; } = new();

    public List<AuditOptionViewModel> AuthRoleOptions { get; set; } = new();

    public List<AuditOptionViewModel> AuthOutcomeOptions { get; set; } = new();

    public List<AdminActionLogRowViewModel> ActionLogs { get; set; } = new();

    public List<ModerationAuditRowViewModel> ModerationAudits { get; set; } = new();

    public List<SettlementAuditRowViewModel> SettlementAudits { get; set; } = new();

    public List<AuthAuditRowViewModel> AuthAudits { get; set; } = new();
}

public sealed class AuditCenterStatsViewModel
{
    public int TotalActionLogs { get; set; }

    public int Recent24hActions { get; set; }

    public int ModerationCount { get; set; }

    public int SettlementCount { get; set; }

    public decimal SettlementAmount7d { get; set; }
}

public sealed class AuthAuditStatsViewModel
{
    public int Total24h { get; set; }

    public int Success24h { get; set; }

    public int Failed24h { get; set; }

    public int Locked24h { get; set; }

    public int Suspicious24h { get; set; }
}

public sealed class AuditOptionViewModel
{
    public string Value { get; set; } = string.Empty;

    public string Text { get; set; } = string.Empty;
}

public sealed class AdminActionLogRowViewModel
{
    public int AdminActionLogId { get; set; }

    public string Area { get; set; } = string.Empty;

    public string ActionName { get; set; } = string.Empty;

    public string TargetType { get; set; } = string.Empty;

    public int? TargetId { get; set; }

    public string Summary { get; set; } = string.Empty;

    public int? ActorUserId { get; set; }

    public string? MetadataJson { get; set; }

    public DateTime CreatedAt { get; set; }
}

public sealed class ModerationAuditRowViewModel
{
    public int ModerationAuditId { get; set; }

    public int? AdminActionLogId { get; set; }

    public string SubjectType { get; set; } = string.Empty;

    public int? SubjectId { get; set; }

    public string Decision { get; set; } = string.Empty;

    public string? Notes { get; set; }

    public int? ActorUserId { get; set; }

    public DateTime CreatedAt { get; set; }
}

public sealed class SettlementAuditRowViewModel
{
    public int SettlementAuditId { get; set; }

    public int? AdminActionLogId { get; set; }

    public string AuditType { get; set; } = string.Empty;

    public string ReferenceType { get; set; } = string.Empty;

    public int? ReferenceId { get; set; }

    public int? SellerId { get; set; }

    public decimal? Amount { get; set; }

    public string Currency { get; set; } = string.Empty;

    public string? Notes { get; set; }

    public int? ActorUserId { get; set; }

    public DateTime CreatedAt { get; set; }
}

public sealed class AuthAuditRowViewModel
{
    public int AuthAuditLogId { get; set; }

    public int? UserId { get; set; }

    public string ClientLane { get; set; } = string.Empty;

    public string RoleName { get; set; } = string.Empty;

    public string Identifier { get; set; } = string.Empty;

    public string? UserName { get; set; }

    public string? Email { get; set; }

    public string EventType { get; set; } = string.Empty;

    public bool Success { get; set; }

    public string? FailureReason { get; set; }

    public int? FailedAttemptCount { get; set; }

    public string? IpAddress { get; set; }

    public string? ForwardedFor { get; set; }

    public string? UserAgent { get; set; }

    public string? DeviceType { get; set; }

    public string? BrowserFamily { get; set; }

    public string? OperatingSystem { get; set; }

    public bool IsSuspicious { get; set; }

    public string? SuspicionReasons { get; set; }

    public DateTime OccurredAt { get; set; }
}
