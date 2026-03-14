using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
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
        [FromQuery] int? sellerId = null,
        [FromQuery] int page = 1,
        [FromQuery] int pageSize = 12,
        CancellationToken cancellationToken = default)
    {
        var normalizedSection = NormalizeSection(section);
        var normalizedStatus = NormalizeStatus(status);
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

        switch (normalizedSection)
        {
            case "refunds":
                rows = await BuildRefundRowsAsync(refundsQuery, q, normalizedStatus, page, pageSize, cancellationToken);
                total = await CountRefundRowsAsync(refundsQuery, q, normalizedStatus, cancellationToken);
                break;
            case "returns":
                rows = await BuildReturnRowsAsync(returnsQuery, q, normalizedStatus, page, pageSize, cancellationToken);
                total = await CountReturnRowsAsync(returnsQuery, q, normalizedStatus, cancellationToken);
                break;
            default:
                rows = await BuildPayoutRowsAsync(payoutsQuery, q, normalizedStatus, page, pageSize, cancellationToken);
                total = await CountPayoutRowsAsync(payoutsQuery, q, normalizedStatus, cancellationToken);
                break;
        }

        totalPages = total <= 0 ? 1 : (int)Math.Ceiling(total / (double)pageSize);
        if (page > totalPages)
        {
            page = totalPages;
            switch (normalizedSection)
            {
                case "refunds":
                    rows = await BuildRefundRowsAsync(refundsQuery, q, normalizedStatus, page, pageSize, cancellationToken);
                    break;
                case "returns":
                    rows = await BuildReturnRowsAsync(returnsQuery, q, normalizedStatus, page, pageSize, cancellationToken);
                    break;
                default:
                    rows = await BuildPayoutRowsAsync(payoutsQuery, q, normalizedStatus, page, pageSize, cancellationToken);
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
                sellerCount
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
                sellerId,
                sellerOptions,
                statusOptions = BuildStatusOptions(normalizedSection)
            },
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
        if (actionName == "all")
        {
            return BadRequest(new { success = false, message = "Action tai chinh khong hop le." });
        }

        return section switch
        {
            "refunds" => await HandleRefundActionAsync(request.RecordId, actionName, note, cancellationToken),
            "returns" => await HandleReturnActionAsync(request.RecordId, actionName, note, cancellationToken),
            _ => await HandlePayoutActionAsync(request.RecordId, actionName, note, cancellationToken)
        };
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
        var payout = await _db.Payouts.FirstOrDefaultAsync(x => x.PayoutId == payoutId, cancellationToken);
        if (payout is null)
        {
            return NotFound(new { success = false, message = "Khong tim thay payout." });
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
                return BadRequest(new { success = false, message = "Action nay khong ap dung cho payout." });
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
        return Ok(new { success = true, message = $"Da {GetFinanceActionLabel(actionName)} payout #{payoutId}." });
    }

    private async Task<IActionResult> HandleRefundActionAsync(int refundId, string actionName, string? note, CancellationToken cancellationToken)
    {
        var refund = await _db.RefundTransactions.FirstOrDefaultAsync(x => x.RefundId == refundId, cancellationToken);
        if (refund is null)
        {
            return NotFound(new { success = false, message = "Khong tim thay refund." });
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
                return BadRequest(new { success = false, message = "Action nay khong ap dung cho refund." });
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
        return Ok(new { success = true, message = $"Da {GetFinanceActionLabel(actionName)} refund #{refundId}." });
    }

    private async Task<IActionResult> HandleReturnActionAsync(int returnId, string actionName, string? note, CancellationToken cancellationToken)
    {
        var returnRequest = await _db.ReturnRequests.FirstOrDefaultAsync(x => x.ReturnId == returnId, cancellationToken);
        if (returnRequest is null)
        {
            return NotFound(new { success = false, message = "Khong tim thay return." });
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
                return BadRequest(new { success = false, message = "Action nay khong ap dung cho return." });
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
        return Ok(new { success = true, message = $"Da {GetFinanceActionLabel(actionName)} return #{returnId}." });
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

    private async Task<IReadOnlyList<object>> BuildPayoutRowsAsync(
        IQueryable<Payout> query,
        string? q,
        string status,
        int page,
        int pageSize,
        CancellationToken cancellationToken)
    {
        var rows = await ApplyPayoutFilters(query, q, status)
            .OrderByDescending(x => x.CreatedAt)
            .ThenByDescending(x => x.PayoutId)
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .Select(x => new
            {
                recordId = x.PayoutId,
                orderId = (int?)null,
                sellerId = (int?)x.SellerId,
                sellerLabel = $"Seller #{x.SellerId}",
                orderCount = x.PayoutItems.Count,
                amountGross = x.AmountGross,
                feeAmount = x.FeeAmount,
                amountNet = x.AmountNet ?? (x.AmountGross - x.FeeAmount),
                refundAmount = (decimal?)null,
                status = x.Status,
                method = x.PaymentTxn != null ? x.PaymentTxn.Method : null,
                provider = x.PaymentTxn != null ? x.PaymentTxn.Provider : null,
                referenceCode = x.PaymentTxn != null ? x.PaymentTxn.ProviderRef : null,
                reasonCode = (string?)null,
                resolution = (string?)null,
                itemName = (string?)null,
                createdAt = x.CreatedAt,
                scheduledAt = x.ScheduledAt,
                processedAt = (DateTime?)null,
                paidAt = x.PaidAt
            })
            .ToListAsync(cancellationToken);

        return rows;
    }

    private async Task<IReadOnlyList<object>> BuildRefundRowsAsync(
        IQueryable<RefundTransaction> query,
        string? q,
        string status,
        int page,
        int pageSize,
        CancellationToken cancellationToken)
    {
        var rows = await ApplyRefundFilters(query, q, status)
            .OrderByDescending(x => x.CreatedAt)
            .ThenByDescending(x => x.RefundId)
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .Select(x => new
            {
                recordId = x.RefundId,
                orderId = (int?)x.PaymentTxn.OrderId,
                sellerId = (int?)null,
                sellerLabel = string.Empty,
                orderCount = (int?)null,
                amountGross = x.PaymentTxn.Amount,
                feeAmount = x.PaymentTxn.FeeAmount,
                amountNet = (decimal?)null,
                refundAmount = (decimal?)x.Amount,
                status = x.Status,
                method = x.PaymentTxn.Method,
                provider = x.Channel ?? x.PaymentTxn.Provider,
                referenceCode = x.ReferenceCode ?? x.PaymentTxn.ProviderRef,
                reasonCode = x.PaymentTxn.FailureCode,
                resolution = x.PaymentTxn.FailureMessage,
                itemName = (string?)null,
                createdAt = x.CreatedAt,
                scheduledAt = (DateTime?)null,
                processedAt = x.ProcessedAt,
                paidAt = x.PaymentTxn.PaidAt
            })
            .ToListAsync(cancellationToken);

        var orderIds = rows.Where(x => x.orderId.HasValue).Select(x => x.orderId!.Value).Distinct().ToList();
        var sellerLookup = await BuildOrderSellerLookupAsync(orderIds, cancellationToken);

        return rows.Select(x => new
        {
            x.recordId,
            x.orderId,
            sellerId = sellerLookup.TryGetValue(x.orderId ?? 0, out var sellerMeta) ? sellerMeta.SellerId : (int?)null,
            sellerLabel = sellerLookup.TryGetValue(x.orderId ?? 0, out sellerMeta) ? sellerMeta.SellerLabel : string.Empty,
            x.orderCount,
            x.amountGross,
            x.feeAmount,
            x.amountNet,
            x.refundAmount,
            x.status,
            x.method,
            x.provider,
            x.referenceCode,
            x.reasonCode,
            x.resolution,
            x.itemName,
            x.createdAt,
            x.scheduledAt,
            x.processedAt,
            x.paidAt
        }).ToList<object>();
    }

    private async Task<IReadOnlyList<object>> BuildReturnRowsAsync(
        IQueryable<ReturnRequest> query,
        string? q,
        string status,
        int page,
        int pageSize,
        CancellationToken cancellationToken)
    {
        var rows = await ApplyReturnFilters(query, q, status)
            .OrderByDescending(x => x.RequestedAt)
            .ThenByDescending(x => x.ReturnId)
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .Select(x => new
            {
                recordId = x.ReturnId,
                orderId = (int?)x.SellerOrderItem.SellerOrder.OrderId,
                sellerId = (int?)x.SellerId,
                sellerLabel = $"Seller #{x.SellerId}",
                orderCount = (int?)null,
                amountGross = x.SellerOrderItem.FinalAmount ?? (x.SellerOrderItem.UnitPrice * x.SellerOrderItem.Quantity),
                feeAmount = (decimal?)null,
                amountNet = (decimal?)null,
                refundAmount = x.RefundAmount,
                status = x.Status,
                method = (string?)null,
                provider = (string?)null,
                referenceCode = (string?)null,
                reasonCode = x.ReasonCode,
                resolution = x.Resolution,
                itemName = x.SellerOrderItem.SnapshotName,
                createdAt = x.RequestedAt,
                scheduledAt = x.ApprovedAt,
                processedAt = x.CompletedAt,
                paidAt = (DateTime?)null
            })
            .ToListAsync(cancellationToken);

        return rows;
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
            _ => actionName
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
    }
}
