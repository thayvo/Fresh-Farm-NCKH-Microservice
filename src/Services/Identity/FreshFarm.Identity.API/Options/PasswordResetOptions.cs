namespace FreshFarm.Identity.Api.Options;

public sealed class PasswordResetOptions
{
    public const string SectionName = "PasswordReset";

    public int TokenLifetimeMinutes { get; set; } = 30;

    public string ResetUrlBase { get; set; } = string.Empty;
}
