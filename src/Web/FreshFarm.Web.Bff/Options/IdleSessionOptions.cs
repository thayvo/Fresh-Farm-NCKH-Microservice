namespace FreshFarm.Web.Bff.Options;

public sealed class IdleSessionOptions
{
    public const string SectionName = "IdleSession";

    public bool Enabled { get; set; } = true;

    public int WarningAfterMinutes { get; set; } = 10;

    public int AutoLogoutAfterWarningMinutes { get; set; } = 2;

    public int AuthenticationLifetimeMinutes { get; set; } = 120;

    public int ServerSessionIdleTimeoutMinutes { get; set; } = 120;
}
