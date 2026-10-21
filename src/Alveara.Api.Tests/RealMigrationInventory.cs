namespace Alveara.Api.Tests;

/// <summary>Why a test class keeps the real create-and-migrate path.</summary>
public enum RealMigrationReason
{
    /// <summary>It rolls the schema back to an earlier migration and forward again (or runs a migration against data), so real migrations must be what built the database it starts from.</summary>
    RollsTheSchemaBackAndForward,

    /// <summary>It tests the fixture itself: initialisation and its failure paths, teardown, retries or file locations.</summary>
    TestsTheFixtureItself,
}

/// <summary>One entry of the inventory.</summary>
public sealed record RealMigrationEntry(Type TestClass, RealMigrationReason Reason, bool CallsMigratorDirectly);

/// <summary>
/// ALV-N016: the explicit, named inventory of the test classes that bypass the template database and keep the real create-and-migrate path. A class is here because its subject is the migration
/// pipeline or the fixture; every other test restores a clone of the run's template. <c>RealMigrationInventoryTests</c> proves the list is complete (no class outside it uses the real path, no source file
/// that calls the migrator is missing) and that each entry still does what it is listed for.
/// </summary>
public static class RealMigrationInventory
{
    public static readonly IReadOnlyList<RealMigrationEntry> All =
    [
        // Rolls the schema back and forward (calls IMigrator.MigrateAsync with an earlier target, then Database.MigrateAsync).
        new(typeof(BillingRefundMigrationTests), RealMigrationReason.RollsTheSchemaBackAndForward, true),
        new(typeof(CompletedProcedureMigrationTests), RealMigrationReason.RollsTheSchemaBackAndForward, true),
        new(typeof(CompletionAdoptionSchemaTests), RealMigrationReason.RollsTheSchemaBackAndForward, true),
        new(typeof(CompletionChartEvidenceTests), RealMigrationReason.RollsTheSchemaBackAndForward, true),
        new(typeof(DiagnosisMigrationTests), RealMigrationReason.RollsTheSchemaBackAndForward, true),
        new(typeof(DiagnosisStructureMigrationTests), RealMigrationReason.RollsTheSchemaBackAndForward, true),
        new(typeof(DocumentLibrarySchemaTests), RealMigrationReason.RollsTheSchemaBackAndForward, true),
        new(typeof(DocumentSchemaTests), RealMigrationReason.RollsTheSchemaBackAndForward, true),
        new(typeof(MigrationTests), RealMigrationReason.RollsTheSchemaBackAndForward, true),
        new(typeof(MigrationUpgradeTests), RealMigrationReason.RollsTheSchemaBackAndForward, true),
        new(typeof(OdontogramCatalogueMigrationTests), RealMigrationReason.RollsTheSchemaBackAndForward, true),
        new(typeof(PerioMigrationTests), RealMigrationReason.RollsTheSchemaBackAndForward, true),
        new(typeof(PrescriptionSchemaTests), RealMigrationReason.RollsTheSchemaBackAndForward, true),
        new(typeof(PrescriptionWorkflowSchemaTests), RealMigrationReason.RollsTheSchemaBackAndForward, true),
        new(typeof(TreatmentPlanRevisionMigrationTests), RealMigrationReason.RollsTheSchemaBackAndForward, true),

        // Tests the fixture itself (initialisation and its failures, teardown, retries, file locations).
        new(typeof(TestDatabaseInitFailureCleanupTests), RealMigrationReason.TestsTheFixtureItself, false),
        new(typeof(TestDatabaseLocationTests), RealMigrationReason.TestsTheFixtureItself, false),
        new(typeof(TestDatabaseTeardownTests), RealMigrationReason.TestsTheFixtureItself, false),
        new(typeof(TestDatabaseTransientRetryTests), RealMigrationReason.TestsTheFixtureItself, false),
    ];

    /// <summary>
    /// Classes that call the migrator but never use <c>TestDatabaseFixture</c>: they create and drop their own database (with their own migrations assembly), so the template is not involved at all.
    /// </summary>
    public static readonly IReadOnlyList<Type> OwnDatabaseNotAFixtureUser = [typeof(MigrationFailureTests)];
}
