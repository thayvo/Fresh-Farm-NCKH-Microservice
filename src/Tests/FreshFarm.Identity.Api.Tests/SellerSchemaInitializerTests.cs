using FreshFarm.Identity.Api.Services;
using Xunit;

namespace FreshFarm.Identity.Api.Tests;

public sealed class SellerSchemaInitializerTests
{
    [Fact]
    public void BuildSqlScripts_IncludesSellerStoreSettingsBootstrapBeforeKycBootstrap()
    {
        var scripts = SellerSchemaInitializer.BuildSqlScripts();

        Assert.NotEmpty(scripts);
        Assert.Contains(scripts, script => script.Contains("CREATE TABLE [dbo].[SellerStoreSettings]", StringComparison.Ordinal));
        Assert.Contains(scripts, script => script.Contains("COL_LENGTH(N'dbo.SellerStoreSettings', N'GhnPickupPhone')", StringComparison.Ordinal));
        Assert.Contains(scripts, script => script.Contains("CREATE TABLE [dbo].[SellerKycProfiles]", StringComparison.Ordinal));

        var sellerStoreIndex = Array.FindIndex(scripts.ToArray(), script => script.Contains("CREATE TABLE [dbo].[SellerStoreSettings]", StringComparison.Ordinal));
        var sellerKycIndex = Array.FindIndex(scripts.ToArray(), script => script.Contains("CREATE TABLE [dbo].[SellerKycProfiles]", StringComparison.Ordinal));
        Assert.True(sellerStoreIndex >= 0 && sellerKycIndex > sellerStoreIndex);
    }

    [Fact]
    public void BuildSqlScripts_IncludesNonDestructiveGuards_ForSellerStoreSettingsConstraints()
    {
        var scripts = SellerSchemaInitializer.BuildSqlScripts();

        Assert.Contains(scripts, script =>
            script.Contains("UX_SellerStoreSettings_UserId", StringComparison.Ordinal) &&
            script.Contains("HAVING COUNT(*) > 1", StringComparison.Ordinal));
        Assert.Contains(scripts, script =>
            script.Contains("FK_SellerStoreSettings_Users", StringComparison.Ordinal) &&
            script.Contains("LEFT JOIN [dbo].[Users]", StringComparison.Ordinal));
        Assert.Contains(scripts, script =>
            script.Contains("COALESCE(NULLIF(LTRIM(RTRIM([GhnPickupPhone]))", StringComparison.Ordinal));
    }

    [Fact]
    public void ShouldRunBootstrapScripts_ReturnsFalse_WhenIdentityBaselineMigrationAlreadyApplied()
    {
        var shouldRun = SellerSchemaInitializer.ShouldRunBootstrapScripts(
            hasAppliedBaselineMigration: true,
            diagnostics: CreateHealthyDiagnostics());

        Assert.False(shouldRun);
    }

    [Fact]
    public void ShouldRunBootstrapScripts_ReturnsFalse_WhenIdentityBaselineIsMissing_ButSellerSchemaAlreadyExists()
    {
        var shouldRun = SellerSchemaInitializer.ShouldRunBootstrapScripts(
            hasAppliedBaselineMigration: false,
            diagnostics: CreateHealthyDiagnostics());

        Assert.False(shouldRun);
    }

    [Fact]
    public void ShouldRunBootstrapScripts_ReturnsTrue_WhenIdentityBaselineIsMissing_AndSellerSchemaIsIncomplete()
    {
        var shouldRun = SellerSchemaInitializer.ShouldRunBootstrapScripts(
            hasAppliedBaselineMigration: false,
            diagnostics: CreateDiagnostics(
                sellerStoreSettingsExists: true,
                sellerKycProfilesExists: false,
                sellerKycReviewEventsExists: false));

        Assert.True(shouldRun);
    }

    [Fact]
    public void BuildWarnings_ReturnsEmpty_ForHealthySellerSchemaDiagnostics()
    {
        var diagnostics = new SellerSchemaInitializer.SellerSchemaBootstrapDiagnostics(
            SellerStoreSettingsExists: true,
            SellerStoreSettingsRows: 4,
            DuplicateUserGroups: 0,
            OrphanUserRows: 0,
            UserIdIndexPresent: true,
            UserForeignKeyPresent: true,
            MissingAdminNotificationEmailRows: 0,
            MissingGhnPickupPhoneRows: 0,
            SellerKycProfilesExists: true,
            SellerKycProfilesRows: 2,
            SellerKycReviewEventsExists: true,
            SellerKycReviewEventsRows: 5);

        var warnings = SellerSchemaInitializer.BuildWarnings(diagnostics);

        Assert.Empty(warnings);
    }

    [Fact]
    public void BuildWarnings_FlagsDuplicateOrphanAndMissingConstraintIssues()
    {
        var diagnostics = CreateDiagnostics(
            duplicateUserGroups: 2,
            orphanUserRows: 3,
            userIdIndexPresent: false,
            userForeignKeyPresent: false,
            missingAdminNotificationEmailRows: 1,
            missingGhnPickupPhoneRows: 4,
            sellerKycProfilesExists: false,
            sellerKycProfilesRows: 0,
            sellerKycReviewEventsExists: false,
            sellerKycReviewEventsRows: 0);

        var warnings = SellerSchemaInitializer.BuildWarnings(diagnostics);

        Assert.Contains(warnings, warning => warning.Contains("duplicate UserId group", StringComparison.OrdinalIgnoreCase));
        Assert.Contains(warnings, warning => warning.Contains("orphan row", StringComparison.OrdinalIgnoreCase));
        Assert.Contains(warnings, warning => warning.Contains("UX_SellerStoreSettings_UserId", StringComparison.Ordinal));
        Assert.Contains(warnings, warning => warning.Contains("FK_SellerStoreSettings_Users", StringComparison.Ordinal));
        Assert.Contains(warnings, warning => warning.Contains("AdminNotificationEmail", StringComparison.Ordinal));
        Assert.Contains(warnings, warning => warning.Contains("GhnPickupPhone", StringComparison.Ordinal));
        Assert.Contains(warnings, warning => warning.Contains("SellerKycProfiles", StringComparison.Ordinal));
        Assert.Contains(warnings, warning => warning.Contains("SellerKycReviewEvents", StringComparison.Ordinal));
    }

    private static SellerSchemaInitializer.SellerSchemaBootstrapDiagnostics CreateHealthyDiagnostics()
        => CreateDiagnostics();

    private static SellerSchemaInitializer.SellerSchemaBootstrapDiagnostics CreateDiagnostics(
        bool sellerStoreSettingsExists = true,
        long sellerStoreSettingsRows = 4,
        long duplicateUserGroups = 0,
        long orphanUserRows = 0,
        bool userIdIndexPresent = true,
        bool userForeignKeyPresent = true,
        long missingAdminNotificationEmailRows = 0,
        long missingGhnPickupPhoneRows = 0,
        bool sellerKycProfilesExists = true,
        long sellerKycProfilesRows = 2,
        bool sellerKycReviewEventsExists = true,
        long sellerKycReviewEventsRows = 5)
        => new(
            SellerStoreSettingsExists: sellerStoreSettingsExists,
            SellerStoreSettingsRows: sellerStoreSettingsRows,
            DuplicateUserGroups: duplicateUserGroups,
            OrphanUserRows: orphanUserRows,
            UserIdIndexPresent: userIdIndexPresent,
            UserForeignKeyPresent: userForeignKeyPresent,
            MissingAdminNotificationEmailRows: missingAdminNotificationEmailRows,
            MissingGhnPickupPhoneRows: missingGhnPickupPhoneRows,
            SellerKycProfilesExists: sellerKycProfilesExists,
            SellerKycProfilesRows: sellerKycProfilesRows,
            SellerKycReviewEventsExists: sellerKycReviewEventsExists,
            SellerKycReviewEventsRows: sellerKycReviewEventsRows);
}
