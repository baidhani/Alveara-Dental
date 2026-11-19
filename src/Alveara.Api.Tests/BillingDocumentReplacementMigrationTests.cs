using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Microsoft.Extensions.DependencyInjection;
using Alveara.Api.Architecture.Billing;
using Xunit;

namespace Alveara.Api.Tests;

/// <summary>
/// ALV-N024 (PD-17, PD-18, OQ-4; briefing revision 2 section 4, approved by BRIEFING-R02), against real SQL Server and through the real migrator on a clone: migration
/// AddReceiptReplacementAndPayerEligibility, exercised by direct persistence that bypasses the service.
/// - A second receipt for a payment must replace the latest receipt nobody has replaced (51295); a replacement belongs to the same account and payment (51294); it needs a reason and only a
///   receipt can be one (CK_FinancialDocuments_Replacement); a document is replaced at most once (unique index).
/// - Two concurrent direct inserts of a first receipt leave exactly one (the per-payment lock plus READCOMMITTEDLOCK; the loser fails 51295, 1205 or 51297 - addendum 01).
/// - A legacy pair issued before the upgrade is left as it is and can be replaced through its latest receipt only (OQ-4).
/// - The payer may be the patient's guarantor or a household member as well as a recorded responsible party; anyone else is refused (51142).
/// - Down is refused once a replacement exists (51296); otherwise it restores both triggers and Up applies again.
/// </summary>
public class BillingDocumentReplacementMigrationTests : BillingTestBase
{
    private const string Before = "20261118143203_AddTreatmentPlanItemAppointmentLinks";
    private const string LinksTrigger = "SELECT COUNT(*) AS [Value] FROM sys.sql_modules m JOIN sys.triggers t ON t.object_id = m.object_id WHERE t.name = 'TR_FinancialDocuments_Links' AND m.definition LIKE '%51297%'";
    private const string PayerTrigger = "SELECT COUNT(*) AS [Value] FROM sys.sql_modules m JOIN sys.triggers t ON t.object_id = m.object_id WHERE t.name = 'TR_BillingEntries_Reversals' AND m.definition LIKE '%household member%'";

    private async Task MigrateToAsync(string target)
    {
        await using var db = Fixture.CreateContext();
        await db.GetInfrastructure().GetRequiredService<IMigrator>().MigrateAsync(target);
    }

    private Task<int> CountAsync(string sql) => WithDb(db => db.Database.SqlQuery<int>(System.Runtime.CompilerServices.FormattableStringFactory.Create(sql)).SingleAsync());

    private FinancialDocument ReceiptRow(BillingAccount account, BillingEntry payment, int number, Guid? replaces = null, string? reason = null, string type = FinancialDocumentTypes.Receipt) => new()
    {
        Id = Guid.NewGuid(), AccountId = account.Id, PatientId = account.PatientId, DocumentType = type, DocumentNumber = number,
        PaymentEntryId = type == FinancialDocumentTypes.Receipt ? payment.Id : null, AsOfEntryNumber = payment.EntryNumber, AsOfUtc = DateTimeOffset.UtcNow.AddMinutes(-1), TemplateVersion = 2,
        ContentHash = new string('a', 64), EnvelopeJson = "{}", IdempotencyKey = $"doc-{Guid.NewGuid():N}", CorrelationId = Guid.NewGuid(), IssuedAtUtc = DateTimeOffset.UtcNow, IssuedByUserId = S.Actor,
        ReplacesDocumentId = replaces, ReplacementReason = reason,
    };

    private async Task<FinancialDocument> AddAsync(FinancialDocument row)
    {
        await using var db = Fixture.CreateContext();
        db.FinancialDocuments.Add(row);
        await db.SaveChangesAsync();
        return row;
    }

    /// <summary>An account whose entry 1 is a +1000 adjustment, so payments recorded after it never take the balance below zero (an existing ledger rule).</summary>
    private async Task<BillingAccount> FundedAccountAsync(Guid? patient = null)
    {
        var account = await NewAccountAsync(patient);
        await AddRowAsync(AdjustmentRow(account, 1000m, 1, "fund-1"));
        return account;
    }

    private async Task<(BillingAccount Account, BillingEntry Payment)> PaidAccountAsync(Guid? patient = null)
    {
        var account = await FundedAccountAsync(patient);
        var payment = PaymentRow(account, -50m, 2, "pay-1");
        await AddRowAsync(payment);
        return (account, payment);
    }

    private Task<int> ReceiptsAsync(Guid payment) => WithDb(db => db.FinancialDocuments.CountAsync(d => d.PaymentEntryId == payment));

    [Fact]
    public async Task A_second_receipt_must_replace_the_latest_one_with_a_reason_and_a_receipt_is_replaced_at_most_once()
    {
        var (account, payment) = await PaidAccountAsync();
        var first = await AddAsync(ReceiptRow(account, payment, 1));
        Assert.Equal(51295, (await SqlRefusal(() => AddAsync(ReceiptRow(account, payment, 2))))!.Number);                                   // a silent second receipt
        Assert.Equal(547, (await SqlRefusal(() => AddAsync(ReceiptRow(account, payment, 2, first.Id, reason: null)))).Number);                // a replacement needs a reason
        Assert.Equal(547, (await SqlRefusal(() => AddAsync(ReceiptRow(account, payment, 2, first.Id, reason: "   ")))).Number);
        var second = await AddAsync(ReceiptRow(account, payment, 2, first.Id, "Patient lost the first copy"));
        Assert.Equal(2601, (await SqlRefusal(() => AddAsync(ReceiptRow(account, payment, 3, first.Id, "again")))).Number);                    // the first is already replaced
        await AddAsync(ReceiptRow(account, payment, 3, second.Id, "Name was misspelled"));                                                      // a chain is allowed
        Assert.Equal(3, await ReceiptsAsync(payment.Id));
    }

    [Fact]
    public async Task A_replacement_belongs_to_the_same_account_and_payment_and_only_a_receipt_can_be_one()
    {
        var (account, payment) = await PaidAccountAsync();
        var other = PaymentRow(account, -20m, 3, "pay-2");
        await AddRowAsync(other);
        var first = await AddAsync(ReceiptRow(account, payment, 1));
        Assert.Equal(51294, (await SqlRefusal(() => AddAsync(ReceiptRow(account, other, 2, first.Id, "wrong payment")))).Number);
        var (bosAccount, bosPayment) = await PaidAccountAsync(Bo);
        Assert.Equal(51294, (await SqlRefusal(() => AddAsync(ReceiptRow(bosAccount, bosPayment, 1, first.Id, "wrong account")))).Number);
        Assert.Equal(547, (await SqlRefusal(() => AddAsync(ReceiptRow(account, payment, 1, first.Id, "a statement", FinancialDocumentTypes.Statement)))).Number);
        Assert.Equal(1, await ReceiptsAsync(payment.Id));
    }

    [Fact]
    public async Task Two_concurrent_direct_inserts_of_a_first_receipt_leave_exactly_one_every_time()
    {
        var account = await FundedAccountAsync();
        for (var round = 1; round <= 20; round++)
        {
            var payment = PaymentRow(account, -10m, round + 1, $"pay-{round}");
            await AddRowAsync(payment);
            using var barrier = new Barrier(2);
            var errors = new List<string>();
            async Task<int> Insert(int number)
            {
                await using var connection = new SqlConnection(Fixture.ConnectionString);
                await connection.OpenAsync();
                await using var transaction = (SqlTransaction)await connection.BeginTransactionAsync();
                await using var command = connection.CreateCommand();
                command.Transaction = transaction;
                command.CommandText = @"INSERT INTO [FinancialDocuments] ([Id],[AccountId],[PatientId],[DocumentType],[DocumentNumber],[PaymentEntryId],[AsOfEntryNumber],[AsOfAllocationNumber],[AsOfReleaseNumber],
                    [AsOfResponsibilitySequence],[AsOfUtc],[TemplateVersion],[ContentHash],[EnvelopeJson],[IdempotencyKey],[CorrelationId],[IssuedAtUtc],[IssuedByUserId])
                    VALUES (NEWID(), @account, @patient, 'Receipt', @number, @payment, @entry, 0, 0, 0, DATEADD(minute, -1, SYSDATETIMEOFFSET()), 2, REPLICATE('a', 64), '{}', CONVERT(nvarchar(36), NEWID()), NEWID(), SYSDATETIMEOFFSET(), @actor)";
                command.Parameters.AddWithValue("@account", account.Id);
                command.Parameters.AddWithValue("@patient", account.PatientId);
                command.Parameters.AddWithValue("@number", 1000 * round + number);
                command.Parameters.AddWithValue("@payment", payment.Id);
                command.Parameters.AddWithValue("@entry", payment.EntryNumber);
                command.Parameters.AddWithValue("@actor", S.Actor);
                barrier.SignalAndWait(TimeSpan.FromSeconds(10));
                try
                {
                    await command.ExecuteNonQueryAsync();
                    await transaction.CommitAsync();
                    return 0;
                }
                catch (SqlException e)
                {
                    lock (errors) errors.Add(string.Join("/", e.Errors.Cast<SqlError>().Select(x => $"{x.Number}:{x.Message}")));
                    return e.Number;
                }
            }
            var results = await Task.WhenAll(Task.Run(() => Insert(1)), Task.Run(() => Insert(2)));
            Assert.Equal(1, await ReceiptsAsync(payment.Id));
            Assert.Single(results, 0);
            // The loser is refused by one of the three defined outcomes (ALV-N024 addendum 01, owner decision 2026-11-19): 51295 (it saw the winner's receipt), 1205 (deadlock victim), or 51297 (the
            // lock was not granted within 5 s: on this direct path each insert writes its row before its trigger runs, so the winner's READCOMMITTEDLOCK read waits on the loser's uncommitted row
            // while the loser waits for the lock; the lock timeout ends that wait). The service path takes the lock before inserting and cannot form this wait.
            Assert.True(new[] { 51295, 1205, 51297 }.Contains(results.Single(r => r != 0)), $"round {round}: " + string.Join(" | ", errors));
        }
    }

    [Fact]
    public async Task A_legacy_pair_is_left_as_it_is_and_can_be_replaced_through_its_latest_receipt_only()
    {
        var (account, payment) = await PaidAccountAsync();
        await MigrateToAsync(Before);                                                                                                           // the old rules: two receipts for one payment
        await ExecuteAsync(@"INSERT INTO [FinancialDocuments] ([Id],[AccountId],[PatientId],[DocumentType],[DocumentNumber],[PaymentEntryId],[AsOfEntryNumber],[AsOfAllocationNumber],[AsOfReleaseNumber],
            [AsOfResponsibilitySequence],[AsOfUtc],[TemplateVersion],[ContentHash],[EnvelopeJson],[IdempotencyKey],[CorrelationId],[IssuedAtUtc],[IssuedByUserId])
            SELECT NEWID(), {0}, {1}, 'Receipt', n, {2}, 2, 0, 0, 0, DATEADD(minute, -1, SYSDATETIMEOFFSET()), 2, REPLICATE('a', 64), '{{}}', CONCAT('legacy-', n), NEWID(), SYSDATETIMEOFFSET(), {3}
            FROM (VALUES (1), (2)) v(n)", account.Id, account.PatientId, payment.Id, S.Actor);
        await using (var db = Fixture.CreateContext()) await db.Database.MigrateAsync();
        var legacy = await WithDb(db => db.FinancialDocuments.AsNoTracking().Where(d => d.PaymentEntryId == payment.Id).OrderBy(d => d.DocumentNumber).ToListAsync());
        Assert.All(legacy, d => Assert.Null(d.ReplacesDocumentId));                                                                             // nothing rewritten
        Assert.Equal(51295, (await SqlRefusal(() => AddAsync(ReceiptRow(account, payment, 3, legacy[0].Id, "not the latest")))).Number);
        Assert.Equal(51295, (await SqlRefusal(() => AddAsync(ReceiptRow(account, payment, 3)))).Number);
        await AddAsync(ReceiptRow(account, payment, 3, legacy[1].Id, "Replacing the latest legacy receipt"));
        Assert.Equal(3, await ReceiptsAsync(payment.Id));
    }

    [Fact]
    public async Task The_payer_may_be_the_guarantor_or_a_household_member_and_no_one_else()
    {
        var guarantor = await S.PatientAsync("Gia", "Guard");
        var sibling = await S.PatientAsync("Sam", "Sib");
        var stranger = await S.PatientAsync("Stu", "Strange");
        var household = Guid.NewGuid();
        await ExecuteAsync("INSERT INTO [Households] ([Id],[CreatedAtUtc]) VALUES ({0}, SYSDATETIMEOFFSET())", household);
        await ExecuteAsync("UPDATE [Patients] SET [GuarantorPatientId] = {0} WHERE [Id] = {1}", guarantor, Ann);
        await ExecuteAsync("UPDATE [Patients] SET [HouseholdId] = {0}, [HouseholdRelationship] = 'Sibling' WHERE [Id] IN ({1}, {2})", household, Ann, sibling);
        var account = await FundedAccountAsync();
        await AddRowAsync(PaymentRow(account, -10m, 2, "by-guarantor"), e => e.PaidByPatientId = guarantor);
        await AddRowAsync(PaymentRow(account, -10m, 3, "by-sibling"), e => e.PaidByPatientId = sibling);
        Assert.Equal(51142, (await SqlRefusal(() => AddRowAsync(PaymentRow(account, -10m, 4, "by-stranger"), e => e.PaidByPatientId = stranger))).Number);
        await AddRowAsync(ResponsibilityRow(account, stranger, 1));                                                                              // a recorded responsible party still may
        await AddRowAsync(PaymentRow(account, -10m, 4, "by-responsible"), e => e.PaidByPatientId = stranger);
        Assert.Equal(4, await WithDb(db => db.BillingEntries.CountAsync(e => e.AccountId == account.Id)));
    }

    [Fact]
    public async Task The_migration_rolls_back_and_forward_while_no_replacement_exists()
    {
        var (account, payment) = await PaidAccountAsync();
        await AddAsync(ReceiptRow(account, payment, 1));
        Assert.Equal((1, 1), (await CountAsync(LinksTrigger), await CountAsync(PayerTrigger)));
        await MigrateToAsync(Before);
        Assert.Equal((0, 0), (await CountAsync(LinksTrigger), await CountAsync(PayerTrigger)));
        Assert.Equal(0, await CountAsync("SELECT COUNT(*) AS [Value] FROM sys.columns WHERE object_id = OBJECT_ID('FinancialDocuments') AND name IN ('ReplacesDocumentId','ReplacementReason')"));
        Assert.Equal(2, await CountAsync("SELECT COUNT(*) AS [Value] FROM sys.triggers WHERE name IN ('TR_FinancialDocuments_Links','TR_BillingEntries_Reversals')"));   // the old ones are back
        await using (var db = Fixture.CreateContext()) await db.Database.MigrateAsync();
        await using var check = Fixture.CreateContext();
        Assert.Empty(await check.Database.GetPendingMigrationsAsync());
        Assert.Equal((1, 1), (await CountAsync(LinksTrigger), await CountAsync(PayerTrigger)));
        Assert.Equal(1, await ReceiptsAsync(payment.Id));
    }

    [Fact]
    public async Task Rolling_back_is_refused_once_a_replacement_exists_and_nothing_is_lost()
    {
        var (account, payment) = await PaidAccountAsync();
        var first = await AddAsync(ReceiptRow(account, payment, 1));
        await AddAsync(ReceiptRow(account, payment, 2, first.Id, "Reprinted for the insurer"));
        Assert.Equal(51296, (await SqlRefusal(() => MigrateToAsync(Before))).Number);
        Assert.Equal(2, await ReceiptsAsync(payment.Id));
        Assert.Equal((1, 1), (await CountAsync(LinksTrigger), await CountAsync(PayerTrigger)));
    }

    [Fact]
    public async Task R02_the_database_accepts_only_an_active_related_payer_for_payments_and_corrections()
    {
        // ALV-N024 R02 (CR-1, owner decision "active only"): 51142 enforces exactly the service's set - an ACTIVE guarantor, household member or recorded-responsible party
        var guarantor = await S.PatientAsync("Gia", "Guard");
        var sibling = await S.PatientAsync("Sam", "Sib");
        var responsible = await S.PatientAsync("Rae", "Resp");
        var household = Guid.NewGuid();
        await ExecuteAsync("INSERT INTO [Households] ([Id],[CreatedAtUtc]) VALUES ({0}, SYSDATETIMEOFFSET())", household);
        await ExecuteAsync("UPDATE [Patients] SET [GuarantorPatientId] = {0} WHERE [Id] = {1}", guarantor, Ann);
        await ExecuteAsync("UPDATE [Patients] SET [HouseholdId] = {0}, [HouseholdRelationship] = 'Sibling' WHERE [Id] IN ({1}, {2})", household, Ann, sibling);
        var account = await FundedAccountAsync();
        await AddRowAsync(ResponsibilityRow(account, responsible, 1));
        var number = 2;
        var payers = new[] { guarantor, sibling, responsible };
        var payments = new List<BillingEntry>();
        foreach (var who in payers.Append(guarantor))                                                          // active: each may pay (a fourth payment is kept for the inactive corrections)
        {
            var payment = PaymentRow(account, -10m, number++, $"active-{payments.Count}");
            await AddRowAsync(payment, e => e.PaidByPatientId = who);
            payments.Add(payment);
        }
        for (var k = 0; k < payers.Length; k++)                                                               // active: each may be the payer of a correction (reversal + replacement)
        {
            await AddRowAsync(ReversalRow(account, payments[k], number++, $"rev-{k}"));
            await AddRowAsync(PaymentRow(account, -10m, number++, $"repl-active-{k}"), e => { e.PaidByPatientId = payers[k]; e.ReplacesEntryId = payments[k].Id; });
        }

        await ExecuteAsync("UPDATE [Patients] SET [IsActive] = 0 WHERE [Id] IN ({0}, {1}, {2})", guarantor, sibling, responsible);
        var before = await WithDb(db => db.BillingEntries.CountAsync(e => e.AccountId == account.Id));
        foreach (var who in payers)                                                                            // inactive: refused for a payment ...
            Assert.Equal(51142, (await SqlRefusal(() => AddRowAsync(PaymentRow(account, -10m, number, $"inactive-{who:N}"), e => e.PaidByPatientId = who))).Number);
        await AddRowAsync(ReversalRow(account, payments[3], number++, "rev-3"));
        foreach (var who in payers)                                                                            // ... and for a correction's replacement
            Assert.Equal(51142, (await SqlRefusal(() => AddRowAsync(PaymentRow(account, -10m, number, $"repl-inactive-{who:N}"), e => { e.PaidByPatientId = who; e.ReplacesEntryId = payments[3].Id; }))).Number);
        Assert.Equal(before + 1, await WithDb(db => db.BillingEntries.CountAsync(e => e.AccountId == account.Id)));   // only the second reversal was added
    }
}
