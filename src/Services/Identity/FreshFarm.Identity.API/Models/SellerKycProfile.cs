namespace FreshFarm.Identity.Api.Models;

public sealed class SellerKycProfile
{
    public int SellerKycProfileId { get; set; }

    public int UserId { get; set; }

    public string LegalFullName { get; set; } = string.Empty;

    public string IdentityNumber { get; set; } = string.Empty;

    public DateTime IdentityIssuedDate { get; set; }

    public string IdentityIssuedPlace { get; set; } = string.Empty;

    public string? TaxCode { get; set; }

    public string? BusinessLicenseNumber { get; set; }

    public string? CitizenIdFrontUrl { get; set; }

    public string? CitizenIdBackUrl { get; set; }

    public string? BusinessLicenseUrl { get; set; }

    public string? AdditionalDocumentUrl { get; set; }

    public string? Notes { get; set; }

    public string ReviewStatus { get; set; } = "pending";

    public string? ReviewNote { get; set; }

    public DateTime? ReviewedAt { get; set; }

    public int? ReviewedByUserId { get; set; }

    public DateTime CreatedAt { get; set; }

    public DateTime UpdatedAt { get; set; }

    public User User { get; set; } = null!;
}
