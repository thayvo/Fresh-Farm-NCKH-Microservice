using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace FreshFarm.Ordering.Api.Migrations
{
    /// <inheritdoc />
    public partial class RecommendationHomeCollaborativeCandidate20260329 : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "RecommendationHomeCollaborativeCandidate",
                columns: table => new
                {
                    RecommendationHomeCollaborativeCandidateId = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    ScopeType = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: false),
                    ScopeKey = table.Column<string>(type: "nvarchar(120)", maxLength: 120, nullable: false),
                    UserId = table.Column<int>(type: "int", nullable: true),
                    ProductId = table.Column<int>(type: "int", nullable: false),
                    CoPurchaseOrderCount = table.Column<int>(type: "int", nullable: false),
                    CoViewSessionCount = table.Column<int>(type: "int", nullable: false),
                    CoClickSessionCount = table.Column<int>(type: "int", nullable: false),
                    CollaborativeScore = table.Column<double>(type: "float", nullable: false),
                    ComputedAt = table.Column<DateTime>(type: "datetime", nullable: false, defaultValueSql: "(getutcdate())")
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_RecommendationHomeCollaborativeCandidate", x => x.RecommendationHomeCollaborativeCandidateId);
                });

            migrationBuilder.CreateIndex(
                name: "IX_RecommendationHomeCollaborativeCandidate_Scope_CollaborativeScore",
                table: "RecommendationHomeCollaborativeCandidate",
                columns: new[] { "ScopeType", "ScopeKey", "CollaborativeScore" },
                descending: new[] { false, false, true });

            migrationBuilder.CreateIndex(
                name: "IX_RecommendationHomeCollaborativeCandidate_User_CollaborativeScore",
                table: "RecommendationHomeCollaborativeCandidate",
                columns: new[] { "UserId", "CollaborativeScore" },
                descending: new[] { false, true });

            migrationBuilder.CreateIndex(
                name: "UQ_RecommendationHomeCollaborativeCandidate_Scope_Product",
                table: "RecommendationHomeCollaborativeCandidate",
                columns: new[] { "ScopeType", "ScopeKey", "ProductId" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "RecommendationHomeCollaborativeCandidate");
        }
    }
}
