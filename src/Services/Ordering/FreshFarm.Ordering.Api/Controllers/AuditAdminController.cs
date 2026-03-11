using FreshFarm.Ordering.Api.Models;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace FreshFarm.Ordering.Api.Controllers;

[ApiController]
[Route("api/orders/admin/audit")]
[Authorize(Policy = "AdminOnly")]
public sealed class AuditAdminController : ControllerBase
{
    private static readonly string[] AllowedAreas = { "all", "campaign_center", "risk_center" };
    private static readonly string[] AllowedTypes = { "all", "campaign", "ads_wallet", "ads_campaign", "risk_case", "risk_center" };

    private readonly FreshFarmOrderingDBContext _db;

    public AuditAdminController(FreshFarmOrderingDBContext db)
    {
        _db = db;
    }

    [HttpGet("center")]
    public async Task<IActionResult> GetCenter(
        [FromQuery] string? q = null,
        [FromQuery] string? area = null,
        [FromQuery] string? type = null,
        CancellationToken cancellationToken = default)
    {
        var normalizedQuery = q?.Trim();
        var normalizedArea = Normalize(area, AllowedAreas);
        var normalizedType = Normalize(type, AllowedTypes);
        var actions = _db.AdminActionLogs.AsNoTracking().AsQueryable();

        if (!string.IsNullOrWhiteSpace(normalizedQuery))
        {
            actions = actions.Where(x =>
                x.Area.Contains(normalizedQuery) ||
                x.ActionName.Contains(normalizedQuery) ||
                x.TargetType.Contains(normalizedQuery) ||
                x.Summary.Contains(normalizedQuery));
        }

        if (normalizedArea != "all")
        {
            actions = actions.Where(x => x.Area == normalizedArea);
        }

        if (normalizedType != "all")
        {
            actions = actions.Where(x => x.TargetType == normalizedType);
        }

        var since24h = DateTime.UtcNow.AddHours(-24);
        var since7d = DateTime.UtcNow.AddDays(-7);

        var totalActionLogs = await _db.AdminActionLogs.AsNoTracking().CountAsync(cancellationToken);
        var recent24hActions = await _db.AdminActionLogs.AsNoTracking().CountAsync(x => x.CreatedAt >= since24h, cancellationToken);
        var moderationCount = await _db.ModerationAudits.AsNoTracking().CountAsync(cancellationToken);
        var settlementCount = await _db.SettlementAudits.AsNoTracking().CountAsync(cancellationToken);
        var settlementAmount7d = await _db.SettlementAudits.AsNoTracking()
            .Where(x => x.CreatedAt >= since7d)
            .SumAsync(x => (decimal?)x.Amount, cancellationToken) ?? 0m;

        var actionRows = await actions
            .OrderByDescending(x => x.CreatedAt)
            .Select(x => new
            {
                adminActionLogId = x.AdminActionLogId,
                area = x.Area,
                actionName = x.ActionName,
                targetType = x.TargetType,
                targetId = x.TargetId,
                summary = x.Summary,
                actorUserId = x.ActorUserId,
                metadataJson = x.MetadataJson,
                createdAt = x.CreatedAt
            })
            .Take(30)
            .ToListAsync(cancellationToken);

        var actionIds = actionRows.Select(x => x.adminActionLogId).ToList();

        var moderationRows = await _db.ModerationAudits
            .AsNoTracking()
            .Where(x => !actionIds.Any() || (x.AdminActionLogId.HasValue && actionIds.Contains(x.AdminActionLogId.Value)))
            .OrderByDescending(x => x.CreatedAt)
            .Select(x => new
            {
                moderationAuditId = x.ModerationAuditId,
                adminActionLogId = x.AdminActionLogId,
                subjectType = x.SubjectType,
                subjectId = x.SubjectId,
                decision = x.Decision,
                notes = x.Notes,
                actorUserId = x.ActorUserId,
                createdAt = x.CreatedAt
            })
            .Take(12)
            .ToListAsync(cancellationToken);

        var settlementRows = await _db.SettlementAudits
            .AsNoTracking()
            .Where(x => !actionIds.Any() || (x.AdminActionLogId.HasValue && actionIds.Contains(x.AdminActionLogId.Value)))
            .OrderByDescending(x => x.CreatedAt)
            .Select(x => new
            {
                settlementAuditId = x.SettlementAuditId,
                adminActionLogId = x.AdminActionLogId,
                auditType = x.AuditType,
                referenceType = x.ReferenceType,
                referenceId = x.ReferenceId,
                sellerId = x.SellerId,
                amount = x.Amount,
                currency = x.Currency,
                notes = x.Notes,
                actorUserId = x.ActorUserId,
                createdAt = x.CreatedAt
            })
            .Take(12)
            .ToListAsync(cancellationToken);

        return Ok(new
        {
            stats = new
            {
                totalActionLogs,
                recent24hActions,
                moderationCount,
                settlementCount,
                settlementAmount7d
            },
            filters = new
            {
                q = normalizedQuery ?? string.Empty,
                area = normalizedArea,
                type = normalizedType,
                areaOptions = BuildOptions(AllowedAreas),
                typeOptions = BuildOptions(AllowedTypes)
            },
            actions = actionRows,
            moderationAudits = moderationRows,
            settlementAudits = settlementRows
        });
    }

    private static string Normalize(string? value, IEnumerable<string> allowed)
    {
        var normalized = string.IsNullOrWhiteSpace(value) ? "all" : value.Trim().ToLowerInvariant();
        return allowed.Contains(normalized) ? normalized : "all";
    }

    private static IEnumerable<object> BuildOptions(IEnumerable<string> values)
        => values.Select(value => new { value, text = FormatLabel(value) });

    private static string FormatLabel(string value)
        => value switch
        {
            "all" => "Tat ca",
            "campaign_center" => "Campaign center",
            "risk_center" => "Risk center",
            "ads_wallet" => "Ads wallet",
            "ads_campaign" => "Ads campaign",
            "risk_case" => "Risk case",
            _ => string.Join(' ', value.Split('_', StringSplitOptions.RemoveEmptyEntries)
                .Select(part => char.ToUpperInvariant(part[0]) + part[1..]))
        };
}
