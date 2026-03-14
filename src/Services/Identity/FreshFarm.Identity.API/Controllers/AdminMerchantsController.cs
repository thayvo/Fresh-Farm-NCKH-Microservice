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
        [FromQuery] string? queue = null,
        [FromQuery] int page = 1,
        [FromQuery] int pageSize = DefaultPageSize,
        CancellationToken cancellationToken = default)
    {
        page = page < 1 ? 1 : page;
        pageSize = pageSize <= 0 ? DefaultPageSize : Math.Min(pageSize, MaxPageSize);
        var normalizedStatus = NormalizeStatus(status);
        var normalizedQueue = NormalizeQueue(queue);

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
            .Where(x => MatchesQueue(x, normalizedQueue))
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
                    new MerchantOptionDto("all", "Tất cả queue"),
                    new MerchantOptionDto("approval", "Chờ duyệt"),
                    new MerchantOptionDto("profile_fix", "Bổ sung hồ sơ"),
                    new MerchantOptionDto("dormant", "Ngủ đông"),
                    new MerchantOptionDto("suspended", "Đang khóa"),
                    new MerchantOptionDto("review", "Rà soát tay")
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
            ShopName = string.IsNullOrWhiteSpace(row.FullName) ? (row.UserName ?? $"Seller #{row.SellerId}") : row.FullName,
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
            DaysSinceLastLogin = daysSinceLastLogin,
            QueueBucket = DetermineQueueBucket(row, flags, profileScore),
            RecommendedAction = BuildRecommendedAction(row, flags, profileScore),
            IssueCount = flags.Count,
            PriorityScore = CalculatePriorityScore(row, flags, profileScore)
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
        if (!row.IsActive)
        {
            return "suspended";
        }

        if (flags.Any(f => f.Code is "missing-phone" or "missing-email" or "missing-address"))
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
        if (!row.IsActive)
        {
            return "Rà soát lý do khóa và chỉ mở lại khi seller xác nhận tiếp tục vận hành.";
        }

        if (flags.Any(f => f.Code is "missing-phone" or "missing-email" or "missing-address"))
        {
            return "Yêu cầu seller bổ sung hồ sơ liên hệ và địa chỉ hoạt động trước khi đẩy quyền tăng trưởng.";
        }

        if (flags.Any(f => f.Code == "stale-login"))
        {
            return "Liên hệ seller để xác nhận shop còn hoạt động trước khi duyệt campaign hoặc traffic.";
        }

        if (profileScore >= 80)
        {
            return "Có thể đưa vào queue chờ duyệt/whitelist cho campaign nội bộ ở mức MVP.";
        }

        return "Rà soát thủ công hồ sơ seller trước khi mở rộng quyền hoặc campaign.";
    }

    private static int CalculatePriorityScore(MerchantProjection row, IReadOnlyCollection<MerchantFlagDto> flags, int profileScore)
    {
        if (!row.IsActive)
        {
            return 100;
        }

        var score = 0;
        if (flags.Any(f => f.Code == "missing-address"))
        {
            score += 35;
        }

        if (flags.Any(f => f.Code is "missing-phone" or "missing-email"))
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

    private static List<string> BuildNextSteps(MerchantListItemDto card)
    {
        var steps = new List<string>();

        if (!card.IsActive)
        {
            steps.Add("Xác minh lý do tạm khóa với đội vận hành hoặc CS.");
            steps.Add("Chỉ mở lại seller khi hồ sơ và trạng thái vận hành đã rõ.");
        }

        if (card.Flags.Any(f => f.Code is "missing-phone" or "missing-email"))
        {
            steps.Add("Yêu cầu bổ sung thông tin liên hệ chính.");
        }

        if (card.Flags.Any(f => f.Code == "missing-address"))
        {
            steps.Add("Yêu cầu cập nhật địa chỉ hoạt động hoặc kho xử lý đơn.");
        }

        if (card.Flags.Any(f => f.Code == "stale-login"))
        {
            steps.Add("Kiểm tra seller còn đăng nhập và xử lý đơn trong 30 ngày gần đây hay không.");
        }

        if (steps.Count == 0 && string.Equals(card.QueueBucket, "approval", StringComparison.OrdinalIgnoreCase))
        {
            steps.Add("Có thể đưa seller vào queue ưu tiên cho campaign hoặc onboarding nâng cao.");
        }

        if (steps.Count == 0)
        {
            steps.Add("Rà soát thủ công thêm để xác nhận seller sẵn sàng vận hành.");
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
        public int ApprovalQueue { get; set; }
        public int ProfileFixQueue { get; set; }
        public int DormantQueue { get; set; }
    }

    public sealed class MerchantFiltersDto
    {
        public string Search { get; set; } = string.Empty;
        public string Status { get; set; } = "all";
        public string Queue { get; set; } = "all";
        public List<MerchantOptionDto> StatusOptions { get; set; } = new();
        public List<MerchantOptionDto> QueueOptions { get; set; } = new();
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
    }

    public sealed record MerchantFlagDto(string Code, string Label, string Tone);
}
