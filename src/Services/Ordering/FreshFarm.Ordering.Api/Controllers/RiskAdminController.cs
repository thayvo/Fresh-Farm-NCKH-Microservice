using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Text.Json;
using FreshFarm.Ordering.Api.Models;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace FreshFarm.Ordering.Api.Controllers;

[ApiController]
[Route("api/orders/admin/risk")]
[Authorize(Policy = "AdminOnly")]
public sealed class RiskAdminController : ControllerBase
{
    private static readonly string[] AllowedCaseTypes = { "all", "voucher_abuse", "return_spike" };
    private static readonly string[] AllowedStatuses = { "all", "open", "pending_review", "monitoring", "resolved", "dismissed" };
    private static readonly string[] AllowedSeverities = { "all", "low", "medium", "high", "critical" };
    private static readonly string[] AllowedDecisionTypes = { "monitor", "limit_coupon", "suspend_campaign", "suspend_seller", "dismiss", "escalate" };

    private readonly FreshFarmOrderingDBContext _db;

    public RiskAdminController(FreshFarmOrderingDBContext db)
    {
        _db = db;
    }

    [HttpGet("overview")]
    public async Task<IActionResult> GetOverview(CancellationToken cancellationToken)
    {
        await SyncHeuristicsCoreAsync(cancellationToken);

        var since = DateTime.UtcNow.AddDays(-7);
        var cases = _db.RiskCases.AsNoTracking();

        var totalCases = await cases.CountAsync(cancellationToken);
        var openCases = await cases.CountAsync(x => x.Status == "open", cancellationToken);
        var reviewCases = await cases.CountAsync(x => x.Status == "pending_review", cancellationToken);
        var escalatedCases = await cases.CountAsync(x => x.IsEscalated, cancellationToken);
        var highSeverityCases = await cases.CountAsync(x => x.Severity == "high" || x.Severity == "critical", cancellationToken);
        var voucherAbuseCases = await cases.CountAsync(x => x.CaseType == "voucher_abuse", cancellationToken);
        var returnSpikeCases = await cases.CountAsync(x => x.CaseType == "return_spike", cancellationToken);
        var decisionsLast7d = await _db.RiskDecisions.AsNoTracking().CountAsync(x => x.CreatedAt >= since, cancellationToken);

        var recent = await cases
            .OrderByDescending(x => x.LastSignalAt ?? x.CreatedAt)
            .Select(x => new
            {
                riskCaseId = x.RiskCaseId,
                caseType = x.CaseType,
                title = x.Title,
                status = x.Status,
                severity = x.Severity,
                signalCount = x.SignalCount,
                updatedAt = x.UpdatedAt,
                lastSignalAt = x.LastSignalAt
            })
            .Take(5)
            .ToListAsync(cancellationToken);

        return Ok(new
        {
            stats = new
            {
                totalCases,
                openCases,
                reviewCases,
                escalatedCases,
                highSeverityCases,
                voucherAbuseCases,
                returnSpikeCases,
                decisionsLast7d
            },
            recent
        });
    }

    [HttpGet("cases")]
    public async Task<IActionResult> GetCases(
        [FromQuery] string? q = null,
        [FromQuery] string? type = null,
        [FromQuery] string? status = null,
        [FromQuery] string? severity = null,
        CancellationToken cancellationToken = default)
    {
        await SyncHeuristicsCoreAsync(cancellationToken);

        var normalizedType = NormalizeOption(type, AllowedCaseTypes, "all");
        var normalizedStatus = NormalizeOption(status, AllowedStatuses, "all");
        var normalizedSeverity = NormalizeOption(severity, AllowedSeverities, "all");
        var normalizedQuery = q?.Trim();

        var query = _db.RiskCases.AsNoTracking().AsQueryable();

        if (!string.IsNullOrWhiteSpace(normalizedQuery))
        {
            query = query.Where(x =>
                x.Title.Contains(normalizedQuery) ||
                (x.Summary != null && x.Summary.Contains(normalizedQuery)) ||
                x.ReferenceKey.Contains(normalizedQuery));
        }

        if (normalizedType != "all")
        {
            query = query.Where(x => x.CaseType == normalizedType);
        }

        if (normalizedStatus != "all")
        {
            query = query.Where(x => x.Status == normalizedStatus);
        }

        if (normalizedSeverity != "all")
        {
            query = query.Where(x => x.Severity == normalizedSeverity);
        }

        var rows = await query
            .OrderByDescending(x => x.Severity == "critical")
            .ThenByDescending(x => x.Severity == "high")
            .ThenByDescending(x => x.Severity == "medium")
            .ThenByDescending(x => x.LastSignalAt ?? x.CreatedAt)
            .Select(x => new
            {
                riskCaseId = x.RiskCaseId,
                caseType = x.CaseType,
                title = x.Title,
                summary = x.Summary,
                status = x.Status,
                severity = x.Severity,
                sellerId = x.SellerId,
                buyerId = x.BuyerId,
                orderId = x.OrderId,
                campaignId = x.CampaignId,
                voucherCouponId = x.VoucherCouponId,
                signalCount = x.SignalCount,
                isEscalated = x.IsEscalated,
                createdAt = x.CreatedAt,
                updatedAt = x.UpdatedAt,
                lastSignalAt = x.LastSignalAt
            })
            .ToListAsync(cancellationToken);

        return Ok(new
        {
            filters = new
            {
                q = normalizedQuery ?? string.Empty,
                type = normalizedType,
                status = normalizedStatus,
                severity = normalizedSeverity,
                typeOptions = BuildOptions(AllowedCaseTypes),
                statusOptions = BuildOptions(AllowedStatuses),
                severityOptions = BuildOptions(AllowedSeverities)
            },
            rows
        });
    }

    [HttpGet("cases/{id:int}")]
    public async Task<IActionResult> GetCaseDetails([FromRoute] int id, CancellationToken cancellationToken)
    {
        await SyncHeuristicsCoreAsync(cancellationToken);

        var riskCase = await _db.RiskCases
            .AsNoTracking()
            .Include(x => x.RiskSignals)
            .Include(x => x.RiskDecisions)
            .Include(x => x.VoucherAbuseCases)
            .FirstOrDefaultAsync(x => x.RiskCaseId == id, cancellationToken);

        if (riskCase is null)
        {
            return NotFound(new { message = "Khong tim thay risk case." });
        }

        var couponCode = riskCase.VoucherCouponId.HasValue
            ? await _db.Coupons.AsNoTracking()
                .Where(x => x.CouponId == riskCase.VoucherCouponId.Value)
                .Select(x => x.Code)
                .FirstOrDefaultAsync(cancellationToken)
            : null;

        return Ok(new
        {
            riskCaseId = riskCase.RiskCaseId,
            referenceKey = riskCase.ReferenceKey,
            caseType = riskCase.CaseType,
            title = riskCase.Title,
            summary = riskCase.Summary,
            status = riskCase.Status,
            severity = riskCase.Severity,
            sellerId = riskCase.SellerId,
            buyerId = riskCase.BuyerId,
            orderId = riskCase.OrderId,
            campaignId = riskCase.CampaignId,
            voucherCouponId = riskCase.VoucherCouponId,
            voucherCode = couponCode,
            signalCount = riskCase.SignalCount,
            isEscalated = riskCase.IsEscalated,
            createdAt = riskCase.CreatedAt,
            updatedAt = riskCase.UpdatedAt,
            lastSignalAt = riskCase.LastSignalAt,
            reviewedAt = riskCase.ReviewedAt,
            reviewedBy = riskCase.ReviewedBy,
            signals = riskCase.RiskSignals
                .OrderByDescending(x => x.TriggeredAt)
                .Select(x => new
                {
                    riskSignalId = x.RiskSignalId,
                    signalType = x.SignalType,
                    signalCode = x.SignalCode,
                    severity = x.Severity,
                    source = x.Source,
                    score = x.Score,
                    metadataJson = x.MetadataJson,
                    triggeredAt = x.TriggeredAt
                }),
            decisions = riskCase.RiskDecisions
                .OrderByDescending(x => x.CreatedAt)
                .Select(x => new
                {
                    riskDecisionId = x.RiskDecisionId,
                    decisionType = x.DecisionType,
                    notes = x.Notes,
                    createdBy = x.CreatedBy,
                    createdAt = x.CreatedAt
                }),
            voucherAbuseCases = riskCase.VoucherAbuseCases
                .OrderByDescending(x => x.CreatedAt)
                .Select(x => new
                {
                    voucherAbuseCaseId = x.VoucherAbuseCaseId,
                    couponId = x.CouponId,
                    buyerId = x.BuyerId,
                    sellerId = x.SellerId,
                    campaignId = x.CampaignId,
                    orderId = x.OrderId,
                    abuseType = x.AbuseType,
                    suspectedBenefitAmount = x.SuspectedBenefitAmount,
                    status = x.Status,
                    createdAt = x.CreatedAt,
                    reviewedAt = x.ReviewedAt
                }),
            decisionOptions = BuildOptions(AllowedDecisionTypes)
        });
    }

    [HttpPost("sync-heuristics")]
    public async Task<IActionResult> SyncHeuristics(CancellationToken cancellationToken)
    {
        var actorUserId = TryGetActorUserId();
        var summary = await SyncHeuristicsCoreAsync(cancellationToken);
        AdminAuditLogger.AddAction(
            _db,
            "risk_center",
            "sync_heuristics",
            "risk_center",
            null,
            "Dong bo heuristic risk center",
            actorUserId,
            new
            {
                summary.CaseCount,
                summary.SignalCount
            });
        await _db.SaveChangesAsync(cancellationToken);
        return Ok(new
        {
            message = $"Da dong bo heuristic risk center. Cases: {summary.CaseCount}, signals: {summary.SignalCount}.",
            caseCount = summary.CaseCount,
            signalCount = summary.SignalCount
        });
    }

    [HttpPost("cases/{id:int}/decisions")]
    public async Task<IActionResult> CreateDecision([FromRoute] int id, [FromBody] RiskDecisionRequest request, CancellationToken cancellationToken)
    {
        var decisionType = NormalizeOption(request.DecisionType, AllowedDecisionTypes, string.Empty);
        if (string.IsNullOrWhiteSpace(decisionType))
        {
            return BadRequest(new { message = "Quyet dinh risk khong hop le." });
        }

        var riskCase = await _db.RiskCases.FirstOrDefaultAsync(x => x.RiskCaseId == id, cancellationToken);
        if (riskCase is null)
        {
            return NotFound(new { message = "Khong tim thay risk case." });
        }

        var actorUserId = TryGetActorUserId();
        var decision = new RiskDecision
        {
            RiskCaseId = id,
            DecisionType = decisionType,
            Notes = NormalizeOptionalText(request.Notes, 2000),
            CreatedBy = actorUserId,
            CreatedAt = DateTime.UtcNow
        };

        riskCase.Status = decisionType switch
        {
            "dismiss" => "dismissed",
            "monitor" => "monitoring",
            "escalate" => "pending_review",
            _ => "resolved"
        };
        riskCase.IsEscalated = decisionType == "escalate";
        riskCase.ReviewedAt = DateTime.UtcNow;
        riskCase.ReviewedBy = decision.CreatedBy;
        riskCase.UpdatedAt = DateTime.UtcNow;

        _db.RiskDecisions.Add(decision);
        var actionLog = AdminAuditLogger.AddAction(
            _db,
            "risk_center",
            "case_decision",
            "risk_case",
            riskCase.RiskCaseId,
            $"Ghi nhan decision {decisionType} cho risk case #{riskCase.RiskCaseId}",
            actorUserId,
            new
            {
                riskCase.CaseType,
                riskCase.Severity,
                decisionType
            });
        AdminAuditLogger.AddModeration(
            _db,
            actionLog,
            "risk_case",
            riskCase.RiskCaseId,
            decisionType,
            request.Notes,
            actorUserId);
        await _db.SaveChangesAsync(cancellationToken);

        return Ok(new { message = "Da ghi nhan quyet dinh risk case.", status = riskCase.Status });
    }

    private async Task<HeuristicSyncSummary> SyncHeuristicsCoreAsync(CancellationToken cancellationToken)
    {
        var couponCandidates = await LoadVoucherAbuseCandidatesAsync(cancellationToken);
        var returnCandidates = await LoadReturnSpikeCandidatesAsync(cancellationToken);
        var caseKeys = couponCandidates.Select(x => x.ReferenceKey)
            .Concat(returnCandidates.Select(x => x.ReferenceKey))
            .Distinct()
            .ToList();

        var signalKeys = couponCandidates.Select(x => x.SignalReferenceKey)
            .Concat(returnCandidates.Select(x => x.SignalReferenceKey))
            .Distinct()
            .ToList();

        var existingCases = await _db.RiskCases
            .Where(x => caseKeys.Contains(x.ReferenceKey))
            .ToDictionaryAsync(x => x.ReferenceKey, cancellationToken);

        var existingSignals = await _db.RiskSignals
            .Where(x => signalKeys.Contains(x.ReferenceKey))
            .ToDictionaryAsync(x => x.ReferenceKey, cancellationToken);

        var existingAbuseCases = await _db.VoucherAbuseCases
            .Where(x => caseKeys.Contains(x.ReferenceKey))
            .ToDictionaryAsync(x => x.ReferenceKey, cancellationToken);

        foreach (var candidate in couponCandidates)
        {
            var riskCase = await UpsertRiskCaseAsync(candidate, existingCases, cancellationToken);
            UpsertRiskSignal(candidate, riskCase, existingSignals);
            UpsertVoucherAbuseCase(candidate, riskCase, existingAbuseCases);
        }

        foreach (var candidate in returnCandidates)
        {
            var riskCase = await UpsertRiskCaseAsync(candidate, existingCases, cancellationToken);
            UpsertRiskSignal(candidate, riskCase, existingSignals);
        }

        await _db.SaveChangesAsync(cancellationToken);

        return new HeuristicSyncSummary
        {
            CaseCount = caseKeys.Count,
            SignalCount = signalKeys.Count
        };
    }

    private async Task<RiskCase> UpsertRiskCaseAsync(
        HeuristicRiskCandidate candidate,
        IDictionary<string, RiskCase> existingCases,
        CancellationToken cancellationToken)
    {
        if (existingCases.TryGetValue(candidate.ReferenceKey, out var existing))
        {
            existing.CaseType = candidate.CaseType;
            existing.Title = candidate.Title;
            existing.Summary = candidate.Summary;
            existing.Severity = candidate.Severity;
            existing.SellerId = candidate.SellerId;
            existing.BuyerId = candidate.BuyerId;
            existing.OrderId = candidate.OrderId;
            existing.CampaignId = candidate.CampaignId;
            existing.VoucherCouponId = candidate.VoucherCouponId;
            existing.SignalCount = candidate.SignalCount;
            existing.LastSignalAt = candidate.LastSignalAt;
            existing.UpdatedAt = DateTime.UtcNow;
            if (existing.Status is "resolved" or "dismissed")
            {
                return existing;
            }

            existing.Status = existing.IsEscalated ? "pending_review" : "open";
            return existing;
        }

        var riskCase = new RiskCase
        {
            ReferenceKey = candidate.ReferenceKey,
            CaseType = candidate.CaseType,
            Status = "open",
            Severity = candidate.Severity,
            SellerId = candidate.SellerId,
            BuyerId = candidate.BuyerId,
            OrderId = candidate.OrderId,
            CampaignId = candidate.CampaignId,
            VoucherCouponId = candidate.VoucherCouponId,
            Title = candidate.Title,
            Summary = candidate.Summary,
            SignalCount = candidate.SignalCount,
            LastSignalAt = candidate.LastSignalAt,
            CreatedAt = DateTime.UtcNow
        };

        _db.RiskCases.Add(riskCase);
        await _db.SaveChangesAsync(cancellationToken);
        existingCases[candidate.ReferenceKey] = riskCase;
        return riskCase;
    }

    private void UpsertRiskSignal(
        HeuristicRiskCandidate candidate,
        RiskCase riskCase,
        IDictionary<string, RiskSignal> existingSignals)
    {
        if (existingSignals.TryGetValue(candidate.SignalReferenceKey, out var signal))
        {
            signal.RiskCaseId = riskCase.RiskCaseId;
            signal.SignalType = candidate.SignalType;
            signal.SignalCode = candidate.SignalCode;
            signal.Severity = candidate.Severity;
            signal.Source = "heuristic_engine";
            signal.Score = candidate.Score;
            signal.SellerId = candidate.SellerId;
            signal.BuyerId = candidate.BuyerId;
            signal.OrderId = candidate.OrderId;
            signal.CampaignId = candidate.CampaignId;
            signal.VoucherCouponId = candidate.VoucherCouponId;
            signal.MetadataJson = candidate.MetadataJson;
            signal.TriggeredAt = candidate.LastSignalAt ?? DateTime.UtcNow;
            return;
        }

        signal = new RiskSignal
        {
            RiskCaseId = riskCase.RiskCaseId,
            ReferenceKey = candidate.SignalReferenceKey,
            SignalType = candidate.SignalType,
            SignalCode = candidate.SignalCode,
            Severity = candidate.Severity,
            Source = "heuristic_engine",
            Score = candidate.Score,
            SellerId = candidate.SellerId,
            BuyerId = candidate.BuyerId,
            OrderId = candidate.OrderId,
            CampaignId = candidate.CampaignId,
            VoucherCouponId = candidate.VoucherCouponId,
            MetadataJson = candidate.MetadataJson,
            TriggeredAt = candidate.LastSignalAt ?? DateTime.UtcNow,
            CreatedAt = DateTime.UtcNow
        };

        _db.RiskSignals.Add(signal);
        existingSignals[candidate.SignalReferenceKey] = signal;
    }

    private void UpsertVoucherAbuseCase(
        HeuristicRiskCandidate candidate,
        RiskCase riskCase,
        IDictionary<string, VoucherAbuseCase> existingAbuseCases)
    {
        if (candidate.CaseType != "voucher_abuse" || !candidate.VoucherCouponId.HasValue)
        {
            return;
        }

        if (existingAbuseCases.TryGetValue(candidate.ReferenceKey, out var abuseCase))
        {
            abuseCase.RiskCaseId = riskCase.RiskCaseId;
            abuseCase.CouponId = candidate.VoucherCouponId.Value;
            abuseCase.BuyerId = candidate.BuyerId;
            abuseCase.SellerId = candidate.SellerId;
            abuseCase.OrderId = candidate.OrderId;
            abuseCase.CampaignId = candidate.CampaignId;
            abuseCase.AbuseType = candidate.SignalCode;
            abuseCase.SuspectedBenefitAmount = candidate.SuspectedBenefitAmount;
            abuseCase.ReviewedAt = riskCase.ReviewedAt;
            if (abuseCase.Status is not ("resolved" or "dismissed"))
            {
                abuseCase.Status = riskCase.Status;
            }

            return;
        }

        abuseCase = new VoucherAbuseCase
        {
            ReferenceKey = candidate.ReferenceKey,
            RiskCaseId = riskCase.RiskCaseId,
            CouponId = candidate.VoucherCouponId.Value,
            BuyerId = candidate.BuyerId,
            SellerId = candidate.SellerId,
            OrderId = candidate.OrderId,
            CampaignId = candidate.CampaignId,
            AbuseType = candidate.SignalCode,
            SuspectedBenefitAmount = candidate.SuspectedBenefitAmount,
            Status = riskCase.Status,
            CreatedAt = DateTime.UtcNow,
            ReviewedAt = riskCase.ReviewedAt
        };

        _db.VoucherAbuseCases.Add(abuseCase);
        existingAbuseCases[candidate.ReferenceKey] = abuseCase;
    }

    private async Task<List<HeuristicRiskCandidate>> LoadVoucherAbuseCandidatesAsync(CancellationToken cancellationToken)
    {
        var since = DateTime.UtcNow.AddDays(-30);
        var couponCreators = await _db.Coupons
            .AsNoTracking()
            .ToDictionaryAsync(x => x.CouponId, x => x.CreatedBy, cancellationToken);

        var raw = await _db.CouponUsageHistories
            .AsNoTracking()
            .Where(x => x.UsedDate >= since)
            .GroupBy(x => new { x.CouponId, x.UserId })
            .Select(g => new
            {
                g.Key.CouponId,
                BuyerId = g.Key.UserId,
                UsageCount = g.Count(),
                TotalDiscount = g.Sum(x => x.DiscountAmount),
                LastUsedAt = g.Max(x => x.UsedDate),
                OrderId = g.Max(x => x.OrderId)
            })
            .Where(x => x.UsageCount >= 3 || x.TotalDiscount >= 300000m)
            .ToListAsync(cancellationToken);

        return raw.Select(x =>
        {
            var severity = x.UsageCount >= 5 || x.TotalDiscount >= 500000m ? "high" : "medium";
            var score = Math.Min(99m, (x.UsageCount * 12m) + (x.TotalDiscount / 50000m));
            var sellerId = couponCreators.TryGetValue(x.CouponId, out var createdBy) ? createdBy : null;
            var metadata = JsonSerializer.Serialize(new
            {
                x.CouponId,
                buyerId = x.BuyerId,
                x.UsageCount,
                x.TotalDiscount,
                windowDays = 30
            });

            return new HeuristicRiskCandidate
            {
                ReferenceKey = $"voucher-abuse:{x.CouponId}:{x.BuyerId}",
                CaseType = "voucher_abuse",
                Severity = severity,
                SellerId = sellerId,
                BuyerId = x.BuyerId,
                OrderId = x.OrderId,
                VoucherCouponId = x.CouponId,
                Title = $"Voucher abuse suspect for coupon #{x.CouponId}",
                Summary = $"Buyer #{x.BuyerId} da dung coupon {x.UsageCount} lan trong 30 ngay, tong discount {x.TotalDiscount:N0} đ.",
                SignalCount = x.UsageCount,
                LastSignalAt = x.LastUsedAt,
                SignalReferenceKey = $"signal:voucher-abuse:{x.CouponId}:{x.BuyerId}",
                SignalType = "voucher_abuse",
                SignalCode = "repeat_redemption",
                Score = score,
                MetadataJson = metadata,
                SuspectedBenefitAmount = x.TotalDiscount
            };
        }).ToList();
    }

    private async Task<List<HeuristicRiskCandidate>> LoadReturnSpikeCandidatesAsync(CancellationToken cancellationToken)
    {
        var since = DateTime.UtcNow.AddDays(-30);
        var raw = await _db.ReturnRequests
            .AsNoTracking()
            .Where(x => x.RequestedAt >= since)
            .GroupBy(x => x.SellerId)
            .Select(g => new
            {
                SellerId = g.Key,
                CaseCount = g.Count(),
                TotalRefundAmount = g.Sum(x => x.RefundAmount ?? 0m),
                LastRequestedAt = g.Max(x => x.RequestedAt)
            })
            .Where(x => x.CaseCount >= 4 || x.TotalRefundAmount >= 1000000m)
            .ToListAsync(cancellationToken);

        return raw.Select(x =>
        {
            var severity = x.CaseCount >= 6 || x.TotalRefundAmount >= 3000000m ? "high" : "medium";
            var score = Math.Min(99m, (x.CaseCount * 10m) + (x.TotalRefundAmount / 200000m));
            var metadata = JsonSerializer.Serialize(new
            {
                sellerId = x.SellerId,
                x.CaseCount,
                x.TotalRefundAmount,
                windowDays = 30
            });

            return new HeuristicRiskCandidate
            {
                ReferenceKey = $"return-spike:{x.SellerId}",
                CaseType = "return_spike",
                Severity = severity,
                SellerId = x.SellerId,
                Title = $"Return spike detected for seller #{x.SellerId}",
                Summary = $"Seller #{x.SellerId} co {x.CaseCount} return request trong 30 ngay, tong refund du kien {x.TotalRefundAmount:N0} đ.",
                SignalCount = x.CaseCount,
                LastSignalAt = x.LastRequestedAt,
                SignalReferenceKey = $"signal:return-spike:{x.SellerId}",
                SignalType = "return_spike",
                SignalCode = "return_volume_spike",
                Score = score,
                MetadataJson = metadata
            };
        }).ToList();
    }

    private int? TryGetActorUserId()
    {
        var subject = User.FindFirstValue(JwtRegisteredClaimNames.Sub) ?? User.FindFirstValue(ClaimTypes.NameIdentifier) ?? User.FindFirstValue("sub");
        return int.TryParse(subject, out var userId) ? userId : null;
    }

    private static string NormalizeOption(string? value, IEnumerable<string> allowed, string fallback)
    {
        var normalized = string.IsNullOrWhiteSpace(value) ? string.Empty : value.Trim().ToLowerInvariant();
        if (allowed.Contains(normalized))
        {
            return normalized;
        }

        return fallback;
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

    private static IEnumerable<object> BuildOptions(IEnumerable<string> values)
        => values.Select(value => new { value, text = FormatLabel(value) });

    private static string FormatLabel(string value)
        => value switch
        {
            "all" => "Tat ca",
            "voucher_abuse" => "Voucher abuse",
            "return_spike" => "Return spike",
            "pending_review" => "Pending review",
            "suspend_campaign" => "Suspend campaign",
            "suspend_seller" => "Suspend seller",
            "limit_coupon" => "Limit coupon",
            _ => string.Join(' ', value.Split('_', StringSplitOptions.RemoveEmptyEntries)
                .Select(part => char.ToUpperInvariant(part[0]) + part[1..]))
        };

    private sealed class HeuristicRiskCandidate
    {
        public string ReferenceKey { get; set; } = string.Empty;

        public string CaseType { get; set; } = string.Empty;

        public string Severity { get; set; } = string.Empty;

        public int? SellerId { get; set; }

        public int? BuyerId { get; set; }

        public int? OrderId { get; set; }

        public int? CampaignId { get; set; }

        public int? VoucherCouponId { get; set; }

        public string Title { get; set; } = string.Empty;

        public string Summary { get; set; } = string.Empty;

        public int SignalCount { get; set; }

        public DateTime? LastSignalAt { get; set; }

        public string SignalReferenceKey { get; set; } = string.Empty;

        public string SignalType { get; set; } = string.Empty;

        public string SignalCode { get; set; } = string.Empty;

        public decimal Score { get; set; }

        public string MetadataJson { get; set; } = string.Empty;

        public decimal? SuspectedBenefitAmount { get; set; }
    }

    private sealed class HeuristicSyncSummary
    {
        public int CaseCount { get; set; }

        public int SignalCount { get; set; }
    }

    public sealed class RiskDecisionRequest
    {
        public string DecisionType { get; set; } = string.Empty;

        public string? Notes { get; set; }
    }
}
