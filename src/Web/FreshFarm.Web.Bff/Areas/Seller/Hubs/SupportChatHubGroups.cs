namespace FreshFarm.Web.Bff.Areas.Seller.Hubs;

public static class SupportChatHubGroups
{
    public static string Seller(int sellerId) => $"seller:{sellerId}";

    public static string Conversation(int conversationId) => $"support:conversation:{conversationId}";
}
