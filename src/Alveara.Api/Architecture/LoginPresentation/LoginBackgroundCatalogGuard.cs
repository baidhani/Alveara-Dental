using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;
using Alveara.Api.Data;

namespace Alveara.Api.Architecture.LoginPresentation;

/// <summary>
/// ALV-N020-C01 R02: one catalog change at a time. Adding an image, making one available again, making one unavailable and choosing the shown background all take
/// this transaction-scoped application lock before they read the counts and the selection, so "29 active images" and "the selection is still active" are decided
/// on current data (the 30-image limit and the select-versus-inactivate race cannot be lost by two overlapping requests). The database triggers 51276-51279
/// stay as the independent backstop. Failure mode: the lock cannot be had within <see cref="LockTimeoutMilliseconds"/> (another change is running) = a clear 409
/// <c>catalog_busy</c>; nothing was written; the administrator repeats the action. No retry here: each operation is one administrator action.
/// </summary>
public static class LoginBackgroundCatalogGuard
{
    public const string Resource = "alveara.login-background-catalog";
    public const int LockTimeoutMilliseconds = 5000;

    /// <summary>Takes the lock for the rest of the current transaction (the caller must have one).</summary>
    public static async Task AcquireAsync(AlveraDbContext db, CancellationToken ct)
    {
        var transaction = db.Database.CurrentTransaction ?? throw new InvalidOperationException("The catalog guard needs the operation's transaction.");
        var connection = db.Database.GetDbConnection();
        await using var command = connection.CreateCommand();
        command.Transaction = transaction.GetDbTransaction();
        command.CommandTimeout = 10;
        command.CommandText = "DECLARE @r int; EXEC @r = sp_getapplock @Resource = @resource, @LockMode = N'Exclusive', @LockOwner = N'Transaction', @LockTimeout = @timeout; SELECT @r;";
        var resource = command.CreateParameter(); resource.ParameterName = "@resource"; resource.Value = Resource; command.Parameters.Add(resource);
        var timeout = command.CreateParameter(); timeout.ParameterName = "@timeout"; timeout.Value = LockTimeoutMilliseconds; command.Parameters.Add(timeout);
        if (Convert.ToInt32(await command.ExecuteScalarAsync(ct)) < 0)
            throw new LoginPresentationException("catalog_busy", "Another sign-in background change is in progress. Wait a moment and try again.", 409);
    }
}
