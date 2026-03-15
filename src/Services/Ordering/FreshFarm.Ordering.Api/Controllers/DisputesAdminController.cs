using FreshFarm.Ordering.Api.Models;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using System.Security.Claims;
using System.Text.Json;

namespace FreshFarm.Ordering.Api.Controllers;

[ApiController]
[Route("api/orders/admin/disputes")]
[Authorize(Policy = "AdminOnly")]
public sealed class DisputesAdminController : ControllerBase
{
    private static readonly string[] RefundOpenStatuses = ["pending", "requested", "processing", "review"];
    private static readonly string[] RefundResolvedStatuses = ["approved", "processed", "completed", "succeeded", "success", "paid"];
    private static readonly string[] RefundFailedStatuses = ["failed", "rejected", "cancelled", "canceled", "error"];

    private static readonly string[] ReturnResolvedStatuses = ["approved", "accepted", "shipping", "shipping_back", "completed", "resolved", "refunded", "done"];
    private static readonly string[] ReturnFailedStatuses = ["rejected", "cancelled", "canceled", "denied"];

    private readonly FreshFarmOrderingDBContext _db;

    public DisputesAdminController(FreshFarmOrderingDBContext db)
    {
        _db = db;
    }

    [HttpGet("queue")]
    public async Task<IActionResult> Queue(
        [FromQuery] string? section = null,
        [FromQuery] string? q = null,
        [FromQuery] string? status = null,
        [FromQuery] int? sellerId = null,
        [FromQuery] int page = 1,
        [FromQuery] int pageSize = 12,
        CancellationToken cancellationToken = default)
    {
        page = page < 1 ? 1 : page;
        pageSize = pageSize <= 0 || pageSize > 50 ? 12 : pageSize;

        var normalizedSection = NormalizeSection(section);
        var normalizedStatus = NormalizeStatus(status);
        sellerId = sellerId > 0 ? sellerId : null;
        var term = string.IsNullOrWhiteSpace(q) ? null : q.Trim();

        var rows = new List<CaseQueueRow>();
        rows.AddRange(await BuildSupportRowsAsync(sellerId, cancellationToken));
        rows.AddRange(await BuildReturnRowsAsync(sellerId, cancellationToken));
        rows.AddRange(await BuildRefundRowsAsync(sellerId, cancellationToken));

        var filteredRows = rows
            .Where(x => normalizedSection == "all" || x.CaseType == normalizedSection)
            .Where(x => MatchesStatus(x, normalizedStatus))
            .Where(x => MatchesQuery(x, term))
            .OrderByDescending(x => x.IsSlaBreached)
            .ThenByDescending(x => x.UpdatedAt ?? x.CreatedAt)
            .ThenByDescending(x => x.CreatedAt)
            .ToList();

        var total = filteredRows.Count;
        var totalPages = total <= 0 ? 1 : (int)Math.Ceiling(total / (double)pageSize);
        if (page > totalPages)
        {
            page = totalPages;
        }

        var pagedRows = filteredRows
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .Select(x => new
            {
                caseType = x.CaseType,
                caseId = x.CaseId,
                title = x.Title,
                summary = x.Summary,
                displayStatus = x.DisplayStatus,
                queueStatus = x.QueueStatus,
                sellerId = x.SellerId,
                sellerLabel = x.SellerLabel,
                buyerId = x.BuyerId,
                buyerName = x.BuyerName,
                buyerPhone = x.BuyerPhone,
                orderId = x.OrderId,
                amount = x.Amount,
                unreadCount = x.UnreadCount,
                assignedOwner = x.AssignedOwner,
                targetResolutionAt = x.TargetResolutionAt,
                evidenceCount = x.EvidenceCount,
                isSlaBreached = x.IsSlaBreached,
                createdAt = x.CreatedAt,
                updatedAt = x.UpdatedAt
            })
            .ToList();

        var sellerOptions = await _db.SellerOrders
            .AsNoTracking()
            .Select(x => x.SellerId)
            .Distinct()
            .OrderBy(x => x)
            .Take(200)
            .Select(x => new { value = x.ToString(), text = $"Seller #{x}" })
            .ToListAsync(cancellationToken);

        return Ok(new
        {
            section = normalizedSection,
            page,
            pageSize,
            total,
            totalPages,
            stats = new
            {
                totalCases = rows.Count,
                openCases = rows.Count(x => x.QueueStatus == "open"),
                pendingCases = rows.Count(x => x.QueueStatus == "pending"),
                resolvedCases = rows.Count(x => x.QueueStatus == "resolved"),
                breachedCases = rows.Count(x => x.IsSlaBreached),
                supportCases = rows.Count(x => x.CaseType == "support"),
                returnCases = rows.Count(x => x.CaseType == "returns"),
                refundCases = rows.Count(x => x.CaseType == "refunds")
            },
            filters = new
            {
                q = q ?? string.Empty,
                status = normalizedStatus,
                section = normalizedSection,
                sellerId,
                sellerOptions,
                sectionOptions = new[]
                {
                    new { value = "all", text = "Tất cả" },
                    new { value = "support", text = "CS queue" },
                    new { value = "returns", text = "Returns" },
                    new { value = "refunds", text = "Refunds" }
                },
                statusOptions = new[]
                {
                    new { value = "all", text = "Tất cả" },
                    new { value = "open", text = "Open" },
                    new { value = "pending", text = "Pending" },
                    new { value = "breached", text = "SLA breach" },
                    new { value = "resolved", text = "Resolved" }
                }
            },
            rows = pagedRows
        });
    }

    [HttpGet("details")]
    public async Task<IActionResult> Details(
        [FromQuery] string? type = null,
        [FromQuery] int id = 0,
        CancellationToken cancellationToken = default)
    {
        if (id <= 0)
        {
            return BadRequest(new { message = "ID case không hợp lệ." });
        }

        var normalizedType = NormalizeSection(type);
        return normalizedType switch
        {
            "support" => await BuildSupportDetailsAsync(id, cancellationToken),
            "returns" => await BuildReturnDetailsAsync(id, cancellationToken),
            "refunds" => await BuildRefundDetailsAsync(id, cancellationToken),
            _ => BadRequest(new { message = "Loại case không hợp lệ." })
        };
    }

    [HttpPost("actions")]
    public async Task<IActionResult> TakeAction([FromBody] DisputeActionRequest? request, CancellationToken cancellationToken = default)
    {
        if (request is null || request.CaseId <= 0)
        {
            return BadRequest(new { success = false, message = "Payload action khong hop le." });
        }

        var caseType = NormalizeSection(request.CaseType);
        var actionName = NormalizeAction(request.ActionName);
        var note = TrimNote(request.Note);
        var assigneeLabel = TrimShortText(request.AssigneeLabel, 120);
        var evidenceNote = TrimNote(request.EvidenceNote);
        var slaHours = NormalizeSlaHours(request.SlaHours);
        if (caseType == "all" || actionName == "all")
        {
            return BadRequest(new { success = false, message = "Loai case hoac action khong hop le." });
        }

        return caseType switch
        {
            "support" => await HandleSupportActionAsync(request.CaseId, actionName, note, assigneeLabel, slaHours, evidenceNote, cancellationToken),
            "returns" => await HandleReturnActionAsync(request.CaseId, actionName, note, assigneeLabel, slaHours, evidenceNote, cancellationToken),
            "refunds" => await HandleRefundActionAsync(request.CaseId, actionName, note, assigneeLabel, slaHours, evidenceNote, cancellationToken),
            _ => BadRequest(new { success = false, message = "Loai case khong duoc ho tro." })
        };
    }

    private async Task<IActionResult> BuildSupportDetailsAsync(int conversationId, CancellationToken cancellationToken)
    {
        var conversation = await _db.SupportConversations
            .AsNoTracking()
            .Where(x => x.ConversationId == conversationId)
            .Select(x => new
            {
                x.ConversationId,
                x.UserId,
                x.Status,
                x.StartedAt,
                x.ClosedAt
            })
            .FirstOrDefaultAsync(cancellationToken);

        if (conversation is null || !conversation.UserId.HasValue)
        {
            return NotFound(new { message = "Không tìm thấy case hỗ trợ." });
        }

        var buyerOrders = await _db.Orders
            .AsNoTracking()
            .Where(x => x.UserId == conversation.UserId.Value)
            .OrderByDescending(x => x.OrderDate)
            .Take(5)
            .Select(x => new
            {
                x.OrderId,
                x.OrderDate,
                x.TotalAmount,
                x.Status,
                x.BuyerFullName,
                x.BuyerPhone,
                x.BuyerEmail
            })
            .ToListAsync(cancellationToken);

        var messages = await _db.SupportMessages
            .AsNoTracking()
            .Where(x => x.ConversationId == conversationId)
            .OrderByDescending(x => x.CreatedAt)
            .Take(12)
            .Select(x => new
            {
                sender = x.SenderType == 0 ? "buyer" : "admin",
                x.Content,
                x.CreatedAt,
                x.IsRead
            })
            .ToListAsync(cancellationToken);

        var relatedReturns = await _db.ReturnRequests
            .AsNoTracking()
            .Where(x => x.SellerOrderItem.SellerOrder.Order.UserId == conversation.UserId.Value)
            .OrderByDescending(x => x.RequestedAt)
            .Take(5)
            .Select(x => new
            {
                caseType = "returns",
                caseId = x.ReturnId,
                title = x.SellerOrderItem.SnapshotName,
                status = x.Status,
                amount = x.RefundAmount,
                createdAt = x.RequestedAt
            })
            .ToListAsync(cancellationToken);

        var relatedRefunds = await _db.RefundTransactions
            .AsNoTracking()
            .Where(x => x.PaymentTxn.Order.UserId == conversation.UserId.Value)
            .OrderByDescending(x => x.CreatedAt)
            .Take(5)
            .Select(x => new
            {
                caseType = "refunds",
                caseId = x.RefundId,
                title = $"Refund #{x.RefundId}",
                status = x.Status,
                amount = x.Amount,
                createdAt = x.CreatedAt
            })
            .ToListAsync(cancellationToken);

        var latestOrder = buyerOrders.FirstOrDefault();
        var queueStatus = GetSupportQueueStatus(
            conversation.Status,
            messages.Any(x => x.sender == "buyer" && !x.IsRead),
            conversation.StartedAt,
            messages.Select(x => (DateTime?)x.CreatedAt).FirstOrDefault());
        var operations = await LoadOperationsSnapshotAsync("support_case", conversationId, queueStatus, cancellationToken);

        return Ok(new
        {
            caseType = "support",
            caseId = conversation.ConversationId,
            title = $"Hội thoại #{conversation.ConversationId}",
            summary = "Case CS được bridge từ support chat hiện tại.",
            displayStatus = string.IsNullOrWhiteSpace(conversation.Status) ? "Open" : conversation.Status,
            queueStatus,
            isSlaBreached = operations.IsSlaBreached,
            createdAt = conversation.StartedAt,
            updatedAt = MaxDate(
                conversation.ClosedAt ?? messages.Select(x => (DateTime?)x.CreatedAt).FirstOrDefault() ?? conversation.StartedAt,
                operations.LastActionAt),
            buyer = new
            {
                userId = conversation.UserId.Value,
                fullName = latestOrder?.BuyerFullName ?? $"Khách #{conversation.UserId.Value}",
                phone = latestOrder?.BuyerPhone,
                email = latestOrder?.BuyerEmail
            },
            order = latestOrder is null ? null : new
            {
                orderId = latestOrder.OrderId,
                totalAmount = latestOrder.TotalAmount,
                status = latestOrder.Status,
                orderDate = latestOrder.OrderDate
            },
            assignment = operations.Assignment,
            sla = operations.Sla,
            timeline = new[]
            {
                new { label = "Mở hội thoại", value = (DateTime?)conversation.StartedAt, tone = "info" },
                new { label = "Đóng hội thoại", value = conversation.ClosedAt, tone = "success" }
            },
            activityItems = operations.ActivityItems,
            evidenceItems = operations.EvidenceItems,
            messages = messages.OrderBy(x => x.CreatedAt).ToList(),
            relatedOrders = buyerOrders.Select(x => new
            {
                x.OrderId,
                x.OrderDate,
                x.TotalAmount,
                x.Status
            }),
            relatedCases = relatedReturns.Cast<object>().Concat(relatedRefunds).ToList()
        });
    }

    private async Task<IActionResult> BuildReturnDetailsAsync(int returnId, CancellationToken cancellationToken)
    {
        var item = await _db.ReturnRequests
            .AsNoTracking()
            .Where(x => x.ReturnId == returnId)
            .Select(x => new
            {
                x.ReturnId,
                x.Status,
                x.ReasonCode,
                x.Description,
                x.Resolution,
                x.RequestedAt,
                x.ApprovedAt,
                x.CompletedAt,
                x.RefundAmount,
                x.SellerId,
                orderId = x.SellerOrderItem.SellerOrder.OrderId,
                itemName = x.SellerOrderItem.SnapshotName,
                buyerId = x.SellerOrderItem.SellerOrder.Order.UserId,
                buyerName = x.SellerOrderItem.SellerOrder.Order.BuyerFullName,
                buyerPhone = x.SellerOrderItem.SellerOrder.Order.BuyerPhone,
                buyerEmail = x.SellerOrderItem.SellerOrder.Order.BuyerEmail,
                orderStatus = x.SellerOrderItem.SellerOrder.Order.Status,
                orderTotal = x.SellerOrderItem.SellerOrder.Order.TotalAmount,
                orderDate = x.SellerOrderItem.SellerOrder.Order.OrderDate
            })
            .FirstOrDefaultAsync(cancellationToken);

        if (item is null)
        {
            return NotFound(new { message = "Không tìm thấy case đổi trả." });
        }

        var relatedRefunds = await _db.RefundTransactions
            .AsNoTracking()
            .Where(x => x.PaymentTxn.OrderId == item.orderId)
            .OrderByDescending(x => x.CreatedAt)
            .Take(5)
            .Select(x => new
            {
                caseType = "refunds",
                caseId = x.RefundId,
                title = $"Refund #{x.RefundId}",
                status = x.Status,
                amount = x.Amount,
                createdAt = x.CreatedAt
            })
            .ToListAsync(cancellationToken);

        var relatedConversations = await _db.SupportConversations
            .AsNoTracking()
            .Where(x => x.UserId == item.buyerId)
            .OrderByDescending(x => x.StartedAt)
            .Take(5)
            .Select(x => new
            {
                caseType = "support",
                caseId = x.ConversationId,
                title = $"Hội thoại #{x.ConversationId}",
                status = x.Status,
                amount = (decimal?)null,
                createdAt = x.StartedAt
            })
            .ToListAsync(cancellationToken);

        var queueStatus = GetReturnQueueStatus(item.Status, item.RequestedAt, item.CompletedAt);
        var operations = await LoadOperationsSnapshotAsync("return_case", returnId, queueStatus, cancellationToken);

        return Ok(new
        {
            caseType = "returns",
            caseId = item.ReturnId,
            title = $"Return #{item.ReturnId} - {item.itemName}",
            summary = item.Description,
            displayStatus = item.Status,
            queueStatus,
            isSlaBreached = operations.IsSlaBreached,
            createdAt = item.RequestedAt,
            updatedAt = MaxDate(item.CompletedAt ?? item.ApprovedAt ?? item.RequestedAt, operations.LastActionAt),
            seller = new
            {
                sellerId = item.SellerId,
                sellerLabel = $"Seller #{item.SellerId}"
            },
            buyer = new
            {
                userId = item.buyerId,
                fullName = item.buyerName,
                phone = item.buyerPhone,
                email = item.buyerEmail
            },
            order = new
            {
                orderId = item.orderId,
                totalAmount = item.orderTotal,
                status = item.orderStatus,
                orderDate = item.orderDate
            },
            assignment = operations.Assignment,
            sla = operations.Sla,
            timeline = new[]
            {
                new { label = "Yêu cầu tạo", value = (DateTime?)item.RequestedAt, tone = "info" },
                new { label = "Phê duyệt", value = item.ApprovedAt, tone = "warning" },
                new { label = "Hoàn tất", value = item.CompletedAt, tone = "success" }
            },
            activityItems = operations.ActivityItems,
            evidenceItems = operations.EvidenceItems,
            messages = Array.Empty<object>(),
            relatedOrders = new[]
            {
                new
                {
                    orderId = item.orderId,
                    orderDate = item.orderDate,
                    totalAmount = item.orderTotal,
                    status = item.orderStatus
                }
            },
            relatedCases = relatedRefunds.Cast<object>().Concat(relatedConversations).ToList(),
            afterSales = new
            {
                reasonCode = item.ReasonCode,
                resolution = item.Resolution,
                refundAmount = item.RefundAmount
            }
        });
    }

    private async Task<IActionResult> BuildRefundDetailsAsync(int refundId, CancellationToken cancellationToken)
    {
        var item = await _db.RefundTransactions
            .AsNoTracking()
            .Where(x => x.RefundId == refundId)
            .Select(x => new
            {
                x.RefundId,
                x.Status,
                x.Channel,
                x.ReferenceCode,
                x.Amount,
                x.CreatedAt,
                x.ProcessedAt,
                orderId = x.PaymentTxn.OrderId,
                provider = x.PaymentTxn.Provider,
                providerRef = x.PaymentTxn.ProviderRef,
                method = x.PaymentTxn.Method,
                paymentStatus = x.PaymentTxn.Status,
                buyerId = x.PaymentTxn.Order.UserId,
                buyerName = x.PaymentTxn.Order.BuyerFullName,
                buyerPhone = x.PaymentTxn.Order.BuyerPhone,
                buyerEmail = x.PaymentTxn.Order.BuyerEmail,
                orderStatus = x.PaymentTxn.Order.Status,
                orderTotal = x.PaymentTxn.Order.TotalAmount,
                orderDate = x.PaymentTxn.Order.OrderDate
            })
            .FirstOrDefaultAsync(cancellationToken);

        if (item is null)
        {
            return NotFound(new { message = "Không tìm thấy case hoàn tiền." });
        }

        var sellerRows = await _db.SellerOrders
            .AsNoTracking()
            .Where(x => x.OrderId == item.orderId)
            .Select(x => x.SellerId)
            .Distinct()
            .OrderBy(x => x)
            .ToListAsync(cancellationToken);

        var relatedReturns = await _db.ReturnRequests
            .AsNoTracking()
            .Where(x => x.SellerOrderItem.SellerOrder.OrderId == item.orderId)
            .OrderByDescending(x => x.RequestedAt)
            .Take(5)
            .Select(x => new
            {
                caseType = "returns",
                caseId = x.ReturnId,
                title = x.SellerOrderItem.SnapshotName,
                status = x.Status,
                amount = x.RefundAmount,
                createdAt = x.RequestedAt
            })
            .ToListAsync(cancellationToken);

        var relatedConversations = await _db.SupportConversations
            .AsNoTracking()
            .Where(x => x.UserId == item.buyerId)
            .OrderByDescending(x => x.StartedAt)
            .Take(5)
            .Select(x => new
            {
                caseType = "support",
                caseId = x.ConversationId,
                title = $"Hội thoại #{x.ConversationId}",
                status = x.Status,
                amount = (decimal?)null,
                createdAt = x.StartedAt
            })
            .ToListAsync(cancellationToken);

        var queueStatus = GetRefundQueueStatus(item.Status, item.CreatedAt, item.ProcessedAt);
        var operations = await LoadOperationsSnapshotAsync("refund_case", refundId, queueStatus, cancellationToken);

        return Ok(new
        {
            caseType = "refunds",
            caseId = item.RefundId,
            title = $"Refund #{item.RefundId}",
            summary = $"Provider {item.provider} / Ref {item.ReferenceCode ?? item.providerRef ?? "-"}",
            displayStatus = item.Status,
            queueStatus,
            isSlaBreached = operations.IsSlaBreached,
            createdAt = item.CreatedAt,
            updatedAt = MaxDate(item.ProcessedAt ?? item.CreatedAt, operations.LastActionAt),
            seller = new
            {
                sellerId = sellerRows.Count == 1 ? sellerRows[0] : (int?)null,
                sellerLabel = FormatSellerLabel(sellerRows)
            },
            buyer = new
            {
                userId = item.buyerId,
                fullName = item.buyerName,
                phone = item.buyerPhone,
                email = item.buyerEmail
            },
            order = new
            {
                orderId = item.orderId,
                totalAmount = item.orderTotal,
                status = item.orderStatus,
                orderDate = item.orderDate
            },
            assignment = operations.Assignment,
            sla = operations.Sla,
            timeline = new[]
            {
                new { label = "Khởi tạo refund", value = (DateTime?)item.CreatedAt, tone = "info" },
                new { label = "Xử lý xong", value = item.ProcessedAt, tone = "success" }
            },
            activityItems = operations.ActivityItems,
            evidenceItems = operations.EvidenceItems,
            messages = Array.Empty<object>(),
            relatedOrders = new[]
            {
                new
                {
                    orderId = item.orderId,
                    orderDate = item.orderDate,
                    totalAmount = item.orderTotal,
                    status = item.orderStatus
                }
            },
            relatedCases = relatedReturns.Cast<object>().Concat(relatedConversations).ToList(),
            afterSales = new
            {
                amount = item.Amount,
                channel = item.Channel,
                method = item.method,
                paymentStatus = item.paymentStatus,
                referenceCode = item.ReferenceCode ?? item.providerRef
            }
        });
    }

    private async Task<IActionResult> HandleSupportActionAsync(int conversationId, string actionName, string? note, string? assigneeLabel, int? slaHours, string? evidenceNote, CancellationToken cancellationToken)
    {
        var conversation = await _db.SupportConversations.FirstOrDefaultAsync(x => x.ConversationId == conversationId, cancellationToken);
        if (conversation is null)
        {
            return NotFound(new { success = false, message = "Khong tim thay case ho tro." });
        }

        var caseActionResult = await HandleCaseManagementActionAsync(
            "support",
            "support_case",
            conversationId,
            actionName,
            note,
            assigneeLabel,
            slaHours,
            evidenceNote,
            null,
            cancellationToken);
        if (caseActionResult is not null)
        {
            return caseActionResult;
        }

        switch (actionName)
        {
            case "acknowledge":
                conversation.Status = "pending";
                conversation.ClosedAt = null;
                await MarkBuyerMessagesAsReadAsync(conversationId, cancellationToken);
                break;
            case "resolve":
                conversation.Status = "closed";
                conversation.ClosedAt = DateTime.UtcNow;
                await MarkBuyerMessagesAsReadAsync(conversationId, cancellationToken);
                break;
            case "reopen":
                conversation.Status = "open";
                conversation.ClosedAt = null;
                break;
            default:
                return BadRequest(new { success = false, message = "Action nay khong ap dung cho support case." });
        }

        var actorUserId = GetActorUserId();
        var actionLog = AdminAuditLogger.AddAction(
            _db,
            "dispute_center",
            $"support_{actionName}",
            "support_case",
            conversationId,
            $"Admin {GetActionLabel(actionName)} support case #{conversationId}.",
            actorUserId,
            new { caseType = "support", actionName, note });
        AdminAuditLogger.AddModeration(_db, actionLog, "support_case", conversationId, actionName, note, actorUserId);

        await _db.SaveChangesAsync(cancellationToken);
        return Ok(new { success = true, message = $"Da {GetActionLabel(actionName)} case ho tro #{conversationId}." });
    }

    private async Task<IActionResult> HandleReturnActionAsync(int returnId, string actionName, string? note, string? assigneeLabel, int? slaHours, string? evidenceNote, CancellationToken cancellationToken)
    {
        var request = await _db.ReturnRequests.FirstOrDefaultAsync(x => x.ReturnId == returnId, cancellationToken);
        if (request is null)
        {
            return NotFound(new { success = false, message = "Khong tim thay case doi tra." });
        }

        var caseActionResult = await HandleCaseManagementActionAsync(
            "returns",
            "return_case",
            returnId,
            actionName,
            note,
            assigneeLabel,
            slaHours,
            evidenceNote,
            request.RefundAmount,
            cancellationToken);
        if (caseActionResult is not null)
        {
            return caseActionResult;
        }

        switch (actionName)
        {
            case "acknowledge":
                request.Status = "review";
                break;
            case "approve":
                request.Status = "approved";
                request.ApprovedAt ??= DateTime.UtcNow;
                request.Resolution = string.IsNullOrWhiteSpace(note) ? "Approved by admin dispute center." : note;
                break;
            case "reject":
                request.Status = "rejected";
                request.CompletedAt = DateTime.UtcNow;
                request.Resolution = string.IsNullOrWhiteSpace(note) ? "Rejected by admin dispute center." : note;
                break;
            case "resolve":
                request.Status = "resolved";
                request.ApprovedAt ??= DateTime.UtcNow;
                request.CompletedAt = DateTime.UtcNow;
                if (!string.IsNullOrWhiteSpace(note))
                {
                    request.Resolution = note;
                }
                break;
            default:
                return BadRequest(new { success = false, message = "Action nay khong ap dung cho return case." });
        }

        var actorUserId = GetActorUserId();
        var actionLog = AdminAuditLogger.AddAction(
            _db,
            "dispute_center",
            $"return_{actionName}",
            "return_case",
            returnId,
            $"Admin {GetActionLabel(actionName)} return case #{returnId}.",
            actorUserId,
            new { caseType = "returns", actionName, note, sellerId = request.SellerId, request.RefundAmount });
        AdminAuditLogger.AddModeration(_db, actionLog, "return_case", returnId, actionName, note, actorUserId);

        await _db.SaveChangesAsync(cancellationToken);
        return Ok(new { success = true, message = $"Da {GetActionLabel(actionName)} return #{returnId}." });
    }

    private async Task<IActionResult> HandleRefundActionAsync(int refundId, string actionName, string? note, string? assigneeLabel, int? slaHours, string? evidenceNote, CancellationToken cancellationToken)
    {
        var refund = await _db.RefundTransactions.FirstOrDefaultAsync(x => x.RefundId == refundId, cancellationToken);
        if (refund is null)
        {
            return NotFound(new { success = false, message = "Khong tim thay refund case." });
        }

        var caseActionResult = await HandleCaseManagementActionAsync(
            "refunds",
            "refund_case",
            refundId,
            actionName,
            note,
            assigneeLabel,
            slaHours,
            evidenceNote,
            refund.Amount,
            cancellationToken);
        if (caseActionResult is not null)
        {
            return caseActionResult;
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
                return BadRequest(new { success = false, message = "Action nay khong ap dung cho refund case." });
        }

        var actorUserId = GetActorUserId();
        var actionLog = AdminAuditLogger.AddAction(
            _db,
            "dispute_center",
            $"refund_{actionName}",
            "refund_case",
            refundId,
            $"Admin {GetActionLabel(actionName)} refund case #{refundId}.",
            actorUserId,
            new { caseType = "refunds", actionName, note, refund.Amount, refund.ReferenceCode });
        AdminAuditLogger.AddModeration(_db, actionLog, "refund_case", refundId, actionName, note, actorUserId);
        AdminAuditLogger.AddSettlement(_db, actionLog, $"refund_{actionName}", "refund_case", refundId, null, refund.Amount, note, actorUserId);

        await _db.SaveChangesAsync(cancellationToken);
        return Ok(new { success = true, message = $"Da {GetActionLabel(actionName)} refund #{refundId}." });
    }

    private async Task MarkBuyerMessagesAsReadAsync(int conversationId, CancellationToken cancellationToken)
    {
        var unreadMessages = await _db.SupportMessages
            .Where(x => x.ConversationId == conversationId && x.SenderType == 0 && !x.IsRead)
            .ToListAsync(cancellationToken);

        foreach (var message in unreadMessages)
        {
            message.IsRead = true;
        }
    }

    private async Task<List<CaseQueueRow>> BuildSupportRowsAsync(int? sellerId, CancellationToken cancellationToken)
    {
        var conversations = await _db.SupportConversations
            .AsNoTracking()
            .Where(x => x.UserId.HasValue)
            .Select(x => new
            {
                x.ConversationId,
                x.UserId,
                x.Status,
                x.StartedAt,
                x.ClosedAt
            })
            .ToListAsync(cancellationToken);

        if (conversations.Count == 0)
        {
            return [];
        }

        var userIds = conversations.Select(x => x.UserId!.Value).Distinct().ToList();
        var orders = await _db.Orders
            .AsNoTracking()
            .Where(x => userIds.Contains(x.UserId))
            .Select(x => new
            {
                x.OrderId,
                x.UserId,
                x.OrderDate,
                x.BuyerFullName,
                x.BuyerPhone,
                x.TotalAmount,
                sellerIds = x.SellerOrders.Select(so => so.SellerId).Distinct().OrderBy(id => id).ToList()
            })
            .ToListAsync(cancellationToken);

        if (sellerId.HasValue)
        {
            var sellerScopedUsers = orders
                .Where(x => x.sellerIds.Contains(sellerId.Value))
                .Select(x => x.UserId)
                .Distinct()
                .ToHashSet();

            conversations = conversations
                .Where(x => sellerScopedUsers.Contains(x.UserId!.Value))
                .ToList();
            orders = orders
                .Where(x => x.sellerIds.Contains(sellerId.Value))
                .ToList();
        }

        var conversationIds = conversations.Select(x => x.ConversationId).ToList();
        var messages = await _db.SupportMessages
            .AsNoTracking()
            .Where(x => conversationIds.Contains(x.ConversationId))
            .Select(x => new
            {
                x.ConversationId,
                x.SenderType,
                x.CreatedAt,
                x.Content,
                x.IsRead,
                x.IsDeleted
            })
            .ToListAsync(cancellationToken);

        var latestOrdersByUser = orders
            .GroupBy(x => x.UserId)
            .ToDictionary(g => g.Key, g => g.OrderByDescending(x => x.OrderDate).First());

        var rows = conversations
            .Select(conversation =>
            {
                var convMessages = messages
                    .Where(x => x.ConversationId == conversation.ConversationId && !x.IsDeleted)
                    .OrderByDescending(x => x.CreatedAt)
                    .ToList();

                latestOrdersByUser.TryGetValue(conversation.UserId!.Value, out var latestOrder);

                var lastMessageAt = convMessages.FirstOrDefault()?.CreatedAt;
                var hasUnreadBuyerMessage = convMessages.Any(x => x.SenderType == 0 && !x.IsRead);
                var lastBuyerMessageAt = convMessages.FirstOrDefault(x => x.SenderType == 0)?.CreatedAt;
                var queueStatus = GetSupportQueueStatus(conversation.Status, hasUnreadBuyerMessage, conversation.StartedAt, lastBuyerMessageAt ?? lastMessageAt);

                var sellerIds = latestOrder?.sellerIds ?? [];
                return new CaseQueueRow
                {
                    CaseType = "support",
                    CaseId = conversation.ConversationId,
                    Title = $"Hội thoại #{conversation.ConversationId}",
                    Summary = string.IsNullOrWhiteSpace(convMessages.FirstOrDefault()?.Content)
                        ? "Chưa có tin nhắn mới."
                        : convMessages.First().Content,
                    DisplayStatus = string.IsNullOrWhiteSpace(conversation.Status) ? "Open" : conversation.Status,
                    QueueStatus = queueStatus,
                    SellerId = sellerIds.Count == 1 ? sellerIds[0] : sellerId,
                    SellerLabel = sellerId.HasValue ? $"Seller #{sellerId.Value}" : FormatSellerLabel(sellerIds),
                    BuyerId = conversation.UserId.Value,
                    BuyerName = string.IsNullOrWhiteSpace(latestOrder?.BuyerFullName) ? $"Khách #{conversation.UserId.Value}" : latestOrder.BuyerFullName,
                    BuyerPhone = latestOrder?.BuyerPhone,
                    OrderId = latestOrder?.OrderId,
                    Amount = latestOrder?.TotalAmount,
                    UnreadCount = convMessages.Count(x => x.SenderType == 0 && !x.IsRead),
                    IsSlaBreached = queueStatus == "breached",
                    CreatedAt = conversation.StartedAt,
                    UpdatedAt = conversation.ClosedAt ?? lastMessageAt ?? conversation.StartedAt
                };
            })
            .ToList();

        await EnrichRowsWithOperationsAsync(rows, cancellationToken);
        return rows;
    }

    private async Task<List<CaseQueueRow>> BuildReturnRowsAsync(int? sellerId, CancellationToken cancellationToken)
    {
        var query = _db.ReturnRequests.AsNoTracking().AsQueryable();
        if (sellerId.HasValue)
        {
            query = query.Where(x => x.SellerId == sellerId.Value);
        }

        var rows = await query
            .OrderByDescending(x => x.RequestedAt)
            .Select(x => new CaseQueueRow
            {
                CaseType = "returns",
                CaseId = x.ReturnId,
                Title = string.IsNullOrWhiteSpace(x.SellerOrderItem.SnapshotName) ? $"Return #{x.ReturnId}" : x.SellerOrderItem.SnapshotName,
                Summary = string.IsNullOrWhiteSpace(x.Description) ? $"Lý do: {x.ReasonCode}" : x.Description,
                DisplayStatus = x.Status,
                QueueStatus = GetReturnQueueStatus(x.Status, x.RequestedAt, x.CompletedAt),
                SellerId = x.SellerId,
                SellerLabel = $"Seller #{x.SellerId}",
                BuyerId = x.SellerOrderItem.SellerOrder.Order.UserId,
                BuyerName = x.SellerOrderItem.SellerOrder.Order.BuyerFullName,
                BuyerPhone = x.SellerOrderItem.SellerOrder.Order.BuyerPhone,
                OrderId = x.SellerOrderItem.SellerOrder.OrderId,
                Amount = x.RefundAmount,
                UnreadCount = 0,
                IsSlaBreached = GetReturnQueueStatus(x.Status, x.RequestedAt, x.CompletedAt) == "breached",
                CreatedAt = x.RequestedAt,
                UpdatedAt = x.CompletedAt ?? x.ApprovedAt ?? x.RequestedAt
            })
            .ToListAsync(cancellationToken);

        await EnrichRowsWithOperationsAsync(rows, cancellationToken);
        return rows;
    }

    private async Task<List<CaseQueueRow>> BuildRefundRowsAsync(int? sellerId, CancellationToken cancellationToken)
    {
        var query = _db.RefundTransactions
            .AsNoTracking()
            .Where(x => x.PaymentTxn != null && x.PaymentTxn.Order != null && x.PaymentTxn.Order.SellerOrders.Any())
            .AsQueryable();

        if (sellerId.HasValue)
        {
            query = query.Where(x => x.PaymentTxn.Order.SellerOrders.Any(so => so.SellerId == sellerId.Value));
        }

        var rows = await query
            .OrderByDescending(x => x.CreatedAt)
            .Select(x => new
            {
                x.RefundId,
                x.Status,
                x.Amount,
                x.Channel,
                x.ReferenceCode,
                x.CreatedAt,
                x.ProcessedAt,
                orderId = x.PaymentTxn.OrderId,
                buyerId = x.PaymentTxn.Order.UserId,
                buyerName = x.PaymentTxn.Order.BuyerFullName,
                buyerPhone = x.PaymentTxn.Order.BuyerPhone,
                sellerIds = x.PaymentTxn.Order.SellerOrders.Select(so => so.SellerId).Distinct().OrderBy(id => id).ToList()
            })
            .ToListAsync(cancellationToken);

        var mappedRows = rows.Select(x =>
        {
            var queueStatus = GetRefundQueueStatus(x.Status, x.CreatedAt, x.ProcessedAt);
            return new CaseQueueRow
            {
                CaseType = "refunds",
                CaseId = x.RefundId,
                Title = $"Refund #{x.RefundId}",
                Summary = $"Channel {x.Channel ?? "-"} / Ref {x.ReferenceCode ?? "-"}",
                DisplayStatus = x.Status,
                QueueStatus = queueStatus,
                SellerId = x.sellerIds.Count == 1 ? x.sellerIds[0] : sellerId,
                SellerLabel = sellerId.HasValue ? $"Seller #{sellerId.Value}" : FormatSellerLabel(x.sellerIds),
                BuyerId = x.buyerId,
                BuyerName = x.buyerName,
                BuyerPhone = x.buyerPhone,
                OrderId = x.orderId,
                Amount = x.Amount,
                UnreadCount = 0,
                IsSlaBreached = queueStatus == "breached",
                CreatedAt = x.CreatedAt,
                UpdatedAt = x.ProcessedAt ?? x.CreatedAt
            };
        }).ToList();

        await EnrichRowsWithOperationsAsync(mappedRows, cancellationToken);
        return mappedRows;
    }

    private async Task<IActionResult?> HandleCaseManagementActionAsync(
        string caseType,
        string targetType,
        int caseId,
        string actionName,
        string? note,
        string? assigneeLabel,
        int? slaHours,
        string? evidenceNote,
        decimal? amount,
        CancellationToken cancellationToken)
    {
        if (actionName is not ("assign" or "set-sla" or "add-evidence"))
        {
            return null;
        }

        var actorUserId = GetActorUserId();
        switch (actionName)
        {
            case "assign":
                if (string.IsNullOrWhiteSpace(assigneeLabel))
                {
                    return BadRequest(new { success = false, message = "Can nhap nguoi phu trach truoc khi gan case." });
                }

                {
                    var summary = $"Gan {CaseTypeLabel(caseType)} #{caseId} cho {assigneeLabel}.";
                    var actionLog = AdminAuditLogger.AddAction(
                        _db,
                        "dispute_center",
                        $"{caseType}_assign",
                        targetType,
                        caseId,
                        summary,
                        actorUserId,
                        new { caseType, actionName, note, assignedOwner = assigneeLabel });
                    AdminAuditLogger.AddModeration(_db, actionLog, targetType, caseId, actionName, note ?? $"Assigned to {assigneeLabel}.", actorUserId);
                    await _db.SaveChangesAsync(cancellationToken);
                    return Ok(new { success = true, message = $"Da gan {CaseTypeLabel(caseType).ToLowerInvariant()} #{caseId} cho {assigneeLabel}." });
                }
            case "set-sla":
                if (!slaHours.HasValue)
                {
                    return BadRequest(new { success = false, message = "Can chon moc SLA hop le." });
                }

                {
                    var targetResolutionAt = DateTime.UtcNow.AddHours(slaHours.Value);
                    var summary = $"Dat SLA cho {CaseTypeLabel(caseType).ToLowerInvariant()} #{caseId} den {targetResolutionAt:yyyy-MM-dd HH:mm} UTC.";
                    var actionLog = AdminAuditLogger.AddAction(
                        _db,
                        "dispute_center",
                        $"{caseType}_set_sla",
                        targetType,
                        caseId,
                        summary,
                        actorUserId,
                        new { caseType, actionName, note, slaHours, targetResolutionAt });
                    AdminAuditLogger.AddModeration(_db, actionLog, targetType, caseId, actionName, note ?? $"SLA set to {slaHours.Value}h.", actorUserId);
                    if (amount.HasValue)
                    {
                        AdminAuditLogger.AddSettlement(_db, actionLog, $"{caseType}_set_sla", targetType, caseId, null, amount, note, actorUserId);
                    }

                    await _db.SaveChangesAsync(cancellationToken);
                    return Ok(new { success = true, message = $"Da dat SLA {slaHours.Value}h cho {CaseTypeLabel(caseType).ToLowerInvariant()} #{caseId}." });
                }
            default:
                if (string.IsNullOrWhiteSpace(evidenceNote))
                {
                    return BadRequest(new { success = false, message = "Can nhap noi dung bang chung truoc khi luu." });
                }

                {
                    var actionLog = AdminAuditLogger.AddAction(
                        _db,
                        "dispute_center",
                        $"{caseType}_add_evidence",
                        targetType,
                        caseId,
                        $"Bo sung bang chung cho {CaseTypeLabel(caseType).ToLowerInvariant()} #{caseId}.",
                        actorUserId,
                        new { caseType, actionName, note, evidenceNote });
                    AdminAuditLogger.AddModeration(_db, actionLog, targetType, caseId, actionName, evidenceNote, actorUserId);
                    await _db.SaveChangesAsync(cancellationToken);
                    return Ok(new { success = true, message = $"Da bo sung bang chung cho {CaseTypeLabel(caseType).ToLowerInvariant()} #{caseId}." });
                }
        }
    }

    private async Task EnrichRowsWithOperationsAsync(List<CaseQueueRow> rows, CancellationToken cancellationToken)
    {
        if (rows.Count == 0)
        {
            return;
        }

        var supportIds = rows.Where(x => x.CaseType == "support").Select(x => x.CaseId).Distinct().ToList();
        var returnIds = rows.Where(x => x.CaseType == "returns").Select(x => x.CaseId).Distinct().ToList();
        var refundIds = rows.Where(x => x.CaseType == "refunds").Select(x => x.CaseId).Distinct().ToList();

        var logs = await _db.AdminActionLogs
            .AsNoTracking()
            .Where(x => x.Area == "dispute_center" && x.TargetId.HasValue &&
                ((x.TargetType == "support_case" && supportIds.Contains(x.TargetId.Value)) ||
                 (x.TargetType == "return_case" && returnIds.Contains(x.TargetId.Value)) ||
                 (x.TargetType == "refund_case" && refundIds.Contains(x.TargetId.Value))))
            .OrderByDescending(x => x.CreatedAt)
            .ToListAsync(cancellationToken);

        var snapshots = logs
            .GroupBy(x => $"{x.TargetType}:{x.TargetId}")
            .ToDictionary(g => g.Key, BuildOperationsSnapshot);

        foreach (var row in rows)
        {
            var key = $"{MapTargetType(row.CaseType)}:{row.CaseId}";
            if (!snapshots.TryGetValue(key, out var snapshot))
            {
                continue;
            }

            row.AssignedOwner = snapshot.Assignment?.OwnerLabel;
            row.TargetResolutionAt = snapshot.Sla?.TargetResolutionAt;
            row.EvidenceCount = snapshot.EvidenceItems.Count;
            row.UpdatedAt = MaxDate(row.UpdatedAt ?? row.CreatedAt, snapshot.LastActionAt);
            if (snapshot.Sla?.IsBreached == true && row.QueueStatus != "resolved")
            {
                row.IsSlaBreached = true;
            }
        }
    }

    private async Task<OperationsSnapshot> LoadOperationsSnapshotAsync(string targetType, int caseId, string queueStatus, CancellationToken cancellationToken)
    {
        var logs = await _db.AdminActionLogs
            .AsNoTracking()
            .Where(x => x.Area == "dispute_center" && x.TargetType == targetType && x.TargetId == caseId)
            .OrderByDescending(x => x.CreatedAt)
            .ToListAsync(cancellationToken);

        var snapshot = BuildOperationsSnapshot(logs);
        if (snapshot.Sla is not null)
        {
            snapshot.Sla.IsBreached = queueStatus != "resolved" && snapshot.Sla.TargetResolutionAt.HasValue && snapshot.Sla.TargetResolutionAt.Value <= DateTime.UtcNow;
        }

        snapshot.IsSlaBreached = queueStatus == "breached" || (snapshot.Sla?.IsBreached ?? false);
        return snapshot;
    }

    private static OperationsSnapshot BuildOperationsSnapshot(IEnumerable<AdminActionLog> logs)
    {
        var snapshot = new OperationsSnapshot();

        foreach (var log in logs.OrderByDescending(x => x.CreatedAt))
        {
            snapshot.LastActionAt = MaxDate(snapshot.LastActionAt, log.CreatedAt);
            var metadata = ParseMetadata(log.MetadataJson);

            if (log.ActionName.EndsWith("_assign", StringComparison.OrdinalIgnoreCase) && snapshot.Assignment is null)
            {
                snapshot.Assignment = new AssignmentSnapshot
                {
                    OwnerLabel = ReadString(metadata, "assignedOwner") ?? "Chưa rõ",
                    AssignedAt = log.CreatedAt
                };
            }

            if (log.ActionName.EndsWith("_set_sla", StringComparison.OrdinalIgnoreCase) && snapshot.Sla is null)
            {
                snapshot.Sla = new SlaSnapshot
                {
                    TargetResolutionAt = ReadDateTime(metadata, "targetResolutionAt"),
                    Source = "manual"
                };
            }

            if (log.ActionName.EndsWith("_add_evidence", StringComparison.OrdinalIgnoreCase))
            {
                snapshot.EvidenceItems.Add(new
                {
                    note = ReadString(metadata, "evidenceNote") ?? log.Summary,
                    createdAt = (DateTime?)log.CreatedAt,
                    actorUserId = log.ActorUserId
                });
            }

            snapshot.ActivityItems.Add(new
            {
                label = FormatActivityLabel(log.ActionName),
                summary = log.Summary,
                createdAt = (DateTime?)log.CreatedAt,
                actorUserId = log.ActorUserId
            });
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

        if (value.ValueKind == JsonValueKind.String && DateTime.TryParse(value.GetString(), out var parsed))
        {
            return parsed;
        }

        return null;
    }

    private static bool MatchesQuery(CaseQueueRow row, string? term)
    {
        if (string.IsNullOrWhiteSpace(term))
        {
            return true;
        }

        if (int.TryParse(term, out var numericId))
        {
            return row.CaseId == numericId || row.OrderId == numericId || row.BuyerId == numericId || row.SellerId == numericId;
        }

        return (row.Title?.Contains(term, StringComparison.OrdinalIgnoreCase) ?? false) ||
               (row.Summary?.Contains(term, StringComparison.OrdinalIgnoreCase) ?? false) ||
               (row.DisplayStatus?.Contains(term, StringComparison.OrdinalIgnoreCase) ?? false) ||
               (row.BuyerName?.Contains(term, StringComparison.OrdinalIgnoreCase) ?? false) ||
               (row.BuyerPhone?.Contains(term, StringComparison.OrdinalIgnoreCase) ?? false) ||
               (row.SellerLabel?.Contains(term, StringComparison.OrdinalIgnoreCase) ?? false);
    }

    private static bool MatchesStatus(CaseQueueRow row, string status)
    {
        if (status == "all")
        {
            return true;
        }

        return status switch
        {
            "breached" => row.IsSlaBreached,
            "resolved" => row.QueueStatus == "resolved",
            "pending" => row.QueueStatus == "pending",
            "open" => row.QueueStatus == "open" || row.QueueStatus == "pending" || row.QueueStatus == "breached",
            _ => true
        };
    }

    private static string NormalizeSection(string? section)
    {
        var normalized = (section ?? string.Empty).Trim().ToLowerInvariant();
        return normalized switch
        {
            "support" => "support",
            "returns" => "returns",
            "refunds" => "refunds",
            _ => "all"
        };
    }

    private static string NormalizeStatus(string? status)
    {
        var normalized = (status ?? string.Empty).Trim().ToLowerInvariant();
        return normalized switch
        {
            "open" => "open",
            "pending" => "pending",
            "breached" => "breached",
            "resolved" => "resolved",
            _ => "all"
        };
    }

    private static string NormalizeAction(string? actionName)
    {
        var normalized = (actionName ?? string.Empty).Trim().ToLowerInvariant();
        return normalized switch
        {
            "acknowledge" => "acknowledge",
            "approve" => "approve",
            "reject" => "reject",
            "resolve" => "resolve",
            "reopen" => "reopen",
            "assign" => "assign",
            "set-sla" or "set_sla" => "set-sla",
            "add-evidence" or "add_evidence" => "add-evidence",
            _ => "all"
        };
    }

    private static string GetActionLabel(string actionName)
    {
        return actionName switch
        {
            "acknowledge" => "nhan xu ly",
            "approve" => "phe duyet",
            "reject" => "tu choi",
            "resolve" => "dong",
            "reopen" => "mo lai",
            "assign" => "gan nguoi phu trach",
            "set-sla" => "cap nhat SLA",
            "add-evidence" => "bo sung bang chung",
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

    private static string? TrimShortText(string? value, int maxLength)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return null;
        }

        var trimmed = value.Trim();
        return trimmed.Length <= maxLength ? trimmed : trimmed[..maxLength];
    }

    private static int? NormalizeSlaHours(int? value)
    {
        if (!value.HasValue || value.Value <= 0)
        {
            return null;
        }

        return value.Value is <= 168 ? value.Value : 168;
    }

    private static string CaseTypeLabel(string caseType)
    {
        return caseType switch
        {
            "support" => "case CS",
            "returns" => "case doi tra",
            "refunds" => "case refund",
            _ => "case"
        };
    }

    private static string MapTargetType(string caseType)
    {
        return caseType switch
        {
            "support" => "support_case",
            "returns" => "return_case",
            "refunds" => "refund_case",
            _ => "unknown_case"
        };
    }

    private static string FormatActivityLabel(string actionName)
    {
        return actionName switch
        {
            var value when value.EndsWith("_assign", StringComparison.OrdinalIgnoreCase) => "Gan nguoi phu trach",
            var value when value.EndsWith("_set_sla", StringComparison.OrdinalIgnoreCase) => "Cap nhat SLA",
            var value when value.EndsWith("_add_evidence", StringComparison.OrdinalIgnoreCase) => "Bo sung bang chung",
            var value when value.EndsWith("_acknowledge", StringComparison.OrdinalIgnoreCase) => "Nhan xu ly",
            var value when value.EndsWith("_approve", StringComparison.OrdinalIgnoreCase) => "Phe duyet",
            var value when value.EndsWith("_reject", StringComparison.OrdinalIgnoreCase) => "Tu choi",
            var value when value.EndsWith("_resolve", StringComparison.OrdinalIgnoreCase) => "Dong case",
            var value when value.EndsWith("_reopen", StringComparison.OrdinalIgnoreCase) => "Mo lai case",
            _ => actionName
        };
    }

    private static DateTime? MaxDate(DateTime? left, DateTime? right)
    {
        if (!left.HasValue)
        {
            return right;
        }

        if (!right.HasValue)
        {
            return left;
        }

        return left.Value >= right.Value ? left : right;
    }

    private int? GetActorUserId()
    {
        var raw = User.FindFirstValue(ClaimTypes.NameIdentifier) ?? User.FindFirstValue("sub");
        return int.TryParse(raw, out var actorUserId) ? actorUserId : null;
    }

    private static string GetSupportQueueStatus(string? sourceStatus, bool hasUnreadBuyerMessage, DateTime startedAt, DateTime? lastBuyerMessageAt)
    {
        if (string.Equals(sourceStatus, "Closed", StringComparison.OrdinalIgnoreCase))
        {
            return "resolved";
        }

        var anchor = lastBuyerMessageAt ?? startedAt;
        if (DateTime.UtcNow - anchor >= TimeSpan.FromHours(24))
        {
            return "breached";
        }

        return hasUnreadBuyerMessage ? "pending" : "open";
    }

    private static string GetReturnQueueStatus(string? status, DateTime requestedAt, DateTime? completedAt)
    {
        var normalized = (status ?? string.Empty).Trim().ToLowerInvariant();
        if (ReturnResolvedStatuses.Contains(normalized) || ReturnFailedStatuses.Contains(normalized) || completedAt.HasValue)
        {
            return "resolved";
        }

        return DateTime.UtcNow - requestedAt >= TimeSpan.FromHours(48) ? "breached" : "pending";
    }

    private static string GetRefundQueueStatus(string? status, DateTime createdAt, DateTime? processedAt)
    {
        var normalized = (status ?? string.Empty).Trim().ToLowerInvariant();
        if (RefundResolvedStatuses.Contains(normalized) || RefundFailedStatuses.Contains(normalized) || processedAt.HasValue)
        {
            return "resolved";
        }

        if (DateTime.UtcNow - createdAt >= TimeSpan.FromHours(48))
        {
            return "breached";
        }

        return RefundOpenStatuses.Contains(normalized) ? "pending" : "open";
    }

    private static string FormatSellerLabel(IReadOnlyCollection<int> sellerIds)
    {
        if (sellerIds.Count == 0)
        {
            return "Chưa rõ seller";
        }

        if (sellerIds.Count == 1)
        {
            return $"Seller #{sellerIds.First()}";
        }

        var first = sellerIds.OrderBy(x => x).First();
        return $"Seller #{first} +{sellerIds.Count - 1}";
    }

    private sealed class CaseQueueRow
    {
        public string CaseType { get; init; } = string.Empty;
        public int CaseId { get; init; }
        public string Title { get; init; } = string.Empty;
        public string Summary { get; init; } = string.Empty;
        public string DisplayStatus { get; init; } = string.Empty;
        public string QueueStatus { get; init; } = string.Empty;
        public int? SellerId { get; init; }
        public string SellerLabel { get; init; } = string.Empty;
        public int BuyerId { get; init; }
        public string BuyerName { get; init; } = string.Empty;
        public string? BuyerPhone { get; init; }
        public int? OrderId { get; init; }
        public decimal? Amount { get; init; }
        public int UnreadCount { get; init; }
        public string? AssignedOwner { get; set; }
        public DateTime? TargetResolutionAt { get; set; }
        public int EvidenceCount { get; set; }
        public bool IsSlaBreached { get; set; }
        public DateTime CreatedAt { get; init; }
        public DateTime? UpdatedAt { get; set; }
    }

    public sealed class DisputeActionRequest
    {
        public string? CaseType { get; set; }
        public int CaseId { get; set; }
        public string? ActionName { get; set; }
        public string? Note { get; set; }
        public string? AssigneeLabel { get; set; }
        public int? SlaHours { get; set; }
        public string? EvidenceNote { get; set; }
    }

    private sealed class OperationsSnapshot
    {
        public AssignmentSnapshot? Assignment { get; set; }
        public SlaSnapshot? Sla { get; set; }
        public List<object> EvidenceItems { get; } = new();
        public List<object> ActivityItems { get; } = new();
        public DateTime? LastActionAt { get; set; }
        public bool IsSlaBreached { get; set; }
    }

    private sealed class AssignmentSnapshot
    {
        public string OwnerLabel { get; set; } = string.Empty;
        public DateTime? AssignedAt { get; set; }
    }

    private sealed class SlaSnapshot
    {
        public DateTime? TargetResolutionAt { get; set; }
        public string Source { get; set; } = string.Empty;
        public bool IsBreached { get; set; }
    }
}
