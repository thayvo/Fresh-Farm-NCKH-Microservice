using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace FreshFarm.Catalog.Api.Migrations
{
    /// <inheritdoc />
    public partial class CatalogSchemaBaseline20260324 : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "Categories",
                columns: table => new
                {
                    CategoryID = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    CategoryName = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: false),
                    Description = table.Column<string>(type: "nvarchar(255)", maxLength: 255, nullable: true),
                    ImageCategoriesName = table.Column<string>(type: "nvarchar(255)", maxLength: 255, nullable: true),
                    IsActive = table.Column<bool>(type: "bit", nullable: false, defaultValue: true),
                    Slug = table.Column<string>(type: "varchar(255)", unicode: false, maxLength: 255, nullable: true),
                    CreatedDate = table.Column<DateTime>(type: "datetime", nullable: false, defaultValueSql: "(getdate())"),
                    UpdatedDate = table.Column<DateTime>(type: "datetime", nullable: true),
                    NearExpiryDays = table.Column<int>(type: "int", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK__Categori__19093A2BD446118F", x => x.CategoryID);
                });

            migrationBuilder.CreateTable(
                name: "Unit",
                columns: table => new
                {
                    UnitID = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    UnitName = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: false),
                    Symbol = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: false),
                    Description = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: true),
                    IsActive = table.Column<bool>(type: "bit", nullable: false, defaultValue: true),
                    CreatedDate = table.Column<DateTime>(type: "datetime", nullable: false, defaultValueSql: "(getdate())")
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Unit", x => x.UnitID);
                });

            migrationBuilder.CreateTable(
                name: "CategoryAttribute",
                columns: table => new
                {
                    CategoryAttributeId = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    CategoryId = table.Column<int>(type: "int", nullable: false),
                    AttributeKey = table.Column<string>(type: "nvarchar(80)", maxLength: 80, nullable: false),
                    DisplayName = table.Column<string>(type: "nvarchar(120)", maxLength: 120, nullable: false),
                    InputType = table.Column<string>(type: "nvarchar(30)", maxLength: 30, nullable: false, defaultValue: "text"),
                    IsRequired = table.Column<bool>(type: "bit", nullable: false),
                    IsFacet = table.Column<bool>(type: "bit", nullable: false),
                    SortOrder = table.Column<int>(type: "int", nullable: false),
                    Placeholder = table.Column<string>(type: "nvarchar(255)", maxLength: 255, nullable: true),
                    IsActive = table.Column<bool>(type: "bit", nullable: false, defaultValue: true),
                    CreatedAt = table.Column<DateTime>(type: "datetime", nullable: false, defaultValueSql: "(getutcdate())"),
                    UpdatedAt = table.Column<DateTime>(type: "datetime", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_CategoryAttribute", x => x.CategoryAttributeId);
                    table.ForeignKey(
                        name: "FK_CategoryAttribute_Category",
                        column: x => x.CategoryId,
                        principalTable: "Categories",
                        principalColumn: "CategoryID",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "Products",
                columns: table => new
                {
                    ProductID = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    CategoryId = table.Column<int>(type: "int", nullable: false),
                    ProductName = table.Column<string>(type: "nvarchar(255)", maxLength: 255, nullable: false),
                    Sku = table.Column<string>(type: "nvarchar(50)", maxLength: 50, nullable: true),
                    Price = table.Column<decimal>(type: "decimal(18,2)", nullable: false),
                    Status = table.Column<bool>(type: "bit", nullable: false, defaultValue: true),
                    StockQuantity = table.Column<int>(type: "int", nullable: false),
                    ImageFileName = table.Column<string>(type: "nvarchar(255)", maxLength: 255, nullable: true),
                    CreatedDate = table.Column<DateTime>(type: "datetime", nullable: false, defaultValueSql: "(getdate())"),
                    ShortDescription = table.Column<string>(type: "nvarchar(300)", maxLength: 300, nullable: true, defaultValue: ""),
                    LongDescription = table.Column<string>(type: "nvarchar(max)", nullable: true, defaultValue: ""),
                    UnitID = table.Column<int>(type: "int", nullable: false),
                    NearExpiryDays = table.Column<int>(type: "int", nullable: true),
                    IsManuallyDisabled = table.Column<bool>(type: "bit", nullable: false),
                    ReservedStock = table.Column<int>(type: "int", nullable: false, defaultValue: 0)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK__Products__B40CC6ED90295002", x => x.ProductID);
                    table.ForeignKey(
                        name: "FK_Products_Unit",
                        column: x => x.UnitID,
                        principalTable: "Unit",
                        principalColumn: "UnitID");
                    table.ForeignKey(
                        name: "FK__Products__Catego__4D94879B",
                        column: x => x.CategoryId,
                        principalTable: "Categories",
                        principalColumn: "CategoryID");
                });

            migrationBuilder.CreateTable(
                name: "FreshInventoryLot",
                columns: table => new
                {
                    FreshInventoryLotId = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    ProductId = table.Column<int>(type: "int", nullable: false),
                    SellerId = table.Column<int>(type: "int", nullable: false),
                    LotCode = table.Column<string>(type: "nvarchar(80)", maxLength: 80, nullable: false),
                    TraceCode = table.Column<string>(type: "nvarchar(120)", maxLength: 120, nullable: true),
                    FarmName = table.Column<string>(type: "nvarchar(150)", maxLength: 150, nullable: true),
                    OriginRegion = table.Column<string>(type: "nvarchar(150)", maxLength: 150, nullable: true),
                    HarvestedAt = table.Column<DateTime>(type: "datetime", nullable: true),
                    PackedAt = table.Column<DateTime>(type: "datetime", nullable: true),
                    ReceivedAt = table.Column<DateTime>(type: "datetime", nullable: false),
                    ExpiresAt = table.Column<DateTime>(type: "datetime", nullable: true),
                    InitialQuantity = table.Column<int>(type: "int", nullable: false),
                    RemainingQuantity = table.Column<int>(type: "int", nullable: false),
                    UnitCost = table.Column<decimal>(type: "decimal(18,2)", nullable: true),
                    Status = table.Column<string>(type: "nvarchar(40)", maxLength: 40, nullable: false, defaultValue: "active"),
                    QualityStatus = table.Column<string>(type: "nvarchar(40)", maxLength: 40, nullable: false, defaultValue: "ok"),
                    Notes = table.Column<string>(type: "nvarchar(2000)", maxLength: 2000, nullable: true),
                    CreatedAt = table.Column<DateTime>(type: "datetime", nullable: false, defaultValueSql: "(getutcdate())"),
                    UpdatedAt = table.Column<DateTime>(type: "datetime", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_FreshInventoryLot", x => x.FreshInventoryLotId);
                    table.ForeignKey(
                        name: "FK_FreshInventoryLot_Product",
                        column: x => x.ProductId,
                        principalTable: "Products",
                        principalColumn: "ProductID",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "ProductAttributeValue",
                columns: table => new
                {
                    ProductAttributeValueId = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    ProductId = table.Column<int>(type: "int", nullable: false),
                    CategoryAttributeId = table.Column<int>(type: "int", nullable: false),
                    ValueText = table.Column<string>(type: "nvarchar(1000)", maxLength: 1000, nullable: false),
                    NormalizedValue = table.Column<string>(type: "nvarchar(1000)", maxLength: 1000, nullable: true),
                    Source = table.Column<string>(type: "nvarchar(50)", maxLength: 50, nullable: true, defaultValue: "manual"),
                    CreatedAt = table.Column<DateTime>(type: "datetime", nullable: false, defaultValueSql: "(getutcdate())"),
                    UpdatedAt = table.Column<DateTime>(type: "datetime", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ProductAttributeValue", x => x.ProductAttributeValueId);
                    table.ForeignKey(
                        name: "FK_ProductAttributeValue_CategoryAttribute",
                        column: x => x.CategoryAttributeId,
                        principalTable: "CategoryAttribute",
                        principalColumn: "CategoryAttributeId",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_ProductAttributeValue_Product",
                        column: x => x.ProductId,
                        principalTable: "Products",
                        principalColumn: "ProductID",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "ProductImage",
                columns: table => new
                {
                    ImageID = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    ProductID = table.Column<int>(type: "int", nullable: false),
                    ImageFileName = table.Column<string>(type: "nvarchar(255)", maxLength: 255, nullable: false),
                    IsMain = table.Column<bool>(type: "bit", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK__ProductI__7516F4EC627BBD39", x => x.ImageID);
                    table.ForeignKey(
                        name: "FK__ProductIm__Produ__4BAC3F29",
                        column: x => x.ProductID,
                        principalTable: "Products",
                        principalColumn: "ProductID");
                });

            migrationBuilder.CreateTable(
                name: "ProductInfo",
                columns: table => new
                {
                    InfoID = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    ProductID = table.Column<int>(type: "int", nullable: false),
                    Weight = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: true),
                    Origin = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: true),
                    Standard = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: true),
                    Preservation = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK__ProductI__4DEC9D9AFB02F334", x => x.InfoID);
                    table.ForeignKey(
                        name: "FK__ProductIn__Produ__4CA06362",
                        column: x => x.ProductID,
                        principalTable: "Products",
                        principalColumn: "ProductID");
                });

            migrationBuilder.CreateTable(
                name: "SellerProducts",
                columns: table => new
                {
                    SellerProductID = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    SellerID = table.Column<int>(type: "int", nullable: false),
                    ProductID = table.Column<int>(type: "int", nullable: false),
                    IsActive = table.Column<bool>(type: "bit", nullable: false, defaultValue: true),
                    CreatedAt = table.Column<DateTime>(type: "datetime", nullable: false, defaultValueSql: "(getutcdate())"),
                    UpdatedAt = table.Column<DateTime>(type: "datetime", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_SellerProducts", x => x.SellerProductID);
                    table.ForeignKey(
                        name: "FK_SellerProducts_Product",
                        column: x => x.ProductID,
                        principalTable: "Products",
                        principalColumn: "ProductID",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "FreshQualityRecall",
                columns: table => new
                {
                    FreshQualityRecallId = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    RecallCode = table.Column<string>(type: "nvarchar(50)", maxLength: 50, nullable: false),
                    ProductId = table.Column<int>(type: "int", nullable: true),
                    FreshInventoryLotId = table.Column<int>(type: "int", nullable: true),
                    SellerId = table.Column<int>(type: "int", nullable: true),
                    RecallType = table.Column<string>(type: "nvarchar(50)", maxLength: 50, nullable: false),
                    Severity = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: false, defaultValue: "medium"),
                    Status = table.Column<string>(type: "nvarchar(40)", maxLength: 40, nullable: false, defaultValue: "open"),
                    Title = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: false),
                    Reason = table.Column<string>(type: "nvarchar(2000)", maxLength: 2000, nullable: true),
                    ActionRequired = table.Column<string>(type: "nvarchar(2000)", maxLength: 2000, nullable: true),
                    StartedAt = table.Column<DateTime>(type: "datetime", nullable: false),
                    ResolvedAt = table.Column<DateTime>(type: "datetime", nullable: true),
                    CreatedAt = table.Column<DateTime>(type: "datetime", nullable: false, defaultValueSql: "(getutcdate())"),
                    UpdatedAt = table.Column<DateTime>(type: "datetime", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_FreshQualityRecall", x => x.FreshQualityRecallId);
                    table.ForeignKey(
                        name: "FK_FreshQualityRecall_FreshInventoryLot",
                        column: x => x.FreshInventoryLotId,
                        principalTable: "FreshInventoryLot",
                        principalColumn: "FreshInventoryLotId");
                    table.ForeignKey(
                        name: "FK_FreshQualityRecall_Product",
                        column: x => x.ProductId,
                        principalTable: "Products",
                        principalColumn: "ProductID");
                });

            migrationBuilder.CreateIndex(
                name: "IX_Categories_IsActive",
                table: "Categories",
                column: "IsActive");

            migrationBuilder.CreateIndex(
                name: "UQ_Categories_CategoryName",
                table: "Categories",
                column: "CategoryName",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "UQ_Categories_Slug",
                table: "Categories",
                column: "Slug",
                unique: true,
                filter: "([Slug] IS NOT NULL)");

            migrationBuilder.CreateIndex(
                name: "IX_CategoryAttribute_Category_Required_Active",
                table: "CategoryAttribute",
                columns: new[] { "CategoryId", "IsRequired", "IsActive" });

            migrationBuilder.CreateIndex(
                name: "UQ_CategoryAttribute_Category_Key",
                table: "CategoryAttribute",
                columns: new[] { "CategoryId", "AttributeKey" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_FreshInventoryLot_Product_Status",
                table: "FreshInventoryLot",
                columns: new[] { "ProductId", "Status" });

            migrationBuilder.CreateIndex(
                name: "IX_FreshInventoryLot_Seller_ExpiresAt",
                table: "FreshInventoryLot",
                columns: new[] { "SellerId", "ExpiresAt" });

            migrationBuilder.CreateIndex(
                name: "UQ_FreshInventoryLot_Seller_Product_Lot",
                table: "FreshInventoryLot",
                columns: new[] { "SellerId", "ProductId", "LotCode" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_FreshQualityRecall_FreshInventoryLotId",
                table: "FreshQualityRecall",
                column: "FreshInventoryLotId");

            migrationBuilder.CreateIndex(
                name: "IX_FreshQualityRecall_ProductId",
                table: "FreshQualityRecall",
                column: "ProductId");

            migrationBuilder.CreateIndex(
                name: "IX_FreshQualityRecall_Seller_Severity",
                table: "FreshQualityRecall",
                columns: new[] { "SellerId", "Severity" });

            migrationBuilder.CreateIndex(
                name: "IX_FreshQualityRecall_Status_StartedAt",
                table: "FreshQualityRecall",
                columns: new[] { "Status", "StartedAt" });

            migrationBuilder.CreateIndex(
                name: "UQ_FreshQualityRecall_Code",
                table: "FreshQualityRecall",
                column: "RecallCode",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_ProductAttributeValue_Attribute_NormalizedValue",
                table: "ProductAttributeValue",
                columns: new[] { "CategoryAttributeId", "NormalizedValue" });

            migrationBuilder.CreateIndex(
                name: "UQ_ProductAttributeValue_Product_Attribute",
                table: "ProductAttributeValue",
                columns: new[] { "ProductId", "CategoryAttributeId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_ProductImage_ProductID",
                table: "ProductImage",
                column: "ProductID");

            migrationBuilder.CreateIndex(
                name: "IX_ProductInfo_ProductID",
                table: "ProductInfo",
                column: "ProductID");

            migrationBuilder.CreateIndex(
                name: "IX_Products_CategoryId",
                table: "Products",
                column: "CategoryId");

            migrationBuilder.CreateIndex(
                name: "IX_Products_UnitID",
                table: "Products",
                column: "UnitID");

            migrationBuilder.CreateIndex(
                name: "UQ__Products__CA1FD3C56FD37852",
                table: "Products",
                column: "Sku",
                unique: true,
                filter: "[Sku] IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "IX_SellerProducts_Product_IsActive",
                table: "SellerProducts",
                columns: new[] { "ProductID", "IsActive" });

            migrationBuilder.CreateIndex(
                name: "IX_SellerProducts_Seller_IsActive",
                table: "SellerProducts",
                columns: new[] { "SellerID", "IsActive" });

            migrationBuilder.CreateIndex(
                name: "UX_SellerProducts_Seller_Product",
                table: "SellerProducts",
                columns: new[] { "SellerID", "ProductID" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "UX_Unit_Symbol",
                table: "Unit",
                column: "Symbol",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "UX_Unit_UnitName",
                table: "Unit",
                column: "UnitName",
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "FreshQualityRecall");

            migrationBuilder.DropTable(
                name: "ProductAttributeValue");

            migrationBuilder.DropTable(
                name: "ProductImage");

            migrationBuilder.DropTable(
                name: "ProductInfo");

            migrationBuilder.DropTable(
                name: "SellerProducts");

            migrationBuilder.DropTable(
                name: "FreshInventoryLot");

            migrationBuilder.DropTable(
                name: "CategoryAttribute");

            migrationBuilder.DropTable(
                name: "Products");

            migrationBuilder.DropTable(
                name: "Unit");

            migrationBuilder.DropTable(
                name: "Categories");
        }
    }
}
