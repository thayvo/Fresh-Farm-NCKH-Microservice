using FreshFarm.Identity.Api.Models;
using FreshFarm.Identity.Api.Options;
using FreshFarm.Identity.Api.Services;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.EntityFrameworkCore;
using Microsoft.IdentityModel.Tokens;
using Microsoft.OpenApi.Models;
using System.Security.Claims;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddControllers();
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen(c =>
{
    c.SwaggerDoc("v1", new OpenApiInfo { Title = "FreshFarm.Identity.Api", Version = "v1" });
    c.AddSecurityDefinition("Bearer", new OpenApiSecurityScheme
    {
        Name = "Authorization",
        Type = SecuritySchemeType.Http,
        Scheme = "bearer",
        BearerFormat = "JWT",
        In = ParameterLocation.Header,
        Description = "Nhap token JWT de goi endpoint can [Authorize]."
    });
    c.AddSecurityRequirement(new OpenApiSecurityRequirement
    {
        {
            new OpenApiSecurityScheme
            {
                Reference = new OpenApiReference
                {
                    Type = ReferenceType.SecurityScheme,
                    Id = "Bearer"
                }
            },
            Array.Empty<string>()
        }
    });
});
builder.Services.AddMemoryCache();
var dataProtectionKeysPath = builder.Configuration["DataProtection:KeysPath"];
if (string.IsNullOrWhiteSpace(dataProtectionKeysPath))
{
    dataProtectionKeysPath = Path.Combine(builder.Environment.ContentRootPath, "App_Data", "DataProtectionKeys");
}

Directory.CreateDirectory(dataProtectionKeysPath);
var dataProtectionBuilder = builder.Services.AddDataProtection()
    .PersistKeysToFileSystem(new DirectoryInfo(dataProtectionKeysPath))
    .SetApplicationName("FreshFarm.Identity");
var dataProtectionCertificateThumbprint =
    builder.Configuration["DataProtection:CertificateThumbprint"]?.Trim();
if (!string.IsNullOrWhiteSpace(dataProtectionCertificateThumbprint))
{
    dataProtectionBuilder.ProtectKeysWithCertificate(dataProtectionCertificateThumbprint);
}
else if (builder.Environment.IsProduction())
{
    throw new InvalidOperationException(
        "Production requires DataProtection:CertificateThumbprint so persisted keys are encrypted at rest.");
}
builder.Services.AddDbContext<FreshFarmIdentityDBContext>(options =>
    options.UseSqlServer(builder.Configuration.GetConnectionString("IdentityDB")));
builder.Services.AddScoped<Microsoft.AspNetCore.Identity.IPasswordHasher<User>, Microsoft.AspNetCore.Identity.PasswordHasher<User>>();
builder.Services.Configure<PasswordResetOptions>(builder.Configuration.GetSection(PasswordResetOptions.SectionName));
builder.Services.Configure<EmailVerificationOptions>(builder.Configuration.GetSection(EmailVerificationOptions.SectionName));
builder.Services.Configure<SmtpOptions>(builder.Configuration.GetSection(SmtpOptions.SectionName));
builder.Services.Configure<GeoIpOptions>(builder.Configuration.GetSection(GeoIpOptions.SectionName));
builder.Services.Configure<OrderingServiceOptions>(builder.Configuration.GetSection(OrderingServiceOptions.SectionName));
builder.Services.Configure<InternalBffOptions>(builder.Configuration.GetSection(InternalBffOptions.SectionName));
builder.Services.AddScoped<IPasswordResetTokenService, PasswordResetTokenService>();
builder.Services.AddScoped<IEmailVerificationTokenService, EmailVerificationTokenService>();
builder.Services.AddScoped<IAccountEmailSender, SmtpAccountEmailSender>();
builder.Services.AddScoped<IAuthAuditService, AuthAuditService>();
builder.Services.AddScoped<ILoginDeviceSecurityService, LoginDeviceSecurityService>();
builder.Services.AddScoped<ISellerStoreSettingsResolver, SellerStoreSettingsResolver>();
builder.Services.AddHttpClient<IGeoIpLookupService, GeoIpLookupService>((serviceProvider, client) =>
{
    var geoIpOptions = serviceProvider.GetRequiredService<Microsoft.Extensions.Options.IOptions<GeoIpOptions>>().Value;
    var baseUrl = string.IsNullOrWhiteSpace(geoIpOptions.BaseUrl) ? "https://ipwho.is/" : geoIpOptions.BaseUrl;
    client.BaseAddress = new Uri(baseUrl.EndsWith('/') ? baseUrl : baseUrl + "/");
    client.Timeout = TimeSpan.FromSeconds(Math.Max(1, geoIpOptions.TimeoutSeconds));
});
builder.Services.AddHttpClient<ICustomerNotificationPublisher, OrderingCustomerNotificationPublisher>((serviceProvider, client) =>
{
    var orderingOptions = serviceProvider.GetRequiredService<Microsoft.Extensions.Options.IOptions<OrderingServiceOptions>>().Value;
    if (!string.IsNullOrWhiteSpace(orderingOptions.BaseUrl))
    {
        var baseUrl = orderingOptions.BaseUrl.EndsWith('/') ? orderingOptions.BaseUrl : orderingOptions.BaseUrl + "/";
        client.BaseAddress = new Uri(baseUrl);
    }

    client.Timeout = TimeSpan.FromSeconds(5);
});
builder.Services.AddSingleton<ITotpService, TotpService>();
builder.Services.AddSingleton<ITwoFactorLoginTicketService, TwoFactorLoginTicketService>();

builder.Services.AddAuthentication(JwtBearerDefaults.AuthenticationScheme)
    .AddJwtBearer(options =>
    {
        var jwtKey = builder.Configuration["Jwt:Key"];
        options.TokenValidationParameters = new TokenValidationParameters
        {
            ValidateIssuer = true,
            ValidIssuer = builder.Configuration["Jwt:Issuer"],
            ValidateAudience = true,
            ValidAudience = builder.Configuration["Jwt:Audience"],
            ValidateIssuerSigningKey = true,
            IssuerSigningKey = new SymmetricSecurityKey(System.Text.Encoding.UTF8.GetBytes(jwtKey!)),
            ValidateLifetime = true,
            ClockSkew = TimeSpan.FromMinutes(1),
            RoleClaimType = ClaimTypes.Role,
            NameClaimType = "username"
        };
        options.Events = new JwtBearerEvents
        {
            OnTokenValidated = async context =>
            {
                var rawUserId = context.Principal?.FindFirstValue("sub")
                    ?? context.Principal?.FindFirstValue(ClaimTypes.NameIdentifier);
                var rawTokenVersion = context.Principal?.FindFirstValue("token_version");
                var accountAccess = context.Principal?.FindFirstValue("account_access");
                var tokenApprovalStatus = context.Principal?.FindFirstValue("approval_status");
                if (!int.TryParse(rawUserId, out var userId)
                    || !int.TryParse(rawTokenVersion, out var tokenVersion)
                    || string.IsNullOrWhiteSpace(accountAccess)
                    || string.IsNullOrWhiteSpace(tokenApprovalStatus))
                {
                    context.Fail("Token is missing required account state claims.");
                    return;
                }

                var db = context.HttpContext.RequestServices.GetRequiredService<FreshFarmIdentityDBContext>();
                var accountState = await db.Users
                    .AsNoTracking()
                    .Where(user => user.UserId == userId)
                    .Select(user => new
                    {
                        user.IsActive,
                        user.EmailConfirmed,
                        user.ApprovalStatus,
                        TokenVersion = user.UserAuth == null ? 0 : user.UserAuth.TokenVersion
                    })
                    .SingleOrDefaultAsync(context.HttpContext.RequestAborted);

                var commonStateIsValid = accountState is not null
                    && accountState.IsActive
                    && accountState.EmailConfirmed
                    && accountState.TokenVersion == tokenVersion
                    && string.Equals(accountState.ApprovalStatus, tokenApprovalStatus, StringComparison.OrdinalIgnoreCase);
                var fullAccessIsValid = commonStateIsValid
                    && string.Equals(accountAccess, "full", StringComparison.Ordinal)
                    && string.Equals(accountState!.ApprovalStatus, AccountApprovalStatus.Approved, StringComparison.OrdinalIgnoreCase);
                var pendingAccessIsValid = commonStateIsValid
                    && string.Equals(accountAccess, "pending", StringComparison.Ordinal)
                    && string.Equals(accountState!.ApprovalStatus, AccountApprovalStatus.Pending, StringComparison.OrdinalIgnoreCase)
                    && context.Principal?.IsInRole("Guest") == true
                    && context.Principal?.IsInRole("Admin") != true
                    && context.Principal?.IsInRole("Seller") != true
                    && context.Principal?.IsInRole("Customer") != true;

                if (!fullAccessIsValid && !pendingAccessIsValid)
                {
                    context.Fail("Account is no longer eligible to use this token.");
                }
            }
        };
    });

builder.Services.AddAuthorization(options =>
{
    var fullAccessPolicy = new Microsoft.AspNetCore.Authorization.AuthorizationPolicyBuilder(
            JwtBearerDefaults.AuthenticationScheme)
        .RequireAuthenticatedUser()
        .RequireClaim("account_access", "full")
        .Build();

    options.DefaultPolicy = fullAccessPolicy;
    options.FallbackPolicy = fullAccessPolicy;
    options.AddPolicy("AnyAuthenticated", policy =>
    {
        policy.AddAuthenticationSchemes(JwtBearerDefaults.AuthenticationScheme);
        policy.RequireAuthenticatedUser();
    });
    options.AddPolicy("SellerOnly", policy =>
    {
        policy.AddAuthenticationSchemes(JwtBearerDefaults.AuthenticationScheme);
        policy.RequireAuthenticatedUser();
        policy.RequireClaim("account_access", "full");
        policy.RequireRole("Seller");
    });

    options.AddPolicy("AdminOnly", policy =>
    {
        policy.AddAuthenticationSchemes(JwtBearerDefaults.AuthenticationScheme);
        policy.RequireAuthenticatedUser();
        policy.RequireClaim("account_access", "full");
        policy.RequireRole("Admin");
    });

    options.AddPolicy("SellerOrAdmin", policy =>
    {
        policy.AddAuthenticationSchemes(JwtBearerDefaults.AuthenticationScheme);
        policy.RequireAuthenticatedUser();
        policy.RequireClaim("account_access", "full");
        policy.RequireRole("Seller", "Admin");
    });
});

var app = builder.Build();

await SellerSchemaInitializer.EnsureCreatedAsync(app.Services);

if (app.Environment.IsDevelopment())
{
    app.UseSwagger();
    app.UseSwaggerUI();
}

app.UseHttpsRedirection();
app.UseAuthentication();
app.UseAuthorization();

app.MapControllers();
app.MapGet("/health", () => Results.Ok("ok")).AllowAnonymous();
app.Run();
