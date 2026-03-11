using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.SignalR;

namespace FreshFarm.Web.Bff.Areas.Seller.Hubs;

[Authorize(Roles = "Seller")]
public sealed class SupportChatHub : Hub
{
    public override async Task OnConnectedAsync()
    {
        await JoinSellerInternalAsync();
        await base.OnConnectedAsync();
    }

    public Task JoinSeller()
    {
        return JoinSellerInternalAsync();
    }

    public async Task JoinConversation(int conversationId)
    {
        if (conversationId <= 0)
        {
            return;
        }

        await Groups.AddToGroupAsync(Context.ConnectionId, SupportChatHubGroups.Conversation(conversationId));
    }

    public async Task LeaveConversation(int conversationId)
    {
        if (conversationId <= 0)
        {
            return;
        }

        await Groups.RemoveFromGroupAsync(Context.ConnectionId, SupportChatHubGroups.Conversation(conversationId));
    }

    public async Task NotifyTyping(int conversationId)
    {
        if (conversationId <= 0)
        {
            return;
        }

        await Clients.OthersInGroup(SupportChatHubGroups.Conversation(conversationId))
            .SendAsync("userTyping", new { conversationId });
    }

    private async Task JoinSellerInternalAsync()
    {
        var sellerId = TryGetSellerId();
        if (!sellerId.HasValue)
        {
            return;
        }

        await Groups.AddToGroupAsync(Context.ConnectionId, SupportChatHubGroups.Seller(sellerId.Value));
    }

    private int? TryGetSellerId()
    {
        var claim =
            Context.User?.FindFirst("sub")?.Value ??
            Context.User?.FindFirst(ClaimTypes.NameIdentifier)?.Value;

        return int.TryParse(claim, out var sellerId) ? sellerId : null;
    }
}
