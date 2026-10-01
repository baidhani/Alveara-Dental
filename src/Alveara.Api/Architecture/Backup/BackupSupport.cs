using System.Text.Json;
using Microsoft.Data.SqlClient;

namespace Alveara.Api.Architecture.Backup;

/// <summary>A backup/recovery rule was violated; <see cref="Code"/> is the stable machine-readable reason.</summary>
public sealed class BackupException(string code, string message, int statusCode = 400) : Exception(message)
{
    public string Code { get; } = code;
    public int StatusCode { get; } = statusCode;
}

public sealed record BackupManifestComponent(string AssetClass, string Path, string Sha256, long SizeBytes);

/// <summary>The inventory stored INSIDE every backup (and echoed in its history row), so a restore can prove completeness before it touches anything.</summary>
public sealed record BackupManifest(
    int FormatVersion,
    Guid BackupId,
    DateTimeOffset CreatedAtUtc,
    string AppVersion,
    string SchemaMigration,
    string DatabaseName,
    IReadOnlyList<string> AssetClasses,
    IReadOnlyList<BackupManifestComponent> Components,
    IReadOnlyDictionary<string, long> TableRowCountsBefore,
    IReadOnlyDictionary<string, long> TableRowCountsAfter,
    DeploymentSettings? Deployment = null)
{
    public const int CurrentFormatVersion = 1;
    public const string FileName = "manifest.json";
    public const string DatabaseComponentPath = "database/alveara.bak";

    public static readonly JsonSerializerOptions Json = new() { WriteIndented = true, PropertyNamingPolicy = JsonNamingPolicy.CamelCase };
}

public sealed record BackupNotification(string Kind, string Message, Guid? BackupRecordId);

/// <summary>
/// Success/failure notification for a LOCAL deployment (no internet mail assumed): the default
/// implementation drops a small text file into a monitored folder. Delivery problems are reported
/// back to the caller, who records them - they never change whether the backup itself succeeded.
/// </summary>
public interface IBackupNotifier
{
    Task DeliverAsync(BackupNotification notification, CancellationToken cancellationToken);
}

public sealed class FileDropBackupNotifier(BackupPaths paths) : IBackupNotifier
{
    public async Task DeliverAsync(BackupNotification notification, CancellationToken cancellationToken)
    {
        Directory.CreateDirectory(paths.NotificationDirectory);
        var name = $"{DateTimeOffset.UtcNow:yyyyMMddTHHmmssfff}-{notification.Kind}-{Guid.NewGuid():N}.txt";
        var body = $"Alveara backup notification{Environment.NewLine}Kind: {notification.Kind}{Environment.NewLine}"
                 + $"Backup: {notification.BackupRecordId?.ToString("N") ?? "n/a"}{Environment.NewLine}"
                 + $"Time (UTC): {DateTimeOffset.UtcNow:O}{Environment.NewLine}{Environment.NewLine}{notification.Message}{Environment.NewLine}";
        await File.WriteAllTextAsync(Path.Combine(paths.NotificationDirectory, name), body, cancellationToken);
    }
}

/// <summary>Database facts a backup/restore needs, read with plain ADO.NET so they work against any database on the server (live or restored).</summary>
public static class DatabaseFacts
{
    public static async Task<string> LatestMigrationAsync(string connectionString, CancellationToken cancellationToken)
    {
        await using var connection = new SqlConnection(connectionString);
        await connection.OpenAsync(cancellationToken);
        await using var command = new SqlCommand("SELECT TOP 1 MigrationId FROM __EFMigrationsHistory ORDER BY MigrationId DESC", connection);
        return (string?)await command.ExecuteScalarAsync(cancellationToken) ?? "(none)";
    }

    public static async Task<IReadOnlyDictionary<string, long>> TableRowCountsAsync(string connectionString, CancellationToken cancellationToken)
    {
        await using var connection = new SqlConnection(connectionString);
        await connection.OpenAsync(cancellationToken);
        await using var command = new SqlCommand(
            "SELECT t.name, SUM(p.rows) FROM sys.tables t JOIN sys.partitions p ON p.object_id = t.object_id AND p.index_id IN (0, 1) GROUP BY t.name", connection);
        var counts = new Dictionary<string, long>(StringComparer.Ordinal);
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken)) counts[reader.GetString(0)] = reader.GetInt64(1);
        return counts;
    }
}

/// <summary>Maps low-level failures to stable codes and fixed messages - exception text is never stored or shown (it can contain paths, server names or data).</summary>
public static class BackupFailure
{
    public static (string Code, string Message) Classify(Exception ex) => ex switch
    {
        BackupException b => (b.Code, b.Message),
        BackupCryptoException c => (c.Code, c.Message),
        OperationCanceledException => ("interrupted", "The backup was interrupted before it finished."),
        IOException io when IsDiskFull(io) => ("destination_full", "The backup destination is out of space."),
        UnauthorizedAccessException or DirectoryNotFoundException => ("destination_unavailable", "The backup destination is not available or not writable."),
        IOException => ("io_error", "A file operation failed while writing the backup."),
        SqlException => ("snapshot_failed", "The database could not produce a consistent snapshot."),
        InvalidDataException => ("corrupt_or_tampered", "The backup contents are damaged."),
        _ => ("unexpected_error", "The backup failed unexpectedly. See the server diagnostics using the backup's id."),
    };

    private static bool IsDiskFull(IOException io) => (io.HResult & 0xFFFF) is 0x70 or 0x27;
}
