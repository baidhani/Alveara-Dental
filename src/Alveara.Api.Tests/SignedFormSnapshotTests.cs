using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using Alveara.Api.Architecture.Forms;
using Alveara.Api.Architecture.Patients;
using Xunit;
using static Alveara.Api.Tests.FormTestSupport;

namespace Alveara.Api.Tests;

/// <summary>ALV-N010: a signed snapshot can never be altered - by a later template edit, a patient edit, a void, application code or a direct database statement.</summary>
public class SignedFormSnapshotTests : IAsyncLifetime
{
    private readonly TestDatabaseFixture _fixture = new();
    private FormTestSupport _s = null!;
    private TemplateDetail _template = null!;
    private Guid _patient;
    private PatientFormDetail _signed = null!;

    public async Task InitializeAsync()
    {
        await _fixture.InitializeAsync();
        _s = new FormTestSupport(_fixture);
        _template = await _s.CreateTemplateAsync();
        _patient = await _s.PatientAsync();
        _signed = (await _s.SignAsync(await _s.ReadyAsync(_patient, _template.Template.Id))).Detail;
    }
    public Task DisposeAsync() => _fixture.DisposeAsync();

    [Fact]
    public async Task A_later_template_edit_does_not_change_the_prior_signed_snapshot()
    {
        await using (var db = _fixture.CreateContext())
            await _s.Templates(db).PublishVersionAsync(_template.Template.Id,
                Content("Privacy notice v2", "Completely different wording.", [new FormFieldDefinition("only_new", "New question", FormFieldKinds.Text, true)]),
                _template.Template.RowVersion, _s.Actor, default);

        var after = await _s.ReloadAsync(_signed.Summary.Id);

        Assert.Equal(_signed.Snapshot!.SnapshotHash, after.Snapshot!.SnapshotHash);
        Assert.Equal("Privacy notice", after.Snapshot.Title);
        Assert.Equal("We protect your information.", after.Snapshot.Body);
        Assert.Equal(1, after.Snapshot.TemplateVersionNumber);
        Assert.Equal(3, after.Snapshot.Fields.Count);
        Assert.Equal(_signed.Snapshot.Responses, after.Snapshot.Responses);
        Assert.True(after.Snapshot.IntegrityVerified);
        Assert.Equal(FormStatuses.Signed, after.Summary.Status);
        Assert.False(after.Summary.NewerVersionAvailable); // only drafts are offered a newer version
    }

    [Fact]
    public async Task Inactivating_the_template_leaves_the_signed_form_untouched()
    {
        await using (var db = _fixture.CreateContext())
            await _s.Templates(db).SetActiveAsync(_template.Template.Id, false, _template.Template.RowVersion, _s.Actor, default);

        var after = await _s.ReloadAsync(_signed.Summary.Id);
        Assert.Equal(FormStatuses.Signed, after.Summary.Status);
        Assert.Equal(_signed.Snapshot!.SnapshotHash, after.Snapshot!.SnapshotHash);
    }

    [Fact]
    public async Task A_later_edit_of_the_patients_name_does_not_change_the_snapshot()
    {
        await using (var db = _fixture.CreateContext())
        {
            var p = await db.Patients.AsNoTracking().SingleAsync(x => x.Id == _patient);
            var fields = new PatientFields("Zelda", null, "Zimmer", p.DateOfBirth.ToString("yyyy-MM-dd"), p.Sex, p.Phone, p.Email, p.AddressLine1, p.AddressLine2, p.City, p.State, p.PostalCode);
            await new PatientEditService(db, Clock).UpdateAsync(_patient, fields, Convert.ToBase64String(p.RowVersion), _s.Actor, default);
        }

        var after = await _s.ReloadAsync(_signed.Summary.Id);
        Assert.Equal("Ann Lee", after.Snapshot!.SignerName);
        Assert.Equal(_signed.Snapshot!.SnapshotHash, after.Snapshot.SnapshotHash);
        Assert.True(after.Snapshot.IntegrityVerified);
    }

    [Fact]
    public async Task Application_code_cannot_modify_or_delete_a_signed_snapshot()
    {
        await using (var db = _fixture.CreateContext())
        {
            (await db.SignedFormSnapshots.SingleAsync()).ResponsesJson = """{"acknowledged":"true","nickname":"Forged"}""";
            await Assert.ThrowsAsync<SignedRecordImmutableException>(() => db.SaveChangesAsync());
        }
        await using (var db = _fixture.CreateContext())
        {
            db.SignedFormSnapshots.Remove(await db.SignedFormSnapshots.SingleAsync());
            await Assert.ThrowsAsync<SignedRecordImmutableException>(() => db.SaveChangesAsync());
        }
        Assert.Equal("Annie", (await _s.ReloadAsync(_signed.Summary.Id)).Snapshot!.Responses["nickname"]);
    }

    [Theory]
    [InlineData("UPDATE SignedFormSnapshots SET SignerName = 'Someone Else'")]
    [InlineData("UPDATE SignedFormSnapshots SET ResponsesJson = 'forged'")]
    [InlineData("UPDATE SignedFormSnapshots SET SnapshotHash = 'x'")]
    [InlineData("DELETE FROM SignedFormSnapshots")]
    public async Task The_database_itself_refuses_any_update_or_delete_of_a_signed_snapshot(string statement)
    {
        await using var db = _fixture.CreateContext();

        var ex = await Assert.ThrowsAsync<SqlException>(() => db.Database.ExecuteSqlRawAsync(statement));

        Assert.Equal(51010, ex.Number);
        Assert.Equal(1, await db.SignedFormSnapshots.CountAsync());
        Assert.True(FormDefinition.VerifyIntegrity(await db.SignedFormSnapshots.AsNoTracking().SingleAsync()));
    }

    [Fact]
    public async Task A_signed_form_cannot_be_edited_only_voided_and_replaced()
    {
        var ex = await Assert.ThrowsAsync<FormException>(async () =>
        {
            await using var db = _fixture.CreateContext();
            await _s.Forms(db).SaveDraftAsync(_signed.Summary.Id, new Dictionary<string, string?> { ["nickname"] = "Forged" }, _signed.RowVersion, _s.Actor, default);
        });
        Assert.Equal("form_not_draft", ex.Code);
        Assert.Equal("Annie", (await _s.ReloadAsync(_signed.Summary.Id)).Snapshot!.Responses["nickname"]);
    }

    [Fact]
    public async Task Voiding_a_signed_form_keeps_the_snapshot_exactly_and_records_who_when_and_why()
    {
        Assert.Equal(1, await _s.CountAsync(db => db.SignedFormSnapshots));
        await using (var db = _fixture.CreateContext())
            await _s.Forms(db).VoidAsync(_signed.Summary.Id, "Signed on the wrong patient's chart", _signed.RowVersion, canVoidSigned: true, _s.Actor, default);

        var after = await _s.ReloadAsync(_signed.Summary.Id);
        Assert.Equal(FormStatuses.Void, after.Summary.Status);
        Assert.True(after.Summary.WasSigned);
        Assert.Equal("Signed on the wrong patient's chart", after.Summary.VoidReason);
        Assert.NotNull(after.Summary.VoidedAtUtc);
        Assert.Equal(_signed.Snapshot!.SnapshotHash, after.Snapshot!.SnapshotHash);
        Assert.True(after.Snapshot.IntegrityVerified);
        Assert.Equal([FormEventTypes.Started, FormEventTypes.Signed, FormEventTypes.Voided], after.Events.Select(e => e.EventType));
        Assert.Equal(1, await _s.CountAsync(db => db.SignedFormSnapshots));
    }

    [Fact]
    public void The_hash_covers_every_part_of_the_snapshot()
    {
        var s = Copy();
        Assert.True(FormDefinition.VerifyIntegrity(s));
        foreach (var mutate in new Action<SignedFormSnapshot>[]
        {
            x => x.Body += "!", x => x.Title += "!", x => x.FieldsJson += " ", x => x.ResponsesJson = "{}", x => x.SignerName = "Other", x => x.SignerRelationship = "Parent",
            x => x.SignatureText = "Other", x => x.SignedAtUtc = x.SignedAtUtc.AddSeconds(1), x => x.TemplateVersionNumber = 9, x => x.PatientId = Guid.NewGuid(), x => x.Attestation += "!",
        })
        {
            var t = Copy(); mutate(t);
            Assert.False(FormDefinition.VerifyIntegrity(t));
        }
    }

    private SignedFormSnapshot Copy()
    {
        using var db = _fixture.CreateContext();
        return db.SignedFormSnapshots.AsNoTracking().Single();
    }
}
