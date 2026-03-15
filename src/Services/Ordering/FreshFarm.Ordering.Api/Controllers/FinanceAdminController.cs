using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Text;
using System.Text.Json;
using FreshFarm.Ordering.Api.Models;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace FreshFarm.Ordering.Api.Controllers;

[ApiController]
[Route("api/orders/admin/finance")]
[Authorize(Policy = "SellerOrAdmin")]
public sealed class FinanceAdminController : ControllerBase
{
    private static readonly string[] PayoutPendingStatuses = ["pending", "queued", "scheduled", "processing", "waiting"];
    private static readonly string[] PayoutPaidStatuses = ["paid", "completed", "done", "success"];
    private static readonly string[] PayoutFailedStatuses = ["failed", "rejected", "cancelled", "canceled", "error"];

    private static readonly string[] RefundOpenStatuses = ["pending", "requested", "processing", "review"];
    private static readonly string[] RefundClosedStatuses = ["approved", "processed", "completed", "succeeded", "success", "paid"];
    private static readonly string[] RefundFailedStatuses = ["failed", "rejected", "cancelled", "canceled", "error"];

    private static readonly string[] ReturnOpenStatuses = ["pending", "requested", "review"];
    private static readonly string[] ReturnApprovedStatuses = ["approved", "accepted", "shipping", "shipping_back"];
    private static readonly string[] ReturnClosedStatuses = ["completed", "resolved", "refunded", "done"];
    private static readonly string[] ReturnRejectedStatuses = ["rejected", "cancelled", "canceled", "denied"];

    private readonly FreshFarmOrderingDBContext _db;

    public FinanceAdminController(FreshFarmOrderingDBContext db)
    {
        _db = db;
    }

    [HttpGet("console")]
    public async Task<IActionResult> GetConsole(
        [FromQuery] string? section = null,
        [FromQuery] string? q = null,
        [FromQuery] string? status = null,
        [FromQuery] string? followUpBucket = null,
        [FromQuery] int? sellerId = null,
        [FromQuery] int page = 1,
        [FromQuery] int pageSize = 12,
        CancellationToken cancellationToken = default)
    {
        var normalizedSection = NormalizeSection(section);
        var normalizedStatus = NormalizeStatus(status);
        var normalizedFollowUpBucket = NormalizeFollowUpBucket(followUpBucket);
        page = page < 1 ? 1 : page;
        pageSize = pageSize <= 0 || pageSize > 50 ? 12 : pageSize;

        if (User.IsInRole("Seller"))
        {
            sellerId = TryGetSellerIdFromToken();
            if (!sellerId.HasValue)
            {
                return Unauthorized(new { success = false, message = "Không xác định được seller hiện tại." });
            }
        }
        else if (sellerId <= 0)
        {
            sellerId = null;
        }

        var sellerOrdersQuery = BuildScopedSellerOrdersQuery(sellerId);
        if (sellerOrdersQuery is null)
        {
            return Unauthorized(new { success = false, message = "Không xác định được phạm vi tài chính." });
        }

        var payoutsQuery = BuildScopedPayoutsQuery(sellerId)!;
        var refundsQuery = BuildScopedRefundsQuery(sellerId)!;
        var returnsQuery = BuildScopedReturnsQuery(sellerId)!;
        var paymentTransactionsQuery = BuildScopedPaymentTransactionsQuery(sellerId)!;

        var grossMerchandiseValue = await sellerOrdersQuery.SumAsync(
            x => (decimal?)((x.SellerEarning) + (x.CommissionAmount)),
            cancellationToken) ?? 0m;
        var platformCommission = await sellerOrdersQuery.SumAsync(x => (decimal?)x.CommissionAmount, cancellationToken) ?? 0m;
        var capturedPayments = await paymentTransactionsQuery
            .Where(x => x.PaidAt.HasValue || PayoutPaidStatuses.Contains((x.Status ?? string.Empty).ToLower()))
            .SumAsync(x => (decimal?)x.Amount, cancellationToken) ?? 0m;
        var pendingPayoutAmount = await payoutsQuery
            .Where(x => PayoutPendingStatuses.Contains((x.Status ?? string.Empty).ToLower()))
            .SumAsync(x => (decimal?)(x.AmountNet ?? (x.AmountGross - x.FeeAmount)), cancellationToken) ?? 0m;
        var refundedAmount = await refundsQuery
            .Where(x => !RefundFailedStatuses.Contains((x.Status ?? string.Empty).ToLower()))
            .SumAsync(x => (decimal?)x.Amount, cancellationToken) ?? 0m;
        var openReturns = await returnsQuery
            .CountAsync(x =>
                !ReturnClosedStatuses.Contains((x.Status ?? string.Empty).ToLower()) &&
                !ReturnRejectedStatuses.Contains((x.Status ?? string.Empty).ToLower()),
                cancellationToken);
        var openRefunds = await refundsQuery
            .CountAsync(x => RefundOpenStatuses.Contains((x.Status ?? string.Empty).ToLower()), cancellationToken);
        var sellerCount = await sellerOrdersQuery.Select(x => x.SellerId).Distinct().CountAsync(cancellationToken);

        var payoutCount = await payoutsQuery.CountAsync(cancellationToken);
        var refundCount = await refundsQuery.CountAsync(cancellationToken);
        var returnCount = await returnsQuery.CountAsync(cancellationToken);

        var sellerOptions = User.IsInRole("Admin")
            ? await sellerOrdersQuery
                .Select(x => x.SellerId)
                .Distinct()
                .OrderBy(x => x)
                .Take(200)
                .Select(x => new { value = x.ToString(), text = $"Seller #{x}" })
                .ToListAsync(cancellationToken)
            : [];

        var total = 0;
        var totalPages = 1;
        object rows;
        object ownerSummary;
        var overdueFollowUps = 0;
        var dueSoonFollowUps = 0;
        var noFollowUpCount = 0;

        switch (normalizedSection)
        {
            case "refunds":
                {
                    var result = await BuildRefundRowsAsync(refundsQuery, q, normalizedStatus, normalizedFollowUpBucket, page, pageSize, cancellationToken);
                    rows = result.Rows;
                    ownerSummary = result.OwnerSummary;
                    total = result.Total;
                    overdueFollowUps = result.OverdueFollowUps;
                    dueSoonFollowUps = result.DueSoonFollowUps;
                    noFollowUpCount = result.NoFollowUpCount;
                }
                break;
            case "returns":
                {
                    var result = await BuildReturnRowsAsync(returnsQuery, q, normalizedStatus, normalizedFollowUpBucket, page, pageSize, cancellationToken);
                    rows = result.Rows;
                    ownerSummary = result.OwnerSummary;
                    total = result.Total;
                    overdueFollowUps = result.OverdueFollowUps;
                    dueSoonFollowUps = result.DueSoonFollowUps;
                    noFollowUpCount = result.NoFollowUpCount;
                }
                break;
            default:
                {
                    var result = await BuildPayoutRowsAsync(payoutsQuery, q, normalizedStatus, normalizedFollowUpBucket, page, pageSize, cancellationToken);
                    rows = result.Rows;
                    ownerSummary = result.OwnerSummary;
                    total = result.Total;
                    overdueFollowUps = result.OverdueFollowUps;
                    dueSoonFollowUps = result.DueSoonFollowUps;
                    noFollowUpCount = result.NoFollowUpCount;
                }
                break;
        }

        totalPages = total <= 0 ? 1 : (int)Math.Ceiling(total / (double)pageSize);
        if (page > totalPages)
        {
            page = totalPages;
            switch (normalizedSection)
            {
                case "refunds":
                    rows = (await BuildRefundRowsAsync(refundsQuery, q, normalizedStatus, normalizedFollowUpBucket, page, pageSize, cancellationToken)).Rows;
                    break;
                case "returns":
                    rows = (await BuildReturnRowsAsync(returnsQuery, q, normalizedStatus, normalizedFollowUpBucket, page, pageSize, cancellationToken)).Rows;
                    break;
                default:
                    rows = (await BuildPayoutRowsAsync(payoutsQuery, q, normalizedStatus, normalizedFollowUpBucket, page, pageSize, cancellationToken)).Rows;
                    break;
            }
        }

        return Ok(new
        {
            scope = User.IsInRole("Admin") ? "Admin" : "Seller",
            section = normalizedSection,
            page,
            pageSize,
            total,
            totalPages,
            stats = new
            {
                grossMerchandiseValue,
                capturedPayments,
                platformCommission,
                pendingPayoutAmount,
                refundedAmount,
                openReturns,
                openRefunds,
                sellerCount,
                overdueFollowUps,
                dueSoonFollowUps,
                noFollowUpCount
            },
            sectionCounts = new
            {
                payouts = payoutCount,
                refunds = refundCount,
                returns = returnCount
            },
            filters = new
            {
                q = q ?? string.Empty,
                status = normalizedStatus,
                followUpBucket = normalizedFollowUpBucket,
                sellerId,
                sellerOptions,
                statusOptions = BuildStatusOptions(normalizedSection)
            },
            ownerSummary,
            rows
        });
    }

    [HttpPost("actions")]
    [Authorize(Policy = "AdminOnly")]
    public async Task<IActionResult> TakeAction([FromBody] FinanceActionRequest? request, CancellationToken cancellationToken = default)
    {
        if (request is null || request.RecordId <= 0)
        {
            return BadRequest(new { success = false, message = "Payload tai chinh khong hop le." });
        }

        var section = NormalizeSection(request.Section);
        var actionName = NormalizeFinanceAction(request.ActionName);
        var note = TrimNote(request.Note);
        var assigneeLabel = TrimShortText(request.AssigneeLabel, 120);
        var followUpAt = NormalizeFollowUpAt(request.FollowUpAt);
        if (actionName == "all")
        {
            return BadRequest(new { success = false, message = "Action tai chinh khong hop le." });
        }

        var managementResult = await HandleFinanceManagementActionAsync(section, request.RecordId, actionName, note, assigneeLabel, followUpAt, cancellationToken);
        if (managementResult is not null)
        {
            return managementResult;
        }

        return section switch
        {
            "refunds" => await HandleRefundActionAsync(request.RecordId, actionName, note, cancellationToken),
            "returns" => await HandleReturnActionAsync(request.RecordId, actionName, note, cancellationToken),
            _ => await HandlePayoutActionAsync(request.RecordId, actionName, note, cancellationToken)
        };
    }

    [HttpPost("bulk-actions")]
    [Authorize(Policy = "AdminOnly")]
    public async Task<IActionResult> TakeBulkAction([FromBody] FinanceBulkActionRequest? request, CancellationToken cancellationToken = default)
    {
        if (request?.RecordIds is null || request.RecordIds.Count == 0)
        {
            return BadRequest(new { success = false, message = "Can chon it nhat mot ban ghi de xu ly hang loat." });
        }

        var section = NormalizeSection(request.Section);
        var actionName = NormalizeFinanceAction(request.ActionName);
        var note = TrimNote(request.Note);
        var assigneeLabel = TrimShortText(request.AssigneeLabel, 120);
        var followUpAt = NormalizeFollowUpAt(request.FollowUpAt);
        var recordIds = request.RecordIds.Where(x => x > 0).Distinct().Take(100).ToList();
        if (actionName == "all" || recordIds.Count == 0)
        {
            return BadRequest(new { success = false, message = "Payload bulk action khong hop le." });
        }

        var successCount = 0;
        var failedIds = new List<int>();
        foreach (var recordId in recordIds)
        {
            var handled = await ApplyFinanceActionAsync(section, recordId, actionName, note, assigneeLabel, followUpAt, cancellationToken);

            if (handled)
            {
                successCount++;
            }
            else
            {
                failedIds.Add(recordId);
            }
        }

        if (successCount == 0)
        {
            return BadRequest(new { success = false, message = "Khong co ban ghi nao duoc cap nhat trong lo xu ly nay.", failedIds });
        }

        return Ok(new
        {
            success = true,
            message = $"Da {GetFinanceActionLabel(actionName)} {successCount}/{recordIds.Count} ban ghi.",
            failedIds
        });
    }

    [HttpGet("export")]
    [Authorize(Policy = "AdminOnly")]
    public async Task<IActionResult> Export(
        [FromQuery] string? section = null,
        [FromQuery] string? q = null,
        [FromQuery] string? status = null,
        [FromQuery] int? sellerId = null,
        CancellationToken cancellationToken = default)
    {
        var normalizedSection = NormalizeSection(section);
        var normalizedStatus = NormalizeStatus(status);
        sellerId = sellerId > 0 ? sellerId : null;

        var builder = new StringBuilder();
        builder.AppendLine("Section,RecordId,OrderId,SellerLabel,AmountGross,FeeAmount,AmountNet,RefundAmount,Status,ReconciliationStatus,NextStep,AssignedOwner,FollowUpAt,ReferenceCode,CreatedAt,ProcessedAt,PaidAt");

        switch (normalizedSection)
        {
            case "refunds":
                {
                    var rows = (await BuildRefundRowsAsync(BuildScopedRefundsQuery(sellerId)!, q, normalizedStatus, "all", 1, 500, cancellationToken)).Rows;
                    foreach (var row in rows)
                    {
                        AppendCsvRow(builder, normalizedSection, row);
                    }
                    break;
                }
            case "returns":
                {
                    var rows = (await BuildReturnRowsAsync(BuildScopedReturnsQuery(sellerId)!, q, normalizedStatus, "all", 1, 500, cancellationToken)).Rows;
                    foreach (var row in rows)
                    {
                        AppendCsvRow(builder, normalizedSection, row);
                    }
                    break;
                }
            default:
                {
                    var rows = (await BuildPayoutRowsAsync(BuildScopedPayoutsQuery(sellerId)!, q, normalizedStatus, "all", 1, 500, cancellationToken)).Rows;
                    foreach (var row in rows)
                    {
                        AppendCsvRow(builder, normalizedSection, row);
                    }
                    break;
                }
        }

        var fileName = $"finance-{normalizedSection}-{DateTime.UtcNow:yyyyMMdd-HHmmss}.csv";
        return File(Encoding.UTF8.GetBytes(builder.ToString()), "text/csv; charset=utf-8", fileName);
    }

    private async Task<IActionResult?> HandleFinanceManagementActionAsync(string section, int recordId, string actionName, string? note, string? assigneeLabel, DateTime? followUpAt, CancellationToken cancellationToken)
    {
        if (actionName is not ("assign" or "mark-exported" or "set-follow-up" or "send-reminder"))
        {
            return null;
        }

        if (actionName == "assign" && string.IsNullOrWhiteSpace(assigneeLabel))
        {
            return BadRequest(new { success = false, message = "Can nhap nguoi phu trach truoc khi gan queue tai chinh." });
        }

        if (actionName == "set-follow-up" && !followUpAt.HasValue)
        {
            return BadRequest(new { success = false, message = "Can chon moc follow-up truoc khi cap nhat doi soat." });
        }

        var handled = await ApplyFinanceActionAsync(section, recordId, actionName, note, assigneeLabel, followUpAt, cancellationToken);
        if (!handled)
        {
            return NotFound(new { success = false, message = "Khong tim thay ban ghi tai chinh." });
        }

        var message = actionName switch
        {
            "assign" => $"Da gan queue tai chinh #{recordId} cho {assigneeLabel}.",
            "set-follow-up" => $"Da cap nhat follow-up doi soat cho ban ghi #{recordId}.",
            "send-reminder" => $"Da nhac owner doi soat cho ban ghi #{recordId}.",
            _ => $"Da danh dau ban ghi #{recordId} da xuat doi soat."
        };
        return Ok(new { success = true, message });
    }

    private async Task<bool> ApplyFinanceActionAsync(string section, int recordId, string actionName, string? note, string? assigneeLabel, DateTime? followUpAt, CancellationToken cancellationToken)
    {
        if (actionName is not ("assign" or "mark-exported" or "set-follow-up" or "send-reminder"))
        {
            return section switch
            {
                "refunds" => await ApplyRefundActionAsync(recordId, actionName, note, cancellationToken),
                "returns" => await ApplyReturnActionAsync(recordId, actionName, note, cancellationToken),
                _ => await ApplyPayoutActionAsync(recordId, actionName, note, cancellationToken)
            };
        }

        var targetType = section switch
        {
            "refunds" => "refund",
            "returns" => "return",
            _ => "payout"
        };

        var exists = section switch
        {
            "refunds" => await _db.RefundTransactions.AnyAsync(x => x.RefundId == recordId, cancellationToken),
            "returns" => await _db.ReturnRequests.AnyAsync(x => x.ReturnId == recordId, cancellationToken),
            _ => await _db.Payouts.AnyAsync(x => x.PayoutId == recordId, cancellationToken)
        };

        if (!exists)
        {
            return false;
        }

        var actorUserId = TryGetSellerIdFromToken();
        var reminderSentAt = actionName == "send-reminder" ? DateTime.UtcNow : (DateTime?)null;
        var actionLog = AdminAuditLogger.AddAction(
            _db,
            "finance_console",
            $"{targetType}_{actionName}",
            targetType,
            recordId,
                actionName switch
                {
                    "assign" => $"Gan queue tai chinh {targetType} #{recordId} cho {assigneeLabel}.",
                    "set-follow-up" => $"Cap nhat follow-up doi soat cho {targetType} #{recordId} vao {followUpAt:yyyy-MM-dd HH:mm}.",
                    "send-reminder" => $"Nhac owner doi soat cho {targetType} #{recordId}.",
                    _ => $"Danh dau {targetType} #{recordId} da xuat doi soat."
                },
            actorUserId,
            new
            {
                section,
                actionName,
                note,
                assignedOwner = assigneeLabel,
                exportedAt = actionName == "mark-exported" ? DateTime.UtcNow : (DateTime?)null,
                followUpAt,
                followUpNote = actionName == "set-follow-up" ? note : null,
                reminderSentAt,
                reminderNote = actionName == "send-reminder" ? note : null
            });
        AdminAuditLogger.AddSettlement(_db, actionLog, $"{targetType}_{actionName}", targetType, recordId, null, null, note, actorUserId);

        await _db.SaveChangesAsync(cancellationToken);
        return true;
    }

    private IQueryable<SellerOrder>? BuildScopedSellerOrdersQuery(int? sellerId)
    {
        var query = _db.SellerOrders.AsNoTracking().AsQueryable();

        if (User.IsInRole("Admin"))
        {
            return sellerId.HasValue ? query.Where(x => x.SellerId == sellerId.Value) : query;
        }

        var scopedSellerId = TryGetSellerIdFromToken();
        return scopedSellerId.HasValue ? query.Where(x => x.SellerId == scopedSellerId.Value) : null;
    }

    private IQueryable<Payout>? BuildScopedPayoutsQuery(int? sellerId)
    {
        var query = _db.Payouts.AsNoTracking().AsQueryable();

        if (User.IsInRole("Admin"))
        {
            return sellerId.HasValue ? query.Where(x => x.SellerId == sellerId.Value) : query;
        }

        var scopedSellerId = TryGetSellerIdFromToken();
        return scopedSellerId.HasValue ? query.Where(x => x.SellerId == scopedSellerId.Value) : null;
    }

    private IQueryable<RefundTransaction>? BuildScopedRefundsQuery(int? sellerId)
    {
        var query = _db.RefundTransactions
            .AsNoTracking()
            .Where(x => x.PaymentTxn != null && x.PaymentTxn.Order != null && x.PaymentTxn.Order.SellerOrders.Any());

        if (User.IsInRole("Admin"))
        {
            return sellerId.HasValue
                ? query.Where(x => x.PaymentTxn.Order.SellerOrders.Any(so => so.SellerId == sellerId.Value))
                : query;
        }

        var scopedSellerId = TryGetSellerIdFromToken();
        return scopedSellerId.HasValue
            ? query.Where(x => x.PaymentTxn.Order.SellerOrders.Any(so => so.SellerId == scopedSellerId.Value))
            : null;
    }

    private IQueryable<ReturnRequest>? BuildScopedReturnsQuery(int? sellerId)
    {
        var query = _db.ReturnRequests.AsNoTracking().AsQueryable();

        if (User.IsInRole("Admin"))
        {
            return sellerId.HasValue ? query.Where(x => x.SellerId == sellerId.Value) : query;
        }

        var scopedSellerId = TryGetSellerIdFromToken();
        return scopedSellerId.HasValue ? query.Where(x => x.SellerId == scopedSellerId.Value) : null;
    }

    private IQueryable<PaymentTransaction>? BuildScopedPaymentTransactionsQuery(int? sellerId)
    {
        var query = _db.PaymentTransactions
            .AsNoTracking()
            .Where(x => x.Order != null && x.Order.SellerOrders.Any());

        if (User.IsInRole("Admin"))
        {
            return sellerId.HasValue
                ? query.Where(x => x.Order.SellerOrders.Any(so => so.SellerId == sellerId.Value))
                : query;
        }

        var scopedSellerId = TryGetSellerIdFromToken();
        return scopedSellerId.HasValue
            ? query.Where(x => x.Order.SellerOrders.Any(so => so.SellerId == scopedSellerId.Value))
            : null;
    }

    private async Task<IActionResult> HandlePayoutActionAsync(int payoutId, string actionName, string? note, CancellationToken cancellationToken)
    {
        if (!await ApplyPayoutActionAsync(payoutId, actionName, note, cancellationToken))
        {
            return NotFound(new { success = false, message = "Khong tim thay payout." });
        }

        return Ok(new { success = true, message = $"Da {GetFinanceActionLabel(actionName)} payout #{payoutId}." });
    }

    private async Task<bool> ApplyPayoutActionAsync(int payoutId, string actionName, string? note, CancellationToken cancellationToken)
    {
        var payout = await _db.Payouts.FirstOrDefaultAsync(x => x.PayoutId == payoutId, cancellationToken);
        if (payout is null)
        {
            return false;
        }

        switch (actionName)
        {
            case "acknowledge":
                payout.Status = "processing";
                break;
            case "release":
                payout.Status = "paid";
                payout.PaidAt = DateTime.UtcNow;
                break;
            case "hold":
                payout.Status = "queued";
                break;
            case "reject":
                payout.Status = "failed";
                break;
            default:
                return false;
        }

        var actorUserId = TryGetSellerIdFromToken();
        var actionLog = AdminAuditLogger.AddAction(
            _db,
            "finance_console",
            $"payout_{actionName}",
            "payout",
            payoutId,
            $"Admin {GetFinanceActionLabel(actionName)} payout #{payoutId}.",
            actorUserId,
            new { section = "payouts", actionName, note, payout.SellerId, payout.AmountNet, payout.AmountGross });
        AdminAuditLogger.AddSettlement(_db, actionLog, $"payout_{actionName}", "payout", payoutId, payout.SellerId, payout.AmountNet ?? (payout.AmountGross - payout.FeeAmount), note, actorUserId);

        await _db.SaveChangesAsync(cancellationToken);
        return true;
    }

    private async Task<IActionResult> HandleRefundActionAsync(int refundId, string actionName, string? note, CancellationToken cancellationToken)
    {
        if (!await ApplyRefundActionAsync(refundId, actionName, note, cancellationToken))
        {
            return NotFound(new { success = false, message = "Khong tim thay refund." });
        }

        return Ok(new { success = true, message = $"Da {GetFinanceActionLabel(actionName)} refund #{refundId}." });
    }

    private async Task<bool> ApplyRefundActionAsync(int refundId, string actionName, string? note, CancellationToken cancellationToken)
    {
        var refund = await _db.RefundTransactions.FirstOrDefaultAsync(x => x.RefundId == refundId, cancellationToken);
        if (refund is null)
        {
            return false;
        }

        switch (actionName)
        {
            case "acknowledge":
                refund.Status = "review";
                break;
            case "approve":
                refund.Status = "approved";
                refund.ProcessedAt ??= DateTime.UtcNow;
                break;
            case "reject":
                refund.Status = "rejected";
                refund.ProcessedAt = DateTime.UtcNow;
                break;
            case "resolve":
                refund.Status = "processed";
                refund.ProcessedAt = DateTime.UtcNow;
                break;
            default:
                return false;
        }

        var actorUserId = TryGetSellerIdFromToken();
        var actionLog = AdminAuditLogger.AddAction(
            _db,
            "finance_console",
            $"refund_{actionName}",
            "refund",
            refundId,
            $"Admin {GetFinanceActionLabel(actionName)} refund #{refundId}.",
            actorUserId,
            new { section = "refunds", actionName, note, refund.Amount, refund.ReferenceCode });
        AdminAuditLogger.AddSettlement(_db, actionLog, $"refund_{actionName}", "refund", refundId, null, refund.Amount, note, actorUserId);

        await _db.SaveChangesAsync(cancellationToken);
        return true;
    }

    private async Task<IActionResult> HandleReturnActionAsync(int returnId, string actionName, string? note, CancellationToken cancellationToken)
    {
        if (!await ApplyReturnActionAsync(returnId, actionName, note, cancellationToken))
        {
            return NotFound(new { success = false, message = "Khong tim thay return." });
        }

        return Ok(new { success = true, message = $"Da {GetFinanceActionLabel(actionName)} return #{returnId}." });
    }

    private async Task<bool> ApplyReturnActionAsync(int returnId, string actionName, string? note, CancellationToken cancellationToken)
    {
        var returnRequest = await _db.ReturnRequests.FirstOrDefaultAsync(x => x.ReturnId == returnId, cancellationToken);
        if (returnRequest is null)
        {
            return false;
        }

        switch (actionName)
        {
            case "acknowledge":
                returnRequest.Status = "review";
                break;
            case "approve":
                returnRequest.Status = "approved";
                returnRequest.ApprovedAt ??= DateTime.UtcNow;
                if (!string.IsNullOrWhiteSpace(note))
                {
                    returnRequest.Resolution = note;
                }
                break;
            case "reject":
                returnRequest.Status = "rejected";
                returnRequest.CompletedAt = DateTime.UtcNow;
                returnRequest.Resolution = string.IsNullOrWhiteSpace(note) ? "Rejected by finance console." : note;
                break;
            case "resolve":
                returnRequest.Status = "resolved";
                returnRequest.ApprovedAt ??= DateTime.UtcNow;
                returnRequest.CompletedAt = DateTime.UtcNow;
                if (!string.IsNullOrWhiteSpace(note))
                {
                    returnRequest.Resolution = note;
                }
                break;
            default:
                return false;
        }

        var actorUserId = TryGetSellerIdFromToken();
        var actionLog = AdminAuditLogger.AddAction(
            _db,
            "finance_console",
            $"return_{actionName}",
            "return",
            returnId,
            $"Admin {GetFinanceActionLabel(actionName)} return #{returnId}.",
            actorUserId,
            new { section = "returns", actionName, note, returnRequest.SellerId, returnRequest.RefundAmount });
        AdminAuditLogger.AddSettlement(_db, actionLog, $"return_{actionName}", "return", returnId, returnRequest.SellerId, returnRequest.RefundAmount, note, actorUserId);

        await _db.SaveChangesAsync(cancellationToken);
        return true;
    }

    private async Task<int> CountPayoutRowsAsync(
        IQueryable<Payout> query,
        string? q,
        string status,
        CancellationToken cancellationToken)
    {
        return await ApplyPayoutFilters(query, q, status).CountAsync(cancellationToken);
    }

    private async Task<int> CountRefundRowsAsync(
        IQueryable<RefundTransaction> query,
        string? q,
        string status,
        CancellationToken cancellationToken)
    {
        return await ApplyRefundFilters(query, q, status).CountAsync(cancellationToken);
    }

    private async Task<int> CountReturnRowsAsync(
        IQueryable<ReturnRequest> query,
        string? q,
        string status,
        CancellationToken cancellationToken)
    {
        return await ApplyReturnFilters(query, q, status).CountAsync(cancellationToken);
    }

    private async Task<FinanceConsoleSectionResult> BuildPayoutRowsAsync(
        IQueryable<Payout> query,
        string? q,
        string status,
        string followUpBucket,
        int page,
        int pageSize,
        CancellationToken cancellationToken)
    {
        var rows = await ApplyPayoutFilters(query, q, status)
            .OrderByDescending(x => x.CreatedAt)
            .ThenByDescending(x => x.PayoutId)
            .Select(x => new FinanceConsoleRowPayload
            {
                RecordId = x.PayoutId,
                OrderId = (int?)null,
                SellerId = (int?)x.SellerId,
                SellerLabel = $"Seller #{x.SellerId}",
                OrderCount = x.PayoutItems.Count,
                AmountGross = x.AmountGross,
                FeeAmount = x.FeeAmount,
                AmountNet = x.AmountNet ?? (x.AmountGross - x.FeeAmount),
                RefundAmount = (decimal?)null,
                Status = x.Status ?? string.Empty,
                Method = x.PaymentTxn != null ? x.PaymentTxn.Method ?? string.Empty : string.Empty,
                Provider = x.PaymentTxn != null ? x.PaymentTxn.Provider ?? string.Empty : string.Empty,
                ReferenceCode = x.PaymentTxn != null ? x.PaymentTxn.ProviderRef ?? string.Empty : string.Empty,
                ReasonCode = string.Empty,
                Resolution = string.Empty,
                ItemName = string.Empty,
                CreatedAt = x.CreatedAt,
                ScheduledAt = x.ScheduledAt,
                ProcessedAt = (DateTime?)null,
                PaidAt = x.PaidAt,
                ReconciliationStatus = GetPayoutReconciliationStatus(x.Status, x.PaidAt),
                NextStep = GetPayoutNextStep(x.Status, x.ScheduledAt, x.PaidAt)
            })
            .ToListAsync(cancellationToken);

        await EnrichFinanceRowsAsync("payout", rows, cancellationToken);
        return FinalizeFinanceRows(rows, followUpBucket, page, pageSize);
    }

    private async Task<FinanceConsoleSectionResult> BuildRefundRowsAsync(
        IQueryable<RefundTransaction> query,
        string? q,
        string status,
        string followUpBucket,
        int page,
        int pageSize,
        CancellationToken cancellationToken)
    {
        var rows = await ApplyRefundFilters(query, q, status)
            .OrderByDescending(x => x.CreatedAt)
            .ThenByDescending(x => x.RefundId)
            .Select(x => new FinanceConsoleRowPayload
            {
                RecordId = x.RefundId,
                OrderId = (int?)x.PaymentTxn.OrderId,
                SellerId = (int?)null,
                SellerLabel = string.Empty,
                OrderCount = (int?)null,
                AmountGross = x.PaymentTxn.Amount,
                FeeAmount = x.PaymentTxn.FeeAmount,
                AmountNet = (decimal?)null,
                RefundAmount = (decimal?)x.Amount,
                Status = x.Status ?? string.Empty,
                Method = x.PaymentTxn.Method ?? string.Empty,
                Provider = x.Channel ?? x.PaymentTxn.Provider ?? string.Empty,
                ReferenceCode = x.ReferenceCode ?? x.PaymentTxn.ProviderRef ?? string.Empty,
                ReasonCode = x.PaymentTxn.FailureCode ?? string.Empty,
                Resolution = x.PaymentTxn.FailureMessage ?? string.Empty,
                ItemName = string.Empty,
                CreatedAt = x.CreatedAt,
                ScheduledAt = (DateTime?)null,
                ProcessedAt = x.ProcessedAt,
                PaidAt = x.PaymentTxn.PaidAt,
                ReconciliationStatus = GetRefundReconciliationStatus(x.Status, x.ProcessedAt),
                NextStep = GetRefundNextStep(x.Status, x.ProcessedAt)
            })
            .ToListAsync(cancellationToken);

        var orderIds = rows.Where(x => x.OrderId.HasValue).Select(x => x.OrderId!.Value).Distinct().ToList();
        var sellerLookup = await BuildOrderSellerLookupAsync(orderIds, cancellationToken);

        foreach (var row in rows)
        {
            if (sellerLookup.TryGetValue(row.OrderId ?? 0, out var sellerMeta))
            {
                row.SellerId = sellerMeta.SellerId;
                row.SellerLabel = sellerMeta.SellerLabel;
            }
        }

        await EnrichFinanceRowsAsync("refund", rows, cancellationToken);
        return FinalizeFinanceRows(rows, followUpBucket, page, pageSize);
    }

    private async Task<FinanceConsoleSectionResult> BuildReturnRowsAsync(
        IQueryable<ReturnRequest> query,
        string? q,
        string status,
        string followUpBucket,
        int page,
        int pageSize,
        CancellationToken cancellationToken)
    {
        var rows = await ApplyReturnFilters(query, q, status)
            .OrderByDescending(x => x.RequestedAt)
            .ThenByDescending(x => x.ReturnId)
            .Select(x => new FinanceConsoleRowPayload
            {
                RecordId = x.ReturnId,
                OrderId = (int?)x.SellerOrderItem.SellerOrder.OrderId,
                SellerId = (int?)x.SellerId,
                SellerLabel = $"Seller #{x.SellerId}",
                OrderCount = (int?)null,
                AmountGross = x.SellerOrderItem.FinalAmount ?? (x.SellerOrderItem.UnitPrice * x.SellerOrderItem.Quantity),
                FeeAmount = (decimal?)null,
                AmountNet = (decimal?)null,
                RefundAmount = x.RefundAmount,
                Status = x.Status ?? string.Empty,
                Method = string.Empty,
                Provider = string.Empty,
                ReferenceCode = string.Empty,
                ReasonCode = x.ReasonCode ?? string.Empty,
                Resolution = x.Resolution ?? string.Empty,
                ItemName = x.SellerOrderItem.SnapshotName ?? string.Empty,
                CreatedAt = x.RequestedAt,
                ScheduledAt = x.ApprovedAt,
                ProcessedAt = x.CompletedAt,
                PaidAt = (DateTime?)null,
                ReconciliationStatus = GetReturnReconciliationStatus(x.Status, x.CompletedAt),
                NextStep = GetReturnNextStep(x.Status, x.ApprovedAt, x.CompletedAt)
            })
            .ToListAsync(cancellationToken);

        await EnrichFinanceRowsAsync("return", rows, cancellationToken);
        return FinalizeFinanceRows(rows, followUpBucket, page, pageSize);
    }

    private IQueryable<Payout> ApplyPayoutFilters(IQueryable<Payout> query, string? q, string status)
    {
        if (!string.Equals(status, "all", StringComparison.OrdinalIgnoreCase))
        {
            query = status switch
            {
                "pending" => query.Where(x => PayoutPendingStatuses.Contains((x.Status ?? string.Empty).ToLower())),
                "paid" => query.Where(x => PayoutPaidStatuses.Contains((x.Status ?? string.Empty).ToLower())),
                "failed" => query.Where(x => PayoutFailedStatuses.Contains((x.Status ?? string.Empty).ToLower())),
                _ => query
            };
        }

        if (string.IsNullOrWhiteSpace(q))
        {
            return query;
        }

        var term = q.Trim();
        var isNumeric = int.TryParse(term, out var numericId);
        return query.Where(x =>
            (!string.IsNullOrWhiteSpace(x.Status) && x.Status.Contains(term)) ||
            (x.PaymentTxn != null && !string.IsNullOrWhiteSpace(x.PaymentTxn.ProviderRef) && x.PaymentTxn.ProviderRef.Contains(term)) ||
            (x.PaymentTxn != null && !string.IsNullOrWhiteSpace(x.PaymentTxn.Provider) && x.PaymentTxn.Provider.Contains(term)) ||
            (x.PaymentTxn != null && !string.IsNullOrWhiteSpace(x.PaymentTxn.Method) && x.PaymentTxn.Method.Contains(term)) ||
            (isNumeric && (x.PayoutId == numericId || x.SellerId == numericId || x.PaymentTxnId == numericId)));
    }

    private IQueryable<RefundTransaction> ApplyRefundFilters(IQueryable<RefundTransaction> query, string? q, string status)
    {
        if (!string.Equals(status, "all", StringComparison.OrdinalIgnoreCase))
        {
            query = status switch
            {
                "pending" => query.Where(x => RefundOpenStatuses.Contains((x.Status ?? string.Empty).ToLower())),
                "processed" => query.Where(x => RefundClosedStatuses.Contains((x.Status ?? string.Empty).ToLower())),
                "failed" => query.Where(x => RefundFailedStatuses.Contains((x.Status ?? string.Empty).ToLower())),
                _ => query
            };
        }

        if (string.IsNullOrWhiteSpace(q))
        {
            return query;
        }

        var term = q.Trim();
        var isNumeric = int.TryParse(term, out var numericId);
        return query.Where(x =>
            (!string.IsNullOrWhiteSpace(x.Status) && x.Status.Contains(term)) ||
            (!string.IsNullOrWhiteSpace(x.ReferenceCode) && x.ReferenceCode.Contains(term)) ||
            (!string.IsNullOrWhiteSpace(x.Channel) && x.Channel.Contains(term)) ||
            (x.PaymentTxn != null && !string.IsNullOrWhiteSpace(x.PaymentTxn.ProviderRef) && x.PaymentTxn.ProviderRef.Contains(term)) ||
            (isNumeric && (
                x.RefundId == numericId ||
                x.PaymentTxnId == numericId ||
                (x.PaymentTxn != null && x.PaymentTxn.OrderId == numericId))));
    }

    private IQueryable<ReturnRequest> ApplyReturnFilters(IQueryable<ReturnRequest> query, string? q, string status)
    {
        if (!string.Equals(status, "all", StringComparison.OrdinalIgnoreCase))
        {
            query = status switch
            {
                "pending" => query.Where(x => ReturnOpenStatuses.Contains((x.Status ?? string.Empty).ToLower())),
                "approved" => query.Where(x => ReturnApprovedStatuses.Contains((x.Status ?? string.Empty).ToLower())),
                "completed" => query.Where(x => ReturnClosedStatuses.Contains((x.Status ?? string.Empty).ToLower())),
                "rejected" => query.Where(x => ReturnRejectedStatuses.Contains((x.Status ?? string.Empty).ToLower())),
                _ => query
            };
        }

        if (string.IsNullOrWhiteSpace(q))
        {
            return query;
        }

        var term = q.Trim();
        var isNumeric = int.TryParse(term, out var numericId);
        return query.Where(x =>
            (!string.IsNullOrWhiteSpace(x.Status) && x.Status.Contains(term)) ||
            (!string.IsNullOrWhiteSpace(x.ReasonCode) && x.ReasonCode.Contains(term)) ||
            (!string.IsNullOrWhiteSpace(x.Description) && x.Description.Contains(term)) ||
            (x.SellerOrderItem != null && !string.IsNullOrWhiteSpace(x.SellerOrderItem.SnapshotName) && x.SellerOrderItem.SnapshotName.Contains(term)) ||
            (isNumeric && (
                x.ReturnId == numericId ||
                x.SellerId == numericId ||
                (x.SellerOrderItem != null && x.SellerOrderItem.SellerOrder != null && x.SellerOrderItem.SellerOrder.OrderId == numericId))));
    }

    private async Task<Dictionary<int, SellerLookupRow>> BuildOrderSellerLookupAsync(
        IReadOnlyCollection<int> orderIds,
        CancellationToken cancellationToken)
    {
        if (orderIds.Count == 0)
        {
            return new Dictionary<int, SellerLookupRow>();
        }

        var rows = await _db.SellerOrders
            .AsNoTracking()
            .Where(x => orderIds.Contains(x.OrderId))
            .Select(x => new { x.OrderId, x.SellerId })
            .ToListAsync(cancellationToken);

        return rows
            .GroupBy(x => x.OrderId)
            .ToDictionary(
                g => g.Key,
                g =>
                {
                    var sellerIds = g.Select(x => x.SellerId).Distinct().OrderBy(x => x).ToList();
                    return new SellerLookupRow(
                        sellerIds.Count == 1 ? sellerIds[0] : null,
                        FormatSellerLabel(sellerIds));
                });
    }

    private async Task EnrichFinanceRowsAsync(string targetType, List<FinanceConsoleRowPayload> rows, CancellationToken cancellationToken)
    {
        if (rows.Count == 0)
        {
            return;
        }

        var recordIds = rows.Select(x => x.RecordId).Distinct().ToList();
        var logs = await _db.AdminActionLogs
            .AsNoTracking()
            .Where(x => x.Area == "finance_console" && x.TargetType == targetType && x.TargetId.HasValue && recordIds.Contains(x.TargetId.Value))
            .OrderByDescending(x => x.CreatedAt)
            .ToListAsync(cancellationToken);

        var snapshots = logs
            .GroupBy(x => x.TargetId!.Value)
            .ToDictionary(g => g.Key, BuildFinanceOpsSnapshot);

        foreach (var row in rows)
        {
            if (!snapshots.TryGetValue(row.RecordId, out var snapshot))
            {
                continue;
            }

            row.AssignedOwner = snapshot.AssignedOwner ?? string.Empty;
            row.ExportedAt = snapshot.ExportedAt;
            row.FollowUpAt = snapshot.FollowUpAt;
            row.FollowUpNote = snapshot.FollowUpNote ?? string.Empty;
            row.ReminderSentAt = snapshot.ReminderSentAt;
            row.ReminderNote = snapshot.ReminderNote ?? string.Empty;
            row.LastActionSummary = snapshot.LastActionSummary ?? string.Empty;
        }
    }

    private static FinanceConsoleSectionResult FinalizeFinanceRows(
        List<FinanceConsoleRowPayload> rows,
        string followUpBucket,
        int page,
        int pageSize)
    {
        var now = DateTime.UtcNow;
        var dueSoonThreshold = now.AddDays(2);

        var overdueFollowUps = rows.Count(x => x.FollowUpAt.HasValue && x.FollowUpAt.Value < now);
        var dueSoonFollowUps = rows.Count(x => x.FollowUpAt.HasValue && x.FollowUpAt.Value >= now && x.FollowUpAt.Value <= dueSoonThreshold);
        var noFollowUpCount = rows.Count(x => !x.FollowUpAt.HasValue);

        IEnumerable<FinanceConsoleRowPayload> filtered = rows;
        filtered = followUpBucket switch
        {
            "overdue" => filtered.Where(x => x.FollowUpAt.HasValue && x.FollowUpAt.Value < now),
            "due-soon" => filtered.Where(x => x.FollowUpAt.HasValue && x.FollowUpAt.Value >= now && x.FollowUpAt.Value <= dueSoonThreshold),
            "no-follow-up" => filtered.Where(x => !x.FollowUpAt.HasValue),
            "scheduled" => filtered.Where(x => x.FollowUpAt.HasValue),
            _ => filtered
        };

        var filteredRows = filtered
            .Select(x =>
            {
                var (priorityKey, priorityLabel, priorityReason, priorityRank) = BuildFinancePriority(x, now, dueSoonThreshold);
                x.PriorityKey = priorityKey;
                x.PriorityLabel = priorityLabel;
                x.PriorityReason = priorityReason;
                x.PriorityRank = priorityRank;
                return x;
            })
            .OrderByDescending(x => x.PriorityRank)
            .ThenBy(x => x.FollowUpAt ?? DateTime.MaxValue)
            .ThenByDescending(x => x.CreatedAt ?? DateTime.MinValue)
            .ToList();
        var ownerSummary = filteredRows
            .GroupBy(x => string.IsNullOrWhiteSpace(x.AssignedOwner) ? "Chua gan owner" : x.AssignedOwner.Trim(), StringComparer.OrdinalIgnoreCase)
            .Select(g => new FinanceOwnerSummaryPayload
            {
                OwnerLabel = g.Key,
                ItemCount = g.Count(),
                OverdueCount = g.Count(x => x.FollowUpAt.HasValue && x.FollowUpAt.Value < now),
                DueSoonCount = g.Count(x => x.FollowUpAt.HasValue && x.FollowUpAt.Value >= now && x.FollowUpAt.Value <= dueSoonThreshold),
                NoFollowUpCount = g.Count(x => !x.FollowUpAt.HasValue)
            })
            .OrderByDescending(x => x.OverdueCount)
            .ThenByDescending(x => x.ItemCount)
            .ThenBy(x => x.OwnerLabel)
            .Take(6)
            .ToList();
        var total = filteredRows.Count;
        var pagedRows = filteredRows
            .Skip((Math.Max(page, 1) - 1) * Math.Max(pageSize, 1))
            .Take(Math.Max(pageSize, 1))
            .ToList();

        return new FinanceConsoleSectionResult
        {
            Rows = pagedRows,
            Total = total,
            OverdueFollowUps = overdueFollowUps,
            DueSoonFollowUps = dueSoonFollowUps,
            NoFollowUpCount = noFollowUpCount,
            OwnerSummary = ownerSummary
        };
    }

    private static (string Key, string Label, string Reason, int Rank) BuildFinancePriority(
        FinanceConsoleRowPayload row,
        DateTime now,
        DateTime dueSoonThreshold)
    {
        var hasOwner = !string.IsNullOrWhiteSpace(row.AssignedOwner);
        var hasReminder = row.ReminderSentAt.HasValue;
        var overdue = row.FollowUpAt.HasValue && row.FollowUpAt.Value < now;
        var dueSoon = row.FollowUpAt.HasValue && row.FollowUpAt.Value >= now && row.FollowUpAt.Value <= dueSoonThreshold;
        var noFollowUp = !row.FollowUpAt.HasValue;

        if (overdue && !hasOwner)
        {
            return ("critical", "Can xu ly ngay", "Qua han follow-up va chua co owner.", 400);
        }

        if (overdue && !hasReminder)
        {
            return ("critical", "Can xu ly ngay", "Qua han follow-up va chua nhac owner.", 380);
        }

        if (overdue)
        {
            return ("critical", "Can xu ly ngay", "Ban ghi da qua moc follow-up.", 360);
        }

        if (dueSoon && !hasOwner)
        {
            return ("today", "Xu ly hom nay", "Sap den han 48h nhung chua co owner.", 320);
        }

        if (noFollowUp && !hasOwner)
        {
            return ("today", "Xu ly hom nay", "Chua co owner va chua dat moc follow-up.", 300);
        }

        if (dueSoon)
        {
            return ("today", "Xu ly hom nay", "Sap den han follow-up trong 48h toi.", 260);
        }

        if (noFollowUp)
        {
            return ("watch", "Can len lich", "Ban ghi chua duoc dat follow-up.", 220);
        }

        return ("normal", "Theo doi", "Queue da co owner va moc follow-up.", 100);
    }

    private static FinanceOpsSnapshot BuildFinanceOpsSnapshot(IEnumerable<AdminActionLog> logs)
    {
        var snapshot = new FinanceOpsSnapshot();
        foreach (var log in logs.OrderByDescending(x => x.CreatedAt))
        {
            snapshot.LastActionSummary ??= log.Summary;
            var metadata = ParseMetadata(log.MetadataJson);

            if (log.ActionName.EndsWith("_assign", StringComparison.OrdinalIgnoreCase) && string.IsNullOrWhiteSpace(snapshot.AssignedOwner))
            {
                snapshot.AssignedOwner = ReadString(metadata, "assignedOwner");
            }

            if (log.ActionName.EndsWith("_mark-exported", StringComparison.OrdinalIgnoreCase) && !snapshot.ExportedAt.HasValue)
            {
                snapshot.ExportedAt = ReadDateTime(metadata, "exportedAt") ?? log.CreatedAt;
            }

            if (log.ActionName.EndsWith("_set-follow-up", StringComparison.OrdinalIgnoreCase) && !snapshot.FollowUpAt.HasValue)
            {
                snapshot.FollowUpAt = ReadDateTime(metadata, "followUpAt");
                snapshot.FollowUpNote = ReadString(metadata, "followUpNote") ?? ReadString(metadata, "note");
            }

            if (log.ActionName.EndsWith("_send-reminder", StringComparison.OrdinalIgnoreCase) && !snapshot.ReminderSentAt.HasValue)
            {
                snapshot.ReminderSentAt = ReadDateTime(metadata, "reminderSentAt") ?? log.CreatedAt;
                snapshot.ReminderNote = ReadString(metadata, "reminderNote") ?? ReadString(metadata, "note");
            }
        }

        return snapshot;
    }

    private static Dictionary<string, JsonElement>? ParseMetadata(string? metadataJson)
    {
        if (string.IsNullOrWhiteSpace(metadataJson))
        {
            return null;
        }

        try
        {
            using var document = JsonDocument.Parse(metadataJson);
            return document.RootElement.EnumerateObject().ToDictionary(x => x.Name, x => x.Value.Clone(), StringComparer.OrdinalIgnoreCase);
        }
        catch
        {
            return null;
        }
    }

    private static string? ReadString(Dictionary<string, JsonElement>? metadata, string propertyName)
    {
        if (metadata is null || !metadata.TryGetValue(propertyName, out var value) || value.ValueKind != JsonValueKind.String)
        {
            return null;
        }

        return value.GetString();
    }

    private static DateTime? ReadDateTime(Dictionary<string, JsonElement>? metadata, string propertyName)
    {
        if (metadata is null || !metadata.TryGetValue(propertyName, out var value))
        {
            return null;
        }

        return value.ValueKind == JsonValueKind.String && DateTime.TryParse(value.GetString(), out var parsed)
            ? parsed
            : null;
    }

    private int? TryGetSellerIdFromToken()
    {
        var sub = User.FindFirstValue(JwtRegisteredClaimNames.Sub)
                  ?? User.FindFirstValue(ClaimTypes.NameIdentifier)
                  ?? User.FindFirstValue("sub");

        return int.TryParse(sub, out var sellerId) ? sellerId : null;
    }

    private static string NormalizeSection(string? section)
    {
        var normalized = (section ?? string.Empty).Trim().ToLowerInvariant();
        return normalized switch
        {
            "refunds" => "refunds",
            "returns" => "returns",
            _ => "payouts"
        };
    }

    private static string NormalizeStatus(string? status)
    {
        return string.IsNullOrWhiteSpace(status) ? "all" : status.Trim().ToLowerInvariant();
    }

    private static string NormalizeFollowUpBucket(string? followUpBucket)
    {
        var normalized = (followUpBucket ?? string.Empty).Trim().ToLowerInvariant();
        return normalized switch
        {
            "overdue" => "overdue",
            "due-soon" or "due_soon" => "due-soon",
            "no-follow-up" or "no_follow_up" or "unscheduled" => "no-follow-up",
            "scheduled" => "scheduled",
            _ => "all"
        };
    }

    private static string NormalizeFinanceAction(string? actionName)
    {
        var normalized = (actionName ?? string.Empty).Trim().ToLowerInvariant();
        return normalized switch
        {
            "acknowledge" => "acknowledge",
            "approve" => "approve",
            "reject" => "reject",
            "resolve" => "resolve",
            "release" => "release",
            "hold" => "hold",
            "assign" => "assign",
            "set-follow-up" or "set_follow_up" or "follow-up" or "follow_up" => "set-follow-up",
            "mark-exported" or "mark_exported" => "mark-exported",
            "send-reminder" or "send_reminder" or "remind-owner" or "remind_owner" => "send-reminder",
            _ => "all"
        };
    }

    private static string GetFinanceActionLabel(string actionName)
    {
        return actionName switch
        {
            "acknowledge" => "nhan xu ly",
            "approve" => "phe duyet",
            "reject" => "tu choi",
            "resolve" => "dong",
            "release" => "chi tra",
            "hold" => "giu lai",
              "assign" => "gan owner",
              "set-follow-up" => "cap nhat follow-up",
              "mark-exported" => "xac nhan export",
              "send-reminder" => "nhac owner",
              _ => actionName
          };
      }

    private static DateTime? NormalizeFollowUpAt(DateTime? followUpAt)
    {
        if (!followUpAt.HasValue)
        {
            return null;
        }

        return followUpAt.Value.Kind switch
        {
            DateTimeKind.Utc => followUpAt.Value,
            DateTimeKind.Local => followUpAt.Value.ToUniversalTime(),
            _ => DateTime.SpecifyKind(followUpAt.Value, DateTimeKind.Local).ToUniversalTime()
        };
    }

    private static string? TrimNote(string? note)
    {
        if (string.IsNullOrWhiteSpace(note))
        {
            return null;
        }

        var trimmed = note.Trim();
        return trimmed.Length <= 1000 ? trimmed : trimmed[..1000];
    }

    private static string? TrimShortText(string? value, int maxLength)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return null;
        }

        var trimmed = value.Trim();
        return trimmed.Length <= maxLength ? trimmed : trimmed[..maxLength];
    }

    private static string GetPayoutReconciliationStatus(string? status, DateTime? paidAt)
    {
        var normalized = (status ?? string.Empty).Trim().ToLowerInvariant();
        if (paidAt.HasValue || PayoutPaidStatuses.Contains(normalized))
        {
            return "Da doi soat";
        }

        if (PayoutFailedStatuses.Contains(normalized))
        {
            return "Can kiem tra";
        }

        return "Cho doi soat";
    }

    private static string GetPayoutNextStep(string? status, DateTime? scheduledAt, DateTime? paidAt)
    {
        var normalized = (status ?? string.Empty).Trim().ToLowerInvariant();
        if (paidAt.HasValue || PayoutPaidStatuses.Contains(normalized))
        {
            return "Xuat file doi soat va chot ky thanh toan";
        }

        if (PayoutFailedStatuses.Contains(normalized))
        {
            return "Ra soat gateway va thong bao seller";
        }

        return scheduledAt.HasValue ? "Doi chieu payout voi lich chi tra" : "Dat lich payout hoac giu lai de doi chieu";
    }

    private static string GetRefundReconciliationStatus(string? status, DateTime? processedAt)
    {
        var normalized = (status ?? string.Empty).Trim().ToLowerInvariant();
        if (processedAt.HasValue || RefundClosedStatuses.Contains(normalized))
        {
            return "Da doi soat";
        }

        if (RefundFailedStatuses.Contains(normalized))
        {
            return "Can kiem tra";
        }

        return "Dang mo";
    }

    private static string GetRefundNextStep(string? status, DateTime? processedAt)
    {
        var normalized = (status ?? string.Empty).Trim().ToLowerInvariant();
        if (processedAt.HasValue || RefundClosedStatuses.Contains(normalized))
        {
            return "Doi chieu payment txn va luu bang chot";
        }

        if (RefundFailedStatuses.Contains(normalized))
        {
            return "Ra soat kenh refund va thong bao buyer";
        }

        return "Kiem tra payment txn va phe duyet hoan tien";
    }

    private static string GetReturnReconciliationStatus(string? status, DateTime? completedAt)
    {
        var normalized = (status ?? string.Empty).Trim().ToLowerInvariant();
        if (completedAt.HasValue || ReturnClosedStatuses.Contains(normalized))
        {
            return "Da doi soat";
        }

        if (ReturnRejectedStatuses.Contains(normalized))
        {
            return "Dong case";
        }

        return "Dang mo";
    }

    private static string GetReturnNextStep(string? status, DateTime? approvedAt, DateTime? completedAt)
    {
        var normalized = (status ?? string.Empty).Trim().ToLowerInvariant();
        if (completedAt.HasValue || ReturnClosedStatuses.Contains(normalized))
        {
            return "Doi chieu return voi refund lien quan";
        }

        if (ReturnRejectedStatuses.Contains(normalized))
        {
            return "Luu ly do tu choi va thong bao hau mai";
        }

        return approvedAt.HasValue ? "Theo doi hang hoan ve va chot refund" : "Ra soat ly do va phe duyet return";
    }

    private static void AppendCsvRow(StringBuilder builder, string section, FinanceConsoleRowPayload row)
    {
        builder.AppendLine(string.Join(",",
            Csv(section),
            Csv(row.RecordId),
            Csv(row.OrderId),
            Csv(row.SellerLabel),
            Csv(row.AmountGross),
            Csv(row.FeeAmount),
            Csv(row.AmountNet),
            Csv(row.RefundAmount),
            Csv(row.Status),
            Csv(row.ReconciliationStatus),
            Csv(row.NextStep),
            Csv(row.AssignedOwner),
            Csv(row.FollowUpAt),
            Csv(row.ReferenceCode),
            Csv(row.CreatedAt),
            Csv(row.ProcessedAt),
            Csv(row.PaidAt)));
    }

    private static string Csv(object? value)
    {
        var text = value switch
        {
            null => string.Empty,
            DateTime dt => dt.ToString("yyyy-MM-dd HH:mm:ss"),
            DateTimeOffset dto => dto.ToString("yyyy-MM-dd HH:mm:ss"),
            _ => value.ToString() ?? string.Empty
        };

        text = text.Replace("\"", "\"\"");
        return $"\"{text}\"";
    }

    private static IReadOnlyList<object> BuildStatusOptions(string section)
    {
        return section switch
        {
            "refunds" =>
            [
                new { value = "all", text = "Tất cả" },
                new { value = "pending", text = "Đang xử lý" },
                new { value = "processed", text = "Đã xử lý" },
                new { value = "failed", text = "Thất bại" }
            ],
            "returns" =>
            [
                new { value = "all", text = "Tất cả" },
                new { value = "pending", text = "Mới tạo" },
                new { value = "approved", text = "Đã duyệt" },
                new { value = "completed", text = "Hoàn tất" },
                new { value = "rejected", text = "Từ chối" }
            ],
            _ =>
            [
                new { value = "all", text = "Tất cả" },
                new { value = "pending", text = "Chờ đối soát" },
                new { value = "paid", text = "Đã chi trả" },
                new { value = "failed", text = "Lỗi/huỷ" }
            ]
        };
    }

    private static string FormatSellerLabel(IReadOnlyList<int> sellerIds)
    {
        if (sellerIds.Count == 0)
        {
            return string.Empty;
        }

        if (sellerIds.Count == 1)
        {
            return $"Seller #{sellerIds[0]}";
        }

        return $"Seller #{sellerIds[0]} +{sellerIds.Count - 1}";
    }

    private sealed record SellerLookupRow(int? SellerId, string SellerLabel);

    public sealed class FinanceActionRequest
    {
        public string? Section { get; set; }
        public int RecordId { get; set; }
        public string? ActionName { get; set; }
        public string? Note { get; set; }
        public string? AssigneeLabel { get; set; }
        public DateTime? FollowUpAt { get; set; }
    }

    public sealed class FinanceBulkActionRequest
    {
        public string? Section { get; set; }
        public List<int> RecordIds { get; set; } = new();
        public string? ActionName { get; set; }
        public string? Note { get; set; }
        public string? AssigneeLabel { get; set; }
        public DateTime? FollowUpAt { get; set; }
    }

    private sealed class FinanceConsoleRowPayload
    {
        public int RecordId { get; set; }
        public int? OrderId { get; set; }
        public int? SellerId { get; set; }
        public string SellerLabel { get; set; } = string.Empty;
        public int? OrderCount { get; set; }
        public decimal? AmountGross { get; set; }
        public decimal? FeeAmount { get; set; }
        public decimal? AmountNet { get; set; }
        public decimal? RefundAmount { get; set; }
        public string Status { get; set; } = string.Empty;
        public string Method { get; set; } = string.Empty;
        public string Provider { get; set; } = string.Empty;
        public string ReferenceCode { get; set; } = string.Empty;
        public string ReasonCode { get; set; } = string.Empty;
        public string Resolution { get; set; } = string.Empty;
        public string ItemName { get; set; } = string.Empty;
        public DateTime? CreatedAt { get; set; }
        public DateTime? ScheduledAt { get; set; }
        public DateTime? ProcessedAt { get; set; }
        public DateTime? PaidAt { get; set; }
        public string ReconciliationStatus { get; set; } = string.Empty;
        public string NextStep { get; set; } = string.Empty;
        public string AssignedOwner { get; set; } = string.Empty;
        public DateTime? ExportedAt { get; set; }
        public DateTime? FollowUpAt { get; set; }
        public string FollowUpNote { get; set; } = string.Empty;
        public DateTime? ReminderSentAt { get; set; }
        public string ReminderNote { get; set; } = string.Empty;
        public string PriorityKey { get; set; } = string.Empty;
        public string PriorityLabel { get; set; } = string.Empty;
        public string PriorityReason { get; set; } = string.Empty;
        public int PriorityRank { get; set; }
        public string LastActionSummary { get; set; } = string.Empty;
    }

    private sealed class FinanceOpsSnapshot
    {
        public string? AssignedOwner { get; set; }
        public DateTime? ExportedAt { get; set; }
        public DateTime? FollowUpAt { get; set; }
        public string? FollowUpNote { get; set; }
        public DateTime? ReminderSentAt { get; set; }
        public string? ReminderNote { get; set; }
        public string? LastActionSummary { get; set; }
    }

    private sealed class FinanceConsoleSectionResult
    {
        public List<FinanceConsoleRowPayload> Rows { get; set; } = new();
        public int Total { get; set; }
        public int OverdueFollowUps { get; set; }
        public int DueSoonFollowUps { get; set; }
        public int NoFollowUpCount { get; set; }
        public List<FinanceOwnerSummaryPayload> OwnerSummary { get; set; } = new();
    }

    private sealed class FinanceOwnerSummaryPayload
    {
        public string OwnerLabel { get; set; } = string.Empty;
        public int ItemCount { get; set; }
        public int OverdueCount { get; set; }
        public int DueSoonCount { get; set; }
        public int NoFollowUpCount { get; set; }
    }
}

