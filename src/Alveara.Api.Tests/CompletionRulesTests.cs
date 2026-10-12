using Alveara.Api.Architecture.Completion;
using Alveara.Api.Architecture.Treatment;
using Xunit;

namespace Alveara.Api.Tests;

/// <summary>
/// ALV-008-C01: the pure rules of completing a set, with no database. A request names every wrong field at once; the performed time may be at most five minutes ahead of the practice clock (the boundary is
/// inclusive, and one millisecond more is refused); the same request always hashes the same whatever order the procedures were picked in, and any change to it changes the hash; and the acceptance time is read
/// from the history so scheduling does not restart it but a decline followed by a new acceptance does.
/// </summary>
public class CompletionRulesTests
{
    private static readonly DateTimeOffset Now = new(2030, 6, 1, 15, 0, 0, TimeSpan.Zero);
    private static readonly Guid A = Guid.Parse("00000000-0000-0000-0000-00000000000a");
    private static readonly Guid B = Guid.Parse("00000000-0000-0000-0000-00000000000b");
    private static readonly Guid Encounter = Guid.Parse("00000000-0000-0000-0000-0000000000e1");
    private static readonly Guid Provider = Guid.Parse("00000000-0000-0000-0000-0000000000f1");

    private static CompleteProceduresRequest Valid(Action<Dictionary<string, object?>>? tweak = null)
    {
        var f = new Dictionary<string, object?> { ["key"] = "k-1", ["encounter"] = Encounter, ["provider"] = Provider, ["performed"] = Now.AddHours(-1), ["items"] = new List<Guid> { B, A } };
        tweak?.Invoke(f);
        return new CompleteProceduresRequest((string?)f["key"], "v", (Guid?)f["encounter"], (Guid?)f["provider"], (DateTimeOffset?)f["performed"], (IReadOnlyList<Guid>?)f["items"]);
    }

    private static Dictionary<string, string> Refused(CompleteProceduresRequest request)
    {
        var e = Assert.Throws<TreatmentPlanException>(() => CompletionRules.Clean(request, Now));
        Assert.Equal(("validation_failed", 400), (e.Code, e.StatusCode));
        return e.FieldErrors.ToDictionary(p => p.Key, p => p.Value);
    }

    // ---------- the request ----------

    [Fact]
    public void A_complete_request_is_cleaned_with_a_trimmed_key_sorted_items_and_the_time_in_whole_milliseconds()
    {
        var clean = CompletionRules.Clean(Valid(f => { f["key"] = "  k-1  "; f["performed"] = Now.AddHours(-1).AddTicks(1234); }), Now);
        Assert.Equal("k-1", clean.Key);
        Assert.Equal([A, B], clean.ItemIds);
        Assert.Equal(Now.AddHours(-1), clean.PerformedAtUtc);                                       // the 123.4 microseconds are cut, so the same instant is always the same value
    }

    [Fact]
    public void Every_wrong_field_is_named_in_one_answer()
    {
        var errors = Refused(new CompleteProceduresRequest(null, null, null, null, null, null));
        Assert.Equal(["encounterId", "idempotencyKey", "itemIds", "performedAtUtc", "providerId"], errors.Keys.Order().ToArray());
    }

    [Theory]
    [InlineData("key blank")]
    [InlineData("key too long")]
    [InlineData("key control")]
    [InlineData("no items")]
    [InlineData("duplicate items")]
    [InlineData("empty item id")]
    [InlineData("too many items")]
    [InlineData("empty encounter")]
    [InlineData("empty provider")]
    public void A_request_that_breaks_a_shape_rule_is_refused_on_the_named_field(string which)
    {
        var request = which switch
        {
            "key blank" => Valid(f => f["key"] = "   "),
            "key too long" => Valid(f => f["key"] = new string('k', CompletionLimits.KeyMax + 1)),
            "key control" => Valid(f => f["key"] = "k\u0001"),
            "no items" => Valid(f => f["items"] = new List<Guid>()),
            "duplicate items" => Valid(f => f["items"] = new List<Guid> { A, A }),
            "empty item id" => Valid(f => f["items"] = new List<Guid> { A, Guid.Empty }),
            "too many items" => Valid(f => f["items"] = Enumerable.Range(1, CompletionRules.MaxItems + 1).Select(_ => Guid.NewGuid()).ToList()),
            "empty encounter" => Valid(f => f["encounter"] = Guid.Empty),
            _ => Valid(f => f["provider"] = Guid.Empty),
        };
        var field = which.StartsWith("key") ? "idempotencyKey" : which.Contains("item") ? "itemIds" : which.Contains("encounter") ? "encounterId" : "providerId";
        Assert.Equal([field], Refused(request).Keys.ToArray());
    }

    [Fact]
    public void The_largest_allowed_selection_is_accepted()
    {
        var ids = Enumerable.Range(1, CompletionRules.MaxItems).Select(_ => Guid.NewGuid()).ToList();
        Assert.Equal(CompletionRules.MaxItems, CompletionRules.Clean(Valid(f => f["items"] = ids), Now).ItemIds.Count);
    }

    // ---------- the clock tolerance ----------

    [Fact]
    public void The_performed_time_may_be_exactly_five_minutes_ahead_of_the_clock_and_not_one_millisecond_more()
    {
        Assert.Equal(TimeSpan.FromMinutes(5), CompletionRules.ClockTolerance);
        Assert.Equal(Now.AddMinutes(5), CompletionRules.Clean(Valid(f => f["performed"] = Now.AddMinutes(5)), Now).PerformedAtUtc);
        Assert.Equal(Now.AddMinutes(5).AddMilliseconds(-1), CompletionRules.Clean(Valid(f => f["performed"] = Now.AddMinutes(5).AddMilliseconds(-1)), Now).PerformedAtUtc);
        var errors = Refused(Valid(f => f["performed"] = Now.AddMinutes(5).AddMilliseconds(1)));
        Assert.Equal(["performedAtUtc"], errors.Keys.ToArray());
        Assert.Contains("5 minutes ahead of the practice clock", errors["performedAtUtc"]);
    }

    [Fact]
    public void A_time_written_with_another_offset_is_the_same_instant()
    {
        var chicago = new DateTimeOffset(2030, 6, 1, 9, 0, 0, TimeSpan.FromHours(-6));              // 15:00 UTC
        Assert.True(CompletionRules.IsAheadOfClock(chicago.AddMinutes(5).AddMilliseconds(1), Now));
        Assert.False(CompletionRules.IsAheadOfClock(chicago.AddMinutes(5), Now));
        Assert.Equal(TimeSpan.Zero, CompletionRules.NormalizeTime(chicago).Offset);
    }

    // ---------- the canonical hash ----------

    [Fact]
    public void The_same_request_hashes_the_same_whatever_order_the_procedures_were_picked_in()
    {
        var one = CompletionRules.RequestHash(CompletionRules.Clean(Valid(f => f["items"] = new List<Guid> { A, B }), Now));
        var two = CompletionRules.RequestHash(CompletionRules.Clean(Valid(f => f["items"] = new List<Guid> { B, A }), Now));
        Assert.Equal(one, two);
        Assert.Matches("^[0-9a-f]{64}$", one);
    }

    [Fact]
    public void A_time_written_another_way_or_with_extra_precision_hashes_the_same()
    {
        var utc = CompletionRules.RequestHash(CompletionRules.Clean(Valid(f => f["performed"] = new DateTimeOffset(2030, 6, 1, 14, 0, 0, TimeSpan.Zero)), Now));
        var chicago = CompletionRules.RequestHash(CompletionRules.Clean(Valid(f => f["performed"] = new DateTimeOffset(2030, 6, 1, 8, 0, 0, TimeSpan.FromHours(-6))), Now));
        var precise = CompletionRules.RequestHash(CompletionRules.Clean(Valid(f => f["performed"] = new DateTimeOffset(2030, 6, 1, 14, 0, 0, TimeSpan.Zero).AddTicks(5)), Now));
        Assert.Equal(utc, chicago);
        Assert.Equal(utc, precise);
    }

    [Theory]
    [InlineData("items")]
    [InlineData("encounter")]
    [InlineData("provider")]
    [InlineData("performed")]
    public void Any_change_to_the_request_changes_the_hash(string which)
    {
        var baseline = CompletionRules.RequestHash(CompletionRules.Clean(Valid(), Now));
        var changed = CompletionRules.RequestHash(CompletionRules.Clean(which switch
        {
            "items" => Valid(f => f["items"] = new List<Guid> { A }),
            "encounter" => Valid(f => f["encounter"] = Guid.NewGuid()),
            "provider" => Valid(f => f["provider"] = Guid.NewGuid()),
            _ => Valid(f => f["performed"] = Now.AddHours(-1).AddMilliseconds(1)),
        }, Now));
        Assert.NotEqual(baseline, changed);
    }

    // ---------- when a procedure was accepted ----------

    private static TreatmentPlanItemState Row(int sequence, string acceptance, string scheduling, int minute) => new()
    {
        Id = Guid.NewGuid(), PlanId = Guid.NewGuid(), PatientId = Guid.NewGuid(), ItemId = Guid.NewGuid(), Sequence = sequence, Acceptance = acceptance, Scheduling = scheduling, Completion = ItemCompletion.NotStarted,
        RevisionNumber = 1, RecordedByUserId = Guid.NewGuid(), RecordedAtUtc = Now.AddMinutes(minute),
    };

    [Fact]
    public void The_acceptance_in_force_is_the_row_that_turned_the_procedure_to_accepted_and_scheduling_does_not_restart_it()
    {
        var history = new[] { Row(1, ItemAcceptance.Accepted, ItemScheduling.Unscheduled, 1), Row(2, ItemAcceptance.Accepted, ItemScheduling.Scheduled, 5), Row(3, ItemAcceptance.Accepted, ItemScheduling.Unscheduled, 9) };
        Assert.Equal(Now.AddMinutes(1), CompletionRules.AcceptedAt(history));
    }

    [Fact]
    public void A_decline_followed_by_a_new_acceptance_restarts_the_clock_and_a_procedure_never_accepted_has_no_time()
    {
        var history = new[] { Row(1, ItemAcceptance.Accepted, ItemScheduling.Unscheduled, 1), Row(2, ItemAcceptance.Declined, ItemScheduling.Unscheduled, 4), Row(3, ItemAcceptance.Accepted, ItemScheduling.Unscheduled, 8) };
        Assert.Equal(Now.AddMinutes(8), CompletionRules.AcceptedAt(history));
        Assert.Null(CompletionRules.AcceptedAt([Row(1, ItemAcceptance.Declined, ItemScheduling.Unscheduled, 1)]));
        Assert.Null(CompletionRules.AcceptedAt([]));
    }

    [Fact]
    public void The_order_the_rows_are_given_in_does_not_matter()
    {
        var history = new[] { Row(3, ItemAcceptance.Accepted, ItemScheduling.Unscheduled, 8), Row(1, ItemAcceptance.Accepted, ItemScheduling.Unscheduled, 1), Row(2, ItemAcceptance.Declined, ItemScheduling.Unscheduled, 4) };
        Assert.Equal(Now.AddMinutes(8), CompletionRules.AcceptedAt(history));
    }
}
