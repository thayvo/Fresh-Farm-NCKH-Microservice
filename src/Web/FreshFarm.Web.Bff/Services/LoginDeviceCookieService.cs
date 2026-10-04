using Microsoft.AspNetCore.DataProtection;

namespace FreshFarm.Web.Bff.Services;

public interface ILoginDeviceCookieService
{
    string GetOrCreateDeviceId(HttpContext httpContext);
}

public sealed class LoginDeviceCookieService : ILoginDeviceCookieService
{
    public const string CookieName = "FreshFarm.Bff.Device";

    private readonly IDataProtector _protector;
    private readonly IWebHostEnvironment _environment;

    public LoginDeviceCookieService(IDataProtectionProvider dataProtectionProvider, IWebHostEnvironment environment)
    {
        _protector = dataProtectionProvider.CreateProtector("FreshFarm.Bff.LoginDevice.v1");
        _environment = environment;
    }

    public string GetOrCreateDeviceId(HttpContext httpContext)
    {
        ArgumentNullException.ThrowIfNull(httpContext);

        if (httpContext.Request.Cookies.TryGetValue(CookieName, out var protectedValue)
            && !string.IsNullOrWhiteSpace(protectedValue))
        {
            try
            {
                var existingDeviceId = _protector.Unprotect(protectedValue);
                if (IsValidDeviceId(existingDeviceId))
                {
                    return existingDeviceId;
                }
            }
            catch (System.Security.Cryptography.CryptographicException)
            {
                // A forged/expired cookie is replaced with a newly signed opaque identifier.
            }
        }

        var deviceId = Convert.ToHexString(System.Security.Cryptography.RandomNumberGenerator.GetBytes(32));
        httpContext.Response.Cookies.Append(
            CookieName,
            _protector.Protect(deviceId),
            new CookieOptions
            {
                HttpOnly = true,
                IsEssential = true,
                Secure = !_environment.IsDevelopment() || httpContext.Request.IsHttps,
                SameSite = SameSiteMode.Lax,
                Path = "/",
                MaxAge = TimeSpan.FromDays(365)
            });

        return deviceId;
    }

    private static bool IsValidDeviceId(string? value)
        => value?.Length == 64 && value.All(Uri.IsHexDigit);
}
