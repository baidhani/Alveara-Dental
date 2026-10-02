using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using Alveara.Api.Architecture.Concurrency;
using Alveara.Api.Architecture.Forms;
using Xunit;
using static Alveara.Api.Tests.FormTestSupport;

namespace Alveara.Api.Tests;

/// <summary>ALV-N010: template administration and versioning - an edit is a NEW version, the old one is never touched.</summary>
public class FormTemplateVersionTests : IAsyncLifetime
{
    private readonly TestDatabaseFixture _fixture = new();
    private FormTestSupport _s = null!;

    public async Task InitializeAsync() { await _fixture.InitializeAsync(); _s = new FormTestSupport(_fixture); }
    public Task DisposeAsync() => _fixture.DisposeAsync();

    private async Task<TemplateDetail> PublishAsync(TemplateDetail t, TemplateContent content, string? version = null)
    {
        await using var db = _fixture.CreateContext();
        return await _s.Templates(db).PublishVersionAsync(t.Template.Id, content, version ?? t.Template.RowVersion, _s.Actor, default);
    }

    [Fact]
    public async Task Creating_a_template_publishes_version_one_with_its_fields_and_a_verifiable_hash()
    {
        var t = await _s.CreateTemplateAsync("privacy-notice", FormCategories.Privacy);

        Assert.Equal("privacy-notice", t.Template.Key);
        Assert.True(t.Template.IsActive);
        Assert.Equal(1, t.Template.VersionCount);
        var v1 = Assert.Single(t.Versions);
        Assert.Equal(1, v1.VersionNumber);
        Assert.Equal(t.Template.Current!.Id, v1.Id);
        Assert.Equal(3, v1.Fields.Count);
        Assert.Equal(["Email", "Phone"], v1.Fields[2].Options);
        Assert.True(v1.IntegrityVerified);
    }

    [Theory]
    [InlineData(FormCategories.Privacy)]
    [InlineData(FormCategories.Financial)]
    [InlineData(FormCategories.GeneralConsent)]
    [InlineData(FormCategories.Treatment)]
    public async Task Every_initial_category_is_accepted(string category)
    {
        var t = await _s.CreateTemplateAsync($"form-{category.ToLowerInvariant()}", category);
        Assert.Equal(category, t.Template.Category);
    }

    [Fact]
    public async Task An_edit_publishes_a_new_version_and_leaves_the_previous_one_exactly_as_it_was()
    {
        var t = await _s.CreateTemplateAsync();
        var v1 = t.Versions.Single();

        var edited = await PublishAsync(t, Content("Privacy notice (2026)", "Updated wording.", note: "Reworded"));

        Assert.Equal(2, edited.Template.VersionCount);
        Assert.Equal(2, edited.Template.Current!.VersionNumber);
        Assert.Equal("Reworded", edited.Template.Current.ChangeNote);
        var old = edited.Versions.Single(v => v.VersionNumber == 1);
        Assert.Equal(v1.Id, old.Id);
        Assert.Equal("Privacy notice", old.Title);
        Assert.Equal("We protect your information.", old.Body);
        Assert.Equal(v1.ContentHash, old.ContentHash);
        Assert.All(edited.Versions, v => Assert.True(v.IntegrityVerified));
    }

    [Fact]
    public async Task Saving_unchanged_content_again_publishes_nothing()
    {
        var t = await _s.CreateTemplateAsync();

        var again = await PublishAsync(t, Content());

        Assert.Equal(1, again.Template.VersionCount);
        Assert.Equal(t.Template.Current!.Id, again.Template.Current!.Id);
    }

    [Fact]
    public async Task A_stale_template_version_is_a_concurrency_conflict_and_the_loser_changes_nothing()
    {
        var t = await _s.CreateTemplateAsync();
        await PublishAsync(t, Content(body: "First admin's wording."));

        await Assert.ThrowsAsync<ConcurrencyConflictException>(() => PublishAsync(t, Content(body: "Second admin's wording.")));

        var now = await _s.ReloadTemplateAsync(t.Template.Id);
        Assert.Equal(2, now.Template.VersionCount);
        Assert.Equal("First admin's wording.", now.Template.Current!.Body);
    }

    [Fact]
    public async Task Two_administrators_publishing_at_the_same_moment_produce_exactly_one_new_version()
    {
        var t = await _s.CreateTemplateAsync();

        var results = await Task.WhenAll(Enumerable.Range(0, 2).Select(async i =>
        {
            try { await PublishAsync(t, Content(body: $"Wording {i}")); return true; }
            catch (ConcurrencyConflictException) { return false; }
        }));

        Assert.Equal(1, results.Count(r => r));
        Assert.Equal(2, (await _s.ReloadTemplateAsync(t.Template.Id)).Template.VersionCount);
    }

    [Fact]
    public async Task A_missing_row_version_is_refused()
    {
        var t = await _s.CreateTemplateAsync();
        var ex = await Assert.ThrowsAsync<FormException>(async () =>
        {
            await using var db = _fixture.CreateContext();
            await _s.Templates(db).PublishVersionAsync(t.Template.Id, Content(body: "x"), null, _s.Actor, default);
        });
        Assert.Equal("row_version_required", ex.Code);
    }

    [Fact]
    public async Task A_template_key_is_unique_and_a_duplicate_is_refused()
    {
        await _s.CreateTemplateAsync("consent-a");
        var ex = await Assert.ThrowsAsync<FormException>(() => _s.CreateTemplateAsync("consent-a", FormCategories.Treatment));
        Assert.Equal("template_key_taken", ex.Code);
        Assert.Equal(409, ex.StatusCode);
    }

    [Fact]
    public async Task Two_simultaneous_creations_of_the_same_key_leave_one_template()
    {
        var results = await Task.WhenAll(Enumerable.Range(0, 4).Select(async _ =>
        {
            try { await _s.CreateTemplateAsync("same-key"); return true; }
            catch (FormException ex) when (ex.Code == "template_key_taken") { return false; }
        }));
        Assert.Equal(1, results.Count(r => r));
        Assert.Equal(1, await _s.CountAsync(db => db.FormTemplates.Where(t => t.Key == "same-key")));
        Assert.Equal(1, await _s.CountAsync(db => db.FormTemplateVersions));
    }

    public static IEnumerable<object[]> InvalidTemplates()
    {
        yield return ["Bad Key!", "Title", "Body", null!, "key"];
        yield return ["ok-key", "", "Body", null!, "title"];
        yield return ["ok-key", "Title", "   ", null!, "body"];
        yield return ["ok-key", new string('t', 201), "Body", null!, "title"];
        yield return ["ok-key", "Title", "Body", new[] { new FormFieldDefinition("Bad Id", "L", FormFieldKinds.Text, false) }, "fields[0].id"];
        yield return ["ok-key", "Title", "Body", new[] { new FormFieldDefinition("a", "L", FormFieldKinds.Text, false), new FormFieldDefinition("a", "M", FormFieldKinds.Text, false) }, "fields[1].id"];
        yield return ["ok-key", "Title", "Body", new[] { new FormFieldDefinition("a", "", FormFieldKinds.Text, false) }, "fields[0].label"];
        yield return ["ok-key", "Title", "Body", new[] { new FormFieldDefinition("a", "L", "slider", false) }, "fields[0].kind"];
        yield return ["ok-key", "Title", "Body", new[] { new FormFieldDefinition("a", "L", FormFieldKinds.Choice, false, ["only-one"]) }, "fields[0].options"];
        yield return ["ok-key", "Title", "Body", new[] { new FormFieldDefinition("a", "L", FormFieldKinds.Choice, false, ["x", "x"]) }, "fields[0].options"];
    }

    [Theory]
    [MemberData(nameof(InvalidTemplates))]
    public async Task Invalid_template_content_is_refused_with_a_message_for_the_field_and_nothing_is_stored(string key, string title, string body, FormFieldDefinition[]? fields, string expectedField)
    {
        var ex = await Assert.ThrowsAsync<FormException>(() => _s.CreateTemplateAsync(key, FormCategories.Privacy, new TemplateContent(title, body, fields, null)));
        Assert.Equal("validation_failed", ex.Code);
        Assert.Contains(expectedField, ex.FieldErrors.Keys);
        Assert.Equal(0, await _s.CountAsync(db => db.FormTemplates));
    }

    [Fact]
    public async Task More_than_the_allowed_number_of_fields_is_refused()
    {
        var many = Enumerable.Range(0, FormDefinition.MaxFields + 1).Select(i => new FormFieldDefinition($"f{i}", "L", FormFieldKinds.Text, false)).ToList();
        var ex = await Assert.ThrowsAsync<FormException>(() => _s.CreateTemplateAsync("too-many", FormCategories.Privacy, Content(fields: many)));
        Assert.Contains("fields", ex.FieldErrors.Keys);
    }

    [Fact]
    public async Task An_unknown_category_is_refused()
    {
        var ex = await Assert.ThrowsAsync<FormException>(() => _s.CreateTemplateAsync("odd", "Marketing"));
        Assert.Contains("category", ex.FieldErrors.Keys);
    }

    [Fact]
    public async Task A_template_can_be_inactivated_and_reactivated_and_a_repeat_changes_nothing()
    {
        var t = await _s.CreateTemplateAsync();
        await using (var db = _fixture.CreateContext())
        {
            var off = await _s.Templates(db).SetActiveAsync(t.Template.Id, false, t.Template.RowVersion, _s.Actor, default);
            Assert.False(off.Template.IsActive);
            // same value again with the (now stale) version is a no-op only when the version still matches
            var again = await _s.Templates(db).SetActiveAsync(t.Template.Id, false, off.Template.RowVersion, _s.Actor, default);
            Assert.Equal(off.Template.RowVersion, again.Template.RowVersion);
        }
        Assert.Equal(1, await _s.CountAsync(db => db.AuditLogEntries.Where(a => a.EventType == FormAuditEvents.TemplateStatusChanged)));
        var reloaded = await _s.ReloadTemplateAsync(t.Template.Id);
        await using var db2 = _fixture.CreateContext();
        Assert.True((await _s.Templates(db2).SetActiveAsync(t.Template.Id, true, reloaded.Template.RowVersion, _s.Actor, default)).Template.IsActive);
    }

    [Fact]
    public async Task The_active_listing_hides_inactive_templates_and_the_full_listing_shows_them()
    {
        var a = await _s.CreateTemplateAsync("form-a");
        await _s.CreateTemplateAsync("form-b");
        await using (var db = _fixture.CreateContext()) await _s.Templates(db).SetActiveAsync(a.Template.Id, false, a.Template.RowVersion, _s.Actor, default);

        await using var read = _fixture.CreateContext();
        Assert.Equal(["form-b"], (await _s.Templates(read).ListAsync(false, default)).Select(t => t.Key));
        Assert.Equal(2, (await _s.Templates(read).ListAsync(true, default)).Count);
    }

    [Fact]
    public async Task Template_administration_is_audited_with_the_key_and_version_and_no_wording()
    {
        var t = await _s.CreateTemplateAsync("privacy-notice", content: Content(body: "SECRET-WORDING"));
        await PublishAsync(t, Content(body: "SECRET-WORDING-2"));

        await using var db = _fixture.CreateContext();
        var entries = await db.AuditLogEntries.Where(a => a.EventType == FormAuditEvents.TemplateCreated || a.EventType == FormAuditEvents.TemplateVersionPublished).ToListAsync();
        Assert.Equal(2, entries.Count);
        Assert.All(entries, e => { Assert.Equal(_s.Actor, e.PerformedByUserAccountId); Assert.DoesNotContain("SECRET", e.Details); Assert.Contains("privacy-notice", e.Details); });
        Assert.Contains(entries, e => e.Details.Contains("version 2"));
    }

    // ---------- immutability of published versions ----------

    [Fact]
    public async Task The_application_refuses_to_change_or_delete_a_published_version()
    {
        var t = await _s.CreateTemplateAsync();

        await using (var db = _fixture.CreateContext())
        {
            var v = await db.FormTemplateVersions.SingleAsync();
            v.Body = "Quietly changed.";
            await Assert.ThrowsAsync<SignedRecordImmutableException>(() => db.SaveChangesAsync());
        }
        await using (var db = _fixture.CreateContext())
        {
            db.FormTemplateVersions.Remove(await db.FormTemplateVersions.SingleAsync());
            await Assert.ThrowsAsync<SignedRecordImmutableException>(() => db.SaveChangesAsync());
        }
        Assert.Equal("We protect your information.", (await _s.ReloadTemplateAsync(t.Template.Id)).Versions.Single().Body);
    }

    [Fact]
    public async Task The_database_itself_refuses_an_update_or_delete_of_a_published_version_from_any_path()
    {
        await _s.CreateTemplateAsync();
        await using var db = _fixture.CreateContext();

        var update = await Assert.ThrowsAsync<SqlException>(() => db.Database.ExecuteSqlRawAsync("UPDATE FormTemplateVersions SET Body = 'x'"));
        Assert.Equal(51011, update.Number);
        var delete = await Assert.ThrowsAsync<SqlException>(() => db.Database.ExecuteSqlRawAsync("DELETE FROM FormTemplateVersions"));
        Assert.Equal(51011, delete.Number);
        Assert.Equal(1, await db.FormTemplateVersions.CountAsync());
    }

    [Fact]
    public async Task A_version_whose_content_no_longer_matches_its_hash_is_reported_as_failing_verification()
    {
        var t = await _s.CreateTemplateAsync();
        var v = t.Versions.Single();
        var tampered = new FormTemplateVersion
        {
            Id = v.Id, TemplateId = t.Template.Id, VersionNumber = 1, Title = v.Title, Body = "Altered.", FieldsJson = FormDefinition.SerializeFields(v.Fields), ContentHash = v.ContentHash,
        };
        Assert.False(FormDefinition.VerifyIntegrity(tampered));
    }
}
