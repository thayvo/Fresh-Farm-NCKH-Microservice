using FreshFarm.Identity.Api.Models;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace FreshFarm.Identity.Api.Controllers;

[ApiController]
[Route("auth/public/merchants")]
public sealed class PublicMerchantsController : ControllerBase
{
    private const int DefaultPageSize = 24;
    private const int MaxPageSize = 60;
    private readonly FreshFarmIdentityDBContext _db;

    public PublicMerchantsController(FreshFarmIdentityDBContext db)
    {
        _db = db;
    }

    [HttpGet]
    public async Task<IActionResult> Get(
        [FromQuery] List<int>? sellerIds = null,
        [FromQuery] string? q = null,
        [FromQuery] int page = 1,
        [FromQuery] int pageSize = DefaultPageSize,
        CancellationToken cancellationToken = default)
    {
        page = page < 1 ? 1 : page;
        pageSize = pageSize <= 0 ? DefaultPageSize : Math.Min(pageSize, MaxPageSize);

        var normalizedIds = (sellerIds ?? new List<int>())
            .Where(id => id > 0)
            .Distinct()
            .ToList();

        var sellerRoleId = await _db.Roles
            .AsNoTracking()
            .Where(r => r.RoleName == "Seller")
            .Select(r => (int?)r.RoleId)
            .FirstOrDefaultAsync(cancellationToken);

        if (!sellerRoleId.HasValue)
        {
            return Ok(new PublicMerchantListResponse
            {
                Page = page,
                PageSize = pageSize,
                Total = 0,
                TotalPages = 1
            });
        }

        var query = _db.Users
            .AsNoTracking()
            .Where(u => u.IsActive && u.UserRoles.Any(ur => ur.RoleId == sellerRoleId.Value));

        if (normalizedIds.Count > 0)
        {
            query = query.Where(u => normalizedIds.Contains(u.UserId));
        }

        if (!string.IsNullOrWhiteSpace(q))
        {
            var term = q.Trim().ToLowerInvariant();
            query = query.Where(u =>
                (u.SellerStoreSetting != null && u.SellerStoreSetting.StoreName.ToLower().Contains(term)) ||
                u.UserName.ToLower().Contains(term) ||
                u.FullName.ToLower().Contains(term) ||
                (u.AddressBook != null && u.AddressBook.IsActive &&
                 (
                    (u.AddressBook.Province != null && u.AddressBook.Province.ToLower().Contains(term)) ||
                    (u.AddressBook.District != null && u.AddressBook.District.ToLower().Contains(term)) ||
                    (u.AddressBook.Ward != null && u.AddressBook.Ward.ToLower().Contains(term))
                 )));
        }

        var merchants = await query
            .Select(u => new PublicMerchantProjection
            {
                SellerId = u.UserId,
                UserName = u.UserName,
                FullName = u.FullName,
                Avatar = u.Avatar,
                CreatedAt = u.CreatedAt,
                StoreName = u.SellerStoreSetting != null ? u.SellerStoreSetting.StoreName : null,
                AddressDetail = u.AddressBook != null && u.AddressBook.IsActive ? u.AddressBook.AddressDetail : null,
                Province = u.AddressBook != null && u.AddressBook.IsActive ? u.AddressBook.Province : null,
                District = u.AddressBook != null && u.AddressBook.IsActive ? u.AddressBook.District : null,
                Ward = u.AddressBook != null && u.AddressBook.IsActive ? u.AddressBook.Ward : null
            })
            .ToListAsync(cancellationToken);

        var mapped = merchants
            .Select(MapMerchant)
            .OrderByDescending(x => x.JoinedAt)
            .ToList();

        if (normalizedIds.Count > 0)
        {
            mapped = mapped
                .OrderBy(x => normalizedIds.IndexOf(x.SellerId))
                .ToList();
        }

        var total = mapped.Count;
        var totalPages = total == 0 ? 1 : (int)Math.Ceiling(total / (double)pageSize);
        if (page > totalPages)
        {
            page = totalPages;
        }

        var items = mapped
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .ToList();

        return Ok(new PublicMerchantListResponse
        {
            Page = page,
            PageSize = pageSize,
            Total = total,
            TotalPages = totalPages,
            Query = q?.Trim() ?? string.Empty,
            Merchants = items
        });
    }

    [HttpGet("{sellerId:int}")]
    public async Task<IActionResult> GetById([FromRoute] int sellerId, CancellationToken cancellationToken = default)
    {
        if (sellerId <= 0)
        {
            return BadRequest(new { message = "Seller không hợp lệ." });
        }

        var sellerRoleId = await _db.Roles
            .AsNoTracking()
            .Where(r => r.RoleName == "Seller")
            .Select(r => (int?)r.RoleId)
            .FirstOrDefaultAsync(cancellationToken);

        if (!sellerRoleId.HasValue)
        {
            return NotFound(new { message = "Không tìm thấy seller." });
        }

        var merchant = await _db.Users
            .AsNoTracking()
            .Where(u => u.UserId == sellerId && u.IsActive && u.UserRoles.Any(ur => ur.RoleId == sellerRoleId.Value))
            .Select(u => new PublicMerchantProjection
            {
                SellerId = u.UserId,
                UserName = u.UserName,
                FullName = u.FullName,
                Avatar = u.Avatar,
                CreatedAt = u.CreatedAt,
                StoreName = u.SellerStoreSetting != null ? u.SellerStoreSetting.StoreName : null,
                AddressDetail = u.AddressBook != null && u.AddressBook.IsActive ? u.AddressBook.AddressDetail : null,
                Province = u.AddressBook != null && u.AddressBook.IsActive ? u.AddressBook.Province : null,
                District = u.AddressBook != null && u.AddressBook.IsActive ? u.AddressBook.District : null,
                Ward = u.AddressBook != null && u.AddressBook.IsActive ? u.AddressBook.Ward : null
            })
            .FirstOrDefaultAsync(cancellationToken);

        if (merchant is null)
        {
            return NotFound(new { message = "Không tìm thấy shop." });
        }

        return Ok(MapMerchant(merchant));
    }

    [HttpGet("shipping-origins")]
    public async Task<IActionResult> GetShippingOrigins(
        [FromQuery] List<int>? sellerIds = null,
        CancellationToken cancellationToken = default)
    {
        var normalizedIds = (sellerIds ?? new List<int>())
            .Where(id => id > 0)
            .Distinct()
            .ToList();

        if (normalizedIds.Count == 0)
        {
            return Ok(new PublicMerchantShippingOriginListResponse());
        }

        var sellerRoleId = await _db.Roles
            .AsNoTracking()
            .Where(r => r.RoleName == "Seller")
            .Select(r => (int?)r.RoleId)
            .FirstOrDefaultAsync(cancellationToken);

        if (!sellerRoleId.HasValue)
        {
            return Ok(new PublicMerchantShippingOriginListResponse());
        }

        var rows = await _db.Users
            .AsNoTracking()
            .Where(u => normalizedIds.Contains(u.UserId) && u.IsActive && u.UserRoles.Any(ur => ur.RoleId == sellerRoleId.Value))
            .Select(u => new PublicMerchantShippingOriginDto
            {
                SellerId = u.UserId,
                ShopName = u.SellerStoreSetting != null && !string.IsNullOrWhiteSpace(u.SellerStoreSetting.StoreName)
                    ? u.SellerStoreSetting.StoreName
                    : (string.IsNullOrWhiteSpace(u.FullName) ? (u.UserName ?? $"FreshFarm Seller {u.UserId}") : u.FullName),
                HasShippingOrigin = u.SellerStoreSetting != null
                    && u.SellerStoreSetting.GhnDistrictId.HasValue
                    && u.SellerStoreSetting.GhnDistrictId.Value > 0
                    && u.SellerStoreSetting.GhnWardCode != null
                    && u.SellerStoreSetting.GhnWardCode != string.Empty,
                GhnDistrictId = u.SellerStoreSetting != null ? u.SellerStoreSetting.GhnDistrictId : null,
                GhnWardCode = u.SellerStoreSetting != null ? u.SellerStoreSetting.GhnWardCode : null,
                GhnDistrictName = u.SellerStoreSetting != null ? u.SellerStoreSetting.GhnDistrictName : null,
                GhnWardName = u.SellerStoreSetting != null ? u.SellerStoreSetting.GhnWardName : null,
                PickupAddressSummary = u.SellerStoreSetting != null ? BuildPickupSummary(u.SellerStoreSetting) : string.Empty
            })
            .ToListAsync(cancellationToken);

        var items = normalizedIds
            .Select(id => rows.FirstOrDefault(x => x.SellerId == id) ?? new PublicMerchantShippingOriginDto
            {
                SellerId = id,
                ShopName = $"FreshFarm Seller {id}",
                HasShippingOrigin = false
            })
            .ToList();

        return Ok(new PublicMerchantShippingOriginListResponse
        {
            Origins = items
        });
    }

    private static PublicMerchantDto MapMerchant(PublicMerchantProjection row)
    {
        return new PublicMerchantDto
        {
            SellerId = row.SellerId,
            ShopName = !string.IsNullOrWhiteSpace(row.StoreName)
                ? row.StoreName.Trim()
                : (string.IsNullOrWhiteSpace(row.FullName) ? (row.UserName ?? $"FreshFarm Seller {row.SellerId}") : row.FullName),
            UserName = row.UserName ?? string.Empty,
            Avatar = row.Avatar,
            AddressSummary = BuildAddressSummary(row),
            JoinedAt = row.CreatedAt
        };
    }

    private static string BuildAddressSummary(PublicMerchantProjection row)
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
        .Distinct(StringComparer.OrdinalIgnoreCase)
        .ToList();

        return parts.Count == 0 ? "Chưa cập nhật địa chỉ hoạt động" : string.Join(", ", parts);
    }

    private static string BuildPickupSummary(SellerStoreSetting settings)
    {
        var parts = new[]
        {
            settings.GhnPickupAddress,
            settings.GhnWardName,
            settings.GhnDistrictName
        }
        .Where(x => !string.IsNullOrWhiteSpace(x))
        .Select(x => x!.Trim())
        .Distinct(StringComparer.OrdinalIgnoreCase)
        .ToList();

        return parts.Count == 0 ? "Chưa cấu hình địa chỉ lấy hàng" : string.Join(", ", parts);
    }

    private sealed class PublicMerchantProjection
    {
        public int SellerId { get; set; }
        public string? UserName { get; set; }
        public string? FullName { get; set; }
        public string? StoreName { get; set; }
        public string? Avatar { get; set; }
        public DateTime CreatedAt { get; set; }
        public string? AddressDetail { get; set; }
        public string? Province { get; set; }
        public string? District { get; set; }
        public string? Ward { get; set; }
    }

    private sealed class PublicMerchantDto
    {
        public int SellerId { get; set; }
        public string ShopName { get; set; } = string.Empty;
        public string UserName { get; set; } = string.Empty;
        public string? Avatar { get; set; }
        public string AddressSummary { get; set; } = string.Empty;
        public DateTime JoinedAt { get; set; }
    }

    private sealed class PublicMerchantListResponse
    {
        public int Page { get; set; }
        public int PageSize { get; set; }
        public int Total { get; set; }
        public int TotalPages { get; set; }
        public string Query { get; set; } = string.Empty;
        public List<PublicMerchantDto> Merchants { get; set; } = new();
    }

    private sealed class PublicMerchantShippingOriginDto
    {
        public int SellerId { get; set; }
        public string ShopName { get; set; } = string.Empty;
        public bool HasShippingOrigin { get; set; }
        public int? GhnDistrictId { get; set; }
        public string? GhnWardCode { get; set; }
        public string? GhnDistrictName { get; set; }
        public string? GhnWardName { get; set; }
        public string PickupAddressSummary { get; set; } = string.Empty;
    }

    private sealed class PublicMerchantShippingOriginListResponse
    {
        public List<PublicMerchantShippingOriginDto> Origins { get; set; } = new();
    }
}
