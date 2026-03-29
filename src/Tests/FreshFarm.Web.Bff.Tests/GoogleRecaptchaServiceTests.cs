using System.Net;
using FreshFarm.Web.Bff.Options;
using FreshFarm.Web.Bff.Services;
using Microsoft.Extensions.FileProviders;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Xunit;

namespace FreshFarm.Web.Bff.Tests;

public sealed class GoogleRecaptchaServiceTests
{
    [Fact]
    public async Task VerifyAsync_ReturnsSuccess_WhenDevelopmentBypassIsEnabled()
    {
        var service = new GoogleRecaptchaService(
            new HttpClient(new ThrowingHttpMessageHandler()),
            Microsoft.Extensions.Options.Options.Create(new GoogleRecaptchaOptions
            {
                AllowDevelopmentBypass = true,
                DevelopmentBypassToken = "dev-bypass-token",
                SellerApplicationAction = "become_seller"
            }),
            new FakeHostEnvironment { EnvironmentName = Environments.Development },
            NullLogger<GoogleRecaptchaService>.Instance);

        var result = await service.VerifyAsync("dev-bypass-token", "become_seller", remoteIp: null);

        Assert.True(result.Success);
        Assert.Equal(1.0m, result.Score);
        Assert.Equal("become_seller", result.Action);
    }

    [Fact]
    public async Task VerifyAsync_DoesNotAllowDevelopmentBypassOutsideDevelopment()
    {
        var service = new GoogleRecaptchaService(
            new HttpClient(new ThrowingHttpMessageHandler()),
            Microsoft.Extensions.Options.Options.Create(new GoogleRecaptchaOptions
            {
                AllowDevelopmentBypass = true,
                DevelopmentBypassToken = "dev-bypass-token"
            }),
            new FakeHostEnvironment { EnvironmentName = Environments.Production },
            NullLogger<GoogleRecaptchaService>.Instance);

        var result = await service.VerifyAsync("dev-bypass-token", "become_seller", remoteIp: null);

        Assert.False(result.Success);
        Assert.Equal("Google reCAPTCHA chưa được cấu hình.", result.ErrorMessage);
    }

    private sealed class ThrowingHttpMessageHandler : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            throw new Xunit.Sdk.XunitException("HTTP verify should not be called in these tests.");
        }
    }

    private sealed class FakeHostEnvironment : IHostEnvironment
    {
        public string EnvironmentName { get; set; } = Environments.Production;

        public string ApplicationName { get; set; } = "FreshFarm.Web.Bff.Tests";

        public string ContentRootPath { get; set; } = AppContext.BaseDirectory;

        public IFileProvider ContentRootFileProvider { get; set; } = new NullFileProvider();
    }
}
