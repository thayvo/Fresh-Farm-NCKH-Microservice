namespace FreshFarm.Identity.Api.Options;

public sealed class GeoIpOptions
{
    public const string SectionName = "GeoIp";

    public string BaseUrl { get; set; } = "https://ipwho.is/";

    public int TimeoutSeconds { get; set; } = 2;

    public int CacheHours { get; set; } = 6;
}
