using System.Net;
using System.Net.Http.Headers;
using Microsoft.AspNetCore.Authentication.JwtBearer;

namespace FreshFarm.Ordering.Api.Security;

public interface IIdentitySessionValidator
{
    Task<bool> IsActiveAsync(string accessToken, CancellationToken cancellationToken);
}

public sealed class IdentitySessionValidator(
    HttpClient httpClient,
    ILogger<IdentitySessionValidator> logger) : IIdentitySessionValidator
{
    public async Task<bool> IsActiveAsync(string accessToken, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(accessToken))
        {
            return false;
        }

        using var request = new HttpRequestMessage(HttpMethod.Get, "/auth/session-status");
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", accessToken);

        try
        {
            using var response = await httpClient.SendAsync(
                request,
                HttpCompletionOption.ResponseHeadersRead,
                cancellationToken);
            return response.StatusCode == HttpStatusCode.OK;
        }
        catch (TaskCanceledException exception) when (!cancellationToken.IsCancellationRequested)
        {
            logger.LogWarning(exception, "Identity session validation timed out; rejecting the access token.");
            return false;
        }
        catch (HttpRequestException exception)
        {
            logger.LogWarning(exception, "Identity session validation failed; rejecting the access token.");
            return false;
        }
    }
}

public static class IdentitySessionJwtValidation
{
    public static async Task ValidateAsync(TokenValidatedContext context)
    {
        var authorization = context.Request.Headers.Authorization.ToString();
        if (!AuthenticationHeaderValue.TryParse(authorization, out var header)
            || !string.Equals(header.Scheme, "Bearer", StringComparison.OrdinalIgnoreCase)
            || string.IsNullOrWhiteSpace(header.Parameter))
        {
            context.Fail("A bearer access token is required for live session validation.");
            return;
        }

        var validator = context.HttpContext.RequestServices.GetRequiredService<IIdentitySessionValidator>();
        if (!await validator.IsActiveAsync(header.Parameter, context.HttpContext.RequestAborted))
        {
            context.Fail("The access token is no longer active.");
        }
    }
}
