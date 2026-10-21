namespace Alveara.Api.Tests;

/// <summary>
/// ALV-N016: a fixture that keeps the real create-and-migrate path (no template). It is used by the named inventory of <see cref="RealMigrationInventory"/>: the tests that roll the schema back and forward,
/// and the tests of the fixture's own initialisation, teardown and file locations. Everything else restores a clone of the run's template.
/// </summary>
public sealed class RealMigrationTestDatabaseFixture : TestDatabaseFixture
{
    public RealMigrationTestDatabaseFixture() : base(useTemplate: false) { }
}
