namespace FreshFarm.Ordering.Api.Models;

public partial class ProductViewEvent
{
    public int ProductViewEventId { get; set; }

    public int? UserId { get; set; }

    public string SessionId { get; set; } = string.Empty;

    public int ProductId { get; set; }

    public int? SellerId { get; set; }

    public string SourcePage { get; set; } = string.Empty;

    public string SourceModule { get; set; } = string.Empty;

    public DateTime CreatedAt { get; set; }
}
