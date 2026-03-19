using System.IdentityModel.Tokens.Jwt; // Dung de parse JWT claim.
using System.Security.Claims; // Dung Claim/ClaimsPrincipal.
using FreshFarm.Web.Bff.Dtos; // Dung DTO vua tao.
using FreshFarm.Web.Bff.Options;
using FreshFarm.Web.Bff.Services; // Dung service GHN.
using FreshFarm.Web.Bff.Utilities;
using Microsoft.AspNetCore.Authentication; // Dung SignInAsync/SignOutAsync.
using Microsoft.AspNetCore.Authentication.Cookies; // Cookie auth scheme.
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.AspNetCore.Authorization; // [Authorize], [AllowAnonymous].
using Microsoft.AspNetCore.Mvc; // Controller, IActionResult.
using Microsoft.Extensions.Options;

namespace FreshFarm.Web.Bff.Controllers; // Namespace controller.

public sealed class AccountController : Controller // MVC controller cho auth/account pages.
{
    private const string AccessTokenSessionKey = "ACCESS_TOKEN"; // Key luu JWT trong session.
    private readonly IHttpClientFactory _httpClientFactory; // Factory tao HttpClient theo ten.
    private readonly IGhnSandboxService _ghnSandboxService; // Service doc danh muc dia chi GHN.
    private readonly GoogleAuthenticationOptions _googleAuthenticationOptions;

    public AccountController(
        IHttpClientFactory httpClientFactory,
        IGhnSandboxService ghnSandboxService,
        IOptions<GoogleAuthenticationOptions> googleAuthenticationOptions) // Inject factory qua DI.
    {
        _httpClientFactory = httpClientFactory; // Gan vao field.
        _ghnSandboxService = ghnSandboxService;
        _googleAuthenticationOptions = googleAuthenticationOptions.Value;
    }

    [HttpGet("/account/signin")] // Route GET signin.
    [AllowAnonymous] // Chua login van vao duoc.
    public IActionResult SignIn(string? returnUrl = null, string? externalError = null, bool rateLimitError = false, string? retryAfter = null) // Render view signin.
    {
        var normalizedReturnUrl = NormalizeReturnUrl(returnUrl); // Chuan hoa returnUrl cho redirect an toan.
        if (User.Identity?.IsAuthenticated == true) // Neu da dang nhap thi khong can vao form.
        {
            return RedirectToLocal(normalizedReturnUrl); // Quay ve trang truoc hoac fallback.
        }

        if (!string.IsNullOrWhiteSpace(externalError))
        {
            TempData["ErrorMessage"] = externalError.Contains("access_denied", StringComparison.OrdinalIgnoreCase)
                ? "Bạn đã hủy đăng nhập bằng Google."
                : "Đăng nhập Google chưa hoàn tất. Vui lòng thử lại.";
        }

        ViewData["GoogleLoginEnabled"] = _googleAuthenticationOptions.IsConfigured;
        ViewData["PendingVerificationIdentifier"] = TempData["PendingVerificationIdentifier"] as string;
        ViewData["ReturnUrl"] = normalizedReturnUrl; // Luu returnUrl de POST redirect dung trang.
        ViewData["LockoutExpiresAtUtc"] = null;
        ViewData["FormErrorMessage"] = null;
        ViewData["RateLimitErrorMessage"] = rateLimitError
            ? BuildRateLimitMessage(retryAfter)
            : null;
        return View(); // Views/Account/SignIn.cshtml.
    }

    private static string BuildRateLimitMessage(string? retryAfter)
    {
        if (int.TryParse(retryAfter, out var retryAfterSeconds) && retryAfterSeconds > 0)
        {
            return $"Bạn thao tác quá nhanh. Vui lòng chờ khoảng {retryAfterSeconds} giây rồi thử lại.";
        }

        return "Bạn thao tác quá nhanh. Vui lòng chờ một lát rồi thử lại.";
    }

    [HttpPost("/account/signin")] // Route POST signin.
    [ValidateAntiForgeryToken] // Bắt buộc token hợp lệ từ form.
    [AllowAnonymous] // Anonymous submit login.
    [EnableRateLimiting("auth-form")]
    public async Task<IActionResult> SignIn(LoginRequestDto request, string? returnUrl = null) // Nhan model form.
    {
        var normalizedReturnUrl = NormalizeReturnUrl(returnUrl); // Chi chap nhan local url de tranh open redirect.
        ViewData["GoogleLoginEnabled"] = _googleAuthenticationOptions.IsConfigured;
        ViewData["ReturnUrl"] = normalizedReturnUrl; // Giu lai de form render lai khi co loi.
        ViewData["PendingVerificationIdentifier"] = null;
        ViewData["LockoutExpiresAtUtc"] = null;
        ViewData["FormErrorMessage"] = null;

        request.ClientLane = "Customer";

        if (string.IsNullOrWhiteSpace(request.Identifier) || string.IsNullOrWhiteSpace(request.Password)) // Validate input.
        {
            ModelState.AddModelError(string.Empty, "Vui lòng nhập đầy đủ thông tin."); // Them loi cho view.
            return View(request); // Render lai form.
        }

        var identityClient = _httpClientFactory.CreateClient("Identity"); // Lay client goi Identity API.
        using var loginHttpRequest = ForwardedAuthRequestBuilder.CreateForwardedJsonRequest(
            HttpContext,
            System.Net.Http.HttpMethod.Post,
            "/auth/login",
            request);
        var loginResponse = await identityClient.SendAsync(loginHttpRequest); // Goi login API.

        if (!loginResponse.IsSuccessStatusCode) // Neu login fail.
        {
            var errorBody = await loginResponse.Content.ReadAsStringAsync();
            var errorText = ApiErrorMessageParser.ExtractMessage(errorBody, "Đăng nhập chưa thành công"); // Doc body loi.
            if (RequiresEmailVerification(errorText))
            {
                ViewData["PendingVerificationIdentifier"] = request.Identifier.Trim();
            }

            if (ApiErrorMessageParser.TryExtractLockedUntilUtc(errorBody, out var lockoutExpiresAtUtc))
            {
                ViewData["LockoutExpiresAtUtc"] = lockoutExpiresAtUtc.ToString("O");
                ViewData["FormErrorMessage"] = errorText;
                return View(request);
            }

            ModelState.AddModelError(string.Empty, errorText); // Show error.
            return View(request); // O lai form login.
        }

        var auth = await loginResponse.Content.ReadFromJsonAsync<AuthResponseDto>(); // Parse body sang DTO.
        if (auth is null || string.IsNullOrWhiteSpace(auth.AccessToken)) // Bao ve response xau.
        {
            ModelState.AddModelError(string.Empty, "Token trả về không hợp lệ."); // Bao loi.
            return View(request); // O lai form.
        }

        await SignInWithIdentityTokenAsync(auth, request.Identifier);

        return RedirectToLocal(normalizedReturnUrl); // Login xong quay ve trang dang dung neu hop le.
    }

    [HttpGet("/account/signin/google")]
    [AllowAnonymous]
    public IActionResult SignInWithGoogle(string? returnUrl = null)
    {
        if (!_googleAuthenticationOptions.IsConfigured)
        {
            TempData["ErrorMessage"] = "Đăng nhập Google chưa được cấu hình trên môi trường dev.";
            return RedirectToAction(nameof(SignIn), new { returnUrl = NormalizeReturnUrl(returnUrl) });
        }

        var normalizedReturnUrl = NormalizeReturnUrl(returnUrl);
        var properties = new AuthenticationProperties
        {
            RedirectUri = Url.Action(nameof(GoogleCallback), new { returnUrl = normalizedReturnUrl })
        };
        if (!string.IsNullOrWhiteSpace(normalizedReturnUrl))
        {
            properties.Items["returnUrl"] = normalizedReturnUrl;
        }

        return Challenge(properties, "Google");
    }

    [HttpGet("/account/signin/google-callback")]
    [AllowAnonymous]
    public async Task<IActionResult> GoogleCallback(string? returnUrl = null)
    {
        var normalizedReturnUrl = NormalizeReturnUrl(returnUrl);
        var externalAuth = await HttpContext.AuthenticateAsync("GoogleExternal");
        if (!externalAuth.Succeeded || externalAuth.Principal is null)
        {
            TempData["ErrorMessage"] = "Không đọc được thông tin tài khoản Google. Vui lòng thử lại.";
            return RedirectToAction(nameof(SignIn), new { returnUrl = normalizedReturnUrl });
        }

        var email = externalAuth.Principal.FindFirstValue(ClaimTypes.Email)
            ?? externalAuth.Principal.FindFirstValue("email");
        if (string.IsNullOrWhiteSpace(email))
        {
            await HttpContext.SignOutAsync("GoogleExternal");
            TempData["ErrorMessage"] = "Google chưa trả về email hợp lệ. Vui lòng chọn tài khoản Gmail khác.";
            return RedirectToAction(nameof(SignIn), new { returnUrl = normalizedReturnUrl });
        }

        var fullName = externalAuth.Principal.FindFirstValue(ClaimTypes.Name) ?? email;
        var avatarUrl = externalAuth.Principal.FindFirstValue("picture")
            ?? externalAuth.Principal.FindFirstValue("urn:google:picture");

        var identityClient = _httpClientFactory.CreateClient("Identity");
        using var externalLoginHttpRequest = ForwardedAuthRequestBuilder.CreateForwardedJsonRequest(
            HttpContext,
            System.Net.Http.HttpMethod.Post,
            "/auth/external-login",
            new ExternalLoginExchangeRequestDto
            {
                Provider = "Google",
                Email = email,
                FullName = fullName,
                AvatarUrl = avatarUrl
            });
        var exchangeResponse = await identityClient.SendAsync(externalLoginHttpRequest);

        await HttpContext.SignOutAsync("GoogleExternal");

        if (!exchangeResponse.IsSuccessStatusCode)
        {
            var errorText = await ApiErrorMessageParser.ReadMessageAsync(exchangeResponse, "Không thể hoàn tất đăng nhập Google");
            TempData["ErrorMessage"] = errorText;
            return RedirectToAction(nameof(SignIn), new { returnUrl = normalizedReturnUrl });
        }

        var auth = await exchangeResponse.Content.ReadFromJsonAsync<AuthResponseDto>();
        if (auth is null || string.IsNullOrWhiteSpace(auth.AccessToken))
        {
            TempData["ErrorMessage"] = "Identity API trả token đăng nhập Google không hợp lệ.";
            return RedirectToAction(nameof(SignIn), new { returnUrl = normalizedReturnUrl });
        }

        await SignInWithIdentityTokenAsync(auth, email);
        return RedirectToLocal(normalizedReturnUrl);
    }

    [HttpGet("/account/signup")] // Route GET signup.
    [AllowAnonymous] // Anonymous vao trang dang ky.
    public IActionResult SignUp(string? returnUrl = null) // Render view signup.
    {
        ViewData["ReturnUrl"] = NormalizeReturnUrl(returnUrl);
        return View(new RegisterRequestDto()); // Views/Account/SignUp.cshtml.
    }

    [HttpGet("/account/terms")]
    [AllowAnonymous]
    public IActionResult Terms()
    {
        return View();
    }

    [HttpGet("/account/privacy")]
    [AllowAnonymous]
    public IActionResult PrivacyPolicy()
    {
        return View("PrivacyPolicy");
    }

    [HttpPost("/account/signup")] // Route POST signup.
    [ValidateAntiForgeryToken] // Chặn submit giả mạo từ site khác.
    [AllowAnonymous] // Anonymous dang ky.
    public async Task<IActionResult> SignUp(RegisterRequestDto request, string? returnUrl = null) // Nhan model form.
    {
        var normalizedReturnUrl = NormalizeReturnUrl(returnUrl);
        ViewData["ReturnUrl"] = normalizedReturnUrl;

        request.FullName = request.FullName?.Trim() ?? string.Empty;
        request.UserName = request.UserName?.Trim() ?? string.Empty;
        request.Email = request.Email?.Trim().ToLowerInvariant() ?? string.Empty;
        request.Phone = request.Phone?.Trim() ?? string.Empty;
        request.RoleName = "Customer";

        if (!string.IsNullOrWhiteSpace(request.Password))
        {
            var hasLetter = request.Password.Any(char.IsLetter);
            var hasDigit = request.Password.Any(char.IsDigit);
            if (!hasLetter || !hasDigit)
            {
                ModelState.AddModelError(nameof(RegisterRequestDto.Password), "Mật khẩu cần có ít nhất 1 chữ cái và 1 chữ số.");
            }
        }

        if (!ModelState.IsValid) // Validate MVC model.
        {
            return View(request); // Render lai neu invalid.
        }

        var identityClient = _httpClientFactory.CreateClient("Identity"); // HttpClient cho Identity.
        using var registerHttpRequest = ForwardedAuthRequestBuilder.CreateForwardedJsonRequest(
            HttpContext,
            System.Net.Http.HttpMethod.Post,
            "/auth/register",
            new
            {
                request.UserName,
                request.FullName,
                request.Email,
                request.Phone,
                request.Password,
                request.ConfirmPassword,
                request.RoleName
            });
        var registerResponse = await identityClient.SendAsync(registerHttpRequest); // Goi register API.

        if (!registerResponse.IsSuccessStatusCode) // Register fail.
        {
            var errorText = await ApiErrorMessageParser.ReadMessageAsync(registerResponse, "Đăng ký chưa thành công"); // Doc loi.
            ModelState.AddModelError(string.Empty, errorText); // Show loi.
            return View(request); // O lai form.
        }

        var registerResult = await registerResponse.Content.ReadFromJsonAsync<RegisterResultDto>()
                            ?? new RegisterResultDto
                            {
                                Email = request.Email,
                                EmailVerificationRequired = true,
                                VerificationEmailSent = true,
                                Message = "Tài khoản đã được tạo. Vui lòng kiểm tra email để xác minh trước khi đăng nhập."
                            };

        return RedirectToAction(
            nameof(VerifyEmailPending),
            new
            {
                email = registerResult.Email,
                returnUrl = normalizedReturnUrl,
                message = registerResult.Message
            });
    }

    [HttpGet("/account/verify-email/pending")]
    [AllowAnonymous]
    public IActionResult VerifyEmailPending(string? email = null, string? message = null, string? returnUrl = null)
    {
        var normalizedReturnUrl = NormalizeReturnUrl(returnUrl);
        var normalizedEmail = email?.Trim() ?? string.Empty;

        var vm = new EmailVerificationPendingViewModel
        {
            Email = normalizedEmail,
            Message = string.IsNullOrWhiteSpace(message)
                ? "Chúng tôi đã tạo tài khoản và gửi email xác minh. Vui lòng xác minh email trước khi đăng nhập."
                : message,
            ReturnUrl = normalizedReturnUrl,
            ResendRequest = new ResendEmailVerificationRequestDto
            {
                Identifier = normalizedEmail,
                ReturnUrl = normalizedReturnUrl
            }
        };

        return View(vm);
    }

    [HttpPost("/account/verify-email/resend")]
    [ValidateAntiForgeryToken]
    [AllowAnonymous]
    [EnableRateLimiting("password-recovery")]
    public async Task<IActionResult> ResendVerificationEmail(ResendEmailVerificationRequestDto request)
    {
        var normalizedReturnUrl = NormalizeReturnUrl(request.ReturnUrl);
        request.Identifier = request.Identifier?.Trim() ?? string.Empty;

        if (!ModelState.IsValid)
        {
            TempData["ErrorMessage"] = "Vui lòng nhập email hoặc tên đăng nhập hợp lệ để gửi lại email xác minh.";
            TempData["PendingVerificationIdentifier"] = request.Identifier;
            return RedirectToAction(nameof(SignIn), new { returnUrl = normalizedReturnUrl });
        }

        var identityClient = _httpClientFactory.CreateClient("Identity");
        using var resendHttpRequest = ForwardedAuthRequestBuilder.CreateForwardedJsonRequest(
            HttpContext,
            System.Net.Http.HttpMethod.Post,
            "/auth/resend-email-verification",
            new
            {
                identifier = request.Identifier
            });
        var response = await identityClient.SendAsync(resendHttpRequest);

        if (!response.IsSuccessStatusCode)
        {
            var errorText = await ApiErrorMessageParser.ReadMessageAsync(response, "Không thể gửi lại email xác minh lúc này");
            TempData["ErrorMessage"] = errorText;
            TempData["PendingVerificationIdentifier"] = request.Identifier;
            return RedirectToAction(nameof(SignIn), new { returnUrl = normalizedReturnUrl });
        }

        TempData["SuccessMessage"] = "Nếu tài khoản tồn tại và chưa xác minh, chúng tôi đã gửi lại email xác minh.";
        TempData["PendingVerificationIdentifier"] = request.Identifier;

        if (request.Identifier.Contains("@", StringComparison.Ordinal))
        {
            return RedirectToAction(nameof(VerifyEmailPending), new
            {
                email = request.Identifier,
                returnUrl = normalizedReturnUrl
            });
        }

        return RedirectToAction(nameof(SignIn), new { returnUrl = normalizedReturnUrl });
    }

    [HttpGet("/account/verify-email")]
    [AllowAnonymous]
    public async Task<IActionResult> VerifyEmail(string? email = null, string? token = null, string? returnUrl = null)
    {
        var normalizedReturnUrl = NormalizeReturnUrl(returnUrl);
        var normalizedEmail = email?.Trim() ?? string.Empty;

        if (string.IsNullOrWhiteSpace(normalizedEmail) || string.IsNullOrWhiteSpace(token))
        {
            return View("VerifyEmailResult", new EmailVerificationResultViewModel
            {
                Success = false,
                Title = "Liên kết xác minh không hợp lệ",
                Message = "Liên kết xác minh email bị thiếu dữ liệu hoặc không còn hợp lệ.",
                ReturnUrl = normalizedReturnUrl,
                Email = normalizedEmail
            });
        }

        var identityClient = _httpClientFactory.CreateClient("Identity");
        using var verifyEmailHttpRequest = ForwardedAuthRequestBuilder.CreateForwardedJsonRequest(
            HttpContext,
            System.Net.Http.HttpMethod.Post,
            "/auth/verify-email",
            new
            {
                email = normalizedEmail,
                token
            });
        var response = await identityClient.SendAsync(verifyEmailHttpRequest);

        if (!response.IsSuccessStatusCode)
        {
            var errorText = await response.Content.ReadAsStringAsync();
            return View("VerifyEmailResult", new EmailVerificationResultViewModel
            {
                Success = false,
                Title = "Xác minh email chưa thành công",
                Message = string.IsNullOrWhiteSpace(errorText)
                    ? "Không thể xác minh email lúc này."
                    : errorText,
                ReturnUrl = normalizedReturnUrl,
                Email = normalizedEmail
            });
        }

        TempData["SuccessMessage"] = "Xác minh email thành công. Bạn có thể đăng nhập.";
        return View("VerifyEmailResult", new EmailVerificationResultViewModel
        {
            Success = true,
            Title = "Xác minh email thành công",
            Message = "Email của bạn đã được xác minh. Bây giờ bạn có thể đăng nhập vào FreshFarm.",
            ReturnUrl = normalizedReturnUrl,
            Email = normalizedEmail
        });
    }

    [HttpGet("/account/forgot-password")] // Route GET quên mật khẩu.
    [AllowAnonymous] // Cho phép user chưa login truy cập.
    public IActionResult ForgotPassword() // Render view forgot password.
    {
        return View(new ForgotPasswordRequestDto()); // Trả view với model rỗng.
    }

    [HttpPost("/account/forgot-password")] // Route POST gửi yêu cầu reset.
    [ValidateAntiForgeryToken] // Chống CSRF cho form.
    [AllowAnonymous] // Anonymous vẫn dùng được.
    [EnableRateLimiting("password-recovery")]
    public async Task<IActionResult> ForgotPassword(ForgotPasswordRequestDto request) // Nhận email từ form.
    {
        if (!ModelState.IsValid) // Validate DataAnnotation.
        {
            return View(request); // Render lại form nếu dữ liệu sai.
        }

        request.Email = request.Email.Trim(); // Chuẩn hóa email trước khi gọi API.

        var identityClient = _httpClientFactory.CreateClient("Identity"); // Client gọi Identity API.
        using var forgotPasswordHttpRequest = ForwardedAuthRequestBuilder.CreateForwardedJsonRequest(
            HttpContext,
            System.Net.Http.HttpMethod.Post,
            "/auth/forgot-password",
            new
            {
                email = request.Email
            });
        var response = await identityClient.SendAsync(forgotPasswordHttpRequest); // Gửi yêu cầu quên mật khẩu.

        if (!response.IsSuccessStatusCode) // Nếu API trả lỗi.
        {
            var errorText = await response.Content.ReadAsStringAsync(); // Đọc message lỗi.
            ModelState.AddModelError(string.Empty, string.IsNullOrWhiteSpace(errorText)
                ? "Không thể gửi email đặt lại mật khẩu lúc này."
                : errorText); // Hiện lỗi rõ cho user.
            return View(request); // Ở lại form.
        }

        TempData["SuccessMessage"] = "Nếu email tồn tại trong hệ thống, chúng tôi đã gửi hướng dẫn đặt lại mật khẩu.";
        return RedirectToAction(nameof(ForgotPassword)); // PRG để tránh submit lại khi refresh.
    }

    [HttpGet("/account/reset-password")] // Route GET trang đặt lại mật khẩu.
    [AllowAnonymous] // User từ email chưa login vẫn truy cập được.
    public IActionResult ResetPassword(string? email = null, string? token = null) // Nhận email + token từ query string.
    {
        var model = new ResetPasswordRequestDto
        {
            Email = email?.Trim() ?? string.Empty,
            Token = token ?? string.Empty
        }; // Khởi tạo model từ query string.

        if (string.IsNullOrWhiteSpace(model.Email) || string.IsNullOrWhiteSpace(model.Token)) // Nếu thiếu dữ liệu từ email.
        {
            ModelState.AddModelError(string.Empty, "Liên kết đặt lại mật khẩu không hợp lệ hoặc đã bị thiếu dữ liệu.");
        }

        return View(model); // Render view reset.
    }

    [HttpPost("/account/reset-password")] // Route POST đặt lại mật khẩu.
    [ValidateAntiForgeryToken] // Chống CSRF cho form.
    [AllowAnonymous] // Anonymous submit reset password.
    [EnableRateLimiting("password-recovery")]
    public async Task<IActionResult> ResetPassword(ResetPasswordRequestDto request) // Nhận email/token/password mới.
    {
        if (!ModelState.IsValid) // Validate DataAnnotation.
        {
            return View(request); // Ở lại form nếu invalid.
        }

        request.Email = request.Email.Trim(); // Chuẩn hóa email.

        var identityClient = _httpClientFactory.CreateClient("Identity"); // Client gọi Identity API.
        using var resetPasswordHttpRequest = ForwardedAuthRequestBuilder.CreateForwardedJsonRequest(
            HttpContext,
            System.Net.Http.HttpMethod.Post,
            "/auth/reset-password",
            new
            {
                email = request.Email,
                token = request.Token,
                newPassword = request.NewPassword,
                confirmPassword = request.ConfirmPassword
            });
        var response = await identityClient.SendAsync(resetPasswordHttpRequest); // Gọi API đặt lại mật khẩu.

        if (!response.IsSuccessStatusCode) // Nếu API fail.
        {
            var errorText = await response.Content.ReadAsStringAsync(); // Đọc body lỗi.
            ModelState.AddModelError(string.Empty, string.IsNullOrWhiteSpace(errorText)
                ? "Không thể đặt lại mật khẩu."
                : errorText); // Gắn lỗi vào validation summary.
            return View(request); // Giữ nguyên email/token để user thử lại.
        }

        TempData["SuccessMessage"] = "Đặt lại mật khẩu thành công. Vui lòng đăng nhập bằng mật khẩu mới.";
        return RedirectToAction(nameof(SignIn)); // Quay lại login sau khi reset xong.
    }

    [HttpPost("/account/logout")] // Route POST logout.
    [ValidateAntiForgeryToken] // Logout cũng là state-changing action.
    [Authorize] // Bat buoc login.
    public async Task<IActionResult> Logout() // Xu ly logout.
    {
        HttpContext.Session.Remove(AccessTokenSessionKey); // Xoa JWT khoi session.
        await HttpContext.SignOutAsync(CookieAuthenticationDefaults.AuthenticationScheme); // Xoa cookie auth.
        return RedirectToAction(nameof(SignIn)); // Ve trang signin.
    }

    [HttpGet] // Route convention: /Account/AvatarById?id=123.
    [AllowAnonymous] // Avatar fallback co the truy cap khong can login.
    public IActionResult AvatarById(int id = 0) // Tra ve avatar SVG mac dinh theo id.
    {
        var palette = new[] { "#2F855A", "#B7791F", "#2B6CB0", "#9B2C2C", "#805AD5", "#319795" }; // Bang mau avatar.
        var color = palette[System.Math.Abs(id) % palette.Length]; // Chon mau on dinh theo user id.
        var svg = $"<svg xmlns='http://www.w3.org/2000/svg' viewBox='0 0 120 120'><rect width='120' height='120' rx='24' fill='{color}'/><circle cx='60' cy='44' r='20' fill='rgba(255,255,255,0.25)'/><path d='M20 105c6-19 18-29 40-29s34 10 40 29' fill='rgba(255,255,255,0.25)'/><text x='60' y='72' text-anchor='middle' font-family='Arial,sans-serif' font-size='34' font-weight='700' fill='white'>U</text></svg>"; // SVG nhe de fallback.
        return Content(svg, "image/svg+xml"); // Tra anh avatar.
    }

    [HttpGet("/account/orders")] // Route GET order history.
    [Authorize] // Chi user login moi xem duoc.
    public async Task<IActionResult> OrderHistory() // Render lich su don.
    {
        var token = HttpContext.Session.GetString(AccessTokenSessionKey); // Lay JWT da luu.
        if (string.IsNullOrWhiteSpace(token)) // Session mat token.
        {
            await HttpContext.SignOutAsync(CookieAuthenticationDefaults.AuthenticationScheme); // Sync logout cookie.
            return RedirectToAction(nameof(SignIn)); // Ve login.
        }

        var orderingClient = _httpClientFactory.CreateClient("Ordering"); // Client goi Ordering API.
        orderingClient.DefaultRequestHeaders.Authorization =
            new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", token); // Gan bearer token.

        var response = await orderingClient.GetAsync("/api/orders/my"); // Goi API don cua toi.
        if (!response.IsSuccessStatusCode) // API fail.
        {
            ViewBag.Error = $"Khong lay duoc lich su don: {(int)response.StatusCode}"; // Set message loi.
            return View(new List<OrderHistoryItemDto>()); // Tra list rong de view van render duoc.
        }

        var orders = await response.Content.ReadFromJsonAsync<List<OrderHistoryItemDto>>() // Parse JSON -> list DTO.
                     ?? new List<OrderHistoryItemDto>(); // Fallback list rong.

        return View(orders); // Render view order history.
    }

    [HttpGet("/account/orders/{id:int}")] // Route xem chi tiet 1 don.
    [Authorize] // Bat buoc login moi xem duoc.
    public async Task<IActionResult> OrderDetail(int id) // Nhan order id tu URL.
    {
        var token = HttpContext.Session.GetString(AccessTokenSessionKey); // Lay JWT da luu trong session.
        if (string.IsNullOrWhiteSpace(token)) // Neu mat token.
        {
            await HttpContext.SignOutAsync(CookieAuthenticationDefaults.AuthenticationScheme); // Dong bo logout cookie.
            return RedirectToAction(nameof(SignIn)); // Day user ve trang login.
        }

        var orderingClient = _httpClientFactory.CreateClient("Ordering"); // Tao client goi Ordering API.
        orderingClient.DefaultRequestHeaders.Authorization =
            new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", token); // Gan bearer token de service auth.

        var response = await orderingClient.GetAsync($"/api/orders/{id}"); // Goi endpoint chi tiet don.
        if (!response.IsSuccessStatusCode) // Neu service tra loi.
        {
            ViewBag.Error = $"Không lấy được chi tiết đơn hàng: {(int)response.StatusCode}."; // Bao loi cho view.
            return View(model: null); // Van render view de user thay thong bao.
        }

        var detail = await response.Content.ReadFromJsonAsync<OrderDetailDto>(); // Parse body sang DTO.
        if (detail is null) // Bao ve body null/khong parse duoc.
        {
            ViewBag.Error = "Không đọc được dữ liệu chi tiết đơn hàng."; // Bao loi cho view.
            return View(model: null); // Render view voi model null.
        }

        detail.Items ??= new List<OrderDetailItemDto>(); // Dam bao khong null khi render view.
        detail.Shippings ??= new List<OrderDetailShippingDto>(); // Dam bao khong null khi render view.
        detail.Payments ??= new List<OrderDetailPaymentDto>(); // Dam bao khong null khi render view.

        return View(detail); // Render Views/Account/OrderDetail.cshtml.
    }

    private string? NormalizeReturnUrl(string? returnUrl) // Chuan hoa returnUrl tu query/form.
    {
        if (string.IsNullOrWhiteSpace(returnUrl)) // Null/rong thi bo qua.
        {
            return null;
        }

        return Url.IsLocalUrl(returnUrl) ? returnUrl : null; // Chi cho phep local url.
    }

    private IActionResult RedirectToLocal(string? returnUrl) // Redirect an toan sau login.
    {
        if (!string.IsNullOrWhiteSpace(returnUrl) && Url.IsLocalUrl(returnUrl)) // URL noi bo hop le.
        {
            var path = returnUrl.Trim();
            if (path.StartsWith("/Admin/", StringComparison.OrdinalIgnoreCase) && !User.IsInRole("Admin"))
            {
                return RedirectToAction("Index", "Home");
            }

            if (path.StartsWith("/Seller/", StringComparison.OrdinalIgnoreCase) && !User.IsInRole("Seller"))
            {
                return RedirectToAction("Index", "Home");
            }

            return Redirect(returnUrl); // Quay lai trang user dang dung.
        }

        return RedirectToAction("Index", "Home"); // Fallback mac dinh ve trang chu buyer-facing.
    }

    private static bool RequiresEmailVerification(string? message)
    {
        return !string.IsNullOrWhiteSpace(message) &&
               message.Contains("chưa được xác minh", StringComparison.OrdinalIgnoreCase);
    }

    // ===== 3) Thay action GET /account/profile bằng bản dùng API thật =====
    [HttpGet("/account/profile")] // Route profile.
    [Authorize] // Chỉ user login mới xem được.
    public async Task<IActionResult> Profile(string? returnUrl = null) // Render profile async để gọi API.
    {
        var token = HttpContext.Session.GetString(AccessTokenSessionKey); // Lấy token từ session.
        if (string.IsNullOrWhiteSpace(token)) // Nếu mất token.
        {
            await HttpContext.SignOutAsync(CookieAuthenticationDefaults.AuthenticationScheme); // Logout cookie cho đồng bộ.
            return RedirectToAction(nameof(SignIn), new { returnUrl = NormalizeReturnUrl(returnUrl) ?? "/account/profile" }); // Về đăng nhập.
        }

        var safeReturnUrl = NormalizeReturnUrl(returnUrl) ?? "/account/profile"; // Chuẩn hóa returnUrl.
        var (vm, error) = await GetProfilePageViewModelAsync(token, safeReturnUrl); // Gọi helper lấy model.
        if (vm is null) // Nếu lấy model lỗi.
        {
            ViewBag.Error = error ?? "Không tải được thông tin tài khoản."; // Set lỗi cho view.
            vm = new ProfilePageViewModel { ReturnUrl = safeReturnUrl }; // Tạo model fallback để view không vỡ.
        }

        ViewBag.GhnSandboxConfigured = _ghnSandboxService.IsConfigured;
        return View(vm); // Render view với model.
    }

    [HttpGet("/account/profile/ghn/provinces")]
    [Authorize]
    public async Task<JsonResult> GetGhnProvinces(CancellationToken cancellationToken)
    {
        var items = await _ghnSandboxService.GetProvincesAsync(cancellationToken);
        return Json(new
        {
            success = true,
            configured = _ghnSandboxService.IsConfigured,
            items = items.Select(x => new
            {
                id = x.Id,
                code = x.Code,
                name = x.Name
            })
        });
    }

    [HttpGet("/account/profile/ghn/districts")]
    [Authorize]
    public async Task<JsonResult> GetGhnDistricts(int provinceId, CancellationToken cancellationToken)
    {
        if (provinceId <= 0)
        {
            return Json(new { success = false, message = "Thiếu mã tỉnh/thành GHN." });
        }

        var items = await _ghnSandboxService.GetDistrictsAsync(provinceId, cancellationToken);
        return Json(new
        {
            success = true,
            configured = _ghnSandboxService.IsConfigured,
            provinceId,
            items = items.Select(x => new
            {
                id = x.Id,
                code = x.Code,
                name = x.Name
            })
        });
    }

    [HttpGet("/account/profile/ghn/wards")]
    [Authorize]
    public async Task<JsonResult> GetGhnWards(int districtId, CancellationToken cancellationToken)
    {
        if (districtId <= 0)
        {
            return Json(new { success = false, message = "Thiếu mã quận/huyện GHN." });
        }

        var items = await _ghnSandboxService.GetWardsAsync(districtId, cancellationToken);
        return Json(new
        {
            success = true,
            configured = _ghnSandboxService.IsConfigured,
            districtId,
            items = items.Select(x => new
            {
                id = x.Id,
                code = x.Code,
                name = x.Name
            })
        });
    }

    // ===== 4) Thay action POST /account/profile bằng bản validate chặt =====
    [HttpPost("/account/profile")] // Route submit cập nhật profile.
    [Authorize] // Chỉ user login mới submit.
    [ValidateAntiForgeryToken] // Bật chống CSRF.
    public async Task<IActionResult> ProfileUpdate(ProfileUpdateRequestDto request, string? returnUrl = null) // Nhận payload cập nhật.
    {
        var safeReturnUrl = NormalizeReturnUrl(returnUrl) // Ưu tiên query returnUrl.
            ?? NormalizeReturnUrl(request.ReturnUrl) // Nếu query không có thì lấy từ form.
            ?? "/account/profile"; // Fallback cứng về profile.

        if (!ModelState.IsValid) // Nếu DataAnnotation fail.
        {
            TempData["ErrorMessage"] = "Dữ liệu cập nhật không hợp lệ. Vui lòng kiểm tra lại."; // Báo lỗi chung.
            return Redirect(safeReturnUrl); // Redirect lại trang profile.
        }

        var token = HttpContext.Session.GetString(AccessTokenSessionKey); // Lấy token session.
        if (string.IsNullOrWhiteSpace(token)) // Nếu mất token.
        {
            await HttpContext.SignOutAsync(CookieAuthenticationDefaults.AuthenticationScheme); // Logout cookie.
            return RedirectToAction(nameof(SignIn), new { returnUrl = safeReturnUrl }); // Về signin.
        }

        request.FullName = request.FullName.Trim(); // Trim fullname.
        request.Email = request.Email.Trim(); // Trim email.
        request.Phone = request.Phone.Trim(); // Trim phone.

        var identityClient = _httpClientFactory.CreateClient("Identity"); // Tạo Identity client.
        identityClient.DefaultRequestHeaders.Authorization = // Gắn Authorization header.
            new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", token); // Bearer token.

        var response = await identityClient.PutAsJsonAsync("/auth/profile", new // Gọi API update profile.
        {
            fullName = request.FullName, // Payload fullName.
            email = request.Email, // Payload email.
            phone = request.Phone // Payload phone.
        });

        if (!response.IsSuccessStatusCode) // Nếu update fail.
        {
            var errorBody = await response.Content.ReadAsStringAsync(); // Đọc error body.
            TempData["ErrorMessage"] = string.IsNullOrWhiteSpace(errorBody) // Chuẩn hóa message.
                ? $"Cập nhật thất bại ({(int)response.StatusCode})." // Message fallback.
                : $"Cập nhật thất bại: {errorBody}"; // Message chi tiết.
            return Redirect(safeReturnUrl); // Quay lại profile.
        }

        var claims = User.Claims.ToList(); // Lấy claim hiện tại.
        UpsertClaim(claims, ClaimTypes.Name, request.FullName); // Cập nhật tên hiển thị.
        UpsertClaim(claims, ClaimTypes.Email, request.Email); // Cập nhật email chuẩn.
        UpsertClaim(claims, "email", request.Email); // Cập nhật email custom.
        UpsertClaim(claims, ClaimTypes.MobilePhone, request.Phone); // Cập nhật phone chuẩn.
        UpsertClaim(claims, "phone", request.Phone); // Cập nhật phone custom.

        var identity = new ClaimsIdentity(claims, CookieAuthenticationDefaults.AuthenticationScheme); // Tạo identity mới.
        var principal = new ClaimsPrincipal(identity); // Tạo principal mới.
        await HttpContext.SignInAsync(CookieAuthenticationDefaults.AuthenticationScheme, principal); // Ghi lại cookie mới.

        TempData["SuccessMessage"] = "Cập nhật thông tin tài khoản thành công."; // Message thành công.
        return Redirect(safeReturnUrl); // Quay lại profile.
    }

    // ===== 5) Thêm action tạo địa chỉ =====
    [HttpPost("/account/profile/address/create")] // Route tạo địa chỉ mới.
    [Authorize] // Bắt buộc login.
    [ValidateAntiForgeryToken] // Chống CSRF.
    public async Task<IActionResult> CreateAddress(UpsertProfileAddressRequestDto request, string? returnUrl = null) // Nhận payload tạo địa chỉ.
    {
        var safeReturnUrl = NormalizeReturnUrl(returnUrl) // Chuẩn hóa query returnUrl.
            ?? NormalizeReturnUrl(request.ReturnUrl) // Hoặc lấy từ form.
            ?? "/account/profile"; // Fallback profile.

        if (!ModelState.IsValid) // Validate payload.
        {
            TempData["ErrorMessage"] = "Thông tin địa chỉ không hợp lệ."; // Báo lỗi validate.
            return Redirect(safeReturnUrl); // Quay lại profile.
        }

        var token = HttpContext.Session.GetString(AccessTokenSessionKey); // Lấy token session.
        if (string.IsNullOrWhiteSpace(token)) // Nếu mất token.
        {
            await HttpContext.SignOutAsync(CookieAuthenticationDefaults.AuthenticationScheme); // Logout cookie.
            return RedirectToAction(nameof(SignIn), new { returnUrl = safeReturnUrl }); // Về signin.
        }

        var identityClient = _httpClientFactory.CreateClient("Identity"); // Identity client.
        identityClient.DefaultRequestHeaders.Authorization = // Gắn bearer.
            new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", token); // Authorization header.

        var response = await identityClient.PostAsJsonAsync("/auth/addresses", new // Gọi API create address.
        {
            recipientName = request.RecipientName?.Trim(), // Payload recipientName.
            phone = request.Phone?.Trim(), // Payload phone.
            addressDetail = request.AddressDetail?.Trim(), // Payload addressDetail.
            province = string.IsNullOrWhiteSpace(request.Province) ? null : request.Province.Trim(), // Payload province.
            district = string.IsNullOrWhiteSpace(request.District) ? null : request.District.Trim(), // Payload district.
            ward = string.IsNullOrWhiteSpace(request.Ward) ? null : request.Ward.Trim(), // Payload ward.
            isDefault = request.IsDefault // Payload isDefault.
        });

        if (!response.IsSuccessStatusCode) // Nếu API fail.
        {
            var errorBody = await response.Content.ReadAsStringAsync(); // Đọc lỗi.
            TempData["ErrorMessage"] = string.IsNullOrWhiteSpace(errorBody) // Chuẩn hóa message.
                ? $"Thêm địa chỉ thất bại ({(int)response.StatusCode})." // Fallback message.
                : $"Thêm địa chỉ thất bại: {errorBody}"; // Message chi tiết.
            return Redirect(safeReturnUrl); // Quay lại profile.
        }

        TempData["SuccessMessage"] = "Đã thêm địa chỉ mới."; // Message thành công.
        return Redirect(safeReturnUrl); // Quay lại profile.
    }

    [HttpPost("/account/profile/address/{addressId:int}/update")] // Form submit sửa địa chỉ.
    [Authorize] // Chỉ user đã đăng nhập.
    [ValidateAntiForgeryToken] // Chống CSRF.
    public async Task<IActionResult> UpdateAddress(int addressId, UpsertProfileAddressRequestDto request, string? returnUrl = null)
    {
        var safeReturnUrl = NormalizeReturnUrl(returnUrl)
            ?? NormalizeReturnUrl(request.ReturnUrl)
            ?? "/account/profile";

        if (!ModelState.IsValid)
        {
            TempData["ErrorMessage"] = "Thông tin địa chỉ không hợp lệ.";
            return Redirect(safeReturnUrl);
        }

        var token = HttpContext.Session.GetString(AccessTokenSessionKey);
        if (string.IsNullOrWhiteSpace(token))
        {
            await HttpContext.SignOutAsync(CookieAuthenticationDefaults.AuthenticationScheme);
            return RedirectToAction(nameof(SignIn), new { returnUrl = safeReturnUrl });
        }

        var identityClient = _httpClientFactory.CreateClient("Identity");
        identityClient.DefaultRequestHeaders.Authorization =
            new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", token);

        var response = await identityClient.PutAsJsonAsync($"/auth/addresses/{addressId}", new
        {
            recipientName = request.RecipientName?.Trim(),
            phone = request.Phone?.Trim(),
            addressDetail = request.AddressDetail?.Trim(),
            province = string.IsNullOrWhiteSpace(request.Province) ? null : request.Province.Trim(),
            district = string.IsNullOrWhiteSpace(request.District) ? null : request.District.Trim(),
            ward = string.IsNullOrWhiteSpace(request.Ward) ? null : request.Ward.Trim(),
            isDefault = request.IsDefault
        });

        if (!response.IsSuccessStatusCode)
        {
            var errorBody = await response.Content.ReadAsStringAsync();
            TempData["ErrorMessage"] = string.IsNullOrWhiteSpace(errorBody)
                ? $"Cập nhật địa chỉ thất bại ({(int)response.StatusCode})."
                : $"Cập nhật địa chỉ thất bại: {errorBody}";
            return Redirect(safeReturnUrl);
        }

        TempData["SuccessMessage"] = "Cập nhật địa chỉ thành công.";
        return Redirect(safeReturnUrl);
    }

    [HttpPost("/account/profile/address/{addressId:int}/delete")] // Form submit xóa địa chỉ.
    [Authorize] // Chỉ user đã đăng nhập.
    [ValidateAntiForgeryToken] // Chống CSRF.
    public async Task<IActionResult> DeleteAddress(int addressId, string? returnUrl = null)
    {
        var safeReturnUrl = NormalizeReturnUrl(returnUrl) ?? "/account/profile";

        var token = HttpContext.Session.GetString(AccessTokenSessionKey);
        if (string.IsNullOrWhiteSpace(token))
        {
            await HttpContext.SignOutAsync(CookieAuthenticationDefaults.AuthenticationScheme);
            return RedirectToAction(nameof(SignIn), new { returnUrl = safeReturnUrl });
        }

        var identityClient = _httpClientFactory.CreateClient("Identity");
        identityClient.DefaultRequestHeaders.Authorization =
            new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", token);

        var response = await identityClient.DeleteAsync($"/auth/addresses/{addressId}");

        if (!response.IsSuccessStatusCode)
        {
            var errorBody = await response.Content.ReadAsStringAsync();
            TempData["ErrorMessage"] = string.IsNullOrWhiteSpace(errorBody)
                ? $"Xóa địa chỉ thất bại ({(int)response.StatusCode})."
                : $"Xóa địa chỉ thất bại: {errorBody}";
            return Redirect(safeReturnUrl);
        }

        TempData["SuccessMessage"] = "Đã xóa địa chỉ.";
        return Redirect(safeReturnUrl);
    }


    // ===== 6) Thêm action đặt địa chỉ mặc định =====
    [HttpPost("/account/profile/address/{addressId:int}/default")] // Route set default address.
    [Authorize] // Bắt buộc login.
    [ValidateAntiForgeryToken] // Chống CSRF.
    public async Task<IActionResult> SetDefaultAddress(int addressId, string? returnUrl = null) // Nhận id cần set default.
    {
        var safeReturnUrl = NormalizeReturnUrl(returnUrl) ?? "/account/profile"; // Chuẩn hóa returnUrl.
        var token = HttpContext.Session.GetString(AccessTokenSessionKey); // Lấy token session.
        if (string.IsNullOrWhiteSpace(token)) // Nếu mất token.
        {
            await HttpContext.SignOutAsync(CookieAuthenticationDefaults.AuthenticationScheme); // Logout.
            return RedirectToAction(nameof(SignIn), new { returnUrl = safeReturnUrl }); // Về signin.
        }

        var identityClient = _httpClientFactory.CreateClient("Identity"); // Identity client.
        identityClient.DefaultRequestHeaders.Authorization = // Gắn bearer.
            new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", token); // Authorization header.

        var response = await identityClient.PostAsync($"/auth/addresses/{addressId}/set-default", content: null); // Gọi API set default.
        if (!response.IsSuccessStatusCode) // Nếu fail.
        {
            var errorBody = await response.Content.ReadAsStringAsync(); // Đọc lỗi.
            TempData["ErrorMessage"] = string.IsNullOrWhiteSpace(errorBody) // Chuẩn hóa lỗi.
                ? $"Đặt địa chỉ mặc định thất bại ({(int)response.StatusCode})." // Fallback.
                : $"Đặt địa chỉ mặc định thất bại: {errorBody}"; // Chi tiết.
            return Redirect(safeReturnUrl); // Quay lại profile.
        }

        TempData["SuccessMessage"] = "Đã đặt địa chỉ mặc định."; // Thành công.
        return Redirect(safeReturnUrl); // Quay lại profile.
    }

    private static void UpsertClaim(List<Claim> claims, string type, string? value)
    {
        claims.RemoveAll(c => c.Type == type);
        if (!string.IsNullOrWhiteSpace(value))
        {
            claims.Add(new Claim(type, value));
        }
    }

    private async Task SignInWithIdentityTokenAsync(AuthResponseDto auth, string fallbackName)
    {
        HttpContext.Session.SetString(AccessTokenSessionKey, auth.AccessToken);

        var jwt = new JwtSecurityTokenHandler().ReadJwtToken(auth.AccessToken);
        var claims = new List<Claim>
        {
            new Claim(ClaimTypes.NameIdentifier, jwt.Subject ?? string.Empty),
            new Claim(ClaimTypes.Name, jwt.Claims.FirstOrDefault(c => c.Type == "username")?.Value ?? fallbackName),
            new Claim("sub", jwt.Subject ?? string.Empty)
        };

        var emailValue = jwt.Claims.FirstOrDefault(c => c.Type == JwtRegisteredClaimNames.Email || c.Type == "email")?.Value;
        if (!string.IsNullOrWhiteSpace(emailValue))
        {
            claims.Add(new Claim(ClaimTypes.Email, emailValue));
            claims.Add(new Claim("email", emailValue));
        }

        var phoneValue = jwt.Claims.FirstOrDefault(c => c.Type == "phone" || c.Type == "phone_number" || c.Type == ClaimTypes.MobilePhone)?.Value;
        if (!string.IsNullOrWhiteSpace(phoneValue))
        {
            claims.Add(new Claim(ClaimTypes.MobilePhone, phoneValue));
            claims.Add(new Claim("phone", phoneValue));
        }

        foreach (var roleClaim in jwt.Claims.Where(c => c.Type == ClaimTypes.Role || c.Type == "role"))
        {
            claims.Add(new Claim(ClaimTypes.Role, roleClaim.Value));
        }

        var identity = new ClaimsIdentity(claims, CookieAuthenticationDefaults.AuthenticationScheme);
        var principal = new ClaimsPrincipal(identity);
        var authProperties = new AuthenticationProperties();
        if (auth.ExpiredAtUtc > DateTime.UtcNow)
        {
            authProperties.ExpiresUtc = new DateTimeOffset(auth.ExpiredAtUtc);
            authProperties.IsPersistent = true;
        }

        await HttpContext.SignInAsync(CookieAuthenticationDefaults.AuthenticationScheme, principal, authProperties);
    }

    // ===== 2) Thêm helper private trong AccountController =====
    private async Task<(ProfilePageViewModel? Model, string? Error)> GetProfilePageViewModelAsync(string token, string? returnUrl) // Hàm gom logic gọi profile + addresses.
    {
        var identityClient = _httpClientFactory.CreateClient("Identity"); // Tạo HttpClient tới Identity API.
        identityClient.DefaultRequestHeaders.Authorization = // Gắn bearer token cho API cần auth.
            new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", token); // Header Authorization.

        var profileResponse = await identityClient.GetAsync("/auth/profile"); // Gọi API lấy profile.
        if (!profileResponse.IsSuccessStatusCode) // Nếu API profile fail.
        {
            var profileError = await profileResponse.Content.ReadAsStringAsync(); // Đọc error text.
            return (null, string.IsNullOrWhiteSpace(profileError) ? "Không lấy được thông tin tài khoản." : profileError); // Trả lỗi.
        }

        var profilePayload = await profileResponse.Content.ReadFromJsonAsync<ProfileResponseBridgeDto>(); // Parse payload profile.
        if (profilePayload is null) // Nếu parse null.
        {
            return (null, "Không đọc được dữ liệu profile từ Identity API."); // Báo lỗi parse.
        }

        var addressesResponse = await identityClient.GetAsync("/auth/addresses"); // Gọi API lấy danh sách địa chỉ.
        if (!addressesResponse.IsSuccessStatusCode) // Nếu API address fail.
        {
            var addressesError = await addressesResponse.Content.ReadAsStringAsync(); // Đọc lỗi API address.
            return (null, string.IsNullOrWhiteSpace(addressesError) ? "Không lấy được sổ địa chỉ." : addressesError); // Trả lỗi rõ.
        }

        var addressesPayload = await addressesResponse.Content.ReadFromJsonAsync<List<ProfileAddressItemDto>>() // Parse list address.
                             ?? new List<ProfileAddressItemDto>(); // Fallback list rỗng.

        var vm = new ProfilePageViewModel // Tạo view model tổng cho trang profile.
        {
            UserName = profilePayload.UserName ?? string.Empty, // Gán username.
            FullName = profilePayload.FullName ?? string.Empty, // Gán fullname.
            Email = profilePayload.Email ?? string.Empty, // Gán email.
            Phone = profilePayload.Phone ?? string.Empty, // Gán phone.
            Addresses = addressesPayload, // Gán danh sách địa chỉ.
            ReturnUrl = returnUrl // Gán returnUrl.
        };

        return (vm, null); // Trả model thành công.
    }

    private sealed class ProfileResponseBridgeDto // DTO bridge nội bộ để parse JSON từ Identity API.
    {
        public int UserId { get; set; } // UserId từ API.
        public string? UserName { get; set; } // UserName từ API.
        public string? FullName { get; set; } // FullName từ API.
        public string? Email { get; set; } // Email từ API.
        public string? Phone { get; set; } // Phone từ API.
    }

}
