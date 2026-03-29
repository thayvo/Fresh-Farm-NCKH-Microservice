using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace FreshFarm.Ordering.Api.Migrations
{
    /// <inheritdoc />
    public partial class OrderingSchemaBaseline20260324 : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "AdminActionLog",
                columns: table => new
                {
                    AdminActionLogId = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    Area = table.Column<string>(type: "nvarchar(50)", maxLength: 50, nullable: false),
                    ActionName = table.Column<string>(type: "nvarchar(80)", maxLength: 80, nullable: false),
                    TargetType = table.Column<string>(type: "nvarchar(80)", maxLength: 80, nullable: false),
                    TargetId = table.Column<int>(type: "int", nullable: true),
                    Summary = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: false),
                    ActorUserId = table.Column<int>(type: "int", nullable: true),
                    MetadataJson = table.Column<string>(type: "nvarchar(4000)", maxLength: 4000, nullable: true),
                    CreatedAt = table.Column<DateTime>(type: "datetime", nullable: false, defaultValueSql: "(getutcdate())")
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_AdminActionLog", x => x.AdminActionLogId);
                });

            migrationBuilder.CreateTable(
                name: "CancelReasons",
                columns: table => new
                {
                    CancelReasonID = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    ReasonCode = table.Column<string>(type: "nvarchar(50)", maxLength: 50, nullable: false),
                    ReasonText = table.Column<string>(type: "nvarchar(255)", maxLength: 255, nullable: false),
                    IsActive = table.Column<bool>(type: "bit", nullable: false, defaultValue: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK__CancelRe__9D7E8F962F0E33D0", x => x.CancelReasonID);
                });

            migrationBuilder.CreateTable(
                name: "Cart",
                columns: table => new
                {
                    CartID = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    UserID = table.Column<int>(type: "int", nullable: false),
                    CreatedDate = table.Column<DateTime>(type: "datetime", nullable: false, defaultValueSql: "(getdate())")
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK__Cart__51BCD797FAF813EA", x => x.CartID);
                });

            migrationBuilder.CreateTable(
                name: "CommunicationTemplate",
                columns: table => new
                {
                    CommunicationTemplateId = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    TemplateName = table.Column<string>(type: "nvarchar(120)", maxLength: 120, nullable: false),
                    EventType = table.Column<string>(type: "nvarchar(80)", maxLength: 80, nullable: false),
                    Channel = table.Column<string>(type: "nvarchar(30)", maxLength: 30, nullable: false),
                    Locale = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: false, defaultValue: "vi-VN"),
                    Subject = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: true),
                    Body = table.Column<string>(type: "nvarchar(4000)", maxLength: 4000, nullable: false),
                    IsActive = table.Column<bool>(type: "bit", nullable: false, defaultValue: true),
                    Version = table.Column<int>(type: "int", nullable: false, defaultValue: 1),
                    LastEditedByUserId = table.Column<int>(type: "int", nullable: true),
                    CreatedAt = table.Column<DateTime>(type: "datetime", nullable: false, defaultValueSql: "(getutcdate())"),
                    UpdatedAt = table.Column<DateTime>(type: "datetime", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_CommunicationTemplate", x => x.CommunicationTemplateId);
                });

            migrationBuilder.CreateTable(
                name: "ContactMessages",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    SenderName = table.Column<string>(type: "nvarchar(150)", maxLength: 150, nullable: false),
                    SenderEmail = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: false),
                    SenderPhone = table.Column<string>(type: "nvarchar(30)", maxLength: 30, nullable: true),
                    Subject = table.Column<string>(type: "nvarchar(300)", maxLength: 300, nullable: false),
                    Message = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "datetime", nullable: false, defaultValueSql: "(getutcdate())"),
                    Status = table.Column<string>(type: "nvarchar(30)", maxLength: 30, nullable: false, defaultValue: "new"),
                    AdminNote = table.Column<string>(type: "nvarchar(1000)", maxLength: 1000, nullable: true),
                    ProcessedAt = table.Column<DateTime>(type: "datetime", nullable: true),
                    IsDeleted = table.Column<bool>(type: "bit", nullable: false),
                    DeletedAt = table.Column<DateTime>(type: "datetime", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ContactMessages", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "Coupons",
                columns: table => new
                {
                    CouponID = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    Code = table.Column<string>(type: "nvarchar(50)", maxLength: 50, nullable: false),
                    DiscountValue = table.Column<decimal>(type: "decimal(10,2)", nullable: false),
                    ExpiryDate = table.Column<DateOnly>(type: "date", nullable: false),
                    MinOrderValue = table.Column<decimal>(type: "decimal(10,2)", nullable: true),
                    DiscountType = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: false, defaultValue: "fixed"),
                    IsActive = table.Column<bool>(type: "bit", nullable: false, defaultValue: true),
                    UsageLimit = table.Column<int>(type: "int", nullable: true),
                    UsedCount = table.Column<int>(type: "int", nullable: false),
                    Description = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: true),
                    CreatedBy = table.Column<int>(type: "int", nullable: true),
                    CreatedDate = table.Column<DateTime>(type: "datetime", nullable: false, defaultValueSql: "(getdate())"),
                    UpdatedDate = table.Column<DateTime>(type: "datetime", nullable: true),
                    MaxDiscountAmount = table.Column<decimal>(type: "decimal(10,2)", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK__Coupons__384AF1DA1AEB61D4", x => x.CouponID);
                });

            migrationBuilder.CreateTable(
                name: "CustomerNotifications",
                columns: table => new
                {
                    NotificationID = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    UserID = table.Column<int>(type: "int", nullable: false),
                    OrderID = table.Column<int>(type: "int", nullable: false),
                    NotificationType = table.Column<string>(type: "varchar(50)", unicode: false, maxLength: 50, nullable: false),
                    Title = table.Column<string>(type: "nvarchar(255)", maxLength: 255, nullable: false),
                    Message = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    IsRead = table.Column<bool>(type: "bit", nullable: true, defaultValue: false),
                    IsPushNotification = table.Column<bool>(type: "bit", nullable: true, defaultValue: false),
                    CreatedAt = table.Column<DateTime>(type: "datetime", nullable: true, defaultValueSql: "(getdate())"),
                    ReadAt = table.Column<DateTime>(type: "datetime", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK__Customer__20CF2E3289B0C6AB", x => x.NotificationID);
                });

            migrationBuilder.CreateTable(
                name: "LoyaltyConfig",
                columns: table => new
                {
                    ConfigID = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    EarnRate = table.Column<decimal>(type: "decimal(9,6)", nullable: false, defaultValue: 0.01m),
                    IncludeShippingFee = table.Column<bool>(type: "bit", nullable: false),
                    PaidKeywords = table.Column<string>(type: "nvarchar(400)", maxLength: 400, nullable: true),
                    CreatedAt = table.Column<DateTime>(type: "datetime", nullable: false, defaultValueSql: "(getdate())"),
                    UpdatedAt = table.Column<DateTime>(type: "datetime", nullable: false, defaultValueSql: "(getdate())")
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK__LoyaltyC__C3BC333C787936AA", x => x.ConfigID);
                });

            migrationBuilder.CreateTable(
                name: "LoyaltyPointHistory",
                columns: table => new
                {
                    PointHistoryID = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    UserID = table.Column<int>(type: "int", nullable: false),
                    OrderID = table.Column<int>(type: "int", nullable: true),
                    Points = table.Column<int>(type: "int", nullable: false),
                    Direction = table.Column<string>(type: "nvarchar(10)", maxLength: 10, nullable: false),
                    Reason = table.Column<string>(type: "nvarchar(255)", maxLength: 255, nullable: true),
                    CreatedAt = table.Column<DateTime>(type: "datetime", nullable: false, defaultValueSql: "(getdate())")
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK__LoyaltyP__DBE13EF7015CFA30", x => x.PointHistoryID);
                });

            migrationBuilder.CreateTable(
                name: "NotificationPreference",
                columns: table => new
                {
                    NotificationPreferenceId = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    UserId = table.Column<int>(type: "int", nullable: false),
                    EventType = table.Column<string>(type: "nvarchar(80)", maxLength: 80, nullable: false),
                    Channel = table.Column<string>(type: "nvarchar(30)", maxLength: 30, nullable: false),
                    IsOptedIn = table.Column<bool>(type: "bit", nullable: false, defaultValue: true),
                    Source = table.Column<string>(type: "nvarchar(40)", maxLength: 40, nullable: false, defaultValue: "admin_console"),
                    CreatedAt = table.Column<DateTime>(type: "datetime", nullable: false, defaultValueSql: "(getutcdate())"),
                    UpdatedAt = table.Column<DateTime>(type: "datetime", nullable: false, defaultValueSql: "(getutcdate())")
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_NotificationPreference", x => x.NotificationPreferenceId);
                });

            migrationBuilder.CreateTable(
                name: "OrderDetailInvalidProductArchive",
                columns: table => new
                {
                    ArchiveID = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    OrderDetailID = table.Column<int>(type: "int", nullable: false),
                    OrderID = table.Column<int>(type: "int", nullable: false),
                    ProductID = table.Column<int>(type: "int", nullable: false),
                    Quantity = table.Column<int>(type: "int", nullable: false),
                    UnitPrice = table.Column<decimal>(type: "decimal(18,2)", nullable: false),
                    UnitSymbol = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: true),
                    Reason = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: false),
                    ArchivedAt = table.Column<DateTime>(type: "datetime2(0)", precision: 0, nullable: false, defaultValueSql: "(sysutcdatetime())")
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK__OrderDet__33A73E7717E354DA", x => x.ArchiveID);
                });

            migrationBuilder.CreateTable(
                name: "OrderStatusHistory",
                columns: table => new
                {
                    HistoryID = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    EntityType = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: false),
                    EntityID = table.Column<int>(type: "int", nullable: false),
                    OldStatus = table.Column<string>(type: "nvarchar(30)", maxLength: 30, nullable: true),
                    NewStatus = table.Column<string>(type: "nvarchar(30)", maxLength: 30, nullable: false),
                    Note = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: true),
                    ActorType = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: false),
                    ActorID = table.Column<int>(type: "int", nullable: true),
                    CreatedAt = table.Column<DateTime>(type: "datetime", nullable: false, defaultValueSql: "(getutcdate())")
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK__OrderSta__4D7B4ADD928419E1", x => x.HistoryID);
                });

            migrationBuilder.CreateTable(
                name: "Review",
                columns: table => new
                {
                    ReviewID = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    ProductID = table.Column<int>(type: "int", nullable: false),
                    UserID = table.Column<int>(type: "int", nullable: false),
                    Rating = table.Column<int>(type: "int", nullable: false),
                    Comment = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    CreatedAt = table.Column<DateTime>(type: "datetime", nullable: false, defaultValueSql: "(getutcdate())"),
                    ReplyTo = table.Column<int>(type: "int", nullable: true),
                    IsApproved = table.Column<bool>(type: "bit", nullable: false),
                    IsEdited = table.Column<bool>(type: "bit", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "datetime2", nullable: true),
                    IsDeleted = table.Column<bool>(type: "bit", nullable: false),
                    DeletedAt = table.Column<DateTime>(type: "datetime2", nullable: true),
                    DeletedByAdminId = table.Column<int>(type: "int", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Review", x => x.ReviewID);
                    table.ForeignKey(
                        name: "FK_Review_ReplyTo",
                        column: x => x.ReplyTo,
                        principalTable: "Review",
                        principalColumn: "ReviewID");
                });

            migrationBuilder.CreateTable(
                name: "RiskCase",
                columns: table => new
                {
                    RiskCaseId = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    ReferenceKey = table.Column<string>(type: "nvarchar(180)", maxLength: 180, nullable: false),
                    CaseType = table.Column<string>(type: "nvarchar(50)", maxLength: 50, nullable: false),
                    Status = table.Column<string>(type: "nvarchar(40)", maxLength: 40, nullable: false, defaultValue: "open"),
                    Severity = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: false, defaultValue: "medium"),
                    SellerId = table.Column<int>(type: "int", nullable: true),
                    BuyerId = table.Column<int>(type: "int", nullable: true),
                    OrderId = table.Column<int>(type: "int", nullable: true),
                    CampaignId = table.Column<int>(type: "int", nullable: true),
                    VoucherCouponId = table.Column<int>(type: "int", nullable: true),
                    Title = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: false),
                    Summary = table.Column<string>(type: "nvarchar(2000)", maxLength: 2000, nullable: true),
                    SignalCount = table.Column<int>(type: "int", nullable: false),
                    IsEscalated = table.Column<bool>(type: "bit", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "datetime", nullable: false, defaultValueSql: "(getutcdate())"),
                    UpdatedAt = table.Column<DateTime>(type: "datetime", nullable: true),
                    LastSignalAt = table.Column<DateTime>(type: "datetime", nullable: true),
                    ReviewedAt = table.Column<DateTime>(type: "datetime", nullable: true),
                    ReviewedBy = table.Column<int>(type: "int", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_RiskCase", x => x.RiskCaseId);
                });

            migrationBuilder.CreateTable(
                name: "SellerAdsWallet",
                columns: table => new
                {
                    WalletId = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    SellerId = table.Column<int>(type: "int", nullable: false),
                    Balance = table.Column<decimal>(type: "decimal(18,2)", nullable: false),
                    ReservedBalance = table.Column<decimal>(type: "decimal(18,2)", nullable: false),
                    TotalTopup = table.Column<decimal>(type: "decimal(18,2)", nullable: false),
                    TotalSpend = table.Column<decimal>(type: "decimal(18,2)", nullable: false),
                    Status = table.Column<string>(type: "nvarchar(40)", maxLength: 40, nullable: false, defaultValue: "active"),
                    CreatedAt = table.Column<DateTime>(type: "datetime", nullable: false, defaultValueSql: "(getutcdate())"),
                    UpdatedAt = table.Column<DateTime>(type: "datetime", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_SellerAdsWallet", x => x.WalletId);
                });

            migrationBuilder.CreateTable(
                name: "StatusType",
                columns: table => new
                {
                    StatusTypeID = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    StatusTypeName = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: false),
                    Description = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: true),
                    IsActive = table.Column<bool>(type: "bit", nullable: false, defaultValue: true),
                    CreatedDate = table.Column<DateTime>(type: "datetime", nullable: false, defaultValueSql: "(getdate())")
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK__StatusTy__A84F3D9327880826", x => x.StatusTypeID);
                });

            migrationBuilder.CreateTable(
                name: "SupportConversations",
                columns: table => new
                {
                    ConversationId = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    UserId = table.Column<int>(type: "int", nullable: true),
                    GuestId = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: true),
                    AdminId = table.Column<int>(type: "int", nullable: true),
                    StartedAt = table.Column<DateTime>(type: "datetime", nullable: false, defaultValueSql: "(getutcdate())"),
                    ClosedAt = table.Column<DateTime>(type: "datetime", nullable: true),
                    Status = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: false, defaultValue: "Open")
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_SupportConversations", x => x.ConversationId);
                });

            migrationBuilder.CreateTable(
                name: "ModerationAudit",
                columns: table => new
                {
                    ModerationAuditId = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    AdminActionLogId = table.Column<int>(type: "int", nullable: true),
                    SubjectType = table.Column<string>(type: "nvarchar(80)", maxLength: 80, nullable: false),
                    SubjectId = table.Column<int>(type: "int", nullable: true),
                    Decision = table.Column<string>(type: "nvarchar(80)", maxLength: 80, nullable: false),
                    Notes = table.Column<string>(type: "nvarchar(2000)", maxLength: 2000, nullable: true),
                    ActorUserId = table.Column<int>(type: "int", nullable: true),
                    CreatedAt = table.Column<DateTime>(type: "datetime", nullable: false, defaultValueSql: "(getutcdate())")
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ModerationAudit", x => x.ModerationAuditId);
                    table.ForeignKey(
                        name: "FK_ModerationAudit_AdminActionLog",
                        column: x => x.AdminActionLogId,
                        principalTable: "AdminActionLog",
                        principalColumn: "AdminActionLogId");
                });

            migrationBuilder.CreateTable(
                name: "SettlementAudit",
                columns: table => new
                {
                    SettlementAuditId = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    AdminActionLogId = table.Column<int>(type: "int", nullable: true),
                    AuditType = table.Column<string>(type: "nvarchar(80)", maxLength: 80, nullable: false),
                    ReferenceType = table.Column<string>(type: "nvarchar(80)", maxLength: 80, nullable: false),
                    ReferenceId = table.Column<int>(type: "int", nullable: true),
                    SellerId = table.Column<int>(type: "int", nullable: true),
                    Amount = table.Column<decimal>(type: "decimal(18,2)", nullable: true),
                    Currency = table.Column<string>(type: "nvarchar(10)", maxLength: 10, nullable: false, defaultValue: "VND"),
                    Notes = table.Column<string>(type: "nvarchar(2000)", maxLength: 2000, nullable: true),
                    ActorUserId = table.Column<int>(type: "int", nullable: true),
                    CreatedAt = table.Column<DateTime>(type: "datetime", nullable: false, defaultValueSql: "(getutcdate())")
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_SettlementAudit", x => x.SettlementAuditId);
                    table.ForeignKey(
                        name: "FK_SettlementAudit_AdminActionLog",
                        column: x => x.AdminActionLogId,
                        principalTable: "AdminActionLog",
                        principalColumn: "AdminActionLogId");
                });

            migrationBuilder.CreateTable(
                name: "CartItem",
                columns: table => new
                {
                    CartItemID = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    CartID = table.Column<int>(type: "int", nullable: false),
                    ProductID = table.Column<int>(type: "int", nullable: false),
                    SellerID = table.Column<int>(type: "int", nullable: false, defaultValue: 0),
                    Quantity = table.Column<int>(type: "int", nullable: false, defaultValue: 1),
                    UnitPrice = table.Column<decimal>(type: "decimal(18,2)", nullable: false),
                    SnapshotSellerName = table.Column<string>(type: "nvarchar(255)", maxLength: 255, nullable: true),
                    SnapshotProductName = table.Column<string>(type: "nvarchar(255)", maxLength: 255, nullable: true),
                    SnapshotImageFileName = table.Column<string>(type: "nvarchar(255)", maxLength: 255, nullable: true),
                    SnapshotUnitSymbol = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK__CartItem__488B0B2A7E7BEB70", x => x.CartItemID);
                    table.ForeignKey(
                        name: "FK__CartItem__CartID__2EDAF651",
                        column: x => x.CartID,
                        principalTable: "Cart",
                        principalColumn: "CartID");
                });

            migrationBuilder.CreateTable(
                name: "NotificationPolicyRule",
                columns: table => new
                {
                    NotificationPolicyRuleId = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    EventType = table.Column<string>(type: "nvarchar(80)", maxLength: 80, nullable: false),
                    Channel = table.Column<string>(type: "nvarchar(30)", maxLength: 30, nullable: false),
                    AudienceType = table.Column<string>(type: "nvarchar(40)", maxLength: 40, nullable: false),
                    CommunicationTemplateId = table.Column<int>(type: "int", nullable: true),
                    IsEnabled = table.Column<bool>(type: "bit", nullable: false, defaultValue: true),
                    CooldownMinutes = table.Column<int>(type: "int", nullable: false),
                    DeliveryMode = table.Column<string>(type: "nvarchar(30)", maxLength: 30, nullable: false, defaultValue: "immediate"),
                    Priority = table.Column<int>(type: "int", nullable: false, defaultValue: 50),
                    Notes = table.Column<string>(type: "nvarchar(1000)", maxLength: 1000, nullable: true),
                    CreatedAt = table.Column<DateTime>(type: "datetime", nullable: false, defaultValueSql: "(getutcdate())"),
                    UpdatedAt = table.Column<DateTime>(type: "datetime", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_NotificationPolicyRule", x => x.NotificationPolicyRuleId);
                    table.ForeignKey(
                        name: "FK_NotificationPolicyRule_CommunicationTemplate",
                        column: x => x.CommunicationTemplateId,
                        principalTable: "CommunicationTemplate",
                        principalColumn: "CommunicationTemplateId",
                        onDelete: ReferentialAction.SetNull);
                });

            migrationBuilder.CreateTable(
                name: "Campaign",
                columns: table => new
                {
                    CampaignId = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    Name = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: false),
                    CampaignType = table.Column<string>(type: "nvarchar(40)", maxLength: 40, nullable: false),
                    Description = table.Column<string>(type: "nvarchar(2000)", maxLength: 2000, nullable: true),
                    RegistrationStartAt = table.Column<DateTime>(type: "datetime", nullable: false),
                    RegistrationEndAt = table.Column<DateTime>(type: "datetime", nullable: false),
                    StartAt = table.Column<DateTime>(type: "datetime", nullable: false),
                    EndAt = table.Column<DateTime>(type: "datetime", nullable: false),
                    Status = table.Column<string>(type: "nvarchar(40)", maxLength: 40, nullable: false, defaultValue: "draft"),
                    BudgetAmount = table.Column<decimal>(type: "decimal(18,2)", nullable: true),
                    IsFeatured = table.Column<bool>(type: "bit", nullable: false),
                    VoucherCouponId = table.Column<int>(type: "int", nullable: true),
                    CreatedBy = table.Column<int>(type: "int", nullable: true),
                    ApprovedBy = table.Column<int>(type: "int", nullable: true),
                    CreatedAt = table.Column<DateTime>(type: "datetime", nullable: false, defaultValueSql: "(getutcdate())"),
                    UpdatedAt = table.Column<DateTime>(type: "datetime", nullable: true),
                    ApprovedAt = table.Column<DateTime>(type: "datetime", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Campaign", x => x.CampaignId);
                    table.ForeignKey(
                        name: "FK_Campaign_Coupon",
                        column: x => x.VoucherCouponId,
                        principalTable: "Coupons",
                        principalColumn: "CouponID");
                });

            migrationBuilder.CreateTable(
                name: "CouponDistribution",
                columns: table => new
                {
                    DistributionID = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    CouponID = table.Column<int>(type: "int", nullable: false),
                    UserID = table.Column<int>(type: "int", nullable: false),
                    SentDate = table.Column<DateTime>(type: "datetime", nullable: false, defaultValueSql: "(getdate())"),
                    SentBy = table.Column<int>(type: "int", nullable: true),
                    IsUsed = table.Column<bool>(type: "bit", nullable: false),
                    UsedDate = table.Column<DateTime>(type: "datetime", nullable: true),
                    Channel = table.Column<string>(type: "nvarchar(50)", maxLength: 50, nullable: true),
                    Status = table.Column<string>(type: "nvarchar(50)", maxLength: 50, nullable: false, defaultValue: "Sent")
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK__CouponDi__3226272F69F0E4C1", x => x.DistributionID);
                    table.ForeignKey(
                        name: "FK_CD_Coupon",
                        column: x => x.CouponID,
                        principalTable: "Coupons",
                        principalColumn: "CouponID");
                });

            migrationBuilder.CreateTable(
                name: "ReviewReport",
                columns: table => new
                {
                    ReportID = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    ReviewID = table.Column<int>(type: "int", nullable: false),
                    ReporterUserID = table.Column<int>(type: "int", nullable: false),
                    Reason = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: true),
                    CreatedAt = table.Column<DateTime>(type: "datetime", nullable: false, defaultValueSql: "(getutcdate())"),
                    Note = table.Column<string>(type: "nvarchar(1000)", maxLength: 1000, nullable: true),
                    Status = table.Column<byte>(type: "tinyint", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ReviewReport", x => x.ReportID);
                    table.ForeignKey(
                        name: "FK_ReviewReport_Review",
                        column: x => x.ReviewID,
                        principalTable: "Review",
                        principalColumn: "ReviewID");
                });

            migrationBuilder.CreateTable(
                name: "RiskDecision",
                columns: table => new
                {
                    RiskDecisionId = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    RiskCaseId = table.Column<int>(type: "int", nullable: false),
                    DecisionType = table.Column<string>(type: "nvarchar(50)", maxLength: 50, nullable: false),
                    Notes = table.Column<string>(type: "nvarchar(2000)", maxLength: 2000, nullable: true),
                    CreatedBy = table.Column<int>(type: "int", nullable: true),
                    CreatedAt = table.Column<DateTime>(type: "datetime", nullable: false, defaultValueSql: "(getutcdate())")
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_RiskDecision", x => x.RiskDecisionId);
                    table.ForeignKey(
                        name: "FK_RiskDecision_RiskCase",
                        column: x => x.RiskCaseId,
                        principalTable: "RiskCase",
                        principalColumn: "RiskCaseId",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "RiskSignal",
                columns: table => new
                {
                    RiskSignalId = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    RiskCaseId = table.Column<int>(type: "int", nullable: false),
                    ReferenceKey = table.Column<string>(type: "nvarchar(180)", maxLength: 180, nullable: false),
                    SignalType = table.Column<string>(type: "nvarchar(80)", maxLength: 80, nullable: false),
                    SignalCode = table.Column<string>(type: "nvarchar(120)", maxLength: 120, nullable: false),
                    Severity = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: false),
                    Source = table.Column<string>(type: "nvarchar(50)", maxLength: 50, nullable: false, defaultValue: "heuristic_engine"),
                    Score = table.Column<decimal>(type: "decimal(5,2)", nullable: false),
                    SellerId = table.Column<int>(type: "int", nullable: true),
                    BuyerId = table.Column<int>(type: "int", nullable: true),
                    OrderId = table.Column<int>(type: "int", nullable: true),
                    CampaignId = table.Column<int>(type: "int", nullable: true),
                    VoucherCouponId = table.Column<int>(type: "int", nullable: true),
                    MetadataJson = table.Column<string>(type: "nvarchar(4000)", maxLength: 4000, nullable: true),
                    TriggeredAt = table.Column<DateTime>(type: "datetime", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "datetime", nullable: false, defaultValueSql: "(getutcdate())")
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_RiskSignal", x => x.RiskSignalId);
                    table.ForeignKey(
                        name: "FK_RiskSignal_RiskCase",
                        column: x => x.RiskCaseId,
                        principalTable: "RiskCase",
                        principalColumn: "RiskCaseId",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "VoucherAbuseCase",
                columns: table => new
                {
                    VoucherAbuseCaseId = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    ReferenceKey = table.Column<string>(type: "nvarchar(180)", maxLength: 180, nullable: false),
                    RiskCaseId = table.Column<int>(type: "int", nullable: false),
                    CouponId = table.Column<int>(type: "int", nullable: false),
                    BuyerId = table.Column<int>(type: "int", nullable: true),
                    SellerId = table.Column<int>(type: "int", nullable: true),
                    CampaignId = table.Column<int>(type: "int", nullable: true),
                    OrderId = table.Column<int>(type: "int", nullable: true),
                    AbuseType = table.Column<string>(type: "nvarchar(80)", maxLength: 80, nullable: false),
                    SuspectedBenefitAmount = table.Column<decimal>(type: "decimal(18,2)", nullable: true),
                    Status = table.Column<string>(type: "nvarchar(40)", maxLength: 40, nullable: false, defaultValue: "open"),
                    CreatedAt = table.Column<DateTime>(type: "datetime", nullable: false, defaultValueSql: "(getutcdate())"),
                    ReviewedAt = table.Column<DateTime>(type: "datetime", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_VoucherAbuseCase", x => x.VoucherAbuseCaseId);
                    table.ForeignKey(
                        name: "FK_VoucherAbuseCase_RiskCase",
                        column: x => x.RiskCaseId,
                        principalTable: "RiskCase",
                        principalColumn: "RiskCaseId",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "AdsTopup",
                columns: table => new
                {
                    TopupId = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    WalletId = table.Column<int>(type: "int", nullable: false),
                    SellerId = table.Column<int>(type: "int", nullable: false),
                    Amount = table.Column<decimal>(type: "decimal(18,2)", nullable: false),
                    Status = table.Column<string>(type: "nvarchar(40)", maxLength: 40, nullable: false, defaultValue: "confirmed"),
                    PaymentMethod = table.Column<string>(type: "nvarchar(50)", maxLength: 50, nullable: true),
                    ReferenceCode = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: true),
                    Notes = table.Column<string>(type: "nvarchar(1000)", maxLength: 1000, nullable: true),
                    CreatedBy = table.Column<int>(type: "int", nullable: true),
                    CreatedAt = table.Column<DateTime>(type: "datetime", nullable: false, defaultValueSql: "(getutcdate())"),
                    ConfirmedAt = table.Column<DateTime>(type: "datetime", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_AdsTopup", x => x.TopupId);
                    table.ForeignKey(
                        name: "FK_AdsTopup_Wallet",
                        column: x => x.WalletId,
                        principalTable: "SellerAdsWallet",
                        principalColumn: "WalletId",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "Status",
                columns: table => new
                {
                    StatusID = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    StatusName = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: false),
                    StatusTypeID = table.Column<int>(type: "int", nullable: false),
                    ColorCode = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: false, defaultValue: "#28a745"),
                    Note = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: true),
                    DisplayOrder = table.Column<int>(type: "int", nullable: false),
                    IsActive = table.Column<bool>(type: "bit", nullable: false, defaultValue: true),
                    CreatedDate = table.Column<DateTime>(type: "datetime", nullable: false, defaultValueSql: "(getdate())")
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK__Status__C8EE2043E8F9AEFE", x => x.StatusID);
                    table.ForeignKey(
                        name: "FK_Status_StatusType",
                        column: x => x.StatusTypeID,
                        principalTable: "StatusType",
                        principalColumn: "StatusTypeID");
                });

            migrationBuilder.CreateTable(
                name: "SupportMessages",
                columns: table => new
                {
                    MessageId = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    ConversationId = table.Column<int>(type: "int", nullable: false),
                    SenderType = table.Column<byte>(type: "tinyint", nullable: false),
                    SenderUserId = table.Column<int>(type: "int", nullable: true),
                    SenderAdminId = table.Column<int>(type: "int", nullable: true),
                    Content = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "datetime", nullable: false, defaultValueSql: "(getutcdate())"),
                    IsRead = table.Column<bool>(type: "bit", nullable: false),
                    ReplyToMessageId = table.Column<int>(type: "int", nullable: true),
                    IsDeleted = table.Column<bool>(type: "bit", nullable: false),
                    DeletedAt = table.Column<DateTime>(type: "datetime", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_SupportMessages", x => x.MessageId);
                    table.ForeignKey(
                        name: "FK_SupportMessages_ReplyTo",
                        column: x => x.ReplyToMessageId,
                        principalTable: "SupportMessages",
                        principalColumn: "MessageId");
                    table.ForeignKey(
                        name: "FK_SupportMsg_Conv",
                        column: x => x.ConversationId,
                        principalTable: "SupportConversations",
                        principalColumn: "ConversationId");
                });

            migrationBuilder.CreateTable(
                name: "AdsCampaign",
                columns: table => new
                {
                    AdsCampaignId = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    WalletId = table.Column<int>(type: "int", nullable: false),
                    SellerId = table.Column<int>(type: "int", nullable: false),
                    CampaignId = table.Column<int>(type: "int", nullable: true),
                    Name = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: false),
                    Channel = table.Column<string>(type: "nvarchar(50)", maxLength: 50, nullable: false, defaultValue: "onsite"),
                    Status = table.Column<string>(type: "nvarchar(40)", maxLength: 40, nullable: false, defaultValue: "draft"),
                    DailyBudget = table.Column<decimal>(type: "decimal(18,2)", nullable: false),
                    TotalBudget = table.Column<decimal>(type: "decimal(18,2)", nullable: false),
                    SpendToDate = table.Column<decimal>(type: "decimal(18,2)", nullable: false),
                    StartAt = table.Column<DateTime>(type: "datetime", nullable: false),
                    EndAt = table.Column<DateTime>(type: "datetime", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "datetime", nullable: false, defaultValueSql: "(getutcdate())"),
                    UpdatedAt = table.Column<DateTime>(type: "datetime", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_AdsCampaign", x => x.AdsCampaignId);
                    table.ForeignKey(
                        name: "FK_AdsCampaign_Campaign",
                        column: x => x.CampaignId,
                        principalTable: "Campaign",
                        principalColumn: "CampaignId");
                    table.ForeignKey(
                        name: "FK_AdsCampaign_Wallet",
                        column: x => x.WalletId,
                        principalTable: "SellerAdsWallet",
                        principalColumn: "WalletId",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "CampaignSellerParticipation",
                columns: table => new
                {
                    ParticipationId = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    CampaignId = table.Column<int>(type: "int", nullable: false),
                    SellerId = table.Column<int>(type: "int", nullable: false),
                    Status = table.Column<string>(type: "nvarchar(40)", maxLength: 40, nullable: false, defaultValue: "pending"),
                    Notes = table.Column<string>(type: "nvarchar(2000)", maxLength: 2000, nullable: true),
                    DiscountPercent = table.Column<decimal>(type: "decimal(5,2)", nullable: true),
                    RequestedSlots = table.Column<int>(type: "int", nullable: true),
                    ApprovedSlots = table.Column<int>(type: "int", nullable: true),
                    RequestedAt = table.Column<DateTime>(type: "datetime", nullable: false, defaultValueSql: "(getutcdate())"),
                    ReviewedAt = table.Column<DateTime>(type: "datetime", nullable: true),
                    ReviewedBy = table.Column<int>(type: "int", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_CampaignSellerParticipation", x => x.ParticipationId);
                    table.ForeignKey(
                        name: "FK_CampaignSellerParticipation_Campaign",
                        column: x => x.CampaignId,
                        principalTable: "Campaign",
                        principalColumn: "CampaignId",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "Orders",
                columns: table => new
                {
                    OrderID = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    UserID = table.Column<int>(type: "int", nullable: false),
                    OrderDate = table.Column<DateTime>(type: "datetime", nullable: false, defaultValueSql: "(getdate())"),
                    ShippingFee = table.Column<decimal>(type: "decimal(10,2)", nullable: false, defaultValue: 15000m),
                    CouponID = table.Column<int>(type: "int", nullable: true),
                    TotalAmount = table.Column<decimal>(type: "decimal(18,2)", nullable: false),
                    OrderNote = table.Column<string>(type: "nvarchar(255)", maxLength: 255, nullable: true),
                    Status = table.Column<string>(type: "nvarchar(50)", maxLength: 50, nullable: false, defaultValue: "Pending"),
                    PaymentStatus = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: true),
                    PaidAt = table.Column<DateTime>(type: "datetime", nullable: true),
                    StatusID = table.Column<int>(type: "int", nullable: true),
                    BuyerFullName = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: true),
                    BuyerPhone = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: true),
                    BuyerEmail = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: true),
                    PointsEarned = table.Column<int>(type: "int", nullable: false),
                    PointsRedeemed = table.Column<int>(type: "int", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK__Orders__C3905BAF4CCB8F90", x => x.OrderID);
                    table.ForeignKey(
                        name: "FK_Orders_Status",
                        column: x => x.StatusID,
                        principalTable: "Status",
                        principalColumn: "StatusID");
                    table.ForeignKey(
                        name: "FK__Orders__CouponID__3A4CA8FD",
                        column: x => x.CouponID,
                        principalTable: "Coupons",
                        principalColumn: "CouponID");
                });

            migrationBuilder.CreateTable(
                name: "SupportMessageReactions",
                columns: table => new
                {
                    ReactionId = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    MessageId = table.Column<int>(type: "int", nullable: false),
                    UserId = table.Column<int>(type: "int", nullable: true),
                    AdminId = table.Column<int>(type: "int", nullable: true),
                    ReactionType = table.Column<string>(type: "nvarchar(50)", maxLength: 50, nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "datetime", nullable: false, defaultValueSql: "(getutcdate())")
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_SupportMessageReactions", x => x.ReactionId);
                    table.ForeignKey(
                        name: "FK_SupportReactions_Message",
                        column: x => x.MessageId,
                        principalTable: "SupportMessages",
                        principalColumn: "MessageId");
                });

            migrationBuilder.CreateTable(
                name: "AdsSpendLedger",
                columns: table => new
                {
                    SpendId = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    WalletId = table.Column<int>(type: "int", nullable: false),
                    SellerId = table.Column<int>(type: "int", nullable: false),
                    AdsCampaignId = table.Column<int>(type: "int", nullable: true),
                    Amount = table.Column<decimal>(type: "decimal(18,2)", nullable: false),
                    SpendType = table.Column<string>(type: "nvarchar(50)", maxLength: 50, nullable: false, defaultValue: "manual_adjustment"),
                    Status = table.Column<string>(type: "nvarchar(40)", maxLength: 40, nullable: false, defaultValue: "posted"),
                    Notes = table.Column<string>(type: "nvarchar(1000)", maxLength: 1000, nullable: true),
                    CreatedBy = table.Column<int>(type: "int", nullable: true),
                    CreatedAt = table.Column<DateTime>(type: "datetime", nullable: false, defaultValueSql: "(getutcdate())")
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_AdsSpendLedger", x => x.SpendId);
                    table.ForeignKey(
                        name: "FK_AdsSpendLedger_AdsCampaign",
                        column: x => x.AdsCampaignId,
                        principalTable: "AdsCampaign",
                        principalColumn: "AdsCampaignId");
                    table.ForeignKey(
                        name: "FK_AdsSpendLedger_Wallet",
                        column: x => x.WalletId,
                        principalTable: "SellerAdsWallet",
                        principalColumn: "WalletId",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "CampaignProductSlot",
                columns: table => new
                {
                    SlotId = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    CampaignId = table.Column<int>(type: "int", nullable: false),
                    ParticipationId = table.Column<int>(type: "int", nullable: true),
                    SellerId = table.Column<int>(type: "int", nullable: false),
                    ProductId = table.Column<int>(type: "int", nullable: false),
                    Status = table.Column<string>(type: "nvarchar(40)", maxLength: 40, nullable: false, defaultValue: "draft"),
                    FlashSalePrice = table.Column<decimal>(type: "decimal(18,2)", nullable: true),
                    InventoryLimit = table.Column<int>(type: "int", nullable: true),
                    CreatedAt = table.Column<DateTime>(type: "datetime", nullable: false, defaultValueSql: "(getutcdate())"),
                    ApprovedAt = table.Column<DateTime>(type: "datetime", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_CampaignProductSlot", x => x.SlotId);
                    table.ForeignKey(
                        name: "FK_CampaignProductSlot_Campaign",
                        column: x => x.CampaignId,
                        principalTable: "Campaign",
                        principalColumn: "CampaignId",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_CampaignProductSlot_Participation",
                        column: x => x.ParticipationId,
                        principalTable: "CampaignSellerParticipation",
                        principalColumn: "ParticipationId");
                });

            migrationBuilder.CreateTable(
                name: "CouponUsageHistory",
                columns: table => new
                {
                    UsageID = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    CouponID = table.Column<int>(type: "int", nullable: false),
                    UserID = table.Column<int>(type: "int", nullable: false),
                    OrderID = table.Column<int>(type: "int", nullable: false),
                    DiscountAmount = table.Column<decimal>(type: "decimal(10,2)", nullable: false),
                    UsedDate = table.Column<DateTime>(type: "datetime", nullable: false, defaultValueSql: "(getdate())"),
                    IPAddress = table.Column<string>(type: "nvarchar(50)", maxLength: 50, nullable: true),
                    UserAgent = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK__CouponUs__29B197C004176640", x => x.UsageID);
                    table.ForeignKey(
                        name: "FK_CUH_Coupon",
                        column: x => x.CouponID,
                        principalTable: "Coupons",
                        principalColumn: "CouponID");
                    table.ForeignKey(
                        name: "FK_CUH_Order",
                        column: x => x.OrderID,
                        principalTable: "Orders",
                        principalColumn: "OrderID");
                });

            migrationBuilder.CreateTable(
                name: "OrderDetail",
                columns: table => new
                {
                    OrderDetailID = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    OrderID = table.Column<int>(type: "int", nullable: false),
                    ProductID = table.Column<int>(type: "int", nullable: false),
                    Quantity = table.Column<int>(type: "int", nullable: false, defaultValue: 1),
                    UnitPrice = table.Column<decimal>(type: "decimal(18,2)", nullable: false),
                    UnitSymbol = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK__OrderDet__D3B9D30CBC689489", x => x.OrderDetailID);
                    table.ForeignKey(
                        name: "FK__OrderDeta__Order__395884C4",
                        column: x => x.OrderID,
                        principalTable: "Orders",
                        principalColumn: "OrderID");
                });

            migrationBuilder.CreateTable(
                name: "Payment",
                columns: table => new
                {
                    PaymentID = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    OrderID = table.Column<int>(type: "int", nullable: false),
                    PaymentMethod = table.Column<string>(type: "nvarchar(50)", maxLength: 50, nullable: false),
                    BankName = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: true),
                    AccountName = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: true),
                    AccountNumber = table.Column<string>(type: "nvarchar(50)", maxLength: 50, nullable: true),
                    TransactionCode = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: true),
                    PaymentStatus = table.Column<string>(type: "nvarchar(50)", maxLength: 50, nullable: false, defaultValue: "Chưa thanh toán"),
                    PaymentDate = table.Column<DateTime>(type: "datetime", nullable: true),
                    UserID = table.Column<int>(type: "int", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK__Payment__9B556A588FA72E9D", x => x.PaymentID);
                    table.ForeignKey(
                        name: "FK__Payment__OrderID__3C34F16F",
                        column: x => x.OrderID,
                        principalTable: "Orders",
                        principalColumn: "OrderID");
                });

            migrationBuilder.CreateTable(
                name: "PaymentTransactions",
                columns: table => new
                {
                    PaymentTxnID = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    OrderID = table.Column<int>(type: "int", nullable: false),
                    Provider = table.Column<string>(type: "nvarchar(50)", maxLength: 50, nullable: false),
                    Method = table.Column<string>(type: "nvarchar(50)", maxLength: 50, nullable: false),
                    Amount = table.Column<decimal>(type: "decimal(18,2)", nullable: false),
                    Currency = table.Column<string>(type: "nvarchar(10)", maxLength: 10, nullable: false, defaultValue: "VND"),
                    FeeAmount = table.Column<decimal>(type: "decimal(18,2)", nullable: true),
                    Status = table.Column<string>(type: "nvarchar(30)", maxLength: 30, nullable: false),
                    ProviderRef = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: true),
                    FailureCode = table.Column<string>(type: "nvarchar(50)", maxLength: 50, nullable: true),
                    FailureMessage = table.Column<string>(type: "nvarchar(255)", maxLength: 255, nullable: true),
                    PaidAt = table.Column<DateTime>(type: "datetime", nullable: true),
                    CreatedAt = table.Column<DateTime>(type: "datetime", nullable: false, defaultValueSql: "(getutcdate())")
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK__PaymentT__F1E4F1947DF6990F", x => x.PaymentTxnID);
                    table.ForeignKey(
                        name: "FK_PaymentTxn_Order",
                        column: x => x.OrderID,
                        principalTable: "Orders",
                        principalColumn: "OrderID");
                });

            migrationBuilder.CreateTable(
                name: "SellerOrders",
                columns: table => new
                {
                    SellerOrderID = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    OrderID = table.Column<int>(type: "int", nullable: false),
                    SellerID = table.Column<int>(type: "int", nullable: false),
                    SellerStatus = table.Column<string>(type: "nvarchar(30)", maxLength: 30, nullable: false),
                    CommissionRate = table.Column<decimal>(type: "decimal(9,4)", nullable: false),
                    CommissionAmount = table.Column<decimal>(type: "decimal(18,2)", nullable: false),
                    ShippingFee = table.Column<decimal>(type: "decimal(18,2)", nullable: false, defaultValue: 0m),
                    SellerEarning = table.Column<decimal>(type: "decimal(18,2)", nullable: false),
                    CancelledBy = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: true),
                    CancelReasonID = table.Column<int>(type: "int", nullable: true),
                    CreatedAt = table.Column<DateTime>(type: "datetime", nullable: false, defaultValueSql: "(getutcdate())"),
                    UpdatedAt = table.Column<DateTime>(type: "datetime", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK__SellerOr__EE1F8CF4BF4B2300", x => x.SellerOrderID);
                    table.ForeignKey(
                        name: "FK_SellerOrders_CancelReason",
                        column: x => x.CancelReasonID,
                        principalTable: "CancelReasons",
                        principalColumn: "CancelReasonID");
                    table.ForeignKey(
                        name: "FK_SellerOrders_Order",
                        column: x => x.OrderID,
                        principalTable: "Orders",
                        principalColumn: "OrderID");
                });

            migrationBuilder.CreateTable(
                name: "Shipping",
                columns: table => new
                {
                    ShippingID = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    OrderID = table.Column<int>(type: "int", nullable: false),
                    ShippingType = table.Column<string>(type: "nvarchar(50)", maxLength: 50, nullable: false),
                    FullName = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: false),
                    Phone = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: false),
                    Email = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: true),
                    AddressDetail = table.Column<string>(type: "nvarchar(255)", maxLength: 255, nullable: true),
                    ProvinceId = table.Column<int>(type: "int", nullable: true),
                    CommuneId = table.Column<int>(type: "int", nullable: true),
                    GhnOrderCode = table.Column<string>(type: "nvarchar(50)", maxLength: 50, nullable: true),
                    GhnClientOrderCode = table.Column<string>(type: "nvarchar(50)", maxLength: 50, nullable: true),
                    GhnStatus = table.Column<string>(type: "nvarchar(50)", maxLength: 50, nullable: true),
                    GhnStatusLabel = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: true),
                    GhnTotalFee = table.Column<decimal>(type: "decimal(18,2)", nullable: true),
                    GhnCreatedAt = table.Column<DateTime>(type: "datetime", nullable: true),
                    GhnExpectedDeliveryTime = table.Column<DateTime>(type: "datetime", nullable: true),
                    GhnLastSyncedAt = table.Column<DateTime>(type: "datetime", nullable: true),
                    IsStorePickup = table.Column<bool>(type: "bit", nullable: false),
                    StoreAddress = table.Column<string>(type: "nvarchar(255)", maxLength: 255, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK__Shipping__5FACD460E9B3CC0E", x => x.ShippingID);
                    table.ForeignKey(
                        name: "FK__Shipping__OrderI__498EEC8D",
                        column: x => x.OrderID,
                        principalTable: "Orders",
                        principalColumn: "OrderID");
                });

            migrationBuilder.CreateTable(
                name: "ReconciliationLog",
                columns: table => new
                {
                    LogID = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    PaymentID = table.Column<int>(type: "int", nullable: false),
                    OrderID = table.Column<int>(type: "int", nullable: false),
                    AdminID = table.Column<int>(type: "int", nullable: true),
                    ActionType = table.Column<string>(type: "nvarchar(50)", maxLength: 50, nullable: false),
                    Amount = table.Column<decimal>(type: "decimal(18,2)", nullable: true),
                    Note = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: true),
                    CreatedAt = table.Column<DateTime>(type: "datetime", nullable: false, defaultValueSql: "(getdate())")
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK__Reconcil__5E5499A8B43A438A", x => x.LogID);
                    table.ForeignKey(
                        name: "FK_ReconciliationLog_Order",
                        column: x => x.OrderID,
                        principalTable: "Orders",
                        principalColumn: "OrderID");
                    table.ForeignKey(
                        name: "FK_ReconciliationLog_Payment",
                        column: x => x.PaymentID,
                        principalTable: "Payment",
                        principalColumn: "PaymentID");
                });

            migrationBuilder.CreateTable(
                name: "Payouts",
                columns: table => new
                {
                    PayoutID = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    SellerID = table.Column<int>(type: "int", nullable: false),
                    AmountGross = table.Column<decimal>(type: "decimal(18,2)", nullable: false),
                    FeeAmount = table.Column<decimal>(type: "decimal(18,2)", nullable: false),
                    AmountNet = table.Column<decimal>(type: "decimal(19,2)", nullable: true, computedColumnSql: "([AmountGross]-[FeeAmount])", stored: true),
                    Status = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: false),
                    ScheduledAt = table.Column<DateTime>(type: "datetime", nullable: true),
                    PaidAt = table.Column<DateTime>(type: "datetime", nullable: true),
                    PaymentTxnID = table.Column<int>(type: "int", nullable: true),
                    CreatedAt = table.Column<DateTime>(type: "datetime", nullable: false, defaultValueSql: "(getutcdate())")
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK__Payouts__35C3DFAE9DF87565", x => x.PayoutID);
                    table.ForeignKey(
                        name: "FK_Payouts_PaymentTxn",
                        column: x => x.PaymentTxnID,
                        principalTable: "PaymentTransactions",
                        principalColumn: "PaymentTxnID");
                });

            migrationBuilder.CreateTable(
                name: "RefundTransactions",
                columns: table => new
                {
                    RefundID = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    PaymentTxnID = table.Column<int>(type: "int", nullable: false),
                    Amount = table.Column<decimal>(type: "decimal(18,2)", nullable: false),
                    Status = table.Column<string>(type: "nvarchar(30)", maxLength: 30, nullable: false),
                    Channel = table.Column<string>(type: "nvarchar(50)", maxLength: 50, nullable: false),
                    ReferenceCode = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: true),
                    CreatedAt = table.Column<DateTime>(type: "datetime", nullable: false, defaultValueSql: "(getutcdate())"),
                    ProcessedAt = table.Column<DateTime>(type: "datetime", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK__RefundTr__725AB90050097E37", x => x.RefundID);
                    table.ForeignKey(
                        name: "FK_RefundTransactions_PaymentTxn",
                        column: x => x.PaymentTxnID,
                        principalTable: "PaymentTransactions",
                        principalColumn: "PaymentTxnID");
                });

            migrationBuilder.CreateTable(
                name: "SellerOrderItems",
                columns: table => new
                {
                    SellerOrderItemID = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    SellerOrderID = table.Column<int>(type: "int", nullable: false),
                    ListingID = table.Column<int>(type: "int", nullable: false),
                    ProductID = table.Column<int>(type: "int", nullable: false),
                    Quantity = table.Column<int>(type: "int", nullable: false),
                    UnitPrice = table.Column<decimal>(type: "decimal(18,2)", nullable: false),
                    DiscountAmount = table.Column<decimal>(type: "decimal(18,2)", nullable: false),
                    SnapshotName = table.Column<string>(type: "nvarchar(255)", maxLength: 255, nullable: false),
                    SnapshotAttributes = table.Column<string>(type: "nvarchar(1000)", maxLength: 1000, nullable: true),
                    FulfillmentType = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: false, defaultValue: "SellerShip"),
                    FinalAmount = table.Column<decimal>(type: "decimal(30,2)", nullable: true, computedColumnSql: "([Quantity]*[UnitPrice]-[DiscountAmount])", stored: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK__SellerOr__CEF4003370300F7C", x => x.SellerOrderItemID);
                    table.ForeignKey(
                        name: "FK_SellerOrderItems_SO",
                        column: x => x.SellerOrderID,
                        principalTable: "SellerOrders",
                        principalColumn: "SellerOrderID");
                });

            migrationBuilder.CreateTable(
                name: "Shipments",
                columns: table => new
                {
                    ShipmentID = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    SellerOrderID = table.Column<int>(type: "int", nullable: false),
                    WarehouseID = table.Column<int>(type: "int", nullable: true),
                    Carrier = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: false),
                    ServiceLevel = table.Column<string>(type: "nvarchar(50)", maxLength: 50, nullable: true),
                    TrackingNumber = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: true),
                    LabelUrl = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: true),
                    Status = table.Column<string>(type: "nvarchar(30)", maxLength: 30, nullable: false),
                    CODAmount = table.Column<decimal>(type: "decimal(18,2)", nullable: true),
                    WeightKg = table.Column<decimal>(type: "decimal(10,3)", nullable: true),
                    LengthCm = table.Column<decimal>(type: "decimal(10,2)", nullable: true),
                    WidthCm = table.Column<decimal>(type: "decimal(10,2)", nullable: true),
                    HeightCm = table.Column<decimal>(type: "decimal(10,2)", nullable: true),
                    ShippedAt = table.Column<DateTime>(type: "datetime", nullable: true),
                    DeliveredAt = table.Column<DateTime>(type: "datetime", nullable: true),
                    CreatedAt = table.Column<DateTime>(type: "datetime", nullable: false, defaultValueSql: "(getutcdate())")
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK__Shipment__5CAD378D0ACDC2AD", x => x.ShipmentID);
                    table.ForeignKey(
                        name: "FK_Shipments_SO",
                        column: x => x.SellerOrderID,
                        principalTable: "SellerOrders",
                        principalColumn: "SellerOrderID");
                });

            migrationBuilder.CreateTable(
                name: "PayoutItems",
                columns: table => new
                {
                    PayoutItemID = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    PayoutID = table.Column<int>(type: "int", nullable: false),
                    SellerOrderID = table.Column<int>(type: "int", nullable: false),
                    Amount = table.Column<decimal>(type: "decimal(18,2)", nullable: false),
                    Note = table.Column<string>(type: "nvarchar(255)", maxLength: 255, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK__PayoutIt__3414EEFEFA459973", x => x.PayoutItemID);
                    table.ForeignKey(
                        name: "FK_PayoutItems_Payout",
                        column: x => x.PayoutID,
                        principalTable: "Payouts",
                        principalColumn: "PayoutID");
                    table.ForeignKey(
                        name: "FK_PayoutItems_SellerOrder",
                        column: x => x.SellerOrderID,
                        principalTable: "SellerOrders",
                        principalColumn: "SellerOrderID");
                });

            migrationBuilder.CreateTable(
                name: "InventoryReservation",
                columns: table => new
                {
                    ReservationID = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    InventoryID = table.Column<int>(type: "int", nullable: false),
                    OrderID = table.Column<int>(type: "int", nullable: true),
                    SellerOrderItemID = table.Column<int>(type: "int", nullable: true),
                    Quantity = table.Column<int>(type: "int", nullable: false),
                    ExpiresAt = table.Column<DateTime>(type: "datetime", nullable: false),
                    Status = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "datetime", nullable: false, defaultValueSql: "(getutcdate())")
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK__Inventor__B7EE5F04BE112097", x => x.ReservationID);
                    table.ForeignKey(
                        name: "FK_InventoryReservation_Order",
                        column: x => x.OrderID,
                        principalTable: "Orders",
                        principalColumn: "OrderID");
                    table.ForeignKey(
                        name: "FK_InventoryReservation_SOI",
                        column: x => x.SellerOrderItemID,
                        principalTable: "SellerOrderItems",
                        principalColumn: "SellerOrderItemID");
                });

            migrationBuilder.CreateTable(
                name: "ReturnRequests",
                columns: table => new
                {
                    ReturnID = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    SellerOrderItemID = table.Column<int>(type: "int", nullable: false),
                    SellerID = table.Column<int>(type: "int", nullable: false),
                    ReasonCode = table.Column<string>(type: "nvarchar(50)", maxLength: 50, nullable: false),
                    Description = table.Column<string>(type: "nvarchar(1000)", maxLength: 1000, nullable: true),
                    Photos = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    Status = table.Column<string>(type: "nvarchar(30)", maxLength: 30, nullable: false),
                    Resolution = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: false),
                    RequestedAt = table.Column<DateTime>(type: "datetime", nullable: false, defaultValueSql: "(getutcdate())"),
                    ApprovedAt = table.Column<DateTime>(type: "datetime", nullable: true),
                    CompletedAt = table.Column<DateTime>(type: "datetime", nullable: true),
                    RefundAmount = table.Column<decimal>(type: "decimal(18,2)", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK__ReturnRe__F445E988BD05394F", x => x.ReturnID);
                    table.ForeignKey(
                        name: "FK_ReturnRequests_SOI",
                        column: x => x.SellerOrderItemID,
                        principalTable: "SellerOrderItems",
                        principalColumn: "SellerOrderItemID");
                });

            migrationBuilder.CreateTable(
                name: "ShipmentEvents",
                columns: table => new
                {
                    EventID = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    ShipmentID = table.Column<int>(type: "int", nullable: false),
                    EventCode = table.Column<string>(type: "nvarchar(50)", maxLength: 50, nullable: false),
                    EventStatus = table.Column<string>(type: "nvarchar(50)", maxLength: 50, nullable: true),
                    EventAt = table.Column<DateTime>(type: "datetime", nullable: false),
                    Location = table.Column<string>(type: "nvarchar(255)", maxLength: 255, nullable: true),
                    RawPayload = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    CreatedAt = table.Column<DateTime>(type: "datetime", nullable: false, defaultValueSql: "(getutcdate())")
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK__Shipment__7944C870166E8523", x => x.EventID);
                    table.ForeignKey(
                        name: "FK_ShipmentEvents_Shipment",
                        column: x => x.ShipmentID,
                        principalTable: "Shipments",
                        principalColumn: "ShipmentID");
                });

            migrationBuilder.CreateIndex(
                name: "IX_AdminActionLog_Area_CreatedAt",
                table: "AdminActionLog",
                columns: new[] { "Area", "CreatedAt" },
                descending: new[] { false, true });

            migrationBuilder.CreateIndex(
                name: "IX_AdminActionLog_Target",
                table: "AdminActionLog",
                columns: new[] { "TargetType", "TargetId" });

            migrationBuilder.CreateIndex(
                name: "IX_AdsCampaign_CampaignId",
                table: "AdsCampaign",
                column: "CampaignId");

            migrationBuilder.CreateIndex(
                name: "IX_AdsCampaign_Seller_Channel",
                table: "AdsCampaign",
                columns: new[] { "SellerId", "Channel" });

            migrationBuilder.CreateIndex(
                name: "IX_AdsCampaign_Status_StartAt",
                table: "AdsCampaign",
                columns: new[] { "Status", "StartAt" },
                descending: new[] { false, true });

            migrationBuilder.CreateIndex(
                name: "IX_AdsCampaign_WalletId",
                table: "AdsCampaign",
                column: "WalletId");

            migrationBuilder.CreateIndex(
                name: "IX_AdsSpendLedger_AdsCampaignId",
                table: "AdsSpendLedger",
                column: "AdsCampaignId");

            migrationBuilder.CreateIndex(
                name: "IX_AdsSpendLedger_Seller_CreatedAt",
                table: "AdsSpendLedger",
                columns: new[] { "SellerId", "CreatedAt" },
                descending: new[] { false, true });

            migrationBuilder.CreateIndex(
                name: "IX_AdsSpendLedger_Status_CreatedAt",
                table: "AdsSpendLedger",
                columns: new[] { "Status", "CreatedAt" },
                descending: new[] { false, true });

            migrationBuilder.CreateIndex(
                name: "IX_AdsSpendLedger_WalletId",
                table: "AdsSpendLedger",
                column: "WalletId");

            migrationBuilder.CreateIndex(
                name: "IX_AdsTopup_Seller_CreatedAt",
                table: "AdsTopup",
                columns: new[] { "SellerId", "CreatedAt" },
                descending: new[] { false, true });

            migrationBuilder.CreateIndex(
                name: "IX_AdsTopup_Status_CreatedAt",
                table: "AdsTopup",
                columns: new[] { "Status", "CreatedAt" },
                descending: new[] { false, true });

            migrationBuilder.CreateIndex(
                name: "IX_AdsTopup_WalletId",
                table: "AdsTopup",
                column: "WalletId");

            migrationBuilder.CreateIndex(
                name: "IX_Campaign_Status_StartAt",
                table: "Campaign",
                columns: new[] { "Status", "StartAt" },
                descending: new[] { false, true });

            migrationBuilder.CreateIndex(
                name: "IX_Campaign_Type_Status",
                table: "Campaign",
                columns: new[] { "CampaignType", "Status" });

            migrationBuilder.CreateIndex(
                name: "IX_Campaign_VoucherCouponId",
                table: "Campaign",
                column: "VoucherCouponId");

            migrationBuilder.CreateIndex(
                name: "IX_CampaignProductSlot_ParticipationId",
                table: "CampaignProductSlot",
                column: "ParticipationId");

            migrationBuilder.CreateIndex(
                name: "IX_CampaignProductSlot_Status_CreatedAt",
                table: "CampaignProductSlot",
                columns: new[] { "Status", "CreatedAt" },
                descending: new[] { false, true });

            migrationBuilder.CreateIndex(
                name: "UQ_CampaignProductSlot_Campaign_Seller_Product",
                table: "CampaignProductSlot",
                columns: new[] { "CampaignId", "SellerId", "ProductId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_CampaignSellerParticipation_Status_RequestedAt",
                table: "CampaignSellerParticipation",
                columns: new[] { "Status", "RequestedAt" },
                descending: new[] { false, true });

            migrationBuilder.CreateIndex(
                name: "UQ_CampaignSellerParticipation_Campaign_Seller",
                table: "CampaignSellerParticipation",
                columns: new[] { "CampaignId", "SellerId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "UQ__CancelRe__A6278DA30C8440EF",
                table: "CancelReasons",
                column: "ReasonCode",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_Cart_UserID",
                table: "Cart",
                column: "UserID",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_CartItem_Cart_Product_Seller",
                table: "CartItem",
                columns: new[] { "CartID", "ProductID", "SellerID" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_CommunicationTemplate_Channel_IsActive",
                table: "CommunicationTemplate",
                columns: new[] { "Channel", "IsActive" });

            migrationBuilder.CreateIndex(
                name: "UQ_CommunicationTemplate_Event_Channel_Locale_Name",
                table: "CommunicationTemplate",
                columns: new[] { "EventType", "Channel", "Locale", "TemplateName" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_ContactMessages_IsDeleted_CreatedAt",
                table: "ContactMessages",
                columns: new[] { "IsDeleted", "CreatedAt" },
                descending: new[] { false, true });

            migrationBuilder.CreateIndex(
                name: "IX_ContactMessages_Status",
                table: "ContactMessages",
                column: "Status");

            migrationBuilder.CreateIndex(
                name: "IX_CouponDistribution_CouponID",
                table: "CouponDistribution",
                column: "CouponID");

            migrationBuilder.CreateIndex(
                name: "IX_CouponDistribution_IsUsed",
                table: "CouponDistribution",
                column: "IsUsed");

            migrationBuilder.CreateIndex(
                name: "IX_CouponDistribution_Status",
                table: "CouponDistribution",
                column: "Status");

            migrationBuilder.CreateIndex(
                name: "IX_CouponDistribution_UserID",
                table: "CouponDistribution",
                column: "UserID");

            migrationBuilder.CreateIndex(
                name: "UQ_CouponDistribution_Coupon_User",
                table: "CouponDistribution",
                columns: new[] { "CouponID", "UserID" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_Coupons_Active_Expiry",
                table: "Coupons",
                columns: new[] { "IsActive", "ExpiryDate" });

            migrationBuilder.CreateIndex(
                name: "IX_Coupons_Code",
                table: "Coupons",
                column: "Code");

            migrationBuilder.CreateIndex(
                name: "IX_Coupons_ExpiryDate",
                table: "Coupons",
                column: "ExpiryDate");

            migrationBuilder.CreateIndex(
                name: "IX_Coupons_IsActive",
                table: "Coupons",
                column: "IsActive");

            migrationBuilder.CreateIndex(
                name: "UQ__Coupons__A25C5AA7914A76AA",
                table: "Coupons",
                column: "Code",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_CouponUsageHistory_CouponID",
                table: "CouponUsageHistory",
                column: "CouponID");

            migrationBuilder.CreateIndex(
                name: "IX_CouponUsageHistory_OrderID",
                table: "CouponUsageHistory",
                column: "OrderID");

            migrationBuilder.CreateIndex(
                name: "IX_CouponUsageHistory_UsedDate",
                table: "CouponUsageHistory",
                column: "UsedDate");

            migrationBuilder.CreateIndex(
                name: "IX_CouponUsageHistory_UserID",
                table: "CouponUsageHistory",
                column: "UserID");

            migrationBuilder.CreateIndex(
                name: "IX_CustomerNotifications_OrderID",
                table: "CustomerNotifications",
                column: "OrderID");

            migrationBuilder.CreateIndex(
                name: "IX_CustomerNotifications_UserID",
                table: "CustomerNotifications",
                column: "UserID");

            migrationBuilder.CreateIndex(
                name: "IX_InventoryReservation_OrderID",
                table: "InventoryReservation",
                column: "OrderID");

            migrationBuilder.CreateIndex(
                name: "IX_InventoryReservation_SellerOrderItemID",
                table: "InventoryReservation",
                column: "SellerOrderItemID");

            migrationBuilder.CreateIndex(
                name: "IX_LPH_OrderID",
                table: "LoyaltyPointHistory",
                column: "OrderID");

            migrationBuilder.CreateIndex(
                name: "IX_LPH_UserID",
                table: "LoyaltyPointHistory",
                columns: new[] { "UserID", "CreatedAt" },
                descending: new[] { false, true });

            migrationBuilder.CreateIndex(
                name: "IX_ModerationAudit_AdminActionLogId",
                table: "ModerationAudit",
                column: "AdminActionLogId");

            migrationBuilder.CreateIndex(
                name: "IX_ModerationAudit_Decision_CreatedAt",
                table: "ModerationAudit",
                columns: new[] { "Decision", "CreatedAt" },
                descending: new[] { false, true });

            migrationBuilder.CreateIndex(
                name: "IX_ModerationAudit_Subject",
                table: "ModerationAudit",
                columns: new[] { "SubjectType", "SubjectId" });

            migrationBuilder.CreateIndex(
                name: "IX_NotificationPolicyRule_Channel_IsEnabled",
                table: "NotificationPolicyRule",
                columns: new[] { "Channel", "IsEnabled" });

            migrationBuilder.CreateIndex(
                name: "IX_NotificationPolicyRule_CommunicationTemplateId",
                table: "NotificationPolicyRule",
                column: "CommunicationTemplateId");

            migrationBuilder.CreateIndex(
                name: "UQ_NotificationPolicyRule_Event_Channel_Audience",
                table: "NotificationPolicyRule",
                columns: new[] { "EventType", "Channel", "AudienceType" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_NotificationPreference_Channel_IsOptedIn",
                table: "NotificationPreference",
                columns: new[] { "Channel", "IsOptedIn" });

            migrationBuilder.CreateIndex(
                name: "UQ_NotificationPreference_User_Event_Channel",
                table: "NotificationPreference",
                columns: new[] { "UserId", "EventType", "Channel" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_OrderDetail_OrderID",
                table: "OrderDetail",
                column: "OrderID");

            migrationBuilder.CreateIndex(
                name: "IX_Orders_CouponID",
                table: "Orders",
                column: "CouponID");

            migrationBuilder.CreateIndex(
                name: "IX_Orders_Status_OrderDate_OrderID",
                table: "Orders",
                columns: new[] { "Status", "OrderDate", "OrderID" });

            migrationBuilder.CreateIndex(
                name: "IX_Orders_StatusID",
                table: "Orders",
                column: "StatusID");

            migrationBuilder.CreateIndex(
                name: "IX_Orders_User_Status_PaidAt",
                table: "Orders",
                columns: new[] { "UserID", "Status", "PaidAt" });

            migrationBuilder.CreateIndex(
                name: "IX_Payment_Order_Paid",
                table: "Payment",
                columns: new[] { "OrderID", "PaymentDate" });

            migrationBuilder.CreateIndex(
                name: "IX_Payments_OrderID_PaymentMethod_PaymentDate",
                table: "Payment",
                columns: new[] { "OrderID", "PaymentMethod", "PaymentDate" });

            migrationBuilder.CreateIndex(
                name: "IX_PaymentTransactions_OrderID",
                table: "PaymentTransactions",
                column: "OrderID");

            migrationBuilder.CreateIndex(
                name: "IX_PayoutItems_PayoutID",
                table: "PayoutItems",
                column: "PayoutID");

            migrationBuilder.CreateIndex(
                name: "IX_PayoutItems_SellerOrderID",
                table: "PayoutItems",
                column: "SellerOrderID");

            migrationBuilder.CreateIndex(
                name: "IX_Payouts_PaymentTxnID",
                table: "Payouts",
                column: "PaymentTxnID");

            migrationBuilder.CreateIndex(
                name: "IX_ReconciliationLog_OrderID",
                table: "ReconciliationLog",
                column: "OrderID");

            migrationBuilder.CreateIndex(
                name: "IX_ReconciliationLog_Payment_Order_CreatedAt",
                table: "ReconciliationLog",
                columns: new[] { "PaymentID", "OrderID", "CreatedAt" },
                descending: new[] { false, false, true });

            migrationBuilder.CreateIndex(
                name: "IX_RefundTransactions_PaymentTxnID",
                table: "RefundTransactions",
                column: "PaymentTxnID");

            migrationBuilder.CreateIndex(
                name: "IX_ReturnRequests_SellerOrderItemID",
                table: "ReturnRequests",
                column: "SellerOrderItemID");

            migrationBuilder.CreateIndex(
                name: "IX_Review_IsDeleted_CreatedAt",
                table: "Review",
                columns: new[] { "IsDeleted", "CreatedAt" },
                descending: new[] { false, true });

            migrationBuilder.CreateIndex(
                name: "IX_Review_Product_CreatedAt",
                table: "Review",
                columns: new[] { "ProductID", "CreatedAt" },
                descending: new[] { false, true });

            migrationBuilder.CreateIndex(
                name: "IX_Review_ReplyTo",
                table: "Review",
                column: "ReplyTo");

            migrationBuilder.CreateIndex(
                name: "IX_ReviewReport_Review",
                table: "ReviewReport",
                columns: new[] { "ReviewID", "Status", "CreatedAt" },
                descending: new[] { false, false, true });

            migrationBuilder.CreateIndex(
                name: "IX_RiskCase_Status_Severity",
                table: "RiskCase",
                columns: new[] { "Status", "Severity" });

            migrationBuilder.CreateIndex(
                name: "IX_RiskCase_Type_CreatedAt",
                table: "RiskCase",
                columns: new[] { "CaseType", "CreatedAt" },
                descending: new[] { false, true });

            migrationBuilder.CreateIndex(
                name: "UQ_RiskCase_ReferenceKey",
                table: "RiskCase",
                column: "ReferenceKey",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_RiskDecision_Case_CreatedAt",
                table: "RiskDecision",
                columns: new[] { "RiskCaseId", "CreatedAt" },
                descending: new[] { false, true });

            migrationBuilder.CreateIndex(
                name: "IX_RiskDecision_Type",
                table: "RiskDecision",
                column: "DecisionType");

            migrationBuilder.CreateIndex(
                name: "IX_RiskSignal_Case_TriggeredAt",
                table: "RiskSignal",
                columns: new[] { "RiskCaseId", "TriggeredAt" },
                descending: new[] { false, true });

            migrationBuilder.CreateIndex(
                name: "IX_RiskSignal_Type_Severity",
                table: "RiskSignal",
                columns: new[] { "SignalType", "Severity" });

            migrationBuilder.CreateIndex(
                name: "UQ_RiskSignal_ReferenceKey",
                table: "RiskSignal",
                column: "ReferenceKey",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_SellerAdsWallet_Status_Balance",
                table: "SellerAdsWallet",
                columns: new[] { "Status", "Balance" },
                descending: new[] { false, true });

            migrationBuilder.CreateIndex(
                name: "UQ_SellerAdsWallet_Seller",
                table: "SellerAdsWallet",
                column: "SellerId",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_SellerOrderItems_SellerOrderID",
                table: "SellerOrderItems",
                column: "SellerOrderID");

            migrationBuilder.CreateIndex(
                name: "IX_SellerOrders_CancelReasonID",
                table: "SellerOrders",
                column: "CancelReasonID");

            migrationBuilder.CreateIndex(
                name: "IX_SellerOrders_OrderID",
                table: "SellerOrders",
                column: "OrderID");

            migrationBuilder.CreateIndex(
                name: "IX_SettlementAudit_AdminActionLogId",
                table: "SettlementAudit",
                column: "AdminActionLogId");

            migrationBuilder.CreateIndex(
                name: "IX_SettlementAudit_Reference",
                table: "SettlementAudit",
                columns: new[] { "ReferenceType", "ReferenceId" });

            migrationBuilder.CreateIndex(
                name: "IX_SettlementAudit_Type_CreatedAt",
                table: "SettlementAudit",
                columns: new[] { "AuditType", "CreatedAt" },
                descending: new[] { false, true });

            migrationBuilder.CreateIndex(
                name: "IX_ShipmentEvents_ShipmentID",
                table: "ShipmentEvents",
                column: "ShipmentID");

            migrationBuilder.CreateIndex(
                name: "IX_Shipments_SellerOrderID",
                table: "Shipments",
                column: "SellerOrderID");

            migrationBuilder.CreateIndex(
                name: "IX_Shipping_OrderID",
                table: "Shipping",
                column: "OrderID");

            migrationBuilder.CreateIndex(
                name: "IX_Status_IsActive",
                table: "Status",
                column: "IsActive");

            migrationBuilder.CreateIndex(
                name: "IX_Status_StatusTypeID",
                table: "Status",
                column: "StatusTypeID");

            migrationBuilder.CreateIndex(
                name: "UQ__StatusTy__656B6258180D479D",
                table: "StatusType",
                column: "StatusTypeName",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_SupportConv_Status_Started",
                table: "SupportConversations",
                columns: new[] { "Status", "StartedAt" },
                descending: new[] { false, true });

            migrationBuilder.CreateIndex(
                name: "IX_SupportMessageReactions_MessageId",
                table: "SupportMessageReactions",
                column: "MessageId");

            migrationBuilder.CreateIndex(
                name: "IX_SupportMessages_ReplyToMessageId",
                table: "SupportMessages",
                column: "ReplyToMessageId");

            migrationBuilder.CreateIndex(
                name: "IX_SupportMsg_Conv_Created",
                table: "SupportMessages",
                columns: new[] { "ConversationId", "CreatedAt" });

            migrationBuilder.CreateIndex(
                name: "IX_VoucherAbuseCase_Coupon_Status",
                table: "VoucherAbuseCase",
                columns: new[] { "CouponId", "Status" });

            migrationBuilder.CreateIndex(
                name: "IX_VoucherAbuseCase_RiskCaseId",
                table: "VoucherAbuseCase",
                column: "RiskCaseId");

            migrationBuilder.CreateIndex(
                name: "UQ_VoucherAbuseCase_ReferenceKey",
                table: "VoucherAbuseCase",
                column: "ReferenceKey",
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "AdsSpendLedger");

            migrationBuilder.DropTable(
                name: "AdsTopup");

            migrationBuilder.DropTable(
                name: "CampaignProductSlot");

            migrationBuilder.DropTable(
                name: "CartItem");

            migrationBuilder.DropTable(
                name: "ContactMessages");

            migrationBuilder.DropTable(
                name: "CouponDistribution");

            migrationBuilder.DropTable(
                name: "CouponUsageHistory");

            migrationBuilder.DropTable(
                name: "CustomerNotifications");

            migrationBuilder.DropTable(
                name: "InventoryReservation");

            migrationBuilder.DropTable(
                name: "LoyaltyConfig");

            migrationBuilder.DropTable(
                name: "LoyaltyPointHistory");

            migrationBuilder.DropTable(
                name: "ModerationAudit");

            migrationBuilder.DropTable(
                name: "NotificationPolicyRule");

            migrationBuilder.DropTable(
                name: "NotificationPreference");

            migrationBuilder.DropTable(
                name: "OrderDetail");

            migrationBuilder.DropTable(
                name: "OrderDetailInvalidProductArchive");

            migrationBuilder.DropTable(
                name: "OrderStatusHistory");

            migrationBuilder.DropTable(
                name: "PayoutItems");

            migrationBuilder.DropTable(
                name: "ReconciliationLog");

            migrationBuilder.DropTable(
                name: "RefundTransactions");

            migrationBuilder.DropTable(
                name: "ReturnRequests");

            migrationBuilder.DropTable(
                name: "ReviewReport");

            migrationBuilder.DropTable(
                name: "RiskDecision");

            migrationBuilder.DropTable(
                name: "RiskSignal");

            migrationBuilder.DropTable(
                name: "SettlementAudit");

            migrationBuilder.DropTable(
                name: "ShipmentEvents");

            migrationBuilder.DropTable(
                name: "Shipping");

            migrationBuilder.DropTable(
                name: "SupportMessageReactions");

            migrationBuilder.DropTable(
                name: "VoucherAbuseCase");

            migrationBuilder.DropTable(
                name: "AdsCampaign");

            migrationBuilder.DropTable(
                name: "CampaignSellerParticipation");

            migrationBuilder.DropTable(
                name: "Cart");

            migrationBuilder.DropTable(
                name: "CommunicationTemplate");

            migrationBuilder.DropTable(
                name: "Payouts");

            migrationBuilder.DropTable(
                name: "Payment");

            migrationBuilder.DropTable(
                name: "SellerOrderItems");

            migrationBuilder.DropTable(
                name: "Review");

            migrationBuilder.DropTable(
                name: "AdminActionLog");

            migrationBuilder.DropTable(
                name: "Shipments");

            migrationBuilder.DropTable(
                name: "SupportMessages");

            migrationBuilder.DropTable(
                name: "RiskCase");

            migrationBuilder.DropTable(
                name: "SellerAdsWallet");

            migrationBuilder.DropTable(
                name: "Campaign");

            migrationBuilder.DropTable(
                name: "PaymentTransactions");

            migrationBuilder.DropTable(
                name: "SellerOrders");

            migrationBuilder.DropTable(
                name: "SupportConversations");

            migrationBuilder.DropTable(
                name: "CancelReasons");

            migrationBuilder.DropTable(
                name: "Orders");

            migrationBuilder.DropTable(
                name: "Status");

            migrationBuilder.DropTable(
                name: "Coupons");

            migrationBuilder.DropTable(
                name: "StatusType");
        }
    }
}
