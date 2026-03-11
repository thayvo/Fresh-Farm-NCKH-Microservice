using FreshFarm.Identity.Api.Models;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using System.ComponentModel.DataAnnotations;

namespace FreshFarm.Identity.Api.Controllers;

[ApiController]
[Route("auth/admin/merchants")]
[Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme, Policy = "AdminOnly")]
public sealed class AdminMerchantsController : ControllerBase
{
    private const int DefaultPageSize = 20;
    private const int MaxPageSize = 100;

    private readonly FreshFarmIdentityDBContext _db;

    public AdminMerchantsController(FreshFarmIdentityDBContext db)
    {
        _db = db;
    }

    [HttpGet]
    public async Task<IActionResult> Get(
        [FromQuery] string? search = null,
        [FromQuery] string? status = null,
        [FromQuery] int page = 1,
        [FromQuery] int pageSize = DefaultPageSize,
        CancellationToken cancellationToken = default)
    {
        page = page < 1 ? 1 : page;
        pageSize = pageSize <= 0 ? DefaultPageSize : Math.Min(pageSize, MaxPageSize);
        var normalizedStatus = NormalizeStatus(status);

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

        var query = _db.Users
            .AsNoTracking()
            .Where(u => u.UserRoles.Any(ur => ur.RoleId == sellerRoleId.Value));

        if (!string.IsNullOrWhiteSpace(search))
        {
            var term = search.Trim().ToLowerInvariant();
            query = query.Where(u =>
                u.UserName.ToLower().Contains(term) ||
                u.FullName.ToLower().Contains(term) ||
                u.Email.ToLower().Contains(term) ||
                (u.Phone != null && u.Phone.Contains(term)));
        }

        var sellers = await query
            .OrderByDescending(u => u.CreatedAt)
            .Select(u => new MerchantProjection
            {
                SellerId = u.UserId,
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
                AddressDetail = u.AddressBook != null && u.AddressBook.IsActive ? u.AddressBook.AddressDetail : null,
                Province = u.AddressBook != null && u.AddressBook.IsActive ? u.AddressBook.Province : null,
                District = u.AddressBook != null && u.AddressBook.IsActive ? u.AddressBook.District : null,
                Ward = u.AddressBook != null && u.AddressBook.IsActive ? u.AddressBook.Ward : null
            })
            .ToListAsync(cancellationToken);

        var items = sellers
            .Select(MapMerchantCard)
            .Where(x => MatchesStatus(x, normalizedStatus))
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
                MissingAddress = items.Count(x => x.Flags.Any(f => string.Equals(f.Code, "missing-address", StringComparison.OrdinalIgnoreCase)))
            },
            Filters = new MerchantFiltersDto
            {
                Search = search?.Trim() ?? string.Empty,
                Status = normalizedStatus,
                StatusOptions =
                [
                    new MerchantOptionDto("all", "Tất cả"),
                    new MerchantOptionDto("ready", "Sẵn sàng"),
                    new MerchantOptionDto("review", "Cần rà soát"),
                    new MerchantOptionDto("suspended", "Đang tạm khóa"),
                    new MerchantOptionDto("stale", "Lâu không hoạt động")
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
            return BadRequest("Seller không hợp lệ.");
        }

        var sellerRoleId = await _db.Roles
            .AsNoTracking()
            .Where(r => r.RoleName == "Seller")
            .Select(r => (int?)r.RoleId)
            .FirstOrDefaultAsync(cancellationToken);

        if (!sellerRoleId.HasValue)
        {
            return NotFound("Không tìm thấy role Seller.");
        }

        var seller = await _db.Users
            .AsNoTracking()
            .Where(u => u.UserId == sellerId && u.UserRoles.Any(ur => ur.RoleId == sellerRoleId.Value))
            .Select(u => new MerchantProjection
            {
                SellerId = u.UserId,
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
                AddressDetail = u.AddressBook != null && u.AddressBook.IsActive ? u.AddressBook.AddressDetail : null,
                Province = u.AddressBook != null && u.AddressBook.IsActive ? u.AddressBook.Province : null,
                District = u.AddressBook != null && u.AddressBook.IsActive ? u.AddressBook.District : null,
                Ward = u.AddressBook != null && u.AddressBook.IsActive ? u.AddressBook.Ward : null
            })
            .FirstOrDefaultAsync(cancellationToken);

        if (seller is null)
        {
            return NotFound("Không tìm thấy seller.");
        }

        return Ok(MapMerchantDetail(seller));
    }

    [HttpPatch("{sellerId:int}/status")]
    public async Task<IActionResult> UpdateStatus(int sellerId, [FromBody] UpdateMerchantStatusRequest request, CancellationToken cancellationToken = default)
    {
        if (sellerId <= 0)
        {
            return BadRequest("Seller không hợp lệ.");
        }

        var sellerRoleId = await _db.Roles
            .AsNoTracking()
            .Where(r => r.RoleName == "Seller")
            .Select(r => (int?)r.RoleId)
            .FirstOrDefaultAsync(cancellationToken);

        if (!sellerRoleId.HasValue)
        {
            return NotFound("Không tìm thấy role Seller.");
        }

        var user = await _db.Users
            .FirstOrDefaultAsync(u => u.UserId == sellerId && u.UserRoles.Any(ur => ur.RoleId == sellerRoleId.Value), cancellationToken);

        if (user is null)
        {
            return NotFound("Không tìm thấy seller.");
        }

        user.IsActive = request.IsActive;
        user.UpdatedAt = DateTime.UtcNow;
        await _db.SaveChangesAsync(cancellationToken);

        return Ok(new
        {
            sellerId = user.UserId,
            isActive = user.IsActive,
            message = user.IsActive ? "Đã mở lại seller." : "Đã tạm khóa seller."
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
            ShopName = string.IsNullOrWhiteSpace(row.FullName) ? row.UserName : row.FullName,
            UserName = row.UserName,
            FullName = row.FullName,
            Email = row.Email,
            Phone = row.Phone,
            Avatar = row.Avatar,
            IsActive = row.IsActive,
            CreatedAt = row.CreatedAt,
            UpdatedAt = row.UpdatedAt,
            LastLogin = lastLogin,
            AddressSummary = BuildAddressSummary(row),
            ProfileScore = profileScore,
            ComplianceStatus = complianceStatus,
            Flags = flags,
            DaysSinceLastLogin = daysSinceLastLogin
        };
    }

    private static MerchantDetailDto MapMerchantDetail(MerchantProjection row)
    {
        var card = MapMerchantCard(row);
        return new MerchantDetailDto
        {
            SellerId = card.SellerId,
            ShopName = card.ShopName,
            UserName = card.UserName,
            FullName = card.FullName,
            Email = card.Email,
            Phone = card.Phone,
            Avatar = card.Avatar,
            IsActive = card.IsActive,
            CreatedAt = card.CreatedAt,
            UpdatedAt = card.UpdatedAt,
            LastLogin = card.LastLogin,
            AddressSummary = card.AddressSummary,
            AddressDetail = row.AddressDetail,
            Province = row.Province,
            District = row.District,
            Ward = row.Ward,
            ProfileScore = card.ProfileScore,
            ComplianceStatus = card.ComplianceStatus,
            Flags = card.Flags,
            DaysSinceLastLogin = card.DaysSinceLastLogin,
            ComplianceSummary = BuildComplianceSummary(card)
        };
    }

    private static List<MerchantFlagDto> BuildFlags(MerchantProjection row)
    {
        var flags = new List<MerchantFlagDto>();

        if (string.IsNullOrWhiteSpace(row.Phone))
        {
            flags.Add(new MerchantFlagDto("missing-phone", "Thiếu số điện thoại", "warning"));
        }

        if (string.IsNullOrWhiteSpace(row.Email))
        {
            flags.Add(new MerchantFlagDto("missing-email", "Thiếu email", "warning"));
        }

        if (string.IsNullOrWhiteSpace(row.AddressDetail) &&
            string.IsNullOrWhiteSpace(row.Province) &&
            string.IsNullOrWhiteSpace(row.District) &&
            string.IsNullOrWhiteSpace(row.Ward))
        {
            flags.Add(new MerchantFlagDto("missing-address", "Thiếu địa chỉ hoạt động", "danger"));
        }

        if (string.IsNullOrWhiteSpace(row.Avatar))
        {
            flags.Add(new MerchantFlagDto("missing-avatar", "Thiếu ảnh đại diện", "neutral"));
        }

        if (!row.LastLogin.HasValue || row.LastLogin.Value < DateTime.UtcNow.AddDays(-30))
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
            points += 15;
        }

        if (!string.IsNullOrWhiteSpace(row.FullName))
        {
            points += 20;
        }

        if (!string.IsNullOrWhiteSpace(row.Email))
        {
            points += 20;
        }

        if (!string.IsNullOrWhiteSpace(row.Phone))
        {
            points += 15;
        }

        if (!string.IsNullOrWhiteSpace(row.Avatar))
        {
            points += 10;
        }

        if (!string.IsNullOrWhiteSpace(row.AddressDetail) ||
            !string.IsNullOrWhiteSpace(row.Province) ||
            !string.IsNullOrWhiteSpace(row.District) ||
            !string.IsNullOrWhiteSpace(row.Ward))
        {
            points += 20;
        }

        return points;
    }

    private static string DetermineComplianceStatus(MerchantProjection row, IReadOnlyCollection<MerchantFlagDto> flags, int profileScore)
    {
        if (!row.IsActive)
        {
            return "suspended";
        }

        if (profileScore < 70 || flags.Any(f => f.Code is "missing-phone" or "missing-address" or "stale-login"))
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

    private static string BuildAddressSummary(MerchantProjection row)
    {
        var parts = new[]
        {
            row.AddressDetail,
            row.Ward,
            row.District,
            row.Province
        }
        .Where(x => !string.IsNullOrWhiteSpace(x))
        .Select(x => x!.Trim())
        .ToArray();

        return parts.Length == 0 ? "Chưa có địa chỉ hoạt động" : string.Join(", ", parts);
    }

    private static string BuildComplianceSummary(MerchantListItemDto card)
    {
        if (!card.IsActive)
        {
            return "Seller đang bị tạm khóa. Cần rà soát trước khi mở lại.";
        }

        if (string.Equals(card.ComplianceStatus, "ready", StringComparison.OrdinalIgnoreCase))
        {
            return "Hồ sơ seller đã đủ thông tin nền tảng để vận hành ở mức MVP.";
        }

        if (card.Flags.Count == 0)
        {
            return "Seller cần rà soát thủ công trước khi đưa vào các chương trình tăng trưởng.";
        }

        return "Seller cần bổ sung hoặc xác nhận lại các mục: " + string.Join("; ", card.Flags.Select(x => x.Label)) + ".";
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

    private sealed class MerchantProjection
    {
        public int SellerId { get; set; }
        public string? UserName { get; set; }
        public string? FullName { get; set; }
        public string? Email { get; set; }
        public string? Phone { get; set; }
        public string? Avatar { get; set; }
        public bool IsActive { get; set; }
        public DateTime CreatedAt { get; set; }
        public DateTime? UpdatedAt { get; set; }
        public DateTime? LastLogin { get; set; }
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
    }

    public sealed class MerchantFiltersDto
    {
        public string Search { get; set; } = string.Empty;
        public string Status { get; set; } = "all";
        public List<MerchantOptionDto> StatusOptions { get; set; } = new();
    }

    public sealed record MerchantOptionDto(string Value, string Text);

    public class MerchantListItemDto
    {
        public int SellerId { get; set; }
        public string ShopName { get; set; } = string.Empty;
        public string? UserName { get; set; }
        public string? FullName { get; set; }
        public string? Email { get; set; }
        public string? Phone { get; set; }
        public string? Avatar { get; set; }
        public bool IsActive { get; set; }
        public DateTime CreatedAt { get; set; }
        public DateTime? UpdatedAt { get; set; }
        public DateTime? LastLogin { get; set; }
        public string AddressSummary { get; set; } = string.Empty;
        public int ProfileScore { get; set; }
        public string ComplianceStatus { get; set; } = string.Empty;
        public List<MerchantFlagDto> Flags { get; set; } = new();
        public int? DaysSinceLastLogin { get; set; }
    }

    public sealed class MerchantDetailDto : MerchantListItemDto
    {
        public string? AddressDetail { get; set; }
        public string? Province { get; set; }
        public string? District { get; set; }
        public string? Ward { get; set; }
        public string ComplianceSummary { get; set; } = string.Empty;
    }

    public sealed record MerchantFlagDto(string Code, string Label, string Tone);
}
