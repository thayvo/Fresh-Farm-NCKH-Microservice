using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace FreshFarm.Identity.Api.Migrations
{
    /// <inheritdoc />
    public partial class IdentitySchemaBaseline20260324 : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "Permissions",
                columns: table => new
                {
                    PermissionId = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    PermissionName = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: false),
                    PermissionCode = table.Column<string>(type: "varchar(50)", unicode: false, maxLength: 50, nullable: false),
                    ModuleName = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: false),
                    Description = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: true),
                    IsActive = table.Column<bool>(type: "bit", nullable: false, defaultValue: true),
                    CreatedAt = table.Column<DateTime>(type: "datetime2(0)", precision: 0, nullable: false, defaultValueSql: "(sysutcdatetime())")
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Permissions", x => x.PermissionId);
                });

            migrationBuilder.CreateTable(
                name: "Roles",
                columns: table => new
                {
                    RoleId = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    RoleName = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: false),
                    Description = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: true),
                    IsActive = table.Column<bool>(type: "bit", nullable: false, defaultValue: true),
                    CreatedAt = table.Column<DateTime>(type: "datetime2(0)", precision: 0, nullable: false, defaultValueSql: "(sysutcdatetime())")
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Roles", x => x.RoleId);
                });

            migrationBuilder.CreateTable(
                name: "SellerKycReviewEvents",
                columns: table => new
                {
                    SellerKycReviewEventId = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    UserId = table.Column<int>(type: "int", nullable: false),
                    Action = table.Column<string>(type: "nvarchar(30)", maxLength: 30, nullable: false),
                    ReviewStatus = table.Column<string>(type: "nvarchar(30)", maxLength: 30, nullable: false),
                    Note = table.Column<string>(type: "nvarchar(1000)", maxLength: 1000, nullable: true),
                    ReviewedAt = table.Column<DateTime>(type: "datetime2(0)", precision: 0, nullable: false, defaultValueSql: "(sysutcdatetime())"),
                    ReviewedByUserId = table.Column<int>(type: "int", nullable: true),
                    ReviewerUserName = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: true),
                    ReviewerFullName = table.Column<string>(type: "nvarchar(150)", maxLength: 150, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_SellerKycReviewEvents", x => x.SellerKycReviewEventId);
                });

            migrationBuilder.CreateTable(
                name: "StoreSettings",
                columns: table => new
                {
                    SettingID = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    StoreName = table.Column<string>(type: "nvarchar(255)", maxLength: 255, nullable: false),
                    StoreAddress = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: false),
                    StoreEmail = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: false),
                    StorePhone = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: false),
                    IsCODEnabled = table.Column<bool>(type: "bit", nullable: false),
                    BankTransferInstructions = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    BankAccountInfo = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    DefaultShippingFee = table.Column<decimal>(type: "decimal(10,2)", nullable: false),
                    FreeShippingThreshold = table.Column<decimal>(type: "decimal(10,2)", nullable: false),
                    IsEmailNewOrderEnabled = table.Column<bool>(type: "bit", nullable: false),
                    IsEmailDeliveredEnabled = table.Column<bool>(type: "bit", nullable: false),
                    IsEmailCancelledEnabled = table.Column<bool>(type: "bit", nullable: false),
                    AdminNotificationEmail = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "datetime2(0)", precision: 0, nullable: false, defaultValueSql: "(sysutcdatetime())"),
                    UpdatedAt = table.Column<DateTime>(type: "datetime2(0)", precision: 0, nullable: false, defaultValueSql: "(sysutcdatetime())")
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_StoreSettings", x => x.SettingID);
                });

            migrationBuilder.CreateTable(
                name: "Users",
                columns: table => new
                {
                    UserId = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    UserName = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: false),
                    FullName = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: false),
                    Email = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: false),
                    Phone = table.Column<string>(type: "nvarchar(10)", maxLength: 10, nullable: false),
                    EmailConfirmed = table.Column<bool>(type: "bit", nullable: false, defaultValue: true),
                    EmailConfirmedAt = table.Column<DateTime>(type: "datetime2(0)", precision: 0, nullable: true),
                    IsActive = table.Column<bool>(type: "bit", nullable: false, defaultValue: true),
                    CreatedAt = table.Column<DateTime>(type: "datetime2(0)", precision: 0, nullable: false, defaultValueSql: "(sysutcdatetime())"),
                    UpdatedAt = table.Column<DateTime>(type: "datetime2(0)", precision: 0, nullable: true),
                    Avatar = table.Column<string>(type: "nvarchar(255)", maxLength: 255, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Users", x => x.UserId);
                });

            migrationBuilder.CreateTable(
                name: "RolePermissions",
                columns: table => new
                {
                    RoleId = table.Column<int>(type: "int", nullable: false),
                    PermissionId = table.Column<int>(type: "int", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "datetime2(0)", precision: 0, nullable: false, defaultValueSql: "(sysutcdatetime())")
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_RolePermissions", x => new { x.RoleId, x.PermissionId });
                    table.ForeignKey(
                        name: "FK_RolePermissions_Permissions",
                        column: x => x.PermissionId,
                        principalTable: "Permissions",
                        principalColumn: "PermissionId",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_RolePermissions_Roles",
                        column: x => x.RoleId,
                        principalTable: "Roles",
                        principalColumn: "RoleId",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "AddressBook",
                columns: table => new
                {
                    AddressId = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    UserId = table.Column<int>(type: "int", nullable: false),
                    RecipientName = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: false),
                    Phone = table.Column<string>(type: "nvarchar(10)", maxLength: 10, nullable: false),
                    AddressDetail = table.Column<string>(type: "nvarchar(255)", maxLength: 255, nullable: false),
                    Province = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: true),
                    District = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: true),
                    Ward = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: true),
                    IsDefault = table.Column<bool>(type: "bit", nullable: false),
                    IsActive = table.Column<bool>(type: "bit", nullable: false, defaultValue: true),
                    CreatedAt = table.Column<DateTime>(type: "datetime2(0)", precision: 0, nullable: false, defaultValueSql: "(sysutcdatetime())"),
                    UpdatedAt = table.Column<DateTime>(type: "datetime2(0)", precision: 0, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_AddressBook", x => x.AddressId);
                    table.ForeignKey(
                        name: "FK_AddressBook_Users",
                        column: x => x.UserId,
                        principalTable: "Users",
                        principalColumn: "UserId");
                });

            migrationBuilder.CreateTable(
                name: "AuthAuditLog",
                columns: table => new
                {
                    AuthAuditLogId = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    UserId = table.Column<int>(type: "int", nullable: true),
                    ClientLane = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: false),
                    RoleName = table.Column<string>(type: "nvarchar(50)", maxLength: 50, nullable: false),
                    Identifier = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: false),
                    UserName = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: true),
                    Email = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: true),
                    EventType = table.Column<string>(type: "nvarchar(50)", maxLength: 50, nullable: false),
                    Success = table.Column<bool>(type: "bit", nullable: false),
                    FailureReason = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: true),
                    FailedAttemptCount = table.Column<int>(type: "int", nullable: true),
                    IpAddress = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: true),
                    ForwardedFor = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: true),
                    CountryCode = table.Column<string>(type: "nvarchar(8)", maxLength: 8, nullable: true),
                    CountryName = table.Column<string>(type: "nvarchar(120)", maxLength: 120, nullable: true),
                    RegionName = table.Column<string>(type: "nvarchar(120)", maxLength: 120, nullable: true),
                    CityName = table.Column<string>(type: "nvarchar(120)", maxLength: 120, nullable: true),
                    UserAgent = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: true),
                    DeviceType = table.Column<string>(type: "nvarchar(30)", maxLength: 30, nullable: true),
                    BrowserFamily = table.Column<string>(type: "nvarchar(50)", maxLength: 50, nullable: true),
                    OperatingSystem = table.Column<string>(type: "nvarchar(50)", maxLength: 50, nullable: true),
                    IsSuspicious = table.Column<bool>(type: "bit", nullable: false, defaultValue: false),
                    SuspicionReasons = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: true),
                    OccurredAt = table.Column<DateTime>(type: "datetime2(0)", precision: 0, nullable: false, defaultValueSql: "(sysutcdatetime())")
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_AuthAuditLog", x => x.AuthAuditLogId);
                    table.ForeignKey(
                        name: "FK_AuthAuditLog_Users",
                        column: x => x.UserId,
                        principalTable: "Users",
                        principalColumn: "UserId",
                        onDelete: ReferentialAction.SetNull);
                });

            migrationBuilder.CreateTable(
                name: "SellerKycProfiles",
                columns: table => new
                {
                    SellerKycProfileId = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    UserId = table.Column<int>(type: "int", nullable: false),
                    LegalFullName = table.Column<string>(type: "nvarchar(150)", maxLength: 150, nullable: false),
                    IdentityNumber = table.Column<string>(type: "nvarchar(50)", maxLength: 50, nullable: false),
                    IdentityIssuedDate = table.Column<DateTime>(type: "datetime2(0)", precision: 0, nullable: false),
                    IdentityIssuedPlace = table.Column<string>(type: "nvarchar(255)", maxLength: 255, nullable: false),
                    TaxCode = table.Column<string>(type: "nvarchar(50)", maxLength: 50, nullable: true),
                    BusinessLicenseNumber = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: true),
                    CitizenIdFrontUrl = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: true),
                    CitizenIdBackUrl = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: true),
                    BusinessLicenseUrl = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: true),
                    AdditionalDocumentUrl = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: true),
                    Notes = table.Column<string>(type: "nvarchar(1000)", maxLength: 1000, nullable: true),
                    ReviewStatus = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    ReviewNote = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    ReviewedAt = table.Column<DateTime>(type: "datetime2", nullable: true),
                    ReviewedByUserId = table.Column<int>(type: "int", nullable: true),
                    CreatedAt = table.Column<DateTime>(type: "datetime2(0)", precision: 0, nullable: false, defaultValueSql: "(sysutcdatetime())"),
                    UpdatedAt = table.Column<DateTime>(type: "datetime2(0)", precision: 0, nullable: false, defaultValueSql: "(sysutcdatetime())")
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_SellerKycProfiles", x => x.SellerKycProfileId);
                    table.ForeignKey(
                        name: "FK_SellerKycProfiles_Users",
                        column: x => x.UserId,
                        principalTable: "Users",
                        principalColumn: "UserId",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "SellerStoreSettings",
                columns: table => new
                {
                    SellerStoreSettingId = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    UserId = table.Column<int>(type: "int", nullable: false),
                    StoreName = table.Column<string>(type: "nvarchar(255)", maxLength: 255, nullable: false),
                    StoreAddress = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: false),
                    StoreEmail = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: false),
                    StorePhone = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: false),
                    IsCODEnabled = table.Column<bool>(type: "bit", nullable: false, defaultValue: true),
                    BankTransferInstructions = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    BankAccountInfo = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    DefaultShippingFee = table.Column<decimal>(type: "decimal(10,2)", nullable: false, defaultValue: 30000m),
                    FreeShippingThreshold = table.Column<decimal>(type: "decimal(10,2)", nullable: false, defaultValue: 500000m),
                    IsEmailNewOrderEnabled = table.Column<bool>(type: "bit", nullable: false, defaultValue: true),
                    IsEmailDeliveredEnabled = table.Column<bool>(type: "bit", nullable: false, defaultValue: true),
                    IsEmailCancelledEnabled = table.Column<bool>(type: "bit", nullable: false, defaultValue: true),
                    AdminNotificationEmail = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: false),
                    GhnPickupName = table.Column<string>(type: "nvarchar(150)", maxLength: 150, nullable: true),
                    GhnPickupPhone = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: true),
                    GhnPickupAddress = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: true),
                    GhnProvinceId = table.Column<int>(type: "int", nullable: true),
                    GhnProvinceName = table.Column<string>(type: "nvarchar(150)", maxLength: 150, nullable: true),
                    GhnDistrictId = table.Column<int>(type: "int", nullable: true),
                    GhnDistrictName = table.Column<string>(type: "nvarchar(150)", maxLength: 150, nullable: true),
                    GhnWardCode = table.Column<string>(type: "nvarchar(50)", maxLength: 50, nullable: true),
                    GhnWardName = table.Column<string>(type: "nvarchar(150)", maxLength: 150, nullable: true),
                    CreatedAt = table.Column<DateTime>(type: "datetime2(0)", precision: 0, nullable: false, defaultValueSql: "(sysutcdatetime())"),
                    UpdatedAt = table.Column<DateTime>(type: "datetime2(0)", precision: 0, nullable: false, defaultValueSql: "(sysutcdatetime())")
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_SellerStoreSettings", x => x.SellerStoreSettingId);
                    table.ForeignKey(
                        name: "FK_SellerStoreSettings_Users",
                        column: x => x.UserId,
                        principalTable: "Users",
                        principalColumn: "UserId");
                });

            migrationBuilder.CreateTable(
                name: "UserAuth",
                columns: table => new
                {
                    UserId = table.Column<int>(type: "int", nullable: false),
                    PasswordHash = table.Column<string>(type: "nvarchar(450)", maxLength: 450, nullable: false),
                    FailedCount = table.Column<int>(type: "int", nullable: false),
                    LockedUntil = table.Column<DateTime>(type: "datetime2(0)", precision: 0, nullable: true),
                    LockoutLevel = table.Column<int>(type: "int", nullable: false, defaultValue: 0),
                    MFASecret = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: true),
                    UpdatedAt = table.Column<DateTime>(type: "datetime2(0)", precision: 0, nullable: false, defaultValueSql: "(sysutcdatetime())")
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_UserAuth", x => x.UserId);
                    table.ForeignKey(
                        name: "FK_UserAuth_Users",
                        column: x => x.UserId,
                        principalTable: "Users",
                        principalColumn: "UserId",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "UserRoles",
                columns: table => new
                {
                    UserId = table.Column<int>(type: "int", nullable: false),
                    RoleId = table.Column<int>(type: "int", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "datetime2(0)", precision: 0, nullable: false, defaultValueSql: "(sysutcdatetime())")
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_UserRoles", x => new { x.UserId, x.RoleId });
                    table.ForeignKey(
                        name: "FK_UserRoles_Roles",
                        column: x => x.RoleId,
                        principalTable: "Roles",
                        principalColumn: "RoleId",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_UserRoles_Users",
                        column: x => x.UserId,
                        principalTable: "Users",
                        principalColumn: "UserId",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "UserSessions",
                columns: table => new
                {
                    SessionId = table.Column<Guid>(type: "uniqueidentifier", nullable: false, defaultValueSql: "(newid())"),
                    UserId = table.Column<int>(type: "int", nullable: false),
                    RefreshToken = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: false),
                    ExpiresAt = table.Column<DateTime>(type: "datetime2(0)", precision: 0, nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "datetime2(0)", precision: 0, nullable: false, defaultValueSql: "(sysutcdatetime())"),
                    RevokedAt = table.Column<DateTime>(type: "datetime2(0)", precision: 0, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_UserSessions", x => x.SessionId);
                    table.ForeignKey(
                        name: "FK_UserSessions_Users",
                        column: x => x.UserId,
                        principalTable: "Users",
                        principalColumn: "UserId",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_AddressBook_UserId_IsActive",
                table: "AddressBook",
                columns: new[] { "UserId", "IsActive", "IsDefault" });

            migrationBuilder.CreateIndex(
                name: "UX_AddressBook_Default_OnePerUser",
                table: "AddressBook",
                column: "UserId",
                unique: true,
                filter: "([IsDefault]=(1) AND [IsActive]=(1))");

            migrationBuilder.CreateIndex(
                name: "IX_AuthAuditLog_ClientLane_OccurredAt",
                table: "AuthAuditLog",
                columns: new[] { "ClientLane", "OccurredAt" },
                descending: new[] { false, true });

            migrationBuilder.CreateIndex(
                name: "IX_AuthAuditLog_IsSuspicious_OccurredAt",
                table: "AuthAuditLog",
                columns: new[] { "IsSuspicious", "OccurredAt" },
                descending: new[] { false, true });

            migrationBuilder.CreateIndex(
                name: "IX_AuthAuditLog_OccurredAt",
                table: "AuthAuditLog",
                column: "OccurredAt",
                descending: new bool[0]);

            migrationBuilder.CreateIndex(
                name: "IX_AuthAuditLog_RoleName_OccurredAt",
                table: "AuthAuditLog",
                columns: new[] { "RoleName", "OccurredAt" },
                descending: new[] { false, true });

            migrationBuilder.CreateIndex(
                name: "IX_AuthAuditLog_Success_OccurredAt",
                table: "AuthAuditLog",
                columns: new[] { "Success", "OccurredAt" },
                descending: new[] { false, true });

            migrationBuilder.CreateIndex(
                name: "IX_AuthAuditLog_UserId_OccurredAt",
                table: "AuthAuditLog",
                columns: new[] { "UserId", "OccurredAt" },
                descending: new[] { false, true });

            migrationBuilder.CreateIndex(
                name: "UQ_Permissions_Code",
                table: "Permissions",
                column: "PermissionCode",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "UQ_Permissions_Name",
                table: "Permissions",
                column: "PermissionName",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_RolePermissions_PermissionId",
                table: "RolePermissions",
                column: "PermissionId");

            migrationBuilder.CreateIndex(
                name: "UQ_Roles_RoleName",
                table: "Roles",
                column: "RoleName",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "UX_SellerKycProfiles_UserId",
                table: "SellerKycProfiles",
                column: "UserId",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_SellerKycReviewEvents_UserId_ReviewedAt",
                table: "SellerKycReviewEvents",
                columns: new[] { "UserId", "ReviewedAt" },
                descending: new[] { false, true });

            migrationBuilder.CreateIndex(
                name: "UX_SellerStoreSettings_UserId",
                table: "SellerStoreSettings",
                column: "UserId",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_UserRoles_RoleId",
                table: "UserRoles",
                column: "RoleId");

            migrationBuilder.CreateIndex(
                name: "UQ_Users_Email",
                table: "Users",
                column: "Email",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "UQ_Users_Phone",
                table: "Users",
                column: "Phone",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "UQ_Users_UserName",
                table: "Users",
                column: "UserName",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_UserSessions_UserId",
                table: "UserSessions",
                column: "UserId");

            migrationBuilder.CreateIndex(
                name: "UQ_UserSessions_RefreshToken",
                table: "UserSessions",
                column: "RefreshToken",
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "AddressBook");

            migrationBuilder.DropTable(
                name: "AuthAuditLog");

            migrationBuilder.DropTable(
                name: "RolePermissions");

            migrationBuilder.DropTable(
                name: "SellerKycProfiles");

            migrationBuilder.DropTable(
                name: "SellerKycReviewEvents");

            migrationBuilder.DropTable(
                name: "SellerStoreSettings");

            migrationBuilder.DropTable(
                name: "StoreSettings");

            migrationBuilder.DropTable(
                name: "UserAuth");

            migrationBuilder.DropTable(
                name: "UserRoles");

            migrationBuilder.DropTable(
                name: "UserSessions");

            migrationBuilder.DropTable(
                name: "Permissions");

            migrationBuilder.DropTable(
                name: "Roles");

            migrationBuilder.DropTable(
                name: "Users");
        }
    }
}
