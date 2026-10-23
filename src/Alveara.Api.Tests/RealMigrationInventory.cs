namespace Alveara.Api.Tests;

/// <summary>How a test class is listed: where its database comes from, and why.</summary>
public enum RealMigrationReason
{
    /// <summary>Its subject is the migration pipeline starting from an EMPTY database, so its database must be created and migrated by the real path (no template).</summary>
    MigratesFromAnEmptyDatabase,

    /// <summary>It tests the fixture itself: initialisation and its failure paths, teardown, retries or file locations (no template).</summary>
    TestsTheFixtureItself,

    /// <summary>It rolls the schema back to an earlier migration and forward again THROUGH THE REAL MIGRATOR. Its database starts as a clone of the run's template (which the real migrations built and verified once), and the
    /// rollback and the upgrade it performs are the real Down and Up migrations. Listed by name so that its use of the real migrator stays visible and guarded.</summary>
    RollsTheSchemaBackAndForwardOnAClone,
}

/// <summary>One entry of the inventory.</summary>
public sealed record RealMigrationEntry(Type TestClass, RealMigrationReason Reason, bool CallsMigratorDirectly)
{
    /// <summary>True when the class bypasses the template: its database is created and migrated by the real create-and-migrate path.</summary>
    public bool UsesRealCreateAndMigrate => Reason != RealMigrationReason.RollsTheSchemaBackAndForwardOnAClone;
}

/// <summary>
/// ALV-N016: the explicit, named inventory of the test classes whose relation to the real migration path is not the default. A class is here because its subject is the migration pipeline or the fixture itself
/// (it bypasses the template and keeps the real create-and-migrate path), or because it rolls the schema back and forward through the real migrator (it starts from a clone and the real Down and Up migrations
/// still run). Every other test restores a clone of the run's template. <c>RealMigrationInventoryTests</c> proves the list is complete (no class outside it uses the real path, no class that calls the migrator is
/// missing from it) and that each entry is in the group it is listed in.
/// </summary>
public static class RealMigrationInventory
{
    public static readonly IReadOnlyList<RealMigrationEntry> All =
    [
        // Real create-and-migrate path: the migration pipeline from an empty database.
        new(typeof(MigrationTests), RealMigrationReason.MigratesFromAnEmptyDatabase, true),
        new(typeof(MigrationUpgradeTests), RealMigrationReason.MigratesFromAnEmptyDatabase, true),

        // Real create-and-migrate path: the fixture itself (initialisation and its failures, teardown, retries, file locations).
        new(typeof(TestDatabaseInitFailureCleanupTests), RealMigrationReason.TestsTheFixtureItself, false),
        new(typeof(TestDatabaseLocationTests), RealMigrationReason.TestsTheFixtureItself, false),
        new(typeof(TestDatabaseTeardownTests), RealMigrationReason.TestsTheFixtureItself, false),
        new(typeof(TestDatabaseTransientRetryTests), RealMigrationReason.TestsTheFixtureItself, false),

        // Starts from a clone; calls IMigrator.MigrateAsync with an earlier target and then Database.MigrateAsync: the real Down and Up migrations.
        new(typeof(BillingRefundMigrationTests), RealMigrationReason.RollsTheSchemaBackAndForwardOnAClone, true),
        new(typeof(CompletedProcedureMigrationTests), RealMigrationReason.RollsTheSchemaBackAndForwardOnAClone, true),
        new(typeof(CompletionAdoptionSchemaTests), RealMigrationReason.RollsTheSchemaBackAndForwardOnAClone, true),
        new(typeof(CompletionChartEvidenceTests), RealMigrationReason.RollsTheSchemaBackAndForwardOnAClone, true),
        new(typeof(DiagnosisMigrationTests), RealMigrationReason.RollsTheSchemaBackAndForwardOnAClone, true),
        new(typeof(DiagnosisStructureMigrationTests), RealMigrationReason.RollsTheSchemaBackAndForwardOnAClone, true),
        new(typeof(DocumentLibrarySchemaTests), RealMigrationReason.RollsTheSchemaBackAndForwardOnAClone, true),
        new(typeof(DocumentSchemaTests), RealMigrationReason.RollsTheSchemaBackAndForwardOnAClone, true),
        new(typeof(NotificationSchemaTests), RealMigrationReason.RollsTheSchemaBackAndForwardOnAClone, true),
        new(typeof(PermissionPolicySchemaTests), RealMigrationReason.RollsTheSchemaBackAndForwardOnAClone, true),
        new(typeof(OdontogramCatalogueMigrationTests), RealMigrationReason.RollsTheSchemaBackAndForwardOnAClone, true),
        new(typeof(PerioMigrationTests), RealMigrationReason.RollsTheSchemaBackAndForwardOnAClone, true),
        new(typeof(PrescriptionSchemaTests), RealMigrationReason.RollsTheSchemaBackAndForwardOnAClone, true),
        new(typeof(PrescriptionWorkflowSchemaTests), RealMigrationReason.RollsTheSchemaBackAndForwardOnAClone, true),
        new(typeof(TreatmentPlanRevisionMigrationTests), RealMigrationReason.RollsTheSchemaBackAndForwardOnAClone, true),
    ];

    /// <summary>
    /// Classes that call the migrator but never use <c>TestDatabaseFixture</c>: they create and drop their own database (with their own migrations assembly), so the template is not involved at all.
    /// </summary>
    public static readonly IReadOnlyList<Type> OwnDatabaseNotAFixtureUser = [typeof(MigrationFailureTests)];
}
