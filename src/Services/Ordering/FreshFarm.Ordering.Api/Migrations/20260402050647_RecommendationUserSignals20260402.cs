using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace FreshFarm.Ordering.Api.Migrations
{
    /// <inheritdoc />
    public partial class RecommendationUserSignals20260402 : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "RecommendationBasketAffinity",
                columns: table => new
                {
                    RecommendationBasketAffinityId = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    ProductId = table.Column<int>(type: "int", nullable: false),
                    CandidateProductId = table.Column<int>(type: "int", nullable: false),
                    CoPurchaseOrderCount = table.Column<int>(type: "int", nullable: false),
                    BasketScore = table.Column<double>(type: "float", nullable: false),
                    ComputedAt = table.Column<DateTime>(type: "datetime", nullable: false, defaultValueSql: "(getutcdate())")
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_RecommendationBasketAffinity", x => x.RecommendationBasketAffinityId);
                });

            migrationBuilder.CreateTable(
                name: "RecommendationReplenishmentProfile",
                columns: table => new
                {
                    RecommendationReplenishmentProfileId = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    UserId = table.Column<int>(type: "int", nullable: false),
                    ProductId = table.Column<int>(type: "int", nullable: false),
                    PurchaseCount = table.Column<int>(type: "int", nullable: false),
                    LastPurchasedAtUtc = table.Column<DateTime>(type: "datetime", nullable: false),
                    AverageRepurchaseDays = table.Column<double>(type: "float", nullable: false),
                    ExpectedReorderAtUtc = table.Column<DateTime>(type: "datetime", nullable: true),
                    ReplenishmentScore = table.Column<double>(type: "float", nullable: false),
                    ComputedAt = table.Column<DateTime>(type: "datetime", nullable: false, defaultValueSql: "(getutcdate())")
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_RecommendationReplenishmentProfile", x => x.RecommendationReplenishmentProfileId);
                });

            migrationBuilder.CreateTable(
                name: "RecommendationUserProductScore",
                columns: table => new
                {
                    RecommendationUserProductScoreId = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    UserId = table.Column<int>(type: "int", nullable: false),
                    ProductId = table.Column<int>(type: "int", nullable: false),
                    ViewCount = table.Column<int>(type: "int", nullable: false),
                    SearchClickCount = table.Column<int>(type: "int", nullable: false),
                    RecommendationClickCount = table.Column<int>(type: "int", nullable: false),
                    PurchaseCount = table.Column<int>(type: "int", nullable: false),
                    UserProductScore = table.Column<double>(type: "float", nullable: false),
                    LastInteractedAtUtc = table.Column<DateTime>(type: "datetime", nullable: true),
                    ComputedAt = table.Column<DateTime>(type: "datetime", nullable: false, defaultValueSql: "(getutcdate())")
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_RecommendationUserProductScore", x => x.RecommendationUserProductScoreId);
                });

            migrationBuilder.CreateIndex(
                name: "IX_RecommendationBasketAffinity_Product_Score",
                table: "RecommendationBasketAffinity",
                columns: new[] { "ProductId", "BasketScore" },
                descending: new[] { false, true });

            migrationBuilder.CreateIndex(
                name: "UQ_RecommendationBasketAffinity_Product_Candidate",
                table: "RecommendationBasketAffinity",
                columns: new[] { "ProductId", "CandidateProductId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_RecommendationReplenishmentProfile_User_Score",
                table: "RecommendationReplenishmentProfile",
                columns: new[] { "UserId", "ReplenishmentScore" },
                descending: new[] { false, true });

            migrationBuilder.CreateIndex(
                name: "UQ_RecommendationReplenishmentProfile_User_Product",
                table: "RecommendationReplenishmentProfile",
                columns: new[] { "UserId", "ProductId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_RecommendationUserProductScore_User_Score",
                table: "RecommendationUserProductScore",
                columns: new[] { "UserId", "UserProductScore" },
                descending: new[] { false, true });

            migrationBuilder.CreateIndex(
                name: "UQ_RecommendationUserProductScore_User_Product",
                table: "RecommendationUserProductScore",
                columns: new[] { "UserId", "ProductId" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "RecommendationBasketAffinity");

            migrationBuilder.DropTable(
                name: "RecommendationReplenishmentProfile");

            migrationBuilder.DropTable(
                name: "RecommendationUserProductScore");
        }
    }
}
