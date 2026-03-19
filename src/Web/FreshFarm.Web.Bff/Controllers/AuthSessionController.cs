using FreshFarm.Web.Bff.Options;
using FreshFarm.Web.Bff.Utilities;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Options;

namespace FreshFarm.Web.Bff.Controllers;

[ApiController]
[Route("api/auth")]
[Authorize]
public sealed class AuthSessionController : ControllerBase
{
    private const string AccessTokenSessionKey = "ACCESS_TOKEN";
    private const string SessionHeartbeatKey = "AUTH_SESSION_LAST_SEEN_UTC";
    private const string AuthCookieName = "FreshFarm.Bff.Auth";
    private const string SessionCookieName = "FreshFarm.Bff.Session";

    private readonly IdleSessionOptions _idleSessionOptions;

    public AuthSessionController(IOptions<IdleSessionOptions> idleSessionOptions)
    {
        _idleSessionOptions = idleSessionOptions.Value;
    }

    [HttpPost("refresh-session")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> RefreshSession()
    {
        var loginUrl = AuthLaneResolver.ResolveLoginPath(User);
        var token = HttpContext.Session.GetString(AccessTokenSessionKey);
        if (string.IsNullOrWhiteSpace(token))
        {
            await ClearAuthenticatedSessionAsync();
            return Unauthorized(new
            {
                message = "Phiên đăng nhập đã hết hạn.",
                loginUrl
            });
        }

        HttpContext.Session.SetString(AccessTokenSessionKey, token);
        HttpContext.Session.SetString(SessionHeartbeatKey, DateTimeOffset.UtcNow.ToString("O"));

        await RefreshAuthenticationCookieAsync();

        return Ok(new
        {
            success = true,
            renewedAtUtc = DateTimeOffset.UtcNow,
            authenticationLifetimeMinutes = _idleSessionOptions.AuthenticationLifetimeMinutes,
            serverSessionIdleTimeoutMinutes = _idleSessionOptions.ServerSessionIdleTimeoutMinutes
        });
    }

    [HttpPost("logout")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Logout()
    {
        var loginUrl = AuthLaneResolver.ResolveLoginPath(User);
        await ClearAuthenticatedSessionAsync();
        return Ok(new
        {
            success = true,
            loginUrl
        });
    }

    private async Task RefreshAuthenticationCookieAsync()
    {
        var authResult = await HttpContext.AuthenticateAsync(CookieAuthenticationDefaults.AuthenticationScheme);
        if (!authResult.Succeeded || authResult.Principal is null)
        {
            return;
        }

        var authProperties = authResult.Properties ?? new AuthenticationProperties();
        authProperties.IssuedUtc = DateTimeOffset.UtcNow;
        if (authProperties.IsPersistent)
        {
            authProperties.ExpiresUtc = authProperties.IssuedUtc.Value.AddMinutes(_idleSessionOptions.AuthenticationLifetimeMinutes);
        }

        await HttpContext.SignInAsync(
            CookieAuthenticationDefaults.AuthenticationScheme,
            authResult.Principal,
            authProperties);
    }

    private async Task ClearAuthenticatedSessionAsync()
    {
        HttpContext.Session.Remove(AccessTokenSessionKey);
        HttpContext.Session.Remove(SessionHeartbeatKey);
        HttpContext.Session.Clear();

        Response.Cookies.Delete(AuthCookieName);
        Response.Cookies.Delete(SessionCookieName);
        Response.Cookies.Delete("ASP.NET_SessionId");
        Response.Cookies.Delete("ADMIN_REMEMBER");
        Response.Cookies.Delete("ADMIN_ID");

        await HttpContext.SignOutAsync(CookieAuthenticationDefaults.AuthenticationScheme);
    }
}
