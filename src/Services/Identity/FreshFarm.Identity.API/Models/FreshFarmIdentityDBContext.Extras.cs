using Microsoft.EntityFrameworkCore;

namespace FreshFarm.Identity.Api.Models;

public partial class FreshFarmIdentityDBContext
{
    public virtual DbSet<AuthAuditLog> AuthAuditLogs => Set<AuthAuditLog>();

    partial void OnModelCreatingPartial(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<AuthAuditLog>(entity =>
        {
            entity.HasKey(e => e.AuthAuditLogId);

            entity.ToTable("AuthAuditLog");

            entity.HasIndex(e => e.OccurredAt, "IX_AuthAuditLog_OccurredAt").IsDescending(true);
            entity.HasIndex(e => new { e.RoleName, e.OccurredAt }, "IX_AuthAuditLog_RoleName_OccurredAt").IsDescending(false, true);
            entity.HasIndex(e => new { e.ClientLane, e.OccurredAt }, "IX_AuthAuditLog_ClientLane_OccurredAt").IsDescending(false, true);
            entity.HasIndex(e => new { e.Success, e.OccurredAt }, "IX_AuthAuditLog_Success_OccurredAt").IsDescending(false, true);
            entity.HasIndex(e => new { e.IsSuspicious, e.OccurredAt }, "IX_AuthAuditLog_IsSuspicious_OccurredAt").IsDescending(false, true);
            entity.HasIndex(e => new { e.UserId, e.OccurredAt }, "IX_AuthAuditLog_UserId_OccurredAt").IsDescending(false, true);

            entity.Property(e => e.BrowserFamily).HasMaxLength(50);
            entity.Property(e => e.ClientLane)
                .IsRequired()
                .HasMaxLength(20);
            entity.Property(e => e.CityName).HasMaxLength(120);
            entity.Property(e => e.DeviceType).HasMaxLength(30);
            entity.Property(e => e.CountryCode).HasMaxLength(8);
            entity.Property(e => e.CountryName).HasMaxLength(120);
            entity.Property(e => e.Email).HasMaxLength(100);
            entity.Property(e => e.EventType)
                .IsRequired()
                .HasMaxLength(50);
            entity.Property(e => e.FailureReason).HasMaxLength(100);
            entity.Property(e => e.ForwardedFor).HasMaxLength(200);
            entity.Property(e => e.Identifier)
                .IsRequired()
                .HasMaxLength(100);
            entity.Property(e => e.IpAddress).HasMaxLength(64);
            entity.Property(e => e.IsSuspicious)
                .HasDefaultValue(false)
                .HasAnnotation("Relational:DefaultConstraintName", "DF_AuthAuditLog_IsSuspicious");
            entity.Property(e => e.OccurredAt)
                .HasPrecision(0)
                .HasDefaultValueSql("(sysutcdatetime())")
                .HasAnnotation("Relational:DefaultConstraintName", "DF_AuthAuditLog_OccurredAt");
            entity.Property(e => e.OperatingSystem).HasMaxLength(50);
            entity.Property(e => e.RegionName).HasMaxLength(120);
            entity.Property(e => e.RoleName)
                .IsRequired()
                .HasMaxLength(50);
            entity.Property(e => e.SuspicionReasons).HasMaxLength(500);
            entity.Property(e => e.UserAgent).HasMaxLength(500);
            entity.Property(e => e.UserName).HasMaxLength(100);

            entity.HasOne<User>()
                .WithMany()
                .HasForeignKey(e => e.UserId)
                .OnDelete(DeleteBehavior.SetNull)
                .HasConstraintName("FK_AuthAuditLog_Users");
        });
    }
}
