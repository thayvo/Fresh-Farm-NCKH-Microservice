using System.IdentityModel.Tokens.Jwt;
using System.Net.Http.Json;
using System.Security.Claims;
using System.Text.Json;
using FreshFarm.Web.Bff.Areas.Seller.Infrastructure;
using FreshFarm.Web.Bff.Areas.Seller.Models;
using FreshFarm.Web.Bff.Dtos;
using FreshFarm.Web.Bff.Utilities;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;

namespace FreshFarm.Web.Bff.Areas.Seller.Controllers;

[Authorize]
[ResponseCache(Duration = 0, Location = ResponseCacheLocation.None, NoStore = true)]
[Area("Seller")]
public class SellerAccountController : LegacySellerControllerBase
{
    private const string AccessTokenSessionKey = "ACCESS_TOKEN";
    private const string TwoFactorChallengeSessionKey = "SELLER_2FA_CHALLENGE";

    private readonly IHttpClientFactory _httpClientFactory;

    public SellerAccountController(IHttpClientFactory httpClientFactory)
    {
        _httpClientFactory = httpClientFactory;
    }

    [HttpGet]
    [AllowAnonymous]
    public IActionResult Login(string? returnUrl, bool rateLimitError = false, string? retryAfter = null)
    {
        var normalizedReturnUrl = NormalizeReturnUrl(returnUrl);
        ClearTwoFactorChallenge();

        if (User.Identity?.IsAuthenticated == true)
        {
            if (User.IsInRole("Seller") || User.IsInRole("Admin"))
            {
                return RedirectToLocal(normalizedReturnUrl);
            }

            ModelState.AddModelError(string.Empty, "Tài khoản hiện tại không có quyền Nhà bán. Vui lòng đăng nhập bằng tài khoản Seller.");
        }

        ViewBag.ReturnUrl = normalizedReturnUrl;
        ViewBag.RateLimitErrorMessage = rateLimitError
            ? BuildRateLimitMessage(retryAfter)
            : null;
        return View(new SellerLoginViewModel());
    }

    [HttpPost]
    [AllowAnonymous]
    [ValidateAntiForgeryToken]
    [EnableRateLimiting("auth-form")]
    public async Task<IActionResult> Login(SellerLoginViewModel vm, string? returnUrl)
    {
        return await HandleLoginAsync(vm, returnUrl);
    }

    [HttpGet("/Seller/AdminAccount/Login")]
    [AllowAnonymous]
    public IActionResult LegacyLogin(string? returnUrl)
    {
        return Login(returnUrl);
    }

    [HttpPost("/Seller/AdminAccount/Login")]
    [AllowAnonymous]
    [ValidateAntiForgeryToken]
    [EnableRateLimiting("auth-form")]
    public async Task<IActionResult> LegacyLoginPost(SellerLoginViewModel vm, string? returnUrl)
    {
        return await HandleLoginAsync(vm, returnUrl);
    }

    [HttpGet]
    [AllowAnonymous]
    public IActionResult TwoFactor()
    {
        var challenge = ReadTwoFactorChallenge();
        if (challenge is null)
        {
            return RedirectToAction(nameof(Login), new { area = "Seller" });
        }

        return View(BuildTwoFactorViewModel(challenge));
    }

    [HttpPost]
    [AllowAnonymous]
    [ValidateAntiForgeryToken]
    [EnableRateLimiting("auth-form")]
    public async Task<IActionResult> TwoFactor(SellerTwoFactorViewModel vm)
    {
        var challenge = ReadTwoFactorChallenge();
        if (challenge is null)
        {
            return RedirectToAction(nameof(Login), new { area = "Seller" });
        }

        if (!ModelState.IsValid)
        {
            return View(BuildTwoFactorViewModel(challenge, vm.Code));
        }

        var identityClient = _httpClientFactory.CreateClient("Identity");
        using var verifyHttpRequest = ForwardedAuthRequestBuilder.CreateForwardedJsonRequest(
            HttpContext,
            System.Net.Http.HttpMethod.Post,
            "/auth/login/2fa",
            new VerifyTwoFactorLoginRequestDto
            {
                Ticket = challenge.Ticket,
                Code = vm.Code?.Trim() ?? string.Empty
            });
        var response = await identityClient.SendAsync(verifyHttpRequest);

        if (!response.IsSuccessStatusCode)
        {
            var errorText = await ApiErrorMessageParser.ReadMessageAsync(response, "Xác thực 2 bước chưa thành công");
            ModelState.AddModelError(string.Empty, errorText);
            return View(BuildTwoFactorViewModel(challenge, vm.Code));
        }

        var auth = await response.Content.ReadFromJsonAsync<AuthResponseDto>();
        if (auth is null || string.IsNullOrWhiteSpace(auth.AccessToken))
        {
            ModelState.AddModelError(string.Empty, "Token xác thực không hợp lệ.");
            return View(BuildTwoFactorViewModel(challenge, vm.Code));
        }

        ClearTwoFactorChallenge();
        return await CompleteSellerSignInAsync(auth, challenge.RememberMe, challenge.ReturnUrl);
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Logout()
    {
        return await HandleLogoutAsync();
    }

    [HttpPost("/Seller/AdminAccount/Logout")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> LegacyLogout()
    {
        return await HandleLogoutAsync();
    }

    private async Task<IActionResult> HandleLoginAsync(SellerLoginViewModel vm, string? returnUrl)
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
            Password = vm.Password ?? string.Empty,
            ClientLane = "Seller"
        };

        if (string.IsNullOrWhiteSpace(request.Identifier) || string.IsNullOrWhiteSpace(request.Password))
        {
            ModelState.AddModelError(string.Empty, "Vui lòng nhập đầy đủ tài khoản và mật khẩu.");
            return View(vm);
        }

        var identityClient = _httpClientFactory.CreateClient("Identity");
        using var loginHttpRequest = FreshFarm.Web.Bff.Utilities.ForwardedAuthRequestBuilder.CreateForwardedJsonRequest(
            HttpContext,
            System.Net.Http.HttpMethod.Post,
            "/auth/login",
            request);
        var loginResponse = await identityClient.SendAsync(loginHttpRequest);
        if (!loginResponse.IsSuccessStatusCode)
        {
            var errorText = await ApiErrorMessageParser.ReadMessageAsync(loginResponse, "Đăng nhập chưa thành công");
            ModelState.AddModelError(string.Empty, errorText);
            return View(vm);
        }

        var auth = await loginResponse.Content.ReadFromJsonAsync<AuthResponseDto>();
        if (auth is null)
        {
            ModelState.AddModelError(string.Empty, "Phản hồi xác thực không hợp lệ.");
            return View(vm);
        }

        if (auth.RequiresTwoFactor)
        {
            SaveTwoFactorChallenge(new TwoFactorChallengeStateDto
            {
                Ticket = auth.TwoFactorTicket ?? string.Empty,
                RememberMe = vm.RememberMe,
                ReturnUrl = normalizedReturnUrl,
                RequiresSetup = auth.RequiresTwoFactorSetup,
                ManualEntryKey = auth.ManualEntryKey,
                OtpAuthUri = auth.OtpAuthUri,
                AuthenticatorIssuer = auth.AuthenticatorIssuer,
                AuthenticatorAccountName = auth.AuthenticatorAccountName,
                ChallengeMessage = auth.ChallengeMessage
            });

            return RedirectToAction(nameof(TwoFactor), new { area = "Seller" });
        }

        if (string.IsNullOrWhiteSpace(auth.AccessToken))
        {
            ModelState.AddModelError(string.Empty, "Token xác thực không hợp lệ.");
            return View(vm);
        }

        return await CompleteSellerSignInAsync(auth, vm.RememberMe, normalizedReturnUrl);
    }

    private async Task<IActionResult> CompleteSellerSignInAsync(AuthResponseDto auth, bool rememberMe, string? returnUrl)
    {
        var jwt = new JwtSecurityTokenHandler().ReadJwtToken(auth.AccessToken);
        var roleValues = jwt.Claims
            .Where(c => c.Type == ClaimTypes.Role || c.Type == "role")
            .Select(c => c.Value)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();

        var hasSellerRole = roleValues.Contains("Seller", StringComparer.OrdinalIgnoreCase);
        if (!hasSellerRole)
        {
            TempData["ErrorMessage"] = "Bạn không có quyền truy cập khu vực nhà bán hàng.";
            return RedirectToAction(nameof(Login), new { area = "Seller", returnUrl });
        }

        await HttpContext.SignOutAsync(CookieAuthenticationDefaults.AuthenticationScheme);
        HttpContext.Session.Remove(AccessTokenSessionKey);
        Session.Remove("ADMIN_ID");
        Session.Remove("ADMIN_NAME");
        Session.Remove("ADMIN_ROLEID");
        Response.Cookies.Delete("ADMIN_ID");

        HttpContext.Session.SetString(AccessTokenSessionKey, auth.AccessToken);

        var userIdText = jwt.Subject ?? "0";
        var userName = jwt.Claims.FirstOrDefault(c => c.Type == "username")?.Value ?? "seller";

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

        if (rememberMe)
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
            new(JwtRegisteredClaimNames.Sub, userIdText),
            new("sub", userIdText),
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
            IsPersistent = rememberMe
        };

        if (auth.ExpiredAtUtc > DateTime.UtcNow)
        {
            authProperties.ExpiresUtc = new DateTimeOffset(auth.ExpiredAtUtc);
        }
        else if (rememberMe)
        {
            authProperties.ExpiresUtc = DateTimeOffset.UtcNow.AddDays(7);
        }

        await HttpContext.SignInAsync(CookieAuthenticationDefaults.AuthenticationScheme, principal, authProperties);

        if (!string.IsNullOrWhiteSpace(returnUrl) && Url.IsLocalUrl(returnUrl))
        {
            return Redirect(returnUrl);
        }

        return RedirectToAction("Dashboard", "Home", new { area = "Seller" });
    }

    private async Task<IActionResult> HandleLogoutAsync()
    {
        ClearTwoFactorChallenge();
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

        return RedirectToAction("Login", "SellerAccount", new { area = "Seller" });
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

        if (User.IsInRole("Admin"))
        {
            return RedirectToAction("Dashboard", "Home", new { area = "Admin" });
        }

        return RedirectToAction("Dashboard", "Home", new { area = "Seller" });
    }

    private void SaveTwoFactorChallenge(TwoFactorChallengeStateDto challenge)
    {
        HttpContext.Session.SetString(TwoFactorChallengeSessionKey, JsonSerializer.Serialize(challenge));
    }

    private TwoFactorChallengeStateDto? ReadTwoFactorChallenge()
    {
        var json = HttpContext.Session.GetString(TwoFactorChallengeSessionKey);
        return string.IsNullOrWhiteSpace(json)
            ? null
            : JsonSerializer.Deserialize<TwoFactorChallengeStateDto>(json);
    }

    private void ClearTwoFactorChallenge()
    {
        HttpContext.Session.Remove(TwoFactorChallengeSessionKey);
    }

    private static string BuildRateLimitMessage(string? retryAfter)
    {
        if (int.TryParse(retryAfter, out var retryAfterSeconds) && retryAfterSeconds > 0)
        {
            return $"Bạn thao tác quá nhanh. Vui lòng chờ khoảng {retryAfterSeconds} giây rồi thử lại.";
        }

        return "Bạn thao tác quá nhanh. Vui lòng chờ một lát rồi thử lại.";
    }

    private static SellerTwoFactorViewModel BuildTwoFactorViewModel(TwoFactorChallengeStateDto challenge, string? code = null)
    {
        return new SellerTwoFactorViewModel
        {
            Code = code ?? string.Empty,
            RequiresSetup = challenge.RequiresSetup,
            RememberMe = challenge.RememberMe,
            ManualEntryKey = challenge.ManualEntryKey,
            OtpAuthUri = challenge.OtpAuthUri,
            QrCodeImageDataUri = QrCodeDataUriBuilder.BuildSvgDataUri(challenge.OtpAuthUri),
            AuthenticatorIssuer = challenge.AuthenticatorIssuer,
            AuthenticatorAccountName = challenge.AuthenticatorAccountName,
            ChallengeMessage = challenge.ChallengeMessage
        };
    }
}
