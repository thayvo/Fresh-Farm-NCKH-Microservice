namespace FreshFarm.Web.Bff.Areas.Admin.Models;

public sealed class AuditCenterPageViewModel
{
    public string Query { get; set; } = string.Empty;

    public string Area { get; set; } = "all";

    public string Type { get; set; } = "all";

    public AuditCenterStatsViewModel Stats { get; set; } = new();

    public List<AuditOptionViewModel> AreaOptions { get; set; } = new();

    public List<AuditOptionViewModel> TypeOptions { get; set; } = new();

    public List<AdminActionLogRowViewModel> ActionLogs { get; set; } = new();

    public List<ModerationAuditRowViewModel> ModerationAudits { get; set; } = new();

    public List<SettlementAuditRowViewModel> SettlementAudits { get; set; } = new();
}

public sealed class AuditCenterStatsViewModel
{
    public int TotalActionLogs { get; set; }

    public int Recent24hActions { get; set; }

    public int ModerationCount { get; set; }

    public int SettlementCount { get; set; }

    public decimal SettlementAmount7d { get; set; }
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
