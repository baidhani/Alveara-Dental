using Xunit;
using Alveara.Api.Architecture.FollowUps;

namespace Alveara.Api.Tests;

/// <summary>STORY-009: the pure rules applied before a follow-up is stored, without a database: trimmed text and its bounds, the due-date window, the interval, the key and the deterministic ids that make a repeat harmless.</summary>
public class FollowUpValidationTests
{
    private static readonly DateTimeOffset Now = new(2026, 10, 24, 12, 0, 0, TimeSpan.Zero);

    private static string CodeOf(Action action) => Assert.Throws<FollowUpException>(action).Code;

    [Fact]
    public void A_title_is_trimmed_and_must_be_one_short_line()
    {
        Assert.Equal("Call the patient", FollowUpValidation.Title("  Call the patient \t"));
        Assert.Equal(new string('x', 120), FollowUpValidation.Title(new string('x', 120)));
        foreach (var bad in new[] { null, "", "   ", new string('x', 121), "a\nb", "a\u0000b" })
            Assert.Equal("title_invalid", CodeOf(() => FollowUpValidation.Title(bad)));
    }

    [Fact]
    public void A_note_is_optional_trimmed_and_may_span_lines_but_holds_no_other_control_character()
    {
        Assert.Null(FollowUpValidation.Note(null));
        Assert.Null(FollowUpValidation.Note("   \n "));
        Assert.Equal("one\ntwo\tthree", FollowUpValidation.Note(" one\ntwo\tthree "));
        Assert.Equal(500, FollowUpValidation.Note(new string('n', 500))!.Length);
        Assert.Equal("note_invalid", CodeOf(() => FollowUpValidation.Note(new string('n', 501))));
        Assert.Equal("note_invalid", CodeOf(() => FollowUpValidation.Note("bell\u0007")));
    }

    [Fact]
    public void Cancelling_needs_a_reason()
    {
        Assert.Equal("Moved away", FollowUpValidation.CancelNote(" Moved away "));
        Assert.Equal("note_required", CodeOf(() => FollowUpValidation.CancelNote("  ")));
        Assert.Equal("note_required", CodeOf(() => FollowUpValidation.CancelNote(null)));
    }

    [Fact]
    public void A_due_date_must_be_present_and_inside_the_window_and_is_kept_in_utc()
    {
        Assert.Equal("due_required", CodeOf(() => FollowUpValidation.Due(null, Now)));
        Assert.Equal(Now.AddDays(-FollowUpRules.MaxPastDays), FollowUpValidation.Due(Now.AddDays(-FollowUpRules.MaxPastDays), Now));                  // the edges are inside
        Assert.Equal(Now.AddDays(FollowUpRules.MaxFutureDays), FollowUpValidation.Due(Now.AddDays(FollowUpRules.MaxFutureDays), Now));
        Assert.Equal("due_out_of_range", CodeOf(() => FollowUpValidation.Due(Now.AddDays(-FollowUpRules.MaxPastDays).AddSeconds(-1), Now)));
        Assert.Equal("due_out_of_range", CodeOf(() => FollowUpValidation.Due(Now.AddDays(FollowUpRules.MaxFutureDays).AddSeconds(1), Now)));
        var local = new DateTimeOffset(2026, 11, 3, 9, 0, 0, TimeSpan.FromHours(-5));
        Assert.Equal(TimeSpan.Zero, FollowUpValidation.Due(local, Now).Offset);
        Assert.Equal(local, FollowUpValidation.Due(local, Now));                                                                                       // the same instant
    }

    [Theory]
    [InlineData(null, true)]
    [InlineData(1, true)]
    [InlineData(3650, true)]
    [InlineData(0, false)]
    [InlineData(-1, false)]
    [InlineData(3651, false)]
    public void A_recall_interval_is_one_to_3650_days_or_none(int? days, bool allowed)
    {
        if (allowed) Assert.Equal(days, FollowUpValidation.Interval(days));
        else Assert.Equal("interval_invalid", CodeOf(() => FollowUpValidation.Interval(days)));
    }

    [Fact]
    public void An_idempotency_key_is_one_to_64_characters_and_a_row_version_is_base64()
    {
        Assert.Equal("k", FollowUpValidation.Key("k"));
        Assert.Equal(64, FollowUpValidation.Key(new string('k', 64)).Length);
        foreach (var bad in new[] { null, "", " ", new string('k', 65) })
            Assert.Equal("idempotency_key_required", CodeOf(() => FollowUpValidation.Key(bad)));
        Assert.Equal(new byte[] { 1, 2, 3 }, FollowUpValidation.Version(Convert.ToBase64String(new byte[] { 1, 2, 3 })));
        foreach (var bad in new[] { null, "", "not base64!" })
            Assert.Equal("row_version_required", CodeOf(() => FollowUpValidation.Version(bad)));
    }

    [Fact]
    public void The_deterministic_id_is_stable_for_the_same_input_and_differs_by_person_key_and_purpose()
    {
        var person = Guid.Parse("11111111-1111-1111-1111-111111111111");
        var other = Guid.Parse("22222222-2222-2222-2222-222222222222");
        var id = FollowUpValidation.DeterministicId("create", person, "key-1");
        Assert.Equal(id, FollowUpValidation.DeterministicId("create", person, "key-1"));
        Assert.NotEqual(id, FollowUpValidation.DeterministicId("create", person, "key-2"));
        Assert.NotEqual(id, FollowUpValidation.DeterministicId("create", other, "key-1"));
        Assert.NotEqual(id, FollowUpValidation.DeterministicId("recall", person, "key-1"));
        Assert.NotEqual(Guid.Empty, id);
        Assert.NotEqual(FollowUpValidation.DeterministicId("recall", person), FollowUpValidation.DeterministicId("recall", other));
    }
}
