using FreshFarm.Ordering.Api.Models;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace FreshFarm.Ordering.Api.Migrations
{
    /// <inheritdoc />
    [DbContext(typeof(FreshFarmOrderingDBContext))]
    [Migration("20260426143000_CartItemSellerIdRequired20260426")]
    public partial class CartItemSellerIdRequired20260426 : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql(@"
IF EXISTS (SELECT 1 FROM [dbo].[CartItem] WHERE [SellerID] <= 0)
BEGIN
    THROW 51000, 'CartItem has SellerID <= 0. Clean cart data before applying CartItemSellerIdRequired20260426.', 1;
END

DECLARE @dfName sysname;
SELECT @dfName = dc.name
FROM sys.default_constraints AS dc
INNER JOIN sys.columns AS c ON c.default_object_id = dc.object_id
INNER JOIN sys.tables AS t ON t.object_id = c.object_id
INNER JOIN sys.schemas AS s ON s.schema_id = t.schema_id
WHERE s.name = N'dbo'
  AND t.name = N'CartItem'
  AND c.name = N'SellerID';

IF @dfName IS NOT NULL
BEGIN
    DECLARE @sql nvarchar(max) = N'ALTER TABLE [dbo].[CartItem] DROP CONSTRAINT ' + QUOTENAME(@dfName);
    EXEC sp_executesql @sql;
END

IF NOT EXISTS (
    SELECT 1
    FROM sys.check_constraints
    WHERE parent_object_id = OBJECT_ID(N'[dbo].[CartItem]')
      AND name = N'CK_CartItem_SellerID_Positive')
BEGIN
    ALTER TABLE [dbo].[CartItem] WITH CHECK
    ADD CONSTRAINT [CK_CartItem_SellerID_Positive] CHECK ([SellerID] > 0);
END

ALTER TABLE [dbo].[CartItem] CHECK CONSTRAINT [CK_CartItem_SellerID_Positive];");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql(@"
IF EXISTS (
    SELECT 1
    FROM sys.check_constraints
    WHERE parent_object_id = OBJECT_ID(N'[dbo].[CartItem]')
      AND name = N'CK_CartItem_SellerID_Positive')
BEGIN
    ALTER TABLE [dbo].[CartItem] DROP CONSTRAINT [CK_CartItem_SellerID_Positive];
END");

            migrationBuilder.AlterColumn<int>(
                name: "SellerID",
                table: "CartItem",
                type: "int",
                nullable: false,
                defaultValue: 0,
                oldClrType: typeof(int),
                oldType: "int");
        }
    }
}
