using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
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
        if (User.IsInRole("Seller"))
        {
            if (!TryGetCurrentUserId(out var userId))
            {
                return Unauthorized("Token không chứa user id hợp lệ.");
            }

            var sellerEntity = await _db.SellerStoreSettings
                .AsNoTracking()
                .SingleOrDefaultAsync(x => x.UserId == userId, cancellationToken);

            if (sellerEntity is null)
            {
                var seller = await _db.Users
                    .AsNoTracking()
                    .SingleOrDefaultAsync(x => x.UserId == userId, cancellationToken);
                if (seller is null)
                {
                    return NotFound("Không tìm thấy seller hiện tại.");
                }

                var now = DateTime.UtcNow;
                var globalDefaults = await GetOrCreateGlobalStoreSettingAsync(cancellationToken);
                sellerEntity = CreateDefaultSellerEntity(seller, globalDefaults, now);
                sellerEntity.StoreName = await EnsureGeneratedStoreNameAsync(sellerEntity.StoreName, seller.UserId, cancellationToken);
                _db.SellerStoreSettings.Add(sellerEntity);
                await _db.SaveChangesAsync(cancellationToken);
            }

            return Ok(MapResponse(sellerEntity));
        }

        var entity = await GetOrCreateGlobalStoreSettingAsync(cancellationToken);
        return Ok(MapResponse(entity));
    }

    [HttpPut]
    public async Task<IActionResult> Put([FromBody] AdminStoreSettingsRequest request, CancellationToken cancellationToken)
    {
        if (!ModelState.IsValid)
        {
            return ValidationProblem(ModelState);
        }

        if (User.IsInRole("Seller"))
        {
            if (!TryGetCurrentUserId(out var userId))
            {
                return Unauthorized("Token không chứa user id hợp lệ.");
            }

            var ghnValidationError = ValidateGhnSettings(request);
            if (!string.IsNullOrWhiteSpace(ghnValidationError))
            {
                return BadRequest(ghnValidationError);
            }

            var seller = await _db.Users
                .SingleOrDefaultAsync(x => x.UserId == userId, cancellationToken);
            if (seller is null)
            {
                return NotFound("Không tìm thấy seller hiện tại.");
            }

            var now = DateTime.UtcNow;
            var entity = await _db.SellerStoreSettings
                .SingleOrDefaultAsync(x => x.UserId == userId, cancellationToken);

            if (entity is null)
            {
                var globalDefaults = await GetOrCreateGlobalStoreSettingAsync(cancellationToken);
                entity = CreateDefaultSellerEntity(seller, globalDefaults, now);
                entity.StoreName = await EnsureGeneratedStoreNameAsync(entity.StoreName, seller.UserId, cancellationToken);
                _db.SellerStoreSettings.Add(entity);
            }

            var normalizedStoreName = NormalizeStoreName(request.StoreName);
            if (string.IsNullOrWhiteSpace(normalizedStoreName))
            {
                return BadRequest("Tên cửa hàng không được để trống.");
            }

            var duplicatedStoreName = await _db.SellerStoreSettings
                .AsNoTracking()
                .AnyAsync(
                    x => x.UserId != userId
                        && x.StoreName != null
                        && x.StoreName.ToLower() == normalizedStoreName,
                    cancellationToken);
            if (duplicatedStoreName)
            {
                return Conflict("Tên cửa hàng này đã tồn tại. Vui lòng chọn tên khác.");
            }

            ApplySharedFields(entity, request, now);
            entity.GhnPickupName = TrimOrNull(request.GhnPickupName);
            entity.GhnPickupPhone = TrimOrNull(request.GhnPickupPhone) ?? entity.StorePhone;
            entity.GhnPickupAddress = TrimOrNull(request.GhnPickupAddress);
            entity.GhnProvinceId = request.GhnProvinceId;
            entity.GhnProvinceName = TrimOrNull(request.GhnProvinceName);
            entity.GhnDistrictId = request.GhnDistrictId;
            entity.GhnDistrictName = TrimOrNull(request.GhnDistrictName);
            entity.GhnWardCode = TrimOrNull(request.GhnWardCode);
            entity.GhnWardName = TrimOrNull(request.GhnWardName);
            entity.UpdatedAt = now;

            await _db.SaveChangesAsync(cancellationToken);

            return Ok(new
            {
                success = true,
                message = "Lưu cài đặt cửa hàng thành công.",
                data = MapResponse(entity)
            });
        }

        var updatedAt = DateTime.UtcNow;
        var globalEntity = await GetOrCreateGlobalStoreSettingAsync(cancellationToken);
        ApplySharedFields(globalEntity, request, updatedAt);
        globalEntity.UpdatedAt = updatedAt;

        await _db.SaveChangesAsync(cancellationToken);

        return Ok(new
        {
            success = true,
            message = "Luu cai dat thanh cong.",
            data = MapResponse(globalEntity)
        });
    }

    private async Task<StoreSetting> GetOrCreateGlobalStoreSettingAsync(CancellationToken cancellationToken)
    {
        var entity = await _db.StoreSettings
            .OrderBy(x => x.SettingId)
            .FirstOrDefaultAsync(cancellationToken);

        if (entity is not null)
        {
            return entity;
        }

        var fallback = CreateDefaultEntity(DateTime.UtcNow);
        _db.StoreSettings.Add(fallback);
        await _db.SaveChangesAsync(cancellationToken);
        return fallback;
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

    private static SellerStoreSetting CreateDefaultSellerEntity(User seller, StoreSetting? globalDefaults, DateTime now)
    {
        var sellerDisplayName = !string.IsNullOrWhiteSpace(seller.FullName)
            ? seller.FullName.Trim()
            : seller.UserName.Trim();

        return new SellerStoreSetting
        {
            UserId = seller.UserId,
            StoreName = $"Cửa hàng {sellerDisplayName}",
            StoreAddress = globalDefaults?.StoreAddress ?? "Chưa cập nhật địa chỉ shop",
            StoreEmail = !string.IsNullOrWhiteSpace(seller.Email) ? seller.Email.Trim() : (globalDefaults?.StoreEmail ?? string.Empty),
            StorePhone = !string.IsNullOrWhiteSpace(seller.Phone) ? seller.Phone.Trim() : (globalDefaults?.StorePhone ?? string.Empty),
            IsCodenabled = globalDefaults?.IsCodenabled ?? true,
            BankTransferInstructions = globalDefaults?.BankTransferInstructions,
            BankAccountInfo = globalDefaults?.BankAccountInfo,
            DefaultShippingFee = globalDefaults?.DefaultShippingFee ?? 30000,
            FreeShippingThreshold = globalDefaults?.FreeShippingThreshold ?? 500000,
            IsEmailNewOrderEnabled = globalDefaults?.IsEmailNewOrderEnabled ?? true,
            IsEmailDeliveredEnabled = globalDefaults?.IsEmailDeliveredEnabled ?? true,
            IsEmailCancelledEnabled = globalDefaults?.IsEmailCancelledEnabled ?? true,
            AdminNotificationEmail = !string.IsNullOrWhiteSpace(seller.Email) ? seller.Email.Trim() : (globalDefaults?.AdminNotificationEmail ?? string.Empty),
            GhnPickupName = sellerDisplayName,
            GhnPickupPhone = !string.IsNullOrWhiteSpace(seller.Phone) ? seller.Phone.Trim() : null,
            CreatedAt = now,
            UpdatedAt = now
        };
    }

    private async Task<string> EnsureGeneratedStoreNameAsync(string preferredName, int userId, CancellationToken cancellationToken)
    {
        var normalizedPreferred = NormalizeStoreName(preferredName);
        if (string.IsNullOrWhiteSpace(normalizedPreferred))
        {
            return $"Cửa hàng {userId}";
        }

        var exists = await _db.SellerStoreSettings
            .AsNoTracking()
            .AnyAsync(
                x => x.UserId != userId
                    && x.StoreName != null
                    && x.StoreName.ToLower() == normalizedPreferred,
                cancellationToken);

        if (!exists)
        {
            return preferredName.Trim();
        }

        return $"{preferredName.Trim()} {userId}";
    }

    private static void ApplySharedFields(StoreSetting entity, AdminStoreSettingsRequest request, DateTime now)
    {
        entity.StoreName = request.StoreName.Trim();
        entity.StoreAddress = request.StoreAddress.Trim();
        entity.StoreEmail = request.StoreEmail.Trim();
        entity.StorePhone = request.StorePhone.Trim();
        entity.IsCodenabled = request.IsCODEnabled;
        entity.BankTransferInstructions = TrimOrNull(request.BankTransferInstructions);
        entity.BankAccountInfo = TrimOrNull(request.BankAccountInfo);
        entity.DefaultShippingFee = request.DefaultShippingFee;
        entity.FreeShippingThreshold = request.FreeShippingThreshold;
        entity.IsEmailNewOrderEnabled = request.IsEmailNewOrderEnabled;
        entity.IsEmailDeliveredEnabled = request.IsEmailDeliveredEnabled;
        entity.IsEmailCancelledEnabled = request.IsEmailCancelledEnabled;
        entity.AdminNotificationEmail = request.AdminNotificationEmail.Trim();
        entity.UpdatedAt = now;
    }

    private static void ApplySharedFields(SellerStoreSetting entity, AdminStoreSettingsRequest request, DateTime now)
    {
        entity.StoreName = request.StoreName.Trim();
        entity.StoreAddress = request.StoreAddress.Trim();
        entity.StoreEmail = request.StoreEmail.Trim();
        entity.StorePhone = request.StorePhone.Trim();
        entity.IsCodenabled = request.IsCODEnabled;
        entity.BankTransferInstructions = TrimOrNull(request.BankTransferInstructions);
        entity.BankAccountInfo = TrimOrNull(request.BankAccountInfo);
        entity.DefaultShippingFee = request.DefaultShippingFee;
        entity.FreeShippingThreshold = request.FreeShippingThreshold;
        entity.IsEmailNewOrderEnabled = request.IsEmailNewOrderEnabled;
        entity.IsEmailDeliveredEnabled = request.IsEmailDeliveredEnabled;
        entity.IsEmailCancelledEnabled = request.IsEmailCancelledEnabled;
        entity.AdminNotificationEmail = request.AdminNotificationEmail.Trim();
        entity.UpdatedAt = now;
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
            UpdatedAt = entity.UpdatedAt,
            HasGhnOrigin = false
        };
    }

    private static AdminStoreSettingsResponse MapResponse(SellerStoreSetting entity)
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
            GhnPickupName = entity.GhnPickupName,
            GhnPickupPhone = entity.GhnPickupPhone,
            GhnPickupAddress = entity.GhnPickupAddress,
            GhnProvinceId = entity.GhnProvinceId,
            GhnProvinceName = entity.GhnProvinceName,
            GhnDistrictId = entity.GhnDistrictId,
            GhnDistrictName = entity.GhnDistrictName,
            GhnWardCode = entity.GhnWardCode,
            GhnWardName = entity.GhnWardName,
            CreatedAt = entity.CreatedAt,
            UpdatedAt = entity.UpdatedAt,
            HasGhnOrigin = entity.GhnDistrictId.HasValue
                && entity.GhnDistrictId.Value > 0
                && !string.IsNullOrWhiteSpace(entity.GhnWardCode)
                && !string.IsNullOrWhiteSpace(entity.GhnPickupAddress)
        };
    }

    private static string? ValidateGhnSettings(AdminStoreSettingsRequest request)
    {
        var hasAnyGhnInput =
            request.GhnProvinceId.HasValue
            || request.GhnDistrictId.HasValue
            || !string.IsNullOrWhiteSpace(request.GhnWardCode)
            || !string.IsNullOrWhiteSpace(request.GhnPickupAddress)
            || !string.IsNullOrWhiteSpace(request.GhnPickupPhone)
            || !string.IsNullOrWhiteSpace(request.GhnPickupName);

        if (!hasAnyGhnInput)
        {
            return null;
        }

        if (!request.GhnDistrictId.HasValue || request.GhnDistrictId.Value <= 0)
        {
            return "Địa chỉ lấy hàng GHN cần có quận/huyện hợp lệ.";
        }

        if (string.IsNullOrWhiteSpace(request.GhnWardCode))
        {
            return "Địa chỉ lấy hàng GHN cần có phường/xã hợp lệ.";
        }

        if (string.IsNullOrWhiteSpace(request.GhnPickupAddress))
        {
            return "Địa chỉ lấy hàng GHN cần có địa chỉ chi tiết.";
        }

        return null;
    }

    private static string? TrimOrNull(string? value)
    {
        return string.IsNullOrWhiteSpace(value) ? null : value.Trim();
    }

    private static string NormalizeStoreName(string? value)
    {
        return string.IsNullOrWhiteSpace(value) ? string.Empty : value.Trim().ToLowerInvariant();
    }

    private bool TryGetCurrentUserId(out int userId)
    {
        userId = 0;

        var rawUserId = User.FindFirstValue(JwtRegisteredClaimNames.Sub)
            ?? User.FindFirstValue(ClaimTypes.NameIdentifier)
            ?? User.FindFirstValue("sub");

        return int.TryParse(rawUserId, out userId);
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

        [StringLength(150)]
        public string? GhnPickupName { get; set; }

        [StringLength(20)]
        public string? GhnPickupPhone { get; set; }

        [StringLength(500)]
        public string? GhnPickupAddress { get; set; }

        public int? GhnProvinceId { get; set; }

        [StringLength(150)]
        public string? GhnProvinceName { get; set; }

        public int? GhnDistrictId { get; set; }

        [StringLength(150)]
        public string? GhnDistrictName { get; set; }

        [StringLength(50)]
        public string? GhnWardCode { get; set; }

        [StringLength(150)]
        public string? GhnWardName { get; set; }
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

        public string? GhnPickupName { get; set; }

        public string? GhnPickupPhone { get; set; }

        public string? GhnPickupAddress { get; set; }

        public int? GhnProvinceId { get; set; }

        public string? GhnProvinceName { get; set; }

        public int? GhnDistrictId { get; set; }

        public string? GhnDistrictName { get; set; }

        public string? GhnWardCode { get; set; }

        public string? GhnWardName { get; set; }

        public DateTime CreatedAt { get; set; }

        public DateTime UpdatedAt { get; set; }

        public bool HasGhnOrigin { get; set; }
    }
}
