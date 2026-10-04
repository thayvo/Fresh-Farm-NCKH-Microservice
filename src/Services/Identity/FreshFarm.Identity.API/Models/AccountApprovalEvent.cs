namespace FreshFarm.Identity.Api.Models;

public sealed class AccountApprovalEvent
{
    public long AccountApprovalEventId { get; set; }

    public int UserId { get; set; }

    public int ActorUserId { get; set; }

    public string FromStatus { get; set; } = string.Empty;

    public string ToStatus { get; set; } = string.Empty;

    public string? Note { get; set; }

    public DateTime OccurredAt { get; set; }
}
