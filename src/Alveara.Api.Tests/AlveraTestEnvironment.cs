using System.Text.RegularExpressions;

namespace Alveara.Api.Tests;

/// <summary>
/// The one place where the tests learn which SQL Server to use and where their files live.
///
/// Every database file and every other file a test creates lives under ONE root (default <c>C:\.alvera-data</c>), in the <c>Test-Data</c> subfolder:
/// <c>Database</c> (the .mdf/.ldf files), <c>Backup</c> (backup sets), <c>Storage</c>, <c>Keys</c> and <c>Temp</c> (scratch). Nothing is ever left to the SQL Server instance's default folders.
///
/// Two environment variables override the defaults (for a machine that differs from the development machine): <c>ALVERA_SQL_SERVER</c> (default <c>.\SQLEXPRESS</c>) and
/// <c>ALVERA_DATA_ROOT</c> (default <c>C:\.alvera-data</c>; it must be an absolute path). The SQL Server service account must be able to read and write the folders under the root.
/// </summary>
internal static class AlveraTestEnvironment
{
    public const string SqlServerVariable = "ALVERA_SQL_SERVER";
    public const string DataRootVariable = "ALVERA_DATA_ROOT";
    public const string DefaultSqlServer = @".\SQLEXPRESS";
    public const string DefaultDataRoot = @"C:\.alvera-data";

    // A database name is spliced into SQL text (CREATE/DROP DATABASE cannot take a parameter), so only a plain identifier is accepted.
    private static readonly Regex SafeName = new("^[A-Za-z0-9_]{1,100}$", RegexOptions.Compiled);

    public static string SqlServer => Environment.GetEnvironmentVariable(SqlServerVariable) is { Length: > 0 } value ? value : DefaultSqlServer;

    public static string DataRoot => ValidateRoot(Environment.GetEnvironmentVariable(DataRootVariable) is { Length: > 0 } value ? value : DefaultDataRoot);

    public static string TestData => Path.Combine(DataRoot, "Test-Data");
    public static string DatabaseFolder => Path.Combine(TestData, "Database");
    public static string BackupFolder => Path.Combine(TestData, "Backup");
    public static string StorageFolder => Path.Combine(TestData, "Storage");
    public static string KeysFolder => Path.Combine(TestData, "Keys");
    public static string TempFolder => Path.Combine(TestData, "Temp");

    public static string MasterConnectionString => ConnectionStringFor("master");

    public static string ConnectionStringFor(string database) => $"Server={SqlServer};Database={database};Trusted_Connection=True;TrustServerCertificate=True";

    /// <summary>Refuses a relative root: a relative path would resolve against whichever folder the test host happens to run in, and the files would land somewhere nobody chose.</summary>
    internal static string ValidateRoot(string root)
    {
        if (!Path.IsPathFullyQualified(root))
            throw new InvalidOperationException($"{DataRootVariable} must be an absolute path (got '{root}').");
        return Path.GetFullPath(root);
    }

    /// <summary>The path of a database's data file and of its log file under <see cref="DatabaseFolder"/>.</summary>
    public static (string Data, string Log) FilesOf(string database)
    {
        RequireSafeName(database);
        return (Path.Combine(DatabaseFolder, database + ".mdf"), Path.Combine(DatabaseFolder, database + "_log.ldf"));
    }

    /// <summary>
    /// A statement that creates <paramref name="database"/> with its files inside <c>Test-Data\Database</c>, and does nothing when it already exists (so it can be repeated).
    /// The folder is created first: SQL Server does not create missing folders.
    /// </summary>
    public static string CreateDatabaseIfMissingSql(string database)
    {
        var (data, log) = FilesOf(database);
        Directory.CreateDirectory(DatabaseFolder);
        return $"IF DB_ID(N'{database}') IS NULL CREATE DATABASE [{database}] ON (NAME = N'{database}_data', FILENAME = N'{data.Replace("'", "''")}') LOG ON (NAME = N'{database}_log', FILENAME = N'{log.Replace("'", "''")}');";
    }

    private static void RequireSafeName(string database)
    {
        if (!SafeName.IsMatch(database))
            throw new ArgumentException($"'{database}' is not a plain database name (letters, digits and underscore only).", nameof(database));
    }
}
