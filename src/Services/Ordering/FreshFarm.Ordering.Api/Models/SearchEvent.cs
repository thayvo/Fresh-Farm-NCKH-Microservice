namespace FreshFarm.Ordering.Api.Models;

public partial class SearchEvent
{
    public int SearchEventId { get; set; }

    public int? UserId { get; set; }

    public string SessionId { get; set; } = string.Empty;

    public string Keyword { get; set; } = string.Empty;

    public string? FiltersJson { get; set; }

    public int ResultCount { get; set; }

    public DateTime CreatedAt { get; set; }
}
