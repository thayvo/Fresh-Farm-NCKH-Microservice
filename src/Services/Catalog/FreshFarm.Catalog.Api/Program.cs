using FreshFarm.Catalog.Api.Options;
using FreshFarm.Catalog.Api.Security;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.EntityFrameworkCore;
using Microsoft.IdentityModel.Tokens;
using Microsoft.OpenApi.Models;
using System.Security.Claims;

var builder = WebApplication.CreateBuilder(args);

var identityBaseUrl = builder.Configuration["Services:Identity:BaseUrl"];
if (string.IsNullOrWhiteSpace(identityBaseUrl) && builder.Environment.IsDevelopment())
{
    identityBaseUrl = "https://localhost:7140";
}

if (!Uri.TryCreate(identityBaseUrl, UriKind.Absolute, out var identityBaseUri)
    || (identityBaseUri.Scheme != Uri.UriSchemeHttps && identityBaseUri.Scheme != Uri.UriSchemeHttp))
{
    throw new InvalidOperationException(
        "Services:Identity:BaseUrl must be an absolute HTTP(S) URL so JWT sessions can be validated live.");
}

builder.Services.AddControllers();
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddInternalInventoryOptions(builder.Configuration);
builder.Services.AddDbContext<FreshFarm.Catalog.Api.Models.FreshFarmCatalogDBContext>(options =>
{
    options.UseSqlServer(builder.Configuration.GetConnectionString("FreshFarmCatalogDB"));
});
builder.Services.AddHttpClient<IIdentitySessionValidator, IdentitySessionValidator>(client =>
{
    client.BaseAddress = identityBaseUri;
    client.Timeout = TimeSpan.FromSeconds(5);
}).ConfigurePrimaryHttpMessageHandler(() => new HttpClientHandler
{
    AllowAutoRedirect = false
});

builder.Services.AddSwaggerGen(c =>
{
    c.SwaggerDoc("v1", new OpenApiInfo { Title = "FreshFarm.Catalog.Api", Version = "v1" });
    c.AddSecurityDefinition("Bearer", new OpenApiSecurityScheme
    {
        Name = "Authorization",
        Type = SecuritySchemeType.Http,
        Scheme = "bearer",
        BearerFormat = "JWT",
        In = ParameterLocation.Header,
        Description = "Nhap: Bearer {token}"
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
            OnTokenValidated = IdentitySessionJwtValidation.ValidateAsync
        };
    });

builder.Services.AddAuthorization(options =>
{
    var fullAccountAccessPolicy = new AuthorizationPolicyBuilder()
        .RequireAuthenticatedUser()
        .RequireClaim("account_access", "full")
        .Build();

    options.DefaultPolicy = fullAccountAccessPolicy;
    options.FallbackPolicy = fullAccountAccessPolicy;

    options.AddPolicy("SellerOnly", policy =>
    {
        policy.RequireAuthenticatedUser();
        policy.RequireClaim("account_access", "full");
        policy.RequireRole("Seller");
    });

    options.AddPolicy("AdminOnly", policy =>
    {
        policy.RequireAuthenticatedUser();
        policy.RequireClaim("account_access", "full");
        policy.RequireRole("Admin");
    });

    options.AddPolicy("SellerOrAdmin", policy =>
    {
        policy.RequireAuthenticatedUser();
        policy.RequireClaim("account_access", "full");
        policy.RequireRole("Seller", "Admin");
    });
});

var app = builder.Build();

if (app.Environment.IsDevelopment())
{
    app.UseSwagger();
    app.UseSwaggerUI();
}

app.UseHttpsRedirection();
app.UseAuthentication();
app.UseAuthorization();

app.MapControllers();
app.MapGet("/health", () => Results.Ok("ok"))
    .AllowAnonymous();
app.Run();
