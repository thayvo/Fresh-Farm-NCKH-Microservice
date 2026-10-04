using FreshFarm.Ordering.Api.Models;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;
using Microsoft.Extensions.Configuration;

namespace FreshFarm.Ordering.Api.DesignTime;

public sealed class OrderingDesignTimeDbContextFactory : IDesignTimeDbContextFactory<FreshFarmOrderingDBContext>
{
    public FreshFarmOrderingDBContext CreateDbContext(string[] args)
    {
        var configuration = BuildConfiguration();
        var connectionString = configuration.GetConnectionString("FreshFarmOrderingDB");
        if (string.IsNullOrWhiteSpace(connectionString))
        {
            throw new InvalidOperationException("Khong tim thay ConnectionStrings:FreshFarmOrderingDB cho design-time DbContext.");
        }

        var optionsBuilder = new DbContextOptionsBuilder<FreshFarmOrderingDBContext>();
        optionsBuilder.UseSqlServer(connectionString);
        return new FreshFarmOrderingDBContext(optionsBuilder.Options);
    }

    private static IConfiguration BuildConfiguration()
    {
        var environment = Environment.GetEnvironmentVariable("ASPNETCORE_ENVIRONMENT") ?? "Development";
        return new ConfigurationBuilder()
            .SetBasePath(Directory.GetCurrentDirectory())
            .AddJsonFile("appsettings.json", optional: true)
            .AddJsonFile($"appsettings.{environment}.json", optional: true)
            .AddUserSecrets<OrderingDesignTimeDbContextFactory>(optional: true)
            .AddEnvironmentVariables()
            .Build();
    }
}
