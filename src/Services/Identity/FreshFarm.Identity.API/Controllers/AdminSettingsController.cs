using FreshFarm.Identity.Api.Models;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using System.ComponentModel.DataAnnotations;

namespace FreshFarm.Identity.Api.Controllers;

[ApiController]
[Route("auth/admin/settings/store")]
[Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme, Policy = "SellerOrAdmin")]
public sealed class AdminSettingsController : ControllerBase
{
    private readonly FreshFarmIdentityDBContext _db;

    public AdminSettingsController(FreshFarmIdentityDBContext db)
    {
        _db = db;
    }

    [HttpGet]
    public async Task<IActionResult> Get(CancellationToken cancellationToken)
    {
        var entity = await _db.StoreSettings
            .AsNoTracking()
            .OrderBy(x => x.SettingId)
            .FirstOrDefaultAsync(cancellationToken);

        if (entity is null)
        {
            var now = DateTime.UtcNow;
            var fallback = CreateDefaultEntity(now);
            _db.StoreSettings.Add(fallback);
            await _db.SaveChangesAsync(cancellationToken);
            entity = fallback;
        }

        return Ok(MapResponse(entity));
    }

    [HttpPut]
    public async Task<IActionResult> Put([FromBody] AdminStoreSettingsRequest request, CancellationToken cancellationToken)
    {
        if (!ModelState.IsValid)
        {
            return ValidationProblem(ModelState);
        }

        var now = DateTime.UtcNow;
        var entity = await _db.StoreSettings
            .OrderBy(x => x.SettingId)
            .FirstOrDefaultAsync(cancellationToken);

        if (entity is null)
        {
            entity = CreateDefaultEntity(now);
            _db.StoreSettings.Add(entity);
        }

        entity.StoreName = request.StoreName.Trim();
        entity.StoreAddress = request.StoreAddress.Trim();
        entity.StoreEmail = request.StoreEmail.Trim();
        entity.StorePhone = request.StorePhone.Trim();
        entity.IsCodenabled = request.IsCODEnabled;
        entity.BankTransferInstructions = request.BankTransferInstructions?.Trim();
        entity.BankAccountInfo = request.BankAccountInfo?.Trim();
        entity.DefaultShippingFee = request.DefaultShippingFee;
        entity.FreeShippingThreshold = request.FreeShippingThreshold;
        entity.IsEmailNewOrderEnabled = request.IsEmailNewOrderEnabled;
        entity.IsEmailDeliveredEnabled = request.IsEmailDeliveredEnabled;
        entity.IsEmailCancelledEnabled = request.IsEmailCancelledEnabled;
        entity.AdminNotificationEmail = request.AdminNotificationEmail.Trim();
        entity.UpdatedAt = now;

        await _db.SaveChangesAsync(cancellationToken);

        return Ok(new
        {
            success = true,
            message = "Luu cai dat thanh cong.",
            data = MapResponse(entity)
        });
    }

    private static StoreSetting CreateDefaultEntity(DateTime now)
    {
        return new StoreSetting
        {
            StoreName = "Fresh Farm",
            StoreAddress = "123 Duong ABC, Quan 1, TP.HCM",
            StoreEmail = "support@freshfarm.vn",
            StorePhone = "1900 1234",
            IsCodenabled = true,
            BankTransferInstructions = "Vui long chuyen khoan voi noi dung: TT [Ma don hang]",
            BankAccountInfo = "Ngan hang: Vietcombank...",
            DefaultShippingFee = 30000,
            FreeShippingThreshold = 500000,
            IsEmailNewOrderEnabled = true,
            IsEmailDeliveredEnabled = true,
            IsEmailCancelledEnabled = true,
            AdminNotificationEmail = "admin@freshfarm.vn",
            CreatedAt = now,
            UpdatedAt = now
        };
    }

    private static AdminStoreSettingsResponse MapResponse(StoreSetting entity)
    {
        return new AdminStoreSettingsResponse
        {
            StoreName = entity.StoreName,
            StoreAddress = entity.StoreAddress,
            StoreEmail = entity.StoreEmail,
            StorePhone = entity.StorePhone,
            IsCODEnabled = entity.IsCodenabled,
            BankTransferInstructions = entity.BankTransferInstructions,
            BankAccountInfo = entity.BankAccountInfo,
            DefaultShippingFee = entity.DefaultShippingFee,
            FreeShippingThreshold = entity.FreeShippingThreshold,
            IsEmailNewOrderEnabled = entity.IsEmailNewOrderEnabled,
            IsEmailDeliveredEnabled = entity.IsEmailDeliveredEnabled,
            IsEmailCancelledEnabled = entity.IsEmailCancelledEnabled,
            AdminNotificationEmail = entity.AdminNotificationEmail,
            CreatedAt = entity.CreatedAt,
            UpdatedAt = entity.UpdatedAt
        };
    }

    public sealed class AdminStoreSettingsRequest
    {
        [Required]
        [StringLength(200)]
        public string StoreName { get; set; } = string.Empty;

        [Required]
        [StringLength(500)]
        public string StoreAddress { get; set; } = string.Empty;

        [Required]
        [EmailAddress]
        [StringLength(150)]
        public string StoreEmail { get; set; } = string.Empty;

        [Required]
        [StringLength(20)]
        public string StorePhone { get; set; } = string.Empty;

        public bool IsCODEnabled { get; set; }

        [StringLength(2000)]
        public string? BankTransferInstructions { get; set; }

        [StringLength(2000)]
        public string? BankAccountInfo { get; set; }

        [Range(0, 1_000_000_000)]
        public decimal DefaultShippingFee { get; set; }

        [Range(0, 1_000_000_000)]
        public decimal FreeShippingThreshold { get; set; }

        public bool IsEmailNewOrderEnabled { get; set; }

        public bool IsEmailDeliveredEnabled { get; set; }

        public bool IsEmailCancelledEnabled { get; set; }

        [Required]
        [EmailAddress]
        [StringLength(150)]
        public string AdminNotificationEmail { get; set; } = string.Empty;
    }

    public sealed class AdminStoreSettingsResponse
    {
        public string StoreName { get; set; } = string.Empty;

        public string StoreAddress { get; set; } = string.Empty;

        public string StoreEmail { get; set; } = string.Empty;

        public string StorePhone { get; set; } = string.Empty;

        public bool IsCODEnabled { get; set; }

        public string? BankTransferInstructions { get; set; }

        public string? BankAccountInfo { get; set; }

        public decimal DefaultShippingFee { get; set; }

        public decimal FreeShippingThreshold { get; set; }

        public bool IsEmailNewOrderEnabled { get; set; }

        public bool IsEmailDeliveredEnabled { get; set; }

        public bool IsEmailCancelledEnabled { get; set; }

        public string AdminNotificationEmail { get; set; } = string.Empty;

        public DateTime CreatedAt { get; set; }

        public DateTime UpdatedAt { get; set; }
    }
}
