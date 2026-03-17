namespace FreshFarm.Ordering.Api.Options;

public sealed class CatalogServiceOptions
{
    public const string SectionName = "Services:Catalog";

    public string BaseUrl { get; set; } = string.Empty;

    public string InternalServiceKey { get; set; } = string.Empty;
}
