using FreshFarm.Identity.Api.Models;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;
using Microsoft.Extensions.Configuration;

namespace FreshFarm.Identity.Api.DesignTime;

public sealed class IdentityDesignTimeDbContextFactory : IDesignTimeDbContextFactory<FreshFarmIdentityDBContext>
{
    public FreshFarmIdentityDBContext CreateDbContext(string[] args)
    {
        var configuration = BuildConfiguration();
        var connectionString = configuration.GetConnectionString("IdentityDB");
        if (string.IsNullOrWhiteSpace(connectionString))
        {
            throw new InvalidOperationException("Khong tim thay ConnectionStrings:IdentityDB cho design-time DbContext.");
        }

        var optionsBuilder = new DbContextOptionsBuilder<FreshFarmIdentityDBContext>();
        optionsBuilder.UseSqlServer(connectionString);
        return new FreshFarmIdentityDBContext(optionsBuilder.Options);
    }

    private static IConfiguration BuildConfiguration()
    {
        var environment = Environment.GetEnvironmentVariable("ASPNETCORE_ENVIRONMENT") ?? "Development";
        return new ConfigurationBuilder()
            .SetBasePath(Directory.GetCurrentDirectory())
            .AddJsonFile("appsettings.json", optional: true)
            .AddJsonFile($"appsettings.{environment}.json", optional: true)
            .AddUserSecrets<IdentityDesignTimeDbContextFactory>(optional: true)
            .AddEnvironmentVariables()
            .Build();
    }
}
