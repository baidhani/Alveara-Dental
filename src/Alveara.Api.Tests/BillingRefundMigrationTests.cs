using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Microsoft.Extensions.DependencyInjection;
using Alveara.Api.Architecture.Billing;
using Xunit;

namespace Alveara.Api.Tests;

/// <summary>
/// ALV-007-C02: the migration that adds refunds, allocation releases and issued documents can be rolled back and forward again with a ledger in the database. Rolling back removes the two new tables, the new
/// entry columns and the new triggers, restores the ALV-007-C01 allocation trigger (which does not know about releases) and keeps every entry and allocation; the ledger still takes a new application. Rolling
/// forward restores everything and the database again refuses to delete a release or to refund a payment for more than it was. A rollback is only possible while no refund has been recorded: afterwards the
/// ledger holds money returned and is deliberately not rewritten by a script (the same stance as the earlier ledger migrations).
/// </summary>
public class BillingRefundMigrationTests : BillingTestBase
{
    private const string Before = "20261012020128_AddCompletedProcedures";

    private async Task<int> CountAsync(string catalog, string name)
    {
        await using var db = Fixture.CreateContext();
        return catalog == "tables"
            ? await db.Database.SqlQuery<int>($"SELECT COUNT(*) AS [Value] FROM sys.tables WHERE name = {name}").SingleAsync()
            : await db.Database.SqlQuery<int>($"SELECT COUNT(*) AS [Value] FROM sys.triggers WHERE name = {name}").SingleAsync();
    }

    private async Task<bool> ColumnExistsAsync(string table, string column)
    {
        await using var db = Fixture.CreateContext();
        return await db.Database.SqlQuery<int>($"SELECT COUNT(*) AS [Value] FROM sys.columns WHERE object_id = OBJECT_ID({table}) AND name = {column}").SingleAsync() > 0;
    }

    private async Task<string> ReversalTriggerAsync()
    {
        await using var db = Fixture.CreateContext();
        return await db.Database.SqlQuery<string>($"SELECT OBJECT_DEFINITION(OBJECT_ID('TR_BillingEntries_Reversals')) AS [Value]").SingleAsync();
    }

    private async Task<string> AllocationTriggerAsync()
    {
        await using var db = Fixture.CreateContext();
        return await db.Database.SqlQuery<string>($"SELECT OBJECT_DEFINITION(OBJECT_ID('TR_BillingAllocations_Links')) AS [Value]").SingleAsync();
    }

    [Fact]
    public async Task Rolling_back_keeps_the_ledger_restores_the_old_allocation_trigger_and_rolling_forward_restores_the_refund_rules()
    {
        var plan = await BillablePlanAsync();
        var account = await NewAccountAsync();
        var charge = ChargeRow(account, plan.Completed, 100m, 1, "c-1");
        var payment = PaymentRow(account, -60m, 2, "p-2");
        await AddRowAsync(charge);
        await AddRowAsync(payment);
        await AddRowAsync(AllocationRow(account, payment, charge, 40m, 1, "a-1"));
        string[] tables = ["BillingAllocationReleases", "FinancialDocuments"];
        string[] triggers = ["TR_BillingEntries_Refunds", "TR_BillingAllocationReleases_Immutable", "TR_BillingAllocationReleases_Links", "TR_FinancialDocuments_Immutable", "TR_FinancialDocuments_Links"];
        foreach (var t in tables) Assert.Equal(1, await CountAsync("tables", t));
        foreach (var t in triggers) Assert.Equal(1, await CountAsync("triggers", t));
        Assert.Contains("BillingAllocationReleases", await AllocationTriggerAsync());
        Assert.Contains("BillingAllocationReleases", await ReversalTriggerAsync());

        await using (var db = Fixture.CreateContext()) await db.GetInfrastructure().GetRequiredService<IMigrator>().MigrateAsync(Before);
        foreach (var t in tables) Assert.Equal(0, await CountAsync("tables", t));
        foreach (var t in triggers) Assert.Equal(0, await CountAsync("triggers", t));
        Assert.False(await ColumnExistsAsync("BillingEntries", "RefundsEntryId"));
        Assert.False(await ColumnExistsAsync("BillingEntries", "ReplacesEntryId"));
        Assert.DoesNotContain("BillingAllocationReleases", await AllocationTriggerAsync());                  // the ALV-007-C01 trigger is back
        Assert.DoesNotContain("BillingAllocationReleases", await ReversalTriggerAsync());                    // and so is the ALV-007-C01 reversal trigger
        await using (var db = Fixture.CreateContext())
        {
            Assert.Equal((2, 1), (await db.BillingEntries.CountAsync(), await db.BillingAllocations.CountAsync()));   // nothing of the ledger was lost
            db.BillingAllocations.Add(AllocationRow(account, payment, charge, 20m, 2, "a-2"));
            await db.SaveChangesAsync();                                                                    // and it still takes an application
        }

        await using (var db = Fixture.CreateContext()) await db.Database.MigrateAsync();
        foreach (var t in tables) Assert.Equal(1, await CountAsync("tables", t));
        foreach (var t in triggers) Assert.Equal(1, await CountAsync("triggers", t));
        Assert.True(await ColumnExistsAsync("BillingEntries", "RefundsEntryId"));
        Assert.Contains("BillingAllocationReleases", await AllocationTriggerAsync());
        Assert.Contains("BillingAllocationReleases", await ReversalTriggerAsync());

        var cash = await Fixture.CreateContext().BillingPaymentMethods.AsNoTracking().Where(m => m.IsBuiltIn && m.Kind == BillingPaymentMethods.Cash).Select(m => m.Id).SingleAsync();
        var refund = NewEntry(account, BillingKinds.Refund, 60.01m, 3, "r-3");
        (refund.RefundsEntryId, refund.PaymentMethod, refund.PaymentMethodId, refund.Reason) = (payment.Id, BillingPaymentMethods.Cash, cash, "Returned");
        var e = await SqlRefusal(() => AddRowAsync(refund));
        Assert.Equal(51158, e.Number);                                                                      // the restored tables refuse a refund for more than the payment
    }
}
