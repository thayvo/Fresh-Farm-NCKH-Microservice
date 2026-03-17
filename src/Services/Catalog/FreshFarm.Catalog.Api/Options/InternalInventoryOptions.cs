namespace FreshFarm.Catalog.Api.Options;

public sealed class InternalInventoryOptions
{
    public const string SectionName = "Services:Internal";

    public string ServiceKey { get; set; } = string.Empty;
}
