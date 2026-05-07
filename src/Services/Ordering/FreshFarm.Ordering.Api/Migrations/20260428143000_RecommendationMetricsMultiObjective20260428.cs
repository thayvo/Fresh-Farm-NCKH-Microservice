using FreshFarm.Ordering.Api.Models;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace FreshFarm.Ordering.Api.Migrations
{
    /// <inheritdoc />
    [DbContext(typeof(FreshFarmOrderingDBContext))]
    [Migration("20260428143000_RecommendationMetricsMultiObjective20260428")]
    public partial class RecommendationMetricsMultiObjective20260428 : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql(
                """
                IF OBJECT_ID(N'[dbo].[RecommendationAddToCart]', N'U') IS NULL
                BEGIN
                    CREATE TABLE [dbo].[RecommendationAddToCart] (
                        [Id] int NOT NULL IDENTITY,
                        [UserId] int NULL,
                        [ProductId] int NOT NULL,
                        [Position] int NULL,
                        [ExperimentGroup] nvarchar(10) NULL,
                        [Timestamp] datetime NOT NULL CONSTRAINT [DF_RecommendationAddToCart_Timestamp] DEFAULT (getutcdate()),
                        CONSTRAINT [PK_RecommendationAddToCart] PRIMARY KEY ([Id])
                    );
                END;
                """);

            EnsureColumnIfMissing(
                migrationBuilder,
                "RecommendationAddToCart",
                "UserId",
                "int NULL");
            EnsureColumnIfMissing(
                migrationBuilder,
                "RecommendationAddToCart",
                "ProductId",
                "int NOT NULL CONSTRAINT [DF_RecommendationAddToCart_ProductId] DEFAULT (0)");
            EnsureColumnIfMissing(
                migrationBuilder,
                "RecommendationAddToCart",
                "Position",
                "int NULL");
            EnsureColumnIfMissing(
                migrationBuilder,
                "RecommendationAddToCart",
                "ExperimentGroup",
                "nvarchar(10) NULL");
            EnsureColumnIfMissing(
                migrationBuilder,
                "RecommendationAddToCart",
                "Timestamp",
                "datetime NOT NULL CONSTRAINT [DF_RecommendationAddToCart_Timestamp] DEFAULT (getutcdate())");

            migrationBuilder.Sql(
                """
                IF OBJECT_ID(N'[dbo].[RecommendationPurchase]', N'U') IS NULL
                BEGIN
                    CREATE TABLE [dbo].[RecommendationPurchase] (
                        [Id] int NOT NULL IDENTITY,
                        [UserId] int NULL,
                        [ProductId] int NOT NULL,
                        [Position] int NULL,
                        [ExperimentGroup] nvarchar(10) NULL,
                        [Revenue] decimal(18,2) NOT NULL CONSTRAINT [DF_RecommendationPurchase_Revenue] DEFAULT (0),
                        [Timestamp] datetime NOT NULL CONSTRAINT [DF_RecommendationPurchase_Timestamp] DEFAULT (getutcdate()),
                        CONSTRAINT [PK_RecommendationPurchase] PRIMARY KEY ([Id])
                    );
                END;
                """);

            EnsureColumnIfMissing(
                migrationBuilder,
                "RecommendationPurchase",
                "UserId",
                "int NULL");
            EnsureColumnIfMissing(
                migrationBuilder,
                "RecommendationPurchase",
                "ProductId",
                "int NOT NULL CONSTRAINT [DF_RecommendationPurchase_ProductId] DEFAULT (0)");
            EnsureColumnIfMissing(
                migrationBuilder,
                "RecommendationPurchase",
                "Position",
                "int NULL");
            EnsureColumnIfMissing(
                migrationBuilder,
                "RecommendationPurchase",
                "ExperimentGroup",
                "nvarchar(10) NULL");
            EnsureColumnIfMissing(
                migrationBuilder,
                "RecommendationPurchase",
                "Revenue",
                "decimal(18,2) NOT NULL CONSTRAINT [DF_RecommendationPurchase_Revenue] DEFAULT (0)");
            EnsureColumnIfMissing(
                migrationBuilder,
                "RecommendationPurchase",
                "Timestamp",
                "datetime NOT NULL CONSTRAINT [DF_RecommendationPurchase_Timestamp] DEFAULT (getutcdate())");

            CreateIndexIfMissing(
                migrationBuilder,
                "RecommendationAddToCart",
                "IX_RecommendationAddToCart_Group_Timestamp",
                "[ExperimentGroup], [Timestamp] DESC");
            CreateIndexIfMissing(
                migrationBuilder,
                "RecommendationAddToCart",
                "IX_RecommendationAddToCart_Product_Timestamp",
                "[ProductId], [Timestamp] DESC");
            CreateIndexIfMissing(
                migrationBuilder,
                "RecommendationAddToCart",
                "IX_RecommendationAddToCart_User_Timestamp",
                "[UserId], [Timestamp] DESC");
            CreateIndexIfMissing(
                migrationBuilder,
                "RecommendationPurchase",
                "IX_RecommendationPurchase_Group_Timestamp",
                "[ExperimentGroup], [Timestamp] DESC");
            CreateIndexIfMissing(
                migrationBuilder,
                "RecommendationPurchase",
                "IX_RecommendationPurchase_Product_Timestamp",
                "[ProductId], [Timestamp] DESC");
            CreateIndexIfMissing(
                migrationBuilder,
                "RecommendationPurchase",
                "IX_RecommendationPurchase_User_Timestamp",
                "[UserId], [Timestamp] DESC");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql(
                """
                IF OBJECT_ID(N'[dbo].[RecommendationPurchase]', N'U') IS NOT NULL
                BEGIN
                    DROP TABLE [dbo].[RecommendationPurchase];
                END;
                """);

            migrationBuilder.Sql(
                """
                IF OBJECT_ID(N'[dbo].[RecommendationAddToCart]', N'U') IS NOT NULL
                BEGIN
                    DROP TABLE [dbo].[RecommendationAddToCart];
                END;
                """);
        }

        private static void EnsureColumnIfMissing(
            MigrationBuilder migrationBuilder,
            string tableName,
            string columnName,
            string columnSql)
        {
            migrationBuilder.Sql($$"""
                IF OBJECT_ID(N'[dbo].[{{tableName}}]', N'U') IS NOT NULL
                    AND COL_LENGTH(N'[dbo].[{{tableName}}]', N'{{columnName}}') IS NULL
                BEGIN
                    ALTER TABLE [dbo].[{{tableName}}]
                    ADD [{{columnName}}] {{columnSql}};
                END;
                """);
        }

        private static void CreateIndexIfMissing(
            MigrationBuilder migrationBuilder,
            string tableName,
            string indexName,
            string columnSql)
        {
            migrationBuilder.Sql($$"""
                IF OBJECT_ID(N'[dbo].[{{tableName}}]', N'U') IS NOT NULL
                    AND NOT EXISTS (
                        SELECT 1
                        FROM sys.indexes
                        WHERE name = N'{{indexName}}'
                            AND object_id = OBJECT_ID(N'[dbo].[{{tableName}}]', N'U'))
                BEGIN
                    CREATE INDEX [{{indexName}}]
                    ON [dbo].[{{tableName}}] ({{columnSql}});
                END;
                """);
        }
    }
}
