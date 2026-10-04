using Microsoft.Extensions.Options;

namespace FreshFarm.Catalog.Api.Options;

public static class InternalInventoryOptionsServiceCollectionExtensions
{
    public static OptionsBuilder<InternalInventoryOptions> AddInternalInventoryOptions(
        this IServiceCollection services,
        IConfiguration configuration)
        => services
            .AddOptions<InternalInventoryOptions>()
            .Bind(configuration.GetSection(InternalInventoryOptions.SectionName))
            .Validate(
                options => options.IsConfigured,
                "Services:Internal:ServiceKey is required for internal inventory endpoints.")
            .ValidateOnStart();
}
