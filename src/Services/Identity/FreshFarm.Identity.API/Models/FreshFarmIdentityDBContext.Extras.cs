using Microsoft.EntityFrameworkCore;

namespace FreshFarm.Identity.Api.Models;

public partial class FreshFarmIdentityDBContext
{
    public virtual DbSet<AuthAuditLog> AuthAuditLogs => Set<AuthAuditLog>();
    public virtual DbSet<AccountApprovalEvent> AccountApprovalEvents => Set<AccountApprovalEvent>();
    public virtual DbSet<LoginDeviceSecurityState> LoginDeviceSecurityStates => Set<LoginDeviceSecurityState>();
    public virtual DbSet<SellerKycReviewEvent> SellerKycReviewEvents => Set<SellerKycReviewEvent>();

    partial void OnModelCreatingPartial(ModelBuilder modelBuilder)
    {
        ConfigureSellerKyc(modelBuilder);
        ConfigureSellerKycReviewEvents(modelBuilder);

        modelBuilder.Entity<User>(entity =>
        {
            entity.Property(e => e.ApprovalStatus)
                .IsRequired()
                .HasMaxLength(20)
                .HasDefaultValue(AccountApprovalStatus.Pending)
                .HasAnnotation("Relational:DefaultConstraintName", "DF_Users_ApprovalStatus");
            entity.Property(e => e.ApprovalVersion)
                .IsConcurrencyToken()
                .HasDefaultValue(0L)
                .HasAnnotation("Relational:DefaultConstraintName", "DF_Users_ApprovalVersion");
            entity.Property(e => e.ApprovedAt).HasPrecision(0);
            entity.Property(e => e.ApprovalNote).HasMaxLength(500);
            entity.Property(e => e.ApprovalStatusChangedAt).HasPrecision(0);

            entity.HasIndex(e => new { e.ApprovalStatus, e.CreatedAt }, "IX_Users_ApprovalStatus_CreatedAt");
        });

        modelBuilder.Entity<AccountApprovalEvent>(entity =>
        {
            entity.HasKey(e => e.AccountApprovalEventId);
            entity.ToTable("AccountApprovalEvent");
            entity.HasIndex(e => new { e.UserId, e.OccurredAt }, "IX_AccountApprovalEvent_UserId_OccurredAt")
                .IsDescending(false, true);
            entity.HasIndex(e => new { e.ActorUserId, e.OccurredAt }, "IX_AccountApprovalEvent_ActorUserId_OccurredAt")
                .IsDescending(false, true);
            entity.Property(e => e.FromStatus).IsRequired().HasMaxLength(20);
            entity.Property(e => e.ToStatus).IsRequired().HasMaxLength(20);
            entity.Property(e => e.Note).HasMaxLength(500);
            entity.Property(e => e.OccurredAt)
                .HasPrecision(0)
                .HasDefaultValueSql("(sysutcdatetime())")
                .HasAnnotation("Relational:DefaultConstraintName", "DF_AccountApprovalEvent_OccurredAt");

            entity.HasOne<User>()
                .WithMany()
                .HasForeignKey(e => e.UserId)
                .OnDelete(DeleteBehavior.Cascade)
                .HasConstraintName("FK_AccountApprovalEvent_Users_UserId");

            entity.HasOne<User>()
                .WithMany()
                .HasForeignKey(e => e.ActorUserId)
                .OnDelete(DeleteBehavior.NoAction)
                .HasConstraintName("FK_AccountApprovalEvent_Users_ActorUserId");
        });

        modelBuilder.Entity<LoginDeviceSecurityState>(entity =>
        {
            entity.HasKey(e => e.LoginDeviceSecurityStateId);
            entity.ToTable("LoginDeviceSecurityState");
            entity.HasIndex(e => e.DeviceKeyHash, "UX_LoginDeviceSecurityState_DeviceKeyHash").IsUnique();
            entity.HasIndex(e => new { e.LastUserId, e.LockedUntil }, "IX_LoginDeviceSecurityState_LastUserId_LockedUntil");
            entity.Property(e => e.DeviceKeyHash).IsRequired().HasMaxLength(64).IsUnicode(false);
            entity.Property(e => e.FailedCount).HasDefaultValue(0);
            entity.Property(e => e.LockoutLevel).HasDefaultValue(0);
            entity.Property(e => e.LockedUntil).HasPrecision(0);
            entity.Property(e => e.LastFailedAt).HasPrecision(0);
            entity.Property(e => e.CreatedAt)
                .HasPrecision(0)
                .HasDefaultValueSql("(sysutcdatetime())");
            entity.Property(e => e.UpdatedAt)
                .HasPrecision(0)
                .HasDefaultValueSql("(sysutcdatetime())");

            entity.HasOne<User>()
                .WithMany()
                .HasForeignKey(e => e.LastUserId)
                .OnDelete(DeleteBehavior.SetNull)
                .HasConstraintName("FK_LoginDeviceSecurityState_Users_LastUserId");
        });

        modelBuilder.Entity<UserAuth>(entity =>
        {
            entity.Property(e => e.TokenVersion)
                .HasDefaultValue(0)
                .HasAnnotation("Relational:DefaultConstraintName", "DF_UserAuth_TokenVersion");
            entity.Property(e => e.PasswordResetNonce).HasMaxLength(64).IsUnicode(false);
            entity.Property(e => e.ExternalLoginProvider).HasMaxLength(50);
            entity.Property(e => e.ExternalLoginSubject).HasMaxLength(200);
            entity.HasIndex(e => new { e.ExternalLoginProvider, e.ExternalLoginSubject }, "UX_UserAuth_ExternalLogin")
                .IsUnique()
                .HasFilter("[ExternalLoginProvider] IS NOT NULL AND [ExternalLoginSubject] IS NOT NULL");
        });

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
