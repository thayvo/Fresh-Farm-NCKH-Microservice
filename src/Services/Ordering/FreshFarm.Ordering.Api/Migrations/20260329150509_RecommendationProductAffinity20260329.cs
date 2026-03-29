using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace FreshFarm.Ordering.Api.Migrations
{
    /// <inheritdoc />
    public partial class RecommendationProductAffinity20260329 : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "RecommendationProductAffinity",
                columns: table => new
                {
                    RecommendationProductAffinityId = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    SeedProductId = table.Column<int>(type: "int", nullable: false),
                    CandidateProductId = table.Column<int>(type: "int", nullable: false),
                    CoPurchaseOrderCount = table.Column<int>(type: "int", nullable: false),
                    CoViewSessionCount = table.Column<int>(type: "int", nullable: false),
                    CoClickSessionCount = table.Column<int>(type: "int", nullable: false),
                    AffinityScore = table.Column<double>(type: "float", nullable: false),
                    ComputedAt = table.Column<DateTime>(type: "datetime", nullable: false, defaultValueSql: "(getutcdate())")
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_RecommendationProductAffinity", x => x.RecommendationProductAffinityId);
                });

            migrationBuilder.CreateIndex(
                name: "IX_RecommendationProductAffinity_CandidateProduct_AffinityScore",
                table: "RecommendationProductAffinity",
                columns: new[] { "CandidateProductId", "AffinityScore" },
                descending: new[] { false, true });

            migrationBuilder.CreateIndex(
                name: "IX_RecommendationProductAffinity_SeedProduct_AffinityScore",
                table: "RecommendationProductAffinity",
                columns: new[] { "SeedProductId", "AffinityScore" },
                descending: new[] { false, true });

            migrationBuilder.CreateIndex(
                name: "UQ_RecommendationProductAffinity_Seed_Candidate",
                table: "RecommendationProductAffinity",
                columns: new[] { "SeedProductId", "CandidateProductId" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "RecommendationProductAffinity");
        }
    }
}
