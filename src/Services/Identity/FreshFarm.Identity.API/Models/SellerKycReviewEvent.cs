namespace FreshFarm.Identity.Api.Models;

public sealed class SellerKycReviewEvent
{
    public int SellerKycReviewEventId { get; set; }

    public int UserId { get; set; }

    public string Action { get; set; } = string.Empty;

    public string ReviewStatus { get; set; } = string.Empty;

    public string? Note { get; set; }

    public DateTime ReviewedAt { get; set; }

    public int? ReviewedByUserId { get; set; }

    public string? ReviewerUserName { get; set; }

    public string? ReviewerFullName { get; set; }
}
