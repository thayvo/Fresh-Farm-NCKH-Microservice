using System.Net;
using System.Net.Http.Json;
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
    public async Task VerifyAsync_FailsClosedWithoutCallingHttp_WhenVerificationEndpointIsNotHttps()
    {
        var handler = new RecordingHttpMessageHandler(_ =>
            throw new Xunit.Sdk.XunitException("HTTP verification must not use a non-HTTPS endpoint."));
        var service = new GoogleRecaptchaService(
            new HttpClient(handler),
            Microsoft.Extensions.Options.Options.Create(new GoogleRecaptchaOptions
            {
                SiteKey = "site-key",
                SecretKey = "secret-key",
                VerificationEndpoint = "http://recaptcha.example.test/siteverify",
                AllowedHostnames = ["app.example.com"]
            }),
            new FakeHostEnvironment { EnvironmentName = Environments.Production },
            NullLogger<GoogleRecaptchaService>.Instance);

        var result = await service.VerifyAsync("token", "signup", remoteIp: null);

        Assert.False(result.Success);
        Assert.Null(handler.LastRequestUri);
    }

    [Fact]
    public async Task VerifyAsync_ReturnsSuccess_WhenActionScoreAndHostnameMatch()
    {
        var handler = new RecordingHttpMessageHandler(_ => new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = JsonContent.Create(new
            {
                success = true,
                score = 0.9m,
                action = "signup",
                hostname = "APP.EXAMPLE.COM."
            })
        });
        var service = CreateConfiguredService(handler);

        var result = await service.VerifyAsync("valid-token", "signup", "203.0.113.10");

        Assert.True(result.Success);
        Assert.Equal("signup", result.Action);
        Assert.Equal(0.9m, result.Score);
        Assert.Equal("https://www.google.com/recaptcha/api/siteverify", handler.LastRequestUri?.ToString());
    }

    [Fact]
    public async Task VerifyAsync_FailsClosedWithoutCallingHttp_WhenExpectedActionIsEmpty()
    {
        var handler = new RecordingHttpMessageHandler(_ =>
            throw new Xunit.Sdk.XunitException("Verification must stop before HTTP when the expected action is empty."));
        var service = CreateConfiguredService(handler);

        var result = await service.VerifyAsync("valid-token", "   ", remoteIp: null);

        Assert.False(result.Success);
        Assert.Null(handler.LastRequestUri);
        Assert.Contains("action", result.ErrorMessage, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task VerifyAsync_RejectsSuccessfulPayload_WhenActionDoesNotMatch()
    {
        var handler = new RecordingHttpMessageHandler(_ => new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = JsonContent.Create(new
            {
                success = true,
                score = 0.9m,
                action = "forgot_password",
                hostname = "app.example.com"
            })
        });
        var service = CreateConfiguredService(handler);

        var result = await service.VerifyAsync("valid-token", "signup", remoteIp: null);

        Assert.False(result.Success);
        Assert.Equal("forgot_password", result.Action);
        Assert.Contains("không hợp lệ", result.ErrorMessage, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task VerifyAsync_RejectsSuccessfulPayload_WhenHostnameIsNotAllowed()
    {
        var handler = new RecordingHttpMessageHandler(_ => new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = JsonContent.Create(new
            {
                success = true,
                score = 0.9m,
                action = "signup",
                hostname = "evil.example.net"
            })
        });
        var service = CreateConfiguredService(handler);

        var result = await service.VerifyAsync("valid-token", "signup", remoteIp: null);

        Assert.False(result.Success);
        Assert.Contains("nguồn", result.ErrorMessage, StringComparison.OrdinalIgnoreCase);
    }

    private static GoogleRecaptchaService CreateConfiguredService(HttpMessageHandler handler)
        => new(
            new HttpClient(handler),
            Microsoft.Extensions.Options.Options.Create(new GoogleRecaptchaOptions
            {
                SiteKey = "site-key",
                SecretKey = "secret-key",
                VerificationEndpoint = "https://www.google.com/recaptcha/api/siteverify",
                AllowedHostnames = ["app.example.com"]
            }),
            new FakeHostEnvironment { EnvironmentName = Environments.Production },
            NullLogger<GoogleRecaptchaService>.Instance);

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

    private sealed class RecordingHttpMessageHandler(Func<HttpRequestMessage, HttpResponseMessage> responder) : HttpMessageHandler
    {
        public Uri? LastRequestUri { get; private set; }

        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            LastRequestUri = request.RequestUri;
            return Task.FromResult(responder(request));
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
