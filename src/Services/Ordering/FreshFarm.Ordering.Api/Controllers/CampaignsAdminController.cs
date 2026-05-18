using System.IdentityModel.Tokens.Jwt;
using FreshFarm.Ordering.Api.Models;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using System.Security.Claims;

namespace FreshFarm.Ordering.Api.Controllers;

[ApiController]
[Route("api/orders/admin/campaigns")]
[Authorize(Policy = "AdminOnly")]
public sealed class CampaignsAdminController : ControllerBase
{
    private static readonly string[] AllowedStatuses = { "draft", "registration_open", "registration_closed", "scheduled", "running", "completed", "suspended" };
    private static readonly string[] AllowedTypes = { "flash_sale", "voucher_boost", "seasonal", "livestream" };
    private static readonly string[] AllowedParticipationStatuses = { "pending", "approved", "rejected", "waitlist", "withdrawn" };
    private static readonly string[] AllowedWalletStatuses = { "active", "paused", "suspended" };
    private static readonly string[] AllowedAdsCampaignStatuses = { "draft", "scheduled", "running", "paused", "completed" };

    private readonly FreshFarmOrderingDBContext _db;

    public CampaignsAdminController(FreshFarmOrderingDBContext db)
    {
        _db = db;
    }

    [HttpGet("overview")]
    public async Task<IActionResult> GetOverview(CancellationToken cancellationToken)
    {
        var now = DateTime.UtcNow;
        var campaigns = _db.Campaigns.AsNoTracking();
        var participations = _db.CampaignSellerParticipations.AsNoTracking();
        var slots = _db.CampaignProductSlots.AsNoTracking();

        var totalCampaigns = await campaigns.CountAsync(cancellationToken);
        var runningCampaigns = await campaigns.CountAsync(x => x.Status == "running", cancellationToken);
        var registrationOpenCampaigns = await campaigns.CountAsync(x => x.Status == "registration_open", cancellationToken);
        var scheduledCampaigns = await campaigns.CountAsync(x => x.Status == "scheduled", cancellationToken);
        var totalParticipations = await participations.CountAsync(cancellationToken);
        var approvedParticipations = await participations.CountAsync(x => x.Status == "approved", cancellationToken);
        var pendingParticipations = await participations.CountAsync(x => x.Status == "pending", cancellationToken);
        var totalSlots = await slots.CountAsync(cancellationToken);
        var approvedSlots = await slots.CountAsync(x => x.Status == "approved", cancellationToken);
        var liveSlots = await slots.CountAsync(x => x.Status == "live", cancellationToken);
        var totalBudget = await campaigns.SumAsync(x => (decimal?)x.BudgetAmount, cancellationToken) ?? 0m;

        var upcoming = await campaigns
            .Where(x => x.EndAt >= now)
            .OrderBy(x => x.StartAt)
            .Select(x => new
            {
                campaignId = x.CampaignId,
                name = x.Name,
                campaignType = x.CampaignType,
                status = x.Status,
                startAt = x.StartAt,
                endAt = x.EndAt,
                registrationEndAt = x.RegistrationEndAt
            })
            .Take(4)
            .ToListAsync(cancellationToken);

        return Ok(new
        {
            totalCampaigns,
            runningCampaigns,
            registrationOpenCampaigns,
            scheduledCampaigns,
            totalParticipations,
            approvedParticipations,
            pendingParticipations,
            totalSlots,
            approvedSlots,
            liveSlots,
            totalBudget,
            upcoming
        });
    }

    [HttpGet]
    public async Task<IActionResult> GetCampaigns([FromQuery] string? q = null, [FromQuery] string? status = null, [FromQuery] string? type = null, CancellationToken cancellationToken = default)
    {
        var normalizedStatus = NormalizeOptional(status);
        var normalizedType = NormalizeOptional(type);
        var normalizedQuery = q?.Trim();

        var query = _db.Campaigns
            .AsNoTracking()
            .Include(x => x.VoucherCoupon)
            .Include(x => x.CampaignSellerParticipations)
            .Include(x => x.CampaignProductSlots)
            .AsQueryable();

        if (!string.IsNullOrWhiteSpace(normalizedQuery))
        {
            query = query.Where(x => x.Name.Contains(normalizedQuery) ||
                                     (x.Description != null && x.Description.Contains(normalizedQuery)));
        }

        if (!string.IsNullOrWhiteSpace(normalizedStatus) && normalizedStatus != "all")
        {
            query = query.Where(x => x.Status == normalizedStatus);
        }

        if (!string.IsNullOrWhiteSpace(normalizedType) && normalizedType != "all")
        {
            query = query.Where(x => x.CampaignType == normalizedType);
        }

        var rows = await query
            .OrderByDescending(x => x.StartAt)
            .ThenByDescending(x => x.CampaignId)
            .Select(x => new
            {
                campaignId = x.CampaignId,
                name = x.Name,
                campaignType = x.CampaignType,
                description = x.Description,
                status = x.Status,
                budgetAmount = x.BudgetAmount,
                isFeatured = x.IsFeatured,
                voucherCouponId = x.VoucherCouponId,
                voucherCode = x.VoucherCoupon != null ? x.VoucherCoupon.Code : null,
                registrationStartAt = x.RegistrationStartAt,
                registrationEndAt = x.RegistrationEndAt,
                startAt = x.StartAt,
                endAt = x.EndAt,
                createdAt = x.CreatedAt,
                updatedAt = x.UpdatedAt,
                participationCount = x.CampaignSellerParticipations.Count,
                approvedParticipationCount = x.CampaignSellerParticipations.Count(p => p.Status == "approved"),
                productSlotCount = x.CampaignProductSlots.Count,
                approvedSlotCount = x.CampaignProductSlots.Count(s => s.Status == "approved" || s.Status == "live")
            })
            .ToListAsync(cancellationToken);

        return Ok(new
        {
            filters = new
            {
                q = normalizedQuery ?? string.Empty,
                status = string.IsNullOrWhiteSpace(normalizedStatus) ? "all" : normalizedStatus,
                type = string.IsNullOrWhiteSpace(normalizedType) ? "all" : normalizedType
            },
            rows,
            statusOptions = BuildOptions(new[] { "all" }.Concat(AllowedStatuses)),
            typeOptions = BuildOptions(new[] { "all" }.Concat(AllowedTypes))
        });
    }

    [HttpGet("{id:int}")]
    public async Task<IActionResult> GetCampaignDetails([FromRoute] int id, CancellationToken cancellationToken)
    {
        var campaign = await _db.Campaigns
            .AsNoTracking()
            .Include(x => x.VoucherCoupon)
            .Include(x => x.CampaignSellerParticipations)
            .Include(x => x.CampaignProductSlots)
            .FirstOrDefaultAsync(x => x.CampaignId == id, cancellationToken);

        if (campaign is null)
        {
            return NotFound(new { message = "Khong tim thay campaign." });
        }

        return Ok(new
        {
            campaignId = campaign.CampaignId,
            name = campaign.Name,
            campaignType = campaign.CampaignType,
            description = campaign.Description,
            status = campaign.Status,
            budgetAmount = campaign.BudgetAmount,
            isFeatured = campaign.IsFeatured,
            voucherCouponId = campaign.VoucherCouponId,
            voucherCode = campaign.VoucherCoupon?.Code,
            registrationStartAt = campaign.RegistrationStartAt,
            registrationEndAt = campaign.RegistrationEndAt,
            startAt = campaign.StartAt,
            endAt = campaign.EndAt,
            createdAt = campaign.CreatedAt,
            updatedAt = campaign.UpdatedAt,
            approvedAt = campaign.ApprovedAt,
            participations = campaign.CampaignSellerParticipations
                .OrderByDescending(x => x.RequestedAt)
                .Select(x => new
                {
                    participationId = x.ParticipationId,
                    sellerId = x.SellerId,
                    status = x.Status,
                    notes = x.Notes,
                    discountPercent = x.DiscountPercent,
                    requestedSlots = x.RequestedSlots,
                    approvedSlots = x.ApprovedSlots,
                    requestedAt = x.RequestedAt,
                    reviewedAt = x.ReviewedAt,
                    reviewedBy = x.ReviewedBy
                }),
            slots = campaign.CampaignProductSlots
                .OrderByDescending(x => x.CreatedAt)
                .Select(x => new
                {
                    slotId = x.SlotId,
                    participationId = x.ParticipationId,
                    sellerId = x.SellerId,
                    productId = x.ProductId,
                    status = x.Status,
                    flashSalePrice = x.FlashSalePrice,
                    inventoryLimit = x.InventoryLimit,
                    createdAt = x.CreatedAt,
                    approvedAt = x.ApprovedAt
                })
        });
    }

    [HttpPost]
    public async Task<IActionResult> Create([FromBody] CampaignUpsertRequest request, CancellationToken cancellationToken)
    {
        var validationError = ValidateRequest(request);
        if (!string.IsNullOrWhiteSpace(validationError))
        {
            return BadRequest(new { message = validationError });
        }

        if (request.VoucherCouponId.HasValue)
        {
            var couponExists = await _db.Coupons.AsNoTracking().AnyAsync(x => x.CouponId == request.VoucherCouponId.Value, cancellationToken);
            if (!couponExists)
            {
                return BadRequest(new { message = "Voucher lien ket khong ton tai." });
            }
        }

        var actorUserId = TryGetActorUserId();
        var campaign = new Campaign
        {
            Name = request.Name.Trim(),
            CampaignType = NormalizeType(request.CampaignType),
            Description = NormalizeOptionalText(request.Description, 2000),
            RegistrationStartAt = request.RegistrationStartAt,
            RegistrationEndAt = request.RegistrationEndAt,
            StartAt = request.StartAt,
            EndAt = request.EndAt,
            Status = NormalizeStatus(request.Status),
            BudgetAmount = request.BudgetAmount,
            IsFeatured = request.IsFeatured,
            VoucherCouponId = request.VoucherCouponId,
            CreatedBy = actorUserId,
            CreatedAt = DateTime.UtcNow
        };

        _db.Campaigns.Add(campaign);
        AdminAuditLogger.AddAction(
            _db,
            "campaign_center",
            "create_campaign",
            "campaign",
            null,
            $"Tao campaign {campaign.Name}",
            actorUserId,
            new
            {
                campaign.CampaignType,
                campaign.Status,
                campaign.BudgetAmount,
                campaign.VoucherCouponId
            });
        await _db.SaveChangesAsync(cancellationToken);

        return Ok(new { success = true, campaignId = campaign.CampaignId, message = "Tao campaign thanh cong." });
    }

    [HttpPut("{id:int}")]
    public async Task<IActionResult> Update([FromRoute] int id, [FromBody] CampaignUpsertRequest request, CancellationToken cancellationToken)
    {
        var validationError = ValidateRequest(request);
        if (!string.IsNullOrWhiteSpace(validationError))
        {
            return BadRequest(new { message = validationError });
        }

        var campaign = await _db.Campaigns.FirstOrDefaultAsync(x => x.CampaignId == id, cancellationToken);
        if (campaign is null)
        {
            return NotFound(new { message = "Khong tim thay campaign." });
        }

        if (request.VoucherCouponId.HasValue)
        {
            var couponExists = await _db.Coupons.AsNoTracking().AnyAsync(x => x.CouponId == request.VoucherCouponId.Value, cancellationToken);
            if (!couponExists)
            {
                return BadRequest(new { message = "Voucher lien ket khong ton tai." });
            }
        }

        campaign.Name = request.Name.Trim();
        campaign.CampaignType = NormalizeType(request.CampaignType);
        campaign.Description = NormalizeOptionalText(request.Description, 2000);
        campaign.RegistrationStartAt = request.RegistrationStartAt;
        campaign.RegistrationEndAt = request.RegistrationEndAt;
        campaign.StartAt = request.StartAt;
        campaign.EndAt = request.EndAt;
        campaign.Status = NormalizeStatus(request.Status);
        campaign.BudgetAmount = request.BudgetAmount;
        campaign.IsFeatured = request.IsFeatured;
        campaign.VoucherCouponId = request.VoucherCouponId;
        campaign.UpdatedAt = DateTime.UtcNow;
        AdminAuditLogger.AddAction(
            _db,
            "campaign_center",
            "update_campaign",
            "campaign",
            campaign.CampaignId,
            $"Cap nhat campaign {campaign.Name}",
            TryGetActorUserId(),
            new
            {
                campaign.CampaignType,
                campaign.Status,
                campaign.BudgetAmount,
                campaign.VoucherCouponId
            });

        await _db.SaveChangesAsync(cancellationToken);

        return Ok(new { success = true, message = "Cap nhat campaign thanh cong." });
    }

    [HttpDelete("{id:int}")]
    public async Task<IActionResult> Delete([FromRoute] int id, CancellationToken cancellationToken)
    {
        var campaign = await _db.Campaigns.FirstOrDefaultAsync(x => x.CampaignId == id, cancellationToken);
        if (campaign is null)
        {
            return NotFound(new { message = "Khong tim thay campaign." });
        }

        var hasParticipations = await _db.CampaignSellerParticipations.AnyAsync(x => x.CampaignId == id, cancellationToken);
        var hasSlots = await _db.CampaignProductSlots.AnyAsync(x => x.CampaignId == id, cancellationToken);
        var hasAdsCampaigns = await _db.AdsCampaigns.AnyAsync(x => x.CampaignId == id, cancellationToken);
        if (hasParticipations || hasSlots || hasAdsCampaigns)
        {
            return BadRequest(new { message = "Campaign da co nha ban, suat san pham hoac ads campaign lien ket nen khong the xoa." });
        }

        var actorUserId = TryGetActorUserId();
        AdminAuditLogger.AddAction(
            _db,
            "campaign_center",
            "delete_campaign",
            "campaign",
            campaign.CampaignId,
            $"Xoa campaign {campaign.Name}",
            actorUserId,
            new
            {
                campaign.CampaignType,
                campaign.Status,
                campaign.BudgetAmount,
                campaign.VoucherCouponId
            });
        _db.Campaigns.Remove(campaign);
        await _db.SaveChangesAsync(cancellationToken);

        return Ok(new { success = true, message = "Xoa campaign thanh cong." });
    }

    [HttpPost("{id:int}/toggle-registration")]
    public async Task<IActionResult> ToggleRegistration([FromRoute] int id, CancellationToken cancellationToken)
    {
        var campaign = await _db.Campaigns.FirstOrDefaultAsync(x => x.CampaignId == id, cancellationToken);
        if (campaign is null)
        {
            return NotFound(new { message = "Khong tim thay campaign." });
        }

        campaign.Status = campaign.Status == "registration_open" ? "registration_closed" : "registration_open";
        campaign.UpdatedAt = DateTime.UtcNow;
        AdminAuditLogger.AddAction(
            _db,
            "campaign_center",
            "toggle_registration",
            "campaign",
            campaign.CampaignId,
            $"Doi trang thai dang ky campaign sang {campaign.Status}",
            TryGetActorUserId(),
            new
            {
                campaign.Status
            });
        await _db.SaveChangesAsync(cancellationToken);

        return Ok(new { success = true, status = campaign.Status, message = campaign.Status == "registration_open" ? "Da mo dang ky seller." : "Da dong dang ky seller." });
    }

    [HttpPost("{id:int}/participations")]
    public async Task<IActionResult> UpsertParticipation([FromRoute] int id, [FromBody] ParticipationUpsertRequest request, CancellationToken cancellationToken)
    {
        if (request.SellerId <= 0)
        {
            return BadRequest(new { message = "SellerId khong hop le." });
        }

        var normalizedStatus = NormalizeParticipationStatus(request.Status);
        if (string.IsNullOrWhiteSpace(normalizedStatus))
        {
            return BadRequest(new { message = "Trang thai tham gia khong hop le." });
        }

        var campaign = await _db.Campaigns.AsNoTracking().FirstOrDefaultAsync(x => x.CampaignId == id, cancellationToken);
        if (campaign is null)
        {
            return NotFound(new { message = "Khong tim thay campaign." });
        }

        var participation = await _db.CampaignSellerParticipations
            .FirstOrDefaultAsync(x => x.CampaignId == id && x.SellerId == request.SellerId, cancellationToken);

        if (participation is null)
        {
            participation = new CampaignSellerParticipation
            {
                CampaignId = id,
                SellerId = request.SellerId,
                Status = normalizedStatus,
                Notes = NormalizeOptionalText(request.Notes, 2000),
                DiscountPercent = request.DiscountPercent,
                RequestedSlots = request.RequestedSlots,
                ApprovedSlots = request.ApprovedSlots,
                RequestedAt = DateTime.UtcNow,
                ReviewedAt = RequiresReviewTimestamp(normalizedStatus) ? DateTime.UtcNow : null,
                ReviewedBy = RequiresReviewTimestamp(normalizedStatus) ? TryGetActorUserId() : null
            };
            _db.CampaignSellerParticipations.Add(participation);
        }
        else
        {
            participation.Status = normalizedStatus;
            participation.Notes = NormalizeOptionalText(request.Notes, 2000);
            participation.DiscountPercent = request.DiscountPercent;
            participation.RequestedSlots = request.RequestedSlots;
            participation.ApprovedSlots = request.ApprovedSlots;
            participation.ReviewedAt = RequiresReviewTimestamp(normalizedStatus) ? DateTime.UtcNow : null;
            participation.ReviewedBy = RequiresReviewTimestamp(normalizedStatus) ? TryGetActorUserId() : null;
        }

        await _db.SaveChangesAsync(cancellationToken);

        return Ok(new { success = true, message = "Da cap nhat seller tham gia campaign." });
    }

    [HttpGet("ads/overview")]
    public async Task<IActionResult> GetAdsOverview([FromQuery] string? q = null, [FromQuery] string? status = null, CancellationToken cancellationToken = default)
    {
        var normalizedQuery = q?.Trim();
        var normalizedStatus = NormalizeWalletStatus(status);

        var wallets = _db.SellerAdsWallets.AsNoTracking().AsQueryable();
        if (!string.IsNullOrWhiteSpace(normalizedStatus) && normalizedStatus != "all")
        {
            wallets = wallets.Where(x => x.Status == normalizedStatus);
        }

        if (!string.IsNullOrWhiteSpace(normalizedQuery) && int.TryParse(normalizedQuery, out var sellerId))
        {
            wallets = wallets.Where(x => x.SellerId == sellerId);
        }

        var walletRows = await wallets
            .OrderByDescending(x => x.Balance)
            .ThenByDescending(x => x.TotalTopup)
            .Select(x => new
            {
                walletId = x.WalletId,
                sellerId = x.SellerId,
                balance = x.Balance,
                reservedBalance = x.ReservedBalance,
                availableBalance = x.Balance - x.ReservedBalance,
                totalTopup = x.TotalTopup,
                totalSpend = x.TotalSpend,
                status = x.Status,
                createdAt = x.CreatedAt,
                updatedAt = x.UpdatedAt
            })
            .ToListAsync(cancellationToken);

        var adsCampaigns = await _db.AdsCampaigns
            .AsNoTracking()
            .OrderByDescending(x => x.CreatedAt)
            .Select(x => new
            {
                adsCampaignId = x.AdsCampaignId,
                walletId = x.WalletId,
                sellerId = x.SellerId,
                campaignId = x.CampaignId,
                name = x.Name,
                channel = x.Channel,
                status = x.Status,
                dailyBudget = x.DailyBudget,
                totalBudget = x.TotalBudget,
                spendToDate = x.SpendToDate,
                startAt = x.StartAt,
                endAt = x.EndAt,
                createdAt = x.CreatedAt
            })
            .Take(8)
            .ToListAsync(cancellationToken);

        var recentTopups = await _db.AdsTopups
            .AsNoTracking()
            .OrderByDescending(x => x.CreatedAt)
            .Select(x => new
            {
                topupId = x.TopupId,
                walletId = x.WalletId,
                sellerId = x.SellerId,
                amount = x.Amount,
                status = x.Status,
                paymentMethod = x.PaymentMethod,
                referenceCode = x.ReferenceCode,
                createdAt = x.CreatedAt
            })
            .Take(6)
            .ToListAsync(cancellationToken);

        var totalBalance = walletRows.Sum(x => x.balance);
        var totalReserved = walletRows.Sum(x => x.reservedBalance);
        var totalAvailable = walletRows.Sum(x => x.availableBalance);
        var totalTopup = walletRows.Sum(x => x.totalTopup);
        var totalSpend = walletRows.Sum(x => x.totalSpend);

        return Ok(new
        {
            stats = new
            {
                walletCount = walletRows.Count,
                activeWalletCount = walletRows.Count(x => x.status == "active"),
                totalBalance,
                totalReserved,
                totalAvailable,
                totalTopup,
                totalSpend,
                runningAdsCampaigns = adsCampaigns.Count(x => x.status == "running")
            },
            filters = new
            {
                q = normalizedQuery ?? string.Empty,
                status = string.IsNullOrWhiteSpace(normalizedStatus) ? "all" : normalizedStatus,
                statusOptions = BuildOptions(new[] { "all" }.Concat(AllowedWalletStatuses))
            },
            wallets = walletRows,
            adsCampaigns,
            recentTopups
        });
    }

    [HttpPost("ads/topups")]
    public async Task<IActionResult> CreateAdsTopup([FromBody] AdsTopupRequest request, CancellationToken cancellationToken)
    {
        if (request.SellerId <= 0 || request.Amount <= 0)
        {
            return BadRequest(new { message = "Thong tin topup khong hop le." });
        }

        var actorUserId = TryGetActorUserId();
        var wallet = await GetOrCreateWalletAsync(request.SellerId, cancellationToken);
        wallet.Balance += request.Amount;
        wallet.TotalTopup += request.Amount;
        wallet.UpdatedAt = DateTime.UtcNow;

        var topup = new AdsTopup
        {
            WalletId = wallet.WalletId,
            SellerId = request.SellerId,
            Amount = request.Amount,
            Status = "confirmed",
            PaymentMethod = NormalizeOptionalText(request.PaymentMethod, 50),
            ReferenceCode = NormalizeOptionalText(request.ReferenceCode, 100),
            Notes = NormalizeOptionalText(request.Notes, 1000),
            CreatedBy = actorUserId,
            CreatedAt = DateTime.UtcNow,
            ConfirmedAt = DateTime.UtcNow
        };

        _db.AdsTopups.Add(topup);
        var actionLog = AdminAuditLogger.AddAction(
            _db,
            "campaign_center",
            "ads_topup",
            "ads_wallet",
            wallet.WalletId,
            $"Nap ads wallet cho seller #{request.SellerId}",
            actorUserId,
            new
            {
                request.SellerId,
                request.Amount,
                request.PaymentMethod,
                request.ReferenceCode
            });
        AdminAuditLogger.AddSettlement(
            _db,
            actionLog,
            "ads_topup",
            "ads_wallet",
            wallet.WalletId,
            request.SellerId,
            request.Amount,
            request.Notes,
            actorUserId);
        await _db.SaveChangesAsync(cancellationToken);

        return Ok(new { success = true, message = "Da nap ngan sach ads cho seller.", walletId = wallet.WalletId });
    }

    [HttpPost("ads/spends")]
    public async Task<IActionResult> CreateAdsSpend([FromBody] AdsSpendRequest request, CancellationToken cancellationToken)
    {
        if (request.SellerId <= 0 || request.Amount <= 0)
        {
            return BadRequest(new { message = "Thong tin spend khong hop le." });
        }

        var actorUserId = TryGetActorUserId();
        var wallet = await GetOrCreateWalletAsync(request.SellerId, cancellationToken);
        if (wallet.Balance - wallet.ReservedBalance < request.Amount)
        {
            return BadRequest(new { message = "So du ads wallet khong du de ghi nhan spend." });
        }

        AdsCampaign? adsCampaign = null;
        if (request.AdsCampaignId.HasValue)
        {
            adsCampaign = await _db.AdsCampaigns.FirstOrDefaultAsync(x => x.AdsCampaignId == request.AdsCampaignId.Value && x.SellerId == request.SellerId, cancellationToken);
            if (adsCampaign is null)
            {
                return BadRequest(new { message = "Ads campaign khong ton tai." });
            }
        }

        wallet.Balance -= request.Amount;
        wallet.TotalSpend += request.Amount;
        wallet.UpdatedAt = DateTime.UtcNow;
        if (adsCampaign is not null)
        {
            adsCampaign.SpendToDate += request.Amount;
            adsCampaign.UpdatedAt = DateTime.UtcNow;
        }

        var spend = new AdsSpendLedger
        {
            WalletId = wallet.WalletId,
            SellerId = request.SellerId,
            AdsCampaignId = request.AdsCampaignId,
            Amount = request.Amount,
            SpendType = string.IsNullOrWhiteSpace(request.SpendType) ? "manual_adjustment" : NormalizeOptional(request.SpendType),
            Status = "posted",
            Notes = NormalizeOptionalText(request.Notes, 1000),
            CreatedBy = actorUserId,
            CreatedAt = DateTime.UtcNow
        };

        _db.AdsSpendLedgers.Add(spend);
        var actionLog = AdminAuditLogger.AddAction(
            _db,
            "campaign_center",
            "ads_spend",
            "ads_wallet",
            wallet.WalletId,
            $"Ghi nhan spend ads cho seller #{request.SellerId}",
            actorUserId,
            new
            {
                request.SellerId,
                request.AdsCampaignId,
                request.Amount,
                spend.SpendType
            });
        AdminAuditLogger.AddSettlement(
            _db,
            actionLog,
            "ads_spend",
            request.AdsCampaignId.HasValue ? "ads_campaign" : "ads_wallet",
            request.AdsCampaignId ?? wallet.WalletId,
            request.SellerId,
            request.Amount,
            request.Notes,
            actorUserId);
        await _db.SaveChangesAsync(cancellationToken);

        return Ok(new { success = true, message = "Da ghi nhan chi phi ads.", walletId = wallet.WalletId });
    }

    [HttpPost("ads/campaigns")]
    public async Task<IActionResult> CreateAdsCampaign([FromBody] AdsCampaignUpsertRequest request, CancellationToken cancellationToken)
    {
        if (request.SellerId <= 0 || string.IsNullOrWhiteSpace(request.Name))
        {
            return BadRequest(new { message = "Thong tin ads campaign khong hop le." });
        }

        if (request.StartAt > request.EndAt)
        {
            return BadRequest(new { message = "Thoi gian ads campaign khong hop le." });
        }

        var normalizedStatus = NormalizeAdsCampaignStatus(request.Status);
        if (string.IsNullOrWhiteSpace(normalizedStatus))
        {
            return BadRequest(new { message = "Trang thai ads campaign khong hop le." });
        }

        var actorUserId = TryGetActorUserId();
        var wallet = await GetOrCreateWalletAsync(request.SellerId, cancellationToken);
        Campaign? campaign = null;
        if (request.CampaignId.HasValue)
        {
            campaign = await _db.Campaigns.AsNoTracking().FirstOrDefaultAsync(x => x.CampaignId == request.CampaignId.Value, cancellationToken);
            if (campaign is null)
            {
                return BadRequest(new { message = "Campaign lien ket khong ton tai." });
            }
        }

        var adsCampaign = new AdsCampaign
        {
            WalletId = wallet.WalletId,
            SellerId = request.SellerId,
            CampaignId = request.CampaignId,
            Name = request.Name.Trim(),
            Channel = string.IsNullOrWhiteSpace(request.Channel) ? "onsite" : NormalizeOptional(request.Channel),
            Status = normalizedStatus,
            DailyBudget = request.DailyBudget,
            TotalBudget = request.TotalBudget,
            SpendToDate = 0m,
            StartAt = request.StartAt,
            EndAt = request.EndAt,
            CreatedAt = DateTime.UtcNow
        };

        if (request.ReserveBudget && request.TotalBudget > 0)
        {
            if (wallet.Balance - wallet.ReservedBalance < request.TotalBudget)
            {
                return BadRequest(new { message = "So du kha dung khong du de reserve budget cho ads campaign." });
            }

            wallet.ReservedBalance += request.TotalBudget;
            wallet.UpdatedAt = DateTime.UtcNow;
        }

        _db.AdsCampaigns.Add(adsCampaign);
        var actionLog = AdminAuditLogger.AddAction(
            _db,
            "campaign_center",
            "create_ads_campaign",
            "ads_campaign",
            null,
            $"Tao ads campaign {adsCampaign.Name} cho seller #{request.SellerId}",
            actorUserId,
            new
            {
                request.SellerId,
                request.CampaignId,
                adsCampaign.Channel,
                adsCampaign.Status,
                adsCampaign.TotalBudget,
                request.ReserveBudget
            });
        if (request.ReserveBudget && request.TotalBudget > 0)
        {
            AdminAuditLogger.AddSettlement(
                _db,
                actionLog,
                "ads_budget_reserve",
                "ads_campaign",
                null,
                request.SellerId,
                request.TotalBudget,
                "Reserve budget khi tao ads campaign",
                actorUserId);
        }
        await _db.SaveChangesAsync(cancellationToken);

        return Ok(new { success = true, message = "Da tao ads campaign MVP.", adsCampaignId = adsCampaign.AdsCampaignId });
    }

    private static string? ValidateRequest(CampaignUpsertRequest request)
    {
        if (string.IsNullOrWhiteSpace(request.Name))
        {
            return "Ten campaign khong duoc de trong.";
        }

        var type = NormalizeType(request.CampaignType);
        if (!AllowedTypes.Contains(type))
        {
            return "Loai campaign khong hop le.";
        }

        var status = NormalizeStatus(request.Status);
        if (!AllowedStatuses.Contains(status))
        {
            return "Trang thai campaign khong hop le.";
        }

        if (request.RegistrationEndAt < request.RegistrationStartAt)
        {
            return "Thoi gian dong dang ky phai sau mo dang ky.";
        }

        if (request.EndAt < request.StartAt)
        {
            return "Thoi gian ket thuc phai sau thoi gian bat dau.";
        }

        if (request.StartAt < request.RegistrationStartAt)
        {
            return "Campaign khong the bat dau truoc khi mo dang ky.";
        }

        if (request.EndAt < request.RegistrationEndAt)
        {
            return "Campaign khong the ket thuc truoc khi dong dang ky.";
        }

        if (request.BudgetAmount.HasValue && request.BudgetAmount.Value < 0)
        {
            return "Ngan sach khong hop le.";
        }

        return null;
    }

    private int? TryGetActorUserId()
    {
        var subject = User.FindFirstValue(JwtRegisteredClaimNames.Sub) ?? User.FindFirstValue(ClaimTypes.NameIdentifier) ?? User.FindFirstValue("sub");
        return int.TryParse(subject, out var userId) ? userId : null;
    }

    private static string NormalizeOptional(string? value)
        => string.IsNullOrWhiteSpace(value) ? string.Empty : value.Trim().ToLowerInvariant();

    private static string NormalizeStatus(string? value)
    {
        var normalized = NormalizeOptional(value);
        return string.IsNullOrWhiteSpace(normalized) ? "draft" : normalized;
    }

    private static string NormalizeType(string? value)
    {
        var normalized = NormalizeOptional(value);
        return string.IsNullOrWhiteSpace(normalized) ? "flash_sale" : normalized;
    }

    private static string? NormalizeOptionalText(string? value, int maxLength)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return null;
        }

        var trimmed = value.Trim();
        return trimmed.Length <= maxLength ? trimmed : trimmed[..maxLength];
    }

    private static string NormalizeParticipationStatus(string? value)
    {
        var normalized = NormalizeOptional(value);
        return AllowedParticipationStatuses.Contains(normalized) ? normalized : string.Empty;
    }

    private static string NormalizeWalletStatus(string? value)
    {
        var normalized = NormalizeOptional(value);
        return normalized is "all" or "" ? "all" : AllowedWalletStatuses.Contains(normalized) ? normalized : "all";
    }

    private static string NormalizeAdsCampaignStatus(string? value)
    {
        var normalized = NormalizeOptional(value);
        return AllowedAdsCampaignStatuses.Contains(normalized) ? normalized : string.Empty;
    }

    private async Task<SellerAdsWallet> GetOrCreateWalletAsync(int sellerId, CancellationToken cancellationToken)
    {
        var wallet = await _db.SellerAdsWallets.FirstOrDefaultAsync(x => x.SellerId == sellerId, cancellationToken);
        if (wallet is not null)
        {
            return wallet;
        }

        wallet = new SellerAdsWallet
        {
            SellerId = sellerId,
            Balance = 0m,
            ReservedBalance = 0m,
            TotalTopup = 0m,
            TotalSpend = 0m,
            Status = "active",
            CreatedAt = DateTime.UtcNow
        };

        _db.SellerAdsWallets.Add(wallet);
        await _db.SaveChangesAsync(cancellationToken);
        return wallet;
    }

    private static bool RequiresReviewTimestamp(string status)
        => status is "approved" or "rejected" or "waitlist";

    private static IEnumerable<object> BuildOptions(IEnumerable<string> values)
        => values.Select(value => new { value, text = FormatLabel(value) });

    private static string FormatLabel(string value)
        => value switch
        {
            "all" => "Tat ca",
            "registration_open" => "Mo dang ky",
            "registration_closed" => "Dong dang ky",
            "flash_sale" => "Flash sale",
            "voucher_boost" => "Voucher boost",
            _ => string.Join(' ', value.Split('_', StringSplitOptions.RemoveEmptyEntries)
                .Select(part => char.ToUpperInvariant(part[0]) + part[1..]))
        };

    public sealed class CampaignUpsertRequest
    {
        public string Name { get; set; } = string.Empty;

        public string CampaignType { get; set; } = string.Empty;

        public string Status { get; set; } = string.Empty;

        public string? Description { get; set; }

        public DateTime RegistrationStartAt { get; set; }

        public DateTime RegistrationEndAt { get; set; }

        public DateTime StartAt { get; set; }

        public DateTime EndAt { get; set; }

        public decimal? BudgetAmount { get; set; }

        public bool IsFeatured { get; set; }

        public int? VoucherCouponId { get; set; }
    }

    public sealed class ParticipationUpsertRequest
    {
        public int SellerId { get; set; }

        public string Status { get; set; } = string.Empty;

        public string? Notes { get; set; }

        public decimal? DiscountPercent { get; set; }

        public int? RequestedSlots { get; set; }

        public int? ApprovedSlots { get; set; }
    }

    public sealed class AdsTopupRequest
    {
        public int SellerId { get; set; }

        public decimal Amount { get; set; }

        public string? PaymentMethod { get; set; }

        public string? ReferenceCode { get; set; }

        public string? Notes { get; set; }
    }

    public sealed class AdsSpendRequest
    {
        public int SellerId { get; set; }

        public int? AdsCampaignId { get; set; }

        public decimal Amount { get; set; }

        public string? SpendType { get; set; }

        public string? Notes { get; set; }
    }

    public sealed class AdsCampaignUpsertRequest
    {
        public int SellerId { get; set; }

        public int? CampaignId { get; set; }

        public string Name { get; set; } = string.Empty;

        public string? Channel { get; set; }

        public string Status { get; set; } = "draft";

        public decimal DailyBudget { get; set; }

        public decimal TotalBudget { get; set; }

        public bool ReserveBudget { get; set; }

        public DateTime StartAt { get; set; }

        public DateTime EndAt { get; set; }
    }
}

