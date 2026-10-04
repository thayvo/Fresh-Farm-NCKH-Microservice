using FreshFarm.Ordering.Api.Models;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace FreshFarm.Ordering.Api.Migrations
{
    /// <inheritdoc />
    [DbContext(typeof(FreshFarmOrderingDBContext))]
    [Migration("20260428131500_RecommendationMetricsExperimentGroup20260428")]
    public partial class RecommendationMetricsExperimentGroup20260428 : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql(
                """
                IF OBJECT_ID(N'[dbo].[RecommendationImpression]', N'U') IS NOT NULL
                    AND COL_LENGTH(N'[dbo].[RecommendationImpression]', N'ExperimentGroup') IS NULL
                BEGIN
                    ALTER TABLE [dbo].[RecommendationImpression]
                    ADD [ExperimentGroup] nvarchar(10) NULL;
                END;
                """);

            migrationBuilder.Sql(
                """
                IF OBJECT_ID(N'[dbo].[RecommendationClick]', N'U') IS NOT NULL
                    AND COL_LENGTH(N'[dbo].[RecommendationClick]', N'ExperimentGroup') IS NULL
                BEGIN
                    ALTER TABLE [dbo].[RecommendationClick]
                    ADD [ExperimentGroup] nvarchar(10) NULL;
                END;
                """);

            migrationBuilder.Sql(
                """
                IF OBJECT_ID(N'[dbo].[RecommendationImpression]', N'U') IS NOT NULL
                    AND COL_LENGTH(N'[dbo].[RecommendationImpression]', N'ExperimentGroup') IS NOT NULL
                    AND NOT EXISTS (
                        SELECT 1
                        FROM sys.indexes
                        WHERE name = N'IX_RecommendationImpression_Source_Group_Timestamp'
                            AND object_id = OBJECT_ID(N'[dbo].[RecommendationImpression]', N'U'))
                BEGIN
                    CREATE INDEX [IX_RecommendationImpression_Source_Group_Timestamp]
                    ON [dbo].[RecommendationImpression] ([RecommendationSource], [ExperimentGroup], [Timestamp] DESC);
                END;
                """);

            migrationBuilder.Sql(
                """
                IF OBJECT_ID(N'[dbo].[RecommendationClick]', N'U') IS NOT NULL
                    AND COL_LENGTH(N'[dbo].[RecommendationClick]', N'ExperimentGroup') IS NOT NULL
                    AND NOT EXISTS (
                        SELECT 1
                        FROM sys.indexes
                        WHERE name = N'IX_RecommendationClick_Group_Timestamp'
                            AND object_id = OBJECT_ID(N'[dbo].[RecommendationClick]', N'U'))
                BEGIN
                    CREATE INDEX [IX_RecommendationClick_Group_Timestamp]
                    ON [dbo].[RecommendationClick] ([ExperimentGroup], [Timestamp] DESC);
                END;
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_RecommendationImpression_Source_Group_Timestamp",
                table: "RecommendationImpression");

            migrationBuilder.DropIndex(
                name: "IX_RecommendationClick_Group_Timestamp",
                table: "RecommendationClick");

            migrationBuilder.DropColumn(
                name: "ExperimentGroup",
                table: "RecommendationImpression");

            migrationBuilder.DropColumn(
                name: "ExperimentGroup",
                table: "RecommendationClick");
        }
    }
}
