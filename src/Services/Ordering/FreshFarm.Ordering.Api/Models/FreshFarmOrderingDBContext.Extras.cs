using Microsoft.EntityFrameworkCore;

namespace FreshFarm.Ordering.Api.Models;

public partial class FreshFarmOrderingDBContext
{
    partial void OnModelCreatingPartial(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<ContactMessage>(entity =>
        {
            entity.ToTable("ContactMessages");

            entity.HasKey(e => e.Id).HasName("PK_ContactMessages");

            entity.HasIndex(e => new { e.IsDeleted, e.CreatedAt }, "IX_ContactMessages_IsDeleted_CreatedAt")
                .IsDescending(false, true);

            entity.HasIndex(e => e.Status, "IX_ContactMessages_Status");

            entity.Property(e => e.SenderName)
                .IsRequired()
                .HasMaxLength(150);

            entity.Property(e => e.SenderEmail)
                .IsRequired()
                .HasMaxLength(200);

            entity.Property(e => e.SenderPhone)
                .HasMaxLength(30);

            entity.Property(e => e.Subject)
                .IsRequired()
                .HasMaxLength(300);

            entity.Property(e => e.Message).IsRequired();

            entity.Property(e => e.CreatedAt)
                .HasDefaultValueSql("(getutcdate())")
                .HasColumnType("datetime");

            entity.Property(e => e.Status)
                .IsRequired()
                .HasMaxLength(30)
                .HasDefaultValue("new");

            entity.Property(e => e.AdminNote)
                .HasMaxLength(1000);

            entity.Property(e => e.ProcessedAt)
                .HasColumnType("datetime");

            entity.Property(e => e.IsDeleted)
                .HasDefaultValue(false);

            entity.Property(e => e.DeletedAt)
                .HasColumnType("datetime");
        });
    }
}
