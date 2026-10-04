using FreshFarm.Catalog.Api.Controllers;
using FreshFarm.Catalog.Api.Options;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;
using Xunit;

namespace FreshFarm.Catalog.Api.Tests;

public sealed class InternalInventorySecurityTests
{
    [Fact]
    public async Task StartupValidation_Fails_WhenInternalServiceKeyIsMissing()
    {
        using var host = BuildOptionsHost(string.Empty);

        var exception = await Assert.ThrowsAsync<OptionsValidationException>(
            () => host.StartAsync());

        Assert.Contains("Services:Internal:ServiceKey", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task StartupValidation_AllowsConfiguredInternalServiceKey()
    {
        using var host = BuildOptionsHost("test-internal-key");

        await host.StartAsync();
        await host.StopAsync();
    }

    [Fact]
    public async Task Reserve_FailsClosedBeforeDatabaseAccess_WhenInternalServiceKeyIsMissing()
    {
        var controller = CreateController(new InternalInventoryOptions());

        var result = await controller.Reserve(
            new InternalInventoryReservationsController.InventoryReservationMutationRequest(),
            CancellationToken.None);

        var objectResult = Assert.IsType<ObjectResult>(result);
        Assert.Equal(StatusCodes.Status500InternalServerError, objectResult.StatusCode);
    }

    [Fact]
    public async Task Reserve_RejectsWrongKey_WhenInternalServiceKeyIsConfigured()
    {
        var controller = CreateController(new InternalInventoryOptions
        {
            ServiceKey = "expected-key"
        });
        controller.HttpContext.Request.Headers["X-Service-Key"] = "wrong-key";

        var result = await controller.Reserve(
            new InternalInventoryReservationsController.InventoryReservationMutationRequest(),
            CancellationToken.None);

        Assert.IsType<UnauthorizedObjectResult>(result);
    }

    [Fact]
    public async Task Reserve_ReachesRequestValidation_WhenInternalServiceKeyMatches()
    {
        var controller = CreateController(new InternalInventoryOptions
        {
            ServiceKey = "expected-key"
        });
        controller.HttpContext.Request.Headers["X-Service-Key"] = "expected-key";

        var result = await controller.Reserve(
            new InternalInventoryReservationsController.InventoryReservationMutationRequest(),
            CancellationToken.None);

        Assert.IsType<BadRequestObjectResult>(result);
    }

    private static IHost BuildOptionsHost(string serviceKey)
    {
        var builder = Host.CreateApplicationBuilder(new HostApplicationBuilderSettings
        {
            ApplicationName = typeof(InternalInventorySecurityTests).Assembly.GetName().Name,
            EnvironmentName = Environments.Production
        });
        builder.Configuration.Sources.Clear();
        builder.Configuration.AddInMemoryCollection(new Dictionary<string, string?>
        {
            [$"{InternalInventoryOptions.SectionName}:ServiceKey"] = serviceKey
        });
        builder.Services.AddInternalInventoryOptions(builder.Configuration);
        return builder.Build();
    }

    private static InternalInventoryReservationsController CreateController(
        InternalInventoryOptions options)
        => new(
            null!,
            Microsoft.Extensions.Options.Options.Create(options))
        {
            ControllerContext = new ControllerContext
            {
                HttpContext = new DefaultHttpContext()
            }
        };
}
