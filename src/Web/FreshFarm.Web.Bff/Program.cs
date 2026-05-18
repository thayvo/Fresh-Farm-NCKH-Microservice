using FreshFarm.Web.Bff.Areas.Seller.Hubs;
using FreshFarm.Web.Bff.Options;
using FreshFarm.Web.Bff.Services; // Thêm using để dùng ICartSessionService/CartSessionService.
using Microsoft.AspNetCore.Authentication.Cookies; // Su dung cookie auth cho web MVC.
using Microsoft.AspNetCore.Diagnostics.HealthChecks;
using Microsoft.AspNetCore.Http.Features;
using Microsoft.AspNetCore.HttpOverrides;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.AspNetCore.Authentication.Google;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using Microsoft.OpenApi.Models; // Cau hinh OpenAPI/Swagger.
using StackExchange.Redis;
using System.Net;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Threading.RateLimiting;

var builder = WebApplication.CreateBuilder(args); // Tao host builder cho app.
builder.Configuration.AddJsonFile(
    Path.Combine("App_Data", "session-aware-rerank-tuning.json"),
    optional: true,
    reloadOnChange: true);

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
builder.Services.Configure<IdleSessionOptions>(
    builder.Configuration.GetSection(IdleSessionOptions.SectionName));
builder.Services.Configure<OrderingServiceOptions>(
    builder.Configuration.GetSection(OrderingServiceOptions.SectionName));
builder.Services.Configure<GhnBackgroundSyncOptions>(
    builder.Configuration.GetSection(GhnBackgroundSyncOptions.SectionName));
builder.Services.Configure<GhnOrderStatusWebhookOptions>(
    builder.Configuration.GetSection(GhnOrderStatusWebhookOptions.SectionName));
builder.Services.Configure<SessionAwareRecommendationOptions>(
    builder.Configuration.GetSection(SessionAwareRecommendationOptions.SectionName));
builder.Services.Configure<RecommendationExperimentOptions>(
    builder.Configuration.GetSection(RecommendationExperimentOptions.SectionName));
builder.Services.Configure<SessionAwareRecommendationTuningOptions>(
    builder.Configuration.GetSection(SessionAwareRecommendationTuningOptions.SectionName));
builder.Services.Configure<MultiObjectiveRecommendationRolloutOptions>(
    builder.Configuration.GetSection(MultiObjectiveRecommendationRolloutOptions.SectionName));
builder.Services.AddSingleton<IRateLimitTelemetryService, RateLimitTelemetryService>();

var sessionStoreOptions = builder.Configuration
    .GetSection(SessionStoreOptions.SectionName)
    .Get<SessionStoreOptions>() ?? new SessionStoreOptions();
var idleSessionOptions = builder.Configuration
    .GetSection(IdleSessionOptions.SectionName)
    .Get<IdleSessionOptions>() ?? new IdleSessionOptions();

builder.Services.AddControllersWithViews(); // Bat MVC + Razor views.
builder.Services.AddMemoryCache();
builder.Services.AddEndpointsApiExplorer(); // Metadata endpoint cho swagger.
builder.Services.Configure<FormOptions>(options =>
{
    options.MultipartBodyLengthLimit = 10 * 1024 * 1024; // Gioi han upload/form body 10 MB cho request multipart.
    options.ValueLengthLimit = 1024 * 1024; // Han che field text qua lon trong form.
    options.MultipartHeadersLengthLimit = 32 * 1024; // Giam rui ro header multipart bat thuong.
});
builder.Services.AddRateLimiter(options =>
{
    options.RejectionStatusCode = StatusCodes.Status429TooManyRequests;
    options.OnRejected = async (context, cancellationToken) =>
    {
        var loggerFactory = context.HttpContext.RequestServices.GetRequiredService<ILoggerFactory>();
        var logger = loggerFactory.CreateLogger("FreshFarm.Web.Bff.RateLimiting");
        var telemetryService = context.HttpContext.RequestServices.GetRequiredService<IRateLimitTelemetryService>();
        var endpointName = context.HttpContext.GetEndpoint()?.DisplayName ?? "unknown-endpoint";
        var retryAfter = context.Lease.TryGetMetadata(MetadataName.RetryAfter, out var retryAfterValue)
            ? retryAfterValue.ToString()
            : null;
        var client = ResolveRateLimitActor(context.HttpContext);
        telemetryService.RecordRejectedRequest(
            endpointName,
            context.HttpContext.Request.Method,
            context.HttpContext.Request.Path.Value ?? "/",
            client,
            context.HttpContext.TraceIdentifier,
            retryAfter,
            context.HttpContext.Request.Headers.UserAgent.ToString());
        logger.LogWarning(
            "Rate limiter chan request. Endpoint={Endpoint}, Method={Method}, Path={Path}, Client={Client}, TraceId={TraceId}, RetryAfter={RetryAfter}, UserAgent={UserAgent}",
            endpointName,
            context.HttpContext.Request.Method,
            context.HttpContext.Request.Path.Value,
            client,
            context.HttpContext.TraceIdentifier,
            retryAfter,
            context.HttpContext.Request.Headers.UserAgent.ToString());

        var loginRedirectPath = ResolveRateLimitLoginRedirectPath(context.HttpContext.Request.Path);
        if (!string.IsNullOrWhiteSpace(loginRedirectPath))
        {
            var destination = string.IsNullOrWhiteSpace(retryAfter)
                ? $"{loginRedirectPath}?rateLimitError=1"
                : $"{loginRedirectPath}?rateLimitError=1&retryAfter={Uri.EscapeDataString(retryAfter)}";
            context.HttpContext.Response.Redirect(destination, permanent: false);
            return;
        }

        context.HttpContext.Response.ContentType = "text/plain; charset=utf-8";
        await context.HttpContext.Response.WriteAsync(
            "Bạn thao tác quá nhanh. Vui lòng chờ một lát rồi thử lại.",
            cancellationToken);
    };

    options.GlobalLimiter = PartitionedRateLimiter.Create<HttpContext, string>(httpContext =>
        RateLimitPartition.GetFixedWindowLimiter(
            partitionKey: BuildRateLimitKey(httpContext, "global"),
            factory: _ => new FixedWindowRateLimiterOptions
            {
                PermitLimit = 900,
                Window = TimeSpan.FromMinutes(1),
                QueueLimit = 0,
                AutoReplenishment = true
            }));

    options.AddPolicy("auth-form", httpContext =>
        RateLimitPartition.GetNoLimiter(
            partitionKey: BuildRateLimitKey(httpContext, "auth-form")));

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

    options.AddPolicy("public-read", httpContext =>
        RateLimitPartition.GetFixedWindowLimiter(
            partitionKey: BuildRateLimitKey(httpContext, "public-read"),
            factory: _ => new FixedWindowRateLimiterOptions
            {
                PermitLimit = 900,
                Window = TimeSpan.FromMinutes(1),
                QueueLimit = 0,
                AutoReplenishment = true
            }));

    options.AddPolicy("search-read", httpContext =>
        RateLimitPartition.GetFixedWindowLimiter(
            partitionKey: BuildRateLimitKey(httpContext, "search-read"),
            factory: _ => new FixedWindowRateLimiterOptions
            {
                PermitLimit = 30,
                Window = TimeSpan.FromSeconds(30),
                QueueLimit = 0,
                AutoReplenishment = true
            }));

    options.AddPolicy("cart-write", httpContext =>
        RateLimitPartition.GetFixedWindowLimiter(
            partitionKey: BuildRateLimitKey(httpContext, "cart-write"),
            factory: _ => new FixedWindowRateLimiterOptions
            {
                PermitLimit = 20,
                Window = TimeSpan.FromMinutes(1),
                QueueLimit = 0,
                AutoReplenishment = true
            }));

    options.AddPolicy("review-write", httpContext =>
        RateLimitPartition.GetFixedWindowLimiter(
            partitionKey: BuildRateLimitKey(httpContext, "review-write"),
            factory: _ => new FixedWindowRateLimiterOptions
            {
                PermitLimit = 10,
                Window = TimeSpan.FromMinutes(1),
                QueueLimit = 0,
                AutoReplenishment = true
            }));

    options.AddPolicy("checkout-read", httpContext =>
        RateLimitPartition.GetFixedWindowLimiter(
            partitionKey: BuildRateLimitKey(httpContext, "checkout-read"),
            factory: _ => new FixedWindowRateLimiterOptions
            {
                PermitLimit = 30,
                Window = TimeSpan.FromMinutes(1),
                QueueLimit = 0,
                AutoReplenishment = true
            }));

    options.AddPolicy("checkout-write", httpContext =>
        RateLimitPartition.GetFixedWindowLimiter(
            partitionKey: BuildRateLimitKey(httpContext, "checkout-write"),
            factory: _ => new FixedWindowRateLimiterOptions
            {
                PermitLimit = 12,
                Window = TimeSpan.FromMinutes(1),
                QueueLimit = 0,
                AutoReplenishment = true
            }));

    options.AddPolicy("ghn-read", httpContext =>
        RateLimitPartition.GetFixedWindowLimiter(
            partitionKey: BuildRateLimitKey(httpContext, "ghn-read"),
            factory: _ => new FixedWindowRateLimiterOptions
            {
                PermitLimit = 20,
                Window = TimeSpan.FromSeconds(30),
                QueueLimit = 0,
                AutoReplenishment = true
            }));
});

var redisCacheState = ConfigureDistributedCache(builder.Services, sessionStoreOptions);
builder.Services.AddSingleton(redisCacheState);
builder.Services.AddHealthChecks()
    .AddCheck<RedisHealthCheck>("redis", tags: new[] { "redis", "dependency" });

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
    options.IdleTimeout = TimeSpan.FromMinutes(idleSessionOptions.ServerSessionIdleTimeoutMinutes); // Het han session neu khong thao tac vuot nguong.
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
        options.ExpireTimeSpan = TimeSpan.FromMinutes(idleSessionOptions.AuthenticationLifetimeMinutes); // Han cookie auth.
        options.Events = new CookieAuthenticationEvents
        {
            OnRedirectToLogin = context =>
            {
                if (context.Request.Path.StartsWithSegments("/api", StringComparison.OrdinalIgnoreCase))
                {
                    context.Response.StatusCode = StatusCodes.Status401Unauthorized;
                    return Task.CompletedTask;
                }

                var returnUrl = ResolveInteractiveReturnUrl(context.Request);
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
                if (context.Request.Path.StartsWithSegments("/api", StringComparison.OrdinalIgnoreCase))
                {
                    context.Response.StatusCode = StatusCodes.Status403Forbidden;
                    return Task.CompletedTask;
                }

                var returnUrl = ResolveInteractiveReturnUrl(context.Request);
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
builder.Services.Configure<GoogleRecaptchaOptions>(
    builder.Configuration.GetSection(GoogleRecaptchaOptions.SectionName));
builder.Services.Configure<VnPayOptions>(
    builder.Configuration.GetSection(VnPayOptions.SectionName));

var vnpayOptionsForCsp = builder.Configuration
    .GetSection(VnPayOptions.SectionName)
    .Get<VnPayOptions>() ?? new VnPayOptions();
var cspFormActionSources = BuildCspFormActionSources(vnpayOptionsForCsp);

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
builder.Services.AddHttpClient<IGoogleRecaptchaService, GoogleRecaptchaService>();

builder.Services.Configure<GhnSandboxOptions>(builder.Configuration.GetSection(GhnSandboxOptions.SectionName));
builder.Services.AddHttpClient("GhnSandbox", client =>
{
    var baseUrl = builder.Configuration[$"{GhnSandboxOptions.SectionName}:BaseUrl"];
    client.BaseAddress = new Uri(baseUrl!);
});
builder.Services.AddScoped<IGhnSandboxService, GhnSandboxService>();
builder.Services.AddHostedService<GhnMetadataSyncBackgroundService>();

builder.Services.AddSwaggerGen(c => // Swagger cho endpoint API o BFF.
{
    c.SwaggerDoc("v1", new OpenApiInfo { Title = "FreshFarm.Web.Bff", Version = "v1" }); // Metadata.
});

builder.Services.AddHttpContextAccessor(); // Bắt buộc vì CartSessionService cần HttpContext.
builder.Services.AddScoped<ICartSessionService, CartSessionService>(); // Mỗi request dùng 1 instance service cart.
builder.Services.AddScoped<ISessionAwareRecommendationReranker, SessionAwareRecommendationReranker>(); // Re-rank ML.NET candidates with Redis session signals.
builder.Services.AddSingleton<ISessionSignalService, SessionSignalService>(); // Lưu recent search/click vào Redis cho recommendation session.
builder.Services.AddSingleton<IRecommendationMetricsClient, RecommendationMetricsClient>(); // Fire-and-forget recommendation impression/click metrics to Ordering.
builder.Services.AddSingleton<IRecommendationExperimentService, RecommendationExperimentService>(); // Stable A/B group assignment for recommendation strategy experiments.
builder.Services.AddSingleton<ISessionAwareRecommendationConfigStore, JsonSessionAwareRecommendationConfigStore>(); // Persist tuned rerank weights to reloadable config.
builder.Services.AddSingleton<ISessionAwareRecommendationWeightTuningService, SessionAwareRecommendationWeightTuningService>(); // Daily CTR-by-position based weight tuning.
builder.Services.AddHostedService<SessionAwareRecommendationWeightTuningBackgroundService>();
builder.Services.AddSingleton<IMultiObjectiveRecommendationRolloutService, MultiObjectiveRecommendationRolloutService>(); // Hourly guarded rollout and objective-metric guardrails.
builder.Services.AddHostedService<MultiObjectiveRecommendationRolloutBackgroundService>();
builder.Services.AddScoped<IProductImageStorageService, ProductImageStorageService>(); // Lưu/xóa ảnh sản phẩm trong wwwroot/uploads/products.
builder.Services.AddScoped<ISellerKycStorageService, SellerKycStorageService>(); // Lưu/xóa giấy tờ KYC seller trong wwwroot/uploads/seller-kyc.
builder.Services.AddSingleton<IVnPayService, VnPayService>(); // Ký URL + verify callback VNPay sandbox.
builder.Services.AddSingleton<ISignUpCaptchaService, SignUpCaptchaService>();

var app = builder.Build(); // Build app pipeline.
var redisSessionCacheState = app.Services.GetRequiredService<RedisSessionCacheState>();
if (redisSessionCacheState.RedisConfigured && !redisSessionCacheState.UsingRedis)
{
    app.Logger.LogWarning(
        "Redis session cache unavailable at startup. Falling back to in-memory IDistributedCache. Reason={Reason}",
        redisSessionCacheState.FailureReason);
}
else if (redisSessionCacheState.UsingRedis)
{
    app.Logger.LogInformation("Redis session cache is available and active.");
}

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
            .Append("form-action ")
            .Append(cspFormActionSources)
            .Append("; ")
            .Append("object-src 'none'; ")
            .Append("img-src 'self' data: https:; ")
            .Append("font-src 'self' data: https://cdn.jsdelivr.net https://fonts.gstatic.com; ")
            .Append("connect-src 'self' https: wss:; ");

        var strictScriptDirective = new StringBuilder()
            .Append("script-src 'self' https://cdn.jsdelivr.net https://cdn.ckeditor.com https://www.google.com https://www.gstatic.com; ")
            .Append("style-src 'self' 'unsafe-inline' https://cdn.jsdelivr.net https://fonts.googleapis.com; ")
            .Append("frame-src 'self' https://www.google.com https://www.gstatic.com https:;");

        var legacyScriptDirective = new StringBuilder()
            .Append("script-src 'self' 'unsafe-inline' 'nonce-")
            .Append(cspNonce)
            .Append("' https://cdn.jsdelivr.net https://cdn.ckeditor.com https://www.google.com https://www.gstatic.com; ")
            .Append("style-src 'self' 'unsafe-inline' https://cdn.jsdelivr.net https://fonts.googleapis.com; ")
            .Append("frame-src 'self' https://www.google.com https://www.gstatic.com https:;");

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
app.MapHealthChecks("/health/redis", new HealthCheckOptions
{
    Predicate = registration => string.Equals(registration.Name, "redis", StringComparison.OrdinalIgnoreCase),
    ResponseWriter = WriteHealthCheckResponseAsync
});

app.Run(); // Chay app.

static RedisSessionCacheState ConfigureDistributedCache(
    IServiceCollection services,
    SessionStoreOptions sessionStoreOptions)
{
    var configuredProvider = string.IsNullOrWhiteSpace(sessionStoreOptions.Provider)
        ? "Memory"
        : sessionStoreOptions.Provider.Trim();

    if (!string.Equals(configuredProvider, "Redis", StringComparison.OrdinalIgnoreCase))
    {
        services.AddDistributedMemoryCache(); // Session store fallback cho local/dev mot instance.
        return new RedisSessionCacheState(
            configuredProvider,
            redisConfigured: false,
            usingRedis: false,
            effectiveProvider: "Memory",
            failureReason: "SessionStore:Provider is not Redis.");
    }

    if (string.IsNullOrWhiteSpace(sessionStoreOptions.RedisConnectionString))
    {
        services.AddDistributedMemoryCache();
        return new RedisSessionCacheState(
            configuredProvider,
            redisConfigured: true,
            usingRedis: false,
            effectiveProvider: "Memory",
            failureReason: "SessionStore:RedisConnectionString is empty.");
    }

    try
    {
        var redisConfiguration = BuildRedisConfiguration(sessionStoreOptions, forceAbortOnConnectFail: true);
        var redisConnection = ConnectionMultiplexer.Connect(redisConfiguration);
        if (!redisConnection.IsConnected)
        {
            redisConnection.Dispose();
            services.AddDistributedMemoryCache();
            return new RedisSessionCacheState(
                configuredProvider,
                redisConfigured: true,
                usingRedis: false,
                effectiveProvider: "Memory",
                failureReason: "Redis connection was created but no endpoint is connected.");
        }

        services.AddSingleton<IConnectionMultiplexer>(redisConnection);
        services.AddStackExchangeRedisCache(options =>
        {
            options.ConfigurationOptions = BuildRedisConfiguration(sessionStoreOptions, forceAbortOnConnectFail: false);
            options.InstanceName = sessionStoreOptions.InstanceName;
            options.ConnectionMultiplexerFactory = () => Task.FromResult<IConnectionMultiplexer>(redisConnection);
        });

        return new RedisSessionCacheState(
            configuredProvider,
            redisConfigured: true,
            usingRedis: true,
            effectiveProvider: "Redis",
            failureReason: null);
    }
    catch (Exception ex) when (ex is RedisConnectionException
                              or RedisTimeoutException
                              or TimeoutException
                              or InvalidOperationException
                              or ArgumentException)
    {
        services.AddDistributedMemoryCache();
        return new RedisSessionCacheState(
            configuredProvider,
            redisConfigured: true,
            usingRedis: false,
            effectiveProvider: "Memory",
            failureReason: ex.Message);
    }
}

static ConfigurationOptions BuildRedisConfiguration(
    SessionStoreOptions sessionStoreOptions,
    bool forceAbortOnConnectFail)
{
    var redisConfiguration = ConfigurationOptions.Parse(sessionStoreOptions.RedisConnectionString!, true);
    redisConfiguration.AbortOnConnectFail = forceAbortOnConnectFail || sessionStoreOptions.AbortOnConnectFail;
    redisConfiguration.ConnectTimeout = Math.Clamp(sessionStoreOptions.ConnectTimeoutMilliseconds, 1, 1_000);
    redisConfiguration.SyncTimeout = Math.Clamp(sessionStoreOptions.ConnectTimeoutMilliseconds, 1, 1_000);
    redisConfiguration.Ssl = sessionStoreOptions.Ssl;
    return redisConfiguration;
}

static Task WriteHealthCheckResponseAsync(HttpContext context, HealthReport report)
{
    context.Response.ContentType = "application/json; charset=utf-8";
    var payload = new
    {
        status = report.Status.ToString(),
        checks = report.Entries.Select(entry => new
        {
            name = entry.Key,
            status = entry.Value.Status.ToString(),
            description = entry.Value.Description,
            durationMs = Math.Round(entry.Value.Duration.TotalMilliseconds, 2),
            data = entry.Value.Data
        })
    };

    return context.Response.WriteAsync(JsonSerializer.Serialize(payload));
}

static string BuildRateLimitKey(HttpContext context, string scope)
{
    return $"{scope}:{ResolveRateLimitActor(context)}";
}

static string? ResolveRateLimitLoginRedirectPath(PathString requestPath)
{
    if (requestPath.StartsWithSegments("/account/signin", StringComparison.OrdinalIgnoreCase))
    {
        return "/account/signin";
    }

    if (requestPath.StartsWithSegments("/Admin/AdminAccount/Login", StringComparison.OrdinalIgnoreCase))
    {
        return "/Admin/AdminAccount/Login";
    }

    if (requestPath.StartsWithSegments("/Seller/SellerAccount/Login", StringComparison.OrdinalIgnoreCase) ||
        requestPath.StartsWithSegments("/Seller/AdminAccount/Login", StringComparison.OrdinalIgnoreCase))
    {
        return "/Seller/SellerAccount/Login";
    }

    return null;
}

static string ResolveRateLimitActor(HttpContext context)
{
    return
        context.User.FindFirst("sub")?.Value ??
        context.User.FindFirst(System.Security.Claims.ClaimTypes.NameIdentifier)?.Value ??
        context.Connection.RemoteIpAddress?.ToString() ??
        "anonymous";
}

static string ResolveInteractiveReturnUrl(HttpRequest request)
{
    if (HttpMethods.IsGet(request.Method) || HttpMethods.IsHead(request.Method))
    {
        return request.PathBase + request.Path + request.QueryString;
    }

    var referer = request.Headers.Referer.ToString();
    if (Uri.TryCreate(referer, UriKind.Absolute, out var absoluteReferer) &&
        string.Equals(absoluteReferer.Authority, request.Host.Value, StringComparison.OrdinalIgnoreCase))
    {
        return absoluteReferer.PathAndQuery;
    }

    if (Uri.TryCreate(referer, UriKind.Relative, out _) &&
        referer.StartsWith("/", StringComparison.Ordinal) &&
        !referer.StartsWith("//", StringComparison.Ordinal))
    {
        return referer;
    }

    return "/";
}

static string BuildCspFormActionSources(VnPayOptions vnPayOptions)
{
    var sources = new List<string> { "'self'" };

    if (!string.IsNullOrWhiteSpace(vnPayOptions.BaseUrl)
        && Uri.TryCreate(vnPayOptions.BaseUrl, UriKind.Absolute, out var vnPayUri))
    {
        var origin = $"{vnPayUri.Scheme}://{vnPayUri.Authority}";
        if (!sources.Contains(origin, StringComparer.OrdinalIgnoreCase))
        {
            sources.Add(origin);
        }
    }

    return string.Join(" ", sources);
}
