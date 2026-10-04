using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace FreshFarm.Identity.Api.Migrations
{
    /// <inheritdoc />
    public partial class GoogleExternalLoginBinding : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "ExternalLoginProvider",
                table: "UserAuth",
                type: "nvarchar(50)",
                maxLength: 50,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "ExternalLoginSubject",
                table: "UserAuth",
                type: "nvarchar(200)",
                maxLength: 200,
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "UX_UserAuth_ExternalLogin",
                table: "UserAuth",
                columns: new[] { "ExternalLoginProvider", "ExternalLoginSubject" },
                unique: true,
                filter: "[ExternalLoginProvider] IS NOT NULL AND [ExternalLoginSubject] IS NOT NULL");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "UX_UserAuth_ExternalLogin",
                table: "UserAuth");

            migrationBuilder.DropColumn(
                name: "ExternalLoginProvider",
                table: "UserAuth");

            migrationBuilder.DropColumn(
                name: "ExternalLoginSubject",
                table: "UserAuth");
        }
    }
}
