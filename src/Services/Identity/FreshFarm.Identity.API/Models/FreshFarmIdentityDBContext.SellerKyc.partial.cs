using Microsoft.EntityFrameworkCore;

namespace FreshFarm.Identity.Api.Models;

public partial class FreshFarmIdentityDBContext
{
    public virtual DbSet<SellerKycProfile> SellerKycProfiles { get; set; } = null!;

    internal static void ConfigureSellerKyc(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<SellerKycProfile>(entity =>
        {
            entity.ToTable("SellerKycProfiles");

            entity.HasKey(e => e.SellerKycProfileId);

            entity.HasIndex(e => e.UserId, "UX_SellerKycProfiles_UserId").IsUnique();

            entity.Property(e => e.LegalFullName)
                .IsRequired()
                .HasMaxLength(150);
            entity.Property(e => e.IdentityNumber)
                .IsRequired()
                .HasMaxLength(50);
            entity.Property(e => e.IdentityIssuedDate)
                .HasPrecision(0);
            entity.Property(e => e.IdentityIssuedPlace)
                .IsRequired()
                .HasMaxLength(255);
            entity.Property(e => e.TaxCode)
                .HasMaxLength(50);
            entity.Property(e => e.BusinessLicenseNumber)
                .HasMaxLength(100);
            entity.Property(e => e.CitizenIdFrontUrl)
                .HasMaxLength(500);
            entity.Property(e => e.CitizenIdBackUrl)
                .HasMaxLength(500);
            entity.Property(e => e.BusinessLicenseUrl)
                .HasMaxLength(500);
            entity.Property(e => e.AdditionalDocumentUrl)
                .HasMaxLength(500);
            entity.Property(e => e.Notes)
                .HasMaxLength(1000);
            entity.Property(e => e.CreatedAt)
                .HasPrecision(0)
                .HasDefaultValueSql("(sysutcdatetime())");
            entity.Property(e => e.UpdatedAt)
                .HasPrecision(0)
                .HasDefaultValueSql("(sysutcdatetime())");

            entity.HasOne(d => d.User)
                .WithOne(p => p.SellerKycProfile)
                .HasForeignKey<SellerKycProfile>(d => d.UserId)
                .OnDelete(DeleteBehavior.Cascade)
                .HasConstraintName("FK_SellerKycProfiles_Users");
        });
    }
}
