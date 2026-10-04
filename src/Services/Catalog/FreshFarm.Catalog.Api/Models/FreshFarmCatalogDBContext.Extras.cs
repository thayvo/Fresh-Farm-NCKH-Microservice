using Microsoft.EntityFrameworkCore;

namespace FreshFarm.Catalog.Api.Models;

public partial class FreshFarmCatalogDBContext
{
    public virtual DbSet<ProductAttributeValue> ProductAttributeValues { get; set; }

    public virtual DbSet<FreshInventoryLot> FreshInventoryLots { get; set; }

    public virtual DbSet<FreshQualityRecall> FreshQualityRecalls { get; set; }

    public virtual DbSet<ProductSeasonality> ProductSeasonalities { get; set; }

    partial void OnModelCreatingPartial(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<CategoryAttribute>(entity =>
        {
            entity.HasIndex(e => new { e.CategoryId, e.AttributeKey }, "UQ_CategoryAttribute_Category_Key").IsUnique();
            entity.HasIndex(e => new { e.CategoryId, e.IsRequired, e.IsActive }, "IX_CategoryAttribute_Category_Required_Active");

            entity.HasOne(d => d.Category).WithMany(p => p.CategoryAttributes)
                .HasForeignKey(d => d.CategoryId)
                .OnDelete(DeleteBehavior.Cascade)
                .HasConstraintName("FK_CategoryAttribute_Category");
        });

        modelBuilder.Entity<ProductAttributeValue>(entity =>
        {
            entity.HasKey(e => e.ProductAttributeValueId);

            entity.ToTable("ProductAttributeValue");

            entity.HasIndex(e => new { e.ProductId, e.CategoryAttributeId }, "UQ_ProductAttributeValue_Product_Attribute").IsUnique();
            entity.HasIndex(e => new { e.CategoryAttributeId, e.NormalizedValue }, "IX_ProductAttributeValue_Attribute_NormalizedValue");

            entity.Property(e => e.ValueText)
                .IsRequired()
                .HasMaxLength(1000);
            entity.Property(e => e.NormalizedValue).HasMaxLength(1000);
            entity.Property(e => e.Source)
                .HasMaxLength(50)
                .HasDefaultValue("manual");
            entity.Property(e => e.CreatedAt)
                .HasDefaultValueSql("(getutcdate())")
                .HasColumnType("datetime");
            entity.Property(e => e.UpdatedAt).HasColumnType("datetime");

            entity.HasOne(d => d.CategoryAttribute).WithMany(p => p.ProductAttributeValues)
                .HasForeignKey(d => d.CategoryAttributeId)
                .OnDelete(DeleteBehavior.Cascade)
                .HasConstraintName("FK_ProductAttributeValue_CategoryAttribute");

            entity.HasOne(d => d.Product).WithMany(p => p.ProductAttributeValues)
                .HasForeignKey(d => d.ProductId)
                .OnDelete(DeleteBehavior.Cascade)
                .HasConstraintName("FK_ProductAttributeValue_Product");
        });

        modelBuilder.Entity<FreshInventoryLot>(entity =>
        {
            entity.HasKey(e => e.FreshInventoryLotId);

            entity.ToTable("FreshInventoryLot");

            entity.HasIndex(e => new { e.SellerId, e.ExpiresAt }, "IX_FreshInventoryLot_Seller_ExpiresAt");
            entity.HasIndex(e => new { e.ProductId, e.Status }, "IX_FreshInventoryLot_Product_Status");
            entity.HasIndex(e => new { e.SellerId, e.ProductId, e.LotCode }, "UQ_FreshInventoryLot_Seller_Product_Lot").IsUnique();

            entity.Property(e => e.LotCode)
                .IsRequired()
                .HasMaxLength(80);
            entity.Property(e => e.TraceCode).HasMaxLength(120);
            entity.Property(e => e.FarmName).HasMaxLength(150);
            entity.Property(e => e.OriginRegion).HasMaxLength(150);
            entity.Property(e => e.HarvestedAt).HasColumnType("datetime");
            entity.Property(e => e.PackedAt).HasColumnType("datetime");
            entity.Property(e => e.ReceivedAt).HasColumnType("datetime");
            entity.Property(e => e.ExpiresAt).HasColumnType("datetime");
            entity.Property(e => e.UnitCost).HasColumnType("decimal(18, 2)");
            entity.Property(e => e.Status)
                .IsRequired()
                .HasMaxLength(40)
                .HasDefaultValue("active");
            entity.Property(e => e.QualityStatus)
                .IsRequired()
                .HasMaxLength(40)
                .HasDefaultValue("ok");
            entity.Property(e => e.Notes).HasMaxLength(2000);
            entity.Property(e => e.CreatedAt)
                .HasDefaultValueSql("(getutcdate())")
                .HasColumnType("datetime");
            entity.Property(e => e.UpdatedAt).HasColumnType("datetime");

            entity.HasOne(d => d.Product).WithMany(p => p.FreshInventoryLots)
                .HasForeignKey(d => d.ProductId)
                .OnDelete(DeleteBehavior.Cascade)
                .HasConstraintName("FK_FreshInventoryLot_Product");
        });

        modelBuilder.Entity<FreshQualityRecall>(entity =>
        {
            entity.HasKey(e => e.FreshQualityRecallId);

            entity.ToTable("FreshQualityRecall");

            entity.HasIndex(e => e.RecallCode, "UQ_FreshQualityRecall_Code").IsUnique();
            entity.HasIndex(e => new { e.Status, e.StartedAt }, "IX_FreshQualityRecall_Status_StartedAt");
            entity.HasIndex(e => new { e.SellerId, e.Severity }, "IX_FreshQualityRecall_Seller_Severity");

            entity.Property(e => e.RecallCode)
                .IsRequired()
                .HasMaxLength(50);
            entity.Property(e => e.RecallType)
                .IsRequired()
                .HasMaxLength(50);
            entity.Property(e => e.Severity)
                .IsRequired()
                .HasMaxLength(20)
                .HasDefaultValue("medium");
            entity.Property(e => e.Status)
                .IsRequired()
                .HasMaxLength(40)
                .HasDefaultValue("open");
            entity.Property(e => e.Title)
                .IsRequired()
                .HasMaxLength(200);
            entity.Property(e => e.Reason).HasMaxLength(2000);
            entity.Property(e => e.ActionRequired).HasMaxLength(2000);
            entity.Property(e => e.StartedAt).HasColumnType("datetime");
            entity.Property(e => e.ResolvedAt).HasColumnType("datetime");
            entity.Property(e => e.CreatedAt)
                .HasDefaultValueSql("(getutcdate())")
                .HasColumnType("datetime");
            entity.Property(e => e.UpdatedAt).HasColumnType("datetime");

            entity.HasOne(d => d.Product).WithMany(p => p.FreshQualityRecalls)
                .HasForeignKey(d => d.ProductId)
                .HasConstraintName("FK_FreshQualityRecall_Product");

            entity.HasOne(d => d.FreshInventoryLot).WithMany()
                .HasForeignKey(d => d.FreshInventoryLotId)
                .HasConstraintName("FK_FreshQualityRecall_FreshInventoryLot");
        });

        modelBuilder.Entity<ProductSeasonality>(entity =>
        {
            entity.HasKey(e => e.ProductSeasonalityId);

            entity.ToTable("ProductSeasonality");

            entity.HasIndex(e => new { e.ProductId, e.SeasonLabel, e.StartMonth, e.EndMonth }, "UX_ProductSeasonality_Product_Season")
                .IsUnique();
            entity.HasIndex(e => e.ProductId, "IX_ProductSeasonality_ProductID");
            entity.HasIndex(e => new { e.StartMonth, e.EndMonth, e.IsYearRound, e.HasPeakSeason }, "IX_ProductSeasonality_MonthWindow");

            entity.Property(e => e.ProductSeasonalityId).HasColumnName("ProductSeasonalityID");
            entity.Property(e => e.ProductId).HasColumnName("ProductID");
            entity.Property(e => e.Country).HasMaxLength(80);
            entity.Property(e => e.ProvinceRegion).HasMaxLength(200);
            entity.Property(e => e.AreaDetail).HasMaxLength(200);
            entity.Property(e => e.SeasonType)
                .IsRequired()
                .HasMaxLength(60);
            entity.Property(e => e.SeasonLabel)
                .IsRequired()
                .HasMaxLength(180);
            entity.Property(e => e.PeakMonths).HasMaxLength(80);
            entity.Property(e => e.IsYearRound)
                .HasDefaultValue(false)
                .HasAnnotation("Relational:DefaultConstraintName", "DF_ProductSeasonality_IsYearRound");
            entity.Property(e => e.HasPeakSeason)
                .HasDefaultValue(false)
                .HasAnnotation("Relational:DefaultConstraintName", "DF_ProductSeasonality_HasPeakSeason");
            entity.Property(e => e.IsControlledCultivation)
                .HasDefaultValue(false)
                .HasAnnotation("Relational:DefaultConstraintName", "DF_ProductSeasonality_IsControlledCultivation");
            entity.Property(e => e.IsImportedSeason)
                .HasDefaultValue(false)
                .HasAnnotation("Relational:DefaultConstraintName", "DF_ProductSeasonality_IsImportedSeason");
            entity.Property(e => e.IsOffSeason)
                .HasDefaultValue(false)
                .HasAnnotation("Relational:DefaultConstraintName", "DF_ProductSeasonality_IsOffSeason");
            entity.Property(e => e.IsPostHarvestAvailability)
                .HasDefaultValue(false)
                .HasAnnotation("Relational:DefaultConstraintName", "DF_ProductSeasonality_IsPostHarvestAvailability");
            entity.Property(e => e.SeasonScoreWeight)
                .HasDefaultValue(50)
                .HasAnnotation("Relational:DefaultConstraintName", "DF_ProductSeasonality_SeasonScoreWeight");
            entity.Property(e => e.ConfidenceLevel).HasMaxLength(40);
            entity.Property(e => e.SourceNote).HasMaxLength(600);
            entity.Property(e => e.CreatedAt)
                .HasDefaultValueSql("(sysutcdatetime())")
                .HasColumnType("datetime2(0)");
            entity.Property(e => e.UpdatedAt)
                .HasDefaultValueSql("(sysutcdatetime())")
                .HasColumnType("datetime2(0)");

            entity.HasOne(d => d.Product).WithMany(p => p.ProductSeasonalities)
                .HasForeignKey(d => d.ProductId)
                .OnDelete(DeleteBehavior.ClientSetNull)
                .HasConstraintName("FK_ProductSeasonality_Products_ProductID");
        });
    }
}
