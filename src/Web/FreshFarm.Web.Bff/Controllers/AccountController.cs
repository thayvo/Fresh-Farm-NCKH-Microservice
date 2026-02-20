using System.IdentityModel.Tokens.Jwt; // Dung de parse JWT claim.
using System.Security.Claims; // Dung Claim/ClaimsPrincipal.
using FreshFarm.Web.Bff.Dtos; // Dung DTO vua tao.
using Microsoft.AspNetCore.Authentication; // Dung SignInAsync/SignOutAsync.
using Microsoft.AspNetCore.Authentication.Cookies; // Cookie auth scheme.
using Microsoft.AspNetCore.Authorization; // [Authorize], [AllowAnonymous].
using Microsoft.AspNetCore.Mvc; // Controller, IActionResult.

namespace FreshFarm.Web.Bff.Controllers; // Namespace controller.

public sealed class AccountController : Controller // MVC controller cho auth/account pages.
{
    private const string AccessTokenSessionKey = "ACCESS_TOKEN"; // Key luu JWT trong session.
    private readonly IHttpClientFactory _httpClientFactory; // Factory tao HttpClient theo ten.

    public AccountController(IHttpClientFactory httpClientFactory) // Inject factory qua DI.
    {
        _httpClientFactory = httpClientFactory; // Gan vao field.
    }

    [HttpGet("/account/signin")] // Route GET signin.
    [AllowAnonymous] // Chua login van vao duoc.
    public IActionResult SignIn() // Render view signin.
    {
        return View(); // Views/Account/SignIn.cshtml.
    }

    [HttpPost("/account/signin")] // Route POST signin.
    [ValidateAntiForgeryToken] // Bắt buộc token hợp lệ từ form.
    [AllowAnonymous] // Anonymous submit login.
    public async Task<IActionResult> SignIn(LoginRequestDto request) // Nhan model form.
    {
        if (string.IsNullOrWhiteSpace(request.Identifier) || string.IsNullOrWhiteSpace(request.Password)) // Validate input.
        {
            ModelState.AddModelError(string.Empty, "Vui long nhap day du thong tin."); // Them loi cho view.
            return View(request); // Render lai form.
        }

        var identityClient = _httpClientFactory.CreateClient("Identity"); // Lay client goi Identity API.
        var loginResponse = await identityClient.PostAsJsonAsync("/auth/login", request); // Goi login API.

        if (!loginResponse.IsSuccessStatusCode) // Neu login fail.
        {
            var errorText = await loginResponse.Content.ReadAsStringAsync(); // Doc body loi.
            ModelState.AddModelError(string.Empty, $"Dang nhap that bai: {errorText}"); // Show error.
            return View(request); // O lai form login.
        }

        var auth = await loginResponse.Content.ReadFromJsonAsync<AuthResponseDto>(); // Parse body sang DTO.
        if (auth is null || string.IsNullOrWhiteSpace(auth.AccessToken)) // Bao ve response xau.
        {
            ModelState.AddModelError(string.Empty, "Token tra ve khong hop le."); // Bao loi.
            return View(request); // O lai form.
        }

        HttpContext.Session.SetString(AccessTokenSessionKey, auth.AccessToken); // Luu JWT vao session server-side.

        var jwt = new JwtSecurityTokenHandler().ReadJwtToken(auth.AccessToken); // Parse token de lay claims.

        var claims = new List<Claim> // Tao claim list cho cookie principal.
        {
            new Claim(ClaimTypes.NameIdentifier, jwt.Subject ?? string.Empty), // sub -> user id.
            new Claim(ClaimTypes.Name, jwt.Claims.FirstOrDefault(c => c.Type == "username")?.Value ?? request.Identifier) // Ten hien thi.
        };

        foreach (var roleClaim in jwt.Claims.Where(c => c.Type == ClaimTypes.Role || c.Type == "role")) // Lay role trong JWT.
        {
            claims.Add(new Claim(ClaimTypes.Role, roleClaim.Value)); // Add role vao principal.
        }

        var identity = new ClaimsIdentity(claims, CookieAuthenticationDefaults.AuthenticationScheme); // Tao identity cho cookie.
        var principal = new ClaimsPrincipal(identity); // Tao principal.

        await HttpContext.SignInAsync(CookieAuthenticationDefaults.AuthenticationScheme, principal); // Set cookie auth.

        return RedirectToAction(nameof(OrderHistory)); // Login xong den order history.
    }

    [HttpGet("/account/signup")] // Route GET signup.
    [AllowAnonymous] // Anonymous vao trang dang ky.
    public IActionResult SignUp() // Render view signup.
    {
        return View(); // Views/Account/SignUp.cshtml.
    }

    [HttpPost("/account/signup")] // Route POST signup.
    [ValidateAntiForgeryToken] // Chặn submit giả mạo từ site khác.
    [AllowAnonymous] // Anonymous dang ky.
    public async Task<IActionResult> SignUp(RegisterRequestDto request) // Nhan model form.
    {
        if (!ModelState.IsValid) // Validate MVC model.
        {
            return View(request); // Render lai neu invalid.
        }

        var identityClient = _httpClientFactory.CreateClient("Identity"); // HttpClient cho Identity.
        var registerResponse = await identityClient.PostAsJsonAsync("/auth/register", request); // Goi register API.

        if (!registerResponse.IsSuccessStatusCode) // Register fail.
        {
            var errorText = await registerResponse.Content.ReadAsStringAsync(); // Doc loi.
            ModelState.AddModelError(string.Empty, $"Dang ky that bai: {errorText}"); // Show loi.
            return View(request); // O lai form.
        }

        return RedirectToAction(nameof(SignIn)); // Dang ky thanh cong -> dang nhap.
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
}
