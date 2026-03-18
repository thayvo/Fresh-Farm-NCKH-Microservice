namespace FreshFarm.Identity.Api.Models;

public partial class AuthAuditLog
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
