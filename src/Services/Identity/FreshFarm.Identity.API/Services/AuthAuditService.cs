using FreshFarm.Identity.Api.Models;
using Microsoft.EntityFrameworkCore;

namespace FreshFarm.Identity.Api.Services;

public interface IAuthAuditService
{
    Task WriteAsync(AuthAuditWriteRequest request, CancellationToken cancellationToken = default);
}

public sealed class AuthAuditWriteRequest
{
    public HttpContext? HttpContext { get; init; }
    public string? ClientLane { get; init; }
    public User? User { get; init; }
    public IReadOnlyCollection<string>? RoleNames { get; init; }
    public string Identifier { get; init; } = string.Empty;
    public string EventType { get; init; } = string.Empty;
    public bool Success { get; init; }
    public string? FailureReason { get; init; }
    public int? FailedAttemptCount { get; init; }
}

public sealed class AuthAuditService : IAuthAuditService
{
    private readonly FreshFarmIdentityDBContext _db;
    private readonly IGeoIpLookupService _geoIpLookupService;
    private readonly ILogger<AuthAuditService> _logger;
    private static readonly string[] SuspiciousAutomationMarkers = ["bot", "crawler", "spider", "curl", "postman", "python-requests", "powershell", "wget"];

    public AuthAuditService(
        FreshFarmIdentityDBContext db,
        IGeoIpLookupService geoIpLookupService,
        ILogger<AuthAuditService> logger)
    {
        _db = db;
        _geoIpLookupService = geoIpLookupService;
        _logger = logger;
    }

    public async Task WriteAsync(AuthAuditWriteRequest request, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);

        var clientLane = NormalizeLane(request.ClientLane);
        var primaryRole = ResolvePrimaryRole(request.User, request.RoleNames);

        var httpContext = request.HttpContext;
        var userAgent = httpContext?.Request.Headers.UserAgent.ToString();
        var forwardedFor = httpContext?.Request.Headers["X-Forwarded-For"].ToString();
        var remoteIp = httpContext?.Connection.RemoteIpAddress?.ToString();
        var geo = await _geoIpLookupService.ResolveAsync(remoteIp, forwardedFor, cancellationToken);
        var deviceInfo = ParseUserAgent(userAgent);

        var suspicionReasons = await BuildSuspicionReasonsAsync(
            clientLane,
            primaryRole,
            request.EventType,
            request.FailureReason,
            request.FailedAttemptCount,
            userAgent,
            geo.EffectiveIp ?? remoteIp,
            geo.CountryCode,
            request.Identifier,
            cancellationToken);

        var row = new AuthAuditLog
        {
            UserId = request.User?.UserId,
            ClientLane = clientLane,
            RoleName = primaryRole,
            Identifier = TrimToLength(request.Identifier?.Trim(), 100) ?? string.Empty,
            UserName = TrimToLength(request.User?.UserName, 100),
            Email = TrimToLength(request.User?.Email, 100),
            EventType = TrimToLength(request.EventType, 50) ?? string.Empty,
            Success = request.Success,
            FailureReason = TrimToLength(request.FailureReason, 100),
            FailedAttemptCount = request.FailedAttemptCount,
            IpAddress = TrimToLength(geo.EffectiveIp ?? remoteIp, 64),
            ForwardedFor = TrimToLength(forwardedFor, 200),
            CountryCode = TrimToLength(geo.CountryCode, 8),
            CountryName = TrimToLength(geo.CountryName, 120),
            RegionName = TrimToLength(geo.RegionName, 120),
            CityName = TrimToLength(geo.CityName, 120),
            UserAgent = TrimToLength(userAgent, 500),
            DeviceType = deviceInfo.DeviceType,
            BrowserFamily = deviceInfo.BrowserFamily,
            OperatingSystem = deviceInfo.OperatingSystem,
            IsSuspicious = suspicionReasons.Count > 0,
            SuspicionReasons = suspicionReasons.Count == 0 ? null : string.Join(", ", suspicionReasons),
            OccurredAt = DateTime.UtcNow
        };

        _db.AuthAuditLogs.Add(row);
        await _db.SaveChangesAsync(cancellationToken);

        _logger.LogInformation(
            "Da ghi auth audit event {EventType} cho lane {ClientLane}, role {RoleName}, userId {UserId}, suspicious={IsSuspicious}.",
            row.EventType,
            row.ClientLane,
            row.RoleName,
            row.UserId,
            row.IsSuspicious);
    }

    private static bool IsBackofficeLane(string? value)
        => string.Equals(value, "Admin", StringComparison.OrdinalIgnoreCase) ||
           string.Equals(value, "Seller", StringComparison.OrdinalIgnoreCase);

    private static string NormalizeLane(string? lane)
    {
        if (string.Equals(lane, "Admin", StringComparison.OrdinalIgnoreCase))
        {
            return "Admin";
        }

        if (string.Equals(lane, "Seller", StringComparison.OrdinalIgnoreCase))
        {
            return "Seller";
        }

        if (string.Equals(lane, "Customer", StringComparison.OrdinalIgnoreCase))
        {
            return "Customer";
        }

        return "Unknown";
    }

    private static string ResolvePrimaryRole(User? user, IReadOnlyCollection<string>? roleNames)
    {
        var roles = roleNames ??
                    user?.UserRoles?
                        .Select(x => x.Role?.RoleName)
                        .Where(x => !string.IsNullOrWhiteSpace(x))
                        .Cast<string>()
                        .ToArray() ??
                    [];

        if (roles.Any(x => string.Equals(x, "Admin", StringComparison.OrdinalIgnoreCase)))
        {
            return "Admin";
        }

        if (roles.Any(x => string.Equals(x, "Seller", StringComparison.OrdinalIgnoreCase)))
        {
            return "Seller";
        }

        if (roles.Any(x => string.Equals(x, "Customer", StringComparison.OrdinalIgnoreCase)))
        {
            return "Customer";
        }

        return "Unknown";
    }

    private async Task<List<string>> BuildSuspicionReasonsAsync(
        string clientLane,
        string primaryRole,
        string eventType,
        string? failureReason,
        int? failedAttemptCount,
        string? userAgent,
        string? remoteIp,
        string? countryCode,
        string identifier,
        CancellationToken cancellationToken)
    {
        var reasons = new List<string>();

        if (string.IsNullOrWhiteSpace(userAgent))
        {
            reasons.Add("missing_user_agent");
        }

        if (string.Equals(eventType, "login_locked", StringComparison.OrdinalIgnoreCase))
        {
            reasons.Add("lockout_triggered");
        }

        if (string.Equals(eventType, "two_factor_failed", StringComparison.OrdinalIgnoreCase))
        {
            reasons.Add("two_factor_failed");
        }

        if (string.Equals(eventType, "login_failed", StringComparison.OrdinalIgnoreCase) &&
            failedAttemptCount.GetValueOrDefault() >= 3)
        {
            reasons.Add("repeated_failed_login");
        }

        if (!string.IsNullOrWhiteSpace(userAgent) &&
            SuspiciousAutomationMarkers.Any(marker => userAgent.Contains(marker, StringComparison.OrdinalIgnoreCase)))
        {
            reasons.Add("automation_user_agent");
        }

        if (IsBackofficeLane(clientLane) &&
            string.Equals(primaryRole, "Unknown", StringComparison.OrdinalIgnoreCase) &&
            string.Equals(failureReason, "account_not_found", StringComparison.OrdinalIgnoreCase))
        {
            reasons.Add("unknown_account_probe");
        }

        if (IsBackofficeLane(clientLane) &&
            !string.Equals(primaryRole, "Unknown", StringComparison.OrdinalIgnoreCase) &&
            !string.Equals(clientLane, primaryRole, StringComparison.OrdinalIgnoreCase))
        {
            reasons.Add("lane_role_mismatch");
        }

        if (IsBackofficeLane(clientLane) &&
            !string.IsNullOrWhiteSpace(countryCode) &&
            !string.Equals(countryCode, "VN", StringComparison.OrdinalIgnoreCase))
        {
            reasons.Add("foreign_backoffice_login");
        }

        if (!string.IsNullOrWhiteSpace(remoteIp))
        {
            var since = DateTime.UtcNow.AddMinutes(-15);
            var ipWindow = _db.AuthAuditLogs.AsNoTracking()
                .Where(x => x.OccurredAt >= since && x.IpAddress == remoteIp);

            var failedFromIp = await ipWindow.CountAsync(x => !x.Success, cancellationToken);
            if (failedFromIp >= 5)
            {
                reasons.Add("repeated_failed_ip");
            }

            var distinctIdentifiersFromIp = await ipWindow
                .Where(x => !string.IsNullOrEmpty(x.Identifier))
                .Select(x => x.Identifier)
                .Distinct()
                .CountAsync(cancellationToken);

            if (distinctIdentifiersFromIp >= 3)
            {
                reasons.Add("multi_account_probe");
            }
        }

        if (!string.IsNullOrWhiteSpace(identifier))
        {
            var accountFailWindowStart = DateTime.UtcNow.AddMinutes(-30);
            var repeatedAccountFailures = await _db.AuthAuditLogs.AsNoTracking()
                .CountAsync(
                    x => x.OccurredAt >= accountFailWindowStart &&
                         x.Identifier == identifier &&
                         !x.Success,
                    cancellationToken);

            if (repeatedAccountFailures >= 5)
            {
                reasons.Add("account_under_attack");
            }

            if (!string.IsNullOrWhiteSpace(countryCode))
            {
                var countryChangeWindowStart = DateTime.UtcNow.AddDays(-7);
                var recentCountryChange = await _db.AuthAuditLogs.AsNoTracking()
                    .Where(x => x.OccurredAt >= countryChangeWindowStart &&
                                x.Identifier == identifier &&
                                x.Success &&
                                x.CountryCode != null &&
                                x.CountryCode != string.Empty)
                    .Select(x => x.CountryCode!)
                    .Distinct()
                    .AnyAsync(x => x != countryCode, cancellationToken);

                if (recentCountryChange)
                {
                    reasons.Add("country_changed_recently");
                }
            }
        }

        return reasons.Distinct(StringComparer.OrdinalIgnoreCase).ToList();
    }

    private static string? TrimToLength(string? value, int maxLength)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return null;
        }

        var trimmed = value.Trim();
        return trimmed.Length <= maxLength ? trimmed : trimmed[..maxLength];
    }

    private static UserAgentInfo ParseUserAgent(string? userAgent)
    {
        if (string.IsNullOrWhiteSpace(userAgent))
        {
            return new UserAgentInfo("Unknown", "Unknown", "Unknown");
        }

        var ua = userAgent.ToLowerInvariant();

        var deviceType =
            ua.Contains("bot") || ua.Contains("crawler") || ua.Contains("spider") || ua.Contains("curl") || ua.Contains("postman")
                ? "Bot/Automation"
                : ua.Contains("ipad") || ua.Contains("tablet")
                    ? "Tablet"
                    : ua.Contains("mobile") || ua.Contains("android") || ua.Contains("iphone")
                        ? "Mobile"
                        : "Desktop";

        var browser =
            ua.Contains("edg/")
                ? "Edge"
                : ua.Contains("chrome/")
                    ? "Chrome"
                    : ua.Contains("firefox/")
                        ? "Firefox"
                        : ua.Contains("safari/") && !ua.Contains("chrome/")
                            ? "Safari"
                            : ua.Contains("opr/") || ua.Contains("opera")
                                ? "Opera"
                                : "Unknown";

        var os =
            ua.Contains("windows")
                ? "Windows"
                : ua.Contains("android")
                    ? "Android"
                    : ua.Contains("iphone") || ua.Contains("ipad") || ua.Contains("ios")
                        ? "iOS"
                        : ua.Contains("mac os x") || ua.Contains("macintosh")
                            ? "macOS"
                            : ua.Contains("linux")
                                ? "Linux"
                                : "Unknown";

        return new UserAgentInfo(deviceType, browser, os);
    }

    private readonly record struct UserAgentInfo(string DeviceType, string BrowserFamily, string OperatingSystem);
}
