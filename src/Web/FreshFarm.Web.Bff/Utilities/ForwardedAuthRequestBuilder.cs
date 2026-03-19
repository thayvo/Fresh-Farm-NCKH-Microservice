using System.Net.Http;
using System.Net.Http.Json;
using Microsoft.AspNetCore.Http;

namespace FreshFarm.Web.Bff.Utilities;

public static class ForwardedAuthRequestBuilder
{
    public static HttpRequestMessage CreateForwardedJsonRequest(
        HttpContext httpContext,
        HttpMethod method,
        string requestUri,
        object? payload)
    {
        var request = new HttpRequestMessage(method, requestUri);
        if (payload is not null)
        {
            request.Content = JsonContent.Create(payload);
        }

        CopyHeaders(httpContext, request);
        return request;
    }

    private static void CopyHeaders(HttpContext httpContext, HttpRequestMessage request)
    {
        var incoming = httpContext.Request;

        if (incoming.Headers.TryGetValue("User-Agent", out var userAgent) && !string.IsNullOrWhiteSpace(userAgent))
        {
            request.Headers.TryAddWithoutValidation("User-Agent", userAgent.ToString());
        }

        var forwardedFor = incoming.Headers.TryGetValue("X-Forwarded-For", out var existingForwardedFor)
            ? existingForwardedFor.ToString()
            : string.Empty;

        var remoteIp = httpContext.Connection.RemoteIpAddress?.ToString();
        var effectiveForwardedFor = string.IsNullOrWhiteSpace(forwardedFor)
            ? remoteIp
            : string.IsNullOrWhiteSpace(remoteIp)
                ? forwardedFor
                : $"{forwardedFor}, {remoteIp}";

        if (!string.IsNullOrWhiteSpace(effectiveForwardedFor))
        {
            request.Headers.TryAddWithoutValidation("X-Forwarded-For", effectiveForwardedFor);
        }

        if (incoming.Headers.TryGetValue("X-Forwarded-Proto", out var forwardedProto) && !string.IsNullOrWhiteSpace(forwardedProto))
        {
            request.Headers.TryAddWithoutValidation("X-Forwarded-Proto", forwardedProto.ToString());
        }
        else if (!string.IsNullOrWhiteSpace(incoming.Scheme))
        {
            request.Headers.TryAddWithoutValidation("X-Forwarded-Proto", incoming.Scheme);
        }

        if (incoming.Headers.TryGetValue("X-Forwarded-Host", out var forwardedHost) && !string.IsNullOrWhiteSpace(forwardedHost))
        {
            request.Headers.TryAddWithoutValidation("X-Forwarded-Host", forwardedHost.ToString());
        }
        else if (incoming.Host.HasValue)
        {
            request.Headers.TryAddWithoutValidation("X-Forwarded-Host", incoming.Host.Value);
        }
    }
}
