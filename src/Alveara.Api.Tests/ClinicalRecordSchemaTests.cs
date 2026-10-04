using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using Alveara.Api.Architecture.Clinical;
using Xunit;
using static Alveara.Api.Tests.SchedulingTestSupport;

namespace Alveara.Api.Tests;

/// <summary>
/// ALV-005-C01 persistence, against real SQL Server: the database itself keeps the longitudinal record, vitals, notes and templates honest, independently of any application
/// code. It accepts valid rows; refuses states outside the model (case-sensitively, and per kind for item statuses); never deletes a clinical record; makes item history and
/// the record's event history append-only; refuses any edit of a recorded vital sign (only voiding is allowed); and refuses note and vitals writes on a finalized encounter.
/// </summary>
public class ClinicalRecordSchemaTests : IAsyncLifetime
{
    private readonly TestDatabaseFixture _fixture = new();
    private SchedulingTestSupport _s = null!;
    private Guid _ann;
    private static readonly DateTimeOffset At = new(2030, 1, 14, 15, 0, 0, TimeSpan.Zero);

    public async Task InitializeAsync()
    {
        await _fixture.InitializeAsync();
        _s = new SchedulingTestSupport(_fixture);
        await _s.ArrangeAsync();
        _ann = await _s.PatientAsync();
    }
    public Task DisposeAsync() => _fixture.DisposeAsync();

    private async Task<T> ReadAsync<T>(Func<Alveara.Api.Data.AlveraDbContext, Task<T>> read)
    {
        await using var db = _fixture.CreateContext();
        return await read(db);
    }

    private static async Task<SqlException> RefusedAsync(Func<Task> write)
    {
        var ex = await Assert.ThrowsAsync<DbUpdateException>(write);
        return Assert.IsType<SqlException>(ex.InnerException);
    }

    private async Task<Guid> ItemAsync(string kind = EncounterEntryKinds.Allergy, string name = "Penicillin", string status = "Active")
    {
        await using var db = _fixture.CreateContext();
        var item = new ClinicalRecordItem { Id = Guid.NewGuid(), PatientId = _ann, Kind = kind, Name = name, Status = status, CreatedAtUtc = At };
        db.ClinicalRecordItems.Add(item);
        await db.SaveChangesAsync();
        return item.Id;
    }

    private async Task<Guid> DraftAsync()
    {
        await using var db = _fixture.CreateContext();
        var e = new Encounter { Id = Guid.NewGuid(), PatientId = _ann, EncounterAtUtc = At, Status = EncounterStatuses.Draft, CreatedAtUtc = At };
        db.Encounters.Add(e);
        await db.SaveChangesAsync();
        return e.Id;
    }

    private async Task FinalizeAsync(Guid encounterId)
    {
        await using var db = _fixture.CreateContext();
        var e = await db.Encounters.SingleAsync(x => x.Id == encounterId);
        e.Status = EncounterStatuses.Finalized; e.FinalizedAtUtc = At.AddMinutes(30); e.FinalizedByUserId = Guid.NewGuid();
        await db.SaveChangesAsync();
    }

    private static EncounterVitals Vitals(Guid encounterId, Guid patient, string key = "k", int? sys = 120, int? dia = 80, int? pulse = 70) => new()
    {
        Id = Guid.NewGuid(), EncounterId = encounterId, PatientId = patient, MeasuredAtUtc = At, SystolicMmHg = sys, DiastolicMmHg = dia, PulseBpm = pulse, ClientKey = key, CreatedAtUtc = At,
    };

    // ---------- items ----------

    [Fact]
    public async Task The_database_accepts_items_of_every_kind_with_the_statuses_that_fit_the_kind()
    {
        await ItemAsync(EncounterEntryKinds.MedicalHistory, "Asthma", "Resolved");
        await ItemAsync(EncounterEntryKinds.DentalHistory, "Bruxism", "Inactive");
        await ItemAsync(EncounterEntryKinds.Allergy, "Latex", "Resolved");
        await ItemAsync(EncounterEntryKinds.Medication, "Aspirin", "Discontinued");
        Assert.Equal(4, await _s.CountAsync(db => db.ClinicalRecordItems));
    }

    [Theory]
    [InlineData(EncounterEntryKinds.Medication, "Resolved")]
    [InlineData(EncounterEntryKinds.Allergy, "Discontinued")]
    [InlineData(EncounterEntryKinds.Allergy, "active")]          // case-sensitive: the default collation would accept it
    [InlineData(EncounterEntryKinds.Allergy, "Gone")]
    [InlineData("allergy", "Active")]
    [InlineData("Vitals", "Active")]
    public async Task The_database_refuses_a_status_that_does_not_fit_the_kind_and_a_kind_outside_the_model(string kind, string status)
    {
        var ex = await RefusedAsync(() => ItemAsync(kind, "Thing", status));
        Assert.Equal(547, ex.Number); // a check constraint refused it
        Assert.Equal(0, await _s.CountAsync(db => db.ClinicalRecordItems));
    }

    [Fact]
    public async Task The_database_refuses_a_blank_name_and_an_unknown_severity()
    {
        Assert.Equal(547, (await RefusedAsync(() => ItemAsync(name: "   "))).Number);
        await using var db = _fixture.CreateContext();
        db.ClinicalRecordItems.Add(new ClinicalRecordItem { Id = Guid.NewGuid(), PatientId = _ann, Kind = EncounterEntryKinds.Allergy, Name = "Latex", Status = "Active", Severity = "mild", CreatedAtUtc = At });
        Assert.Equal(547, (await RefusedAsync(() => db.SaveChangesAsync())).Number);
    }

    [Fact]
    public async Task One_live_item_per_name_in_a_section_but_a_removed_name_can_be_entered_again()
    {
        var first = await ItemAsync();
        Assert.Equal(2601, (await RefusedAsync(() => ItemAsync(name: "PENICILLIN"))).Number); // a unique index, matching names case-insensitively
        await using (var db = _fixture.CreateContext())
        {
            var item = await db.ClinicalRecordItems.SingleAsync(i => i.Id == first);
            item.RemovedAtUtc = At;
            await db.SaveChangesAsync();
        }
        await ItemAsync();                                                                       // now allowed
        await ItemAsync(EncounterEntryKinds.Medication, "Penicillin");                           // and another kind is its own section
        Assert.Equal(3, await _s.CountAsync(db => db.ClinicalRecordItems));
    }

    [Fact]
    public async Task A_clinical_record_item_is_never_deleted()
    {
        var id = await ItemAsync();
        await using var db = _fixture.CreateContext();
        db.ClinicalRecordItems.Remove(await db.ClinicalRecordItems.SingleAsync(i => i.Id == id));
        Assert.Equal(51040, (await RefusedAsync(() => db.SaveChangesAsync())).Number);
        Assert.Equal(1, await _s.CountAsync(d => d.ClinicalRecordItems));
    }

    [Fact]
    public async Task Item_history_is_append_only_and_version_numbers_are_unique_per_item()
    {
        var id = await ItemAsync();
        Guid version;
        await using (var db = _fixture.CreateContext())
        {
            var v = new ClinicalRecordItemVersion { Id = Guid.NewGuid(), ItemId = id, PatientId = _ann, VersionNumber = 1, ChangeType = "Added", Kind = "Allergy", Name = "Penicillin", Status = "Active", OccurredAtUtc = At };
            db.ClinicalRecordItemVersions.Add(v);
            await db.SaveChangesAsync();
            version = v.Id;
        }
        await using (var db = _fixture.CreateContext())
        {
            db.ClinicalRecordItemVersions.Add(new ClinicalRecordItemVersion { Id = Guid.NewGuid(), ItemId = id, PatientId = _ann, VersionNumber = 1, ChangeType = "Changed", Kind = "Allergy", Name = "x", Status = "Active", OccurredAtUtc = At });
            Assert.Equal(2601, (await RefusedAsync(() => db.SaveChangesAsync())).Number);
        }
        await using (var db = _fixture.CreateContext())
        {
            (await db.ClinicalRecordItemVersions.SingleAsync(v => v.Id == version)).Name = "Rewritten";
            Assert.Equal(51041, (await RefusedAsync(() => db.SaveChangesAsync())).Number);
        }
        await using (var db = _fixture.CreateContext())
        {
            db.ClinicalRecordItemVersions.Remove(await db.ClinicalRecordItemVersions.SingleAsync(v => v.Id == version));
            Assert.Equal(51041, (await RefusedAsync(() => db.SaveChangesAsync())).Number);
        }
        await using (var db = _fixture.CreateContext())
        {
            db.ClinicalRecordItemVersions.Add(new ClinicalRecordItemVersion { Id = Guid.NewGuid(), ItemId = id, PatientId = _ann, VersionNumber = 2, ChangeType = "Edited", Kind = "Allergy", Name = "x", Status = "Active", OccurredAtUtc = At });
            Assert.Equal(547, (await RefusedAsync(() => db.SaveChangesAsync())).Number); // a change type outside the model
        }
    }

    [Fact]
    public async Task The_records_event_history_is_append_only()
    {
        Guid id;
        await using (var db = _fixture.CreateContext())
        {
            var e = new ClinicalRecordEvent { Id = Guid.NewGuid(), PatientId = _ann, Section = "Allergy", EventType = "ItemAdded", OccurredAtUtc = At, Detail = "Allergies: item added." };
            db.ClinicalRecordEvents.Add(e);
            await db.SaveChangesAsync();
            id = e.Id;
        }
        await using (var db = _fixture.CreateContext())
        {
            (await db.ClinicalRecordEvents.SingleAsync(e => e.Id == id)).Detail = "Edited";
            Assert.Equal(51042, (await RefusedAsync(() => db.SaveChangesAsync())).Number);
        }
        await using (var db = _fixture.CreateContext())
        {
            db.ClinicalRecordEvents.Remove(await db.ClinicalRecordEvents.SingleAsync(e => e.Id == id));
            Assert.Equal(51042, (await RefusedAsync(() => db.SaveChangesAsync())).Number);
        }
    }

    [Theory]
    [InlineData("Reviewed", true)]
    [InlineData("NoneKnown", true)]
    [InlineData("Unknown", true)]
    [InlineData("NotReviewed", false)]   // "not reviewed" is the absence of a row, never a stored value
    [InlineData("reviewed", false)]
    public async Task A_section_review_is_one_of_three_stored_states_and_there_is_one_per_section(string state, bool accepted)
    {
        await using var db = _fixture.CreateContext();
        db.ClinicalSectionReviews.Add(new ClinicalSectionReview { Id = Guid.NewGuid(), PatientId = _ann, Section = "Allergy", State = state, ReviewedAtUtc = At });
        if (accepted)
        {
            await db.SaveChangesAsync();
            await using var again = _fixture.CreateContext();
            again.ClinicalSectionReviews.Add(new ClinicalSectionReview { Id = Guid.NewGuid(), PatientId = _ann, Section = "Allergy", State = "Unknown", ReviewedAtUtc = At });
            Assert.Equal(2601, (await RefusedAsync(() => again.SaveChangesAsync())).Number);
        }
        else Assert.Equal(547, (await RefusedAsync(() => db.SaveChangesAsync())).Number);
    }

    // ---------- vitals ----------

    [Fact]
    public async Task The_database_accepts_vitals_with_attribution_and_refuses_a_reading_with_no_values_or_half_a_blood_pressure()
    {
        var enc = await DraftAsync();
        await using (var db = _fixture.CreateContext())
        {
            db.EncounterVitals.Add(Vitals(enc, _ann));
            db.EncounterVitals.Add(Vitals(enc, _ann, "pulse-only", null, null, 64));
            await db.SaveChangesAsync();
        }
        foreach (var bad in new[] { Vitals(enc, _ann, "none", null, null, null), Vitals(enc, _ann, "half", 120, null, 70), Vitals(enc, _ann, "inverted", 80, 120, 70), Vitals(enc, _ann, "equal", 100, 100, 70) })
        {
            await using var db = _fixture.CreateContext();
            db.EncounterVitals.Add(bad);
            Assert.Equal(547, (await RefusedAsync(() => db.SaveChangesAsync())).Number);
        }
        Assert.Equal(2, await _s.CountAsync(db => db.EncounterVitals));
    }

    [Fact]
    public async Task A_vitals_key_is_unique_per_encounter_and_a_void_needs_who_when_and_why()
    {
        var enc = await DraftAsync();
        await using (var db = _fixture.CreateContext())
        {
            db.EncounterVitals.Add(Vitals(enc, _ann, "same"));
            await db.SaveChangesAsync();
        }
        await using (var db = _fixture.CreateContext())
        {
            db.EncounterVitals.Add(Vitals(enc, _ann, "same"));
            Assert.Equal(2601, (await RefusedAsync(() => db.SaveChangesAsync())).Number);
        }
        await using (var db = _fixture.CreateContext())
        {
            (await db.EncounterVitals.SingleAsync()).VoidedAtUtc = At;      // no reason, no voider: the stamp is all-or-nothing
            Assert.Equal(547, (await RefusedAsync(() => db.SaveChangesAsync())).Number);
        }
    }

    [Fact]
    public async Task A_recorded_vital_sign_can_only_be_voided_never_edited_or_deleted()
    {
        var enc = await DraftAsync();
        await using (var db = _fixture.CreateContext())
        {
            db.EncounterVitals.Add(Vitals(enc, _ann));
            await db.SaveChangesAsync();
        }
        await using (var db = _fixture.CreateContext())
        {
            (await db.EncounterVitals.SingleAsync()).PulseBpm = 99;
            Assert.Equal(51048, (await RefusedAsync(() => db.SaveChangesAsync())).Number);
        }
        await using (var db = _fixture.CreateContext())
        {
            db.EncounterVitals.Remove(await db.EncounterVitals.SingleAsync());
            Assert.Equal(51046, (await RefusedAsync(() => db.SaveChangesAsync())).Number);
        }
        await using (var db = _fixture.CreateContext())
        {
            var v = await db.EncounterVitals.SingleAsync();
            v.VoidedAtUtc = At; v.VoidedByUserId = Guid.NewGuid(); v.VoidReason = "Wrong patient";   // voiding is the one allowed change
            await db.SaveChangesAsync();
        }
        Assert.Equal(70, (await ReadAsync(db => db.EncounterVitals.Select(v => v.PulseBpm).SingleAsync())));
    }

    [Fact]
    public async Task Vitals_cannot_be_added_to_or_voided_on_a_finalized_encounter()
    {
        var enc = await DraftAsync();
        await using (var db = _fixture.CreateContext())
        {
            db.EncounterVitals.Add(Vitals(enc, _ann));
            await db.SaveChangesAsync();
        }
        await FinalizeAsync(enc);
        await using (var db = _fixture.CreateContext())
        {
            db.EncounterVitals.Add(Vitals(enc, _ann, "late"));
            Assert.Equal(51045, (await RefusedAsync(() => db.SaveChangesAsync())).Number);
        }
        await using (var db = _fixture.CreateContext())
        {
            var v = await db.EncounterVitals.SingleAsync();
            v.VoidedAtUtc = At; v.VoidedByUserId = Guid.NewGuid(); v.VoidReason = "Too late";
            Assert.Equal(51045, (await RefusedAsync(() => db.SaveChangesAsync())).Number);
        }
    }

    // ---------- notes ----------

    [Fact]
    public async Task Notes_are_one_per_section_checked_case_sensitively_never_deleted_and_frozen_when_finalized()
    {
        var enc = await DraftAsync();
        await using (var db = _fixture.CreateContext())
        {
            db.EncounterNotes.Add(new EncounterNote { Id = Guid.NewGuid(), EncounterId = enc, Section = "Subjective", Body = "Pain", CreatedAtUtc = At });
            await db.SaveChangesAsync();
        }
        await using (var db = _fixture.CreateContext())
        {
            db.EncounterNotes.Add(new EncounterNote { Id = Guid.NewGuid(), EncounterId = enc, Section = "Subjective", Body = "Again", CreatedAtUtc = At });
            Assert.Equal(2601, (await RefusedAsync(() => db.SaveChangesAsync())).Number);
        }
        await using (var db = _fixture.CreateContext())
        {
            db.EncounterNotes.Add(new EncounterNote { Id = Guid.NewGuid(), EncounterId = enc, Section = "objective", Body = "x", CreatedAtUtc = At });
            Assert.Equal(547, (await RefusedAsync(() => db.SaveChangesAsync())).Number);
        }
        await using (var db = _fixture.CreateContext())
        {
            db.EncounterNotes.Remove(await db.EncounterNotes.SingleAsync());
            Assert.Equal(51044, (await RefusedAsync(() => db.SaveChangesAsync())).Number);
        }
        await FinalizeAsync(enc);
        await using (var db = _fixture.CreateContext())
        {
            (await db.EncounterNotes.SingleAsync()).Body = "Rewritten";
            Assert.Equal(51043, (await RefusedAsync(() => db.SaveChangesAsync())).Number);
        }
        await using (var db = _fixture.CreateContext())
        {
            db.EncounterNotes.Add(new EncounterNote { Id = Guid.NewGuid(), EncounterId = enc, Section = "Plan", Body = "New", CreatedAtUtc = At });
            Assert.Equal(51043, (await RefusedAsync(() => db.SaveChangesAsync())).Number);
        }
    }

    // ---------- templates ----------

    [Fact]
    public async Task Templates_have_unique_names_checked_sections_and_are_never_deleted()
    {
        Guid id;
        await using (var db = _fixture.CreateContext())
        {
            var t = new NoteTemplate { Id = Guid.NewGuid(), Name = "SOAP", CreatedAtUtc = At };
            db.NoteTemplates.Add(t);
            db.NoteTemplateSections.Add(new NoteTemplateSection { Id = Guid.NewGuid(), TemplateId = t.Id, Section = "Plan", SortOrder = 0, Required = true });
            await db.SaveChangesAsync();
            id = t.Id;
        }
        await using (var db = _fixture.CreateContext())
        {
            db.NoteTemplates.Add(new NoteTemplate { Id = Guid.NewGuid(), Name = "soap", CreatedAtUtc = At });
            Assert.Equal(2601, (await RefusedAsync(() => db.SaveChangesAsync())).Number);
        }
        await using (var db = _fixture.CreateContext())
        {
            db.NoteTemplateSections.Add(new NoteTemplateSection { Id = Guid.NewGuid(), TemplateId = id, Section = "Plan", SortOrder = 1 });
            Assert.Equal(2601, (await RefusedAsync(() => db.SaveChangesAsync())).Number);
        }
        await using (var db = _fixture.CreateContext())
        {
            db.NoteTemplateSections.Add(new NoteTemplateSection { Id = Guid.NewGuid(), TemplateId = id, Section = "objective", SortOrder = 1 });
            Assert.Equal(547, (await RefusedAsync(() => db.SaveChangesAsync())).Number);
        }
        await using (var db = _fixture.CreateContext())
        {
            db.NoteTemplates.Remove(await db.NoteTemplates.SingleAsync());
            db.NoteTemplateSections.RemoveRange(await db.NoteTemplateSections.ToListAsync());
            Assert.Equal(51047, (await RefusedAsync(() => db.SaveChangesAsync())).Number);
        }
    }

    // ---------- the encounter columns and the amended section ----------

    [Fact]
    public async Task A_signature_needs_both_who_and_when_and_an_addendum_can_name_the_section_it_amends()
    {
        var enc = await DraftAsync();
        await using (var db = _fixture.CreateContext())
        {
            (await db.Encounters.SingleAsync(e => e.Id == enc)).SignedAtUtc = At;      // when, but not who
            Assert.Equal(547, (await RefusedAsync(() => db.SaveChangesAsync())).Number);
        }
        await using (var db = _fixture.CreateContext())
        {
            var e = await db.Encounters.SingleAsync(x => x.Id == enc);
            e.SignedAtUtc = At; e.SignedByUserId = Guid.NewGuid();
            await db.SaveChangesAsync();                                                // a signed draft is still a draft to the database
        }
        await using (var db = _fixture.CreateContext())
        {
            var e = await db.Encounters.SingleAsync(x => x.Id == enc);
            e.Status = EncounterStatuses.Finalized; e.FinalizedAtUtc = At.AddMinutes(5); e.FinalizedByUserId = Guid.NewGuid();
            await db.SaveChangesAsync();
            db.EncounterAddenda.Add(new EncounterAddendum { Id = Guid.NewGuid(), EncounterId = enc, Text = "Allergy corrected", ClientKey = "k", Section = "Allergy", CreatedAtUtc = At.AddMinutes(10) });
            await db.SaveChangesAsync();
        }
        Assert.Equal("Allergy", await ReadAsync(db => db.EncounterAddenda.Select(a => a.Section).SingleAsync()));
    }
}
