using Microsoft.EntityFrameworkCore;

namespace FreshFarm.Identity.Api.Models;

public partial class FreshFarmIdentityDBContext
{
    internal static void ConfigureSellerKycReviewEvents(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<SellerKycReviewEvent>(entity =>
        {
            entity.ToTable("SellerKycReviewEvents");

            entity.HasKey(e => e.SellerKycReviewEventId);

            entity.HasIndex(e => new { e.UserId, e.ReviewedAt }, "IX_SellerKycReviewEvents_UserId_ReviewedAt")
                .IsDescending(false, true);

            entity.Property(e => e.Action)
                .IsRequired()
                .HasMaxLength(30);
            entity.Property(e => e.ReviewStatus)
                .IsRequired()
                .HasMaxLength(30);
            entity.Property(e => e.Note)
                .HasMaxLength(1000);
            entity.Property(e => e.ReviewedAt)
                .HasPrecision(0)
                .HasDefaultValueSql("(sysutcdatetime())");
            entity.Property(e => e.ReviewerUserName)
                .HasMaxLength(100);
            entity.Property(e => e.ReviewerFullName)
                .HasMaxLength(150);
        });
    }
}
