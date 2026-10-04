namespace FreshFarm.Identity.Api.Models;

public partial class User
{
    public string ApprovalStatus { get; set; } = AccountApprovalStatus.Pending;

    public long ApprovalVersion { get; set; }

    public DateTime? ApprovedAt { get; set; }

    public int? ApprovedByUserId { get; set; }

    public string? ApprovalNote { get; set; }

    public DateTime? ApprovalStatusChangedAt { get; set; }
}
