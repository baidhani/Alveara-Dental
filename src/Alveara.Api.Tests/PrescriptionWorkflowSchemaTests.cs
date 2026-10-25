using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Microsoft.Extensions.DependencyInjection;
using Alveara.Api.Architecture.Prescribing;
using Xunit;

namespace Alveara.Api.Tests;

/// <summary>
/// ALV-016-C01, the database on its own: rows written straight to the tables, around any service, so the tests prove the database refuses an incomplete or incorrect draft, detail or event by itself, keeps a
/// finalized prescription's history append-only, lets a draft change only while it is a draft, allows one ending event and one amendment per prescription, keeps an amendment inside one patient, and refuses to roll
/// back over recorded workflow history.
/// </summary>
public class PrescriptionWorkflowSchemaTests : SafetyTestBase
{
    private const string AfterPrescriptions = "20261013231207_AddPrescriptions";
    private static readonly string Hash = new('a', 64);

    private Prescription NewPrescription(Guid? patient = null, Action<Prescription>? change = null)
    {
        var row = new Prescription
        {
            Id = Guid.NewGuid(), PatientId = patient ?? Ann, Medication = "Amoxicillin", DoseValue = 500m, DoseUnit = "mg", Frequency = "Three times daily",
            AllergyOutcome = AllergyOutcomes.NoRecordedMatch, AllergyListState = AllergyListStates.Established, IdempotencyKey = $"rx-{Guid.NewGuid():N}", RequestHash = Hash,
            CorrelationId = Guid.NewGuid(), PrescriberUserId = S.Actor, CreatedAtUtc = DateTimeOffset.UtcNow,
        };
        change?.Invoke(row);
        return row;
    }

    private PrescriptionDraft Draft(Action<PrescriptionDraft>? change = null, Guid? patient = null)
    {
        var now = DateTimeOffset.UtcNow;
        var row = new PrescriptionDraft
        {
            Id = Guid.NewGuid(), PatientId = patient ?? Ann, Medication = "Amoxicillin", DoseValue = 500m, DoseUnit = "mg", Frequency = "Three times daily", Instructions = "Take one with food.",
            QuantityValue = 21m, QuantityUnit = "capsule", Refills = 0, SafetyFingerprint = Hash, SafetySnapshot = "AllergyList|Reviewed\nMedicationList|Reviewed", Status = DraftStatuses.Draft,
            CreateKey = $"draft-{Guid.NewGuid():N}", CreateHash = Hash, CreatedByUserId = S.Actor, CreatedAtUtc = now, UpdatedByUserId = S.Actor, UpdatedAtUtc = now,
        };
        change?.Invoke(row);
        return row;
    }

    private static PrescriptionDetail Detail(Guid prescriptionId, Action<PrescriptionDetail>? change = null)
    {
        var row = new PrescriptionDetail
        {
            PrescriptionId = prescriptionId, Instructions = "Take one with food.", QuantityValue = 21m, QuantityUnit = "capsule", Refills = 2, SafetyFingerprint = Hash,
            SafetySnapshot = "AllergyList|Reviewed\nMedicationList|Reviewed", FinalizedByUserId = default, FinalizedAtUtc = DateTimeOffset.UtcNow,
        };
        change?.Invoke(row);
        return row;
    }

    private static PrescriptionEvent Event(Guid prescriptionId, string type, Action<PrescriptionEvent>? change = null)
    {
        var row = new PrescriptionEvent { Id = Guid.NewGuid(), PrescriptionId = prescriptionId, EventType = type, ActorUserId = default, OccurredAtUtc = DateTimeOffset.UtcNow, CorrelationId = Guid.NewGuid() };
        change?.Invoke(row);
        return row;
    }

    private async Task AddAsync(params object[] rows)
    {
        foreach (var r in rows)
        {
            if (r is PrescriptionDetail d && d.FinalizedByUserId == default) d.FinalizedByUserId = S.Actor;
            if (r is PrescriptionEvent e && e.ActorUserId == default) e.ActorUserId = S.Actor;
        }
        await using var db = Fixture.CreateContext();
        db.AddRange(rows);
        await db.SaveChangesAsync();
    }

    private async Task<Prescription> SavedAsync(Guid? patient = null, Action<Prescription>? change = null)
    {
        var p = NewPrescription(patient, change);
        await AddAsync(p);
        return p;
    }

    private static async Task<Microsoft.Data.SqlClient.SqlException> Refusal(Func<Task> action)
    {
        var e = await Assert.ThrowsAnyAsync<Exception>(action);
        return Assert.IsType<Microsoft.Data.SqlClient.SqlException>(e is DbUpdateException { InnerException: not null } d ? d.InnerException : e);
    }

    private async Task<int> CountAsync<T>() where T : class { await using var db = Fixture.CreateContext(); return await db.Set<T>().CountAsync(); }

    private async Task<Microsoft.Data.SqlClient.SqlException> SqlRefusalAsync(FormattableString sql)
    {
        await using var db = Fixture.CreateContext();
        return await Refusal(() => db.Database.ExecuteSqlAsync(sql));
    }

    // ---------- what is accepted ----------

    [Fact]
    public async Task A_draft_with_and_without_the_optional_parts_a_discarded_draft_and_a_finalized_one_are_accepted()
    {
        var prescription = await SavedAsync();
        await AddAsync(
            Draft(),
            Draft(d => { d.Instructions = null; d.QuantityValue = null; d.QuantityUnit = null; d.Refills = null; }),
            Draft(d => { d.Status = DraftStatuses.Discarded; d.DiscardedReason = "Wrong patient."; }),
            Draft(d => { d.Status = DraftStatuses.Discarded; }),
            Draft(d => { d.Status = DraftStatuses.Finalized; d.FinalizedPrescriptionId = prescription.Id; }));
        Assert.Equal(5, await CountAsync<PrescriptionDraft>());
    }

    [Fact]
    public async Task A_detail_and_the_events_of_a_prescription_are_accepted_and_the_quantity_keeps_two_decimals()
    {
        var p = await SavedAsync();
        await AddAsync(Detail(p.Id, d => d.QuantityValue = 12.34m), Event(p.Id, PrescriptionEventTypes.Finalized), Event(p.Id, PrescriptionEventTypes.Discontinued, e => e.Reason = "Course finished early."));
        await using var db = Fixture.CreateContext();
        Assert.Equal(12.34m, (await db.PrescriptionDetails.AsNoTracking().SingleAsync()).QuantityValue);
        Assert.Equal(2, await db.PrescriptionEvents.CountAsync());
    }

    [Fact]
    public async Task A_prescription_saved_by_story_016_needs_no_detail_and_no_event()
    {
        await SavedAsync();
        Assert.Equal((1, 0, 0), (await CountAsync<Prescription>(), await CountAsync<PrescriptionDetail>(), await CountAsync<PrescriptionEvent>()));
    }

    [Fact]
    public async Task The_same_create_key_may_be_used_by_two_different_patients()
    {
        await AddAsync(Draft(d => d.CreateKey = "same"), Draft(d => d.CreateKey = "same", patient: Bo));
        Assert.Equal(2, await CountAsync<PrescriptionDraft>());
    }

    // ---------- what the database refuses on its own: drafts ----------

    [Theory]
    [InlineData("keyBlank", "CK_PrescriptionDrafts_Key")]
    [InlineData("keyControl", "CK_PrescriptionDrafts_Key")]
    [InlineData("hashNotHex", "CK_PrescriptionDrafts_Hash")]
    [InlineData("fingerprintShort", "CK_PrescriptionDrafts_Fingerprint")]
    [InlineData("fingerprintUpper", "CK_PrescriptionDrafts_Fingerprint")]
    [InlineData("medicationBlank", "CK_PrescriptionDrafts_Medication")]
    [InlineData("medicationControl", "CK_PrescriptionDrafts_Medication")]
    [InlineData("unitUnknown", "CK_PrescriptionDrafts_Unit|CK_PrescriptionDrafts_Dose")]
    [InlineData("frequencyUnknown", "CK_PrescriptionDrafts_Frequency")]
    [InlineData("doseZero", "CK_PrescriptionDrafts_Dose")]
    [InlineData("doseAboveCeiling", "CK_PrescriptionDrafts_Dose")]
    [InlineData("instructionsBlank", "CK_PrescriptionDrafts_Instructions")]
    [InlineData("quantityWithoutUnit", "CK_PrescriptionDrafts_Quantity")]
    [InlineData("unitWithoutQuantity", "CK_PrescriptionDrafts_Quantity")]
    [InlineData("quantityZero", "CK_PrescriptionDrafts_Quantity")]
    [InlineData("quantityAboveCeiling", "CK_PrescriptionDrafts_Quantity")]
    [InlineData("quantityUnknownUnit", "CK_PrescriptionDrafts_Quantity")]
    [InlineData("refillsNegative", "CK_PrescriptionDrafts_Refills")]
    [InlineData("refillsAboveCeiling", "CK_PrescriptionDrafts_Refills")]
    [InlineData("reviewBlank", "CK_PrescriptionDrafts_Review")]
    [InlineData("statusUnknown", "CK_PrescriptionDrafts_Status|CK_PrescriptionDrafts_Outcome")]
    [InlineData("finalizedWithoutPrescription", "CK_PrescriptionDrafts_Outcome")]
    [InlineData("draftNamingAPrescription", "CK_PrescriptionDrafts_Outcome")]
    [InlineData("reasonOnADraft", "CK_PrescriptionDrafts_Outcome")]
    [InlineData("reasonBlank", "CK_PrescriptionDrafts_Outcome")]
    [InlineData("createdLongAgo", "CK_PrescriptionDrafts_Times")]
    [InlineData("updatedBeforeCreated", "CK_PrescriptionDrafts_Times")]
    public async Task The_database_refuses_an_incorrect_draft_by_itself(string which, string constraint)
    {
        var existing = await SavedAsync();
        var row = Draft(d =>
        {
            switch (which)
            {
                case "keyBlank": d.CreateKey = "   "; break;
                case "keyControl": d.CreateKey = "key\u0001"; break;
                case "hashNotHex": d.CreateHash = new string('g', 64); break;
                case "fingerprintShort": d.SafetyFingerprint = new string('a', 63).PadRight(64, ' '); break;
                case "fingerprintUpper": d.SafetyFingerprint = new string('A', 64); break;
                case "medicationBlank": d.Medication = "   "; break;
                case "medicationControl": d.Medication = "Amox\u0007icillin"; break;
                case "unitUnknown": d.DoseUnit = "stone"; break;
                case "frequencyUnknown": d.Frequency = "now and then"; break;
                case "doseZero": d.DoseValue = 0m; break;
                case "doseAboveCeiling": d.DoseValue = 5000.001m; break;
                case "instructionsBlank": d.Instructions = "   "; break;
                case "quantityWithoutUnit": d.QuantityUnit = null; break;
                case "unitWithoutQuantity": d.QuantityValue = null; break;
                case "quantityZero": d.QuantityValue = 0m; break;
                case "quantityAboveCeiling": d.QuantityValue = 1000.01m; break;
                case "quantityUnknownUnit": d.QuantityUnit = "box"; break;
                case "refillsNegative": d.Refills = -1; break;
                case "refillsAboveCeiling": d.Refills = 13; break;
                case "reviewBlank": d.ReviewNote = "   "; break;
                case "statusUnknown": d.Status = "Open"; break;
                case "finalizedWithoutPrescription": d.Status = DraftStatuses.Finalized; break;
                case "draftNamingAPrescription": d.FinalizedPrescriptionId = existing.Id; break;
                case "reasonOnADraft": d.DiscardedReason = "Because."; break;
                case "reasonBlank": d.Status = DraftStatuses.Discarded; d.DiscardedReason = "  "; break;
                case "createdLongAgo": d.CreatedAtUtc = new DateTimeOffset(1999, 1, 1, 0, 0, 0, TimeSpan.Zero); d.UpdatedAtUtc = DateTimeOffset.UtcNow; break;
                case "updatedBeforeCreated": d.UpdatedAtUtc = d.CreatedAtUtc.AddMinutes(-1); break;
                default: throw new ArgumentOutOfRangeException(nameof(which));
            }
        });
        var e = await Refusal(() => AddAsync(row));
        Assert.Contains(constraint.Split('|'), c => e.Message.Contains(c));
        Assert.Equal(0, await CountAsync<PrescriptionDraft>());
    }

    [Fact]
    public async Task The_same_create_key_cannot_make_a_second_draft_for_one_patient_and_a_draft_needs_a_patient_and_a_prescriber_that_exist()
    {
        await AddAsync(Draft(d => d.CreateKey = "once"));
        Assert.Contains("IX_PrescriptionDrafts_PatientId_CreateKey", (await Refusal(() => AddAsync(Draft(d => d.CreateKey = "once")))).Message);
        Assert.Contains("FK_PrescriptionDrafts", (await Refusal(() => AddAsync(Draft(patient: Guid.NewGuid())))).Message);
        Assert.Contains("FK_PrescriptionDrafts", (await Refusal(() => AddAsync(Draft(d => d.CreatedByUserId = Guid.NewGuid())))).Message);
        Assert.Equal(1, await CountAsync<PrescriptionDraft>());
    }

    [Fact]
    public async Task Only_one_draft_can_be_open_to_amend_a_prescription_and_a_new_one_is_allowed_once_the_first_is_not_a_draft_any_more()
    {
        var original = await SavedAsync();
        var first = Draft(d => d.SupersedesPrescriptionId = original.Id);
        await AddAsync(first);
        Assert.Contains("IX_PrescriptionDrafts_SupersedesPrescriptionId", (await Refusal(() => AddAsync(Draft(d => d.SupersedesPrescriptionId = original.Id)))).Message);
        await using (var db = Fixture.CreateContext())
        {
            var d = await db.PrescriptionDrafts.SingleAsync(x => x.Id == first.Id);
            d.Status = DraftStatuses.Discarded; d.UpdatedAtUtc = DateTimeOffset.UtcNow;
            await db.SaveChangesAsync();
        }
        await AddAsync(Draft(d => d.SupersedesPrescriptionId = original.Id));
        Assert.Equal(2, await CountAsync<PrescriptionDraft>());
    }

    // ---------- what the database refuses on its own: details ----------

    [Theory]
    [InlineData("instructionsBlank", "CK_PrescriptionDetails_Instructions")]
    [InlineData("quantityZero", "CK_PrescriptionDetails_Quantity")]
    [InlineData("quantityAboveCeiling", "CK_PrescriptionDetails_Quantity")]
    [InlineData("quantityUnknownUnit", "CK_PrescriptionDetails_Quantity")]
    [InlineData("refillsNegative", "CK_PrescriptionDetails_Refills")]
    [InlineData("refillsAboveCeiling", "CK_PrescriptionDetails_Refills")]
    [InlineData("fingerprintNotHex", "CK_PrescriptionDetails_Fingerprint")]
    [InlineData("supersedesItself", "CK_PrescriptionDetails_Supersedes")]
    [InlineData("finalizedLongAgo", "CK_PrescriptionDetails_Finalized")]
    public async Task The_database_refuses_an_incorrect_detail_by_itself(string which, string constraint)
    {
        var p = await SavedAsync();
        var row = Detail(p.Id, d =>
        {
            switch (which)
            {
                case "instructionsBlank": d.Instructions = "  "; break;
                case "quantityZero": d.QuantityValue = 0m; break;
                case "quantityAboveCeiling": d.QuantityValue = 1000.01m; break;
                case "quantityUnknownUnit": d.QuantityUnit = "box"; break;
                case "refillsNegative": d.Refills = -1; break;
                case "refillsAboveCeiling": d.Refills = 13; break;
                case "fingerprintNotHex": d.SafetyFingerprint = new string('z', 64); break;
                case "supersedesItself": d.SupersedesPrescriptionId = p.Id; break;
                case "finalizedLongAgo": d.FinalizedAtUtc = new DateTimeOffset(1999, 1, 1, 0, 0, 0, TimeSpan.Zero); break;
                default: throw new ArgumentOutOfRangeException(nameof(which));
            }
        });
        var e = await Refusal(() => AddAsync(row));
        Assert.Contains(constraint, e.Message);
        Assert.Equal(0, await CountAsync<PrescriptionDetail>());
    }

    [Fact]
    public async Task A_prescription_has_one_detail_a_draft_becomes_one_prescription_and_a_prescription_is_amended_by_one_successor()
    {
        var p = await SavedAsync();
        await AddAsync(Detail(p.Id));
        Assert.Contains("PK_PrescriptionDetails", (await Refusal(() => AddAsync(Detail(p.Id)))).Message);

        var draft = Draft(); await AddAsync(draft);
        var one = await SavedAsync(); var two = await SavedAsync();
        await AddAsync(Detail(one.Id, d => d.DraftId = draft.Id));
        Assert.Contains("IX_PrescriptionDetails_DraftId", (await Refusal(() => AddAsync(Detail(two.Id, d => d.DraftId = draft.Id)))).Message);

        var third = await SavedAsync(); var fourth = await SavedAsync();
        await AddAsync(Detail(third.Id, d => d.SupersedesPrescriptionId = p.Id));
        Assert.Contains("IX_PrescriptionDetails_SupersedesPrescriptionId", (await Refusal(() => AddAsync(Detail(fourth.Id, d => d.SupersedesPrescriptionId = p.Id)))).Message);
    }

    [Fact]
    public async Task A_detail_for_a_prescription_that_does_not_exist_is_refused()
    {
        Assert.Contains("FK_PrescriptionDetails", (await Refusal(() => AddAsync(Detail(Guid.NewGuid())))).Message);
        Assert.Equal(0, await CountAsync<PrescriptionDetail>());
    }

    // ---------- what the database refuses on its own: events ----------

    [Theory]
    [InlineData("typeUnknown", "CK_PrescriptionEvents_Type|CK_PrescriptionEvents_Reason|CK_PrescriptionEvents_Superseded")]
    [InlineData("cancelledWithoutReason", "CK_PrescriptionEvents_Reason")]
    [InlineData("discontinuedWithoutReason", "CK_PrescriptionEvents_Reason")]
    [InlineData("reasonOnFinalized", "CK_PrescriptionEvents_Reason")]
    [InlineData("reasonBlank", "CK_PrescriptionEvents_Reason")]
    [InlineData("supersededWithoutSuccessor", "CK_PrescriptionEvents_Superseded")]
    [InlineData("successorOnCancelled", "CK_PrescriptionEvents_Superseded|CK_PrescriptionEvents_Reason")]
    [InlineData("supersededByItself", "CK_PrescriptionEvents_Superseded")]
    [InlineData("occurredLongAgo", "CK_PrescriptionEvents_Time")]
    public async Task The_database_refuses_an_incorrect_event_by_itself(string which, string constraint)
    {
        var p = await SavedAsync(); var other = await SavedAsync();
        var row = which switch
        {
            "typeUnknown" => Event(p.Id, "Renewed"),
            "cancelledWithoutReason" => Event(p.Id, PrescriptionEventTypes.Cancelled),
            "discontinuedWithoutReason" => Event(p.Id, PrescriptionEventTypes.Discontinued),
            "reasonOnFinalized" => Event(p.Id, PrescriptionEventTypes.Finalized, e => e.Reason = "Why?"),
            "reasonBlank" => Event(p.Id, PrescriptionEventTypes.Cancelled, e => e.Reason = "  "),
            "supersededWithoutSuccessor" => Event(p.Id, PrescriptionEventTypes.Superseded),
            "successorOnCancelled" => Event(p.Id, PrescriptionEventTypes.Cancelled, e => { e.Reason = "Wrong."; e.SupersededByPrescriptionId = other.Id; }),
            "supersededByItself" => Event(p.Id, PrescriptionEventTypes.Superseded, e => e.SupersededByPrescriptionId = p.Id),
            "occurredLongAgo" => Event(p.Id, PrescriptionEventTypes.Finalized, e => e.OccurredAtUtc = new DateTimeOffset(1999, 1, 1, 0, 0, 0, TimeSpan.Zero)),
            _ => throw new ArgumentOutOfRangeException(nameof(which)),
        };
        var e = await Refusal(() => AddAsync(row));
        Assert.Contains(constraint.Split('|'), c => e.Message.Contains(c));
        Assert.Equal(0, await CountAsync<PrescriptionEvent>());
    }

    [Fact]
    public async Task A_prescription_is_finalized_once_and_ends_at_most_once()
    {
        var p = await SavedAsync(); var successor = await SavedAsync();
        await AddAsync(Event(p.Id, PrescriptionEventTypes.Finalized));
        Assert.Contains("IX_PrescriptionEvents_OneFinalized", (await Refusal(() => AddAsync(Event(p.Id, PrescriptionEventTypes.Finalized)))).Message);
        await AddAsync(Event(p.Id, PrescriptionEventTypes.Cancelled, e => e.Reason = "Entered for the wrong patient."));
        Assert.Contains("IX_PrescriptionEvents_OneEnding", (await Refusal(() => AddAsync(Event(p.Id, PrescriptionEventTypes.Discontinued, e => e.Reason = "Stopped.")))).Message);
        Assert.Contains("IX_PrescriptionEvents_OneEnding", (await Refusal(() => AddAsync(Event(p.Id, PrescriptionEventTypes.Superseded, e => e.SupersededByPrescriptionId = successor.Id)))).Message);
        Assert.Equal(2, await CountAsync<PrescriptionEvent>());
    }

    // ---------- a draft changes only while it is a draft ----------

    [Fact]
    public async Task A_draft_can_be_edited_and_then_finalized_and_the_edit_is_refused_when_a_stale_version_is_used()
    {
        var p = await SavedAsync();
        var draft = Draft(); await AddAsync(draft);
        await using (var db = Fixture.CreateContext())
        {
            var d = await db.PrescriptionDrafts.SingleAsync(x => x.Id == draft.Id);
            d.Medication = "Amoxicillin trihydrate"; d.Refills = 1; d.UpdatedAtUtc = DateTimeOffset.UtcNow;
            await db.SaveChangesAsync();
        }
        await using (var stale = Fixture.CreateContext())
        {
            var d = await stale.PrescriptionDrafts.SingleAsync(x => x.Id == draft.Id);
            await using (var other = Fixture.CreateContext())
            {
                var o = await other.PrescriptionDrafts.SingleAsync(x => x.Id == draft.Id);
                o.Refills = 2; await other.SaveChangesAsync();
            }
            d.Refills = 3;
            await Assert.ThrowsAsync<DbUpdateConcurrencyException>(() => stale.SaveChangesAsync());
        }
        await using (var db = Fixture.CreateContext())
        {
            var d = await db.PrescriptionDrafts.SingleAsync(x => x.Id == draft.Id);
            d.Status = DraftStatuses.Finalized; d.FinalizedPrescriptionId = p.Id; d.UpdatedAtUtc = DateTimeOffset.UtcNow;
            await db.SaveChangesAsync();
        }
        await using var check = Fixture.CreateContext();
        var stored = await check.PrescriptionDrafts.AsNoTracking().SingleAsync(x => x.Id == draft.Id);
        Assert.Equal((DraftStatuses.Finalized, p.Id, "Amoxicillin trihydrate", 2), (stored.Status, stored.FinalizedPrescriptionId, stored.Medication, stored.Refills));
    }

    [Theory]
    [InlineData("finalized")] [InlineData("discarded")]
    public async Task A_finalized_or_discarded_draft_never_changes(string status)
    {
        var p = await SavedAsync();
        var draft = status == "finalized"
            ? Draft(d => { d.Status = DraftStatuses.Finalized; d.FinalizedPrescriptionId = p.Id; })
            : Draft(d => d.Status = DraftStatuses.Discarded);
        await AddAsync(draft);
        var e = await SqlRefusalAsync($"UPDATE [PrescriptionDrafts] SET [Medication] = 'Changed' WHERE [Id] = {draft.Id}");
        Assert.Equal(51176, e.Number);
        await using var check = Fixture.CreateContext();
        Assert.Equal("Amoxicillin", (await check.PrescriptionDrafts.AsNoTracking().SingleAsync(x => x.Id == draft.Id)).Medication);
    }

    [Theory]
    [InlineData("patient")] [InlineData("createKey")] [InlineData("createdAt")] [InlineData("createdBy")] [InlineData("supersedes")]
    public async Task A_drafts_patient_creation_and_amended_prescription_never_change_even_while_it_is_a_draft(string which)
    {
        var p = await SavedAsync();
        var draft = Draft(); await AddAsync(draft);
        var e = which switch
        {
            "patient" => await SqlRefusalAsync($"UPDATE [PrescriptionDrafts] SET [PatientId] = {Bo} WHERE [Id] = {draft.Id}"),
            "createKey" => await SqlRefusalAsync($"UPDATE [PrescriptionDrafts] SET [CreateKey] = 'other' WHERE [Id] = {draft.Id}"),
            "createdAt" => await SqlRefusalAsync($"UPDATE [PrescriptionDrafts] SET [CreatedAtUtc] = DATEADD(day, -1, [CreatedAtUtc]) WHERE [Id] = {draft.Id}"),
            "createdBy" => await SqlRefusalAsync($"UPDATE [PrescriptionDrafts] SET [CreatedByUserId] = {Guid.NewGuid()} WHERE [Id] = {draft.Id}"),
            "supersedes" => await SqlRefusalAsync($"UPDATE [PrescriptionDrafts] SET [SupersedesPrescriptionId] = {p.Id} WHERE [Id] = {draft.Id}"),
            _ => throw new ArgumentOutOfRangeException(nameof(which)),
        };
        Assert.Contains(e.Number, new[] { 51177, 547 });                                                    // a created-by user that does not exist is refused by the foreign key first
    }

    [Fact]
    public async Task A_draft_is_never_deleted()
    {
        var draft = Draft(); await AddAsync(draft);
        Assert.Equal(51175, (await SqlRefusalAsync($"DELETE FROM [PrescriptionDrafts] WHERE [Id] = {draft.Id}")).Number);
        Assert.Equal(1, await CountAsync<PrescriptionDraft>());
    }

    // ---------- finalized history is append-only and stays inside one patient ----------

    [Fact]
    public async Task The_details_and_the_events_of_a_prescription_can_be_neither_changed_nor_deleted()
    {
        var p = await SavedAsync();
        await AddAsync(Detail(p.Id), Event(p.Id, PrescriptionEventTypes.Finalized));
        Assert.Equal(51178, (await SqlRefusalAsync($"UPDATE [PrescriptionDetails] SET [Refills] = 5")).Number);
        Assert.Equal(51178, (await SqlRefusalAsync($"DELETE FROM [PrescriptionDetails]")).Number);
        Assert.Equal(51180, (await SqlRefusalAsync($"UPDATE [PrescriptionEvents] SET [OccurredAtUtc] = SYSDATETIMEOFFSET()")).Number);
        Assert.Equal(51180, (await SqlRefusalAsync($"DELETE FROM [PrescriptionEvents]")).Number);
        await using var check = Fixture.CreateContext();
        Assert.Equal(2, (await check.PrescriptionDetails.AsNoTracking().SingleAsync()).Refills);
        Assert.Equal(1, await check.PrescriptionEvents.CountAsync());
    }

    [Fact]
    public async Task An_amendment_must_be_for_the_same_patient_as_the_prescription_it_replaces_and_as_the_draft_it_came_from()
    {
        var anns = await SavedAsync(Ann);
        var bos = await SavedAsync(Bo);
        Assert.Equal(51179, (await Refusal(() => AddAsync(Detail(bos.Id, d => d.SupersedesPrescriptionId = anns.Id)))).Number);

        var annsDraft = Draft(); await AddAsync(annsDraft);
        Assert.Equal(51179, (await Refusal(() => AddAsync(Detail(bos.Id, d => d.DraftId = annsDraft.Id)))).Number);
        Assert.Equal(0, await CountAsync<PrescriptionDetail>());

        var anns2 = await SavedAsync(Ann);
        await AddAsync(Detail(anns2.Id, d => { d.SupersedesPrescriptionId = anns.Id; d.DraftId = annsDraft.Id; }));
        Assert.Equal(1, await CountAsync<PrescriptionDetail>());
    }

    [Fact]
    public async Task A_prescription_can_only_be_replaced_by_a_prescription_of_the_same_patient()
    {
        var anns = await SavedAsync(Ann);
        var bos = await SavedAsync(Bo);
        var e = await Refusal(() => AddAsync(Event(anns.Id, PrescriptionEventTypes.Superseded, x => x.SupersededByPrescriptionId = bos.Id)));
        Assert.Equal(51181, e.Number);
        var anns2 = await SavedAsync(Ann);
        await AddAsync(Event(anns.Id, PrescriptionEventTypes.Superseded, x => x.SupersededByPrescriptionId = anns2.Id));
        Assert.Equal(1, await CountAsync<PrescriptionEvent>());
    }

    [Fact]
    public async Task A_prescription_that_has_a_detail_or_an_event_cannot_be_deleted_and_the_story_016_trigger_still_holds()
    {
        var p = await SavedAsync();
        await AddAsync(Detail(p.Id), Event(p.Id, PrescriptionEventTypes.Finalized));
        Assert.Equal(51174, (await SqlRefusalAsync($"DELETE FROM [Prescriptions] WHERE [Id] = {p.Id}")).Number);
        Assert.Equal(51174, (await SqlRefusalAsync($"UPDATE [Prescriptions] SET [DoseValue] = 1 WHERE [Id] = {p.Id}")).Number);
    }

    // ---------- the migration ----------

    [Fact]
    public async Task The_migration_rolls_back_and_forward_while_the_workflow_is_empty_and_refuses_to_roll_back_over_a_draft_a_detail_or_an_event()
    {
        await using (var db = Fixture.CreateContext()) await db.GetInfrastructure().GetRequiredService<IMigrator>().MigrateAsync(AfterPrescriptions);
        await using (var db = Fixture.CreateContext())
        {
            Assert.Equal(0, await db.Database.SqlQuery<int>($"SELECT COUNT(*) AS [Value] FROM sys.tables WHERE name IN ('PrescriptionDrafts','PrescriptionDetails','PrescriptionEvents')").SingleAsync());
            Assert.Equal(1, await db.Database.SqlQuery<int>($"SELECT COUNT(*) AS [Value] FROM sys.tables WHERE name = 'Prescriptions'").SingleAsync());        // STORY-016's table stays
        }
        await using (var db = Fixture.CreateContext()) await db.Database.MigrateAsync();
        await using (var db = Fixture.CreateContext())
        {
            Assert.Equal(3, await db.Database.SqlQuery<int>($"SELECT COUNT(*) AS [Value] FROM sys.tables WHERE name IN ('PrescriptionDrafts','PrescriptionDetails','PrescriptionEvents')").SingleAsync());
            Assert.Equal(6, await db.Database.SqlQuery<int>($"SELECT COUNT(*) AS [Value] FROM sys.triggers WHERE name LIKE 'TR_Prescription%' AND name NOT IN ('TR_Prescriptions_Immutable') AND name NOT LIKE '%[_]MergeScope'").SingleAsync());
        }

        await AddAsync(Draft());
        var e = await Refusal(async () => { await using var db = Fixture.CreateContext(); await db.GetInfrastructure().GetRequiredService<IMigrator>().MigrateAsync(AfterPrescriptions); });
        Assert.Equal(51178, e.Number);
        Assert.Equal(1, await CountAsync<PrescriptionDraft>());                                               // the history is still there
    }
}
