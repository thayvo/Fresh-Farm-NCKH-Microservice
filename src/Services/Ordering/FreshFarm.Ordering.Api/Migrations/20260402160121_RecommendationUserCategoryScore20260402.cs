using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace FreshFarm.Ordering.Api.Migrations
{
    /// <inheritdoc />
    public partial class RecommendationUserCategoryScore20260402 : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "RecommendationUserCategoryScore",
                columns: table => new
                {
                    RecommendationUserCategoryScoreId = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    UserId = table.Column<int>(type: "int", nullable: false),
                    CategoryId = table.Column<int>(type: "int", nullable: false),
                    CategoryName = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: false),
                    ViewCount = table.Column<int>(type: "int", nullable: false),
                    SearchClickCount = table.Column<int>(type: "int", nullable: false),
                    RecommendationClickCount = table.Column<int>(type: "int", nullable: false),
                    PurchaseCount = table.Column<int>(type: "int", nullable: false),
                    UserCategoryScore = table.Column<double>(type: "float", nullable: false),
                    LastInteractedAtUtc = table.Column<DateTime>(type: "datetime", nullable: true),
                    ComputedAt = table.Column<DateTime>(type: "datetime", nullable: false, defaultValueSql: "(getutcdate())")
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_RecommendationUserCategoryScore", x => x.RecommendationUserCategoryScoreId);
                });

            migrationBuilder.CreateIndex(
                name: "IX_RecommendationUserCategoryScore_User_Score",
                table: "RecommendationUserCategoryScore",
                columns: new[] { "UserId", "UserCategoryScore" },
                descending: new[] { false, true });

            migrationBuilder.CreateIndex(
                name: "UQ_RecommendationUserCategoryScore_User_Category",
                table: "RecommendationUserCategoryScore",
                columns: new[] { "UserId", "CategoryId" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "RecommendationUserCategoryScore");
        }
    }
}
