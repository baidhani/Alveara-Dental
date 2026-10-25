using System.Security.Cryptography;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Xunit;
using Alveara.Api.Architecture.Backup;
using Alveara.Api.Architecture.Documents;
using Alveara.Api.Architecture.ExternalCases;
using Alveara.Api.Architecture.Identity;
using Alveara.Api.Data;

namespace Alveara.Api.Tests;

/// <summary>
/// ALV-009-C02, the recovery drill for referral and lab data against a real SQL Server restore: outside parties, a referral and a lab case with their append-only history, a document link and the document it points to are
/// backed up and restored into an isolated target, and the restored database holds exactly what the backup held (the link points at the same document, which is not duplicated). A change made after the backup (a
/// cancellation, a removed link, an inactive party) is not in the restore, and the restored database still refuses what the live one refuses (deleting a party or a case, changing the history, linking another patient's
/// document, linking a document twice).
/// </summary>
[Collection(ParallelismCollections.SerialServer)]
public class ExternalCaseBackupRestoreTests : SafetyTestBase
{
    private BackupTestEnvironment _env = null!;
    private readonly List<string> _restored = [];

    public override async Task InitializeAsync()
    {
        await base.InitializeAsync();
        _env = new BackupTestEnvironment(Fixture);
        await _env.ResetBackupStateAsync();
        await _env.ConfigureRecoveryKeyAsync();
    }

    public override async Task DisposeAsync()
    {
        foreach (var name in _restored) await _env.RestoreProvider.DropDatabaseAsync(name, default);
        _env.Dispose();
        await base.DisposeAsync();
    }

    private async Task<BackupRecord> BackupAsync()
    {
        await using var db = _env.NewDb();
        return await _env.NewBackupService(db).RunBackupAsync(BackupKind.Manual, $"manual:{Guid.NewGuid():N}", S.Actor, default);
    }

    private async Task<RestoreDrillRecord> DrillAsync(Guid recordId)
    {
        await using var db = _env.NewDb();
        var drill = await _env.NewRestoreService(db).RestoreDrillAsync(recordId, _env.Key.EncryptedPrivateKeyPem, BackupTestEnvironment.Passphrase, S.Actor, default);
        if (drill.TargetDatabase is not null) _restored.Add(drill.TargetDatabase);
        return drill;
    }

    private AlveraDbContext RestoredDb(RestoreDrillRecord drill) =>
        new(new DbContextOptionsBuilder<AlveraDbContext>().UseSqlServer(_env.RestoreProvider.ConnectionStringFor(drill.TargetDatabase!)).Options);

    private async Task<T> Cases<T>(Func<ExternalCaseService, Task<T>> action)
    {
        await using var db = Fixture.CreateContext();
        return await action(new ExternalCaseService(db, PatientTestSupport.Clock));
    }

    [Fact]
    public async Task Parties_cases_history_and_document_links_survive_a_backup_and_restore_and_a_later_change_is_not_in_it()
    {
        var today = DateOnly.FromDateTime(PatientTestSupport.Clock.ToPracticeLocal(DateTimeOffset.UtcNow).Date);
        ExternalPartyView provider, vendor, spare;
        await using (var db = Fixture.CreateContext())
        {
            var parties = new ExternalPartyService(db, PatientTestSupport.Clock);
            provider = await parties.CreateAsync(S.Actor, new CreatePartyRequest(ExternalPartyKinds.ReferralProvider, "Riverside Endodontics", "555-010-0100"), default);
            vendor = await parties.CreateAsync(S.Actor, new CreatePartyRequest(ExternalPartyKinds.LabVendor, "Smile Dental Lab", null), default);
            spare = await parties.CreateAsync(S.Actor, new CreatePartyRequest(ExternalPartyKinds.LabVendor, "Unused Lab", null), default);              // no case points at it, so only the trigger can refuse its deletion
        }
        var referral = (await Cases(s => s.CreateReferralAsync(S.Actor, Ann, new CreateReferralRequest(provider.Id, "Retreatment of tooth 19", today.AddDays(5), null, "Bring the film"), "k-ref", default))).Case;
        referral = (await Cases(s => s.ChangeStatusAsync(S.Actor, ExternalCaseKinds.Referral, referral.Id, new ChangeStatusRequest("Sent", null, today.AddDays(-1), referral.RowVersion), default))).Case;
        var lab = (await Cases(s => s.CreateLabAsync(S.Actor, Ann, new CreateLabRequest(vendor.Id, "Crown, zirconia", today.AddDays(9), "36", null, "Zirconia", "A2", null), "k-lab", default))).Case;

        // a real document with its file where the backup looks for the documents asset class
        var bytes = System.Text.Encoding.UTF8.GetBytes("panoramic film bytes");
        var blob = Guid.NewGuid();
        await File.WriteAllBytesAsync(Path.Combine(_env.BlobRoot, blob.ToString("N")), bytes);
        var doc = new PatientDocument
        {
            Id = Guid.NewGuid(), PatientId = Ann, CategoryId = DocumentCategorySeeds.Radiograph, DocumentDate = new DateOnly(2026, 10, 1), Description = "Panoramic film.", OriginalFileName = "pano.png", DetectedType = "png", ContentType = "image/png",
            SizeBytes = bytes.Length, Sha256 = Convert.ToHexString(SHA256.HashData(bytes)).ToLowerInvariant(), BlobId = blob, ImportedByUserId = S.Actor, ImportedAtUtc = DateTimeOffset.UtcNow, IdempotencyKey = $"doc-{Guid.NewGuid():N}", RequestHash = new string('a', 64),
        };
        await using (var db = Fixture.CreateContext()) { db.Add(doc); await db.SaveChangesAsync(); }
        await Cases(s => s.LinkDocumentAsync(S.Actor, ExternalCaseKinds.Referral, referral.Id, doc.Id, default));

        await using var before = Fixture.CreateContext();
        var expected = (Parties: await before.ExternalParties.CountAsync(), Referrals: await before.ReferralCases.CountAsync(), Labs: await before.LabCases.CountAsync(), Events: await before.ExternalCaseEvents.CountAsync(),
                        Links: await before.ExternalCaseDocuments.CountAsync(), Documents: await before.Documents.CountAsync(), Audit: await before.AuditLogEntries.CountAsync(a => a.EventType.StartsWith("ReferralCase") || a.EventType.StartsWith("LabCase") || a.EventType.StartsWith("ExternalParty") || a.EventType.StartsWith("CaseDocument")));
        Assert.Equal((3, 1, 1, 4, 1, 1), (expected.Parties, expected.Referrals, expected.Labs, expected.Events, expected.Links, expected.Documents));

        var record = await BackupAsync();

        // life goes on after the backup: the referral is cancelled (which needs it to have not been finished), the link removed and the vendor made inactive. The restore returns the state of the backup, not of today.
        var current = await Cases(s => s.GetAsync(ExternalCaseKinds.Referral, referral.Id, default));
        await Cases(s => s.UnlinkDocumentAsync(S.Actor, ExternalCaseKinds.Referral, referral.Id, doc.Id, default));
        await Cases(s => s.ChangeStatusAsync(S.Actor, ExternalCaseKinds.Referral, referral.Id, new ChangeStatusRequest("Cancelled", "Patient moved away", null, current.RowVersion), default));
        await using (var db = Fixture.CreateContext()) await new ExternalPartyService(db, PatientTestSupport.Clock).SetActiveAsync(S.Actor, vendor.Id, new SetPartyActiveRequest(false, vendor.RowVersion), default);

        var drill = await DrillAsync(record.Id);
        Assert.Equal("Succeeded", drill.Outcome);
        var checks = JsonSerializer.Deserialize<List<ValidationCheck>>(drill.ValidationJson!, BackupManifest.Json)!;
        Assert.All(checks.Where(c => c.Blocking), c => Assert.True(c.Passed, c.Name + ": " + c.Detail));

        await using var restored = RestoredDb(drill);
        Assert.Equal((expected.Parties, expected.Referrals, expected.Labs, expected.Events, expected.Links, expected.Documents),
            (await restored.ExternalParties.CountAsync(), await restored.ReferralCases.CountAsync(), await restored.LabCases.CountAsync(), await restored.ExternalCaseEvents.CountAsync(), await restored.ExternalCaseDocuments.CountAsync(), await restored.Documents.CountAsync()));
        var sent = await restored.ReferralCases.AsNoTracking().SingleAsync();
        Assert.Equal(("Sent", today.AddDays(-1), (Guid?)null, "Retreatment of tooth 19"), (sent.Status, sent.SentOn, sent.ClosedByUserId, sent.Reason));            // the later cancellation is not in the restore
        var labRow = await restored.LabCases.AsNoTracking().SingleAsync();
        Assert.Equal(("Draft", "36", "Zirconia", "A2"), (labRow.Status, labRow.ToothKey, labRow.Material, labRow.Shade));
        Assert.True((await restored.ExternalParties.AsNoTracking().SingleAsync(p => p.Id == vendor.Id)).IsActive);                                                   // and neither is the inactive party
        var link = await restored.ExternalCaseDocuments.AsNoTracking().SingleAsync();
        Assert.Equal((doc.Id, sent.Id), (link.DocumentId, link.ReferralCaseId));                                                                                      // the link points at the one document, which is not duplicated
        Assert.Equal(expected.Audit, await restored.AuditLogEntries.CountAsync(a => a.EventType.StartsWith("ReferralCase") || a.EventType.StartsWith("LabCase") || a.EventType.StartsWith("ExternalParty") || a.EventType.StartsWith("CaseDocument")));
        Assert.Equal(new[] { "Created", "DocumentLinked", "StatusChanged" }, (await restored.ExternalCaseEvents.AsNoTracking().Where(e => e.ReferralCaseId == sent.Id).OrderBy(e => e.OccurredAtUtc).Select(e => e.Kind).ToListAsync()).Order().ToArray());

        // the restored database still refuses what the live one refuses
        Assert.Contains("51220", (await Assert.ThrowsAnyAsync<Exception>(() => restored.Database.ExecuteSqlAsync($"DELETE FROM [ExternalParties] WHERE [Id] = {spare.Id}"))).ToString());
        var deleteCase = (await Assert.ThrowsAnyAsync<Exception>(() => restored.Database.ExecuteSqlAsync($"DELETE FROM [ReferralCases] WHERE [Id] = {sent.Id}"))).ToString();
        Assert.True(deleteCase.Contains("51221") || deleteCase.Contains("REFERENCE constraint"), deleteCase);                                  // the case has history pointing at it, so the foreign key or the trigger refuses
        Assert.Contains("51223", (await Assert.ThrowsAnyAsync<Exception>(() => restored.Database.ExecuteSqlAsync($"UPDATE [ExternalCaseEvents] SET [Note] = N'rewritten'"))).ToString());
        Assert.Contains("unique", (await Assert.ThrowsAnyAsync<Exception>(() => restored.Database.ExecuteSqlAsync($"INSERT INTO [ExternalCaseDocuments] ([Id], [ReferralCaseId], [DocumentId], [LinkedByUserId], [LinkedAtUtc]) VALUES (NEWID(), {sent.Id}, {doc.Id}, {S.Actor}, SYSDATETIMEOFFSET())"))).ToString(), StringComparison.OrdinalIgnoreCase);
        var boDoc = Guid.NewGuid();
        await restored.Database.ExecuteSqlAsync($"INSERT INTO [Documents] ([Id], [PatientId], [CategoryId], [DocumentDate], [OriginalFileName], [DetectedType], [ContentType], [SizeBytes], [Sha256], [BlobId], [ImportedByUserId], [ImportedAtUtc], [IdempotencyKey], [RequestHash]) VALUES ({boDoc}, {Bo}, {DocumentCategorySeeds.Radiograph}, '2026-10-01', N'bo.png', N'png', N'image/png', 1, {new string('b', 64)}, {Guid.NewGuid()}, {S.Actor}, SYSDATETIMEOFFSET(), N'bo-doc', {new string('b', 64)})");
        Assert.Contains("51222", (await Assert.ThrowsAnyAsync<Exception>(() => restored.Database.ExecuteSqlAsync($"INSERT INTO [ExternalCaseDocuments] ([Id], [ReferralCaseId], [DocumentId], [LinkedByUserId], [LinkedAtUtc]) VALUES (NEWID(), {sent.Id}, {boDoc}, {S.Actor}, SYSDATETIMEOFFSET())"))).ToString());

        // and the live database is untouched by the drill
        await using var live = Fixture.CreateContext();
        Assert.Equal(("Cancelled", false), ((await live.ReferralCases.AsNoTracking().SingleAsync()).Status, (await live.ExternalParties.AsNoTracking().SingleAsync(p => p.Id == vendor.Id)).IsActive));
        Assert.Equal(0, await live.ExternalCaseDocuments.CountAsync());
    }
}
