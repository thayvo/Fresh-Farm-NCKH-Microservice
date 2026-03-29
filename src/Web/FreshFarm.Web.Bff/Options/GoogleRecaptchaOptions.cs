namespace FreshFarm.Web.Bff.Options;

public sealed class GoogleRecaptchaOptions
{
    public const string SectionName = "Security:GoogleRecaptcha";

    public string SiteKey { get; set; } = string.Empty;

    public string SecretKey { get; set; } = string.Empty;

    public string VerificationEndpoint { get; set; } = "https://www.google.com/recaptcha/api/siteverify";

    public string SignUpAction { get; set; } = "signup";

    public string SellerApplicationAction { get; set; } = "become_seller";

    public decimal MinimumScore { get; set; } = 0.5m;

    public bool AllowDevelopmentBypass { get; set; }

    public string DevelopmentBypassToken { get; set; } = string.Empty;

    public bool IsConfigured =>
        !string.IsNullOrWhiteSpace(SiteKey) &&
        !string.IsNullOrWhiteSpace(SecretKey);
}
