using Alveara.Api.Architecture.Completion;
using Alveara.Api.Architecture.Identity;
using Alveara.Api.Architecture.Treatment;
using Xunit;

namespace Alveara.Api.Tests;

/// <summary>
/// ALV-008-C01 against real SQL Server: the clinical history (STORY-008) with the authoritative detail added. A procedure completed through the new workflow shows its current provider, performed time, encounter,
/// charge and chart outcome (and a correction changes what it shows); one completed the old way is listed exactly as before with those fields empty; the plan service's own history is unchanged. The readiness also
/// offers the active providers and the patient's own encounters for the review screen.
/// </summary>
public class CompletedProcedureHistoryTests : CompletionTestBase
{
    private ProviderProfile _provider = null!;
    private DateTimeOffset? _performed;

    public override async Task InitializeAsync()
    {
        await base.InitializeAsync();
        _provider = await ProviderAsync("Dr. Provider");
    }

    private Task<CompletionResult> CompleteAsync(PlanView plan, IEnumerable<int> items, string key = "set-1", string? version = null) =>
        Completion(s => s.CompleteAsync(plan.Id, RequestFor(plan, items, AnnEncounter, _provider.Id, key, _performed ??= DateTimeOffset.UtcNow.AddSeconds(2), version), S.Actor, default));

    private Task<IReadOnlyList<ClinicalHistoryEntryView>> HistoryAsync(Guid patient) => Completion(s => s.ClinicalHistoryAsync(patient, default));

    [Fact]
    public async Task A_procedure_completed_through_the_workflow_shows_its_provider_performed_time_encounter_charge_and_chart_outcome()
    {
        var plan = await AcceptedPlanAsync(2);
        var done = await CompleteAsync(plan, [0, 1]);

        var history = await HistoryAsync(Ann);

        Assert.Equal(2, history.Count);
        var entry = history.Single(h => h.ItemNumber == 1);
        Assert.Equal(done.Procedures[0].Id, entry.CompletedProcedureId);
        Assert.Equal(("Dr. Provider", AnnEncounter, 1, 100m, ChartOutcomes.NotApplicable), (entry.ProviderName, entry.EncounterId, entry.ChargeEntryNumber, entry.ChargeAmount, entry.ChartOutcome));
        Assert.Equal(CompletionRules.NormalizeTime(_performed!.Value), entry.PerformedAtUtc);
        Assert.Equal((plan.Items[0].ProcedureCode, plan.CurrentRevision), (entry.ProcedureCode, entry.PlanRevisionAtCompletion));      // the STORY-008 fields are as they were
    }

    [Fact]
    public async Task A_correction_changes_what_the_history_shows_and_a_zero_fee_procedure_shows_no_charge()
    {
        var diagnosis = await DiagnosisAsync();
        var plan = await AcceptedToothPlanAsync(diagnosis, ("EXAM-1", 0m, "11", null), ("FILL-2", 100m, "14", "O"));
        var done = await CompleteAsync(plan, [0, 1]);
        var other = await ProviderAsync("Dr. Other");
        await Completion(s => s.AmendAsync(done.Procedures[1].Id, new AmendCompletedProcedureRequest("a1", 0, other.Id, _performed!.Value.AddMinutes(1), null, "Wrong provider and time"), S.Actor, default));

        var history = await HistoryAsync(Ann);

        var free = history.Single(h => h.ItemNumber == 1);
        Assert.Equal((null, null), (free.ChargeEntryNumber, free.ChargeAmount));
        var filled = history.Single(h => h.ItemNumber == 2);
        Assert.Equal(("Dr. Other", CompletionRules.NormalizeTime(_performed!.Value.AddMinutes(1)), 1), (filled.ProviderName, filled.PerformedAtUtc, filled.ChargeEntryNumber));
    }

    [Fact]
    public async Task A_procedure_completed_the_old_way_is_listed_as_before_with_the_detail_empty_and_the_plan_services_history_is_unchanged()
    {
        var plan = await AcceptedPlanAsync(2);
        await LegacyComplete(plan.Id, plan.Items[0].Id);                                              // the old way
        var current = await PlanNowAsync(plan.Id);
        await CompleteAsync(plan, [1], version: current.RowVersion);                                                                   // the new way

        var enriched = await HistoryAsync(Ann);
        var legacy = enriched.Single(h => h.ItemNumber == 1);
        Assert.Equal((null, null, null, null, null, null, null), (legacy.CompletedProcedureId, legacy.ProviderName, legacy.PerformedAtUtc, legacy.EncounterId, legacy.ChargeEntryNumber, legacy.ChargeAmount, legacy.ChartOutcome));
        Assert.NotNull(enriched.Single(h => h.ItemNumber == 2).CompletedProcedureId);

        var plain = await Svc(s => s.ClinicalHistoryAsync(Ann, default));                                                              // the plan service does not know about the new records
        Assert.Equal(2, plain.Count);
        Assert.All(plain, h => Assert.Equal((null, null, null), (h.CompletedProcedureId, h.ProviderName, h.ChargeEntryNumber)));
        Assert.Equal(plain.Select(h => (h.ItemId, h.CompletedByName, h.CompletedAtUtc)).ToArray(), enriched.Select(h => (h.ItemId, h.CompletedByName, h.CompletedAtUtc)).ToArray());
    }

    [Fact]
    public async Task The_history_of_a_patient_with_nothing_completed_is_empty_and_an_unknown_patient_is_not_found()
    {
        Assert.Empty(await HistoryAsync(Ann));
        var e = await Assert.ThrowsAsync<TreatmentPlanException>(() => HistoryAsync(Guid.NewGuid()));
        Assert.Equal(404, e.StatusCode);
    }

    [Fact]
    public async Task The_readiness_offers_the_active_providers_and_only_this_patients_encounters_newest_first()
    {
        var plan = await AcceptedPlanAsync(1);
        var inactive = await ProviderAsync("Dr. Retired");
        await ExecuteAsync("UPDATE [ProviderProfiles] SET [IsActive] = 0 WHERE [Id] = {0}", inactive.Id);
        var later = await EncounterAsync(Ann);

        var readiness = await Completion(s => s.ReadinessAsync(plan.Id, null, default));

        Assert.Contains(readiness.Providers, p => p.Id == _provider.Id && p.Name == "Dr. Provider");
        Assert.DoesNotContain(readiness.Providers, p => p.Id == inactive.Id);
        Assert.Equal(readiness.Providers.Select(p => p.Name).Order().ToArray(), readiness.Providers.Select(p => p.Name).ToArray());       // sorted by name
        Assert.Equal([later, AnnEncounter], readiness.Encounters.Select(e => e.Id).ToArray());                                          // Ann's two, the newest first
        Assert.DoesNotContain(readiness.Encounters, e => e.Id == BoEncounter);
        Assert.All(readiness.Encounters, e => Assert.False(string.IsNullOrEmpty(e.Status)));
    }
}
