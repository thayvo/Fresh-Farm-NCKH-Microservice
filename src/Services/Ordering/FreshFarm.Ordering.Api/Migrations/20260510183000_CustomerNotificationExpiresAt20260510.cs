using FreshFarm.Ordering.Api.Models;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace FreshFarm.Ordering.Api.Migrations
{
    /// <inheritdoc />
    [DbContext(typeof(FreshFarmOrderingDBContext))]
    [Migration("20260510183000_CustomerNotificationExpiresAt20260510")]
    public partial class CustomerNotificationExpiresAt20260510 : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql(
                """
                IF OBJECT_ID(N'[dbo].[CustomerNotifications]', N'U') IS NOT NULL
                    AND COL_LENGTH(N'[dbo].[CustomerNotifications]', N'ExpiresAt') IS NULL
                BEGIN
                    ALTER TABLE [dbo].[CustomerNotifications]
                    ADD [ExpiresAt] datetime NULL;
                END;
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql(
                """
                IF OBJECT_ID(N'[dbo].[CustomerNotifications]', N'U') IS NOT NULL
                    AND COL_LENGTH(N'[dbo].[CustomerNotifications]', N'ExpiresAt') IS NOT NULL
                BEGIN
                    ALTER TABLE [dbo].[CustomerNotifications]
                    DROP COLUMN [ExpiresAt];
                END;
                """);
        }
    }
}
