using System.IO;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.DependencyInjection;

namespace FreshFarm.Web.Bff.Areas.Seller.Infrastructure;

public abstract class LegacySellerControllerBase : Controller
{
    protected LegacySessionAdapter Session => new(HttpContext.Session);

    protected LegacyServerUtility Server => new(HttpContext);

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
