using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Microsoft.Extensions.DependencyInjection;
using Alveara.Api.Architecture.Prescribing;
using Xunit;

namespace Alveara.Api.Tests;

/// <summary>
/// STORY-016, the database on its own: rows written straight to the table, around the service, so the tests prove the database refuses an incorrect prescription by itself ("incorrect dosage is accepted" cannot
/// happen through a bug or a script). A dose that is not positive or is above the typing-error ceiling of its unit, a unit or frequency off the closed lists, an allergy outcome that disagrees with the list state it
/// was made from, an entry that needed the prescriber's review and has none, a duplicate save key, a changed or deleted prescription, and a migration that cannot be rolled back over recorded history.
/// </summary>
[RealMigrations]
public class PrescriptionSchemaTests : SafetyTestBase
{
    private const string Before = "20261013170209_AddCompletionChartVersion";
    private static readonly string Hash = new('a', 64);

    private Prescription Row(Action<Prescription>? change = null, Guid? patient = null)
    {
        var row = new Prescription
        {
            Id = Guid.NewGuid(), PatientId = patient ?? Ann, Medication = "Amoxicillin", DoseValue = 500m, DoseUnit = "mg", Frequency = "Three times daily",
            AllergyOutcome = AllergyOutcomes.NoRecordedMatch, AllergyListState = AllergyListStates.Established, AllergyMatches = null, ReviewNote = null,
            IdempotencyKey = $"rx-{Guid.NewGuid():N}", RequestHash = Hash, CorrelationId = Guid.NewGuid(), PrescriberUserId = S.Actor, CreatedAtUtc = DateTimeOffset.UtcNow,
        };
        change?.Invoke(row);
        return row;
    }

    private async Task AddAsync(params Prescription[] rows)
    {
        await using var db = Fixture.CreateContext();
        db.Prescriptions.AddRange(rows);
        await db.SaveChangesAsync();
    }

    private static async Task<Microsoft.Data.SqlClient.SqlException> Refusal(Func<Task> action)
    {
        var e = await Assert.ThrowsAnyAsync<Exception>(action);
        return Assert.IsType<Microsoft.Data.SqlClient.SqlException>(e is DbUpdateException { InnerException: not null } d ? d.InnerException : e);
    }

    private async Task<int> CountAsync() { await using var db = Fixture.CreateContext(); return await db.Prescriptions.CountAsync(); }

    // ---------- what is accepted ----------

    [Fact]
    public async Task A_correct_entry_is_accepted_and_its_dose_keeps_three_decimals()
    {
        var row = Row(r => r.DoseValue = 12.345m);
        await AddAsync(row);
        await using var db = Fixture.CreateContext();
        var stored = await db.Prescriptions.AsNoTracking().SingleAsync(p => p.Id == row.Id);
        Assert.Equal((12.345m, "mg", "Three times daily", AllergyOutcomes.NoRecordedMatch), (stored.DoseValue, stored.DoseUnit, stored.Frequency, stored.AllergyOutcome));
    }

    [Fact]
    public async Task An_entry_that_needed_review_is_accepted_when_it_carries_the_prescribers_note()
    {
        await AddAsync(
            Row(r => { r.AllergyOutcome = AllergyOutcomes.RecordedAllergyMatch; r.AllergyMatches = "Penicillin (severe), reaction: Hives"; r.ReviewNote = "Childhood rash; tolerated since."; }),
            Row(r => { r.AllergyOutcome = AllergyOutcomes.NotEstablished; r.AllergyListState = AllergyListStates.NotEstablished; r.ReviewNote = "Asked the patient: no allergies."; }),
            Row(r => { r.AllergyOutcome = AllergyOutcomes.NotEstablished; r.AllergyListState = AllergyListStates.Unconfirmed; r.ReviewNote = "Confirmed with the patient today."; }),
            Row(r => r.ReviewNote = "An optional note on a clear entry is allowed."));
        Assert.Equal(4, await CountAsync());
    }

    [Fact]
    public async Task The_same_save_key_may_be_used_by_two_different_patients()
    {
        await AddAsync(Row(r => r.IdempotencyKey = "same"), Row(r => r.IdempotencyKey = "same", patient: Bo));
        Assert.Equal(2, await CountAsync());
    }

    // ---------- what the database refuses on its own ----------

    [Theory]
    [InlineData("keyBlank", "CK_Prescriptions_Key")]
    [InlineData("keyControl", "CK_Prescriptions_Key")]
    [InlineData("hashShort", "CK_Prescriptions_Hash")]
    [InlineData("hashUpper", "CK_Prescriptions_Hash")]
    [InlineData("hashNotHex", "CK_Prescriptions_Hash")]
    [InlineData("medicationBlank", "CK_Prescriptions_Medication")]
    [InlineData("medicationOneCharacter", "CK_Prescriptions_Medication")]
    [InlineData("medicationControl", "CK_Prescriptions_Medication")]
    [InlineData("unitUnknown", "CK_Prescriptions_Unit|CK_Prescriptions_Dose")]           // an unknown unit breaks both rules (the dose rule needs a known unit for its ceiling); SQL Server reports whichever it evaluates first
    [InlineData("unitWrongCase", "CK_Prescriptions_Unit|CK_Prescriptions_Dose")]
    [InlineData("frequencyUnknown", "CK_Prescriptions_Frequency")]
    [InlineData("frequencyWrongCase", "CK_Prescriptions_Frequency")]
    [InlineData("doseZero", "CK_Prescriptions_Dose")]
    [InlineData("doseNegative", "CK_Prescriptions_Dose")]
    [InlineData("doseRoundsToZero", "CK_Prescriptions_Dose")]
    [InlineData("outcomeUnknown", "CK_Prescriptions_Allergy")]
    [InlineData("listStateUnknown", "CK_Prescriptions_Allergy")]
    [InlineData("matchWithoutMatches", "CK_Prescriptions_Allergy")]
    [InlineData("matchWithBlankMatches", "CK_Prescriptions_Allergy")]
    [InlineData("noMatchOnAnUnestablishedList", "CK_Prescriptions_Allergy")]
    [InlineData("noMatchWithMatchesText", "CK_Prescriptions_Allergy")]
    [InlineData("notEstablishedOnAnEstablishedList", "CK_Prescriptions_Allergy")]
    [InlineData("notEstablishedWithMatchesText", "CK_Prescriptions_Allergy")]
    [InlineData("noteBlank", "CK_Prescriptions_Review")]
    [InlineData("matchWithoutNote", "CK_Prescriptions_ReviewRequired")]
    [InlineData("notEstablishedWithoutNote", "CK_Prescriptions_ReviewRequired")]
    [InlineData("createdLongAgo", "CK_Prescriptions_Created")]
    public async Task The_database_refuses_an_incorrect_entry_by_itself(string which, string constraint)
    {
        var row = Row(r =>
        {
            switch (which)
            {
                case "keyBlank": r.IdempotencyKey = "   "; break;
                case "keyControl": r.IdempotencyKey = "key\u0001"; break;
                case "hashShort": r.RequestHash = new string('a', 63) + "b"[..0]; r.RequestHash = r.RequestHash.PadRight(64, ' '); break;
                case "hashUpper": r.RequestHash = new string('A', 64); break;
                case "hashNotHex": r.RequestHash = new string('g', 64); break;
                case "medicationBlank": r.Medication = "   "; break;
                case "medicationOneCharacter": r.Medication = "A"; break;
                case "medicationControl": r.Medication = "Amox\u0007icillin"; break;
                case "unitUnknown": r.DoseUnit = "stone"; break;
                case "unitWrongCase": r.DoseUnit = "MG"; break;
                case "frequencyUnknown": r.Frequency = "now and then"; break;
                case "frequencyWrongCase": r.Frequency = "three times daily"; break;
                case "doseZero": r.DoseValue = 0m; break;
                case "doseNegative": r.DoseValue = -1m; break;
                case "doseRoundsToZero": r.DoseValue = 0.0004m; break;                                           // the column keeps three decimals, so this arrives as zero
                case "outcomeUnknown": r.AllergyOutcome = "Clear"; break;
                case "listStateUnknown": r.AllergyListState = "Fine"; break;
                case "matchWithoutMatches": r.AllergyOutcome = AllergyOutcomes.RecordedAllergyMatch; r.ReviewNote = "note"; break;
                case "matchWithBlankMatches": r.AllergyOutcome = AllergyOutcomes.RecordedAllergyMatch; r.AllergyMatches = "  "; r.ReviewNote = "note"; break;
                case "noMatchOnAnUnestablishedList": r.AllergyListState = AllergyListStates.NotEstablished; break;
                case "noMatchWithMatchesText": r.AllergyMatches = "Penicillin"; break;
                case "notEstablishedOnAnEstablishedList": r.AllergyOutcome = AllergyOutcomes.NotEstablished; r.ReviewNote = "note"; break;
                case "notEstablishedWithMatchesText": r.AllergyOutcome = AllergyOutcomes.NotEstablished; r.AllergyListState = AllergyListStates.NotEstablished; r.AllergyMatches = "Penicillin"; r.ReviewNote = "note"; break;
                case "noteBlank": r.ReviewNote = "   "; break;
                case "matchWithoutNote": r.AllergyOutcome = AllergyOutcomes.RecordedAllergyMatch; r.AllergyMatches = "Penicillin"; break;
                case "notEstablishedWithoutNote": r.AllergyOutcome = AllergyOutcomes.NotEstablished; r.AllergyListState = AllergyListStates.NotEstablished; break;
                case "createdLongAgo": r.CreatedAtUtc = new DateTimeOffset(1999, 1, 1, 0, 0, 0, TimeSpan.Zero); break;
                default: throw new ArgumentOutOfRangeException(nameof(which));
            }
        });
        var e = await Refusal(() => AddAsync(row));
        Assert.Contains(constraint.Split('|'), c => e.Message.Contains(c));
        Assert.Equal(0, await CountAsync());
    }

    [Theory]
    [InlineData("mg", 5000, 5000.001)] [InlineData("mcg", 5000, 5000.001)] [InlineData("g", 10, 10.001)] [InlineData("mL", 100, 100.001)] [InlineData("tablet", 10, 10.001)]
    [InlineData("capsule", 10, 10.001)] [InlineData("drop", 20, 20.001)] [InlineData("puff", 20, 20.001)] [InlineData("unit", 1000, 1000.001)]
    public async Task A_dose_above_the_typing_ceiling_of_its_unit_is_refused_by_the_database_and_one_at_the_ceiling_is_accepted(string unit, double ceiling, double above)
    {
        await AddAsync(Row(r => { r.DoseUnit = unit; r.DoseValue = (decimal)ceiling; }));
        var e = await Refusal(() => AddAsync(Row(r => { r.DoseUnit = unit; r.DoseValue = (decimal)above; })));
        Assert.Contains("CK_Prescriptions_Dose", e.Message);
        Assert.Equal(1, await CountAsync());
    }

    [Fact]
    public async Task The_same_save_key_cannot_save_a_second_prescription_for_one_patient()
    {
        await AddAsync(Row(r => r.IdempotencyKey = "once"));
        var e = await Refusal(() => AddAsync(Row(r => r.IdempotencyKey = "once")));
        Assert.Contains("IX_Prescriptions_PatientId_IdempotencyKey", e.Message);
        Assert.Equal(1, await CountAsync());
    }

    [Fact]
    public async Task A_prescription_for_a_patient_or_by_a_prescriber_that_does_not_exist_is_refused()
    {
        Assert.Contains("FK_Prescriptions", (await Refusal(() => AddAsync(Row(patient: Guid.NewGuid())))).Message);
        Assert.Contains("FK_Prescriptions", (await Refusal(() => AddAsync(Row(r => r.PrescriberUserId = Guid.NewGuid())))).Message);
        Assert.Equal(0, await CountAsync());
    }

    // ---------- append-only ----------

    [Fact]
    public async Task A_prescription_can_be_neither_changed_nor_deleted()
    {
        var row = Row();
        await AddAsync(row);
        await using var db = Fixture.CreateContext();
        var update = await Refusal(() => db.Database.ExecuteSqlRawAsync("UPDATE [Prescriptions] SET [DoseValue] = 1"));
        var delete = await Refusal(() => db.Database.ExecuteSqlRawAsync("DELETE FROM [Prescriptions]"));
        Assert.Equal((51174, 51174), (update.Number, delete.Number));
        await using var check = Fixture.CreateContext();
        Assert.Equal(500m, (await check.Prescriptions.AsNoTracking().SingleAsync(p => p.Id == row.Id)).DoseValue);
    }

    // ---------- the migration ----------

    [Fact]
    public async Task The_migration_rolls_back_and_forward_while_nothing_is_recorded_and_refuses_to_roll_back_over_a_prescription()
    {
        await using (var db = Fixture.CreateContext()) await db.GetInfrastructure().GetRequiredService<IMigrator>().MigrateAsync(Before);
        await using (var db = Fixture.CreateContext())
            Assert.Equal(0, await db.Database.SqlQuery<int>($"SELECT COUNT(*) AS [Value] FROM sys.tables WHERE name = 'Prescriptions'").SingleAsync());
        await using (var db = Fixture.CreateContext()) await db.Database.MigrateAsync();
        await using (var db = Fixture.CreateContext())
        {
            Assert.Equal(1, await db.Database.SqlQuery<int>($"SELECT COUNT(*) AS [Value] FROM sys.tables WHERE name = 'Prescriptions'").SingleAsync());
            Assert.Equal(1, await db.Database.SqlQuery<int>($"SELECT COUNT(*) AS [Value] FROM sys.triggers WHERE name = 'TR_Prescriptions_Immutable'").SingleAsync());
        }

        await AddAsync(Row());
        var e = await Refusal(async () => { await using var db = Fixture.CreateContext(); await db.GetInfrastructure().GetRequiredService<IMigrator>().MigrateAsync(Before); });
        Assert.Equal(51174, e.Number);
        Assert.Equal(1, await CountAsync());                                                                  // the history is still there
    }
}
