namespace FreshFarm.Identity.Api.Options;

public sealed class InternalBffOptions
{
    public const string SectionName = "InternalBff";

    public string HeaderName { get; set; } = "X-FreshFarm-Internal-Key";

    public string SharedKey { get; set; } = string.Empty;

    public bool IsConfigured => !string.IsNullOrWhiteSpace(HeaderName) && !string.IsNullOrWhiteSpace(SharedKey);
}
