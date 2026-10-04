using System.Net;
using System.Net.Http.Headers;
using FreshFarm.Catalog.Api.Security;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace FreshFarm.Catalog.Api.Tests;

public sealed class IdentitySessionValidationTests
{
    [Fact]
    public async Task Validator_accepts_active_session_and_forwards_same_bearer_token()
    {
        HttpRequestMessage? observedRequest = null;
        var handler = new StubHttpMessageHandler(request =>
        {
            observedRequest = request;
            return new HttpResponseMessage(HttpStatusCode.OK);
        });
        var client = new HttpClient(handler) { BaseAddress = new Uri("https://identity.test") };
        var validator = new IdentitySessionValidator(client, NullLogger<IdentitySessionValidator>.Instance);

        var active = await validator.IsActiveAsync("live-token", CancellationToken.None);

        Assert.True(active);
        Assert.Equal(new Uri("https://identity.test/auth/session-status"), observedRequest?.RequestUri);
        Assert.Equal(new AuthenticationHeaderValue("Bearer", "live-token"), observedRequest?.Headers.Authorization);
    }

    [Theory]
    [InlineData(HttpStatusCode.Unauthorized)]
    [InlineData(HttpStatusCode.Forbidden)]
    [InlineData(HttpStatusCode.ServiceUnavailable)]
    public async Task Validator_rejects_every_non_ok_identity_response(HttpStatusCode statusCode)
    {
        var handler = new StubHttpMessageHandler(_ => new HttpResponseMessage(statusCode));
        var client = new HttpClient(handler) { BaseAddress = new Uri("https://identity.test") };
        var validator = new IdentitySessionValidator(client, NullLogger<IdentitySessionValidator>.Instance);

        var active = await validator.IsActiveAsync("revoked-token", CancellationToken.None);

        Assert.False(active);
    }

    [Fact]
    public async Task Validator_fails_closed_when_identity_is_unreachable()
    {
        var handler = new StubHttpMessageHandler(_ => throw new HttpRequestException("offline"));
        var client = new HttpClient(handler) { BaseAddress = new Uri("https://identity.test") };
        var validator = new IdentitySessionValidator(client, NullLogger<IdentitySessionValidator>.Instance);

        var active = await validator.IsActiveAsync("otherwise-valid-token", CancellationToken.None);

        Assert.False(active);
    }

    [Fact]
    public async Task Jwt_event_preserves_active_token_but_fails_revoked_token()
    {
        var activeContext = CreateTokenValidatedContext(new StubSessionValidator(true), "Bearer active-token");
        await IdentitySessionJwtValidation.ValidateAsync(activeContext);

        var revokedContext = CreateTokenValidatedContext(new StubSessionValidator(false), "Bearer revoked-token");
        await IdentitySessionJwtValidation.ValidateAsync(revokedContext);

        Assert.Null(activeContext.Result);
        Assert.NotNull(revokedContext.Result?.Failure);
    }

    private static TokenValidatedContext CreateTokenValidatedContext(
        IIdentitySessionValidator validator,
        string authorization)
    {
        var services = new ServiceCollection()
            .AddSingleton(validator)
            .BuildServiceProvider();
        var httpContext = new DefaultHttpContext { RequestServices = services };
        httpContext.Request.Headers.Authorization = authorization;
        return new TokenValidatedContext(
            httpContext,
            new AuthenticationScheme(JwtBearerDefaults.AuthenticationScheme, null, typeof(JwtBearerHandler)),
            new JwtBearerOptions());
    }

    private sealed class StubSessionValidator(bool active) : IIdentitySessionValidator
    {
        public Task<bool> IsActiveAsync(string accessToken, CancellationToken cancellationToken) =>
            Task.FromResult(active);
    }

    private sealed class StubHttpMessageHandler(
        Func<HttpRequestMessage, HttpResponseMessage> responseFactory) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken) =>
            Task.FromResult(responseFactory(request));
    }
}
