using FreshFarm.Ordering.Api.Models;
using FreshFarm.Ordering.Api.Options;
using FreshFarm.Ordering.Api.Services;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.EntityFrameworkCore;
using Microsoft.IdentityModel.Tokens;
using Microsoft.OpenApi.Models;
using System.Security.Claims;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddControllers();
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen(c =>
{
    c.SwaggerDoc("v1", new OpenApiInfo { Title = "FreshFarm.Ordering.Api", Version = "v1" });
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
    });

builder.Services.AddAuthorization(options =>
{
    options.AddPolicy("SellerOnly", policy =>
    {
        policy.RequireAuthenticatedUser();
        policy.RequireRole("Seller");
    });

    options.AddPolicy("AdminOnly", policy =>
    {
        policy.RequireAuthenticatedUser();
        policy.RequireRole("Admin");
    });

    options.AddPolicy("SellerOrAdmin", policy =>
    {
        policy.RequireAuthenticatedUser();
        policy.RequireRole("Seller", "Admin");
    });
});

builder.Services.AddDbContext<FreshFarmOrderingDBContext>(options =>
{
    options.UseSqlServer(builder.Configuration.GetConnectionString("FreshFarmOrderingDB"));
});
builder.Services.Configure<CatalogServiceOptions>(
    builder.Configuration.GetSection(CatalogServiceOptions.SectionName));
builder.Services.AddHttpClient("Catalog", client =>
{
    var baseUrl = builder.Configuration[$"{CatalogServiceOptions.SectionName}:BaseUrl"];
    if (!string.IsNullOrWhiteSpace(baseUrl))
    {
        client.BaseAddress = new Uri(baseUrl);
    }
});
builder.Services.AddScoped<CatalogInventoryClient>();
builder.Services.AddScoped<OrderReservationService>();
builder.Services.AddScoped<InventoryReconciliationService>();
builder.Services.AddHostedService<PendingPaymentExpirationBackgroundService>();

var app = builder.Build();

app.UseHttpsRedirection();
app.UseAuthentication();
app.UseAuthorization();

if (app.Environment.IsDevelopment())
{
    app.UseSwagger();
    app.UseSwaggerUI(c => c.SwaggerEndpoint("/swagger/v1/swagger.json", "FreshFarm.Ordering.Api v1"));
}

app.MapControllers();
app.MapGet("/health", () => Results.Ok("ok"));
app.Run();
