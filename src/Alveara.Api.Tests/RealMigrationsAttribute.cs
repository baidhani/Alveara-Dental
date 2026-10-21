namespace Alveara.Api.Tests;

/// <summary>
/// ALV-N016: marks a <see cref="SafetyTestBase"/> test class that keeps the real create-and-migrate path (its fixture is a <see cref="RealMigrationTestDatabaseFixture"/> and never restores a template clone).
/// Every class carrying it must be named in <see cref="RealMigrationInventory"/>; a guard test fails otherwise.
/// </summary>
[AttributeUsage(AttributeTargets.Class, Inherited = true)]
public sealed class RealMigrationsAttribute : Attribute
{
}
