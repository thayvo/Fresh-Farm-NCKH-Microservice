using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace FreshFarm.Ordering.Api.Migrations
{
    /// <inheritdoc />
    public partial class RecommendationEventTracking20260329 : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "ProductViewEvent",
                columns: table => new
                {
                    ProductViewEventId = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    UserId = table.Column<int>(type: "int", nullable: true),
                    SessionId = table.Column<string>(type: "nvarchar(120)", maxLength: 120, nullable: false),
                    ProductId = table.Column<int>(type: "int", nullable: false),
                    SellerId = table.Column<int>(type: "int", nullable: true),
                    SourcePage = table.Column<string>(type: "nvarchar(50)", maxLength: 50, nullable: false),
                    SourceModule = table.Column<string>(type: "nvarchar(80)", maxLength: 80, nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "datetime", nullable: false, defaultValueSql: "(getutcdate())")
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ProductViewEvent", x => x.ProductViewEventId);
                });

            migrationBuilder.CreateTable(
                name: "RecommendationImpressionEvent",
                columns: table => new
                {
                    RecommendationImpressionEventId = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    UserId = table.Column<int>(type: "int", nullable: true),
                    SessionId = table.Column<string>(type: "nvarchar(120)", maxLength: 120, nullable: false),
                    Placement = table.Column<string>(type: "nvarchar(60)", maxLength: 60, nullable: false),
                    RecommendationRunId = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: true),
                    ProductId = table.Column<int>(type: "int", nullable: false),
                    Rank = table.Column<int>(type: "int", nullable: false),
                    Algorithm = table.Column<string>(type: "nvarchar(30)", maxLength: 30, nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "datetime", nullable: false, defaultValueSql: "(getutcdate())")
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_RecommendationImpressionEvent", x => x.RecommendationImpressionEventId);
                });

            migrationBuilder.CreateTable(
                name: "SearchEvent",
                columns: table => new
                {
                    SearchEventId = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    UserId = table.Column<int>(type: "int", nullable: true),
                    SessionId = table.Column<string>(type: "nvarchar(120)", maxLength: 120, nullable: false),
                    Keyword = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: false),
                    FiltersJson = table.Column<string>(type: "nvarchar(4000)", maxLength: 4000, nullable: true),
                    ResultCount = table.Column<int>(type: "int", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "datetime", nullable: false, defaultValueSql: "(getutcdate())")
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_SearchEvent", x => x.SearchEventId);
                });

            migrationBuilder.CreateTable(
                name: "RecommendationClickEvent",
                columns: table => new
                {
                    RecommendationClickEventId = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    RecommendationImpressionEventId = table.Column<int>(type: "int", nullable: true),
                    UserId = table.Column<int>(type: "int", nullable: true),
                    SessionId = table.Column<string>(type: "nvarchar(120)", maxLength: 120, nullable: false),
                    ProductId = table.Column<int>(type: "int", nullable: false),
                    Placement = table.Column<string>(type: "nvarchar(60)", maxLength: 60, nullable: false),
                    Algorithm = table.Column<string>(type: "nvarchar(30)", maxLength: 30, nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "datetime", nullable: false, defaultValueSql: "(getutcdate())")
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_RecommendationClickEvent", x => x.RecommendationClickEventId);
                    table.ForeignKey(
                        name: "FK_RecommendationClickEvent_RecommendationImpressionEvent",
                        column: x => x.RecommendationImpressionEventId,
                        principalTable: "RecommendationImpressionEvent",
                        principalColumn: "RecommendationImpressionEventId");
                });

            migrationBuilder.CreateTable(
                name: "SearchClickEvent",
                columns: table => new
                {
                    SearchClickEventId = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    SearchEventId = table.Column<int>(type: "int", nullable: true),
                    UserId = table.Column<int>(type: "int", nullable: true),
                    SessionId = table.Column<string>(type: "nvarchar(120)", maxLength: 120, nullable: false),
                    ProductId = table.Column<int>(type: "int", nullable: false),
                    SellerId = table.Column<int>(type: "int", nullable: true),
                    Rank = table.Column<int>(type: "int", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "datetime", nullable: false, defaultValueSql: "(getutcdate())")
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_SearchClickEvent", x => x.SearchClickEventId);
                    table.ForeignKey(
                        name: "FK_SearchClickEvent_SearchEvent",
                        column: x => x.SearchEventId,
                        principalTable: "SearchEvent",
                        principalColumn: "SearchEventId");
                });

            migrationBuilder.CreateIndex(
                name: "IX_ProductViewEvent_Product_CreatedAt",
                table: "ProductViewEvent",
                columns: new[] { "ProductId", "CreatedAt" },
                descending: new[] { false, true });

            migrationBuilder.CreateIndex(
                name: "IX_ProductViewEvent_Session_CreatedAt",
                table: "ProductViewEvent",
                columns: new[] { "SessionId", "CreatedAt" },
                descending: new[] { false, true });

            migrationBuilder.CreateIndex(
                name: "IX_ProductViewEvent_User_CreatedAt",
                table: "ProductViewEvent",
                columns: new[] { "UserId", "CreatedAt" },
                descending: new[] { false, true });

            migrationBuilder.CreateIndex(
                name: "IX_RecommendationClickEvent_Placement_CreatedAt",
                table: "RecommendationClickEvent",
                columns: new[] { "Placement", "CreatedAt" },
                descending: new[] { false, true });

            migrationBuilder.CreateIndex(
                name: "IX_RecommendationClickEvent_Product_CreatedAt",
                table: "RecommendationClickEvent",
                columns: new[] { "ProductId", "CreatedAt" },
                descending: new[] { false, true });

            migrationBuilder.CreateIndex(
                name: "IX_RecommendationClickEvent_RecommendationImpressionEventId",
                table: "RecommendationClickEvent",
                column: "RecommendationImpressionEventId");

            migrationBuilder.CreateIndex(
                name: "IX_RecommendationClickEvent_User_CreatedAt",
                table: "RecommendationClickEvent",
                columns: new[] { "UserId", "CreatedAt" },
                descending: new[] { false, true });

            migrationBuilder.CreateIndex(
                name: "IX_RecommendationImpressionEvent_Placement_CreatedAt",
                table: "RecommendationImpressionEvent",
                columns: new[] { "Placement", "CreatedAt" },
                descending: new[] { false, true });

            migrationBuilder.CreateIndex(
                name: "IX_RecommendationImpressionEvent_Session_CreatedAt",
                table: "RecommendationImpressionEvent",
                columns: new[] { "SessionId", "CreatedAt" },
                descending: new[] { false, true });

            migrationBuilder.CreateIndex(
                name: "IX_RecommendationImpressionEvent_User_CreatedAt",
                table: "RecommendationImpressionEvent",
                columns: new[] { "UserId", "CreatedAt" },
                descending: new[] { false, true });

            migrationBuilder.CreateIndex(
                name: "IX_SearchClickEvent_Product_CreatedAt",
                table: "SearchClickEvent",
                columns: new[] { "ProductId", "CreatedAt" },
                descending: new[] { false, true });

            migrationBuilder.CreateIndex(
                name: "IX_SearchClickEvent_SearchEvent_CreatedAt",
                table: "SearchClickEvent",
                columns: new[] { "SearchEventId", "CreatedAt" },
                descending: new[] { false, true });

            migrationBuilder.CreateIndex(
                name: "IX_SearchClickEvent_User_CreatedAt",
                table: "SearchClickEvent",
                columns: new[] { "UserId", "CreatedAt" },
                descending: new[] { false, true });

            migrationBuilder.CreateIndex(
                name: "IX_SearchEvent_CreatedAt_Keyword",
                table: "SearchEvent",
                columns: new[] { "CreatedAt", "Keyword" },
                descending: new[] { true, false });

            migrationBuilder.CreateIndex(
                name: "IX_SearchEvent_Session_CreatedAt",
                table: "SearchEvent",
                columns: new[] { "SessionId", "CreatedAt" },
                descending: new[] { false, true });

            migrationBuilder.CreateIndex(
                name: "IX_SearchEvent_User_CreatedAt",
                table: "SearchEvent",
                columns: new[] { "UserId", "CreatedAt" },
                descending: new[] { false, true });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "ProductViewEvent");

            migrationBuilder.DropTable(
                name: "RecommendationClickEvent");

            migrationBuilder.DropTable(
                name: "SearchClickEvent");

            migrationBuilder.DropTable(
                name: "RecommendationImpressionEvent");

            migrationBuilder.DropTable(
                name: "SearchEvent");
        }
    }
}
