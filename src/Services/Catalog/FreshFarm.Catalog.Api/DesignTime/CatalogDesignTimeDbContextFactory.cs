using FreshFarm.Catalog.Api.Models;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;
using Microsoft.Extensions.Configuration;

namespace FreshFarm.Catalog.Api.DesignTime;

public sealed class CatalogDesignTimeDbContextFactory : IDesignTimeDbContextFactory<FreshFarmCatalogDBContext>
{
    public FreshFarmCatalogDBContext CreateDbContext(string[] args)
    {
        var configuration = BuildConfiguration();
        var connectionString = configuration.GetConnectionString("FreshFarmCatalogDB");
        if (string.IsNullOrWhiteSpace(connectionString))
        {
            throw new InvalidOperationException("Khong tim thay ConnectionStrings:FreshFarmCatalogDB cho design-time DbContext.");
        }

        var optionsBuilder = new DbContextOptionsBuilder<FreshFarmCatalogDBContext>();
        optionsBuilder.UseSqlServer(connectionString);
        return new FreshFarmCatalogDBContext(optionsBuilder.Options);
    }

    private static IConfiguration BuildConfiguration()
    {
        var environment = Environment.GetEnvironmentVariable("ASPNETCORE_ENVIRONMENT") ?? "Development";
        return new ConfigurationBuilder()
            .SetBasePath(Directory.GetCurrentDirectory())
            .AddJsonFile("appsettings.json", optional: true)
            .AddJsonFile($"appsettings.{environment}.json", optional: true)
            .AddUserSecrets<CatalogDesignTimeDbContextFactory>(optional: true)
            .AddEnvironmentVariables()
            .Build();
    }
}
