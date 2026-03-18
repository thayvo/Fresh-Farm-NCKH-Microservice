namespace FreshFarm.Identity.Api.Options;

public sealed class EmailVerificationOptions
{
    public const string SectionName = "EmailVerification";

    public int TokenLifetimeMinutes { get; set; } = 60;

    public string VerifyUrlBase { get; set; } = string.Empty;
}
