using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using Alveara.Api.Architecture.Safety;
using Xunit;
using static Alveara.Api.Tests.SchedulingTestSupport;

namespace Alveara.Api.Tests;

/// <summary>
/// ALV-N011 persistence, against real SQL Server: the database itself keeps patient-safety data honest, independently of any application code. It accepts valid alerts and
/// clearances; refuses categories, severities and statuses outside the model (case-sensitively); refuses a "resolved" alert without who, when and why and a "resolved" clearance
/// that was never received; allows one active alert and one open clearance per subject; never deletes an alert or a clearance; and keeps both histories and every acknowledgement
/// append-only.
/// </summary>
public class SafetySchemaTests : IAsyncLifetime
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

    private static async Task<SqlException> RefusedAsync(Func<Task> write)
    {
        var ex = await Assert.ThrowsAsync<DbUpdateException>(write);
        return Assert.IsType<SqlException>(ex.InnerException);
    }

    private SafetyAlert NewAlert(string title = "Prosthetic heart valve", string category = "Condition", string severity = "High", string status = "Active", string source = "Patient") => new()
    {
        Id = Guid.NewGuid(), PatientId = _ann, Category = category, Title = title, Severity = severity, Status = status, SourceNote = source, CreatedAtUtc = At,
    };

    private async Task<Guid> AlertAsync(Action<SafetyAlert>? tweak = null)
    {
        await using var db = _fixture.CreateContext();
        var a = NewAlert();
        tweak?.Invoke(a);
        db.SafetyAlerts.Add(a);
        await db.SaveChangesAsync();
        return a.Id;
    }

    private Clearance NewClearance(string status = "Requested", string reason = "Cardiac clearance", string key = "k1") => new()
    {
        Id = Guid.NewGuid(), PatientId = _ann, Kind = "Medical", Reason = reason, ReasonKey = key, Status = status, RequestedAtUtc = At,
    };

    private async Task<Guid> ClearanceAsync(Action<Clearance>? tweak = null)
    {
        await using var db = _fixture.CreateContext();
        var c = NewClearance();
        tweak?.Invoke(c);
        db.Clearances.Add(c);
        await db.SaveChangesAsync();
        return c.Id;
    }

    // ---------- alerts ----------

    [Fact]
    public async Task The_database_accepts_each_alert_category_a_resolved_alert_with_its_stamp_and_an_active_one()
    {
        foreach (var category in SafetyCategories.AlertCategories) await AlertAsync(a => { a.Category = category; a.Title = category; });
        await AlertAsync(a =>
        {
            a.Title = "Resolved one"; a.Status = "Resolved"; a.ResolvedAtUtc = At; a.ResolvedByUserId = Guid.NewGuid(); a.ResolutionReason = "Entered in error";
        });
        Assert.Equal(6, await _s.CountAsync(db => db.SafetyAlerts));
    }

    [Theory]
    [InlineData("Allergy", "High", "Active")]              // allergies come from the clinical record, not from alerts
    [InlineData("Medication", "High", "Active")]
    [InlineData("condition", "High", "Active")]            // case-sensitive: the default collation would accept it
    [InlineData("Condition", "Urgent", "Active")]
    [InlineData("Condition", "high", "Active")]
    [InlineData("Condition", "High", "active")]
    [InlineData("Condition", "High", "Dismissed")]
    public async Task The_database_refuses_a_category_severity_or_status_outside_the_model(string category, string severity, string status)
    {
        var ex = await RefusedAsync(() => AlertAsync(a => { a.Category = category; a.Severity = severity; a.Status = status; }));
        Assert.Equal(547, ex.Number);
        Assert.Equal(0, await _s.CountAsync(db => db.SafetyAlerts));
    }

    [Fact]
    public async Task The_database_refuses_an_alert_with_a_blank_title_or_a_blank_source()
    {
        Assert.Equal(547, (await RefusedAsync(() => AlertAsync(a => a.Title = "   "))).Number);
        Assert.Equal(547, (await RefusedAsync(() => AlertAsync(a => a.SourceNote = "  "))).Number);
    }

    [Theory]
    [InlineData(false, true, true, "reason")]       // resolved without when
    [InlineData(true, false, true, "reason")]       // without who
    [InlineData(true, true, false, null)]           // without a reason
    [InlineData(true, true, true, "   ")]           // with a blank reason
    public async Task A_resolved_alert_needs_who_when_and_a_real_reason_in_the_database_too(bool when, bool who, bool hasReason, string? reason)
    {
        var ex = await RefusedAsync(() => AlertAsync(a =>
        {
            a.Status = "Resolved"; a.ResolvedAtUtc = when ? At : null; a.ResolvedByUserId = who ? Guid.NewGuid() : null; a.ResolutionReason = hasReason ? reason : null;
        }));
        Assert.Equal(547, ex.Number);
    }

    [Fact]
    public async Task An_active_alert_cannot_carry_resolution_fields()
    {
        Assert.Equal(547, (await RefusedAsync(() => AlertAsync(a => a.ResolutionReason = "Leftover"))).Number);
    }

    [Fact]
    public async Task One_active_alert_per_category_and_title_but_a_resolved_one_does_not_block_a_new_one()
    {
        var first = await AlertAsync();
        Assert.Equal(2601, (await RefusedAsync(() => AlertAsync(a => a.Title = "PROSTHETIC HEART VALVE"))).Number);          // a unique index, case-insensitive
        await AlertAsync(a => { a.Category = "Pregnancy"; });                                                                 // another category is its own
        await using (var db = _fixture.CreateContext())
        {
            var a = await db.SafetyAlerts.SingleAsync(x => x.Id == first);
            a.Status = "Resolved"; a.ResolvedAtUtc = At; a.ResolvedByUserId = Guid.NewGuid(); a.ResolutionReason = "Gone";
            await db.SaveChangesAsync();
        }
        await AlertAsync();                                                                                                   // now allowed
        Assert.Equal(3, await _s.CountAsync(db => db.SafetyAlerts));
    }

    [Fact]
    public async Task An_alert_is_never_deleted()
    {
        var id = await AlertAsync();
        await using var db = _fixture.CreateContext();
        db.SafetyAlerts.Remove(await db.SafetyAlerts.SingleAsync(x => x.Id == id));
        Assert.Equal(51050, (await RefusedAsync(() => db.SaveChangesAsync())).Number);
        Assert.Equal(1, await _s.CountAsync(d => d.SafetyAlerts));
    }

    [Fact]
    public async Task Alert_history_is_append_only_unique_per_version_and_limited_to_the_known_change_types()
    {
        var id = await AlertAsync();
        Guid version;
        await using (var db = _fixture.CreateContext())
        {
            var v = new SafetyAlertVersion { Id = Guid.NewGuid(), AlertId = id, PatientId = _ann, VersionNumber = 1, ChangeType = "Created", Category = "Condition", Title = "x", Severity = "High", SourceNote = "s", Status = "Active", OccurredAtUtc = At };
            db.SafetyAlertVersions.Add(v);
            await db.SaveChangesAsync();
            version = v.Id;
        }
        await using (var db = _fixture.CreateContext())
        {
            db.SafetyAlertVersions.Add(new SafetyAlertVersion { Id = Guid.NewGuid(), AlertId = id, PatientId = _ann, VersionNumber = 1, ChangeType = "Changed", Category = "Condition", Title = "x", Severity = "High", SourceNote = "s", Status = "Active", OccurredAtUtc = At });
            Assert.Equal(2601, (await RefusedAsync(() => db.SaveChangesAsync())).Number);
        }
        await using (var db = _fixture.CreateContext())
        {
            db.SafetyAlertVersions.Add(new SafetyAlertVersion { Id = Guid.NewGuid(), AlertId = id, PatientId = _ann, VersionNumber = 2, ChangeType = "Dismissed", Category = "Condition", Title = "x", Severity = "High", SourceNote = "s", Status = "Active", OccurredAtUtc = At });
            Assert.Equal(547, (await RefusedAsync(() => db.SaveChangesAsync())).Number);
        }
        await using (var db = _fixture.CreateContext())
        {
            (await db.SafetyAlertVersions.SingleAsync(v => v.Id == version)).Title = "Rewritten";
            Assert.Equal(51051, (await RefusedAsync(() => db.SaveChangesAsync())).Number);
        }
        await using (var db = _fixture.CreateContext())
        {
            db.SafetyAlertVersions.Remove(await db.SafetyAlertVersions.SingleAsync(v => v.Id == version));
            Assert.Equal(51051, (await RefusedAsync(() => db.SaveChangesAsync())).Number);
        }
    }

    [Fact]
    public async Task Acknowledgements_are_unique_per_person_and_revision_and_can_neither_be_changed_nor_deleted()
    {
        var id = await AlertAsync();
        var user = Guid.NewGuid();
        Guid ack;
        await using (var db = _fixture.CreateContext())
        {
            var a = new SafetyAlertAcknowledgement { Id = Guid.NewGuid(), AlertId = id, PatientId = _ann, UserId = user, Revision = 1, AcknowledgedAtUtc = At };
            db.SafetyAlertAcknowledgements.Add(a);
            await db.SaveChangesAsync();
            ack = a.Id;
            db.SafetyAlertAcknowledgements.Add(new SafetyAlertAcknowledgement { Id = Guid.NewGuid(), AlertId = id, PatientId = _ann, UserId = Guid.NewGuid(), Revision = 1, AcknowledgedAtUtc = At }); // another person: fine
            db.SafetyAlertAcknowledgements.Add(new SafetyAlertAcknowledgement { Id = Guid.NewGuid(), AlertId = id, PatientId = _ann, UserId = user, Revision = 2, AcknowledgedAtUtc = At });          // a new revision: fine
            await db.SaveChangesAsync();
        }
        await using (var db = _fixture.CreateContext())
        {
            db.SafetyAlertAcknowledgements.Add(new SafetyAlertAcknowledgement { Id = Guid.NewGuid(), AlertId = id, PatientId = _ann, UserId = user, Revision = 1, AcknowledgedAtUtc = At });
            Assert.Equal(2601, (await RefusedAsync(() => db.SaveChangesAsync())).Number);
        }
        await using (var db = _fixture.CreateContext())
        {
            (await db.SafetyAlertAcknowledgements.SingleAsync(a => a.Id == ack)).Revision = 9;
            Assert.Equal(51052, (await RefusedAsync(() => db.SaveChangesAsync())).Number);
        }
        await using (var db = _fixture.CreateContext())
        {
            db.SafetyAlertAcknowledgements.Remove(await db.SafetyAlertAcknowledgements.SingleAsync(a => a.Id == ack));
            Assert.Equal(51052, (await RefusedAsync(() => db.SaveChangesAsync())).Number);
        }
        Assert.Equal(3, await _s.CountAsync(d => d.SafetyAlertAcknowledgements));
    }

    // ---------- clearances ----------

    [Fact]
    public async Task The_database_accepts_every_clearance_status_with_the_stamps_that_go_with_it()
    {
        var who = Guid.NewGuid();
        await ClearanceAsync(c => c.ReasonKey = "a");
        await ClearanceAsync(c => { c.ReasonKey = "b"; c.Status = "Received"; c.ReceivedAtUtc = At; c.ReceivedByUserId = who; });
        await ClearanceAsync(c => { c.ReasonKey = "c"; c.Status = "Resolved"; c.ReceivedAtUtc = At; c.ReceivedByUserId = who; c.ClosedAtUtc = At; c.ClosedByUserId = who; c.ClosingReason = "Cleared"; });
        await ClearanceAsync(c => { c.ReasonKey = "d"; c.Status = "Cancelled"; c.ClosedAtUtc = At; c.ClosedByUserId = who; c.ClosingReason = "No longer needed"; });
        Assert.Equal(4, await _s.CountAsync(db => db.Clearances));
    }

    [Fact]
    public async Task The_database_refuses_a_clearance_that_skipped_a_step_or_closed_without_who_when_and_why()
    {
        var who = Guid.NewGuid();
        // resolved but never received
        Assert.Equal(547, (await RefusedAsync(() => ClearanceAsync(c => { c.Status = "Resolved"; c.ClosedAtUtc = At; c.ClosedByUserId = who; c.ClosingReason = "Assumed"; }))).Number);
        // received without who
        Assert.Equal(547, (await RefusedAsync(() => ClearanceAsync(c => { c.Status = "Received"; c.ReceivedAtUtc = At; }))).Number);
        // resolved with a blank reason
        Assert.Equal(547, (await RefusedAsync(() => ClearanceAsync(c => { c.Status = "Resolved"; c.ReceivedAtUtc = At; c.ReceivedByUserId = who; c.ClosedAtUtc = At; c.ClosedByUserId = who; c.ClosingReason = "  "; }))).Number);
        // cancelled without a reason
        Assert.Equal(547, (await RefusedAsync(() => ClearanceAsync(c => { c.Status = "Cancelled"; c.ClosedAtUtc = At; c.ClosedByUserId = who; }))).Number);
        // a still-requested clearance with a close stamp
        Assert.Equal(547, (await RefusedAsync(() => ClearanceAsync(c => { c.ClosedAtUtc = At; }))).Number);
        Assert.Equal(0, await _s.CountAsync(db => db.Clearances));
    }

    [Theory]
    [InlineData("Surgical", "Requested")]
    [InlineData("medical", "Requested")]
    [InlineData("Medical", "requested")]
    [InlineData("Medical", "Pending")]
    public async Task The_database_refuses_a_clearance_kind_or_status_outside_the_model(string kind, string status)
    {
        Assert.Equal(547, (await RefusedAsync(() => ClearanceAsync(c => { c.Kind = kind; c.Status = status; }))).Number);
    }

    [Fact]
    public async Task A_clearance_needs_a_reason_and_only_one_clearance_per_reason_can_be_open_at_a_time()
    {
        Assert.Equal(547, (await RefusedAsync(() => ClearanceAsync(c => c.Reason = "   "))).Number);
        var first = await ClearanceAsync();
        Assert.Equal(2601, (await RefusedAsync(() => ClearanceAsync())).Number);                          // the same open clearance again
        await ClearanceAsync(c => c.ReasonKey = "other-reason");                                           // a different reason is its own
        await using (var db = _fixture.CreateContext())
        {
            var c = await db.Clearances.SingleAsync(x => x.Id == first);
            c.Status = "Cancelled"; c.ClosedAtUtc = At; c.ClosedByUserId = Guid.NewGuid(); c.ClosingReason = "No longer needed";
            await db.SaveChangesAsync();
        }
        await ClearanceAsync();                                                                            // a closed one does not block asking again
        Assert.Equal(3, await _s.CountAsync(db => db.Clearances));
    }

    [Fact]
    public async Task A_clearance_is_never_deleted_and_its_history_is_append_only()
    {
        var id = await ClearanceAsync();
        await using (var db = _fixture.CreateContext())
        {
            db.Clearances.Remove(await db.Clearances.SingleAsync(x => x.Id == id));
            Assert.Equal(51053, (await RefusedAsync(() => db.SaveChangesAsync())).Number);
        }
        Guid version;
        await using (var db = _fixture.CreateContext())
        {
            var v = new ClearanceVersion { Id = Guid.NewGuid(), ClearanceId = id, PatientId = _ann, VersionNumber = 1, ChangeType = "Requested", Kind = "Medical", Reason = "r", Status = "Requested", OccurredAtUtc = At };
            db.ClearanceVersions.Add(v);
            await db.SaveChangesAsync();
            version = v.Id;
        }
        await using (var db = _fixture.CreateContext())
        {
            db.ClearanceVersions.Add(new ClearanceVersion { Id = Guid.NewGuid(), ClearanceId = id, PatientId = _ann, VersionNumber = 1, ChangeType = "Received", Kind = "Medical", Reason = "r", Status = "Received", OccurredAtUtc = At });
            Assert.Equal(2601, (await RefusedAsync(() => db.SaveChangesAsync())).Number);
        }
        await using (var db = _fixture.CreateContext())
        {
            db.ClearanceVersions.Add(new ClearanceVersion { Id = Guid.NewGuid(), ClearanceId = id, PatientId = _ann, VersionNumber = 2, ChangeType = "Waived", Kind = "Medical", Reason = "r", Status = "Received", OccurredAtUtc = At });
            Assert.Equal(547, (await RefusedAsync(() => db.SaveChangesAsync())).Number);
        }
        await using (var db = _fixture.CreateContext())
        {
            (await db.ClearanceVersions.SingleAsync(v => v.Id == version)).Reason = "Rewritten";
            Assert.Equal(51054, (await RefusedAsync(() => db.SaveChangesAsync())).Number);
        }
        await using (var db = _fixture.CreateContext())
        {
            db.ClearanceVersions.Remove(await db.ClearanceVersions.SingleAsync(v => v.Id == version));
            Assert.Equal(51054, (await RefusedAsync(() => db.SaveChangesAsync())).Number);
        }
    }
}
