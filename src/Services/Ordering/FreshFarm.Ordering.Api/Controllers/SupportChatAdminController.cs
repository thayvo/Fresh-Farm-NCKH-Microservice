using FreshFarm.Ordering.Api.Models;
using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Text.Json.Serialization;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace FreshFarm.Ordering.Api.Controllers;

[ApiController]
[Route("api/orders/admin/support-chat")]
[Authorize(Policy = "SellerOrAdmin")]
public sealed class SupportChatAdminController : ControllerBase
{
    private readonly FreshFarmOrderingDBContext _db;

    public SupportChatAdminController(FreshFarmOrderingDBContext db)
    {
        _db = db;
    }

    [HttpGet("conversations")]
    public async Task<IActionResult> Conversations(CancellationToken cancellationToken = default)
    {
        var scopedOrders = BuildScopedOrdersQuery();
        if (scopedOrders is null)
        {
            return Unauthorized(new { ok = false, message = "Không xác định được phạm vi hội thoại." });
        }

        var sellerUserIds = await scopedOrders
            .AsNoTracking()
            .Select(o => o.UserId)
            .Distinct()
            .ToListAsync(cancellationToken);

        var conversations = await _db.SupportConversations
            .AsNoTracking()
            .Where(x => x.UserId.HasValue && sellerUserIds.Contains(x.UserId.Value))
            .OrderByDescending(x => x.StartedAt)
            .ToListAsync(cancellationToken);

        var conversationIds = conversations.Select(x => x.ConversationId).ToList();
        var messages = await _db.SupportMessages
            .AsNoTracking()
            .Where(x => conversationIds.Contains(x.ConversationId))
            .ToListAsync(cancellationToken);

        var userIds = conversations
            .Where(x => x.UserId.HasValue)
            .Select(x => x.UserId!.Value)
            .Distinct()
            .ToList();

        var latestOrdersByUser = await scopedOrders
            .AsNoTracking()
            .Where(o => userIds.Contains(o.UserId))
            .GroupBy(o => o.UserId)
            .Select(g => g.OrderByDescending(x => x.OrderDate).First())
            .ToDictionaryAsync(x => x.UserId, cancellationToken);

        var payload = conversations
            .Select(conversation =>
            {
                var convMessages = messages
                    .Where(m => m.ConversationId == conversation.ConversationId)
                    .OrderBy(m => m.CreatedAt)
                    .ToList();

                var lastMessage = convMessages.LastOrDefault();
                var hasUnread = convMessages.Any(m => m.SenderType == 0 && !m.IsDeleted && !m.IsRead);

                latestOrdersByUser.TryGetValue(conversation.UserId ?? 0, out var userOrder);

                return new
                {
                    conversationId = conversation.ConversationId,
                    userId = conversation.UserId ?? 0,
                    userName = string.IsNullOrWhiteSpace(userOrder?.BuyerFullName)
                        ? $"Khach #{conversation.UserId ?? 0}"
                        : userOrder!.BuyerFullName,
                    userPhone = string.IsNullOrWhiteSpace(userOrder?.BuyerPhone) ? (string?)null : userOrder!.BuyerPhone,
                    guestId = conversation.GuestId,
                    lastContent = lastMessage?.Content ?? string.Empty,
                    lastTime = lastMessage?.CreatedAt ?? conversation.StartedAt,
                    hasUnread,
                    status = conversation.Status
                };
            })
            .OrderByDescending(x => x.lastTime)
            .ToList();

        return Ok(new { ok = true, conversations = payload });
    }

    [HttpGet("conversations/{conversationId:int}/messages")]
    public async Task<IActionResult> Messages([FromRoute] int conversationId, [FromQuery] int take = 100, CancellationToken cancellationToken = default)
    {
        if (take <= 0)
        {
            take = 100;
        }

        if (take > 500)
        {
            take = 500;
        }

        var exists = await CanAccessConversationAsync(conversationId, cancellationToken);

        if (!exists)
        {
            return Ok(new { ok = true, messages = Array.Empty<object>() });
        }

        var source = await _db.SupportMessages
            .AsNoTracking()
            .Where(x => x.ConversationId == conversationId)
            .OrderBy(x => x.CreatedAt)
            .ToListAsync(cancellationToken);

        var reactions = await _db.SupportMessageReactions
            .AsNoTracking()
            .Where(x => source.Select(m => m.MessageId).Contains(x.MessageId))
            .OrderByDescending(x => x.CreatedAt)
            .ToListAsync(cancellationToken);

        var latest = source.TakeLast(take).ToList();

        var payload = latest.Select(msg => new
        {
            messageId = msg.MessageId,
            from = msg.SenderType == 0 ? "user" : "admin",
            content = msg.Content,
            createdAt = msg.CreatedAt,
            isDeleted = msg.IsDeleted,
            reactionType = reactions.FirstOrDefault(x => x.MessageId == msg.MessageId)?.ReactionType,
            replyTo = msg.ReplyToMessageId.HasValue
                ? source.Where(x => x.MessageId == msg.ReplyToMessageId.Value)
                    .Select(x => new
                    {
                        messageId = x.MessageId,
                        from = x.SenderType == 0 ? "user" : "admin",
                        content = x.Content
                    })
                    .FirstOrDefault()
                : null
        }).ToList();

        return Ok(new { ok = true, messages = payload });
    }

    [HttpGet("conversations/{conversationId:int}/details")]
    public async Task<IActionResult> ConversationDetails([FromRoute] int conversationId, CancellationToken cancellationToken = default)
    {
        var scopedOrders = BuildScopedOrdersQuery();
        if (scopedOrders is null)
        {
            return Unauthorized(new { ok = false, message = "Không xác định được phạm vi hội thoại." });
        }

        var conversation = await _db.SupportConversations
            .AsNoTracking()
            .FirstOrDefaultAsync(x => x.ConversationId == conversationId, cancellationToken);

        if (conversation is null || !await CanAccessConversationAsync(conversationId, cancellationToken))
        {
            return NotFound(new { ok = false, message = "Khong tim thay hoi thoai." });
        }

        var userId = conversation.UserId ?? 0;
        var latestOrder = await scopedOrders
            .AsNoTracking()
            .Where(o => o.UserId == userId)
            .OrderByDescending(o => o.OrderDate)
            .FirstOrDefaultAsync(cancellationToken);

        var orders = await scopedOrders
            .AsNoTracking()
            .Where(o => o.UserId == userId)
            .OrderByDescending(o => o.OrderDate)
            .Take(5)
            .Select(o => new
            {
                orderId = o.OrderId,
                orderCode = "#" + o.OrderId.ToString("D6"),
                orderDate = o.OrderDate,
                totalAmount = o.TotalAmount,
                status = o.Status
            })
            .ToListAsync(cancellationToken);

        var profile = new
        {
            userId,
            fullName = string.IsNullOrWhiteSpace(latestOrder?.BuyerFullName) ? $"Khach #{userId}" : latestOrder!.BuyerFullName,
            email = latestOrder?.BuyerEmail,
            phone = latestOrder?.BuyerPhone,
            avatarUrl = (string?)null,
            totalPoints = 0,
            rankName = (string?)null
        };

        return Ok(new { ok = true, profile, orders });
    }

    [HttpPost("conversations/{conversationId:int}/close")]
    public async Task<IActionResult> Close([FromRoute] int conversationId, CancellationToken cancellationToken = default)
    {
        var conversation = await _db.SupportConversations
            .FirstOrDefaultAsync(x => x.ConversationId == conversationId, cancellationToken);

        if (conversation is null || !await CanAccessConversationAsync(conversationId, cancellationToken))
        {
            return NotFound(new { ok = false, message = "Khong tim thay hoi thoai." });
        }

        conversation.Status = "Closed";
        conversation.ClosedAt = DateTime.UtcNow;
        await _db.SaveChangesAsync(cancellationToken);

        return Ok(new { ok = true });
    }

    [HttpPost("conversations/{conversationId:int}/mark-read")]
    public async Task<IActionResult> MarkAsRead([FromRoute] int conversationId, CancellationToken cancellationToken = default)
    {
        var conversationExists = await CanAccessConversationAsync(conversationId, cancellationToken);

        if (!conversationExists)
        {
            return NotFound(new { ok = false, message = "Khong tim thay hoi thoai." });
        }

        var messages = await _db.SupportMessages
            .Where(x => x.ConversationId == conversationId && x.SenderType == 0 && !x.IsDeleted && !x.IsRead)
            .ToListAsync(cancellationToken);

        if (messages.Count > 0)
        {
            foreach (var message in messages)
            {
                message.IsRead = true;
            }

            await _db.SaveChangesAsync(cancellationToken);
        }

        return Ok(new { ok = true });
    }

    [HttpPost("conversations/{conversationId:int}/messages")]
    public async Task<IActionResult> SendMessage(
        [FromRoute] int conversationId,
        [FromBody] SendSupportMessageRequest? request,
        CancellationToken cancellationToken = default)
    {
        var actorId = TryGetActorIdFromToken();
        if (!actorId.HasValue)
        {
            return Unauthorized(new { ok = false, message = "Không xác định được người gửi." });
        }

        if (!await CanAccessConversationAsync(conversationId, cancellationToken))
        {
            return NotFound(new { ok = false, message = "Khong tim thay hoi thoai." });
        }

        var content = (request?.Content ?? string.Empty).Trim();
        if (string.IsNullOrWhiteSpace(content))
        {
            return BadRequest(new { ok = false, message = "Noi dung tin nhan khong duoc de trong." });
        }

        if (content.Length > 4000)
        {
            return BadRequest(new { ok = false, message = "Noi dung tin nhan qua dai (toi da 4000 ky tu)." });
        }

        var conversation = await _db.SupportConversations
            .FirstOrDefaultAsync(x => x.ConversationId == conversationId, cancellationToken);

        if (conversation is null)
        {
            return NotFound(new { ok = false, message = "Khong tim thay hoi thoai." });
        }

        if (string.Equals(conversation.Status, "Closed", StringComparison.OrdinalIgnoreCase))
        {
            return BadRequest(new { ok = false, message = "Hoi thoai da ket thuc. Khong the gui tin nhan moi." });
        }

        SupportMessage? replyMessage = null;
        if (request?.ReplyToMessageId is int replyToMessageId && replyToMessageId > 0)
        {
            replyMessage = await _db.SupportMessages
                .AsNoTracking()
                .FirstOrDefaultAsync(
                    x => x.MessageId == replyToMessageId && x.ConversationId == conversationId,
                    cancellationToken);

            if (replyMessage is null)
            {
                return BadRequest(new { ok = false, message = "Khong tim thay tin nhan goc de tra loi." });
            }
        }

        if (!conversation.AdminId.HasValue || conversation.AdminId.Value <= 0)
        {
            conversation.AdminId = actorId.Value;
        }

        var now = DateTime.UtcNow;
        var message = new SupportMessage
        {
            ConversationId = conversationId,
            SenderType = 1, // admin/seller
            SenderAdminId = actorId.Value,
            Content = content,
            CreatedAt = now,
            IsRead = false,
            ReplyToMessageId = replyMessage?.MessageId,
            IsDeleted = false
        };

        _db.SupportMessages.Add(message);
        await _db.SaveChangesAsync(cancellationToken);

        return Ok(new
        {
            ok = true,
            message = new
            {
                messageId = message.MessageId,
                from = "admin",
                content = message.Content,
                createdAt = message.CreatedAt,
                isDeleted = false,
                reactionType = (string?)null,
                replyTo = replyMessage is null
                    ? null
                    : new
                    {
                        messageId = replyMessage.MessageId,
                        from = replyMessage.SenderType == 0 ? "user" : "admin",
                        content = replyMessage.Content
                    }
            }
        });
    }

    private int? TryGetActorIdFromToken()
    {
        var sub = User.FindFirstValue(JwtRegisteredClaimNames.Sub)
                  ?? User.FindFirstValue(ClaimTypes.NameIdentifier)
                  ?? User.FindFirstValue("sub");

        return int.TryParse(sub, out var actorId) ? actorId : null;
    }

    private IQueryable<Order> ApplySellerScopeToOrders(IQueryable<Order> query, int sellerId)
    {
        return query.Where(o => o.SellerOrders.Any(so =>
            so.SellerId == sellerId &&
            so.SellerOrderItems.Any()));
    }

    private IQueryable<Order>? BuildScopedOrdersQuery()
    {
        if (User.IsInRole("Admin"))
        {
            return _db.Orders.AsQueryable();
        }

        var sellerId = TryGetActorIdFromToken();
        if (!sellerId.HasValue)
        {
            return null;
        }

        return ApplySellerScopeToOrders(_db.Orders.AsQueryable(), sellerId.Value);
    }

    private async Task<bool> CanAccessConversationAsync(int conversationId, CancellationToken cancellationToken)
    {
        var userId = await _db.SupportConversations
            .AsNoTracking()
            .Where(x => x.ConversationId == conversationId)
            .Select(x => x.UserId)
            .FirstOrDefaultAsync(cancellationToken);

        if (!userId.HasValue || userId.Value <= 0)
        {
            return false;
        }

        var scopedOrders = BuildScopedOrdersQuery();
        if (scopedOrders is null)
        {
            return false;
        }

        return await scopedOrders
            .AsNoTracking()
            .AnyAsync(o => o.UserId == userId.Value, cancellationToken);
    }

    public sealed class SendSupportMessageRequest
    {
        [JsonPropertyName("content")]
        public string? Content { get; set; }

        [JsonPropertyName("replyToMessageId")]
        public int? ReplyToMessageId { get; set; }
    }
}
