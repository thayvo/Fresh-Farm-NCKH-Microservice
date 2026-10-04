namespace FreshFarm.Identity.Api.Models;

public sealed class LoginDeviceSecurityState
{
    public long LoginDeviceSecurityStateId { get; set; }

    public string DeviceKeyHash { get; set; } = string.Empty;

    public int? LastUserId { get; set; }

    public int FailedCount { get; set; }

    public int LockoutLevel { get; set; }

    public DateTime? LockedUntil { get; set; }

    public DateTime? LastFailedAt { get; set; }

    public DateTime CreatedAt { get; set; }

    public DateTime UpdatedAt { get; set; }
}
