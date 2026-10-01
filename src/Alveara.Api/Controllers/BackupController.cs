using System.Security.Claims;
using System.Text.Json;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Alveara.Api.Architecture.Backup;
using Alveara.Api.Architecture.Concurrency;
using Alveara.Api.Architecture.Configuration;
using Alveara.Api.Architecture.Identity;

namespace Alveara.Api.Controllers;

public record SaveBackupSettingsRequest(
    bool ScheduleEnabled, int ScheduleIntervalHours, int RetentionCount, string? DestinationDirectory,
    int RequiredSuccessfulVerifications, int VerificationCadenceDays, string? RowVersion);
public record ConfigureRecoveryKeyRequest(string? CurrentPassword, string? Passphrase, bool ReplaceExisting);
/// <summary>The recovery key file contents and passphrase travel only in the request body and are never stored or logged.</summary>
public record RecoveryMaterialRequest(string? CurrentPassword, string? RecoveryKey, string? Passphrase);
public record StepUpRequest(string? CurrentPassword);

public record BackupRecordDto(
    Guid Id, string Kind, string Status, DateTimeOffset StartedAtUtc, DateTimeOffset? CompletedAtUtc, string? FileName, long? SizeBytes, string? Sha256,
    IReadOnlyList<string> IncludedAssetClasses, string? SchemaMigration, string? AppVersion, string? FailureCode, string? FailureMessage,
    string VerificationStatus, DateTimeOffset? VerifiedAtUtc, string? VerificationFailureCode)
{
    public static BackupRecordDto? From(BackupRecord? r) => r is null ? null : new(
        r.Id, r.Kind.ToString(), r.Status.ToString(), r.StartedAtUtc, r.CompletedAtUtc, r.FileName, r.SizeBytes, r.Sha256,
        (r.IncludedAssetClasses ?? "").Split(',', StringSplitOptions.RemoveEmptyEntries), r.SchemaMigration, r.AppVersion, r.FailureCode, r.FailureMessage,
        r.VerificationStatus.ToString(), r.VerifiedAtUtc, r.VerificationFailureCode);
}

public record RestoreDrillDto(
    Guid Id, Guid BackupRecordId, DateTimeOffset StartedAtUtc, DateTimeOffset? CompletedAtUtc, string Outcome, string? FailureCode, string? FailureMessage,
    string? TargetDatabase, string? TargetDirectory, bool TargetRemoved, IReadOnlyList<ValidationCheck> Checks)
{
    public static RestoreDrillDto From(RestoreDrillRecord d) => new(
        d.Id, d.BackupRecordId, d.StartedAtUtc, d.CompletedAtUtc, d.Outcome, d.FailureCode, d.FailureMessage, d.TargetDatabase, d.TargetDirectory, d.TargetRemoved,
        d.ValidationJson is null ? [] : JsonSerializer.Deserialize<List<ValidationCheck>>(d.ValidationJson, BackupManifest.Json) ?? []);
}

/// <summary>
/// ALV-N004: backup and recovery administration. Managing backups is administrator-only
/// (<see cref="Permission.ManageBackups"/>); viewing status and failures is also available to the
/// practice manager (<see cref="Permission.ViewBackupStatus"/>). Every sensitive action - setting up
/// the recovery key, verifying, restore drills, removing a restore target - additionally requires the
/// caller's CURRENT PASSWORD (step-up through the shared lockout boundary). Recovery material (the
/// key file and passphrase) is accepted only in request bodies and is never stored, logged, echoed or
/// written into a backup. There is no endpoint that restores over live data.
/// </summary>
[ApiController]
[Route("api/backup")]
[Authorize]
public class BackupController(
    BackupService backups, BackupRestoreService restores, AccountService accounts) : ControllerBase
{
    private Guid Actor => Guid.Parse(User.FindFirstValue(ClaimTypes.NameIdentifier)!);

    private async Task<IActionResult> Run(Func<Task<IActionResult>> action)
    {
        try
        {
            return await action();
        }
        catch (BackupException ex) { return StatusCode(ex.StatusCode, new { error = ex.Code, message = ex.Message }); }
        catch (ConfigurationException ex) { return StatusCode(ex.StatusCode, new { error = ex.Code, message = ex.Message }); }
        catch (ConcurrencyConflictException ex) { return Conflict(ex.ToProblem()); }
        catch (InvalidCurrentPasswordException) { return StatusCode(StatusCodes.Status403Forbidden, new { error = "step_up_failed", message = "Your current password is required and was not accepted." }); }
        catch (AccountLockedOutException ex) { return StatusCode(StatusCodes.Status429TooManyRequests, new { error = "account_locked", lockedUntilUtc = ex.LockedOutUntilUtc }); }
    }

    private async Task StepUpAsync(string? currentPassword, CancellationToken ct) => await accounts.ReauthenticateAsync(Actor, currentPassword, ct);

    private static void RequireMaterial(RecoveryMaterialRequest request)
    {
        if (string.IsNullOrWhiteSpace(request.RecoveryKey) || string.IsNullOrEmpty(request.Passphrase))
            throw new BackupException("recovery_material_required", "The recovery key file and its passphrase are required.");
        if (request.RecoveryKey.Length > 20_000 || request.Passphrase.Length > 500)
            throw new BackupException("recovery_material_invalid", "The recovery material is not valid.");
    }

    // ---------- Read ----------

    [HttpGet("status")]
    [RequirePermission(Permission.ViewBackupStatus)]
    public Task<IActionResult> Status(CancellationToken ct) => Run(async () =>
    {
        var s = await backups.GetStatusAsync(ct);
        return Ok(new
        {
            settings = s.Settings,
            lastSuccess = BackupRecordDto.From(s.LastSuccess),
            lastFailure = BackupRecordDto.From(s.LastFailure),
            s.LatestAttemptFailed, s.SuccessfulVerificationCount, s.Trusted, s.LastFullVerificationAtUtc, s.VerificationOverdue,
            s.RequiredAssetClasses, s.LastSuccessCoversAllAssetClasses, s.MissingAssetClasses,
        });
    });

    [HttpGet("history")]
    [RequirePermission(Permission.ViewBackupStatus)]
    public Task<IActionResult> History([FromQuery] int take = 50, CancellationToken ct = default) =>
        Run(async () => Ok((await backups.HistoryAsync(take, ct)).Select(BackupRecordDto.From)));

    [HttpGet("restore-drills")]
    [RequirePermission(Permission.ManageBackups)]
    public Task<IActionResult> Drills([FromQuery] int take = 50, CancellationToken ct = default) =>
        Run(async () => Ok((await restores.DrillsAsync(take, ct)).Select(RestoreDrillDto.From)));

    [HttpGet("notifications")]
    [RequirePermission(Permission.ViewBackupStatus)]
    public Task<IActionResult> Notifications([FromQuery] int take = 20, CancellationToken ct = default) =>
        Run(async () => Ok((await backups.NotificationsAsync(take, ct))
            .Select(n => new { n.Id, n.BackupRecordId, n.Kind, n.Message, n.CreatedAtUtc, delivery = n.Delivery.ToString(), n.DeliveryFailureCode })));

    // ---------- Settings + recovery key ----------

    [HttpPut("settings")]
    [RequirePermission(Permission.ManageBackups)]
    [RequireCsrfToken]
    public Task<IActionResult> SaveSettings([FromBody] SaveBackupSettingsRequest r, CancellationToken ct) => Run(async () =>
        Ok(await backups.SaveSettingsAsync(r.ScheduleEnabled, r.ScheduleIntervalHours, r.RetentionCount, r.DestinationDirectory,
            r.RequiredSuccessfulVerifications, r.VerificationCadenceDays, r.RowVersion, Actor, ct)));

    /// <summary>Returns the encrypted private recovery key ONCE; only the public half is stored. The response must not be cached.</summary>
    [HttpPost("recovery-key")]
    [RequirePermission(Permission.ManageBackups)]
    [RequireCsrfToken]
    public Task<IActionResult> ConfigureRecoveryKey([FromBody] ConfigureRecoveryKeyRequest r, CancellationToken ct) => Run(async () =>
    {
        await StepUpAsync(r.CurrentPassword, ct);
        var privateKey = await backups.ConfigureRecoveryKeyAsync(r.Passphrase ?? "", r.ReplaceExisting, Actor, ct);
        Response.Headers.CacheControl = "no-store";
        var settings = await backups.GetSettingsAsync(ct);
        return Ok(new
        {
            recoveryKey = privateKey,
            fingerprint = settings.RecoveryKeyFingerprint,
            warning = "Store this recovery key file and its passphrase OFFLINE, away from this server. It is shown only once. Without both, backups cannot be restored.",
        });
    });

    // ---------- Backups ----------

    [HttpPost("backups")]
    [RequirePermission(Permission.ManageBackups)]
    [RequireCsrfToken]
    public async Task<IActionResult> RunManualBackup(CancellationToken ct)
    {
        try
        {
            var record = await backups.RunBackupAsync(BackupKind.Manual, $"manual:{Guid.NewGuid():N}", Actor, ct);
            return StatusCode(StatusCodes.Status201Created, BackupRecordDto.From(record));
        }
        catch (BackupException ex) when (ex.Code == "recovery_key_required")
        {
            return StatusCode(ex.StatusCode, new { error = ex.Code, message = ex.Message }); // refused before any attempt was recorded
        }
        catch (Exception ex)
        {
            // The run already recorded a Failed history row and notified; report the safe code, never the exception text.
            var (code, message) = BackupFailure.Classify(ex);
            return StatusCode(StatusCodes.Status500InternalServerError, new { error = code, message });
        }
    }

    [HttpPost("backups/{id:guid}/verify-hash")]
    [RequirePermission(Permission.ManageBackups)]
    [RequireCsrfToken]
    public Task<IActionResult> VerifyHash(Guid id, CancellationToken ct) => Run(async () => Ok(BackupRecordDto.From(await backups.VerifyHashAsync(id, Actor, ct))));

    [HttpPost("backups/{id:guid}/preflight")]
    [RequirePermission(Permission.ManageBackups)]
    [RequireCsrfToken]
    public Task<IActionResult> Preflight(Guid id, [FromBody] RecoveryMaterialRequest r, CancellationToken ct) => Run(async () =>
    {
        await StepUpAsync(r.CurrentPassword, ct);
        return Ok(await restores.PreflightAsync(id, r.RecoveryKey ?? "", r.Passphrase ?? "", ct));
    });

    [HttpPost("backups/{id:guid}/verify")]
    [RequirePermission(Permission.ManageBackups)]
    [RequireCsrfToken]
    public Task<IActionResult> VerifyFull(Guid id, [FromBody] RecoveryMaterialRequest r, CancellationToken ct) => Run(async () =>
    {
        await StepUpAsync(r.CurrentPassword, ct);
        RequireMaterial(r);
        return Ok(BackupRecordDto.From(await restores.VerifyFullAsync(id, r.RecoveryKey!, r.Passphrase!, Actor, ct)));
    });

    [HttpPost("backups/{id:guid}/restore-drill")]
    [RequirePermission(Permission.ManageBackups)]
    [RequireCsrfToken]
    public Task<IActionResult> RestoreDrill(Guid id, [FromBody] RecoveryMaterialRequest r, CancellationToken ct) => Run(async () =>
    {
        await StepUpAsync(r.CurrentPassword, ct);
        RequireMaterial(r);
        var drill = await restores.RestoreDrillAsync(id, r.RecoveryKey!, r.Passphrase!, Actor, ct);
        return StatusCode(drill.Outcome == "Succeeded" ? StatusCodes.Status201Created : StatusCodes.Status422UnprocessableEntity, RestoreDrillDto.From(drill));
    });

    [HttpPost("restore-drills/{id:guid}/remove-target")]
    [RequirePermission(Permission.ManageBackups)]
    [RequireCsrfToken]
    public Task<IActionResult> RemoveTarget(Guid id, [FromBody] StepUpRequest r, CancellationToken ct) => Run(async () =>
    {
        await StepUpAsync(r.CurrentPassword, ct);
        return Ok(RestoreDrillDto.From(await restores.RemoveTargetAsync(id, Actor, ct)));
    });

    [HttpPost("notifications/test")]
    [RequirePermission(Permission.ManageBackups)]
    [RequireCsrfToken]
    public Task<IActionResult> TestNotification(CancellationToken ct) => Run(async () =>
    {
        var n = await backups.SendTestNotificationAsync(Actor, ct);
        return Ok(new { n.Id, n.Kind, delivery = n.Delivery.ToString(), n.DeliveryFailureCode });
    });
}
