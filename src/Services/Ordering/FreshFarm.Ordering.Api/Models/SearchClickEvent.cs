namespace FreshFarm.Ordering.Api.Models;

public partial class SearchClickEvent
{
    public int SearchClickEventId { get; set; }

    public int? SearchEventId { get; set; }

    public int? UserId { get; set; }

    public string SessionId { get; set; } = string.Empty;

    public int ProductId { get; set; }

    public int? SellerId { get; set; }

    public int Rank { get; set; }

    public DateTime CreatedAt { get; set; }
}
