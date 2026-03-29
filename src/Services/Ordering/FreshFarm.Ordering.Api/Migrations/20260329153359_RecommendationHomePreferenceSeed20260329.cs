using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace FreshFarm.Ordering.Api.Migrations
{
    /// <inheritdoc />
    public partial class RecommendationHomePreferenceSeed20260329 : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "RecommendationHomePreferenceSeed",
                columns: table => new
                {
                    RecommendationHomePreferenceSeedId = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    ScopeType = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: false),
                    ScopeKey = table.Column<string>(type: "nvarchar(120)", maxLength: 120, nullable: false),
                    UserId = table.Column<int>(type: "int", nullable: true),
                    ProductId = table.Column<int>(type: "int", nullable: false),
                    ViewCount = table.Column<int>(type: "int", nullable: false),
                    SearchClickCount = table.Column<int>(type: "int", nullable: false),
                    RecommendationClickCount = table.Column<int>(type: "int", nullable: false),
                    PurchaseCount = table.Column<int>(type: "int", nullable: false),
                    PreferenceScore = table.Column<double>(type: "float", nullable: false),
                    LastInteractedAtUtc = table.Column<DateTime>(type: "datetime", nullable: true),
                    ComputedAt = table.Column<DateTime>(type: "datetime", nullable: false, defaultValueSql: "(getutcdate())")
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_RecommendationHomePreferenceSeed", x => x.RecommendationHomePreferenceSeedId);
                });

            migrationBuilder.CreateIndex(
                name: "IX_RecommendationHomePreferenceSeed_Scope_PreferenceScore",
                table: "RecommendationHomePreferenceSeed",
                columns: new[] { "ScopeType", "ScopeKey", "PreferenceScore" },
                descending: new[] { false, false, true });

            migrationBuilder.CreateIndex(
                name: "IX_RecommendationHomePreferenceSeed_User_PreferenceScore",
                table: "RecommendationHomePreferenceSeed",
                columns: new[] { "UserId", "PreferenceScore" },
                descending: new[] { false, true });

            migrationBuilder.CreateIndex(
                name: "UQ_RecommendationHomePreferenceSeed_Scope_Product",
                table: "RecommendationHomePreferenceSeed",
                columns: new[] { "ScopeType", "ScopeKey", "ProductId" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "RecommendationHomePreferenceSeed");
        }
    }
}
