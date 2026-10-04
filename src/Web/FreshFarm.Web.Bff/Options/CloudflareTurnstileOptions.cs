namespace FreshFarm.Web.Bff.Options;

public sealed class CloudflareTurnstileOptions
{
    public const string SectionName = "Security:CloudflareTurnstile";

    public string SiteKey { get; set; } = string.Empty;

    public string SecretKey { get; set; } = string.Empty;

    public string VerificationEndpoint { get; set; } = "https://challenges.cloudflare.com/turnstile/v0/siteverify";

    public string[] AllowedHostnames { get; set; } = [];

    public bool IsConfigured =>
        !string.IsNullOrWhiteSpace(SiteKey) &&
        !string.IsNullOrWhiteSpace(SecretKey) &&
        Uri.TryCreate(VerificationEndpoint, UriKind.Absolute, out var endpoint) &&
        string.Equals(endpoint.Scheme, Uri.UriSchemeHttps, StringComparison.OrdinalIgnoreCase) &&
        AllowedHostnames.Any(hostname => !string.IsNullOrWhiteSpace(hostname));
}
