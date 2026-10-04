using System;
using FreshFarm.Ordering.Api.Models;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace FreshFarm.Ordering.Api.Migrations
{
    /// <inheritdoc />
    [DbContext(typeof(FreshFarmOrderingDBContext))]
    [Migration("20260426170000_FinancePayoutCommission20260426")]
    public partial class FinancePayoutCommission20260426 : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "SellerCommissionSetting",
                columns: table => new
                {
                    SellerCommissionSettingID = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    SellerID = table.Column<int>(type: "int", nullable: true),
                    CommissionRate = table.Column<decimal>(type: "decimal(9,4)", nullable: false),
                    IsActive = table.Column<bool>(type: "bit", nullable: false, defaultValue: true),
                    EffectiveFrom = table.Column<DateTime>(type: "datetime", nullable: false, defaultValueSql: "(getutcdate())"),
                    EffectiveTo = table.Column<DateTime>(type: "datetime", nullable: true),
                    CreatedAt = table.Column<DateTime>(type: "datetime", nullable: false, defaultValueSql: "(getutcdate())"),
                    UpdatedAt = table.Column<DateTime>(type: "datetime", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_SellerCommissionSetting", x => x.SellerCommissionSettingID);
                });

            migrationBuilder.CreateIndex(
                name: "IX_SellerCommissionSetting_Seller_Active_EffectiveFrom",
                table: "SellerCommissionSetting",
                columns: new[] { "SellerID", "IsActive", "EffectiveFrom" },
                descending: new[] { false, false, true });

            migrationBuilder.Sql(@"
IF NOT EXISTS (
    SELECT 1
    FROM [dbo].[SellerCommissionSetting]
    WHERE [SellerID] IS NULL
      AND [IsActive] = 1
      AND [EffectiveTo] IS NULL)
BEGIN
    INSERT INTO [dbo].[SellerCommissionSetting] ([SellerID], [CommissionRate], [IsActive], [EffectiveFrom], [CreatedAt])
    VALUES (NULL, 0.1000, 1, GETUTCDATE(), GETUTCDATE());
END

IF EXISTS (
    SELECT 1
    FROM [dbo].[PayoutItems]
    GROUP BY [SellerOrderID]
    HAVING COUNT(*) > 1)
BEGIN
    THROW 51001, 'PayoutItems has duplicate SellerOrderID rows. Clean duplicate payout items before applying FinancePayoutCommission20260426.', 1;
END");

            migrationBuilder.CreateIndex(
                name: "UX_PayoutItems_SellerOrderID",
                table: "PayoutItems",
                column: "SellerOrderID",
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "UX_PayoutItems_SellerOrderID",
                table: "PayoutItems");

            migrationBuilder.DropTable(
                name: "SellerCommissionSetting");
        }
    }
}
