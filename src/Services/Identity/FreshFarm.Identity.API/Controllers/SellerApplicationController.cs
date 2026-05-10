using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using FreshFarm.Identity.Api.Dtos;
using FreshFarm.Identity.Api.Models;
using FreshFarm.Identity.Api.Services;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace FreshFarm.Identity.Api.Controllers;

[ApiController]
[Route("auth/seller-application")]
[Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)]
public sealed class SellerApplicationController : ControllerBase
{
    private readonly FreshFarmIdentityDBContext _db;
    private readonly ISellerStoreSettingsResolver _sellerStoreSettingsResolver;

    public SellerApplicationController(
        FreshFarmIdentityDBContext db,
        ISellerStoreSettingsResolver sellerStoreSettingsResolver)
    {
        _db = db;
        _sellerStoreSettingsResolver = sellerStoreSettingsResolver;
    }

    [HttpGet("me")]
    public async Task<IActionResult> GetCurrent(CancellationToken cancellationToken = default)
    {
        if (!TryGetCurrentUserId(out var userId))
        {
            return Unauthorized("Token không chứa user id hợp lệ.");
        }

        var user = await _db.Users
            .AsNoTracking()
            .Include(u => u.UserRoles)
            .ThenInclude(ur => ur.Role)
            .Include(u => u.AddressBooks)
            .Include(u => u.SellerKycProfile)
            .SingleOrDefaultAsync(u => u.UserId == userId, cancellationToken);

        if (user is null)
        {
            return NotFound("Không tìm thấy tài khoản.");
        }

        var sellerStoreSetting = await _sellerStoreSettingsResolver
            .GetLatestForUserAsync(user.UserId, asNoTracking: true, cancellationToken);

        var response = BuildResponse(user, sellerStoreSetting);
        response.ReviewHistory = await LoadReviewHistoryAsync(user.UserId, cancellationToken);
        return Ok(response);
    }

    [HttpPost]
    public async Task<IActionResult> Upsert([FromBody] UpsertSellerApplicationRequestDto request, CancellationToken cancellationToken = default)
    {
        if (!ModelState.IsValid)
        {
            return ValidationProblem(ModelState);
        }

        if (!TryGetCurrentUserId(out var userId))
        {
            return Unauthorized("Token không chứa user id hợp lệ.");
        }

        var user = await _db.Users
            .Include(u => u.UserRoles)
            .ThenInclude(ur => ur.Role)
            .Include(u => u.SellerKycProfile)
            .SingleOrDefaultAsync(u => u.UserId == userId, cancellationToken);

        if (user is null)
        {
            return NotFound("Không tìm thấy tài khoản.");
        }

        if (!TryNormalizeVietnamPhone(request.StorePhone, out var normalizedStorePhone, out var storePhoneError))
        {
            return BadRequest(storePhoneError);
        }

        var normalizedPickupPhone = normalizedStorePhone;
        if (!string.IsNullOrWhiteSpace(request.PickupPhone) &&
            !TryNormalizeVietnamPhone(request.PickupPhone, out normalizedPickupPhone, out var pickupPhoneError))
        {
            return BadRequest(pickupPhoneError);
        }

        var identityNumber = request.IdentityNumber.Trim();
        if (identityNumber.Length < 9)
        {
            return BadRequest("Số CCCD/CMND không hợp lệ.");
        }

        var now = DateTime.UtcNow;
        var isSellerApproved = user.UserRoles.Any(x => string.Equals(x.Role?.RoleName, "Seller", StringComparison.OrdinalIgnoreCase));

        var entity = await _sellerStoreSettingsResolver
            .GetLatestForUserAsync(user.UserId, asNoTracking: false, cancellationToken);
        if (entity is null)
        {
            entity = new SellerStoreSetting
            {
                UserId = user.UserId,
                CreatedAt = now,
                IsCodenabled = true,
                DefaultShippingFee = 30000m,
                FreeShippingThreshold = 500000m,
                IsEmailNewOrderEnabled = true,
                IsEmailDeliveredEnabled = true,
                IsEmailCancelledEnabled = true
            };
            _db.SellerStoreSettings.Add(entity);
        }

        entity.StoreName = request.StoreName.Trim();
        entity.StoreAddress = request.StoreAddress.Trim();
        entity.StoreEmail = request.StoreEmail.Trim();
        entity.StorePhone = normalizedStorePhone;
        entity.BankAccountInfo = request.BankAccountInfo?.Trim() ?? string.Empty;
        entity.BankTransferInstructions = request.BankTransferInstructions?.Trim() ?? string.Empty;
        entity.AdminNotificationEmail = string.IsNullOrWhiteSpace(request.AdminNotificationEmail)
            ? entity.StoreEmail
            : request.AdminNotificationEmail.Trim();
        entity.GhnPickupName = string.IsNullOrWhiteSpace(request.PickupName)
            ? user.FullName
            : request.PickupName.Trim();
        entity.GhnPickupPhone = normalizedPickupPhone;
        entity.GhnPickupAddress = string.IsNullOrWhiteSpace(request.PickupAddress)
            ? entity.StoreAddress
            : request.PickupAddress.Trim();
        entity.UpdatedAt = now;

        var kyc = user.SellerKycProfile;
        if (kyc is null)
        {
            kyc = new SellerKycProfile
            {
                UserId = user.UserId,
                CreatedAt = now
            };
            _db.SellerKycProfiles.Add(kyc);
        }

        kyc.LegalFullName = request.LegalFullName.Trim();
        kyc.IdentityNumber = identityNumber;
        kyc.IdentityIssuedDate = request.IdentityIssuedDate!.Value.Date;
        kyc.IdentityIssuedPlace = request.IdentityIssuedPlace.Trim();
        kyc.TaxCode = string.IsNullOrWhiteSpace(request.TaxCode) ? null : request.TaxCode.Trim();
        kyc.BusinessLicenseNumber = string.IsNullOrWhiteSpace(request.BusinessLicenseNumber) ? null : request.BusinessLicenseNumber.Trim();
        kyc.CitizenIdFrontUrl = string.IsNullOrWhiteSpace(request.CitizenIdFrontUrl) ? null : request.CitizenIdFrontUrl.Trim();
        kyc.CitizenIdBackUrl = string.IsNullOrWhiteSpace(request.CitizenIdBackUrl) ? null : request.CitizenIdBackUrl.Trim();
        kyc.BusinessLicenseUrl = string.IsNullOrWhiteSpace(request.BusinessLicenseUrl) ? null : request.BusinessLicenseUrl.Trim();
        kyc.AdditionalDocumentUrl = string.IsNullOrWhiteSpace(request.AdditionalDocumentUrl) ? null : request.AdditionalDocumentUrl.Trim();
        kyc.Notes = string.IsNullOrWhiteSpace(request.Notes) ? null : request.Notes.Trim();
        if (!isSellerApproved && string.Equals(NormalizeReviewStatus(kyc.ReviewStatus), "rejected", StringComparison.Ordinal))
        {
            kyc.ReviewStatus = "pending";
            kyc.ReviewNote = null;
            kyc.ReviewedAt = null;
            kyc.ReviewedByUserId = null;
        }
        kyc.UpdatedAt = now;

        user.UpdatedAt = now;
        await _db.SaveChangesAsync(cancellationToken);

        var response = BuildResponse(user, entity, kyc);
        response.ReviewHistory = await LoadReviewHistoryAsync(user.UserId, cancellationToken);
        response.Message = response.IsSellerApproved
            ? "Đã cập nhật hồ sơ gian hàng và KYC."
            : "Đã gửi hồ sơ đăng ký người bán kèm giấy tờ pháp lý. FreshFarm sẽ rà soát và phản hồi sớm.";

        return Ok(response);
    }

    private static SellerApplicationResponseDto BuildResponse(
        User user,
        SellerStoreSetting? entity = null,
        SellerKycProfile? kyc = null)
    {
        kyc ??= user.SellerKycProfile;

        var isSellerApproved = user.UserRoles.Any(x => string.Equals(x.Role?.RoleName, "Seller", StringComparison.OrdinalIgnoreCase));
        var hasApplication = entity is not null;
        var reviewStatus = ResolveSellerReviewStatus(isSellerApproved, hasApplication, kyc);
        var fallbackAddress = BuildFallbackAddress(user);
        var storeEmail = entity?.StoreEmail ?? user.Email ?? string.Empty;
        var storePhone = entity?.StorePhone ?? user.Phone ?? string.Empty;

        return new SellerApplicationResponseDto
        {
            HasApplication = hasApplication,
            IsSellerApproved = isSellerApproved,
            Status = reviewStatus,
            StatusLabel = ResolveSellerStatusLabel(reviewStatus),
            ReviewStatus = reviewStatus,
            ReviewStatusLabel = ResolveSellerReviewStatusLabel(reviewStatus),
            ReviewNote = string.Equals(reviewStatus, "rejected", StringComparison.OrdinalIgnoreCase)
                ? kyc?.ReviewNote?.Trim() ?? string.Empty
                : string.Empty,
            ReviewedAtUtc = kyc?.ReviewedAt,
            StoreName = entity?.StoreName ?? user.FullName ?? string.Empty,
            StoreAddress = entity?.StoreAddress ?? fallbackAddress,
            StoreEmail = storeEmail,
            StorePhone = storePhone,
            BankAccountInfo = entity?.BankAccountInfo ?? string.Empty,
            BankTransferInstructions = entity?.BankTransferInstructions ?? string.Empty,
            AdminNotificationEmail = entity?.AdminNotificationEmail ?? storeEmail,
            PickupName = entity?.GhnPickupName ?? user.FullName ?? string.Empty,
            PickupPhone = entity?.GhnPickupPhone ?? storePhone,
            PickupAddress = entity?.GhnPickupAddress ?? fallbackAddress,
            SubmittedAtUtc = entity?.CreatedAt,
            UpdatedAtUtc = entity?.UpdatedAt,
            Message = ResolveSellerMessage(reviewStatus, kyc?.ReviewNote),
            Kyc = BuildKycResponse(kyc)
        };
    }

    private static SellerKycResponseDto BuildKycResponse(SellerKycProfile? kyc)
    {
        if (kyc is null)
        {
            return new SellerKycResponseDto();
        }

        return new SellerKycResponseDto
        {
            HasKycProfile = true,
            HasIdentityDocuments =
                !string.IsNullOrWhiteSpace(kyc.CitizenIdFrontUrl) &&
                !string.IsNullOrWhiteSpace(kyc.CitizenIdBackUrl),
            HasBusinessLicense = !string.IsNullOrWhiteSpace(kyc.BusinessLicenseUrl),
            ReviewStatus = NormalizeReviewStatus(kyc.ReviewStatus),
            ReviewStatusLabel = ResolveSellerReviewStatusLabel(kyc.ReviewStatus),
            ReviewNote = kyc.ReviewNote?.Trim() ?? string.Empty,
            ReviewedAtUtc = kyc.ReviewedAt,
            LegalFullName = kyc.LegalFullName ?? string.Empty,
            IdentityNumber = kyc.IdentityNumber ?? string.Empty,
            IdentityNumberMasked = MaskIdentityNumber(kyc.IdentityNumber),
            IdentityIssuedDate = kyc.IdentityIssuedDate,
            IdentityIssuedPlace = kyc.IdentityIssuedPlace ?? string.Empty,
            TaxCode = kyc.TaxCode ?? string.Empty,
            BusinessLicenseNumber = kyc.BusinessLicenseNumber ?? string.Empty,
            CitizenIdFrontUrl = kyc.CitizenIdFrontUrl ?? string.Empty,
            CitizenIdBackUrl = kyc.CitizenIdBackUrl ?? string.Empty,
            BusinessLicenseUrl = kyc.BusinessLicenseUrl ?? string.Empty,
            AdditionalDocumentUrl = kyc.AdditionalDocumentUrl ?? string.Empty,
            Notes = kyc.Notes ?? string.Empty
        };
    }

    private static string MaskIdentityNumber(string? identityNumber)
    {
        var raw = identityNumber?.Trim() ?? string.Empty;
        if (raw.Length <= 4)
        {
            return raw;
        }

        var visibleTail = raw[^4..];
        return new string('*', Math.Max(0, raw.Length - 4)) + visibleTail;
    }

    private static string BuildFallbackAddress(User user)
    {
        var address = GetPreferredAddress(user);
        var parts = new[]
        {
            address?.AddressDetail,
            address?.Ward,
            address?.District,
            address?.Province
        }
        .Where(x => !string.IsNullOrWhiteSpace(x))
        .Select(x => x!.Trim())
        .ToArray();

        return parts.Length == 0 ? string.Empty : string.Join(", ", parts);
    }

    private static AddressBook? GetPreferredAddress(User user)
    {
        return user.AddressBooks
            .Where(x => x.IsActive)
            .OrderByDescending(x => x.IsDefault)
            .ThenByDescending(x => x.UpdatedAt ?? x.CreatedAt)
            .ThenByDescending(x => x.AddressId)
            .FirstOrDefault();
    }

    private static string ResolveSellerReviewStatus(bool isSellerApproved, bool hasApplication, SellerKycProfile? kyc)
    {
        if (isSellerApproved)
        {
            return "approved";
        }

        if (!hasApplication)
        {
            return "not_applied";
        }

        return NormalizeReviewStatus(kyc?.ReviewStatus);
    }

    private static string NormalizeReviewStatus(string? reviewStatus)
    {
        var normalized = reviewStatus?.Trim().ToLowerInvariant();
        return normalized is "approved" or "rejected" or "pending"
            ? normalized
            : "pending";
    }

    private static string ResolveSellerStatusLabel(string reviewStatus)
    {
        return reviewStatus switch
        {
            "approved" => "Đã là người bán",
            "rejected" => "Cần bổ sung hồ sơ",
            "pending" => "Đang chờ duyệt",
            _ => "Chưa đăng ký"
        };
    }

    private static string ResolveSellerReviewStatusLabel(string? reviewStatus)
    {
        return NormalizeReviewStatus(reviewStatus) switch
        {
            "approved" => "Đã duyệt",
            "rejected" => "Đã từ chối",
            _ => "Đang chờ duyệt"
        };
    }

    private static string ResolveSellerMessage(string reviewStatus, string? reviewNote)
    {
        return reviewStatus switch
        {
            "approved" => "Tài khoản của bạn đã có quyền người bán.",
            "rejected" when !string.IsNullOrWhiteSpace(reviewNote)
                => $"Hồ sơ seller cần bổ sung trước khi duyệt: {reviewNote.Trim()}",
            "rejected" => "Hồ sơ seller cần được bổ sung trước khi đội vận hành duyệt tiếp.",
            "pending" => "Hồ sơ đang chờ đội vận hành phê duyệt.",
            _ => "Bạn có thể gửi hồ sơ để bắt đầu bán hàng trên FreshFarm."
        };
    }

    private async Task<List<SellerApplicationReviewHistoryDto>> LoadReviewHistoryAsync(int userId, CancellationToken cancellationToken)
    {
        return await _db.SellerKycReviewEvents
            .AsNoTracking()
            .Where(x => x.UserId == userId)
            .OrderByDescending(x => x.ReviewedAt)
            .Take(10)
            .Select(x => new SellerApplicationReviewHistoryDto
            {
                Action = x.Action ?? string.Empty,
                ReviewStatus = x.ReviewStatus ?? string.Empty,
                ReviewStatusLabel = ResolveSellerReviewStatusLabel(x.ReviewStatus),
                Note = x.Note ?? string.Empty,
                ReviewedAtUtc = x.ReviewedAt,
                ReviewedByUserId = x.ReviewedByUserId,
                ReviewerUserName = x.ReviewerUserName ?? string.Empty,
                ReviewerFullName = x.ReviewerFullName ?? string.Empty
            })
            .ToListAsync(cancellationToken);
    }

    private bool TryGetCurrentUserId(out int userId)
    {
        userId = 0;
        var rawUserId = User.FindFirstValue(JwtRegisteredClaimNames.Sub)
            ?? User.FindFirstValue(ClaimTypes.NameIdentifier)
            ?? User.FindFirstValue("sub");
        return int.TryParse(rawUserId, out userId);
    }

    private static bool TryNormalizeVietnamPhone(string? rawPhone, out string normalizedPhone, out string errorMessage)
    {
        normalizedPhone = string.Empty;
        errorMessage = "Số điện thoại phải đúng định dạng Việt Nam.";

        var trimmed = rawPhone?.Trim() ?? string.Empty;
        if (string.IsNullOrWhiteSpace(trimmed))
        {
            errorMessage = "Số điện thoại không được để trống.";
            return false;
        }

        var isValid = trimmed.Length == 10 && trimmed[0] == '0' && trimmed.Skip(1).All(char.IsDigit);
        if (!isValid && trimmed.StartsWith("+84", StringComparison.Ordinal) && trimmed.Length == 12)
        {
            isValid = trimmed[3..].All(char.IsDigit);
        }

        if (!isValid)
        {
            errorMessage = "Số điện thoại phải là số di động Việt Nam hợp lệ gồm 10 số, hoặc bắt đầu bằng +84 và đủ 9 số phía sau.";
            return false;
        }

        normalizedPhone = trimmed.StartsWith("+84", StringComparison.Ordinal)
            ? $"0{trimmed[3..]}"
            : trimmed;

        return true;
    }
}
