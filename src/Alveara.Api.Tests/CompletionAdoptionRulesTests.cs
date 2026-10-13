using Alveara.Api.Architecture.Completion;
using Alveara.Api.Architecture.Treatment;
using Xunit;

namespace Alveara.Api.Tests;

/// <summary>
/// ALV-008-C02 without a database: what an adoption request must look like (every wrong field named at once, the same time, provider and encounter rules as a completion, plus a required reason) and the
/// canonical form of an adoption (review correction C1): its hash binds a version tag, the operation discriminator, the item, the encounter, the provider, the performed time and the trimmed reason, so a replay
/// must be identical and anything else under the same key is a conflict, including a request of another kind.
/// </summary>
public class CompletionAdoptionRulesTests
{
    private static readonly DateTimeOffset Now = new(2030, 1, 1, 12, 0, 0, TimeSpan.Zero);
    private static readonly Guid Item = Guid.Parse("00000000-0000-0000-0000-0000000000a1");
    private static readonly Guid Encounter = Guid.Parse("00000000-0000-0000-0000-0000000000e1");
    private static readonly Guid Provider = Guid.Parse("00000000-0000-0000-0000-0000000000b1");

    private static AdoptCompletionRequest Good(Action<Dictionary<string, object?>>? change = null)
    {
        var v = new Dictionary<string, object?> { ["key"] = "adopt-1", ["encounter"] = Encounter, ["provider"] = Provider, ["performed"] = Now.AddMinutes(-30), ["reason"] = "Completed before procedure records existed" };
        change?.Invoke(v);
        return new AdoptCompletionRequest((string?)v["key"], "AAAA", (Guid?)v["encounter"], (Guid?)v["provider"], (DateTimeOffset?)v["performed"], (string?)v["reason"]);
    }

    private static CleanAdoption Clean(Action<Dictionary<string, object?>>? change = null) => CompletionRules.CleanForAdoption(Good(change), Item, Now);
    private static string Hash(Action<Dictionary<string, object?>>? change = null) => CompletionRules.AdoptionHash(Clean(change));

    [Fact]
    public void A_good_request_is_cleaned_with_a_trimmed_key_and_reason_and_the_time_in_whole_utc_milliseconds()
    {
        var c = Clean(v => { v["key"] = "  adopt-1  "; v["reason"] = "  late entry  "; v["performed"] = new DateTimeOffset(2030, 1, 1, 6, 0, 0, 123, TimeSpan.FromHours(-6)).AddTicks(4567); });
        Assert.Equal(("adopt-1", "late entry", Item, Encounter, Provider), (c.Key, c.Reason, c.ItemId, c.EncounterId, c.ProviderId));
        Assert.Equal(new DateTimeOffset(2030, 1, 1, 12, 0, 0, 123, TimeSpan.Zero), c.PerformedAtUtc);
    }

    [Fact]
    public void Every_missing_field_is_named_at_once()
    {
        var e = Assert.Throws<TreatmentPlanException>(() => CompletionRules.CleanForAdoption(new AdoptCompletionRequest(null, null, null, null, null, null), Item, Now));
        Assert.Equal(("validation_failed", 400), (e.Code, e.StatusCode));
        Assert.Equal(["encounterId", "idempotencyKey", "performedAtUtc", "providerId", "reason"], e.FieldErrors.Keys.Order().ToArray());
    }

    [Theory]
    [InlineData("key-empty", "idempotencyKey")]
    [InlineData("key-long", "idempotencyKey")]
    [InlineData("key-control", "idempotencyKey")]
    [InlineData("encounter-empty", "encounterId")]
    [InlineData("provider-empty", "providerId")]
    [InlineData("ahead", "performedAtUtc")]
    [InlineData("reason-blank", "reason")]
    [InlineData("reason-long", "reason")]
    [InlineData("reason-control", "reason")]
    public void Each_wrong_field_is_refused_by_name(string which, string field)
    {
        var e = Assert.Throws<TreatmentPlanException>(() => Clean(v =>
        {
            switch (which)
            {
                case "key-empty": v["key"] = "   "; break;
                case "key-long": v["key"] = new string('k', CompletionLimits.KeyMax + 1); break;
                case "key-control": v["key"] = "a\tb"; break;
                case "encounter-empty": v["encounter"] = Guid.Empty; break;
                case "provider-empty": v["provider"] = Guid.Empty; break;
                case "ahead": v["performed"] = Now + CompletionRules.ClockTolerance + TimeSpan.FromMilliseconds(1); break;
                case "reason-blank": v["reason"] = "   "; break;
                case "reason-long": v["reason"] = new string('r', CompletionLimits.ReasonMax + 1); break;
                case "reason-control": v["reason"] = "line one\nline two"; break;
            }
        }));
        Assert.Equal([field], e.FieldErrors.Keys.ToArray());
    }

    [Fact]
    public void The_performed_time_may_be_exactly_the_clock_tolerance_ahead_and_a_reason_of_the_maximum_length_is_accepted()
    {
        var c = Clean(v => { v["performed"] = Now + CompletionRules.ClockTolerance; v["reason"] = new string('r', CompletionLimits.ReasonMax); });
        Assert.Equal(Now + CompletionRules.ClockTolerance, c.PerformedAtUtc);
        Assert.Equal(CompletionLimits.ReasonMax, c.Reason.Length);
    }

    [Fact]
    public void The_same_request_always_hashes_the_same_whatever_the_spelling_of_the_key_time_zone_and_spaces_around_the_reason()
    {
        var plain = Hash();
        Assert.Equal(plain, Hash());
        Assert.Equal(plain, Hash(v => v["reason"] = "   Completed before procedure records existed  "));
        Assert.Equal(plain, Hash(v => v["performed"] = new DateTimeOffset(Now.AddMinutes(-30).UtcDateTime).ToOffset(TimeSpan.FromHours(5))));
        Assert.Equal(plain, Hash(v => v["performed"] = Now.AddMinutes(-30).AddTicks(1234)));                       // below a millisecond is the same instant
        Assert.Equal(plain, Hash(v => v["key"] = "another-key"));                                                    // the key identifies the request, it is not part of what is requested
        Assert.Matches("^[0-9a-f]{64}$", plain);
    }

    [Theory]
    [InlineData("reason")]
    [InlineData("encounter")]
    [InlineData("provider")]
    [InlineData("performed")]
    public void Changing_the_reason_the_encounter_the_provider_or_the_time_changes_the_hash(string field)
    {
        var changed = Hash(v =>
        {
            switch (field)
            {
                case "reason": v["reason"] = "A different reason"; break;
                case "encounter": v["encounter"] = Guid.Parse("00000000-0000-0000-0000-0000000000e2"); break;
                case "provider": v["provider"] = Guid.Parse("00000000-0000-0000-0000-0000000000b2"); break;
                case "performed": v["performed"] = Now.AddMinutes(-31); break;
            }
        });
        Assert.NotEqual(Hash(), changed);
    }

    [Fact]
    public void Changing_the_item_changes_the_hash()
    {
        var other = CompletionRules.AdoptionHash(CompletionRules.CleanForAdoption(Good(), Guid.Parse("00000000-0000-0000-0000-0000000000a2"), Now));
        Assert.NotEqual(Hash(), other);
    }

    [Fact]
    public void An_adoption_can_never_have_the_hash_of_an_ordinary_completion_of_the_same_procedure_or_of_an_amendment()
    {
        var performed = Now.AddMinutes(-30);
        var adoption = Hash();
        var completion = CompletionRules.RequestHash(new CleanCompletion("adopt-1", Encounter, Provider, performed, [Item]));
        var amendment = CompletionRules.AmendmentHash(new CleanAmendment("adopt-1", 0, Provider, performed, Encounter, "Completed before procedure records existed"));
        Assert.Equal(3, new[] { adoption, completion, amendment }.Distinct().Count());
    }
}
