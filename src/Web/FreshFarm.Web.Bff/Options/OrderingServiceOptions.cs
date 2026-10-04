namespace FreshFarm.Web.Bff.Options;

public sealed class OrderingServiceOptions
{
    public const string SectionName = "Services:Ordering";

    public string BaseUrl { get; set; } = string.Empty;

    public string InternalServiceKey { get; set; } = string.Empty;
}
