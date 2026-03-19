using FreshFarm.Web.Bff.Areas.Seller.Hubs;
using FreshFarm.Web.Bff.Options;
using FreshFarm.Web.Bff.Services; // Thêm using để dùng ICartSessionService/CartSessionService.
using Microsoft.AspNetCore.Authentication.Cookies; // Su dung cookie auth cho web MVC.
using Microsoft.AspNetCore.HttpOverrides;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.AspNetCore.Authentication.Google;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.OpenApi.Models; // Cau hinh OpenAPI/Swagger.
using StackExchange.Redis;
using System.Net;
using System.Security.Cryptography;
using System.Text;
using System.Threading.RateLimiting;

var builder = WebApplication.CreateBuilder(args); // Tao host builder cho app.

var dataProtectionKeysPath = builder.Configuration["DataProtection:KeysPath"];
if (string.IsNullOrWhiteSpace(dataProtectionKeysPath))
{
    dataProtectionKeysPath = Path.Combine(builder.Environment.ContentRootPath, "App_Data", "DataProtectionKeys");
}

Directory.CreateDirectory(dataProtectionKeysPath);
builder.Services.AddDataProtection()
    .PersistKeysToFileSystem(new DirectoryInfo(dataProtectionKeysPath))
    .SetApplicationName("FreshFarm.Bff");

builder.Services.Configure<SessionStoreOptions>(
    builder.Configuration.GetSection(SessionStoreOptions.SectionName));

var sessionStoreOptions = builder.Configuration
    .GetSection(SessionStoreOptions.SectionName)
    .Get<SessionStoreOptions>() ?? new SessionStoreOptions();

builder.Services.AddControllersWithViews(); // Bat MVC + Razor views.
builder.Services.AddEndpointsApiExplorer(); // Metadata endpoint cho swagger.
builder.Services.AddRateLimiter(options =>
{
    options.RejectionStatusCode = StatusCodes.Status429TooManyRequests;
    options.OnRejected = async (context, cancellationToken) =>
    {
        context.HttpContext.Response.ContentType = "text/plain; charset=utf-8";
        await context.HttpContext.Response.WriteAsync(
            "Bạn thao tác quá nhanh. Vui lòng chờ một lát rồi thử lại.",
            cancellationToken);
    };

    options.AddPolicy("auth-form", httpContext =>
        RateLimitPartition.GetFixedWindowLimiter(
            partitionKey: BuildRateLimitKey(httpContext, "auth-form"),
            factory: _ => new FixedWindowRateLimiterOptions
            {
                PermitLimit = 5,
                Window = TimeSpan.FromMinutes(1),
                QueueLimit = 0,
                AutoReplenishment = true
            }));

    options.AddPolicy("password-recovery", httpContext =>
        RateLimitPartition.GetFixedWindowLimiter(
            partitionKey: BuildRateLimitKey(httpContext, "password-recovery"),
            factory: _ => new FixedWindowRateLimiterOptions
            {
                PermitLimit = 3,
                Window = TimeSpan.FromMinutes(15),
                QueueLimit = 0,
                AutoReplenishment = true
            }));

    options.AddPolicy("support-chat", httpContext =>
        RateLimitPartition.GetFixedWindowLimiter(
            partitionKey: BuildRateLimitKey(httpContext, "support-chat"),
            factory: _ => new FixedWindowRateLimiterOptions
            {
                PermitLimit = 20,
                Window = TimeSpan.FromSeconds(30),
                QueueLimit = 0,
                AutoReplenishment = true
            }));

    options.AddPolicy("support-chat-hub", httpContext =>
        RateLimitPartition.GetFixedWindowLimiter(
            partitionKey: BuildRateLimitKey(httpContext, "support-chat-hub"),
            factory: _ => new FixedWindowRateLimiterOptions
            {
                PermitLimit = 10,
                Window = TimeSpan.FromMinutes(1),
                QueueLimit = 0,
                AutoReplenishment = true
            }));
});

if (string.Equals(sessionStoreOptions.Provider, "Redis", StringComparison.OrdinalIgnoreCase))
{
    if (string.IsNullOrWhiteSpace(sessionStoreOptions.RedisConnectionString))
    {
        throw new InvalidOperationException(
            "SessionStore:RedisConnectionString chưa được cấu hình dù Provider = Redis.");
    }

    builder.Services.AddStackExchangeRedisCache(options =>
    {
        var redisConfiguration = ConfigurationOptions.Parse(sessionStoreOptions.RedisConnectionString, true);
        redisConfiguration.AbortOnConnectFail = sessionStoreOptions.AbortOnConnectFail;
        redisConfiguration.ConnectTimeout = sessionStoreOptions.ConnectTimeoutMilliseconds;
        redisConfiguration.Ssl = sessionStoreOptions.Ssl;
        options.ConfigurationOptions = redisConfiguration;
        options.InstanceName = sessionStoreOptions.InstanceName;
    });
}
else
{
    builder.Services.AddDistributedMemoryCache(); // Session store fallback cho local/dev mot instance.
}

builder.Services.Configure<ForwardedHeadersOptions>(options =>
{
    options.ForwardedHeaders =
        ForwardedHeaders.XForwardedFor |
        ForwardedHeaders.XForwardedProto |
        ForwardedHeaders.XForwardedHost;
    options.ForwardLimit = 2; // Trinh duyet -> Cloudflare -> cloudflared local.
    options.KnownProxies.Add(IPAddress.Loopback); // Chi tin proxy local do cloudflared ket noi vao app.
    options.KnownProxies.Add(IPAddress.IPv6Loopback);
});
builder.Services.AddSession(options => // Cau hinh session middleware.
{
    options.Cookie.Name = "FreshFarm.Bff.Session"; // Ten cookie session de de nhan biet.
    options.Cookie.HttpOnly = true; // Chan javascript doc cookie session.
    options.Cookie.IsEssential = true; // Session van chay du consent cookie.
    options.Cookie.SecurePolicy = CookieSecurePolicy.Always; // Cookie session chi gui qua HTTPS.
    options.Cookie.SameSite = SameSiteMode.Lax; // Giu top-level redirect flow nhu Google/VNPay.
    options.IdleTimeout = TimeSpan.FromHours(2); // Het han session neu khong thao tac 2h.
});

builder.Services // Dang ky cookie authentication cho user web.
    .AddAuthentication(CookieAuthenticationDefaults.AuthenticationScheme)
    .AddCookie(options =>
    {
        options.Cookie.Name = "FreshFarm.Bff.Auth";
        options.Cookie.HttpOnly = true;
        options.Cookie.IsEssential = true;
        options.Cookie.SecurePolicy = CookieSecurePolicy.Always;
        options.Cookie.SameSite = SameSiteMode.Lax;
        options.LoginPath = "/account/signin"; // Chua login -> redirect signin.
        options.AccessDeniedPath = "/account/signin"; // Tam thoi redirect signin cho MVP.
        options.SlidingExpiration = true; // User hoat dong thi reset han cookie.
        options.ExpireTimeSpan = TimeSpan.FromHours(2); // Han cookie auth.
        options.Events = new CookieAuthenticationEvents
        {
            OnRedirectToLogin = context =>
            {
                var returnUrl = context.Request.PathBase + context.Request.Path + context.Request.QueryString;
                var loginPath = "/account/signin";

                if (context.Request.Path.StartsWithSegments("/Admin", StringComparison.OrdinalIgnoreCase))
                {
                    loginPath = "/Admin/AdminAccount/Login";
                }
                else if (context.Request.Path.StartsWithSegments("/Seller", StringComparison.OrdinalIgnoreCase))
                {
                    loginPath = "/Seller/SellerAccount/Login";
                }

                context.Response.Redirect($"{loginPath}?returnUrl={Uri.EscapeDataString(returnUrl)}");
                return Task.CompletedTask;
            },
            OnRedirectToAccessDenied = context =>
            {
                var returnUrl = context.Request.PathBase + context.Request.Path + context.Request.QueryString;
                var loginPath = "/account/signin";

                if (context.Request.Path.StartsWithSegments("/Admin", StringComparison.OrdinalIgnoreCase))
                {
                    loginPath = "/Admin/AdminAccount/Login";
                }
                else if (context.Request.Path.StartsWithSegments("/Seller", StringComparison.OrdinalIgnoreCase))
                {
                    loginPath = "/Seller/SellerAccount/Login";
                }

                context.Response.Redirect($"{loginPath}?returnUrl={Uri.EscapeDataString(returnUrl)}");
                return Task.CompletedTask;
            }
        };
    })
    .AddCookie("GoogleExternal", options =>
    {
        options.Cookie.Name = "FreshFarm.Bff.GoogleExternal";
        options.Cookie.HttpOnly = true;
        options.Cookie.IsEssential = true;
        options.Cookie.SecurePolicy = CookieSecurePolicy.Always;
        options.Cookie.SameSite = SameSiteMode.Lax;
        options.ExpireTimeSpan = TimeSpan.FromMinutes(10);
    });

builder.Services.Configure<GoogleAuthenticationOptions>(
    builder.Configuration.GetSection(GoogleAuthenticationOptions.SectionName));
builder.Services.Configure<VnPayOptions>(
    builder.Configuration.GetSection(VnPayOptions.SectionName));

var googleAuthOptions = builder.Configuration
    .GetSection(GoogleAuthenticationOptions.SectionName)
    .Get<GoogleAuthenticationOptions>() ?? new GoogleAuthenticationOptions();

if (googleAuthOptions.IsConfigured)
{
    builder.Services.AddAuthentication()
        .AddGoogle("Google", options =>
        {
            options.ClientId = googleAuthOptions.ClientId;
            options.ClientSecret = googleAuthOptions.ClientSecret;
            options.SignInScheme = "GoogleExternal";
            options.CallbackPath = "/signin-google";
            options.SaveTokens = false;
            options.Events.OnRemoteFailure = context =>
            {
                var loggerFactory = context.HttpContext.RequestServices.GetRequiredService<ILoggerFactory>();
                var logger = loggerFactory.CreateLogger("GoogleAuthentication");
                var returnUrl = context.Properties?.Items.TryGetValue("returnUrl", out var storedReturnUrl) == true
                    ? storedReturnUrl
                    : null;
                var failureMessage = context.Failure?.Message ?? "access_denied";

                if (failureMessage.Contains("access_denied", StringComparison.OrdinalIgnoreCase))
                {
                    logger.LogInformation("Nguoi dung da huy dang nhap Google. returnUrl={ReturnUrl}", returnUrl);
                }
                else
                {
                    logger.LogWarning(
                        context.Failure,
                        "Google login that bai trong remote callback. returnUrl={ReturnUrl}",
                        returnUrl);
                }

                var safeReturnUrl = string.IsNullOrWhiteSpace(returnUrl)
                    ? string.Empty
                    : $"?returnUrl={Uri.EscapeDataString(returnUrl)}&externalError={Uri.EscapeDataString(failureMessage)}";

                context.Response.Redirect($"/account/signin{safeReturnUrl}");
                context.HandleResponse();
                return Task.CompletedTask;
            };
        });
}

builder.Services.AddAuthorization(options =>
{
    options.AddPolicy("SellerOnly", policy => policy.RequireRole("Seller"));
    options.AddPolicy("AdminOnly", policy => policy.RequireRole("Admin"));
}); // Bat [Authorize] va policy role cho area controllers.
builder.Services.AddSignalR();

builder.Services.AddHttpClient("Identity", client => // HttpClient typed by name cho Identity API.
{
    var baseUrl = builder.Configuration["Services:Identity:BaseUrl"]; // Doc base url tu config.
    client.BaseAddress = new Uri(baseUrl!); // Gan base address.
});

builder.Services.AddHttpClient("Catalog", client => // HttpClient cho Catalog API.
{
    var baseUrl = builder.Configuration["Services:Catalog:BaseUrl"]; // Doc base url tu config.
    client.BaseAddress = new Uri(baseUrl!); // Gan base address.
});

builder.Services.AddHttpClient("Ordering", client => // HttpClient cho Ordering API.
{
    var baseUrl = builder.Configuration["Services:Ordering:BaseUrl"]; // Doc base url tu config.
    client.BaseAddress = new Uri(baseUrl!); // Gan base address.
});

builder.Services.Configure<GhnSandboxOptions>(builder.Configuration.GetSection(GhnSandboxOptions.SectionName));
builder.Services.AddHttpClient("GhnSandbox", client =>
{
    var baseUrl = builder.Configuration[$"{GhnSandboxOptions.SectionName}:BaseUrl"];
    client.BaseAddress = new Uri(baseUrl!);
});
builder.Services.AddScoped<IGhnSandboxService, GhnSandboxService>();

builder.Services.AddSwaggerGen(c => // Swagger cho endpoint API o BFF.
{
    c.SwaggerDoc("v1", new OpenApiInfo { Title = "FreshFarm.Web.Bff", Version = "v1" }); // Metadata.
});

builder.Services.AddHttpContextAccessor(); // Bắt buộc vì CartSessionService cần HttpContext.
builder.Services.AddScoped<ICartSessionService, CartSessionService>(); // Mỗi request dùng 1 instance service cart.
builder.Services.AddScoped<IProductImageStorageService, ProductImageStorageService>(); // Lưu/xóa ảnh sản phẩm trong wwwroot/uploads/products.
builder.Services.AddSingleton<IVnPayService, VnPayService>(); // Ký URL + verify callback VNPay sandbox.

var app = builder.Build(); // Build app pipeline.

if (!app.Environment.IsDevelopment()) // Pipeline production.
{
    app.UseExceptionHandler("/Home/Error"); // Trang loi chung.
    app.UseHsts(); // Bat HSTS.
}

if (app.Environment.IsDevelopment()) // Dev moi mo swagger.
{
    app.UseSwagger(); // Tao swagger json.
    app.UseSwaggerUI(); // UI swagger.
}

app.UseForwardedHeaders(); // Doc X-Forwarded-* truoc redirect/auth/link generation khi chay sau Cloudflare Tunnel.

app.Use(async (context, next) =>
{
    context.Items["CspNonce"] = Convert.ToBase64String(RandomNumberGenerator.GetBytes(16));

    context.Response.OnStarting(() =>
    {
        var headers = context.Response.Headers;
        var cspNonce = context.Items["CspNonce"] as string ?? string.Empty;
        var cspPrefix = new StringBuilder()
            .Append("default-src 'self'; ")
            .Append("base-uri 'self'; ")
            .Append("frame-ancestors 'self'; ")
            .Append("form-action 'self'; ")
            .Append("object-src 'none'; ")
            .Append("img-src 'self' data: https:; ")
            .Append("font-src 'self' data: https://cdn.jsdelivr.net; ")
            .Append("connect-src 'self' https: wss:; ");

        var strictScriptDirective = new StringBuilder()
            .Append("script-src 'self' https://cdn.jsdelivr.net https://cdn.ckeditor.com; ")
            .Append("style-src 'self' 'unsafe-inline' https://cdn.jsdelivr.net https://fonts.googleapis.com; ")
            .Append("frame-src 'self' https:;");

        var legacyScriptDirective = new StringBuilder()
            .Append("script-src 'self' 'unsafe-inline' 'nonce-")
            .Append(cspNonce)
            .Append("' https://cdn.jsdelivr.net https://cdn.ckeditor.com; ")
            .Append("style-src 'self' 'unsafe-inline' https://cdn.jsdelivr.net https://fonts.googleapis.com; ")
            .Append("frame-src 'self' https:;");

        var requestPath = context.Request.Path;
        var useReportOnly =
            requestPath.StartsWithSegments("/Admin", StringComparison.OrdinalIgnoreCase) ||
            requestPath.StartsWithSegments("/Seller", StringComparison.OrdinalIgnoreCase) ||
            requestPath.StartsWithSegments("/swagger", StringComparison.OrdinalIgnoreCase);

        headers["X-Content-Type-Options"] = "nosniff";
        headers["X-Frame-Options"] = "SAMEORIGIN";
        headers["Referrer-Policy"] = "strict-origin-when-cross-origin";
        headers["Permissions-Policy"] = "camera=(), microphone=(), geolocation=(), browsing-topics=()";
        if (useReportOnly)
        {
            headers["Content-Security-Policy-Report-Only"] = cspPrefix.ToString() + legacyScriptDirective.ToString();
            headers.Remove("Content-Security-Policy");
        }
        else
        {
            headers["Content-Security-Policy"] = cspPrefix.ToString() + strictScriptDirective.ToString();
            headers.Remove("Content-Security-Policy-Report-Only");
        }
        return Task.CompletedTask;
    });

    await next();
});

app.UseHttpsRedirection(); // Ep HTTPS.
app.UseStaticFiles(); // Phuc vu css/js/image.
app.UseRouting(); // Route matching.

app.UseRateLimiter(); // Gioi han tan suat cho endpoint nhay cam cong khai.
app.UseSession(); // IMPORTANT: session truoc auth neu action can doc session token.
app.UseAuthentication(); // Doc cookie auth.
app.UseAuthorization(); // Enforce [Authorize].

app.Use(async (context, next) =>
{
    if (context.User.Identity?.IsAuthenticated == true && context.User.IsInRole("Seller"))
    {
        var path = context.Request.Path.Value ?? string.Empty;
        if (path.StartsWith("/Seller/Category", StringComparison.OrdinalIgnoreCase) ||
            path.StartsWith("/Seller/Unit", StringComparison.OrdinalIgnoreCase))
        {
            context.Response.Redirect("/Seller/Home/Dashboard");
            return;
        }
    }

    await next();
});

app.MapControllers(); // Map API controllers.
app.MapHub<SupportChatHub>("/hubs/support-chat").RequireRateLimiting("support-chat-hub");
app.MapControllerRoute( // Map MVC area route cho Seller/Admin modules.
    name: "areas",
    pattern: "{area:exists}/{controller=Home}/{action=Dashboard}/{id?}");
app.MapControllerRoute( // Map MVC default route.
    name: "default",
    pattern: "{controller=Home}/{action=Index}/{id?}");

app.MapGet("/health", () => Results.Ok("ok")); // Health check endpoint.

app.Run(); // Chay app.

static string BuildRateLimitKey(HttpContext context, string scope)
{
    var userKey =
        context.User.FindFirst("sub")?.Value ??
        context.User.FindFirst(System.Security.Claims.ClaimTypes.NameIdentifier)?.Value ??
        context.Connection.RemoteIpAddress?.ToString() ??
        "anonymous";

    return $"{scope}:{userKey}";
}
