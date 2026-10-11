using Microsoft.EntityFrameworkCore;
using Alveara.Api.Architecture.Procedures;
using Xunit;

namespace Alveara.Api.Tests;

/// <summary>
/// STORY-014 failure paths against real SQL Server: "procedure fails to save in the catalog" and "system fails to log catalog changes". When the log (or the save) fails, nothing is stored, the person
/// is told so in a stable, non-technical answer (<c>save_failed</c>, 503), and the same change works once the log is back.
/// </summary>
public class ProcedureCatalogFailurePathTests : ProcedureTestBase
{
    private const string RefuseAudit = @"CREATE TRIGGER [TR_Test_RefuseCatalogAudit] ON [AuditLogEntries] INSTEAD OF INSERT AS
BEGIN
    IF EXISTS (SELECT 1 FROM inserted WHERE EntityType = 'ProcedureDefinition') THROW 59020, 'audit unavailable', 1;
    INSERT INTO [AuditLogEntries] SELECT * FROM inserted;
END";

    private async Task Sql(string sql)
    {
        await using var db = Fixture.CreateContext();
        await db.Database.ExecuteSqlRawAsync(sql);
    }

    private async Task<(int Procedures, int Versions, int Events, int Audit)> CountsAsync()
    {
        await using var db = Fixture.CreateContext();
        return (await db.ProcedureDefinitions.CountAsync(), await db.ProcedureVersions.CountAsync(), await db.ProcedureEvents.CountAsync(),
            await db.AuditLogEntries.CountAsync(a => a.EntityType == nameof(ProcedureDefinition)));
    }

    [Fact]
    public async Task A_failed_audit_write_when_adding_a_procedure_saves_nothing_says_so_and_the_same_add_works_once_the_log_is_back()
    {
        await Sql(RefuseAudit);
        var e = await Refused(() => CreateAsync(Input(code: "LOCAL-900")));
        Assert.Equal(("save_failed", 503), (e.Code, e.StatusCode));
        Assert.DoesNotContain("audit", e.Message, StringComparison.OrdinalIgnoreCase);       // no internals reach the person
        Assert.Equal((0, 0, 0, 0), await CountsAsync());

        await Sql("DROP TRIGGER [TR_Test_RefuseCatalogAudit]");
        await CreateAsync(Input(code: "LOCAL-900"));
        Assert.Equal((1, 1, 1, 1), await CountsAsync());
    }

    [Fact]
    public async Task A_failed_audit_write_on_a_later_change_changes_nothing()
    {
        var created = await CreateAsync(Input(code: "LOCAL-901", fee: 50m));
        await Sql(RefuseAudit);

        var revise = await Refused(() => ReviseAsync(created, Like(created, fee: 65m)));
        var inactivate = await Refused(() => With(s => s.InactivateAsync(created.Summary.Id, "Paused", false, created.Summary.RowVersion, S.Actor, default)));
        Assert.All(new[] { revise, inactivate }, e => Assert.Equal(("save_failed", 503), (e.Code, e.StatusCode)));
        Assert.Equal((1, 1, 1, 1), await CountsAsync());

        await Sql("DROP TRIGGER [TR_Test_RefuseCatalogAudit]");
        var revised = await ReviseAsync(created, Like(created, fee: 65m));
        Assert.Equal(2, revised.Summary.CurrentVersionNumber);
        Assert.Equal((1, 2, 2, 2), await CountsAsync());
    }
}
