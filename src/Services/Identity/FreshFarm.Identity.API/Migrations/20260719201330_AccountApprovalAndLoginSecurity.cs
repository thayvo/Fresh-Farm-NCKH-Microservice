using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace FreshFarm.Identity.Api.Migrations
{
    /// <inheritdoc />
    public partial class AccountApprovalAndLoginSecurity : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "ApprovalNote",
                table: "Users",
                type: "nvarchar(500)",
                maxLength: 500,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "ApprovalStatus",
                table: "Users",
                type: "nvarchar(20)",
                maxLength: 20,
                nullable: false,
                defaultValue: "Approved");

            migrationBuilder.AddColumn<long>(
                name: "ApprovalVersion",
                table: "Users",
                type: "bigint",
                nullable: false,
                defaultValue: 0L);

            migrationBuilder.AddColumn<DateTime>(
                name: "ApprovalStatusChangedAt",
                table: "Users",
                type: "datetime2(0)",
                precision: 0,
                nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "ApprovedAt",
                table: "Users",
                type: "datetime2(0)",
                precision: 0,
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "ApprovedByUserId",
                table: "Users",
                type: "int",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "PasswordResetNonce",
                table: "UserAuth",
                type: "varchar(64)",
                unicode: false,
                maxLength: 64,
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "TokenVersion",
                table: "UserAuth",
                type: "int",
                nullable: false,
                defaultValue: 0);

            // Existing users are backfilled as Approved by AddColumn above. Only future
            // inserts inherit Pending after this default constraint is changed.
            migrationBuilder.AlterColumn<string>(
                name: "ApprovalStatus",
                table: "Users",
                type: "nvarchar(20)",
                maxLength: 20,
                nullable: false,
                defaultValue: "Pending",
                oldClrType: typeof(string),
                oldType: "nvarchar(20)",
                oldMaxLength: 20,
                oldDefaultValue: "Approved");

            migrationBuilder.CreateTable(
                name: "AccountApprovalEvent",
                columns: table => new
                {
                    AccountApprovalEventId = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    UserId = table.Column<int>(type: "int", nullable: false),
                    ActorUserId = table.Column<int>(type: "int", nullable: false),
                    FromStatus = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: false),
                    ToStatus = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: false),
                    Note = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: true),
                    OccurredAt = table.Column<DateTime>(type: "datetime2(0)", precision: 0, nullable: false, defaultValueSql: "(sysutcdatetime())")
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_AccountApprovalEvent", x => x.AccountApprovalEventId);
                    table.ForeignKey(
                        name: "FK_AccountApprovalEvent_Users_ActorUserId",
                        column: x => x.ActorUserId,
                        principalTable: "Users",
                        principalColumn: "UserId");
                    table.ForeignKey(
                        name: "FK_AccountApprovalEvent_Users_UserId",
                        column: x => x.UserId,
                        principalTable: "Users",
                        principalColumn: "UserId",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "LoginDeviceSecurityState",
                columns: table => new
                {
                    LoginDeviceSecurityStateId = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    DeviceKeyHash = table.Column<string>(type: "varchar(64)", unicode: false, maxLength: 64, nullable: false),
                    LastUserId = table.Column<int>(type: "int", nullable: true),
                    FailedCount = table.Column<int>(type: "int", nullable: false, defaultValue: 0),
                    LockoutLevel = table.Column<int>(type: "int", nullable: false, defaultValue: 0),
                    LockedUntil = table.Column<DateTime>(type: "datetime2(0)", precision: 0, nullable: true),
                    LastFailedAt = table.Column<DateTime>(type: "datetime2(0)", precision: 0, nullable: true),
                    CreatedAt = table.Column<DateTime>(type: "datetime2(0)", precision: 0, nullable: false, defaultValueSql: "(sysutcdatetime())"),
                    UpdatedAt = table.Column<DateTime>(type: "datetime2(0)", precision: 0, nullable: false, defaultValueSql: "(sysutcdatetime())")
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_LoginDeviceSecurityState", x => x.LoginDeviceSecurityStateId);
                    table.ForeignKey(
                        name: "FK_LoginDeviceSecurityState_Users_LastUserId",
                        column: x => x.LastUserId,
                        principalTable: "Users",
                        principalColumn: "UserId",
                        onDelete: ReferentialAction.SetNull);
                });

            migrationBuilder.CreateIndex(
                name: "IX_Users_ApprovalStatus_CreatedAt",
                table: "Users",
                columns: new[] { "ApprovalStatus", "CreatedAt" });

            migrationBuilder.CreateIndex(
                name: "IX_AccountApprovalEvent_ActorUserId_OccurredAt",
                table: "AccountApprovalEvent",
                columns: new[] { "ActorUserId", "OccurredAt" },
                descending: new[] { false, true });

            migrationBuilder.CreateIndex(
                name: "IX_AccountApprovalEvent_UserId_OccurredAt",
                table: "AccountApprovalEvent",
                columns: new[] { "UserId", "OccurredAt" },
                descending: new[] { false, true });

            migrationBuilder.CreateIndex(
                name: "IX_LoginDeviceSecurityState_LastUserId_LockedUntil",
                table: "LoginDeviceSecurityState",
                columns: new[] { "LastUserId", "LockedUntil" });

            migrationBuilder.CreateIndex(
                name: "UX_LoginDeviceSecurityState_DeviceKeyHash",
                table: "LoginDeviceSecurityState",
                column: "DeviceKeyHash",
                unique: true);

        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "AccountApprovalEvent");

            migrationBuilder.DropTable(
                name: "LoginDeviceSecurityState");

            migrationBuilder.DropIndex(
                name: "IX_Users_ApprovalStatus_CreatedAt",
                table: "Users");

            migrationBuilder.DropColumn(
                name: "ApprovalNote",
                table: "Users");

            migrationBuilder.DropColumn(
                name: "ApprovalStatus",
                table: "Users");

            migrationBuilder.DropColumn(
                name: "ApprovalStatusChangedAt",
                table: "Users");

            migrationBuilder.DropColumn(
                name: "ApprovalVersion",
                table: "Users");

            migrationBuilder.DropColumn(
                name: "ApprovedAt",
                table: "Users");

            migrationBuilder.DropColumn(
                name: "ApprovedByUserId",
                table: "Users");

            migrationBuilder.DropColumn(
                name: "PasswordResetNonce",
                table: "UserAuth");

            migrationBuilder.DropColumn(
                name: "TokenVersion",
                table: "UserAuth");

        }
    }
}
