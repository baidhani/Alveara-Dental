using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Alveara.Api.Architecture.Forms;
using Alveara.Api.Architecture.Identity;
using Alveara.Api.Architecture.Measurement;
using Alveara.Api.Architecture.Time;
using Alveara.Api.Data;

namespace Alveara.Api.Tests;

/// <summary>Shared arrangement for ALV-N010's form tests: templates, patients and forms are all created through the real services against real SQL Server.</summary>
public sealed class FormTestSupport(TestDatabaseFixture fixture)
{
    public static readonly IPracticeClock Clock = PatientTestSupport.Clock;
    public readonly Guid Actor = Guid.NewGuid();
    private readonly PatientTestSupport _patients = new(fixture);
    private int _n;

    public FormTemplateService Templates(AlveraDbContext db) => new(db, Clock);
    public PatientFormService Forms(AlveraDbContext db, IMeasurementEventSink? measurements = null) => new(db, Clock, measurements);
    public PatientFormReader Reader(AlveraDbContext db) => new(db);

    public static IReadOnlyList<FormFieldDefinition> DefaultFields() =>
    [
        new("acknowledged", "I have read the privacy notice", FormFieldKinds.Checkbox, true),
        new("nickname", "Preferred name", FormFieldKinds.Text, false),
        new("contact", "Preferred contact", FormFieldKinds.Choice, false, ["Email", "Phone"]),
    ];

    public static TemplateContent Content(string title = "Privacy notice", string body = "We protect your information.", IReadOnlyList<FormFieldDefinition>? fields = null, string? note = null) =>
        new(title, body, fields ?? DefaultFields(), note);

    public async Task<TemplateDetail> CreateTemplateAsync(string key = "privacy-notice", string category = FormCategories.Privacy, TemplateContent? content = null)
    {
        await using var db = fixture.CreateContext();
        return await Templates(db).CreateAsync(key, category, content ?? Content(), Actor, default);
    }

    public async Task<TemplateDetail> ReloadTemplateAsync(Guid id)
    {
        await using var db = fixture.CreateContext();
        return await Templates(db).GetAsync(id, default);
    }

    public Task<Guid> PatientAsync(string first = "Ann", string last = "Lee") => _patients.RegisterAsync(first, last, $"19{80 + _n++ % 19}-0{1 + _n % 9}-1{_n % 9}").ContinueWith(t => t.Result.Id);

    public async Task<PatientFormDetail> StartAsync(Guid patientId, Guid templateId)
    {
        await using var db = fixture.CreateContext();
        return (await Forms(db).StartAsync(patientId, templateId, Actor, default)).Detail;
    }

    public async Task<PatientFormDetail> FillAsync(PatientFormDetail form, params (string Field, string Value)[] answers)
    {
        await using var db = fixture.CreateContext();
        return await Forms(db).SaveDraftAsync(form.Summary.Id, answers.ToDictionary(a => a.Field, a => (string?)a.Value), form.RowVersion, Actor, default);
    }

    /// <summary>A complete, ready-to-sign draft.</summary>
    public async Task<PatientFormDetail> ReadyAsync(Guid patientId, Guid templateId) =>
        await FillAsync(await StartAsync(patientId, templateId), ("acknowledged", "true"), ("nickname", "Annie"), ("contact", "Email"));

    public static SignInput SignInput(PatientFormDetail form, string signer = "Ann Lee", string relationship = "Self", string? note = null, string? signature = null, bool attested = true) =>
        new(signer, relationship, note, signature ?? signer, attested, form.Version.Id, form.RowVersion);

    public async Task<(PatientFormDetail Detail, bool Created)> SignAsync(PatientFormDetail form, string? key = null, SignInput? input = null)
    {
        await using var db = fixture.CreateContext();
        return await Forms(db).SignAsync(form.Summary.Id, input ?? SignInput(form), key ?? $"sign-{Interlocked.Increment(ref _n)}-{Guid.NewGuid():N}", Actor, default);
    }

    public async Task<PatientFormDetail> ReloadAsync(Guid formId)
    {
        await using var db = fixture.CreateContext();
        return await Reader(db).GetAsync(formId, default);
    }

    public async Task<int> CountAsync(Func<AlveraDbContext, IQueryable<object>> query)
    {
        await using var db = fixture.CreateContext();
        return await query(db).CountAsync();
    }

    /// <summary>A context whose saves fail whenever an audit entry is part of them, to simulate an interrupted/failed signature.</summary>
    public AlveraDbContext FailingAuditContext() =>
        new(new DbContextOptionsBuilder<AlveraDbContext>().UseSqlServer(fixture.ConnectionString).AddInterceptors(new FailAuditWrites()).Options);

    private sealed class FailAuditWrites : SaveChangesInterceptor
    {
        public override ValueTask<InterceptionResult<int>> SavingChangesAsync(DbContextEventData eventData, InterceptionResult<int> result, CancellationToken cancellationToken = default)
        {
            if (eventData.Context!.ChangeTracker.Entries<AuditLogEntry>().Any(e => e.State == EntityState.Added))
                throw new DbUpdateException("simulated audit storage failure");
            return base.SavingChangesAsync(eventData, result, cancellationToken);
        }
    }
}
