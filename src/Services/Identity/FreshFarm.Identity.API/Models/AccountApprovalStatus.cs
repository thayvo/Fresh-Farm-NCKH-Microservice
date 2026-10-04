namespace FreshFarm.Identity.Api.Models;

public static class AccountApprovalStatus
{
    public const string Pending = "Pending";
    public const string Approved = "Approved";
    public const string Rejected = "Rejected";
    public const string Suspended = "Suspended";

    public static bool IsValid(string? value)
        => string.Equals(value, Pending, StringComparison.OrdinalIgnoreCase)
           || string.Equals(value, Approved, StringComparison.OrdinalIgnoreCase)
           || string.Equals(value, Rejected, StringComparison.OrdinalIgnoreCase)
           || string.Equals(value, Suspended, StringComparison.OrdinalIgnoreCase);
}
