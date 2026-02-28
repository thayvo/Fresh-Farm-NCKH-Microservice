using FreshFarm.Ordering.Api.Models;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace FreshFarm.Ordering.Api.Controllers;

[ApiController]
[Route("api/orders/admin/support-chat")]
[Authorize(Policy = "SellerOnly")]
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
        await EnsureSeedDataAsync(cancellationToken);

        var conversations = await _db.SupportConversations
            .AsNoTracking()
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

        var latestOrdersByUser = await _db.Orders
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
        await EnsureSeedDataAsync(cancellationToken);

        if (take <= 0)
        {
            take = 100;
        }

        if (take > 500)
        {
            take = 500;
        }

        var exists = await _db.SupportConversations
            .AsNoTracking()
            .AnyAsync(x => x.ConversationId == conversationId, cancellationToken);

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
        await EnsureSeedDataAsync(cancellationToken);

        var conversation = await _db.SupportConversations
            .AsNoTracking()
            .FirstOrDefaultAsync(x => x.ConversationId == conversationId, cancellationToken);

        if (conversation is null)
        {
            return NotFound(new { ok = false, message = "Khong tim thay hoi thoai." });
        }

        var userId = conversation.UserId ?? 0;
        var latestOrder = await _db.Orders
            .AsNoTracking()
            .Where(o => o.UserId == userId)
            .OrderByDescending(o => o.OrderDate)
            .FirstOrDefaultAsync(cancellationToken);

        var orders = await _db.Orders
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
        await EnsureSeedDataAsync(cancellationToken);

        var conversation = await _db.SupportConversations
            .FirstOrDefaultAsync(x => x.ConversationId == conversationId, cancellationToken);

        if (conversation is null)
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
        await EnsureSeedDataAsync(cancellationToken);

        var conversationExists = await _db.SupportConversations
            .AsNoTracking()
            .AnyAsync(x => x.ConversationId == conversationId, cancellationToken);

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

    private async Task EnsureSeedDataAsync(CancellationToken cancellationToken)
    {
        var hasAnyConversation = await _db.SupportConversations
            .AsNoTracking()
            .AnyAsync(cancellationToken);

        if (hasAnyConversation)
        {
            return;
        }

        var recentOrders = await _db.Orders
            .AsNoTracking()
            .OrderByDescending(o => o.OrderDate)
            .Take(200)
            .ToListAsync(cancellationToken);

        var conversationByUser = new Dictionary<int, SupportConversation>();

        foreach (var group in recentOrders.GroupBy(o => o.UserId))
        {
            var userId = group.Key;
            if (userId <= 0)
            {
                continue;
            }

            var latestOrder = group.OrderByDescending(x => x.OrderDate).First();
            var conversation = new SupportConversation
            {
                UserId = userId,
                Status = "Open",
                StartedAt = latestOrder.OrderDate
            };

            _db.SupportConversations.Add(conversation);
            conversationByUser[userId] = conversation;
        }

        if (conversationByUser.Count == 0)
        {
            return;
        }

        await _db.SaveChangesAsync(cancellationToken);

        foreach (var item in conversationByUser)
        {
            var latestOrder = recentOrders
                .Where(x => x.UserId == item.Key)
                .OrderByDescending(x => x.OrderDate)
                .First();

            _db.SupportMessages.Add(new SupportMessage
            {
                ConversationId = item.Value.ConversationId,
                SenderType = 0,
                SenderUserId = item.Key,
                Content = $"Xin chao shop, toi can ho tro don #{latestOrder.OrderId:D6}.",
                CreatedAt = latestOrder.OrderDate.AddMinutes(1),
                IsRead = false,
                IsDeleted = false
            });
        }

        await _db.SaveChangesAsync(cancellationToken);
    }
}
