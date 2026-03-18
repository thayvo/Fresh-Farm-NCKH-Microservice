namespace FreshFarm.Ordering.Api.Options;

public sealed class InternalServiceAuthOptions
{
    public const string SectionName = "Services:InternalAuth";

    public string InternalServiceKey { get; set; } = string.Empty;
}
