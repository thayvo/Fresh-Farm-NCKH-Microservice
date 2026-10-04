using System.Net;
using System.Text.Json;
using FreshFarm.Web.Bff.Dtos;
using FreshFarm.Web.Bff.Services;
using FreshFarm.Web.Bff.Utilities;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace FreshFarm.Web.Bff.Tests;

public sealed class ForwardedAuthRequestBuilderTests
{
    [Fact]
    public async Task CreateForwardedJsonRequest_AlwaysOverwritesClientSuppliedDeviceId()
    {
        const string trustedDeviceId = "A9A8A7A6A5A4A3A2A1A0B9B8B7B6B5B4B3B2B1B0C9C8C7C6C5C4C3C2C1C0D9D8";
        var services = new ServiceCollection()
            .AddSingleton<ILoginDeviceCookieService>(new StubLoginDeviceCookieService(trustedDeviceId))
            .BuildServiceProvider();
        var context = new DefaultHttpContext
        {
            RequestServices = services
        };
        context.Connection.RemoteIpAddress = IPAddress.Loopback;
        var payload = new LoginRequestDto
        {
            Identifier = "customer@example.test",
            Password = "not-a-real-password",
            ClientLane = "Customer",
            DeviceId = "attacker-controlled-device-id"
        };

        using var request = ForwardedAuthRequestBuilder.CreateForwardedJsonRequest(
            context,
            HttpMethod.Post,
            "/auth/login",
            payload);
        Assert.NotNull(request.Content);
        var forwardedJson = await request.Content.ReadAsStringAsync();
        var forwarded = JsonSerializer.Deserialize<LoginRequestDto>(
            forwardedJson,
            new JsonSerializerOptions { PropertyNameCaseInsensitive = true });

        Assert.Equal(trustedDeviceId, payload.DeviceId);
        Assert.NotNull(forwarded);
        Assert.Equal(trustedDeviceId, forwarded.DeviceId);
        Assert.DoesNotContain("attacker-controlled-device-id", forwardedJson);
    }

    private sealed class StubLoginDeviceCookieService(string deviceId) : ILoginDeviceCookieService
    {
        public string GetOrCreateDeviceId(HttpContext httpContext) => deviceId;
    }
}
