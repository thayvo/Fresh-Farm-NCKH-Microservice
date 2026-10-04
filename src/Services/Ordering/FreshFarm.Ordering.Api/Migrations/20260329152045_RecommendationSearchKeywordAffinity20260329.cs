using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace FreshFarm.Ordering.Api.Migrations
{
    /// <inheritdoc />
    public partial class RecommendationSearchKeywordAffinity20260329 : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "RecommendationSearchKeywordAffinity",
                columns: table => new
                {
                    RecommendationSearchKeywordAffinityId = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    Keyword = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: false),
                    ProductId = table.Column<int>(type: "int", nullable: false),
                    SearchClickCount = table.Column<int>(type: "int", nullable: false),
                    SearchClickSessionCount = table.Column<int>(type: "int", nullable: false),
                    SearchViewSessionCount = table.Column<int>(type: "int", nullable: false),
                    SearchRecommendationClickCount = table.Column<int>(type: "int", nullable: false),
                    HybridSearchScore = table.Column<double>(type: "float", nullable: false),
                    ComputedAt = table.Column<DateTime>(type: "datetime", nullable: false, defaultValueSql: "(getutcdate())")
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_RecommendationSearchKeywordAffinity", x => x.RecommendationSearchKeywordAffinityId);
                });

            migrationBuilder.CreateIndex(
                name: "IX_RecommendationSearchKeywordAffinity_Keyword_HybridScore",
                table: "RecommendationSearchKeywordAffinity",
                columns: new[] { "Keyword", "HybridSearchScore" },
                descending: new[] { false, true });

            migrationBuilder.CreateIndex(
                name: "IX_RecommendationSearchKeywordAffinity_Product_HybridScore",
                table: "RecommendationSearchKeywordAffinity",
                columns: new[] { "ProductId", "HybridSearchScore" },
                descending: new[] { false, true });

            migrationBuilder.CreateIndex(
                name: "UQ_RecommendationSearchKeywordAffinity_Keyword_Product",
                table: "RecommendationSearchKeywordAffinity",
                columns: new[] { "Keyword", "ProductId" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "RecommendationSearchKeywordAffinity");
        }
    }
}
