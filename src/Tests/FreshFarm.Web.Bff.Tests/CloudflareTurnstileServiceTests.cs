using System.Net;
using System.Net.Http.Json;
using FreshFarm.Web.Bff.Options;
using FreshFarm.Web.Bff.Services;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace FreshFarm.Web.Bff.Tests;

public sealed class CloudflareTurnstileServiceTests
{
    [Fact]
    public async Task VerifyAsync_ReturnsSuccess_WhenActionAndHostnameMatch()
    {
        var handler = new RecordingHandler(_ => new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = JsonContent.Create(new
            {
                success = true,
                action = "signup",
                hostname = "APP.EXAMPLE.COM."
            })
        });
        var service = CreateService(handler);

        var result = await service.VerifyAsync(
            "valid-token",
            "signup",
            "203.0.113.10");

        Assert.True(result.Success);
        Assert.Equal(BotChallengeProvider.CloudflareTurnstile, result.Provider);
        Assert.Equal("signup", result.Action);
        Assert.Equal("APP.EXAMPLE.COM.", result.Hostname);
        Assert.Equal("https://challenges.cloudflare.test/turnstile/v0/siteverify", handler.LastRequestUri?.ToString());
        Assert.Contains("secret=test-secret", handler.LastRequestBody, StringComparison.Ordinal);
        Assert.Contains("response=valid-token", handler.LastRequestBody, StringComparison.Ordinal);
        Assert.Contains("remoteip=203.0.113.10", handler.LastRequestBody, StringComparison.Ordinal);
    }

    [Fact]
    public async Task VerifyAsync_RejectsSuccessfulPayload_WhenActionDoesNotMatch()
    {
        var handler = new RecordingHandler(_ => new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = JsonContent.Create(new
            {
                success = true,
                action = "forgot_password",
                hostname = "app.example.com"
            })
        });
        var service = CreateService(handler);

        var result = await service.VerifyAsync("valid-token", "signup", remoteIp: null);

        Assert.False(result.Success);
        Assert.Contains("mục đích", result.ErrorMessage, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task VerifyAsync_RejectsPayload_WhenCloudflareReportsFailure()
    {
        var handler = new RecordingHandler(_ => new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = JsonContent.Create(new
            {
                success = false,
                error_codes = new[] { "timeout-or-duplicate" }
            })
        });
        var service = CreateService(handler);

        var result = await service.VerifyAsync("expired-or-replayed-token", "signup", remoteIp: null);

        Assert.False(result.Success);
        Assert.Contains("hết hạn", result.ErrorMessage, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task VerifyAsync_RejectsSuccessfulPayload_WhenHostnameIsNotAllowed()
    {
        var handler = new RecordingHandler(_ => new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = JsonContent.Create(new
            {
                success = true,
                action = "signup",
                hostname = "evil.example.net"
            })
        });
        var service = CreateService(handler);

        var result = await service.VerifyAsync("valid-token", "signup", remoteIp: null);

        Assert.False(result.Success);
        Assert.Contains("nguồn", result.ErrorMessage, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task VerifyAsync_FailsClosed_WhenSiteverifyCannotBeReached()
    {
        var handler = new RecordingHandler(_ => throw new HttpRequestException("network unavailable"));
        var service = CreateService(handler);

        var result = await service.VerifyAsync("valid-token", "signup", remoteIp: null);

        Assert.False(result.Success);
        Assert.Equal(BotChallengeProvider.CloudflareTurnstile, result.Provider);
    }

    [Fact]
    public async Task VerifyAsync_FailsClosedWithoutCallingHttp_WhenConfigurationIsIncomplete()
    {
        var handler = new RecordingHandler(_ =>
            throw new Xunit.Sdk.XunitException("Siteverify must not be called with incomplete configuration."));
        var service = new CloudflareTurnstileService(
            new HttpClient(handler),
            Microsoft.Extensions.Options.Options.Create(new CloudflareTurnstileOptions
            {
                SiteKey = "site-key",
                SecretKey = "",
                AllowedHostnames = ["app.example.com"]
            }),
            NullLogger<CloudflareTurnstileService>.Instance);

        var result = await service.VerifyAsync("valid-token", "signup", remoteIp: null);

        Assert.False(result.Success);
        Assert.Null(handler.LastRequestUri);
    }

    private static CloudflareTurnstileService CreateService(HttpMessageHandler handler)
        => new(
            new HttpClient(handler),
            Microsoft.Extensions.Options.Options.Create(new CloudflareTurnstileOptions
            {
                SiteKey = "test-site-key",
                SecretKey = "test-secret",
                VerificationEndpoint = "https://challenges.cloudflare.test/turnstile/v0/siteverify",
                AllowedHostnames = ["app.example.com"]
            }),
            NullLogger<CloudflareTurnstileService>.Instance);

    private sealed class RecordingHandler(Func<HttpRequestMessage, HttpResponseMessage> responder) : HttpMessageHandler
    {
        public Uri? LastRequestUri { get; private set; }

        public string LastRequestBody { get; private set; } = string.Empty;

        protected override async Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            LastRequestUri = request.RequestUri;
            LastRequestBody = request.Content is null
                ? string.Empty
                : await request.Content.ReadAsStringAsync(cancellationToken);
            return responder(request);
        }
    }
}
