using FreshFarm.Ordering.Api.Models;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

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

        return Ok(new
        {
            caseType = "support",
            caseId = conversation.ConversationId,
            title = $"Hội thoại #{conversation.ConversationId}",
            summary = "Case CS được bridge từ support chat hiện tại.",
            displayStatus = string.IsNullOrWhiteSpace(conversation.Status) ? "Open" : conversation.Status,
            queueStatus,
            isSlaBreached = queueStatus == "breached",
            createdAt = conversation.StartedAt,
            updatedAt = conversation.ClosedAt ?? messages.Select(x => (DateTime?)x.CreatedAt).FirstOrDefault() ?? conversation.StartedAt,
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
            timeline = new[]
            {
                new { label = "Mở hội thoại", value = (DateTime?)conversation.StartedAt, tone = "info" },
                new { label = "Đóng hội thoại", value = conversation.ClosedAt, tone = "success" }
            },
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

        return Ok(new
        {
            caseType = "returns",
            caseId = item.ReturnId,
            title = $"Return #{item.ReturnId} - {item.itemName}",
            summary = item.Description,
            displayStatus = item.Status,
            queueStatus,
            isSlaBreached = queueStatus == "breached",
            createdAt = item.RequestedAt,
            updatedAt = item.CompletedAt ?? item.ApprovedAt ?? item.RequestedAt,
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
            timeline = new[]
            {
                new { label = "Yêu cầu tạo", value = (DateTime?)item.RequestedAt, tone = "info" },
                new { label = "Phê duyệt", value = item.ApprovedAt, tone = "warning" },
                new { label = "Hoàn tất", value = item.CompletedAt, tone = "success" }
            },
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

        return Ok(new
        {
            caseType = "refunds",
            caseId = item.RefundId,
            title = $"Refund #{item.RefundId}",
            summary = $"Provider {item.provider} / Ref {item.ReferenceCode ?? item.providerRef ?? "-"}",
            displayStatus = item.Status,
            queueStatus,
            isSlaBreached = queueStatus == "breached",
            createdAt = item.CreatedAt,
            updatedAt = item.ProcessedAt ?? item.CreatedAt,
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
            timeline = new[]
            {
                new { label = "Khởi tạo refund", value = (DateTime?)item.CreatedAt, tone = "info" },
                new { label = "Xử lý xong", value = item.ProcessedAt, tone = "success" }
            },
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

        return conversations
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
    }

    private async Task<List<CaseQueueRow>> BuildReturnRowsAsync(int? sellerId, CancellationToken cancellationToken)
    {
        var query = _db.ReturnRequests.AsNoTracking().AsQueryable();
        if (sellerId.HasValue)
        {
            query = query.Where(x => x.SellerId == sellerId.Value);
        }

        return await query
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

        return rows.Select(x =>
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
        public bool IsSlaBreached { get; init; }
        public DateTime CreatedAt { get; init; }
        public DateTime? UpdatedAt { get; init; }
    }
}
