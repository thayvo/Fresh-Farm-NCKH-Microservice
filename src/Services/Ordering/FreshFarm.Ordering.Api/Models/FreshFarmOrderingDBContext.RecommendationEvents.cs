using Microsoft.EntityFrameworkCore;

namespace FreshFarm.Ordering.Api.Models;

public partial class FreshFarmOrderingDBContext
{
    public virtual DbSet<ProductViewEvent> ProductViewEvents { get; set; } = null!;

    public virtual DbSet<SearchEvent> SearchEvents { get; set; } = null!;

    public virtual DbSet<SearchClickEvent> SearchClickEvents { get; set; } = null!;

    public virtual DbSet<RecommendationImpressionEvent> RecommendationImpressionEvents { get; set; } = null!;

    public virtual DbSet<RecommendationClickEvent> RecommendationClickEvents { get; set; } = null!;

    public virtual DbSet<RecommendationProductAffinity> RecommendationProductAffinities { get; set; } = null!;

    public virtual DbSet<RecommendationSearchKeywordAffinity> RecommendationSearchKeywordAffinities { get; set; } = null!;

    public virtual DbSet<RecommendationHomePreferenceSeed> RecommendationHomePreferenceSeeds { get; set; } = null!;

    public virtual DbSet<RecommendationHomeCollaborativeCandidate> RecommendationHomeCollaborativeCandidates { get; set; } = null!;

    partial void OnModelCreatingPartial(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<ProductViewEvent>(entity =>
        {
            entity.HasKey(e => e.ProductViewEventId);

            entity.ToTable("ProductViewEvent");

            entity.HasIndex(e => new { e.ProductId, e.CreatedAt }, "IX_ProductViewEvent_Product_CreatedAt").IsDescending(false, true);
            entity.HasIndex(e => new { e.SessionId, e.CreatedAt }, "IX_ProductViewEvent_Session_CreatedAt").IsDescending(false, true);
            entity.HasIndex(e => new { e.UserId, e.CreatedAt }, "IX_ProductViewEvent_User_CreatedAt").IsDescending(false, true);

            entity.Property(e => e.CreatedAt)
                .HasDefaultValueSql("(getutcdate())")
                .HasAnnotation("Relational:DefaultConstraintName", "DF_ProductViewEvent_CreatedAt")
                .HasColumnType("datetime");
            entity.Property(e => e.SessionId)
                .IsRequired()
                .HasMaxLength(120);
            entity.Property(e => e.SourceModule)
                .IsRequired()
                .HasMaxLength(80);
            entity.Property(e => e.SourcePage)
                .IsRequired()
                .HasMaxLength(50);
        });

        modelBuilder.Entity<SearchEvent>(entity =>
        {
            entity.HasKey(e => e.SearchEventId);

            entity.ToTable("SearchEvent");

            entity.HasIndex(e => new { e.CreatedAt, e.Keyword }, "IX_SearchEvent_CreatedAt_Keyword").IsDescending(true, false);
            entity.HasIndex(e => new { e.SessionId, e.CreatedAt }, "IX_SearchEvent_Session_CreatedAt").IsDescending(false, true);
            entity.HasIndex(e => new { e.UserId, e.CreatedAt }, "IX_SearchEvent_User_CreatedAt").IsDescending(false, true);

            entity.Property(e => e.CreatedAt)
                .HasDefaultValueSql("(getutcdate())")
                .HasAnnotation("Relational:DefaultConstraintName", "DF_SearchEvent_CreatedAt")
                .HasColumnType("datetime");
            entity.Property(e => e.FiltersJson).HasMaxLength(4000);
            entity.Property(e => e.Keyword)
                .IsRequired()
                .HasMaxLength(200);
            entity.Property(e => e.SessionId)
                .IsRequired()
                .HasMaxLength(120);
        });

        modelBuilder.Entity<SearchClickEvent>(entity =>
        {
            entity.HasKey(e => e.SearchClickEventId);

            entity.ToTable("SearchClickEvent");

            entity.HasIndex(e => new { e.ProductId, e.CreatedAt }, "IX_SearchClickEvent_Product_CreatedAt").IsDescending(false, true);
            entity.HasIndex(e => new { e.SearchEventId, e.CreatedAt }, "IX_SearchClickEvent_SearchEvent_CreatedAt").IsDescending(false, true);
            entity.HasIndex(e => new { e.UserId, e.CreatedAt }, "IX_SearchClickEvent_User_CreatedAt").IsDescending(false, true);

            entity.Property(e => e.CreatedAt)
                .HasDefaultValueSql("(getutcdate())")
                .HasAnnotation("Relational:DefaultConstraintName", "DF_SearchClickEvent_CreatedAt")
                .HasColumnType("datetime");
            entity.Property(e => e.SessionId)
                .IsRequired()
                .HasMaxLength(120);

            entity.HasOne<SearchEvent>()
                .WithMany()
                .HasForeignKey(e => e.SearchEventId)
                .HasConstraintName("FK_SearchClickEvent_SearchEvent");
        });

        modelBuilder.Entity<RecommendationImpressionEvent>(entity =>
        {
            entity.HasKey(e => e.RecommendationImpressionEventId);

            entity.ToTable("RecommendationImpressionEvent");

            entity.HasIndex(e => new { e.Placement, e.CreatedAt }, "IX_RecommendationImpressionEvent_Placement_CreatedAt").IsDescending(false, true);
            entity.HasIndex(e => new { e.SessionId, e.CreatedAt }, "IX_RecommendationImpressionEvent_Session_CreatedAt").IsDescending(false, true);
            entity.HasIndex(e => new { e.UserId, e.CreatedAt }, "IX_RecommendationImpressionEvent_User_CreatedAt").IsDescending(false, true);

            entity.Property(e => e.Algorithm)
                .IsRequired()
                .HasMaxLength(30);
            entity.Property(e => e.CreatedAt)
                .HasDefaultValueSql("(getutcdate())")
                .HasAnnotation("Relational:DefaultConstraintName", "DF_RecommendationImpressionEvent_CreatedAt")
                .HasColumnType("datetime");
            entity.Property(e => e.Placement)
                .IsRequired()
                .HasMaxLength(60);
            entity.Property(e => e.RecommendationRunId).HasMaxLength(100);
            entity.Property(e => e.SessionId)
                .IsRequired()
                .HasMaxLength(120);
        });

        modelBuilder.Entity<RecommendationClickEvent>(entity =>
        {
            entity.HasKey(e => e.RecommendationClickEventId);

            entity.ToTable("RecommendationClickEvent");

            entity.HasIndex(e => new { e.Placement, e.CreatedAt }, "IX_RecommendationClickEvent_Placement_CreatedAt").IsDescending(false, true);
            entity.HasIndex(e => new { e.ProductId, e.CreatedAt }, "IX_RecommendationClickEvent_Product_CreatedAt").IsDescending(false, true);
            entity.HasIndex(e => new { e.UserId, e.CreatedAt }, "IX_RecommendationClickEvent_User_CreatedAt").IsDescending(false, true);

            entity.Property(e => e.Algorithm)
                .IsRequired()
                .HasMaxLength(30);
            entity.Property(e => e.CreatedAt)
                .HasDefaultValueSql("(getutcdate())")
                .HasAnnotation("Relational:DefaultConstraintName", "DF_RecommendationClickEvent_CreatedAt")
                .HasColumnType("datetime");
            entity.Property(e => e.Placement)
                .IsRequired()
                .HasMaxLength(60);
            entity.Property(e => e.SessionId)
                .IsRequired()
                .HasMaxLength(120);

            entity.HasOne<RecommendationImpressionEvent>()
                .WithMany()
                .HasForeignKey(e => e.RecommendationImpressionEventId)
                .HasConstraintName("FK_RecommendationClickEvent_RecommendationImpressionEvent");
        });

        modelBuilder.Entity<RecommendationProductAffinity>(entity =>
        {
            entity.HasKey(e => e.RecommendationProductAffinityId);

            entity.ToTable("RecommendationProductAffinity");

            entity.HasIndex(e => new { e.SeedProductId, e.AffinityScore }, "IX_RecommendationProductAffinity_SeedProduct_AffinityScore")
                .IsDescending(false, true);
            entity.HasIndex(e => new { e.CandidateProductId, e.AffinityScore }, "IX_RecommendationProductAffinity_CandidateProduct_AffinityScore")
                .IsDescending(false, true);
            entity.HasIndex(e => new { e.SeedProductId, e.CandidateProductId }, "UQ_RecommendationProductAffinity_Seed_Candidate")
                .IsUnique();

            entity.Property(e => e.ComputedAt)
                .HasDefaultValueSql("(getutcdate())")
                .HasAnnotation("Relational:DefaultConstraintName", "DF_RecommendationProductAffinity_ComputedAt")
                .HasColumnType("datetime");
        });

        modelBuilder.Entity<RecommendationSearchKeywordAffinity>(entity =>
        {
            entity.HasKey(e => e.RecommendationSearchKeywordAffinityId);

            entity.ToTable("RecommendationSearchKeywordAffinity");

            entity.HasIndex(e => new { e.Keyword, e.HybridSearchScore }, "IX_RecommendationSearchKeywordAffinity_Keyword_HybridScore")
                .IsDescending(false, true);
            entity.HasIndex(e => new { e.ProductId, e.HybridSearchScore }, "IX_RecommendationSearchKeywordAffinity_Product_HybridScore")
                .IsDescending(false, true);
            entity.HasIndex(e => new { e.Keyword, e.ProductId }, "UQ_RecommendationSearchKeywordAffinity_Keyword_Product")
                .IsUnique();

            entity.Property(e => e.ComputedAt)
                .HasDefaultValueSql("(getutcdate())")
                .HasAnnotation("Relational:DefaultConstraintName", "DF_RecommendationSearchKeywordAffinity_ComputedAt")
                .HasColumnType("datetime");
            entity.Property(e => e.Keyword)
                .IsRequired()
                .HasMaxLength(200);
        });

        modelBuilder.Entity<RecommendationHomePreferenceSeed>(entity =>
        {
            entity.HasKey(e => e.RecommendationHomePreferenceSeedId);

            entity.ToTable("RecommendationHomePreferenceSeed");

            entity.HasIndex(e => new { e.ScopeType, e.ScopeKey, e.PreferenceScore }, "IX_RecommendationHomePreferenceSeed_Scope_PreferenceScore")
                .IsDescending(false, false, true);
            entity.HasIndex(e => new { e.ScopeType, e.ScopeKey, e.ProductId }, "UQ_RecommendationHomePreferenceSeed_Scope_Product")
                .IsUnique();
            entity.HasIndex(e => new { e.UserId, e.PreferenceScore }, "IX_RecommendationHomePreferenceSeed_User_PreferenceScore")
                .IsDescending(false, true);

            entity.Property(e => e.ComputedAt)
                .HasDefaultValueSql("(getutcdate())")
                .HasAnnotation("Relational:DefaultConstraintName", "DF_RecommendationHomePreferenceSeed_ComputedAt")
                .HasColumnType("datetime");
            entity.Property(e => e.LastInteractedAtUtc).HasColumnType("datetime");
            entity.Property(e => e.ScopeKey)
                .IsRequired()
                .HasMaxLength(120);
            entity.Property(e => e.ScopeType)
                .IsRequired()
                .HasMaxLength(20);
        });

        modelBuilder.Entity<RecommendationHomeCollaborativeCandidate>(entity =>
        {
            entity.HasKey(e => e.RecommendationHomeCollaborativeCandidateId);

            entity.ToTable("RecommendationHomeCollaborativeCandidate");

            entity.HasIndex(e => new { e.ScopeType, e.ScopeKey, e.CollaborativeScore }, "IX_RecommendationHomeCollaborativeCandidate_Scope_CollaborativeScore")
                .IsDescending(false, false, true);
            entity.HasIndex(e => new { e.ScopeType, e.ScopeKey, e.ProductId }, "UQ_RecommendationHomeCollaborativeCandidate_Scope_Product")
                .IsUnique();
            entity.HasIndex(e => new { e.UserId, e.CollaborativeScore }, "IX_RecommendationHomeCollaborativeCandidate_User_CollaborativeScore")
                .IsDescending(false, true);

            entity.Property(e => e.ComputedAt)
                .HasDefaultValueSql("(getutcdate())")
                .HasAnnotation("Relational:DefaultConstraintName", "DF_RecommendationHomeCollaborativeCandidate_ComputedAt")
                .HasColumnType("datetime");
            entity.Property(e => e.ScopeKey)
                .IsRequired()
                .HasMaxLength(120);
            entity.Property(e => e.ScopeType)
                .IsRequired()
                .HasMaxLength(20);
        });
    }
}
