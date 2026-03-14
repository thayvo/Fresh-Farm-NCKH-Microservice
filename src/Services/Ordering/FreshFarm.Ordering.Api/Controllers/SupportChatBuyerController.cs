using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Text.Json.Serialization;
using FreshFarm.Ordering.Api.Models;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace FreshFarm.Ordering.Api.Controllers;

[ApiController]
[Route("api/orders/support-chat")]
[Authorize]
public sealed class SupportChatBuyerController : ControllerBase
{
    private readonly FreshFarmOrderingDBContext _db;

    public SupportChatBuyerController(FreshFarmOrderingDBContext db)
    {
        _db = db;
    }

    [HttpGet("summaries")]
    public async Task<IActionResult> Summaries([FromQuery] int[]? sellerIds, CancellationToken cancellationToken = default)
    {
        var buyerId = TryGetBuyerIdFromToken();
        if (!buyerId.HasValue)
        {
            return Unauthorized(new { ok = false, message = "Không xác định được người mua." });
        }

        var normalizedSellerIds = (sellerIds ?? Array.Empty<int>())
            .Where(x => x > 0)
            .Distinct()
            .Take(50)
            .ToArray();

        if (normalizedSellerIds.Length == 0)
        {
            return Ok(new { ok = true, summaries = Array.Empty<object>() });
        }

        var conversations = await _db.SupportConversations
            .AsNoTracking()
            .Where(x => x.UserId == buyerId.Value && x.AdminId.HasValue && normalizedSellerIds.Contains(x.AdminId.Value))
            .OrderByDescending(x => x.StartedAt)
            .ToListAsync(cancellationToken);

        if (conversations.Count == 0)
        {
            return Ok(new { ok = true, summaries = Array.Empty<object>() });
        }

        var conversationIds = conversations.Select(x => x.ConversationId).ToList();
        var lastMessages = await _db.SupportMessages
            .AsNoTracking()
            .Where(x => conversationIds.Contains(x.ConversationId) && !x.IsDeleted)
            .GroupBy(x => x.ConversationId)
            .Select(g => g.OrderByDescending(x => x.CreatedAt).First())
            .ToListAsync(cancellationToken);

        var unreadConversationIds = await _db.SupportMessages
            .AsNoTracking()
            .Where(x => conversationIds.Contains(x.ConversationId) && x.SenderType == 1 && !x.IsDeleted && !x.IsRead)
            .Select(x => x.ConversationId)
            .Distinct()
            .ToListAsync(cancellationToken);

        var lastMessagesByConversation = lastMessages.ToDictionary(x => x.ConversationId);
        var unreadConversationSet = unreadConversationIds.ToHashSet();

        var summaries = conversations
            .GroupBy(x => x.AdminId!.Value)
            .Select(group =>
            {
                var conversation = group
                    .OrderByDescending(x =>
                    {
                        if (lastMessagesByConversation.TryGetValue(x.ConversationId, out var lastMessage))
                        {
                            return lastMessage.CreatedAt;
                        }

                        return x.StartedAt;
                    })
                    .First();

                lastMessagesByConversation.TryGetValue(conversation.ConversationId, out var lastMessage);

                return new
                {
                    sellerId = group.Key,
                    conversationId = conversation.ConversationId,
                    status = conversation.Status,
                    startedAt = conversation.StartedAt,
                    closedAt = conversation.ClosedAt,
                    lastContent = lastMessage?.Content ?? string.Empty,
                    lastTime = lastMessage?.CreatedAt ?? conversation.StartedAt,
                    hasUnread = unreadConversationSet.Contains(conversation.ConversationId)
                };
            })
            .OrderBy(x => x.sellerId)
            .ToList();

        return Ok(new { ok = true, summaries });
    }

    [HttpGet("sellers/{sellerId:int}/conversation")]
    public async Task<IActionResult> GetConversation(
        [FromRoute] int sellerId,
        [FromQuery] bool createIfMissing = false,
        CancellationToken cancellationToken = default)
    {
        if (sellerId <= 0)
        {
            return BadRequest(new { ok = false, message = "sellerId không hợp lệ." });
        }

        var buyerId = TryGetBuyerIdFromToken();
        if (!buyerId.HasValue)
        {
            return Unauthorized(new { ok = false, message = "Không xác định được người mua." });
        }

        var conversation = await _db.SupportConversations
            .AsNoTracking()
            .Where(x => x.UserId == buyerId.Value && x.AdminId == sellerId)
            .OrderByDescending(x => x.StartedAt)
            .FirstOrDefaultAsync(cancellationToken);

        if (conversation is null && createIfMissing)
        {
            var now = DateTime.UtcNow;
            var newConversation = new SupportConversation
            {
                UserId = buyerId.Value,
                AdminId = sellerId,
                StartedAt = now,
                Status = "Open",
                GuestId = null!,
                ClosedAt = null
            };

            _db.SupportConversations.Add(newConversation);
            await _db.SaveChangesAsync(cancellationToken);

            conversation = await _db.SupportConversations
                .AsNoTracking()
                .FirstAsync(x => x.ConversationId == newConversation.ConversationId, cancellationToken);
        }

        if (conversation is null)
        {
            return Ok(new { ok = true, conversation = (object?)null });
        }

        var lastMessage = await _db.SupportMessages
            .AsNoTracking()
            .Where(x => x.ConversationId == conversation.ConversationId && !x.IsDeleted)
            .OrderByDescending(x => x.CreatedAt)
            .Select(x => new { x.Content, x.CreatedAt })
            .FirstOrDefaultAsync(cancellationToken);

        var hasUnread = await _db.SupportMessages
            .AsNoTracking()
            .AnyAsync(
                x => x.ConversationId == conversation.ConversationId &&
                     x.SenderType == 1 &&
                     !x.IsDeleted &&
                     !x.IsRead,
                cancellationToken);

        return Ok(new
        {
            ok = true,
            conversation = new
            {
                conversationId = conversation.ConversationId,
                sellerId,
                status = conversation.Status,
                startedAt = conversation.StartedAt,
                closedAt = conversation.ClosedAt,
                lastContent = lastMessage?.Content ?? string.Empty,
                lastTime = lastMessage?.CreatedAt ?? conversation.StartedAt,
                hasUnread
            }
        });
    }

    [HttpGet("conversations/{conversationId:int}/messages")]
    public async Task<IActionResult> Messages(
        [FromRoute] int conversationId,
        [FromQuery] int take = 100,
        CancellationToken cancellationToken = default)
    {
        if (take <= 0)
        {
            take = 100;
        }

        if (take > 500)
        {
            take = 500;
        }

        if (!await CanAccessConversationAsync(conversationId, cancellationToken))
        {
            return NotFound(new { ok = false, message = "Không tìm thấy hội thoại." });
        }

        var source = await _db.SupportMessages
            .AsNoTracking()
            .Where(x => x.ConversationId == conversationId)
            .OrderBy(x => x.CreatedAt)
            .ToListAsync(cancellationToken);

        var latest = source.TakeLast(take).ToList();

        var payload = latest.Select(msg => new
        {
            messageId = msg.MessageId,
            from = msg.SenderType == 0 ? "buyer" : "seller",
            content = msg.Content,
            createdAt = msg.CreatedAt,
            isDeleted = msg.IsDeleted,
            replyTo = msg.ReplyToMessageId.HasValue
                ? source.Where(x => x.MessageId == msg.ReplyToMessageId.Value)
                    .Select(x => new
                    {
                        messageId = x.MessageId,
                        from = x.SenderType == 0 ? "buyer" : "seller",
                        content = x.Content
                    })
                    .FirstOrDefault()
                : null
        }).ToList();

        return Ok(new { ok = true, messages = payload });
    }

    [HttpPost("conversations/{conversationId:int}/messages")]
    public async Task<IActionResult> SendMessage(
        [FromRoute] int conversationId,
        [FromBody] SendBuyerSupportMessageRequest? request,
        CancellationToken cancellationToken = default)
    {
        var buyerId = TryGetBuyerIdFromToken();
        if (!buyerId.HasValue)
        {
            return Unauthorized(new { ok = false, message = "Không xác định được người mua." });
        }

        var conversation = await _db.SupportConversations
            .FirstOrDefaultAsync(x => x.ConversationId == conversationId, cancellationToken);

        if (conversation is null || !await CanAccessConversationAsync(conversationId, cancellationToken))
        {
            return NotFound(new { ok = false, message = "Không tìm thấy hội thoại." });
        }

        if (string.Equals(conversation.Status, "Closed", StringComparison.OrdinalIgnoreCase))
        {
            return BadRequest(new { ok = false, message = "Hội thoại đã kết thúc. Không thể gửi tin nhắn mới." });
        }

        var content = (request?.Content ?? string.Empty).Trim();
        if (string.IsNullOrWhiteSpace(content))
        {
            return BadRequest(new { ok = false, message = "Nội dung tin nhắn không được để trống." });
        }

        if (content.Length > 4000)
        {
            return BadRequest(new { ok = false, message = "Nội dung tin nhắn quá dài (tối đa 4000 ký tự)." });
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
                return BadRequest(new { ok = false, message = "Không tìm thấy tin nhắn gốc để trả lời." });
            }
        }

        var message = new SupportMessage
        {
            ConversationId = conversationId,
            SenderType = 0,
            SenderUserId = buyerId.Value,
            Content = content,
            CreatedAt = DateTime.UtcNow,
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
                from = "buyer",
                content = message.Content,
                createdAt = message.CreatedAt,
                isDeleted = false,
                replyTo = replyMessage is null
                    ? null
                    : new
                    {
                        messageId = replyMessage.MessageId,
                        from = replyMessage.SenderType == 0 ? "buyer" : "seller",
                        content = replyMessage.Content
                    }
            }
        });
    }

    [HttpPost("conversations/{conversationId:int}/mark-read")]
    public async Task<IActionResult> MarkAsRead([FromRoute] int conversationId, CancellationToken cancellationToken = default)
    {
        if (!await CanAccessConversationAsync(conversationId, cancellationToken))
        {
            return NotFound(new { ok = false, message = "Không tìm thấy hội thoại." });
        }

        var messages = await _db.SupportMessages
            .Where(x => x.ConversationId == conversationId && x.SenderType == 1 && !x.IsDeleted && !x.IsRead)
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

    private int? TryGetBuyerIdFromToken()
    {
        var sub = User.FindFirstValue(JwtRegisteredClaimNames.Sub)
                  ?? User.FindFirstValue(ClaimTypes.NameIdentifier)
                  ?? User.FindFirstValue("sub");

        return int.TryParse(sub, out var buyerId) ? buyerId : null;
    }

    private async Task<bool> CanAccessConversationAsync(int conversationId, CancellationToken cancellationToken)
    {
        var buyerId = TryGetBuyerIdFromToken();
        if (!buyerId.HasValue)
        {
            return false;
        }

        return await _db.SupportConversations
            .AsNoTracking()
            .AnyAsync(
                x => x.ConversationId == conversationId &&
                     x.UserId == buyerId.Value,
                cancellationToken);
    }

    public sealed class SendBuyerSupportMessageRequest
    {
        [JsonPropertyName("content")]
        public string? Content { get; set; }

        [JsonPropertyName("replyToMessageId")]
        public int? ReplyToMessageId { get; set; }
    }
}
