using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace FreshFarm.Ordering.Api.Migrations
{
    /// <inheritdoc />
    public partial class RecommendationUserSellerScore20260402 : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "RecommendationUserSellerScore",
                columns: table => new
                {
                    RecommendationUserSellerScoreId = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    UserId = table.Column<int>(type: "int", nullable: false),
                    SellerId = table.Column<int>(type: "int", nullable: false),
                    ViewCount = table.Column<int>(type: "int", nullable: false),
                    SearchClickCount = table.Column<int>(type: "int", nullable: false),
                    PurchaseCount = table.Column<int>(type: "int", nullable: false),
                    UserSellerScore = table.Column<double>(type: "float", nullable: false),
                    LastInteractedAtUtc = table.Column<DateTime>(type: "datetime", nullable: true),
                    ComputedAt = table.Column<DateTime>(type: "datetime", nullable: false, defaultValueSql: "(getutcdate())")
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_RecommendationUserSellerScore", x => x.RecommendationUserSellerScoreId);
                });

            migrationBuilder.CreateIndex(
                name: "IX_RecommendationUserSellerScore_User_Score",
                table: "RecommendationUserSellerScore",
                columns: new[] { "UserId", "UserSellerScore" },
                descending: new[] { false, true });

            migrationBuilder.CreateIndex(
                name: "UQ_RecommendationUserSellerScore_User_Seller",
                table: "RecommendationUserSellerScore",
                columns: new[] { "UserId", "SellerId" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "RecommendationUserSellerScore");
        }
    }
}
