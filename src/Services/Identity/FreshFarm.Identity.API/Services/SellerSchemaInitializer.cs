using FreshFarm.Identity.Api.Models;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using System.Data;
using System.Data.Common;

namespace FreshFarm.Identity.Api.Services;

public static class SellerSchemaInitializer
{
    internal const string IdentityBaselineMigrationId = "20260324135630_IdentitySchemaBaseline20260324";

    public static async Task EnsureCreatedAsync(IServiceProvider serviceProvider, CancellationToken cancellationToken = default)
    {
        await using var scope = serviceProvider.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<FreshFarmIdentityDBContext>();
        var loggerFactory = scope.ServiceProvider.GetRequiredService<ILoggerFactory>();
        var logger = loggerFactory.CreateLogger("FreshFarm.Identity.Api.Services.SellerSchemaInitializer");
        var hasAppliedBaselineMigration = await HasAppliedBaselineMigrationAsync(db, cancellationToken);
        var diagnostics = await CollectDiagnosticsAsync(db, cancellationToken);
        var shouldRunBootstrapScripts = ShouldRunBootstrapScripts(hasAppliedBaselineMigration, diagnostics);

        if (shouldRunBootstrapScripts)
        {
            foreach (var script in BuildSqlScripts())
            {
                await db.Database.ExecuteSqlRawAsync(script, cancellationToken);
            }

            diagnostics = await CollectDiagnosticsAsync(db, cancellationToken);
        }

        var warnings = BuildWarnings(diagnostics);
        var executionMode = hasAppliedBaselineMigration || !shouldRunBootstrapScripts
            ? "diagnostics-only"
            : "bootstrap-and-diagnostics";

        if (warnings.Count == 0)
        {
            logger.LogInformation(
                "Seller schema startup completed in {ExecutionMode} mode. SellerStoreSettingsRows={SellerStoreSettingsRows}, SellerKycProfilesRows={SellerKycProfilesRows}, SellerKycReviewEventsRows={SellerKycReviewEventsRows}",
                executionMode,
                diagnostics.SellerStoreSettingsRows,
                diagnostics.SellerKycProfilesRows,
                diagnostics.SellerKycReviewEventsRows);

            return;
        }

        logger.LogWarning(
            "Seller schema startup completed in {ExecutionMode} mode with warnings: {Warnings}. SellerStoreSettingsRows={SellerStoreSettingsRows}, DuplicateUserGroups={DuplicateUserGroups}, OrphanUserRows={OrphanUserRows}, SellerKycProfilesRows={SellerKycProfilesRows}, SellerKycReviewEventsRows={SellerKycReviewEventsRows}",
            executionMode,
            string.Join(" | ", warnings),
            diagnostics.SellerStoreSettingsRows,
            diagnostics.DuplicateUserGroups,
            diagnostics.OrphanUserRows,
            diagnostics.SellerKycProfilesRows,
            diagnostics.SellerKycReviewEventsRows);
    }

    internal static bool ShouldRunBootstrapScripts(
        bool hasAppliedBaselineMigration,
        SellerSchemaBootstrapDiagnostics diagnostics)
        => !hasAppliedBaselineMigration && !HasRequiredSellerSchema(diagnostics);

    internal static bool HasRequiredSellerSchema(SellerSchemaBootstrapDiagnostics diagnostics)
        => diagnostics.SellerStoreSettingsExists
        && diagnostics.SellerKycProfilesExists
        && diagnostics.SellerKycReviewEventsExists;

    internal static IReadOnlyList<string> BuildSqlScripts()
    {
        return
        [
            CreateSellerStoreSettingsTableSql,
            EnsureSellerStoreSettingsColumnsSql,
            NormalizeSellerStoreSettingsSql,
            EnsureSellerStoreSettingsUserIndexSql,
            EnsureSellerStoreSettingsUserForeignKeySql,
            CreateSellerKycProfilesTableSql,
            EnsureReviewColumnsSql,
            NormalizeReviewStatusSql,
            CreateReviewEventsTableSql
        ];
    }

    internal static IReadOnlyList<string> BuildWarnings(SellerSchemaBootstrapDiagnostics diagnostics)
    {
        var warnings = new List<string>();

        if (!diagnostics.SellerStoreSettingsExists)
        {
            warnings.Add("SellerStoreSettings table is missing after bootstrap.");
        }

        if (diagnostics.DuplicateUserGroups > 0)
        {
            warnings.Add($"SellerStoreSettings has {diagnostics.DuplicateUserGroups} duplicate UserId group(s).");
        }

        if (!diagnostics.UserIdIndexPresent)
        {
            warnings.Add("UX_SellerStoreSettings_UserId is missing.");
        }

        if (diagnostics.OrphanUserRows > 0)
        {
            warnings.Add($"SellerStoreSettings has {diagnostics.OrphanUserRows} orphan row(s) without Users.UserId.");
        }

        if (!diagnostics.UserForeignKeyPresent)
        {
            warnings.Add("FK_SellerStoreSettings_Users is missing.");
        }

        if (diagnostics.MissingAdminNotificationEmailRows > 0)
        {
            warnings.Add($"SellerStoreSettings still has {diagnostics.MissingAdminNotificationEmailRows} row(s) missing AdminNotificationEmail.");
        }

        if (diagnostics.MissingGhnPickupPhoneRows > 0)
        {
            warnings.Add($"SellerStoreSettings still has {diagnostics.MissingGhnPickupPhoneRows} row(s) missing GhnPickupPhone.");
        }

        if (!diagnostics.SellerKycProfilesExists)
        {
            warnings.Add("SellerKycProfiles table is missing after bootstrap.");
        }

        if (!diagnostics.SellerKycReviewEventsExists)
        {
            warnings.Add("SellerKycReviewEvents table is missing after bootstrap.");
        }

        return warnings;
    }

    internal static async Task<bool> HasAppliedBaselineMigrationAsync(
        FreshFarmIdentityDBContext db,
        CancellationToken cancellationToken = default)
    {
        var connection = db.Database.GetDbConnection();
        var shouldClose = connection.State != ConnectionState.Open;

        if (shouldClose)
        {
            await connection.OpenAsync(cancellationToken);
        }

        try
        {
            await using var command = connection.CreateCommand();
            command.CommandText = """
                SELECT CAST(CASE
                    WHEN OBJECT_ID(N'dbo.__EFMigrationsHistory', N'U') IS NULL THEN 0
                    WHEN EXISTS (
                        SELECT 1
                        FROM [dbo].[__EFMigrationsHistory]
                        WHERE [MigrationId] = @migrationId
                    ) THEN 1
                    ELSE 0
                END AS bit);
                """;

            var parameter = command.CreateParameter();
            parameter.ParameterName = "@migrationId";
            parameter.DbType = DbType.String;
            parameter.Value = IdentityBaselineMigrationId;
            command.Parameters.Add(parameter);

            var result = await command.ExecuteScalarAsync(cancellationToken);
            return result is bool hasApplied && hasApplied;
        }
        finally
        {
            if (shouldClose)
            {
                await connection.CloseAsync();
            }
        }
    }

    internal static async Task<SellerSchemaBootstrapDiagnostics> CollectDiagnosticsAsync(
        FreshFarmIdentityDBContext db,
        CancellationToken cancellationToken = default)
    {
        var connection = db.Database.GetDbConnection();
        var shouldClose = connection.State != ConnectionState.Open;

        if (shouldClose)
        {
            await connection.OpenAsync(cancellationToken);
        }

        try
        {
            var sellerStoreSettings = await ReadSellerStoreSettingsDiagnosticsAsync(connection, cancellationToken);
            var kyc = await ReadSellerKycDiagnosticsAsync(connection, cancellationToken);

            return new SellerSchemaBootstrapDiagnostics(
                sellerStoreSettings.SellerStoreSettingsExists,
                sellerStoreSettings.SellerStoreSettingsRows,
                sellerStoreSettings.DuplicateUserGroups,
                sellerStoreSettings.OrphanUserRows,
                sellerStoreSettings.UserIdIndexPresent,
                sellerStoreSettings.UserForeignKeyPresent,
                sellerStoreSettings.MissingAdminNotificationEmailRows,
                sellerStoreSettings.MissingGhnPickupPhoneRows,
                kyc.SellerKycProfilesExists,
                kyc.SellerKycProfilesRows,
                kyc.SellerKycReviewEventsExists,
                kyc.SellerKycReviewEventsRows);
        }
        finally
        {
            if (shouldClose)
            {
                await connection.CloseAsync();
            }
        }
    }

    private static async Task<SellerStoreSettingsDiagnostics> ReadSellerStoreSettingsDiagnosticsAsync(
        DbConnection connection,
        CancellationToken cancellationToken)
    {
        await using var command = connection.CreateCommand();
        command.CommandText = """
            IF OBJECT_ID(N'dbo.SellerStoreSettings', N'U') IS NULL
            BEGIN
                SELECT
                    CAST(0 AS bit) AS [SellerStoreSettingsExists],
                    CAST(0 AS bigint) AS [SellerStoreSettingsRows],
                    CAST(0 AS bigint) AS [DuplicateUserGroups],
                    CAST(0 AS bigint) AS [OrphanUserRows],
                    CAST(0 AS bit) AS [UserIdIndexPresent],
                    CAST(0 AS bit) AS [UserForeignKeyPresent],
                    CAST(0 AS bigint) AS [MissingAdminNotificationEmailRows],
                    CAST(0 AS bigint) AS [MissingGhnPickupPhoneRows];
            END
            ELSE
            BEGIN
                SELECT
                    CAST(1 AS bit) AS [SellerStoreSettingsExists],
                    COUNT_BIG(1) AS [SellerStoreSettingsRows],
                    (
                        SELECT COUNT_BIG(1)
                        FROM
                        (
                            SELECT [UserId]
                            FROM [dbo].[SellerStoreSettings]
                            GROUP BY [UserId]
                            HAVING COUNT_BIG(1) > 1
                        ) duplicate_groups
                    ) AS [DuplicateUserGroups],
                    (
                        SELECT COUNT_BIG(1)
                        FROM [dbo].[SellerStoreSettings] s
                        LEFT JOIN [dbo].[Users] u ON u.[UserId] = s.[UserId]
                        WHERE u.[UserId] IS NULL
                    ) AS [OrphanUserRows],
                    CAST(CASE WHEN EXISTS (
                        SELECT 1
                        FROM sys.indexes
                        WHERE [name] = N'UX_SellerStoreSettings_UserId'
                          AND [object_id] = OBJECT_ID(N'dbo.SellerStoreSettings'))
                        THEN 1 ELSE 0 END AS bit) AS [UserIdIndexPresent],
                    CAST(CASE WHEN EXISTS (
                        SELECT 1
                        FROM sys.foreign_keys
                        WHERE [name] = N'FK_SellerStoreSettings_Users'
                          AND [parent_object_id] = OBJECT_ID(N'dbo.SellerStoreSettings'))
                        THEN 1 ELSE 0 END AS bit) AS [UserForeignKeyPresent],
                    (
                        SELECT COUNT_BIG(1)
                        FROM [dbo].[SellerStoreSettings]
                        WHERE [AdminNotificationEmail] IS NULL OR LTRIM(RTRIM([AdminNotificationEmail])) = N''
                    ) AS [MissingAdminNotificationEmailRows],
                    (
                        SELECT COUNT_BIG(1)
                        FROM [dbo].[SellerStoreSettings]
                        WHERE [GhnPickupPhone] IS NULL OR LTRIM(RTRIM([GhnPickupPhone])) = N''
                    ) AS [MissingGhnPickupPhoneRows]
                FROM [dbo].[SellerStoreSettings];
            END
            """;

        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        await reader.ReadAsync(cancellationToken);

        return new SellerStoreSettingsDiagnostics(
            reader.GetBoolean(0),
            reader.GetInt64(1),
            reader.GetInt64(2),
            reader.GetInt64(3),
            reader.GetBoolean(4),
            reader.GetBoolean(5),
            reader.GetInt64(6),
            reader.GetInt64(7));
    }

    private static async Task<SellerKycDiagnostics> ReadSellerKycDiagnosticsAsync(
        DbConnection connection,
        CancellationToken cancellationToken)
    {
        await using var command = connection.CreateCommand();
        command.CommandText = """
            SELECT
                CAST(CASE WHEN OBJECT_ID(N'dbo.SellerKycProfiles', N'U') IS NULL THEN 0 ELSE 1 END AS bit) AS [SellerKycProfilesExists],
                CAST(CASE
                    WHEN OBJECT_ID(N'dbo.SellerKycProfiles', N'U') IS NULL THEN 0
                    ELSE (SELECT COUNT_BIG(1) FROM [dbo].[SellerKycProfiles])
                END AS bigint) AS [SellerKycProfilesRows],
                CAST(CASE WHEN OBJECT_ID(N'dbo.SellerKycReviewEvents', N'U') IS NULL THEN 0 ELSE 1 END AS bit) AS [SellerKycReviewEventsExists],
                CAST(CASE
                    WHEN OBJECT_ID(N'dbo.SellerKycReviewEvents', N'U') IS NULL THEN 0
                    ELSE (SELECT COUNT_BIG(1) FROM [dbo].[SellerKycReviewEvents])
                END AS bigint) AS [SellerKycReviewEventsRows];
            """;

        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        await reader.ReadAsync(cancellationToken);

        return new SellerKycDiagnostics(
            reader.GetBoolean(0),
            reader.GetInt64(1),
            reader.GetBoolean(2),
            reader.GetInt64(3));
    }

    internal sealed record SellerSchemaBootstrapDiagnostics(
        bool SellerStoreSettingsExists,
        long SellerStoreSettingsRows,
        long DuplicateUserGroups,
        long OrphanUserRows,
        bool UserIdIndexPresent,
        bool UserForeignKeyPresent,
        long MissingAdminNotificationEmailRows,
        long MissingGhnPickupPhoneRows,
        bool SellerKycProfilesExists,
        long SellerKycProfilesRows,
        bool SellerKycReviewEventsExists,
        long SellerKycReviewEventsRows);

    private sealed record SellerStoreSettingsDiagnostics(
        bool SellerStoreSettingsExists,
        long SellerStoreSettingsRows,
        long DuplicateUserGroups,
        long OrphanUserRows,
        bool UserIdIndexPresent,
        bool UserForeignKeyPresent,
        long MissingAdminNotificationEmailRows,
        long MissingGhnPickupPhoneRows);

    private sealed record SellerKycDiagnostics(
        bool SellerKycProfilesExists,
        long SellerKycProfilesRows,
        bool SellerKycReviewEventsExists,
        long SellerKycReviewEventsRows);

    private const string CreateSellerStoreSettingsTableSql = """
        IF OBJECT_ID(N'dbo.SellerStoreSettings', N'U') IS NULL
        BEGIN
            CREATE TABLE [dbo].[SellerStoreSettings]
            (
                [SellerStoreSettingId] INT IDENTITY(1,1) NOT NULL CONSTRAINT [PK_SellerStoreSettings] PRIMARY KEY,
                [UserId] INT NOT NULL,
                [StoreName] NVARCHAR(255) NOT NULL,
                [StoreAddress] NVARCHAR(500) NOT NULL,
                [StoreEmail] NVARCHAR(100) NOT NULL,
                [StorePhone] NVARCHAR(20) NOT NULL,
                [IsCODEnabled] BIT NOT NULL CONSTRAINT [DF_SellerStoreSettings_IsCODEnabled] DEFAULT ((1)),
                [BankTransferInstructions] NVARCHAR(MAX) NULL,
                [BankAccountInfo] NVARCHAR(MAX) NULL,
                [DefaultShippingFee] DECIMAL(10,2) NOT NULL CONSTRAINT [DF_SellerStoreSettings_DefaultShippingFee] DEFAULT ((30000)),
                [FreeShippingThreshold] DECIMAL(10,2) NOT NULL CONSTRAINT [DF_SellerStoreSettings_FreeShippingThreshold] DEFAULT ((500000)),
                [IsEmailNewOrderEnabled] BIT NOT NULL CONSTRAINT [DF_SellerStoreSettings_IsEmailNewOrderEnabled] DEFAULT ((1)),
                [IsEmailDeliveredEnabled] BIT NOT NULL CONSTRAINT [DF_SellerStoreSettings_IsEmailDeliveredEnabled] DEFAULT ((1)),
                [IsEmailCancelledEnabled] BIT NOT NULL CONSTRAINT [DF_SellerStoreSettings_IsEmailCancelledEnabled] DEFAULT ((1)),
                [AdminNotificationEmail] NVARCHAR(100) NOT NULL,
                [GhnPickupName] NVARCHAR(150) NULL,
                [GhnPickupPhone] NVARCHAR(20) NULL,
                [GhnPickupAddress] NVARCHAR(500) NULL,
                [GhnProvinceId] INT NULL,
                [GhnProvinceName] NVARCHAR(150) NULL,
                [GhnDistrictId] INT NULL,
                [GhnDistrictName] NVARCHAR(150) NULL,
                [GhnWardCode] NVARCHAR(50) NULL,
                [GhnWardName] NVARCHAR(150) NULL,
                [CreatedAt] DATETIME2(0) NOT NULL CONSTRAINT [DF_SellerStoreSettings_CreatedAt] DEFAULT (sysutcdatetime()),
                [UpdatedAt] DATETIME2(0) NOT NULL CONSTRAINT [DF_SellerStoreSettings_UpdatedAt] DEFAULT (sysutcdatetime()),
                CONSTRAINT [UX_SellerStoreSettings_UserId] UNIQUE ([UserId]),
                CONSTRAINT [FK_SellerStoreSettings_Users] FOREIGN KEY ([UserId]) REFERENCES [dbo].[Users]([UserId])
            );
        END
        """;

    private const string EnsureSellerStoreSettingsColumnsSql = """
        IF COL_LENGTH(N'dbo.SellerStoreSettings', N'AdminNotificationEmail') IS NULL
        BEGIN
            ALTER TABLE [dbo].[SellerStoreSettings]
            ADD [AdminNotificationEmail] NVARCHAR(100) NULL;
        END

        IF COL_LENGTH(N'dbo.SellerStoreSettings', N'GhnPickupName') IS NULL
        BEGIN
            ALTER TABLE [dbo].[SellerStoreSettings]
            ADD [GhnPickupName] NVARCHAR(150) NULL;
        END

        IF COL_LENGTH(N'dbo.SellerStoreSettings', N'GhnPickupPhone') IS NULL
        BEGIN
            ALTER TABLE [dbo].[SellerStoreSettings]
            ADD [GhnPickupPhone] NVARCHAR(20) NULL;
        END

        IF COL_LENGTH(N'dbo.SellerStoreSettings', N'GhnPickupAddress') IS NULL
        BEGIN
            ALTER TABLE [dbo].[SellerStoreSettings]
            ADD [GhnPickupAddress] NVARCHAR(500) NULL;
        END

        IF COL_LENGTH(N'dbo.SellerStoreSettings', N'GhnProvinceId') IS NULL
        BEGIN
            ALTER TABLE [dbo].[SellerStoreSettings]
            ADD [GhnProvinceId] INT NULL;
        END

        IF COL_LENGTH(N'dbo.SellerStoreSettings', N'GhnProvinceName') IS NULL
        BEGIN
            ALTER TABLE [dbo].[SellerStoreSettings]
            ADD [GhnProvinceName] NVARCHAR(150) NULL;
        END

        IF COL_LENGTH(N'dbo.SellerStoreSettings', N'GhnDistrictId') IS NULL
        BEGIN
            ALTER TABLE [dbo].[SellerStoreSettings]
            ADD [GhnDistrictId] INT NULL;
        END

        IF COL_LENGTH(N'dbo.SellerStoreSettings', N'GhnDistrictName') IS NULL
        BEGIN
            ALTER TABLE [dbo].[SellerStoreSettings]
            ADD [GhnDistrictName] NVARCHAR(150) NULL;
        END

        IF COL_LENGTH(N'dbo.SellerStoreSettings', N'GhnWardCode') IS NULL
        BEGIN
            ALTER TABLE [dbo].[SellerStoreSettings]
            ADD [GhnWardCode] NVARCHAR(50) NULL;
        END

        IF COL_LENGTH(N'dbo.SellerStoreSettings', N'GhnWardName') IS NULL
        BEGIN
            ALTER TABLE [dbo].[SellerStoreSettings]
            ADD [GhnWardName] NVARCHAR(150) NULL;
        END
        """;

    private const string NormalizeSellerStoreSettingsSql = """
        UPDATE [dbo].[SellerStoreSettings]
        SET
            [AdminNotificationEmail] = COALESCE(NULLIF(LTRIM(RTRIM([AdminNotificationEmail])), N''), NULLIF(LTRIM(RTRIM([StoreEmail])), N''), N'seller@freshfarm.local'),
            [GhnPickupName] = COALESCE(NULLIF(LTRIM(RTRIM([GhnPickupName])), N''), NULLIF(LTRIM(RTRIM([StoreName])), N'')),
            [GhnPickupPhone] = COALESCE(NULLIF(LTRIM(RTRIM([GhnPickupPhone])), N''), NULLIF(LTRIM(RTRIM([StorePhone])), N'')),
            [CreatedAt] = COALESCE([CreatedAt], sysutcdatetime()),
            [UpdatedAt] = COALESCE([UpdatedAt], sysutcdatetime())
        WHERE
            [AdminNotificationEmail] IS NULL OR LTRIM(RTRIM([AdminNotificationEmail])) = N''
            OR [GhnPickupName] IS NULL OR LTRIM(RTRIM([GhnPickupName])) = N''
            OR [GhnPickupPhone] IS NULL OR LTRIM(RTRIM([GhnPickupPhone])) = N''
            OR [CreatedAt] IS NULL
            OR [UpdatedAt] IS NULL;
        """;

    private const string EnsureSellerStoreSettingsUserIndexSql = """
        IF OBJECT_ID(N'dbo.SellerStoreSettings', N'U') IS NOT NULL
           AND NOT EXISTS (
               SELECT 1
               FROM sys.indexes
               WHERE [name] = N'UX_SellerStoreSettings_UserId'
                 AND [object_id] = OBJECT_ID(N'dbo.SellerStoreSettings'))
           AND NOT EXISTS (
               SELECT [UserId]
               FROM [dbo].[SellerStoreSettings]
               GROUP BY [UserId]
               HAVING COUNT(*) > 1)
        BEGIN
            CREATE UNIQUE INDEX [UX_SellerStoreSettings_UserId]
                ON [dbo].[SellerStoreSettings]([UserId]);
        END
        """;

    private const string EnsureSellerStoreSettingsUserForeignKeySql = """
        IF OBJECT_ID(N'dbo.SellerStoreSettings', N'U') IS NOT NULL
           AND NOT EXISTS (
               SELECT 1
               FROM sys.foreign_keys
               WHERE [name] = N'FK_SellerStoreSettings_Users'
                 AND [parent_object_id] = OBJECT_ID(N'dbo.SellerStoreSettings'))
           AND NOT EXISTS (
               SELECT 1
               FROM [dbo].[SellerStoreSettings] s
               LEFT JOIN [dbo].[Users] u ON u.[UserId] = s.[UserId]
               WHERE u.[UserId] IS NULL)
        BEGIN
            ALTER TABLE [dbo].[SellerStoreSettings]
            ADD CONSTRAINT [FK_SellerStoreSettings_Users]
                FOREIGN KEY ([UserId]) REFERENCES [dbo].[Users]([UserId]);
        END
        """;

    private const string CreateSellerKycProfilesTableSql = """
            IF OBJECT_ID(N'dbo.SellerKycProfiles', N'U') IS NULL
            BEGIN
                CREATE TABLE [dbo].[SellerKycProfiles]
                (
                    [SellerKycProfileId] INT IDENTITY(1,1) NOT NULL CONSTRAINT [PK_SellerKycProfiles] PRIMARY KEY,
                    [UserId] INT NOT NULL,
                    [LegalFullName] NVARCHAR(150) NOT NULL,
                    [IdentityNumber] NVARCHAR(50) NOT NULL,
                    [IdentityIssuedDate] DATETIME2(0) NOT NULL,
                    [IdentityIssuedPlace] NVARCHAR(255) NOT NULL,
                    [TaxCode] NVARCHAR(50) NULL,
                    [BusinessLicenseNumber] NVARCHAR(100) NULL,
                    [CitizenIdFrontUrl] NVARCHAR(500) NULL,
                    [CitizenIdBackUrl] NVARCHAR(500) NULL,
                    [BusinessLicenseUrl] NVARCHAR(500) NULL,
                    [AdditionalDocumentUrl] NVARCHAR(500) NULL,
                    [Notes] NVARCHAR(1000) NULL,
                    [ReviewStatus] NVARCHAR(30) NOT NULL CONSTRAINT [DF_SellerKycProfiles_ReviewStatus] DEFAULT (N'pending'),
                    [ReviewNote] NVARCHAR(1000) NULL,
                    [ReviewedAt] DATETIME2(0) NULL,
                    [ReviewedByUserId] INT NULL,
                    [CreatedAt] DATETIME2(0) NOT NULL CONSTRAINT [DF_SellerKycProfiles_CreatedAt] DEFAULT (sysutcdatetime()),
                    [UpdatedAt] DATETIME2(0) NOT NULL CONSTRAINT [DF_SellerKycProfiles_UpdatedAt] DEFAULT (sysutcdatetime()),
                    CONSTRAINT [UX_SellerKycProfiles_UserId] UNIQUE ([UserId]),
                    CONSTRAINT [FK_SellerKycProfiles_Users] FOREIGN KEY ([UserId]) REFERENCES [dbo].[Users]([UserId]) ON DELETE CASCADE
                );
            END
            """;

    private const string EnsureReviewColumnsSql = """
            IF COL_LENGTH(N'dbo.SellerKycProfiles', N'ReviewStatus') IS NULL
            BEGIN
                ALTER TABLE [dbo].[SellerKycProfiles]
                ADD [ReviewStatus] NVARCHAR(30) NOT NULL
                    CONSTRAINT [DF_SellerKycProfiles_ReviewStatus] DEFAULT (N'pending');
            END

            IF COL_LENGTH(N'dbo.SellerKycProfiles', N'ReviewNote') IS NULL
            BEGIN
                ALTER TABLE [dbo].[SellerKycProfiles]
                ADD [ReviewNote] NVARCHAR(1000) NULL;
            END

            IF COL_LENGTH(N'dbo.SellerKycProfiles', N'ReviewedAt') IS NULL
            BEGIN
                ALTER TABLE [dbo].[SellerKycProfiles]
                ADD [ReviewedAt] DATETIME2(0) NULL;
            END

            IF COL_LENGTH(N'dbo.SellerKycProfiles', N'ReviewedByUserId') IS NULL
            BEGIN
                ALTER TABLE [dbo].[SellerKycProfiles]
                ADD [ReviewedByUserId] INT NULL;
            END
            """;

    private const string NormalizeReviewStatusSql = """
            UPDATE [dbo].[SellerKycProfiles]
            SET [ReviewStatus] = N'pending'
            WHERE [ReviewStatus] IS NULL OR LTRIM(RTRIM([ReviewStatus])) = N'';
            """;

    private const string CreateReviewEventsTableSql = """
            IF OBJECT_ID(N'dbo.SellerKycReviewEvents', N'U') IS NULL
            BEGIN
                CREATE TABLE [dbo].[SellerKycReviewEvents]
                (
                    [SellerKycReviewEventId] INT IDENTITY(1,1) NOT NULL CONSTRAINT [PK_SellerKycReviewEvents] PRIMARY KEY,
                    [UserId] INT NOT NULL,
                    [Action] NVARCHAR(30) NOT NULL,
                    [ReviewStatus] NVARCHAR(30) NOT NULL,
                    [Note] NVARCHAR(1000) NULL,
                    [ReviewedAt] DATETIME2(0) NOT NULL CONSTRAINT [DF_SellerKycReviewEvents_ReviewedAt] DEFAULT (sysutcdatetime()),
                    [ReviewedByUserId] INT NULL,
                    [ReviewerUserName] NVARCHAR(100) NULL,
                    [ReviewerFullName] NVARCHAR(150) NULL,
                    CONSTRAINT [FK_SellerKycReviewEvents_Users] FOREIGN KEY ([UserId]) REFERENCES [dbo].[Users]([UserId]) ON DELETE CASCADE
                );

                CREATE INDEX [IX_SellerKycReviewEvents_UserId_ReviewedAt]
                    ON [dbo].[SellerKycReviewEvents]([UserId] ASC, [ReviewedAt] DESC);
            END
            """;
}
