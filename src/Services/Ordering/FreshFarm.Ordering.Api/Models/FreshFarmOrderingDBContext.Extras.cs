using Microsoft.EntityFrameworkCore;

namespace FreshFarm.Ordering.Api.Models;

public partial class FreshFarmOrderingDBContext
{
    public virtual Microsoft.EntityFrameworkCore.DbSet<SellerWithdrawal> SellerWithdrawals { get; set; }

    private static void ConfigureSellerWithdrawals(Microsoft.EntityFrameworkCore.ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<SellerWithdrawal>(entity =>
        {
            entity.ToTable("SellerWithdrawals");
            entity.HasKey(e => e.SellerWithdrawalId);
            entity.Property(e => e.SellerWithdrawalId).HasColumnName("SellerWithdrawalID");
            entity.Property(e => e.SellerId).HasColumnName("SellerID");
            entity.Property(e => e.Amount).HasColumnType("decimal(18, 2)");
            entity.Property(e => e.BankName).IsRequired().HasMaxLength(160);
            entity.Property(e => e.BankAccountName).IsRequired().HasMaxLength(160);
            entity.Property(e => e.BankAccountNumber).IsRequired().HasMaxLength(80);
            entity.Property(e => e.Status).IsRequired().HasMaxLength(30).HasDefaultValue("completed");
            entity.Property(e => e.Note).HasMaxLength(500);
            entity.Property(e => e.CreatedAt).HasColumnType("datetime").HasDefaultValueSql("(getutcdate())");
            entity.Property(e => e.CompletedAt).HasColumnType("datetime");
            entity.HasIndex(e => new { e.SellerId, e.CreatedAt }, "IX_SellerWithdrawals_Seller_CreatedAt").IsDescending(false, true);
        });
    }
}
