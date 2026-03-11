using System.IO;
using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.DependencyInjection;

namespace FreshFarm.Web.Bff.Areas.Seller.Infrastructure;

public abstract class LegacySellerControllerBase : Controller
{
    protected LegacySessionAdapter Session => new(HttpContext.Session);

    protected LegacyServerUtility Server => new(HttpContext);

    protected string? GetAccessToken(string sessionKey = "ACCESS_TOKEN")
    {
        var currentUserId = GetCurrentPrincipalUserId();
        if (!currentUserId.HasValue)
        {
            HttpContext.Session.Remove(sessionKey);
            return null;
        }

        var claimToken = User.FindFirst("ff_access_token")?.Value
                         ?? User.FindFirst("access_token")?.Value;
        if (!string.IsNullOrWhiteSpace(claimToken) && IsTokenOwnedByUser(claimToken, currentUserId.Value))
        {
            HttpContext.Session.SetString(sessionKey, claimToken);
            return claimToken;
        }

        var sessionToken = HttpContext.Session.GetString(sessionKey);
        if (!string.IsNullOrWhiteSpace(sessionToken) && IsTokenOwnedByUser(sessionToken, currentUserId.Value))
        {
            return sessionToken;
        }

        HttpContext.Session.Remove(sessionKey);
        return null;
    }

    protected int? GetCurrentPrincipalUserId()
    {
        var rawUserId = User.FindFirstValue(ClaimTypes.NameIdentifier)
                        ?? User.FindFirstValue(JwtRegisteredClaimNames.Sub)
                        ?? User.FindFirstValue("sub");

        return int.TryParse(rawUserId, out var userId) ? userId : null;
    }

    private static int? TryReadUserIdFromJwt(string token)
    {
        try
        {
            var jwt = new JwtSecurityTokenHandler().ReadJwtToken(token);
            var rawUserId = jwt.Subject
                            ?? jwt.Claims.FirstOrDefault(c => c.Type == ClaimTypes.NameIdentifier)?.Value
                            ?? jwt.Claims.FirstOrDefault(c => c.Type == JwtRegisteredClaimNames.Sub)?.Value
                            ?? jwt.Claims.FirstOrDefault(c => c.Type == "sub")?.Value;

            return int.TryParse(rawUserId, out var userId) ? userId : null;
        }
        catch
        {
            return null;
        }
    }

    private static bool IsTokenOwnedByUser(string token, int currentUserId)
    {
        var tokenUserId = TryReadUserIdFromJwt(token);
        return tokenUserId.HasValue && tokenUserId.Value == currentUserId;
    }

    protected NotFoundResult HttpNotFound()
    {
        return NotFound();
    }

    protected NotFoundObjectResult HttpNotFound(string message)
    {
        return NotFound(message);
    }
}

public sealed class LegacySessionAdapter
{
    private readonly ISession _session;

    public LegacySessionAdapter(ISession session)
    {
        _session = session;
    }

    public object? this[string key]
    {
        get
        {
            var intValue = _session.GetInt32(key);
            if (intValue.HasValue)
            {
                return intValue.Value;
            }

            return _session.GetString(key);
        }
        set
        {
            switch (value)
            {
                case null:
                    _session.Remove(key);
                    break;
                case int intValue:
                    _session.SetInt32(key, intValue);
                    break;
                default:
                    _session.SetString(key, value.ToString() ?? string.Empty);
                    break;
            }
        }
    }

    public void Remove(string key)
    {
        _session.Remove(key);
    }

    public void Clear()
    {
        _session.Clear();
    }

    public void Abandon()
    {
        _session.Clear();
    }
}

public sealed class LegacyServerUtility
{
    private readonly HttpContext _httpContext;

    public LegacyServerUtility(HttpContext httpContext)
    {
        _httpContext = httpContext;
    }

    public string MapPath(string virtualPath)
    {
        var environment = _httpContext.RequestServices.GetRequiredService<IWebHostEnvironment>();
        var webRoot = environment.WebRootPath ?? environment.ContentRootPath;

        var trimmed = virtualPath.Trim();
        if (trimmed.StartsWith("~/"))
        {
            trimmed = trimmed[2..];
        }
        else if (trimmed.StartsWith("~"))
        {
            trimmed = trimmed[1..];
        }

        trimmed = trimmed.TrimStart('/', '\\')
                         .Replace('/', Path.DirectorySeparatorChar)
                         .Replace('\\', Path.DirectorySeparatorChar);

        return Path.Combine(webRoot, trimmed);
    }
}
