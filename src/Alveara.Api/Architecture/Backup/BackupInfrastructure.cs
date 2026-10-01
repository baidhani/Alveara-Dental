using System.Data;
using System.Security.Cryptography;
using Microsoft.Data.SqlClient;

namespace Alveara.Api.Architecture.Backup;

/// <summary>
/// Where backup/restore work happens on disk. All of it is server-owned configuration (never user
/// input): <c>Backup:Directory</c> (the default destination), <c>Backup:StagingRoot</c> (scratch space the
/// SQL Server service account must be able to write for BACKUP/RESTORE), <c>Backup:RestoreRoot</c>
/// (isolated restore targets), and <c>Backup:NotificationDirectory</c> (a local drop folder).
/// </summary>
public sealed record BackupPaths(
    string DefaultBackupDirectory,
    string StagingRoot,
    string RestoreRoot,
    string NotificationDirectory,
    string ConnectionString,
    string DatabaseName);

public sealed record StagedFile(string AssetClass, string RelativePath, string Sha256, long SizeBytes);

/// <summary>
/// One persistent asset class a backup must capture. A later story that introduces a new on-disk
/// store registers another implementation (and adds the class to <see cref="ManagedAssetClasses"/>);
/// the database is handled separately because it needs SQL Server's own BACKUP/RESTORE.
/// </summary>
public interface IBackupAssetSource
{
    string AssetClass { get; }

    /// <summary>Copies the asset's files under <paramref name="stagingDirectory"/>/{AssetClass}/ and returns what was staged.</summary>
    Task<IReadOnlyList<StagedFile>> StageAsync(string stagingDirectory, CancellationToken cancellationToken);
}

/// <summary>A directory tree (document blobs, Data Protection keys) captured file by file with SHA-256 per file.</summary>
public sealed class FileTreeAssetSource(string assetClass, string rootPath, Func<string, bool>? exclude = null) : IBackupAssetSource
{
    public string AssetClass => assetClass;

    public async Task<IReadOnlyList<StagedFile>> StageAsync(string stagingDirectory, CancellationToken cancellationToken)
    {
        var target = Path.Combine(stagingDirectory, assetClass);
        Directory.CreateDirectory(target);
        var staged = new List<StagedFile>();
        if (!Directory.Exists(rootPath)) return staged; // nothing exists yet for this class: an empty, still-"included" component

        foreach (var file in Directory.EnumerateFiles(rootPath, "*", SearchOption.AllDirectories).OrderBy(f => f, StringComparer.Ordinal))
        {
            cancellationToken.ThrowIfCancellationRequested();
            var relative = Path.GetRelativePath(rootPath, file);
            if (exclude?.Invoke(relative) == true) continue; // e.g. in-flight ".tmp-" blob uploads
            var destination = Path.Combine(target, relative);
            Directory.CreateDirectory(Path.GetDirectoryName(destination)!);
            await using (var source = new FileStream(file, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete))
            await using (var dest = File.Create(destination))
            {
                await source.CopyToAsync(dest, cancellationToken);
            }
            staged.Add(new StagedFile(assetClass, Path.Combine(assetClass, relative).Replace('\\', '/'), await HashFileAsync(destination, cancellationToken), new FileInfo(destination).Length));
        }
        return staged;
    }

    public static async Task<string> HashFileAsync(string path, CancellationToken cancellationToken)
    {
        await using var stream = File.OpenRead(path);
        return Convert.ToHexStringLower(await SHA256.HashDataAsync(stream, cancellationToken));
    }
}

public sealed record RestoredFileInfo(string LogicalName, string PhysicalName, string Type);

/// <summary>
/// SQL Server restore operations, behind an interface so the service stays testable. Every operation uses SQL
/// Server's own RESTORE (never a file copy), and restore ALWAYS targets a new, differently-named
/// database - this seam has no way to overwrite the live database.
/// </summary>
public interface IDatabaseRestoreProvider
{
    /// <summary>Runs RESTORE VERIFYONLY ... WITH CHECKSUM; returns false if the backup media is damaged.</summary>
    Task<bool> VerifyBackupAsync(string backupFilePath, CancellationToken cancellationToken);

    /// <summary>Restores into a NEW database named <paramref name="targetDatabase"/>, relocating its files under <paramref name="targetDirectory"/>.</summary>
    Task RestoreToNewDatabaseAsync(string backupFilePath, string targetDatabase, string targetDirectory, CancellationToken cancellationToken);

    Task DropDatabaseAsync(string databaseName, CancellationToken cancellationToken);

    Task<bool> DatabaseExistsAsync(string databaseName, CancellationToken cancellationToken);

    /// <summary>Connection string for a restored database on the same server (for validation and the application drill).</summary>
    string ConnectionStringFor(string databaseName);
}

public sealed class SqlServerRestoreProvider(string connectionString) : IDatabaseRestoreProvider
{
    public string ConnectionStringFor(string databaseName)
    {
        var builder = new SqlConnectionStringBuilder(connectionString) { InitialCatalog = databaseName };
        return builder.ConnectionString;
    }

    private async Task<SqlConnection> OpenMasterAsync(CancellationToken cancellationToken)
    {
        var builder = new SqlConnectionStringBuilder(connectionString) { InitialCatalog = "master" };
        var connection = new SqlConnection(builder.ConnectionString);
        await connection.OpenAsync(cancellationToken);
        return connection;
    }

    public async Task<bool> VerifyBackupAsync(string backupFilePath, CancellationToken cancellationToken)
    {
        await using var connection = await OpenMasterAsync(cancellationToken);
        await using var command = new SqlCommand("RESTORE VERIFYONLY FROM DISK = @path WITH CHECKSUM", connection) { CommandTimeout = 600 };
        command.Parameters.AddWithValue("@path", backupFilePath);
        try
        {
            await command.ExecuteNonQueryAsync(cancellationToken);
            return true;
        }
        catch (SqlException)
        {
            return false;
        }
    }

    public async Task RestoreToNewDatabaseAsync(string backupFilePath, string targetDatabase, string targetDirectory, CancellationToken cancellationToken)
    {
        EnsureSafeIdentifier(targetDatabase);
        await using var connection = await OpenMasterAsync(cancellationToken);

        if (await DatabaseExistsAsync(targetDatabase, cancellationToken))
            throw new InvalidOperationException("Restore target database already exists; restore never overwrites an existing database.");

        var files = new List<RestoredFileInfo>();
        await using (var list = new SqlCommand("RESTORE FILELISTONLY FROM DISK = @path", connection))
        {
            list.Parameters.AddWithValue("@path", backupFilePath);
            await using var reader = await list.ExecuteReaderAsync(cancellationToken);
            while (await reader.ReadAsync(cancellationToken))
                files.Add(new RestoredFileInfo(reader.GetString(reader.GetOrdinal("LogicalName")), reader.GetString(reader.GetOrdinal("PhysicalName")), reader.GetString(reader.GetOrdinal("Type"))));
        }

        Directory.CreateDirectory(targetDirectory);
        var moves = string.Join(", ", files.Select((f, i) => $"MOVE @logical{i} TO @physical{i}"));
        await using var restore = new SqlCommand($"RESTORE DATABASE [{targetDatabase}] FROM DISK = @path WITH {moves}, CHECKSUM", connection) { CommandTimeout = 1800 };
        restore.Parameters.AddWithValue("@path", backupFilePath);
        for (var i = 0; i < files.Count; i++)
        {
            var extension = files[i].Type == "L" ? ".ldf" : ".mdf";
            restore.Parameters.AddWithValue($"@logical{i}", files[i].LogicalName);
            restore.Parameters.AddWithValue($"@physical{i}", Path.Combine(targetDirectory, $"{targetDatabase}_{i}{extension}"));
        }
        await restore.ExecuteNonQueryAsync(cancellationToken);
    }

    public async Task DropDatabaseAsync(string databaseName, CancellationToken cancellationToken)
    {
        EnsureSafeIdentifier(databaseName);
        await using var connection = await OpenMasterAsync(cancellationToken);
        await using var command = new SqlCommand(
            $"IF DB_ID(@name) IS NOT NULL BEGIN ALTER DATABASE [{databaseName}] SET SINGLE_USER WITH ROLLBACK IMMEDIATE; DROP DATABASE [{databaseName}]; END", connection);
        command.Parameters.AddWithValue("@name", databaseName);
        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    public async Task<bool> DatabaseExistsAsync(string databaseName, CancellationToken cancellationToken)
    {
        await using var connection = await OpenMasterAsync(cancellationToken);
        await using var command = new SqlCommand("SELECT DB_ID(@name)", connection);
        command.Parameters.AddWithValue("@name", databaseName);
        var result = await command.ExecuteScalarAsync(cancellationToken);
        return result is not null and not DBNull;
    }

    /// <summary>T-SQL cannot parameterize database names. Names here are server-generated ("AlveraRestore_" + hex id), never user input; this guard makes that a hard rule.</summary>
    private static void EnsureSafeIdentifier(string name)
    {
        if (name.Length is 0 or > 100 || !name.All(c => char.IsAsciiLetterOrDigit(c) || c == '_'))
            throw new ArgumentException("Unsafe database identifier.", nameof(name));
    }
}
