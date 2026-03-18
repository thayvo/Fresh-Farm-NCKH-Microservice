using FreshFarm.Identity.Api.Models;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace FreshFarm.Identity.Api.Controllers;

[ApiController]
[Route("admin/auth-audit")]
[Authorize(Policy = "AdminOnly")]
public sealed class AdminAuthAuditController : ControllerBase
{
    private static readonly string[] AllowedRoles = { "all", "admin", "seller", "customer", "unknown" };
    private static readonly string[] AllowedOutcomes = { "all", "success", "failed", "suspicious", "locked" };

    private readonly FreshFarmIdentityDBContext _db;

    public AdminAuthAuditController(FreshFarmIdentityDBContext db)
    {
        _db = db;
    }

    [HttpGet]
    public async Task<IActionResult> GetAuthAudit(
        [FromQuery] string? q = null,
        [FromQuery] string? role = null,
        [FromQuery] string? outcome = null,
        CancellationToken cancellationToken = default)
    {
        var normalizedQuery = q?.Trim();
        var normalizedRole = Normalize(role, AllowedRoles);
        var normalizedOutcome = Normalize(outcome, AllowedOutcomes);

        var authEvents = _db.AuthAuditLogs.AsNoTracking().AsQueryable();

        if (!string.IsNullOrWhiteSpace(normalizedQuery))
        {
            authEvents = authEvents.Where(x =>
                x.Identifier.Contains(normalizedQuery) ||
                (x.UserName != null && x.UserName.Contains(normalizedQuery)) ||
                (x.Email != null && x.Email.Contains(normalizedQuery)) ||
                (x.IpAddress != null && x.IpAddress.Contains(normalizedQuery)) ||
                (x.ForwardedFor != null && x.ForwardedFor.Contains(normalizedQuery)) ||
                (x.UserAgent != null && x.UserAgent.Contains(normalizedQuery)) ||
                (x.FailureReason != null && x.FailureReason.Contains(normalizedQuery)));
        }

        if (normalizedRole != "all")
        {
            var targetRole = normalizedRole switch
            {
                "admin" => "Admin",
                "seller" => "Seller",
                "customer" => "Customer",
                _ => "Unknown"
            };

            authEvents = authEvents.Where(x => x.RoleName == targetRole);
        }

        authEvents = normalizedOutcome switch
        {
            "success" => authEvents.Where(x => x.Success),
            "failed" => authEvents.Where(x => !x.Success),
            "suspicious" => authEvents.Where(x => x.IsSuspicious),
            "locked" => authEvents.Where(x => x.EventType == "login_locked"),
            _ => authEvents
        };

        var since24h = DateTime.UtcNow.AddHours(-24);
        var statsBase = _db.AuthAuditLogs.AsNoTracking()
            .Where(x => x.OccurredAt >= since24h &&
                (x.RoleName == "Admin" || x.RoleName == "Seller" || x.RoleName == "Customer" || x.RoleName == "Unknown"));

        var total24h = await statsBase.CountAsync(cancellationToken);
        var success24h = await statsBase.CountAsync(x => x.Success, cancellationToken);
        var failed24h = await statsBase.CountAsync(x => !x.Success, cancellationToken);
        var locked24h = await statsBase.CountAsync(x => x.EventType == "login_locked", cancellationToken);
        var suspicious24h = await statsBase.CountAsync(x => x.IsSuspicious, cancellationToken);

        var rows = await authEvents
            .OrderByDescending(x => x.OccurredAt)
            .Select(x => new
            {
                authAuditLogId = x.AuthAuditLogId,
                userId = x.UserId,
                clientLane = x.ClientLane,
                roleName = x.RoleName,
                identifier = x.Identifier,
                userName = x.UserName,
                email = x.Email,
                eventType = x.EventType,
                success = x.Success,
                failureReason = x.FailureReason,
                failedAttemptCount = x.FailedAttemptCount,
                ipAddress = x.IpAddress,
                forwardedFor = x.ForwardedFor,
                userAgent = x.UserAgent,
                deviceType = x.DeviceType,
                browserFamily = x.BrowserFamily,
                operatingSystem = x.OperatingSystem,
                isSuspicious = x.IsSuspicious,
                suspicionReasons = x.SuspicionReasons,
                occurredAt = x.OccurredAt
            })
            .Take(40)
            .ToListAsync(cancellationToken);

        return Ok(new
        {
            stats = new
            {
                total24h,
                success24h,
                failed24h,
                locked24h,
                suspicious24h
            },
            filters = new
            {
                q = normalizedQuery ?? string.Empty,
                role = normalizedRole,
                outcome = normalizedOutcome,
                roleOptions = BuildOptions(AllowedRoles),
                outcomeOptions = BuildOptions(AllowedOutcomes)
            },
            items = rows
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
            "all" => "Tất cả",
            "admin" => "Admin",
            "seller" => "Seller",
            "customer" => "Người dùng",
            "unknown" => "Không xác định",
            "success" => "Thành công",
            "failed" => "Thất bại",
            "suspicious" => "Đáng ngờ",
            "locked" => "Bị khóa",
            _ => value
        };
}
