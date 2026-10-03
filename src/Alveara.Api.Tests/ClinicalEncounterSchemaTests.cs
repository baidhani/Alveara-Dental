using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using Alveara.Api.Architecture.Clinical;
using Xunit;
using static Alveara.Api.Tests.SchedulingTestSupport;

namespace Alveara.Api.Tests;

/// <summary>
/// STORY-005 persistence, against real SQL Server: the database itself keeps clinical documentation honest, independently of any application code.
/// It accepts a draft encounter with structured history, allergies and medications; refuses states and kinds outside the model (case-sensitively);
/// refuses every change to a finalized encounter, its entries and its section reviews; accepts an addendum only on a finalized encounter and never lets
/// it (or the history) change; and never deletes a clinical record. Behaviour (who may do what, validation messages, audit) is tested with the service.
/// </summary>
public class ClinicalEncounterSchemaTests : IAsyncLifetime
{
    private readonly TestDatabaseFixture _fixture = new();
    private SchedulingTestSupport _s = null!;
    private Guid _ann;

    public async Task InitializeAsync()
    {
        await _fixture.InitializeAsync();
        _s = new SchedulingTestSupport(_fixture);
        await _s.ArrangeAsync();
        _ann = await _s.PatientAsync();
    }
    public Task DisposeAsync() => _fixture.DisposeAsync();

    private static readonly DateTimeOffset Seen = new(2030, 1, 14, 15, 0, 0, TimeSpan.Zero);

    private async Task<Guid> DraftAsync(Guid? appointmentId = null)
    {
        await using var db = _fixture.CreateContext();
        var e = new Encounter { Id = Guid.NewGuid(), PatientId = _ann, AppointmentId = appointmentId, EncounterAtUtc = Seen, Status = EncounterStatuses.Draft, CreatedAtUtc = Seen };
        db.Encounters.Add(e);
        await db.SaveChangesAsync();
        return e.Id;
    }

    private async Task<Guid> AddEntryAsync(Guid encounterId, string kind = EncounterEntryKinds.Allergy, string name = "Penicillin")
    {
        await using var db = _fixture.CreateContext();
        var entry = new EncounterEntry { Id = Guid.NewGuid(), EncounterId = encounterId, Kind = kind, Name = name, CreatedAtUtc = Seen };
        db.EncounterEntries.Add(entry);
        await db.SaveChangesAsync();
        return entry.Id;
    }

    private async Task FinalizeAsync(Guid encounterId)
    {
        await using var db = _fixture.CreateContext();
        var e = await db.Encounters.SingleAsync(x => x.Id == encounterId);
        e.Status = EncounterStatuses.Finalized;
        e.FinalizedAtUtc = Seen.AddMinutes(30);
        e.FinalizedByUserId = Guid.NewGuid();
        await db.SaveChangesAsync();
    }

    /// <summary>Runs a write the database must refuse and returns the SQL Server error (its number says which rule refused it).</summary>
    private static async Task<SqlException> RefusedAsync(Func<Task> write)
    {
        var ex = await Assert.ThrowsAsync<DbUpdateException>(write);
        return Assert.IsType<SqlException>(ex.InnerException);
    }

    // ---------- what the database accepts ----------

    [Fact]
    public async Task A_draft_encounter_holds_all_four_kinds_of_entry_a_section_review_and_its_history()
    {
        var id = await DraftAsync();
        await using (var db = _fixture.CreateContext())
        {
            db.EncounterEntries.AddRange(
                new EncounterEntry { Id = Guid.NewGuid(), EncounterId = id, Kind = EncounterEntryKinds.MedicalHistory, Name = "Hypertension", Detail = "Controlled", CreatedAtUtc = Seen },
                new EncounterEntry { Id = Guid.NewGuid(), EncounterId = id, Kind = EncounterEntryKinds.DentalHistory, Name = "Root canal 2019", CreatedAtUtc = Seen },
                new EncounterEntry { Id = Guid.NewGuid(), EncounterId = id, Kind = EncounterEntryKinds.Allergy, Name = "Penicillin", Reaction = "Hives", Severity = EncounterSeverities.Moderate, CreatedAtUtc = Seen },
                new EncounterEntry { Id = Guid.NewGuid(), EncounterId = id, Kind = EncounterEntryKinds.Medication, Name = "Lisinopril", Dose = "10 mg", Frequency = "daily", CreatedAtUtc = Seen });
            db.EncounterEvents.Add(new EncounterEvent { Id = Guid.NewGuid(), EncounterId = id, PatientId = _ann, EventType = EncounterEventTypes.Created, OccurredAtUtc = Seen });
            await db.SaveChangesAsync();
        }
        await using var read = _fixture.CreateContext();
        var entries = await read.EncounterEntries.Where(x => x.EncounterId == id).ToListAsync();
        Assert.Equal(EncounterEntryKinds.All.OrderBy(k => k), entries.Select(e => e.Kind).OrderBy(k => k));
        Assert.Equal("Hives", entries.Single(e => e.Kind == EncounterEntryKinds.Allergy).Reaction);
        Assert.Single(await read.EncounterEvents.Where(x => x.EncounterId == id).ToListAsync());
    }

    [Fact]
    public async Task A_section_can_be_reviewed_as_none_reported_once_and_a_draft_encounter_can_change_and_move_its_row_version()
    {
        var id = await DraftAsync();
        await using (var db = _fixture.CreateContext())
        {
            db.EncounterSectionMarks.Add(new EncounterSectionMark { Id = Guid.NewGuid(), EncounterId = id, Section = EncounterEntryKinds.Medication, State = EncounterSectionStates.NoneReported, MarkedAtUtc = Seen });
            await db.SaveChangesAsync();
        }
        byte[] before;
        await using (var db = _fixture.CreateContext())
        {
            var e = await db.Encounters.SingleAsync(x => x.Id == id);
            before = e.RowVersion;
            e.UpdatedAtUtc = Seen.AddMinutes(1);
            await db.SaveChangesAsync();
        }
        await using var read = _fixture.CreateContext();
        Assert.NotEqual(before, (await read.Encounters.SingleAsync(x => x.Id == id)).RowVersion);
        // a marked section can be unmarked while the encounter is still a draft
        read.EncounterSectionMarks.RemoveRange(read.EncounterSectionMarks.Where(m => m.EncounterId == id));
        await read.SaveChangesAsync();
        Assert.Empty(await read.EncounterSectionMarks.Where(m => m.EncounterId == id).ToListAsync());
    }

    [Fact]
    public async Task Two_editors_of_the_same_draft_cannot_both_win()
    {
        var id = await DraftAsync();
        await using var first = _fixture.CreateContext();
        await using var second = _fixture.CreateContext();
        var a = await first.Encounters.SingleAsync(x => x.Id == id);
        var b = await second.Encounters.SingleAsync(x => x.Id == id);
        a.UpdatedAtUtc = Seen.AddMinutes(1);
        await first.SaveChangesAsync();
        b.UpdatedAtUtc = Seen.AddMinutes(2);
        await Assert.ThrowsAsync<DbUpdateConcurrencyException>(() => second.SaveChangesAsync());
    }

    [Fact]
    public async Task A_removed_entry_is_kept_with_who_and_when_so_nothing_typed_is_lost()
    {
        var id = await DraftAsync();
        var entryId = await AddEntryAsync(id);
        await using (var db = _fixture.CreateContext())
        {
            var entry = await db.EncounterEntries.SingleAsync(x => x.Id == entryId);
            entry.RemovedAtUtc = Seen.AddMinutes(5);
            entry.RemovedByUserId = Guid.NewGuid();
            await db.SaveChangesAsync();
        }
        await using var read = _fixture.CreateContext();
        var kept = await read.EncounterEntries.SingleAsync(x => x.Id == entryId);
        Assert.Equal("Penicillin", kept.Name);
        Assert.NotNull(kept.RemovedAtUtc);
    }

    // ---------- what the database refuses: values outside the model ----------

    [Theory]
    [InlineData("draft")]
    [InlineData("finalized")]
    [InlineData("Signed")]
    [InlineData("")]
    public async Task The_database_refuses_any_encounter_status_but_Draft_and_Finalized_case_sensitively(string status)
    {
        await using var db = _fixture.CreateContext();
        db.Encounters.Add(new Encounter { Id = Guid.NewGuid(), PatientId = _ann, EncounterAtUtc = Seen, Status = status, CreatedAtUtc = Seen });
        Assert.Equal(547, (await RefusedAsync(() => db.SaveChangesAsync())).Number);
    }

    [Fact]
    public async Task A_finalized_encounter_must_say_who_finalized_it_and_when_and_a_draft_must_not()
    {
        await using (var db = _fixture.CreateContext())
        {
            db.Encounters.Add(new Encounter { Id = Guid.NewGuid(), PatientId = _ann, EncounterAtUtc = Seen, Status = EncounterStatuses.Finalized, CreatedAtUtc = Seen });
            Assert.Equal(547, (await RefusedAsync(() => db.SaveChangesAsync())).Number);
        }
        await using (var db = _fixture.CreateContext())
        {
            db.Encounters.Add(new Encounter { Id = Guid.NewGuid(), PatientId = _ann, EncounterAtUtc = Seen, Status = EncounterStatuses.Draft, CreatedAtUtc = Seen, FinalizedAtUtc = Seen, FinalizedByUserId = Guid.NewGuid() });
            Assert.Equal(547, (await RefusedAsync(() => db.SaveChangesAsync())).Number);
        }
    }

    [Fact]
    public async Task An_encounter_needs_a_real_patient()
    {
        await using var db = _fixture.CreateContext();
        db.Encounters.Add(new Encounter { Id = Guid.NewGuid(), PatientId = Guid.NewGuid(), EncounterAtUtc = Seen, Status = EncounterStatuses.Draft, CreatedAtUtc = Seen });
        Assert.Equal(547, (await RefusedAsync(() => db.SaveChangesAsync())).Number);
    }

    [Fact]
    public async Task An_appointment_has_at_most_one_encounter_but_encounters_without_an_appointment_are_unlimited()
    {
        var appointment = (await _s.ScheduleAsync(_s.Request(_ann, At(9)))).Appointment.Id;
        await DraftAsync(appointment);
        await using (var db = _fixture.CreateContext())
        {
            db.Encounters.Add(new Encounter { Id = Guid.NewGuid(), PatientId = _ann, AppointmentId = appointment, EncounterAtUtc = Seen, Status = EncounterStatuses.Draft, CreatedAtUtc = Seen });
            Assert.Contains((await RefusedAsync(() => db.SaveChangesAsync())).Number, new[] { 2601, 2627 });
        }
        await DraftAsync();
        await DraftAsync();
    }

    [Fact]
    public async Task A_retried_start_with_the_same_key_cannot_create_a_second_encounter_but_encounters_without_a_key_are_unlimited()
    {
        await using (var db = _fixture.CreateContext())
        {
            db.Encounters.Add(new Encounter { Id = Guid.NewGuid(), PatientId = _ann, EncounterAtUtc = Seen, Status = EncounterStatuses.Draft, CreatedAtUtc = Seen, StartKey = "start-1" });
            await db.SaveChangesAsync();
        }
        await using (var db = _fixture.CreateContext())
        {
            db.Encounters.Add(new Encounter { Id = Guid.NewGuid(), PatientId = _ann, EncounterAtUtc = Seen, Status = EncounterStatuses.Draft, CreatedAtUtc = Seen, StartKey = "start-1" });
            Assert.Contains((await RefusedAsync(() => db.SaveChangesAsync())).Number, new[] { 2601, 2627 });
        }
        await DraftAsync();
        await DraftAsync();
    }

    [Theory]
    [InlineData("allergy")]
    [InlineData("Condition")]
    [InlineData("")]
    public async Task The_database_refuses_an_entry_kind_outside_the_four_sections(string kind)
    {
        var id = await DraftAsync();
        await using var db = _fixture.CreateContext();
        db.EncounterEntries.Add(new EncounterEntry { Id = Guid.NewGuid(), EncounterId = id, Kind = kind, Name = "x", CreatedAtUtc = Seen });
        Assert.Equal(547, (await RefusedAsync(() => db.SaveChangesAsync())).Number);
    }

    [Theory]
    [InlineData("severe")]
    [InlineData("Critical")]
    public async Task The_database_refuses_a_severity_outside_the_three_levels_but_allows_none(string severity)
    {
        var id = await DraftAsync();
        await using (var db = _fixture.CreateContext())
        {
            db.EncounterEntries.Add(new EncounterEntry { Id = Guid.NewGuid(), EncounterId = id, Kind = EncounterEntryKinds.Allergy, Name = "x", Severity = severity, CreatedAtUtc = Seen });
            Assert.Equal(547, (await RefusedAsync(() => db.SaveChangesAsync())).Number);
        }
        await AddEntryAsync(id); // no severity at all is fine: not known
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public async Task An_entry_cannot_have_a_blank_name(string name)
    {
        var id = await DraftAsync();
        await using var db = _fixture.CreateContext();
        db.EncounterEntries.Add(new EncounterEntry { Id = Guid.NewGuid(), EncounterId = id, Kind = EncounterEntryKinds.Medication, Name = name, CreatedAtUtc = Seen });
        Assert.Equal(547, (await RefusedAsync(() => db.SaveChangesAsync())).Number);
    }

    [Theory]
    [InlineData("medication", "NoneReported")]
    [InlineData("Medication", "noneReported")]
    [InlineData("Medication", "Unknown")]
    [InlineData("Vitals", "NoneReported")]
    public async Task The_database_refuses_a_section_review_outside_the_model(string section, string state)
    {
        var id = await DraftAsync();
        await using var db = _fixture.CreateContext();
        db.EncounterSectionMarks.Add(new EncounterSectionMark { Id = Guid.NewGuid(), EncounterId = id, Section = section, State = state, MarkedAtUtc = Seen });
        Assert.Equal(547, (await RefusedAsync(() => db.SaveChangesAsync())).Number);
    }

    [Fact]
    public async Task A_section_is_reviewed_at_most_once_per_encounter()
    {
        var id = await DraftAsync();
        await using (var db = _fixture.CreateContext())
        {
            db.EncounterSectionMarks.Add(new EncounterSectionMark { Id = Guid.NewGuid(), EncounterId = id, Section = EncounterEntryKinds.Allergy, State = EncounterSectionStates.NoneReported, MarkedAtUtc = Seen });
            await db.SaveChangesAsync();
        }
        await using var again = _fixture.CreateContext();
        again.EncounterSectionMarks.Add(new EncounterSectionMark { Id = Guid.NewGuid(), EncounterId = id, Section = EncounterEntryKinds.Allergy, State = EncounterSectionStates.NoneReported, MarkedAtUtc = Seen });
        Assert.Contains((await RefusedAsync(() => again.SaveChangesAsync())).Number, new[] { 2601, 2627 });
    }

    // ---------- finalized means finalized: the database refuses any change ----------

    [Fact]
    public async Task Finalizing_is_one_update_that_the_database_accepts_and_it_is_recorded()
    {
        var id = await DraftAsync();
        await FinalizeAsync(id);
        await using var read = _fixture.CreateContext();
        var e = await read.Encounters.SingleAsync(x => x.Id == id);
        Assert.Equal(EncounterStatuses.Finalized, e.Status);
        Assert.NotNull(e.FinalizedAtUtc);
        Assert.NotNull(e.FinalizedByUserId);
    }

    [Fact]
    public async Task A_finalized_encounter_cannot_be_changed_or_reopened_and_stays_exactly_as_it_was()
    {
        var id = await DraftAsync();
        await FinalizeAsync(id);
        await using (var db = _fixture.CreateContext())
        {
            var e = await db.Encounters.SingleAsync(x => x.Id == id);
            e.UpdatedAtUtc = Seen.AddHours(2);
            Assert.Equal(51030, (await RefusedAsync(() => db.SaveChangesAsync())).Number);
        }
        await using (var db = _fixture.CreateContext())
        {
            var e = await db.Encounters.SingleAsync(x => x.Id == id);
            e.Status = EncounterStatuses.Draft;
            e.FinalizedAtUtc = null;
            e.FinalizedByUserId = null;
            Assert.Equal(51030, (await RefusedAsync(() => db.SaveChangesAsync())).Number);
        }
        await using var read = _fixture.CreateContext();
        var after = await read.Encounters.SingleAsync(x => x.Id == id);
        Assert.Equal(EncounterStatuses.Finalized, after.Status);
        Assert.Null(after.UpdatedAtUtc); // neither refused write left a trace
    }

    [Fact]
    public async Task The_entries_of_a_finalized_encounter_cannot_be_added_to_or_changed()
    {
        var id = await DraftAsync();
        var existing = await AddEntryAsync(id);
        await FinalizeAsync(id);
        await using (var db = _fixture.CreateContext())
        {
            db.EncounterEntries.Add(new EncounterEntry { Id = Guid.NewGuid(), EncounterId = id, Kind = EncounterEntryKinds.Medication, Name = "Added later", CreatedAtUtc = Seen });
            Assert.Equal(51032, (await RefusedAsync(() => db.SaveChangesAsync())).Number);
        }
        await using (var db = _fixture.CreateContext())
        {
            var entry = await db.EncounterEntries.SingleAsync(x => x.Id == existing);
            entry.Name = "Rewritten";
            Assert.Equal(51032, (await RefusedAsync(() => db.SaveChangesAsync())).Number);
        }
        await using (var db = _fixture.CreateContext())
        {
            var entry = await db.EncounterEntries.SingleAsync(x => x.Id == existing);
            entry.RemovedAtUtc = Seen.AddHours(3); // even marking one removed is a change
            Assert.Equal(51032, (await RefusedAsync(() => db.SaveChangesAsync())).Number);
        }
        await using var read = _fixture.CreateContext();
        Assert.Equal("Penicillin", (await read.EncounterEntries.SingleAsync(x => x.Id == existing)).Name);
        Assert.Single(await read.EncounterEntries.Where(x => x.EncounterId == id).ToListAsync());
    }

    [Fact]
    public async Task The_section_reviews_of_a_finalized_encounter_cannot_be_added_or_removed()
    {
        var id = await DraftAsync();
        await using (var db = _fixture.CreateContext())
        {
            db.EncounterSectionMarks.Add(new EncounterSectionMark { Id = Guid.NewGuid(), EncounterId = id, Section = EncounterEntryKinds.Medication, State = EncounterSectionStates.NoneReported, MarkedAtUtc = Seen });
            await db.SaveChangesAsync();
        }
        await FinalizeAsync(id);
        await using (var db = _fixture.CreateContext())
        {
            db.EncounterSectionMarks.Add(new EncounterSectionMark { Id = Guid.NewGuid(), EncounterId = id, Section = EncounterEntryKinds.Allergy, State = EncounterSectionStates.NoneReported, MarkedAtUtc = Seen });
            Assert.Equal(51034, (await RefusedAsync(() => db.SaveChangesAsync())).Number);
        }
        await using (var db = _fixture.CreateContext())
        {
            db.EncounterSectionMarks.RemoveRange(db.EncounterSectionMarks.Where(m => m.EncounterId == id));
            Assert.Equal(51034, (await RefusedAsync(() => db.SaveChangesAsync())).Number);
        }
    }

    // ---------- an addendum is the only way to add to a finalized encounter ----------

    [Fact]
    public async Task An_addendum_is_refused_on_a_draft_and_accepted_once_the_encounter_is_finalized_beside_the_untouched_original()
    {
        var id = await DraftAsync();
        var entryId = await AddEntryAsync(id);
        await using (var db = _fixture.CreateContext())
        {
            db.EncounterAddenda.Add(new EncounterAddendum { Id = Guid.NewGuid(), EncounterId = id, Text = "Too early", ClientKey = "k1", CreatedAtUtc = Seen });
            Assert.Equal(51035, (await RefusedAsync(() => db.SaveChangesAsync())).Number);
        }
        await FinalizeAsync(id);
        await using (var db = _fixture.CreateContext())
        {
            db.EncounterAddenda.Add(new EncounterAddendum { Id = Guid.NewGuid(), EncounterId = id, Text = "Patient also reports latex sensitivity.", ClientKey = "k1", CreatedAtUtc = Seen.AddDays(1), CreatedByUserId = Guid.NewGuid() });
            await db.SaveChangesAsync();
        }
        await using var read = _fixture.CreateContext();
        Assert.Single(await read.EncounterAddenda.Where(a => a.EncounterId == id).ToListAsync());
        Assert.Equal("Penicillin", (await read.EncounterEntries.SingleAsync(x => x.Id == entryId)).Name); // the original is exactly as it was
        Assert.Equal(EncounterStatuses.Finalized, (await read.Encounters.SingleAsync(x => x.Id == id)).Status);
    }

    [Fact]
    public async Task An_addendum_can_never_be_changed_or_removed()
    {
        var id = await DraftAsync();
        await FinalizeAsync(id);
        var addendumId = Guid.NewGuid();
        await using (var db = _fixture.CreateContext())
        {
            db.EncounterAddenda.Add(new EncounterAddendum { Id = addendumId, EncounterId = id, Text = "Original addendum text", ClientKey = "k1", CreatedAtUtc = Seen });
            await db.SaveChangesAsync();
        }
        await using (var db = _fixture.CreateContext())
        {
            (await db.EncounterAddenda.SingleAsync(a => a.Id == addendumId)).Text = "Rewritten";
            Assert.Equal(51036, (await RefusedAsync(() => db.SaveChangesAsync())).Number);
        }
        await using (var db = _fixture.CreateContext())
        {
            db.EncounterAddenda.Remove(await db.EncounterAddenda.SingleAsync(a => a.Id == addendumId));
            Assert.Equal(51036, (await RefusedAsync(() => db.SaveChangesAsync())).Number);
        }
        await using var read = _fixture.CreateContext();
        Assert.Equal("Original addendum text", (await read.EncounterAddenda.SingleAsync(a => a.Id == addendumId)).Text);
    }

    [Fact]
    public async Task A_retried_addendum_with_the_same_client_key_cannot_be_added_twice_but_a_different_key_can()
    {
        var id = await DraftAsync();
        await FinalizeAsync(id);
        await using (var db = _fixture.CreateContext())
        {
            db.EncounterAddenda.Add(new EncounterAddendum { Id = Guid.NewGuid(), EncounterId = id, Text = "First", ClientKey = "same", CreatedAtUtc = Seen });
            await db.SaveChangesAsync();
        }
        await using (var db = _fixture.CreateContext())
        {
            db.EncounterAddenda.Add(new EncounterAddendum { Id = Guid.NewGuid(), EncounterId = id, Text = "Retry", ClientKey = "same", CreatedAtUtc = Seen });
            Assert.Contains((await RefusedAsync(() => db.SaveChangesAsync())).Number, new[] { 2601, 2627 });
        }
        await using (var db = _fixture.CreateContext())
        {
            db.EncounterAddenda.Add(new EncounterAddendum { Id = Guid.NewGuid(), EncounterId = id, Text = "Second", ClientKey = "other", CreatedAtUtc = Seen });
            await db.SaveChangesAsync();
        }
        await using var read = _fixture.CreateContext();
        Assert.Equal(2, await read.EncounterAddenda.CountAsync(a => a.EncounterId == id));
    }

    [Fact]
    public async Task An_addendum_cannot_be_blank()
    {
        var id = await DraftAsync();
        await FinalizeAsync(id);
        await using var db = _fixture.CreateContext();
        db.EncounterAddenda.Add(new EncounterAddendum { Id = Guid.NewGuid(), EncounterId = id, Text = "   ", ClientKey = "k", CreatedAtUtc = Seen });
        Assert.Equal(547, (await RefusedAsync(() => db.SaveChangesAsync())).Number);
    }

    // ---------- nothing clinical is ever deleted; the history is append-only ----------

    [Fact]
    public async Task A_clinical_record_is_never_deleted_not_even_in_a_draft()
    {
        var id = await DraftAsync();
        var entryId = await AddEntryAsync(id);
        await using (var db = _fixture.CreateContext())
        {
            db.EncounterEntries.Remove(await db.EncounterEntries.SingleAsync(x => x.Id == entryId));
            Assert.Equal(51033, (await RefusedAsync(() => db.SaveChangesAsync())).Number);
        }
        await using (var db = _fixture.CreateContext())
        {
            db.Encounters.Remove(await db.Encounters.SingleAsync(x => x.Id == id));
            Assert.Equal(51031, (await RefusedAsync(() => db.SaveChangesAsync())).Number);
        }
        await using var read = _fixture.CreateContext();
        Assert.True(await read.Encounters.AnyAsync(x => x.Id == id));
        Assert.True(await read.EncounterEntries.AnyAsync(x => x.Id == entryId));
    }

    [Fact]
    public async Task The_history_can_be_added_to_but_never_changed_or_removed()
    {
        var id = await DraftAsync();
        var eventId = Guid.NewGuid();
        await using (var db = _fixture.CreateContext())
        {
            db.EncounterEvents.Add(new EncounterEvent { Id = eventId, EncounterId = id, PatientId = _ann, EventType = EncounterEventTypes.Created, OccurredAtUtc = Seen, Detail = "Encounter started" });
            await db.SaveChangesAsync();
        }
        await using (var db = _fixture.CreateContext())
        {
            (await db.EncounterEvents.SingleAsync(x => x.Id == eventId)).Detail = "Rewritten";
            Assert.Equal(51037, (await RefusedAsync(() => db.SaveChangesAsync())).Number);
        }
        await using (var db = _fixture.CreateContext())
        {
            db.EncounterEvents.Remove(await db.EncounterEvents.SingleAsync(x => x.Id == eventId));
            Assert.Equal(51037, (await RefusedAsync(() => db.SaveChangesAsync())).Number);
        }
        await using var read = _fixture.CreateContext();
        Assert.Equal("Encounter started", (await read.EncounterEvents.SingleAsync(x => x.Id == eventId)).Detail);
    }
}
