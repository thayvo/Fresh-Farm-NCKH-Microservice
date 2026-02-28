using System.IdentityModel.Tokens.Jwt;
using System.Net.Http.Json;
using System.Security.Claims;
using FreshFarm.Web.Bff.Areas.Seller.Infrastructure;
using FreshFarm.Web.Bff.Areas.Seller.Models;
using FreshFarm.Web.Bff.Dtos;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;

namespace FreshFarm.Web.Bff.Areas.Seller.Controllers;

[Authorize]
[ResponseCache(Duration = 0, Location = ResponseCacheLocation.None, NoStore = true)]
[Area("Seller")]
public class AdminAccountController : LegacySellerControllerBase
{
    private const string AccessTokenSessionKey = "ACCESS_TOKEN";

    private readonly IHttpClientFactory _httpClientFactory;

    public AdminAccountController(IHttpClientFactory httpClientFactory)
    {
        _httpClientFactory = httpClientFactory;
    }

    [HttpGet]
    [AllowAnonymous]
    public IActionResult Login(string? returnUrl)
    {
        var normalizedReturnUrl = NormalizeReturnUrl(returnUrl);
        if (User.Identity?.IsAuthenticated == true)
        {
            return RedirectToLocal(normalizedReturnUrl);
        }

        ViewBag.ReturnUrl = normalizedReturnUrl;
        return View(new AdminLoginVM());
    }

    [HttpPost]
    [AllowAnonymous]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Login(AdminLoginVM vm, string? returnUrl)
    {
        var normalizedReturnUrl = NormalizeReturnUrl(returnUrl);
        ViewBag.ReturnUrl = normalizedReturnUrl;

        if (!ModelState.IsValid)
        {
            return View(vm);
        }

        var request = new LoginRequestDto
        {
            Identifier = vm.UserName?.Trim() ?? string.Empty,
            Password = vm.Password ?? string.Empty
        };

        if (string.IsNullOrWhiteSpace(request.Identifier) || string.IsNullOrWhiteSpace(request.Password))
        {
            ModelState.AddModelError(string.Empty, "Vui long nhap day du tai khoan va mat khau.");
            return View(vm);
        }

        var identityClient = _httpClientFactory.CreateClient("Identity");
        var loginResponse = await identityClient.PostAsJsonAsync("/auth/login", request);
        if (!loginResponse.IsSuccessStatusCode)
        {
            var errorText = await loginResponse.Content.ReadAsStringAsync();
            ModelState.AddModelError(string.Empty, $"Dang nhap that bai: {errorText}");
            return View(vm);
        }

        var auth = await loginResponse.Content.ReadFromJsonAsync<AuthResponseDto>();
        if (auth is null || string.IsNullOrWhiteSpace(auth.AccessToken))
        {
            ModelState.AddModelError(string.Empty, "Dang nhap that bai: token khong hop le.");
            return View(vm);
        }

        var jwt = new JwtSecurityTokenHandler().ReadJwtToken(auth.AccessToken);
        var roleValues = jwt.Claims
            .Where(c => c.Type == ClaimTypes.Role || c.Type == "role")
            .Select(c => c.Value)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();

        if (!roleValues.Contains("Seller", StringComparer.OrdinalIgnoreCase))
        {
            ModelState.AddModelError(string.Empty, "Tai khoan khong co quyen Seller.");
            return View(vm);
        }

        HttpContext.Session.SetString(AccessTokenSessionKey, auth.AccessToken);

        var userIdText = jwt.Subject ?? "0";
        var userName = jwt.Claims.FirstOrDefault(c => c.Type == "username")?.Value ?? request.Identifier;

        if (!int.TryParse(userIdText, out var adminId))
        {
            adminId = 0;
        }

        Session["ADMIN_ID"] = adminId;
        Session["ADMIN_NAME"] = userName;
        Session["ADMIN_ROLEID"] = 0;

        var adminCookieOptions = new CookieOptions
        {
            HttpOnly = true,
            Secure = Request.IsHttps,
            SameSite = SameSiteMode.Lax,
            Expires = DateTimeOffset.UtcNow.AddDays(7)
        };
        Response.Cookies.Append("ADMIN_ID", adminId.ToString(), adminCookieOptions);

        if (vm.RememberMe)
        {
            var rememberOptions = new CookieOptions
            {
                HttpOnly = true,
                Secure = Request.IsHttps,
                SameSite = SameSiteMode.Lax,
                Expires = DateTimeOffset.UtcNow.AddDays(7)
            };
            Response.Cookies.Append("ADMIN_REMEMBER", userName, rememberOptions);
        }
        else
        {
            Response.Cookies.Delete("ADMIN_REMEMBER");
        }

        var claims = new List<Claim>
        {
            new(ClaimTypes.NameIdentifier, userIdText),
            new(ClaimTypes.Name, userName)
        };

        foreach (var role in roleValues)
        {
            claims.Add(new Claim(ClaimTypes.Role, role));
        }

        var email = jwt.Claims.FirstOrDefault(c => c.Type == JwtRegisteredClaimNames.Email || c.Type == "email")?.Value;
        if (!string.IsNullOrWhiteSpace(email))
        {
            claims.Add(new Claim(ClaimTypes.Email, email));
        }

        var identity = new ClaimsIdentity(claims, CookieAuthenticationDefaults.AuthenticationScheme);
        var principal = new ClaimsPrincipal(identity);
        var authProperties = new AuthenticationProperties
        {
            IsPersistent = vm.RememberMe
        };

        if (vm.RememberMe)
        {
            authProperties.ExpiresUtc = DateTimeOffset.UtcNow.AddDays(7);
        }

        await HttpContext.SignInAsync(CookieAuthenticationDefaults.AuthenticationScheme, principal, authProperties);

        return RedirectToLocal(normalizedReturnUrl);
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Logout()
    {
        Session.Clear();
        Session.Abandon();
        HttpContext.Session.Remove(AccessTokenSessionKey);

        Response.Cookies.Delete("ASP.NET_SessionId");
        Response.Cookies.Delete("ADMIN_REMEMBER");
        Response.Cookies.Delete("ADMIN_ID");

        await HttpContext.SignOutAsync(CookieAuthenticationDefaults.AuthenticationScheme);

        Response.Headers["Cache-Control"] = "no-store, no-cache, max-age=0";
        Response.Headers["Pragma"] = "no-cache";
        Response.Headers["Expires"] = "0";

        return RedirectToAction("Login", "AdminAccount", new { area = "Seller" });
    }

    private string? NormalizeReturnUrl(string? returnUrl)
    {
        if (string.IsNullOrWhiteSpace(returnUrl))
        {
            return null;
        }

        return Url.IsLocalUrl(returnUrl) ? returnUrl : null;
    }

    private IActionResult RedirectToLocal(string? returnUrl)
    {
        if (!string.IsNullOrWhiteSpace(returnUrl) && Url.IsLocalUrl(returnUrl))
        {
            return Redirect(returnUrl);
        }

        return RedirectToAction("Dashboard", "Home", new { area = "Seller" });
    }
}
