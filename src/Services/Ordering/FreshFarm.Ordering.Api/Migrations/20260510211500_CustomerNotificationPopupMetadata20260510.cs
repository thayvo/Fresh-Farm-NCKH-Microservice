using FreshFarm.Ordering.Api.Models;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace FreshFarm.Ordering.Api.Migrations
{
    /// <inheritdoc />
    [DbContext(typeof(FreshFarmOrderingDBContext))]
    [Migration("20260510211500_CustomerNotificationPopupMetadata20260510")]
    public partial class CustomerNotificationPopupMetadata20260510 : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql(
                """
                IF OBJECT_ID(N'[dbo].[CustomerNotifications]', N'U') IS NOT NULL
                    AND COL_LENGTH(N'[dbo].[CustomerNotifications]', N'PopupType') IS NULL
                BEGIN
                    ALTER TABLE [dbo].[CustomerNotifications]
                    ADD [PopupType] varchar(20) NULL;
                END;

                IF OBJECT_ID(N'[dbo].[CustomerNotifications]', N'U') IS NOT NULL
                    AND COL_LENGTH(N'[dbo].[CustomerNotifications]', N'PopupImageUrl') IS NULL
                BEGIN
                    ALTER TABLE [dbo].[CustomerNotifications]
                    ADD [PopupImageUrl] nvarchar(1000) NULL;
                END;
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql(
                """
                IF OBJECT_ID(N'[dbo].[CustomerNotifications]', N'U') IS NOT NULL
                    AND COL_LENGTH(N'[dbo].[CustomerNotifications]', N'PopupImageUrl') IS NOT NULL
                BEGIN
                    ALTER TABLE [dbo].[CustomerNotifications]
                    DROP COLUMN [PopupImageUrl];
                END;

                IF OBJECT_ID(N'[dbo].[CustomerNotifications]', N'U') IS NOT NULL
                    AND COL_LENGTH(N'[dbo].[CustomerNotifications]', N'PopupType') IS NOT NULL
                BEGIN
                    ALTER TABLE [dbo].[CustomerNotifications]
                    DROP COLUMN [PopupType];
                END;
                """);
        }
    }
}
