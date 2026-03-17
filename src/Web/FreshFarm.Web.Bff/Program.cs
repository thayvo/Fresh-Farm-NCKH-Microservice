using FreshFarm.Web.Bff.Areas.Seller.Hubs;
using FreshFarm.Web.Bff.Options;
using FreshFarm.Web.Bff.Services; // Thêm using để dùng ICartSessionService/CartSessionService.
using Microsoft.AspNetCore.Authentication.Cookies; // Su dung cookie auth cho web MVC.
using Microsoft.AspNetCore.Authentication.Google;
using Microsoft.OpenApi.Models; // Cau hinh OpenAPI/Swagger.

var builder = WebApplication.CreateBuilder(args); // Tao host builder cho app.

builder.Services.AddControllersWithViews(); // Bat MVC + Razor views.
builder.Services.AddEndpointsApiExplorer(); // Metadata endpoint cho swagger.

builder.Services.AddDistributedMemoryCache(); // Session store trong memory (du cho MVP local).
builder.Services.AddSession(options => // Cau hinh session middleware.
{
    options.Cookie.Name = "FreshFarm.Bff.Session"; // Ten cookie session de de nhan biet.
    options.Cookie.HttpOnly = true; // Chan javascript doc cookie session.
    options.Cookie.IsEssential = true; // Session van chay du consent cookie.
    options.IdleTimeout = TimeSpan.FromHours(2); // Het han session neu khong thao tac 2h.
});

builder.Services // Dang ky cookie authentication cho user web.
    .AddAuthentication(CookieAuthenticationDefaults.AuthenticationScheme)
    .AddCookie(options =>
    {
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
                var returnUrl = context.Properties?.Items.TryGetValue("returnUrl", out var storedReturnUrl) == true
                    ? storedReturnUrl
                    : null;

                var safeReturnUrl = string.IsNullOrWhiteSpace(returnUrl)
                    ? string.Empty
                    : $"?returnUrl={Uri.EscapeDataString(returnUrl)}&externalError={Uri.EscapeDataString(context.Failure?.Message ?? "access_denied")}";

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

app.UseHttpsRedirection(); // Ep HTTPS.
app.UseStaticFiles(); // Phuc vu css/js/image.
app.UseRouting(); // Route matching.

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
app.MapHub<SupportChatHub>("/hubs/support-chat");
app.MapControllerRoute( // Map MVC area route cho Seller/Admin modules.
    name: "areas",
    pattern: "{area:exists}/{controller=Home}/{action=Dashboard}/{id?}");
app.MapControllerRoute( // Map MVC default route.
    name: "default",
    pattern: "{controller=Home}/{action=Index}/{id?}");

app.MapGet("/health", () => Results.Ok("ok")); // Health check endpoint.

app.Run(); // Chay app.
