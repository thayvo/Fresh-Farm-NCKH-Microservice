namespace FreshFarm.Web.Bff.Options;

public sealed class BotChallengeOptions
{
    public const string SectionName = "Security:BotChallenge";

    // Supported values: CloudflareTurnstile, GoogleRecaptcha, LocalSvg.
    public string Provider { get; set; } = "CloudflareTurnstile";

    public string SignUpAction { get; set; } = "signup";

    public string ForgotPasswordAction { get; set; } = "forgot_password";

    public bool AllowLocalSvgInDevelopment { get; set; } = true;
}
