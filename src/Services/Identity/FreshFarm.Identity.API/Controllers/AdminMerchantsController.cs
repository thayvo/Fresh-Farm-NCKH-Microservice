using FreshFarm.Identity.Api.Dtos;
using FreshFarm.Identity.Api.Models;
using FreshFarm.Identity.Api.Services;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using System.ComponentModel.DataAnnotations;
using System.Security.Claims;
using System.Text.RegularExpressions;

namespace FreshFarm.Identity.Api.Controllers;

[ApiController]
[Route("auth/admin/merchants")]
[Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme, Policy = "AdminOnly")]
public sealed class AdminMerchantsController : ControllerBase
{
    private const int DefaultPageSize = 20;
    private const int MaxPageSize = 100;

    private readonly FreshFarmIdentityDBContext _db;
    private readonly ISellerStoreSettingsResolver _sellerStoreSettingsResolver;
    private readonly IAccountEmailSender _accountEmailSender;
    private readonly ICustomerNotificationPublisher _customerNotificationPublisher;
    private readonly ILogger<AdminMerchantsController> _logger;

    public AdminMerchantsController(
        FreshFarmIdentityDBContext db,
        ISellerStoreSettingsResolver sellerStoreSettingsResolver,
        IAccountEmailSender accountEmailSender,
        ICustomerNotificationPublisher customerNotificationPublisher,
        ILogger<AdminMerchantsController> logger)
    {
        _db = db;
        _sellerStoreSettingsResolver = sellerStoreSettingsResolver;
        _accountEmailSender = accountEmailSender;
        _customerNotificationPublisher = customerNotificationPublisher;
        _logger = logger;
    }

    [HttpGet]
    public async Task<IActionResult> Get(
        [FromQuery] string? search = null,
        [FromQuery] string? status = null,
        [FromQuery] string? queue = null,
        [FromQuery] string? reviewStatus = null,
        [FromQuery] string? reviewWindow = null,
        [FromQuery] int page = 1,
        [FromQuery] int pageSize = DefaultPageSize,
        CancellationToken cancellationToken = default)
    {
        page = page < 1 ? 1 : page;
        pageSize = pageSize <= 0 ? DefaultPageSize : Math.Min(pageSize, MaxPageSize);
        var normalizedStatus = NormalizeStatus(status);
        var normalizedQueue = NormalizeQueue(queue);
        var normalizedReviewStatus = NormalizeReviewStatusFilter(reviewStatus);
        var normalizedReviewWindow = NormalizeReviewWindow(reviewWindow);

        var sellerRoleId = await _db.Roles
            .AsNoTracking()
            .Where(r => r.RoleName == "Seller")
            .Select(r => (int?)r.RoleId)
            .FirstOrDefaultAsync(cancellationToken);

        if (!sellerRoleId.HasValue)
        {
            return Ok(new MerchantListResponse
            {
                Page = page,
                PageSize = pageSize,
                Total = 0,
                TotalPages = 1
            });
        }

        var sellerStoreSettings = _db.SellerStoreSettings.AsNoTracking();

        var query = _db.Users
            .AsNoTracking()
            .Where(u =>
                u.UserRoles.Any(ur => ur.RoleId == sellerRoleId.Value) ||
                sellerStoreSettings.Any(s => s.UserId == u.UserId));

        if (!string.IsNullOrWhiteSpace(search))
        {
            var term = search.Trim().ToLowerInvariant();
            query = query.Where(u =>
                u.UserName.ToLower().Contains(term) ||
                u.FullName.ToLower().Contains(term) ||
                u.Email.ToLower().Contains(term) ||
                (u.Phone != null && u.Phone.Contains(term)) ||
                sellerStoreSettings.Any(s =>
                    s.UserId == u.UserId &&
                    (
                        (s.StoreName != null && s.StoreName.ToLower().Contains(term)) ||
                        (s.StoreEmail != null && s.StoreEmail.ToLower().Contains(term)) ||
                        (s.StorePhone != null && s.StorePhone.Contains(term))
                    )));
        }

        var sellers = await query
            .OrderByDescending(u => u.CreatedAt)
            .Select(u => new MerchantProjection
            {
                SellerId = u.UserId,
                IsSellerApproved = u.UserRoles.Any(ur => ur.RoleId == sellerRoleId.Value),
                UserName = u.UserName,
                FullName = u.FullName,
                Email = u.Email,
                Phone = u.Phone,
                Avatar = u.Avatar,
                IsActive = u.IsActive,
                CreatedAt = u.CreatedAt,
                UpdatedAt = u.UpdatedAt,
                LastLogin = u.UserSessions
                    .OrderByDescending(s => s.CreatedAt)
                    .Select(s => (DateTime?)s.CreatedAt)
                    .FirstOrDefault(),
                AddressDetail = u.AddressBooks.Where(a => a.IsActive).OrderByDescending(a => a.IsDefault).ThenByDescending(a => a.UpdatedAt ?? a.CreatedAt).ThenByDescending(a => a.AddressId).Select(a => a.AddressDetail).FirstOrDefault(),
                Province = u.AddressBooks.Where(a => a.IsActive).OrderByDescending(a => a.IsDefault).ThenByDescending(a => a.UpdatedAt ?? a.CreatedAt).ThenByDescending(a => a.AddressId).Select(a => a.Province).FirstOrDefault(),
                District = u.AddressBooks.Where(a => a.IsActive).OrderByDescending(a => a.IsDefault).ThenByDescending(a => a.UpdatedAt ?? a.CreatedAt).ThenByDescending(a => a.AddressId).Select(a => a.District).FirstOrDefault(),
                Ward = u.AddressBooks.Where(a => a.IsActive).OrderByDescending(a => a.IsDefault).ThenByDescending(a => a.UpdatedAt ?? a.CreatedAt).ThenByDescending(a => a.AddressId).Select(a => a.Ward).FirstOrDefault(),
                StoreName = sellerStoreSettings
                    .Where(s => s.UserId == u.UserId)
                    .OrderByDescending(s => s.UpdatedAt)
                    .ThenByDescending(s => s.SellerStoreSettingId)
                    .Select(s => s.StoreName)
                    .FirstOrDefault(),
                StoreAddress = sellerStoreSettings
                    .Where(s => s.UserId == u.UserId)
                    .OrderByDescending(s => s.UpdatedAt)
                    .ThenByDescending(s => s.SellerStoreSettingId)
                    .Select(s => s.StoreAddress)
                    .FirstOrDefault(),
                StoreEmail = sellerStoreSettings
                    .Where(s => s.UserId == u.UserId)
                    .OrderByDescending(s => s.UpdatedAt)
                    .ThenByDescending(s => s.SellerStoreSettingId)
                    .Select(s => s.StoreEmail)
                    .FirstOrDefault(),
                StorePhone = sellerStoreSettings
                    .Where(s => s.UserId == u.UserId)
                    .OrderByDescending(s => s.UpdatedAt)
                    .ThenByDescending(s => s.SellerStoreSettingId)
                    .Select(s => s.StorePhone)
                    .FirstOrDefault(),
                ApplicationSubmittedAt = sellerStoreSettings
                    .Where(s => s.UserId == u.UserId)
                    .OrderByDescending(s => s.UpdatedAt)
                    .ThenByDescending(s => s.SellerStoreSettingId)
                    .Select(s => (DateTime?)s.CreatedAt)
                    .FirstOrDefault(),
                ApplicationUpdatedAt = sellerStoreSettings
                    .Where(s => s.UserId == u.UserId)
                    .OrderByDescending(s => s.UpdatedAt)
                    .ThenByDescending(s => s.SellerStoreSettingId)
                    .Select(s => (DateTime?)s.UpdatedAt)
                    .FirstOrDefault(),
                LegalFullName = u.SellerKycProfile != null ? u.SellerKycProfile.LegalFullName : null,
                IdentityNumber = u.SellerKycProfile != null ? u.SellerKycProfile.IdentityNumber : null,
                IdentityIssuedDate = u.SellerKycProfile != null ? (DateTime?)u.SellerKycProfile.IdentityIssuedDate : null,
                IdentityIssuedPlace = u.SellerKycProfile != null ? u.SellerKycProfile.IdentityIssuedPlace : null,
                TaxCode = u.SellerKycProfile != null ? u.SellerKycProfile.TaxCode : null,
                BusinessLicenseNumber = u.SellerKycProfile != null ? u.SellerKycProfile.BusinessLicenseNumber : null,
                CitizenIdFrontUrl = u.SellerKycProfile != null ? u.SellerKycProfile.CitizenIdFrontUrl : null,
                CitizenIdBackUrl = u.SellerKycProfile != null ? u.SellerKycProfile.CitizenIdBackUrl : null,
                BusinessLicenseUrl = u.SellerKycProfile != null ? u.SellerKycProfile.BusinessLicenseUrl : null,
                AdditionalDocumentUrl = u.SellerKycProfile != null ? u.SellerKycProfile.AdditionalDocumentUrl : null,
                KycNotes = u.SellerKycProfile != null ? u.SellerKycProfile.Notes : null,
                ReviewStatus = u.SellerKycProfile != null ? u.SellerKycProfile.ReviewStatus : null,
                ReviewNote = u.SellerKycProfile != null ? u.SellerKycProfile.ReviewNote : null,
                ReviewedAt = u.SellerKycProfile != null ? u.SellerKycProfile.ReviewedAt : null
            })
            .ToListAsync(cancellationToken);

        var items = sellers
            .GroupBy(x => x.SellerId)
            .Select(group => SelectPreferredMerchantProjection(group))
            .Select(MapMerchantCard)
            .Where(x => MatchesStatus(x, normalizedStatus))
            .Where(x => MatchesQueue(x, normalizedQueue))
            .Where(x => MatchesReviewStatus(x, normalizedReviewStatus))
            .Where(x => MatchesReviewWindow(x, normalizedReviewWindow))
            .OrderByDescending(x => x.PriorityScore)
            .ThenByDescending(x => x.ProfileScore)
            .ThenByDescending(x => x.CreatedAt)
            .ToList();

        var total = items.Count;
        var totalPages = total == 0 ? 1 : (int)Math.Ceiling(total / (double)pageSize);
        if (page > totalPages)
        {
            page = totalPages;
        }

        var paged = items
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .ToList();

        return Ok(new MerchantListResponse
        {
            Page = page,
            PageSize = pageSize,
            Total = total,
            TotalPages = totalPages,
            Stats = new MerchantStatsDto
            {
                TotalSellers = items.Count,
                ActiveSellers = items.Count(x => x.IsActive),
                SuspendedSellers = items.Count(x => !x.IsActive),
                ReviewNeeded = items.Count(x => string.Equals(x.ComplianceStatus, "review", StringComparison.OrdinalIgnoreCase)),
                MissingAddress = items.Count(x => x.Flags.Any(f => string.Equals(f.Code, "missing-address", StringComparison.OrdinalIgnoreCase))),
                ApprovalQueue = items.Count(x => string.Equals(x.QueueBucket, "approval", StringComparison.OrdinalIgnoreCase)),
                ProfileFixQueue = items.Count(x => string.Equals(x.QueueBucket, "profile_fix", StringComparison.OrdinalIgnoreCase)),
                DormantQueue = items.Count(x => string.Equals(x.QueueBucket, "dormant", StringComparison.OrdinalIgnoreCase))
            },
            Filters = new MerchantFiltersDto
            {
                Search = search?.Trim() ?? string.Empty,
                Status = normalizedStatus,
                Queue = normalizedQueue,
                ReviewStatus = normalizedReviewStatus,
                ReviewWindow = normalizedReviewWindow,
                StatusOptions =
                [
                    new MerchantOptionDto("all", "Tất cả"),
                    new MerchantOptionDto("ready", "Sẵn sàng"),
                    new MerchantOptionDto("review", "Cần rà soát"),
                    new MerchantOptionDto("suspended", "Đang tạm khóa"),
                    new MerchantOptionDto("stale", "Lâu không hoạt động")
                ],
                QueueOptions =
                [
                    new MerchantOptionDto("all", "Tất cả hàng xử lý"),
                    new MerchantOptionDto("approval", "Chờ duyệt"),
                    new MerchantOptionDto("profile_fix", "Bổ sung hồ sơ"),
                    new MerchantOptionDto("dormant", "Ít hoạt động"),
                    new MerchantOptionDto("suspended", "Đang khóa"),
                    new MerchantOptionDto("review", "Rà soát thủ công")
                ],
                ReviewStatusOptions =
                [
                    new MerchantOptionDto("all", "Tất cả"),
                    new MerchantOptionDto("pending", "Đang chờ duyệt"),
                    new MerchantOptionDto("rejected", "Đã từ chối"),
                    new MerchantOptionDto("approved", "Đã duyệt")
                ],
                ReviewWindowOptions =
                [
                    new MerchantOptionDto("all", "Mọi lúc"),
                    new MerchantOptionDto("unreviewed", "Chưa thẩm định"),
                    new MerchantOptionDto("today", "Hôm nay"),
                    new MerchantOptionDto("7d", "7 ngày"),
                    new MerchantOptionDto("30d", "30 ngày")
                ],
                RejectReasonTemplates =
                [
                    "Thiếu ảnh CCCD mặt sau rõ nét.",
                    "Thông tin địa chỉ hoạt động chưa đầy đủ, cần bổ sung địa chỉ cụ thể.",
                    "Thông tin cửa hàng và liên hệ chưa khớp, cần rà soát lại số điện thoại/email.",
                    "Cần bổ sung giấy phép kinh doanh hoặc chứng từ pháp lý liên quan."
                ]
            },
            Merchants = paged
        });
    }

    [HttpGet("{sellerId:int}")]
    public async Task<IActionResult> GetById(int sellerId, CancellationToken cancellationToken = default)
    {
        if (sellerId <= 0)
        {
            return BadRequest("Nhà bán hàng không hợp lệ.");
        }

        var sellerRoleId = await _db.Roles
            .AsNoTracking()
            .Where(r => r.RoleName == "Seller")
            .Select(r => (int?)r.RoleId)
            .FirstOrDefaultAsync(cancellationToken);

        if (!sellerRoleId.HasValue)
        {
            return NotFound("Không tìm thấy vai trò nhà bán hàng.");
        }

        var sellerStoreSettings = _db.SellerStoreSettings.AsNoTracking();

        var sellerRows = await _db.Users
            .AsNoTracking()
            .Where(u => u.UserId == sellerId && (u.UserRoles.Any(ur => ur.RoleId == sellerRoleId.Value) || sellerStoreSettings.Any(s => s.UserId == u.UserId)))
            .Select(u => new MerchantProjection
            {
                SellerId = u.UserId,
                IsSellerApproved = u.UserRoles.Any(ur => ur.RoleId == sellerRoleId.Value),
                UserName = u.UserName,
                FullName = u.FullName,
                Email = u.Email,
                Phone = u.Phone,
                Avatar = u.Avatar,
                IsActive = u.IsActive,
                CreatedAt = u.CreatedAt,
                UpdatedAt = u.UpdatedAt,
                LastLogin = u.UserSessions
                    .OrderByDescending(s => s.CreatedAt)
                    .Select(s => (DateTime?)s.CreatedAt)
                    .FirstOrDefault(),
                AddressDetail = u.AddressBooks.Where(a => a.IsActive).OrderByDescending(a => a.IsDefault).ThenByDescending(a => a.UpdatedAt ?? a.CreatedAt).ThenByDescending(a => a.AddressId).Select(a => a.AddressDetail).FirstOrDefault(),
                Province = u.AddressBooks.Where(a => a.IsActive).OrderByDescending(a => a.IsDefault).ThenByDescending(a => a.UpdatedAt ?? a.CreatedAt).ThenByDescending(a => a.AddressId).Select(a => a.Province).FirstOrDefault(),
                District = u.AddressBooks.Where(a => a.IsActive).OrderByDescending(a => a.IsDefault).ThenByDescending(a => a.UpdatedAt ?? a.CreatedAt).ThenByDescending(a => a.AddressId).Select(a => a.District).FirstOrDefault(),
                Ward = u.AddressBooks.Where(a => a.IsActive).OrderByDescending(a => a.IsDefault).ThenByDescending(a => a.UpdatedAt ?? a.CreatedAt).ThenByDescending(a => a.AddressId).Select(a => a.Ward).FirstOrDefault(),
                StoreName = sellerStoreSettings
                    .Where(s => s.UserId == u.UserId)
                    .OrderByDescending(s => s.UpdatedAt)
                    .ThenByDescending(s => s.SellerStoreSettingId)
                    .Select(s => s.StoreName)
                    .FirstOrDefault(),
                StoreAddress = sellerStoreSettings
                    .Where(s => s.UserId == u.UserId)
                    .OrderByDescending(s => s.UpdatedAt)
                    .ThenByDescending(s => s.SellerStoreSettingId)
                    .Select(s => s.StoreAddress)
                    .FirstOrDefault(),
                StoreEmail = sellerStoreSettings
                    .Where(s => s.UserId == u.UserId)
                    .OrderByDescending(s => s.UpdatedAt)
                    .ThenByDescending(s => s.SellerStoreSettingId)
                    .Select(s => s.StoreEmail)
                    .FirstOrDefault(),
                StorePhone = sellerStoreSettings
                    .Where(s => s.UserId == u.UserId)
                    .OrderByDescending(s => s.UpdatedAt)
                    .ThenByDescending(s => s.SellerStoreSettingId)
                    .Select(s => s.StorePhone)
                    .FirstOrDefault(),
                ApplicationSubmittedAt = sellerStoreSettings
                    .Where(s => s.UserId == u.UserId)
                    .OrderByDescending(s => s.UpdatedAt)
                    .ThenByDescending(s => s.SellerStoreSettingId)
                    .Select(s => (DateTime?)s.CreatedAt)
                    .FirstOrDefault(),
                ApplicationUpdatedAt = sellerStoreSettings
                    .Where(s => s.UserId == u.UserId)
                    .OrderByDescending(s => s.UpdatedAt)
                    .ThenByDescending(s => s.SellerStoreSettingId)
                    .Select(s => (DateTime?)s.UpdatedAt)
                    .FirstOrDefault(),
                LegalFullName = u.SellerKycProfile != null ? u.SellerKycProfile.LegalFullName : null,
                IdentityNumber = u.SellerKycProfile != null ? u.SellerKycProfile.IdentityNumber : null,
                IdentityIssuedDate = u.SellerKycProfile != null ? (DateTime?)u.SellerKycProfile.IdentityIssuedDate : null,
                IdentityIssuedPlace = u.SellerKycProfile != null ? u.SellerKycProfile.IdentityIssuedPlace : null,
                TaxCode = u.SellerKycProfile != null ? u.SellerKycProfile.TaxCode : null,
                BusinessLicenseNumber = u.SellerKycProfile != null ? u.SellerKycProfile.BusinessLicenseNumber : null,
                CitizenIdFrontUrl = u.SellerKycProfile != null ? u.SellerKycProfile.CitizenIdFrontUrl : null,
                CitizenIdBackUrl = u.SellerKycProfile != null ? u.SellerKycProfile.CitizenIdBackUrl : null,
                BusinessLicenseUrl = u.SellerKycProfile != null ? u.SellerKycProfile.BusinessLicenseUrl : null,
                AdditionalDocumentUrl = u.SellerKycProfile != null ? u.SellerKycProfile.AdditionalDocumentUrl : null,
                KycNotes = u.SellerKycProfile != null ? u.SellerKycProfile.Notes : null,
                ReviewStatus = u.SellerKycProfile != null ? u.SellerKycProfile.ReviewStatus : null,
                ReviewNote = u.SellerKycProfile != null ? u.SellerKycProfile.ReviewNote : null,
                ReviewedAt = u.SellerKycProfile != null ? u.SellerKycProfile.ReviewedAt : null
            })
            .ToListAsync(cancellationToken);

        var seller = sellerRows
            .GroupBy(x => x.SellerId)
            .Select(group => SelectPreferredMerchantProjection(group))
            .FirstOrDefault();

        if (seller is null)
        {
            return NotFound("Không tìm thấy nhà bán hàng.");
        }

        var detail = MapMerchantDetail(seller);
        detail.ReviewHistory = await _db.SellerKycReviewEvents
            .AsNoTracking()
            .Where(x => x.UserId == sellerId)
            .OrderByDescending(x => x.ReviewedAt)
            .Take(10)
            .Select(x => new MerchantReviewHistoryDto
            {
                Action = x.Action,
                ReviewStatus = x.ReviewStatus,
                ReviewStatusLabel = BuildReviewStatusLabel(x.ReviewStatus, string.Equals(x.ReviewStatus, "approved", StringComparison.OrdinalIgnoreCase)),
                Note = x.Note,
                ReviewedAt = x.ReviewedAt,
                ReviewedByUserId = x.ReviewedByUserId,
                ReviewerUserName = x.ReviewerUserName,
                ReviewerFullName = x.ReviewerFullName
            })
            .ToListAsync(cancellationToken);

        return Ok(detail);
    }

    [HttpPatch("{sellerId:int}/status")]
    public async Task<IActionResult> UpdateStatus(int sellerId, [FromBody] UpdateMerchantStatusRequest request, CancellationToken cancellationToken = default)
    {
        if (sellerId <= 0)
        {
            return BadRequest("Nhà bán hàng không hợp lệ.");
        }

        var sellerRoleId = await _db.Roles
            .AsNoTracking()
            .Where(r => r.RoleName == "Seller")
            .Select(r => (int?)r.RoleId)
            .FirstOrDefaultAsync(cancellationToken);

        if (!sellerRoleId.HasValue)
        {
            return NotFound("Không tìm thấy vai trò nhà bán hàng.");
        }

        var user = await _db.Users
            .FirstOrDefaultAsync(
                u => u.UserId == sellerId &&
                    (u.UserRoles.Any(ur => ur.RoleId == sellerRoleId.Value) || _db.SellerStoreSettings.Any(s => s.UserId == u.UserId)),
                cancellationToken);

        if (user is null)
        {
            return NotFound("Không tìm thấy nhà bán hàng.");
        }

        user.IsActive = request.IsActive;
        user.UpdatedAt = DateTime.UtcNow;
        await _db.SaveChangesAsync(cancellationToken);

        return Ok(new
        {
            sellerId = user.UserId,
            isActive = user.IsActive,
            message = user.IsActive ? "Đã mở lại nhà bán hàng." : "Đã tạm khóa nhà bán hàng."
        });
    }

    [HttpPost("{sellerId:int}/approve")]
    public async Task<IActionResult> ApproveSeller(int sellerId, CancellationToken cancellationToken = default)
    {
        if (sellerId <= 0)
        {
            return BadRequest("Nhà bán hàng không hợp lệ.");
        }

        var sellerRole = await _db.Roles
            .SingleOrDefaultAsync(r => r.RoleName == "Seller", cancellationToken);

        if (sellerRole is null)
        {
            return NotFound("Không tìm thấy vai trò nhà bán hàng.");
        }

        var user = await _db.Users
            .Include(u => u.UserRoles)
            .Include(u => u.SellerKycProfile)
            .FirstOrDefaultAsync(u => u.UserId == sellerId, cancellationToken);

        var sellerStoreSetting = user is null
            ? null
            : await _sellerStoreSettingsResolver.GetLatestForUserAsync(user.UserId, asNoTracking: false, cancellationToken);

        if (user is null || sellerStoreSetting is null)
        {
            return NotFound("Không tìm thấy hồ sơ đăng ký người bán.");
        }

        var reviewerInfo = await GetReviewerInfoAsync(cancellationToken);

        if (!HasCompleteSellerKyc(sellerStoreSetting, user.SellerKycProfile))
        {
            return BadRequest("Hồ sơ người bán chưa đầy đủ. Cần đủ thông tin cửa hàng, thông tin CCCD và ảnh CCCD hai mặt trước khi duyệt người bán.");
        }

        if (!user.UserRoles.Any(ur => ur.RoleId == sellerRole.RoleId))
        {
            _db.UserRoles.Add(new UserRole
            {
                UserId = user.UserId,
                RoleId = sellerRole.RoleId,
                CreatedAt = DateTime.UtcNow
            });
        }

        if (user.SellerKycProfile is not null)
        {
            user.SellerKycProfile.ReviewStatus = "approved";
            user.SellerKycProfile.ReviewNote = null;
            user.SellerKycProfile.ReviewedAt = DateTime.UtcNow;
            user.SellerKycProfile.ReviewedByUserId = reviewerInfo.UserId;
            user.SellerKycProfile.UpdatedAt = DateTime.UtcNow;
        }

        _db.SellerKycReviewEvents.Add(new SellerKycReviewEvent
        {
            UserId = user.UserId,
            Action = "approve",
            ReviewStatus = "approved",
            ReviewedAt = DateTime.UtcNow,
            ReviewedByUserId = reviewerInfo.UserId,
            ReviewerUserName = reviewerInfo.UserName,
            ReviewerFullName = reviewerInfo.FullName
        });

        user.IsActive = true;
        user.UpdatedAt = DateTime.UtcNow;
        await _db.SaveChangesAsync(cancellationToken);
        await TryPublishSellerReviewNotificationAsync(user, sellerStoreSetting.StoreName, isApproved: true, reviewNote: null, cancellationToken);
        await TrySendSellerReviewEmailAsync(user, sellerStoreSetting.StoreName, isApproved: true, reviewNote: null, cancellationToken);

        return Ok(new
        {
            sellerId = user.UserId,
            message = "Đã duyệt hồ sơ và cấp quyền người bán."
        });
    }

    [HttpPost("{sellerId:int}/reject")]
    public async Task<IActionResult> RejectSeller(int sellerId, [FromBody] RejectSellerApplicationRequestDto request, CancellationToken cancellationToken = default)
    {
        if (sellerId <= 0)
        {
            return BadRequest("Nhà bán hàng không hợp lệ.");
        }

        if (!ModelState.IsValid)
        {
            return ValidationProblem(ModelState);
        }

        var sellerRoleId = await _db.Roles
            .AsNoTracking()
            .Where(r => r.RoleName == "Seller")
            .Select(r => (int?)r.RoleId)
            .FirstOrDefaultAsync(cancellationToken);

        var user = await _db.Users
            .Include(u => u.UserRoles)
            .Include(u => u.SellerKycProfile)
            .FirstOrDefaultAsync(u => u.UserId == sellerId, cancellationToken);
        var reviewerInfo = await GetReviewerInfoAsync(cancellationToken);

        var sellerStoreSetting = user is null
            ? null
            : await _sellerStoreSettingsResolver.GetLatestForUserAsync(user.UserId, asNoTracking: false, cancellationToken);

        if (user is null || sellerStoreSetting is null || user.SellerKycProfile is null)
        {
            return NotFound("Không tìm thấy hồ sơ đăng ký người bán.");
        }

        if (sellerRoleId.HasValue && user.UserRoles.Any(ur => ur.RoleId == sellerRoleId.Value))
        {
            return BadRequest("Nhà bán hàng đã được duyệt. Hãy dùng tạm khóa nếu cần chặn vận hành.");
        }

        var originalReason = request.Reason.Trim();
        var reason = NormalizeRejectReason(request.Reason);
        if (!string.Equals(reason, originalReason, StringComparison.Ordinal))
        {
            _logger.LogInformation(
                "Da chuan hoa reject reason cho seller {SellerId} tu '{OriginalReason}' thanh '{NormalizedReason}'.",
                user.UserId,
                originalReason,
                reason);
        }

        user.SellerKycProfile.ReviewStatus = "rejected";
        user.SellerKycProfile.ReviewNote = reason;
        user.SellerKycProfile.ReviewedAt = DateTime.UtcNow;
        user.SellerKycProfile.ReviewedByUserId = reviewerInfo.UserId;
        user.SellerKycProfile.UpdatedAt = DateTime.UtcNow;
        user.UpdatedAt = DateTime.UtcNow;

        _db.SellerKycReviewEvents.Add(new SellerKycReviewEvent
        {
            UserId = user.UserId,
            Action = "reject",
            ReviewStatus = "rejected",
            Note = reason,
            ReviewedAt = DateTime.UtcNow,
            ReviewedByUserId = reviewerInfo.UserId,
            ReviewerUserName = reviewerInfo.UserName,
            ReviewerFullName = reviewerInfo.FullName
        });

        await _db.SaveChangesAsync(cancellationToken);
        await TryPublishSellerReviewNotificationAsync(user, sellerStoreSetting.StoreName, isApproved: false, reviewNote: reason, cancellationToken);
        await TrySendSellerReviewEmailAsync(user, sellerStoreSetting.StoreName, isApproved: false, reviewNote: reason, cancellationToken);

        return Ok(new
        {
            sellerId = user.UserId,
            message = "Đã từ chối hồ sơ người bán và gửi lại yêu cầu bổ sung."
        });
    }

    private static MerchantListItemDto MapMerchantCard(MerchantProjection row)
    {
        var flags = BuildFlags(row);
        var profileScore = CalculateProfileScore(row);
        var complianceStatus = DetermineComplianceStatus(row, flags, profileScore);
        var lastLogin = row.LastLogin;
        var daysSinceLastLogin = lastLogin.HasValue ? (int)Math.Floor((DateTime.UtcNow - lastLogin.Value).TotalDays) : (int?)null;

        return new MerchantListItemDto
        {
            SellerId = row.SellerId,
            IsSellerApproved = row.IsSellerApproved,
            ReviewStatus = NormalizeReviewStatus(row.ReviewStatus, row.IsSellerApproved),
            ReviewStatusLabel = BuildReviewStatusLabel(row.ReviewStatus, row.IsSellerApproved),
            ReviewNote = row.ReviewNote,
            ReviewedAt = row.ReviewedAt,
            ShopName = !string.IsNullOrWhiteSpace(row.StoreName)
                ? row.StoreName
                : string.IsNullOrWhiteSpace(row.FullName) ? (row.UserName ?? $"Nhà bán hàng #{row.SellerId}") : row.FullName,
            UserName = row.UserName,
            FullName = row.FullName,
            Email = row.Email,
            Phone = row.Phone,
            StoreName = row.StoreName,
            StoreAddress = row.StoreAddress,
            StoreEmail = row.StoreEmail,
            StorePhone = row.StorePhone,
            Avatar = row.Avatar,
            IsActive = row.IsActive,
            CreatedAt = row.CreatedAt,
            UpdatedAt = row.UpdatedAt,
            LastLogin = lastLogin,
            ApplicationSubmittedAt = row.ApplicationSubmittedAt,
            ApplicationUpdatedAt = row.ApplicationUpdatedAt,
            AddressSummary = BuildAddressSummary(row),
            ProfileScore = profileScore,
            ComplianceStatus = complianceStatus,
            Flags = flags,
            DaysSinceLastLogin = daysSinceLastLogin,
            QueueBucket = DetermineQueueBucket(row, flags, profileScore),
            RecommendedAction = BuildRecommendedAction(row, flags, profileScore),
            IssueCount = flags.Count,
            PriorityScore = CalculatePriorityScore(row, flags, profileScore)
        };
    }

    private static MerchantProjection SelectPreferredMerchantProjection(IEnumerable<MerchantProjection> rows)
    {
        return rows
            .OrderByDescending(x => x.ApplicationUpdatedAt ?? x.ApplicationSubmittedAt ?? DateTime.MinValue)
            .ThenByDescending(x => x.ApplicationSubmittedAt ?? DateTime.MinValue)
            .ThenByDescending(x => x.UpdatedAt ?? x.CreatedAt)
            .First();
    }

    private static MerchantDetailDto MapMerchantDetail(MerchantProjection row)
    {
        var card = MapMerchantCard(row);
        return new MerchantDetailDto
        {
            SellerId = card.SellerId,
            IsSellerApproved = card.IsSellerApproved,
            ShopName = card.ShopName,
            UserName = card.UserName,
            FullName = card.FullName,
            Email = card.Email,
            Phone = card.Phone,
            StoreName = card.StoreName,
            StoreAddress = card.StoreAddress,
            StoreEmail = card.StoreEmail,
            StorePhone = card.StorePhone,
            Avatar = card.Avatar,
            IsActive = card.IsActive,
            CreatedAt = card.CreatedAt,
            UpdatedAt = card.UpdatedAt,
            LastLogin = card.LastLogin,
            ApplicationSubmittedAt = card.ApplicationSubmittedAt,
            ApplicationUpdatedAt = card.ApplicationUpdatedAt,
            LegalFullName = row.LegalFullName,
            IdentityNumberMasked = MaskIdentityNumber(row.IdentityNumber),
            IdentityIssuedDate = row.IdentityIssuedDate,
            IdentityIssuedPlace = row.IdentityIssuedPlace,
            TaxCode = row.TaxCode,
            BusinessLicenseNumber = row.BusinessLicenseNumber,
            CitizenIdFrontUrl = row.CitizenIdFrontUrl,
            CitizenIdBackUrl = row.CitizenIdBackUrl,
            BusinessLicenseUrl = row.BusinessLicenseUrl,
            AdditionalDocumentUrl = row.AdditionalDocumentUrl,
            KycNotes = row.KycNotes,
            ReviewStatus = card.ReviewStatus,
            ReviewStatusLabel = card.ReviewStatusLabel,
            ReviewNote = card.ReviewNote,
            ReviewedAt = card.ReviewedAt,
            AddressSummary = card.AddressSummary,
            AddressDetail = row.AddressDetail,
            Province = row.Province,
            District = row.District,
            Ward = row.Ward,
            ProfileScore = card.ProfileScore,
            ComplianceStatus = card.ComplianceStatus,
            Flags = card.Flags,
            DaysSinceLastLogin = card.DaysSinceLastLogin,
            ComplianceSummary = BuildComplianceSummary(card),
            QueueBucket = card.QueueBucket,
            RecommendedAction = card.RecommendedAction,
            IssueCount = card.IssueCount,
            PriorityScore = card.PriorityScore,
            NextSteps = BuildNextSteps(card)
        };
    }

    private static string DetermineQueueBucket(MerchantProjection row, IReadOnlyCollection<MerchantFlagDto> flags, int profileScore)
    {
        if (!row.IsSellerApproved)
        {
            return "approval";
        }

        if (!row.IsActive)
        {
            return "suspended";
        }

        if (flags.Any(f => f.Code is "missing-phone" or "missing-email" or "missing-address" or "missing-store-name" or "missing-store-email" or "missing-store-phone"))
        {
            return "profile_fix";
        }

        if (flags.Any(f => f.Code == "stale-login"))
        {
            return "dormant";
        }

        if (profileScore >= 80)
        {
            return "approval";
        }

        return "review";
    }

    private static string BuildRecommendedAction(MerchantProjection row, IReadOnlyCollection<MerchantFlagDto> flags, int profileScore)
    {
        var reviewStatus = NormalizeReviewStatus(row.ReviewStatus, row.IsSellerApproved);
        if (!row.IsSellerApproved && reviewStatus == "rejected")
        {
            return "Bổ sung lại hồ sơ pháp lý và thông tin cửa hàng theo lý do từ chối trước khi gửi lại cho quản trị viên.";
        }

        if (!row.IsSellerApproved)
        {
            return "Rà soát hồ sơ cửa hàng và hồ sơ pháp lý, chỉ cấp quyền người bán khi CCCD cùng thông tin vận hành đã đủ.";
        }

        if (!row.IsActive)
        {
            return "Rà soát lý do khóa và chỉ mở lại khi nhà bán hàng xác nhận tiếp tục vận hành.";
        }

        if (flags.Any(f => f.Code is "missing-phone" or "missing-email" or "missing-address" or "missing-store-name" or "missing-store-email" or "missing-store-phone"))
        {
            return "Yêu cầu nhà bán hàng bổ sung thông tin liên hệ và địa chỉ hoạt động trước khi đưa vào chương trình tăng trưởng.";
        }

        if (flags.Any(f => f.Code == "stale-login"))
        {
            return "Liên hệ nhà bán hàng để xác nhận cửa hàng còn hoạt động trước khi duyệt chiến dịch hoặc phân bổ lượt truy cập.";
        }

        if (profileScore >= 80)
        {
            return "Có thể đưa vào nhóm ưu tiên cho chương trình tăng trưởng hoặc chiến dịch nội bộ.";
        }

        return "Rà soát thủ công hồ sơ nhà bán hàng trước khi mở rộng quyền hoặc chiến dịch.";
    }

    private static int CalculatePriorityScore(MerchantProjection row, IReadOnlyCollection<MerchantFlagDto> flags, int profileScore)
    {
        var reviewStatus = NormalizeReviewStatus(row.ReviewStatus, row.IsSellerApproved);
        if (!row.IsSellerApproved && reviewStatus == "rejected")
        {
            return 130 + Math.Max(0, 100 - profileScore);
        }

        if (!row.IsSellerApproved)
        {
            return 120 + Math.Max(0, 100 - profileScore);
        }

        if (!row.IsActive)
        {
            return 100;
        }

        var score = 0;
        if (flags.Any(f => f.Code == "missing-address"))
        {
            score += 35;
        }

        if (flags.Any(f => f.Code is "missing-phone" or "missing-email" or "missing-store-email" or "missing-store-phone"))
        {
            score += 20;
        }

        if (flags.Any(f => f.Code == "stale-login"))
        {
            score += 15;
        }

        score += Math.Max(0, 100 - profileScore);
        return score;
    }

    private static List<MerchantFlagDto> BuildFlags(MerchantProjection row)
    {
        var flags = new List<MerchantFlagDto>();

        var reviewStatus = NormalizeReviewStatus(row.ReviewStatus, row.IsSellerApproved);
        if (!row.IsSellerApproved && reviewStatus == "rejected")
        {
            flags.Add(new MerchantFlagDto("rejected-application", "Hồ sơ đã bị từ chối", "danger"));
        }
        else if (!row.IsSellerApproved)
        {
            flags.Add(new MerchantFlagDto("pending-approval", "Hồ sơ đang chờ duyệt", "warning"));
        }

        if (string.IsNullOrWhiteSpace(row.Phone))
        {
            flags.Add(new MerchantFlagDto("missing-phone", "Thiếu số điện thoại", "warning"));
        }

        if (string.IsNullOrWhiteSpace(row.StorePhone))
        {
            flags.Add(new MerchantFlagDto("missing-store-phone", "Thiếu số điện thoại cửa hàng", "warning"));
        }

        if (string.IsNullOrWhiteSpace(row.Email))
        {
            flags.Add(new MerchantFlagDto("missing-email", "Thiếu email", "warning"));
        }

        if (string.IsNullOrWhiteSpace(row.StoreEmail))
        {
            flags.Add(new MerchantFlagDto("missing-store-email", "Thiếu email cửa hàng", "warning"));
        }

        if (string.IsNullOrWhiteSpace(row.StoreName))
        {
            flags.Add(new MerchantFlagDto("missing-store-name", "Thiếu tên cửa hàng", "warning"));
        }

        if (string.IsNullOrWhiteSpace(row.StoreAddress) &&
            string.IsNullOrWhiteSpace(row.AddressDetail) &&
            string.IsNullOrWhiteSpace(row.Province) &&
            string.IsNullOrWhiteSpace(row.District) &&
            string.IsNullOrWhiteSpace(row.Ward))
        {
            flags.Add(new MerchantFlagDto("missing-address", "Thiếu địa chỉ hoạt động", "danger"));
        }

        if (string.IsNullOrWhiteSpace(row.LegalFullName) || string.IsNullOrWhiteSpace(row.IdentityNumber))
        {
            flags.Add(new MerchantFlagDto("missing-kyc-core", "Thiếu thông tin CCCD", "danger"));
        }

        if (string.IsNullOrWhiteSpace(row.CitizenIdFrontUrl) || string.IsNullOrWhiteSpace(row.CitizenIdBackUrl))
        {
            flags.Add(new MerchantFlagDto("missing-identity-files", "Thiếu ảnh CCCD", "danger"));
        }

        if (string.IsNullOrWhiteSpace(row.Avatar))
        {
            flags.Add(new MerchantFlagDto("missing-avatar", "Thiếu ảnh đại diện", "neutral"));
        }

        if (row.IsSellerApproved && (!row.LastLogin.HasValue || row.LastLogin.Value < DateTime.UtcNow.AddDays(-30)))
        {
            flags.Add(new MerchantFlagDto("stale-login", "Không có đăng nhập trong 30 ngày", "warning"));
        }

        return flags;
    }

    private static int CalculateProfileScore(MerchantProjection row)
    {
        var points = 0;

        if (!string.IsNullOrWhiteSpace(row.UserName))
        {
            points += 10;
        }

        if (!string.IsNullOrWhiteSpace(row.FullName))
        {
            points += 15;
        }

        if (!string.IsNullOrWhiteSpace(row.Email))
        {
            points += 10;
        }

        if (!string.IsNullOrWhiteSpace(row.Phone))
        {
            points += 10;
        }

        if (!string.IsNullOrWhiteSpace(row.Avatar))
        {
            points += 5;
        }

        if (!string.IsNullOrWhiteSpace(row.StoreName))
        {
            points += 20;
        }

        if (!string.IsNullOrWhiteSpace(row.StoreEmail))
        {
            points += 10;
        }

        if (!string.IsNullOrWhiteSpace(row.StorePhone))
        {
            points += 10;
        }

        if (!string.IsNullOrWhiteSpace(row.LegalFullName))
        {
            points += 10;
        }

        if (!string.IsNullOrWhiteSpace(row.IdentityNumber))
        {
            points += 5;
        }

        if (!string.IsNullOrWhiteSpace(row.CitizenIdFrontUrl) && !string.IsNullOrWhiteSpace(row.CitizenIdBackUrl))
        {
            points += 5;
        }

        if (!string.IsNullOrWhiteSpace(row.BusinessLicenseUrl))
        {
            points += 5;
        }

        if (!string.IsNullOrWhiteSpace(row.StoreAddress) ||
            !string.IsNullOrWhiteSpace(row.AddressDetail) ||
            !string.IsNullOrWhiteSpace(row.Province) ||
            !string.IsNullOrWhiteSpace(row.District) ||
            !string.IsNullOrWhiteSpace(row.Ward))
        {
            points += 20;
        }

        return Math.Min(points, 100);
    }

    private static string DetermineComplianceStatus(MerchantProjection row, IReadOnlyCollection<MerchantFlagDto> flags, int profileScore)
    {
        var reviewStatus = NormalizeReviewStatus(row.ReviewStatus, row.IsSellerApproved);
        if (!row.IsSellerApproved && reviewStatus == "rejected")
        {
            return "review";
        }

        if (!row.IsSellerApproved)
        {
            return "review";
        }

        if (!row.IsActive)
        {
            return "suspended";
        }

        if (profileScore < 70 || flags.Any(f => f.Code is "missing-phone" or "missing-address" or "stale-login" or "missing-store-name" or "missing-store-email" or "missing-store-phone" or "missing-kyc-core" or "missing-identity-files"))
        {
            return "review";
        }

        return "ready";
    }

    private static bool MatchesStatus(MerchantListItemDto item, string status)
    {
        return status switch
        {
            "ready" => string.Equals(item.ComplianceStatus, "ready", StringComparison.OrdinalIgnoreCase),
            "review" => string.Equals(item.ComplianceStatus, "review", StringComparison.OrdinalIgnoreCase),
            "suspended" => !item.IsActive,
            "stale" => item.Flags.Any(f => string.Equals(f.Code, "stale-login", StringComparison.OrdinalIgnoreCase)),
            _ => true
        };
    }

    private static bool MatchesQueue(MerchantListItemDto item, string queue)
    {
        return queue switch
        {
            "approval" => string.Equals(item.QueueBucket, "approval", StringComparison.OrdinalIgnoreCase),
            "profile_fix" => string.Equals(item.QueueBucket, "profile_fix", StringComparison.OrdinalIgnoreCase),
            "dormant" => string.Equals(item.QueueBucket, "dormant", StringComparison.OrdinalIgnoreCase),
            "suspended" => string.Equals(item.QueueBucket, "suspended", StringComparison.OrdinalIgnoreCase),
            "review" => string.Equals(item.QueueBucket, "review", StringComparison.OrdinalIgnoreCase),
            _ => true
        };
    }

    private static bool MatchesReviewStatus(MerchantListItemDto item, string reviewStatus)
    {
        return reviewStatus switch
        {
            "pending" => string.Equals(item.ReviewStatus, "pending", StringComparison.OrdinalIgnoreCase),
            "rejected" => string.Equals(item.ReviewStatus, "rejected", StringComparison.OrdinalIgnoreCase),
            "approved" => string.Equals(item.ReviewStatus, "approved", StringComparison.OrdinalIgnoreCase),
            _ => true
        };
    }

    private static bool MatchesReviewWindow(MerchantListItemDto item, string reviewWindow)
    {
        var reviewedAt = item.ReviewedAt;
        var now = DateTime.UtcNow;

        return reviewWindow switch
        {
            "unreviewed" => !reviewedAt.HasValue,
            "today" => reviewedAt.HasValue && reviewedAt.Value >= now.AddDays(-1),
            "7d" => reviewedAt.HasValue && reviewedAt.Value >= now.AddDays(-7),
            "30d" => reviewedAt.HasValue && reviewedAt.Value >= now.AddDays(-30),
            _ => true
        };
    }

    private static string BuildAddressSummary(MerchantProjection row)
    {
        var parts = new[]
        {
            row.StoreAddress,
            row.AddressDetail,
            row.Ward,
            row.District,
            row.Province
        }
        .Where(x => !string.IsNullOrWhiteSpace(x))
        .Select(x => x!.Trim())
        .Distinct(StringComparer.OrdinalIgnoreCase)
        .ToArray();

        return parts.Length == 0 ? "Chưa có địa chỉ hoạt động" : string.Join(", ", parts);
    }

    private static string BuildComplianceSummary(MerchantListItemDto card)
    {
        if (!card.IsSellerApproved && string.Equals(card.ReviewStatus, "rejected", StringComparison.OrdinalIgnoreCase))
        {
            if (!string.IsNullOrWhiteSpace(card.ReviewNote))
            {
                return $"Hồ sơ người bán đã bị từ chối. Nhà bán hàng cần bổ sung trước khi gửi lại: {card.ReviewNote.Trim()}";
            }

            return "Hồ sơ người bán đã bị từ chối và cần bổ sung trước khi gửi lại.";
        }

        if (!card.IsSellerApproved)
        {
            return "Hồ sơ đã được gửi nhưng chưa được cấp quyền người bán. Cần rà soát thông tin cửa hàng trước khi duyệt.";
        }

        if (!card.IsActive)
        {
            return "Nhà bán hàng đang bị tạm khóa. Cần rà soát trước khi mở lại.";
        }

        if (string.Equals(card.ComplianceStatus, "ready", StringComparison.OrdinalIgnoreCase))
        {
            return "Hồ sơ nhà bán hàng đã đủ thông tin nền tảng để vận hành.";
        }

        if (card.Flags.Count == 0)
        {
            return "Nhà bán hàng cần rà soát thủ công trước khi đưa vào các chương trình tăng trưởng.";
        }

        return "Nhà bán hàng cần bổ sung hoặc xác nhận lại các mục: " + string.Join("; ", card.Flags.Select(x => x.Label)) + ".";
    }

    private static List<string> BuildNextSteps(MerchantListItemDto card)
    {
        var steps = new List<string>();

        if (!card.IsSellerApproved && string.Equals(card.ReviewStatus, "rejected", StringComparison.OrdinalIgnoreCase))
        {
            steps.Add("Đọc kỹ lý do từ chối và đối chiếu lại bộ hồ sơ pháp lý.");
            steps.Add("Yêu cầu nhà bán hàng cập nhật lại hồ sơ rồi gửi lại để chuyển về trạng thái chờ duyệt.");
            if (!string.IsNullOrWhiteSpace(card.ReviewNote))
            {
                steps.Add("Lý do từ chối hiện tại: " + card.ReviewNote.Trim());
            }
        }
        else if (!card.IsSellerApproved)
        {
            steps.Add("Rà soát tên cửa hàng, địa chỉ hoạt động và thông tin liên hệ trước khi duyệt.");
            steps.Add("Xác nhận hồ sơ pháp lý có đủ họ tên pháp lý, số CCCD, ngày/nơi cấp và ảnh CCCD hai mặt.");
            steps.Add("Cấp quyền người bán sau khi xác nhận hồ sơ phù hợp.");
        }

        if (!card.IsActive)
        {
            steps.Add("Xác minh lý do tạm khóa với đội vận hành hoặc CS.");
            steps.Add("Chỉ mở lại nhà bán hàng khi hồ sơ và trạng thái vận hành đã rõ.");
        }

        if (card.Flags.Any(f => f.Code is "missing-phone" or "missing-email" or "missing-store-phone" or "missing-store-email"))
        {
            steps.Add("Yêu cầu bổ sung thông tin liên hệ chính.");
        }

        if (card.Flags.Any(f => f.Code == "missing-address"))
        {
            steps.Add("Yêu cầu cập nhật địa chỉ hoạt động hoặc kho xử lý đơn.");
        }

        if (card.Flags.Any(f => f.Code == "stale-login"))
        {
            steps.Add("Kiểm tra nhà bán hàng còn đăng nhập và xử lý đơn trong 30 ngày gần đây hay không.");
        }

        if (steps.Count == 0 && string.Equals(card.QueueBucket, "approval", StringComparison.OrdinalIgnoreCase))
        {
            steps.Add("Có thể đưa nhà bán hàng vào nhóm ưu tiên cho chiến dịch hoặc quy trình hướng dẫn nâng cao.");
        }

        if (steps.Count == 0)
        {
            steps.Add("Rà soát thủ công thêm để xác nhận nhà bán hàng sẵn sàng vận hành.");
        }

        return steps;
    }

    private static string NormalizeStatus(string? status)
    {
        if (string.IsNullOrWhiteSpace(status))
        {
            return "all";
        }

        var normalized = status.Trim().ToLowerInvariant();
        return normalized is "all" or "ready" or "review" or "suspended" or "stale"
            ? normalized
            : "all";
    }

    private static string NormalizeQueue(string? queue)
    {
        if (string.IsNullOrWhiteSpace(queue))
        {
            return "all";
        }

        var normalized = queue.Trim().ToLowerInvariant();
        return normalized is "all" or "approval" or "profile_fix" or "dormant" or "suspended" or "review"
            ? normalized
            : "all";
    }

    private static string NormalizeReviewStatusFilter(string? reviewStatus)
    {
        if (string.IsNullOrWhiteSpace(reviewStatus))
        {
            return "all";
        }

        var normalized = reviewStatus.Trim().ToLowerInvariant();
        return normalized is "all" or "pending" or "rejected" or "approved"
            ? normalized
            : "all";
    }

    private static string NormalizeReviewWindow(string? reviewWindow)
    {
        if (string.IsNullOrWhiteSpace(reviewWindow))
        {
            return "all";
        }

        var normalized = reviewWindow.Trim().ToLowerInvariant();
        return normalized is "all" or "unreviewed" or "today" or "7d" or "30d"
            ? normalized
            : "all";
    }

    private sealed class MerchantProjection
    {
        public int SellerId { get; set; }
        public bool IsSellerApproved { get; set; }
        public string? ReviewStatus { get; set; }
        public string? ReviewNote { get; set; }
        public DateTime? ReviewedAt { get; set; }
        public string? UserName { get; set; }
        public string? FullName { get; set; }
        public string? Email { get; set; }
        public string? Phone { get; set; }
        public string? StoreName { get; set; }
        public string? StoreAddress { get; set; }
        public string? StoreEmail { get; set; }
        public string? StorePhone { get; set; }
        public string? Avatar { get; set; }
        public bool IsActive { get; set; }
        public DateTime CreatedAt { get; set; }
        public DateTime? UpdatedAt { get; set; }
        public DateTime? LastLogin { get; set; }
        public DateTime? ApplicationSubmittedAt { get; set; }
        public DateTime? ApplicationUpdatedAt { get; set; }
        public string? LegalFullName { get; set; }
        public string? IdentityNumber { get; set; }
        public DateTime? IdentityIssuedDate { get; set; }
        public string? IdentityIssuedPlace { get; set; }
        public string? TaxCode { get; set; }
        public string? BusinessLicenseNumber { get; set; }
        public string? CitizenIdFrontUrl { get; set; }
        public string? CitizenIdBackUrl { get; set; }
        public string? BusinessLicenseUrl { get; set; }
        public string? AdditionalDocumentUrl { get; set; }
        public string? KycNotes { get; set; }
        public string? AddressDetail { get; set; }
        public string? Province { get; set; }
        public string? District { get; set; }
        public string? Ward { get; set; }
    }

    public sealed class UpdateMerchantStatusRequest
    {
        [Required]
        public bool IsActive { get; set; }
    }

    public sealed class MerchantListResponse
    {
        public int Page { get; set; }
        public int PageSize { get; set; }
        public int Total { get; set; }
        public int TotalPages { get; set; }
        public MerchantStatsDto Stats { get; set; } = new();
        public MerchantFiltersDto Filters { get; set; } = new();
        public List<MerchantListItemDto> Merchants { get; set; } = new();
    }

    public sealed class MerchantStatsDto
    {
        public int TotalSellers { get; set; }
        public int ActiveSellers { get; set; }
        public int SuspendedSellers { get; set; }
        public int ReviewNeeded { get; set; }
        public int MissingAddress { get; set; }
        public int ApprovalQueue { get; set; }
        public int ProfileFixQueue { get; set; }
        public int DormantQueue { get; set; }
    }

    public sealed class MerchantFiltersDto
    {
        public string Search { get; set; } = string.Empty;
        public string Status { get; set; } = "all";
        public string Queue { get; set; } = "all";
        public string ReviewStatus { get; set; } = "all";
        public string ReviewWindow { get; set; } = "all";
        public List<MerchantOptionDto> StatusOptions { get; set; } = new();
        public List<MerchantOptionDto> QueueOptions { get; set; } = new();
        public List<MerchantOptionDto> ReviewStatusOptions { get; set; } = new();
        public List<MerchantOptionDto> ReviewWindowOptions { get; set; } = new();
        public List<string> RejectReasonTemplates { get; set; } = new();
    }

    public sealed record MerchantOptionDto(string Value, string Text);

    public class MerchantListItemDto
    {
        public int SellerId { get; set; }
        public bool IsSellerApproved { get; set; }
        public string ReviewStatus { get; set; } = "not_applied";
        public string ReviewStatusLabel { get; set; } = "Chưa có hồ sơ";
        public string? ReviewNote { get; set; }
        public DateTime? ReviewedAt { get; set; }
        public string ShopName { get; set; } = string.Empty;
        public string? UserName { get; set; }
        public string? FullName { get; set; }
        public string? Email { get; set; }
        public string? Phone { get; set; }
        public string? StoreName { get; set; }
        public string? StoreAddress { get; set; }
        public string? StoreEmail { get; set; }
        public string? StorePhone { get; set; }
        public string? Avatar { get; set; }
        public bool IsActive { get; set; }
        public DateTime CreatedAt { get; set; }
        public DateTime? UpdatedAt { get; set; }
        public DateTime? LastLogin { get; set; }
        public DateTime? ApplicationSubmittedAt { get; set; }
        public DateTime? ApplicationUpdatedAt { get; set; }
        public string? LegalFullName { get; set; }
        public string? IdentityNumberMasked { get; set; }
        public DateTime? IdentityIssuedDate { get; set; }
        public string? IdentityIssuedPlace { get; set; }
        public string? TaxCode { get; set; }
        public string? BusinessLicenseNumber { get; set; }
        public string? CitizenIdFrontUrl { get; set; }
        public string? CitizenIdBackUrl { get; set; }
        public string? BusinessLicenseUrl { get; set; }
        public string? AdditionalDocumentUrl { get; set; }
        public string? KycNotes { get; set; }
        public string AddressSummary { get; set; } = string.Empty;
        public int ProfileScore { get; set; }
        public string ComplianceStatus { get; set; } = string.Empty;
        public List<MerchantFlagDto> Flags { get; set; } = new();
        public int? DaysSinceLastLogin { get; set; }
        public string QueueBucket { get; set; } = string.Empty;
        public string RecommendedAction { get; set; } = string.Empty;
        public int IssueCount { get; set; }
        public int PriorityScore { get; set; }
    }

    public sealed class MerchantDetailDto : MerchantListItemDto
    {
        public string? AddressDetail { get; set; }
        public string? Province { get; set; }
        public string? District { get; set; }
        public string? Ward { get; set; }
        public string ComplianceSummary { get; set; } = string.Empty;
        public List<string> NextSteps { get; set; } = new();
        public List<MerchantReviewHistoryDto> ReviewHistory { get; set; } = new();
    }

    public sealed record MerchantFlagDto(string Code, string Label, string Tone);

    public sealed class MerchantReviewHistoryDto
    {
        public string Action { get; set; } = string.Empty;
        public string ReviewStatus { get; set; } = string.Empty;
        public string ReviewStatusLabel { get; set; } = string.Empty;
        public string? Note { get; set; }
        public DateTime ReviewedAt { get; set; }
        public int? ReviewedByUserId { get; set; }
        public string? ReviewerUserName { get; set; }
        public string? ReviewerFullName { get; set; }
    }

    private static bool HasCompleteSellerKyc(SellerStoreSetting storeSetting, SellerKycProfile? kyc)
    {
        return kyc is not null &&
               !string.IsNullOrWhiteSpace(storeSetting.StoreName) &&
               !string.IsNullOrWhiteSpace(storeSetting.StoreAddress) &&
               !string.IsNullOrWhiteSpace(storeSetting.StoreEmail) &&
               !string.IsNullOrWhiteSpace(storeSetting.StorePhone) &&
               !string.IsNullOrWhiteSpace(kyc.LegalFullName) &&
               !string.IsNullOrWhiteSpace(kyc.IdentityNumber) &&
               kyc.IdentityIssuedDate > DateTime.MinValue &&
               !string.IsNullOrWhiteSpace(kyc.IdentityIssuedPlace) &&
               !string.IsNullOrWhiteSpace(kyc.CitizenIdFrontUrl) &&
               !string.IsNullOrWhiteSpace(kyc.CitizenIdBackUrl);
    }

    private int? TryGetCurrentUserId()
    {
        var principal = User;
        if (principal?.Identity is null)
        {
            return null;
        }

        var raw = principal.FindFirstValue(ClaimTypes.NameIdentifier)
            ?? principal.FindFirstValue("sub");
        return int.TryParse(raw, out var userId) ? userId : null;
    }

    private async Task<(int? UserId, string? UserName, string? FullName)> GetReviewerInfoAsync(CancellationToken cancellationToken)
    {
        var reviewerUserId = TryGetCurrentUserId();
        if (!reviewerUserId.HasValue)
        {
            return (null, null, null);
        }

        var reviewer = await _db.Users
            .AsNoTracking()
            .Where(x => x.UserId == reviewerUserId.Value)
            .Select(x => new { x.UserId, x.UserName, x.FullName })
            .FirstOrDefaultAsync(cancellationToken);

        return reviewer is null
            ? (reviewerUserId, null, null)
            : (reviewer.UserId, reviewer.UserName, reviewer.FullName);
    }

    private async Task TrySendSellerReviewEmailAsync(User user, string? storeName, bool isApproved, string? reviewNote, CancellationToken cancellationToken)
    {
        if (!_accountEmailSender.IsConfigured)
        {
            _logger.LogInformation(
                "Bo qua gui email ket qua review seller cho user {UserId} vi SMTP chua duoc cau hinh.",
                user.UserId);
            return;
        }

        if (string.IsNullOrWhiteSpace(user.Email))
        {
            _logger.LogWarning(
                "Bo qua gui email ket qua review seller cho user {UserId} vi user khong co email.",
                user.UserId);
            return;
        }

        try
        {
            await _accountEmailSender.SendSellerApplicationReviewAsync(
                user.Email,
                user.FullName,
                storeName,
                isApproved,
                reviewNote,
                cancellationToken);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(
                ex,
                "Gui email ket qua review seller that bai cho user {UserId}. Van tiep tuc vi review da duoc luu.",
                user.UserId);
        }
    }

    private async Task TryPublishSellerReviewNotificationAsync(User user, string? storeName, bool isApproved, string? reviewNote, CancellationToken cancellationToken)
    {
        if (!_customerNotificationPublisher.IsConfigured)
        {
            _logger.LogInformation(
                "Bo qua tao customer notification cho ket qua review seller cua user {UserId} vi Ordering notification client chua duoc cau hinh.",
                user.UserId);
            return;
        }

        try
        {
            await _customerNotificationPublisher.PublishSellerReviewAsync(
                user,
                storeName,
                isApproved,
                reviewNote,
                cancellationToken);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(
                ex,
                "Tao customer notification cho ket qua review seller that bai voi user {UserId}. Van tiep tuc vi review da duoc luu.",
                user.UserId);
        }
    }

    private static string NormalizeReviewStatus(string? reviewStatus, bool isSellerApproved)
    {
        if (isSellerApproved)
        {
            return "approved";
        }

        var normalized = reviewStatus?.Trim().ToLowerInvariant();
        return normalized is "approved" or "rejected" or "pending"
            ? normalized
            : "pending";
    }

    private static string NormalizeRejectReason(string reason)
    {
        var normalized = Regex.Replace(reason.Trim(), "\\s+", " ");
        if (string.IsNullOrWhiteSpace(normalized))
        {
            return string.Empty;
        }

        var replacements = new (string Source, string Target)[]
        {
            ("vui long", "vui lòng"),
            ("bo sung", "bổ sung"),
            ("anh cccd", "ảnh CCCD"),
            ("mat truoc", "mặt trước"),
            ("mat sau", "mặt sau"),
            ("ro net", "rõ nét"),
            ("anh dai dien", "ảnh đại diện"),
            ("cua hang", "cửa hàng"),
            ("dia chi", "địa chỉ"),
            ("thong tin", "thông tin"),
            ("so dien thoai", "số điện thoại"),
            ("giay phep kinh doanh", "giấy phép kinh doanh"),
            ("yeu cau", "yêu cầu")
        };

        foreach (var (source, target) in replacements)
        {
            normalized = Regex.Replace(
                normalized,
                $@"(?<!\p{{L}}){Regex.Escape(source)}(?!\p{{L}})",
                target,
                RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
        }

        return normalized;
    }

    private static string BuildReviewStatusLabel(string? reviewStatus, bool isSellerApproved)
    {
        return NormalizeReviewStatus(reviewStatus, isSellerApproved) switch
        {
            "approved" => "Đã duyệt",
            "rejected" => "Đã từ chối",
            _ => "Đang chờ duyệt"
        };
    }

    private static string? MaskIdentityNumber(string? identityNumber)
    {
        var raw = identityNumber?.Trim();
        if (string.IsNullOrWhiteSpace(raw))
        {
            return null;
        }

        if (raw.Length <= 4)
        {
            return raw;
        }

        return new string('*', raw.Length - 4) + raw[^4..];
    }
}
