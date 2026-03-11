using System.Text.Json;
using FreshFarm.Ordering.Api.Models;

namespace FreshFarm.Ordering.Api.Controllers;

internal static class AdminAuditLogger
{
    public static AdminActionLog AddAction(
        FreshFarmOrderingDBContext db,
        string area,
        string actionName,
        string targetType,
        int? targetId,
        string summary,
        int? actorUserId,
        object? metadata = null)
    {
        var log = new AdminActionLog
        {
            Area = area,
            ActionName = actionName,
            TargetType = targetType,
            TargetId = targetId,
            Summary = summary,
            ActorUserId = actorUserId,
            MetadataJson = metadata is null ? null : JsonSerializer.Serialize(metadata),
            CreatedAt = DateTime.UtcNow
        };

        db.AdminActionLogs.Add(log);
        return log;
    }

    public static void AddModeration(
        FreshFarmOrderingDBContext db,
        AdminActionLog? actionLog,
        string subjectType,
        int? subjectId,
        string decision,
        string? notes,
        int? actorUserId)
    {
        db.ModerationAudits.Add(new ModerationAudit
        {
            AdminActionLog = actionLog,
            SubjectType = subjectType,
            SubjectId = subjectId,
            Decision = decision,
            Notes = Trim(notes, 2000),
            ActorUserId = actorUserId,
            CreatedAt = DateTime.UtcNow
        });
    }

    public static void AddSettlement(
        FreshFarmOrderingDBContext db,
        AdminActionLog? actionLog,
        string auditType,
        string referenceType,
        int? referenceId,
        int? sellerId,
        decimal? amount,
        string? notes,
        int? actorUserId)
    {
        db.SettlementAudits.Add(new SettlementAudit
        {
            AdminActionLog = actionLog,
            AuditType = auditType,
            ReferenceType = referenceType,
            ReferenceId = referenceId,
            SellerId = sellerId,
            Amount = amount,
            Currency = "VND",
            Notes = Trim(notes, 2000),
            ActorUserId = actorUserId,
            CreatedAt = DateTime.UtcNow
        });
    }

    private static string? Trim(string? value, int maxLength)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return null;
        }

        var trimmed = value.Trim();
        return trimmed.Length <= maxLength ? trimmed : trimmed[..maxLength];
    }
}
