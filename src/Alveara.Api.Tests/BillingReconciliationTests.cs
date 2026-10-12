using System.Globalization;
using Alveara.Api.Architecture.Billing;
using Alveara.Api.Architecture.Identity;
using Xunit;

namespace Alveara.Api.Tests;

/// <summary>
/// ALV-007-C02 Gate D1: cent-exact reconciliation through the real service and SQL Server. A seeded walk of more than sixty real transactions (payments with and without named charges, applications, refunds,
/// the releases they cause, moves, corrections of payments and adjustments, adjustments and reversals of payments, adjustments and charges) is kept against an independent model that knows only the plain meaning of
/// each operation. After every transaction the balance equals the model to the cent, balance = open charges - unapplied payments + net adjustments, each payment's refunded + applied + unapplied equals what it
/// was, each charge's paid + open equals what it was, and the preserved history verifies clean. Statements read at intervals agree with the account, and a statement issued mid-walk reprints byte for byte
/// at the end whatever happened after it.
/// </summary>
public class BillingReconciliationTests : CompletionTestBase
{
    private ProviderProfile _provider = null!;
    private int _step;

    public override async Task InitializeAsync()
    {
        await base.InitializeAsync();
        _provider = await ProviderAsync();
    }

    private static decimal Cents(int cents) => cents / 100m;
    private string Key(string kind) => $"{kind}-{++_step}";

    private sealed class Model { public decimal Balance; public decimal AdjustmentNet; public int Transactions; public Dictionary<string, int> Counts = []; public void Did(string kind) { Counts[kind] = Counts.GetValueOrDefault(kind) + 1; Transactions++; } }

    private static BillingEntryStanding St(BillingAccountView a, int n) => a.Standing!.First(s => s.EntryNumber == n);
    private static IEnumerable<BillingEntryView> Live(BillingAccountView a, string kind) => a.Entries.Where(e => e.Kind == kind && !St(a, e.EntryNumber).IsReversed);

    private async Task<BillingAccountView> StepAsync(string kind, Model model, Func<Task<BillingAccountView>> action)
    {
        var after = await action();
        model.Did(kind);
        return after;
    }

    private async Task AssertReconciledAsync(BillingAccountView a, Model model, string context)
    {
        Assert.True(model.Balance == a.BalanceOwed, $"{context}: balance {a.BalanceOwed} differs from the model {model.Balance}");
        Assert.True(a.BalanceOwed == a.OpenChargeTotal - a.UnappliedTotal + model.AdjustmentNet, $"{context}: balance != open - unapplied + adjustments ({a.OpenChargeTotal} - {a.UnappliedTotal} + {model.AdjustmentNet})");
        foreach (var p in Live(a, BillingKinds.Payment))
        {
            var s = St(a, p.EntryNumber);
            Assert.True(s.RefundedAmount + s.AppliedAmount + s.UnappliedAmount == -p.Amount, $"{context}: payment {p.EntryNumber} parts do not add to {-p.Amount}");
            Assert.True(s.RefundableAmount == -p.Amount - s.RefundedAmount, $"{context}: payment {p.EntryNumber} refundable is wrong");
            Assert.True(s.UnappliedAmount >= 0 && s.AppliedAmount >= 0, $"{context}: payment {p.EntryNumber} has a negative part");
        }
        foreach (var c in Live(a, BillingKinds.Charge))
        {
            var s = St(a, c.EntryNumber);
            Assert.True(s.PaidAmount + s.OpenAmount == c.Amount, $"{context}: charge {c.EntryNumber} paid + open != {c.Amount}");
        }
        var paidByLivePayments = a.Allocations!.Where(x => !x.Released).Aggregate(0m, (sum, x) => sum + x.Amount - (x.ReleasedAmount ?? 0m));
        var paidOnLiveCharges = Live(a, BillingKinds.Charge).Aggregate(0m, (sum, c) => sum + (St(a, c.EntryNumber).PaidAmount ?? 0m));
        Assert.True(paidByLivePayments == paidOnLiveCharges, $"{context}: what payments applied ({paidByLivePayments}) != what charges were paid ({paidOnLiveCharges})");
        Assert.All(a.Allocations!, x => Assert.InRange(x.ReleasedAmount ?? 0m, 0m, x.Amount));
        var check = await Bill(s => s.VerifyLedgerAsync(Ann, default));
        Assert.True(check.Consistent, $"{context}: {string.Join("; ", check.Findings)}");
        Assert.Equal(a.BalanceOwed, check.BalanceOwed);
    }

    private async Task AssertStatementAgreesAsync(BillingAccountView a, string context)
    {
        var preview = await Bill(s => s.PreviewDocumentAsync(Ann, new DocumentPreviewInput("Statement", null), default));
        var s = preview.Statement!;
        string F(decimal v) => v.ToString("F2", CultureInfo.InvariantCulture);
        Assert.Equal((F(a.BalanceOwed), F(a.OpenChargeTotal), F(a.UnappliedTotal)), (s.Balance, s.OpenChargeTotal, s.UnappliedTotal));
        Assert.Equal(a.Entries.Count, s.Lines.Count);
        var buckets = new[] { s.Aging.Current, s.Aging.Days31To60, s.Aging.Days61To90, s.Aging.Over90 }.Sum(x => decimal.Parse(x, CultureInfo.InvariantCulture));
        Assert.True(buckets == a.OpenChargeTotal, $"{context}: aging buckets {buckets} != open charges {a.OpenChargeTotal}");
        Assert.Equal(a.Entries.Select(e => F(e.BalanceAfter)), s.Lines.Select(l => l.BalanceAfter));
    }

    [Fact]
    public async Task More_than_sixty_real_transactions_reconcile_to_the_cent_after_every_step_and_an_issued_statement_reprints_unchanged()
    {
        var plan = await AcceptedPlanAsync(6);
        var performed = DateTimeOffset.UtcNow.AddSeconds(2);
        await Completion(s => s.CompleteAsync(plan.Id, RequestFor(plan, Enumerable.Range(0, 6), AnnEncounter, _provider.Id, "set-1", performed), S.Actor, default));
        var a = await Bill(s => s.GetAccountAsync(Ann, default));
        var model = new Model { Balance = 600m };
        Assert.Equal(600m, a.BalanceOwed);
        await AssertReconciledAsync(a, model, "start");

        var rng = new Random(20261012);
        IssuedDocumentView? mid = null;
        string? midHash = null;
        var guard = 0;
        while (model.Transactions < 70 && ++guard < 500)
        {
            var ops = new List<string>();
            var balance = a.BalanceOwed;
            var payments = Live(a, BillingKinds.Payment).ToList();
            var refundable = payments.Where(p => (St(a, p.EntryNumber).RefundableAmount ?? 0m) > 0m).ToList();
            var applyable = payments.Where(p => (St(a, p.EntryNumber).UnappliedAmount ?? 0m) > 0m).ToList();
            var movable = payments.Where(p => (St(a, p.EntryNumber).AppliedAmount ?? 0m) > 0m).ToList();
            var correctable = payments.Where(p => (St(a, p.EntryNumber).RefundedAmount ?? 0m) == 0m).ToList();
            var reversiblePayments = correctable;
            var adjustments = Live(a, BillingKinds.Adjustment).ToList();
            var freeCharges = Live(a, BillingKinds.Charge).Where(c => St(a, c.EntryNumber).PaidAmount == 0m && balance >= c.Amount).ToList();
            if (balance > 0m) { ops.Add("pay"); ops.Add("pay"); ops.Add("adjust"); ops.Add("adjust"); ops.Add("adjust"); }
            if (balance == 0m) ops.Add("adjust-up");
            if (refundable.Count > 0) { ops.Add("refund"); ops.Add("refund"); }
            if (applyable.Count > 0 && a.OpenChargeTotal > 0m) ops.Add("apply");
            if (movable.Count > 0 && Live(a, BillingKinds.Charge).Count(c => (St(a, c.EntryNumber).OpenAmount ?? 0m) > 0m) > 0) { ops.Add("move"); ops.Add("move"); }
            if (correctable.Count > 0) ops.Add("correct-payment");
            if (adjustments.Any(x => balance - x.Amount >= 0m)) { ops.Add("correct-adjustment"); ops.Add("correct-adjustment"); ops.Add("reverse-adjustment"); ops.Add("reverse-adjustment"); ops.Add("reverse-adjustment"); }
            if (reversiblePayments.Count > 0) ops.Add("reverse-payment");
            if (freeCharges.Count > 0) ops.Add("reverse-charge");
            if (ops.Count == 0) { ops.Add("adjust-up"); }
            var op = _step < 3 ? "pay" : _step == 3 ? "refund" : ops[rng.Next(ops.Count)];
            if (op == "refund" && refundable.Count == 0) op = "pay";

            switch (op)
            {
                case "pay":
                {
                    var cents = rng.Next(1, Math.Min((int)(balance * 100m), 25000) + 1);
                    var amount = Cents(cents);
                    List<AllocationInput>? named = null;
                    var open = Live(a, BillingKinds.Charge).Where(c => (St(a, c.EntryNumber).OpenAmount ?? 0m) > 0m).ToList();
                    if (open.Count > 0 && rng.Next(3) == 0)
                    {
                        var target = open[rng.Next(open.Count)];
                        var room = (int)(St(a, target.EntryNumber).OpenAmount!.Value * 100m);
                        named = [new AllocationInput(target.EntryNumber, Cents(Math.Min(room, cents)))];
                    }
                    a = await StepAsync(op, model, () => Bill(s => s.RecordPaymentAsync(Ann, new PaymentInput(amount, "Cash", null, a.RowVersion, Key("pay"), named), S.Actor, default)));
                    model.Balance -= amount;
                    break;
                }
                case "adjust":
                case "adjust-up":
                {
                    var up = op == "adjust-up" || rng.Next(2) == 0;
                    var cents = up ? rng.Next(100, 3000) : rng.Next(100, Math.Min((int)(balance * 100m), 3000) + 1);
                    var amount = up ? Cents(cents) : -Cents(cents);
                    a = await StepAsync("adjust", model, () => Bill(s => s.RecordAdjustmentAsync(Ann, new AdjustmentInput(amount, "Seeded adjustment", a.RowVersion, Key("adj")), S.Actor, default)));
                    model.Balance += amount; model.AdjustmentNet += amount;
                    break;
                }
                case "refund":
                {
                    var p = refundable[rng.Next(refundable.Count)];
                    var cents = rng.Next(1, (int)(St(a, p.EntryNumber).RefundableAmount!.Value * 100m) + 1);
                    var amount = Cents(cents);
                    a = await StepAsync(op, model, () => Bill(s => s.RefundPaymentAsync(Ann, new RefundInput(p.EntryNumber, amount, "Cash", "Seeded refund", a.RowVersion, Key("rf")), S.Actor, default)));
                    model.Balance += amount;
                    break;
                }
                case "apply":
                {
                    var p = applyable[rng.Next(applyable.Count)];
                    a = await StepAsync(op, model, () => Bill(s => s.ApplyPaymentAsync(Ann, new ApplyPaymentInput(p.EntryNumber, null, a.RowVersion, Key("ap")), S.Actor, default)));
                    break;
                }
                case "move":
                {
                    var p = movable[rng.Next(movable.Count)];
                    var net = a.Allocations!.Where(x => x.PaymentEntryNumber == p.EntryNumber && !x.Released).GroupBy(x => x.ChargeEntryNumber)
                        .Select(g => (Charge: g.Key, Net: g.Sum(x => x.Amount - (x.ReleasedAmount ?? 0m)))).Where(x => x.Net > 0m).ToList();
                    var targets = Live(a, BillingKinds.Charge).Where(c => (St(a, c.EntryNumber).OpenAmount ?? 0m) > 0m).ToList();
                    var from = net[rng.Next(net.Count)];
                    var to = targets.Where(t => t.EntryNumber != from.Charge).ToList();
                    if (to.Count == 0) { model.Counts["skipped-move"] = model.Counts.GetValueOrDefault("skipped-move") + 1; break; }
                    var target = to[rng.Next(to.Count)];
                    var limit = Math.Min((int)(from.Net * 100m), (int)(St(a, target.EntryNumber).OpenAmount!.Value * 100m));
                    var amount = Cents(rng.Next(1, limit + 1));
                    a = await StepAsync(op, model, () => Bill(s => s.MoveAllocationsAsync(Ann, new MoveAllocationInput(p.EntryNumber, [new(from.Charge, amount)], [new(target.EntryNumber, amount)], "Seeded move", a.RowVersion, Key("mv")), S.Actor, default)));
                    break;
                }
                case "correct-payment":
                {
                    var p = correctable[rng.Next(correctable.Count)];
                    var cents = rng.Next(1, Math.Min((int)((balance - p.Amount) * 100m), 25000) + 1);
                    var amount = Cents(cents);
                    a = await StepAsync(op, model, () => Bill(s => s.CorrectEntryAsync(Ann, new CorrectionInput(p.EntryNumber, "Seeded correction", amount, "Cash", null, null, a.RowVersion, Key("cp")), S.Actor, default)));
                    model.Balance += -p.Amount - amount;
                    break;
                }
                case "correct-adjustment":
                {
                    var x = adjustments.Where(y => balance - y.Amount >= 0m).ToList()[0];
                    var amount = Cents(rng.Next(100, 2500));
                    a = await StepAsync(op, model, () => Bill(s => s.CorrectEntryAsync(Ann, new CorrectionInput(x.EntryNumber, "Seeded correction", amount, null, null, null, a.RowVersion, Key("ca")), S.Actor, default)));
                    model.Balance += -x.Amount + amount; model.AdjustmentNet += -x.Amount + amount;
                    break;
                }
                case "reverse-payment":
                {
                    var p = reversiblePayments[rng.Next(reversiblePayments.Count)];
                    a = await StepAsync(op, model, () => Bill(s => s.ReverseEntryAsync(Ann, new ReversalInput(p.EntryNumber, "Seeded reversal", a.RowVersion, Key("rp")), S.Actor, default)));
                    model.Balance += -p.Amount;
                    break;
                }
                case "reverse-adjustment":
                {
                    var x = adjustments.Where(y => balance - y.Amount >= 0m).ToList()[0];
                    a = await StepAsync(op, model, () => Bill(s => s.ReverseEntryAsync(Ann, new ReversalInput(x.EntryNumber, "Seeded reversal", a.RowVersion, Key("ra")), S.Actor, default)));
                    model.Balance -= x.Amount; model.AdjustmentNet -= x.Amount;
                    break;
                }
                case "reverse-charge":
                {
                    var c = freeCharges[rng.Next(freeCharges.Count)];
                    a = await StepAsync(op, model, () => Bill(s => s.ReverseEntryAsync(Ann, new ReversalInput(c.EntryNumber, "Seeded reversal", a.RowVersion, Key("rc")), S.Actor, default)));
                    model.Balance -= c.Amount;
                    break;
                }
            }
            await AssertReconciledAsync(a, model, $"after transaction {model.Transactions} ({op})");
            if (model.Transactions % 12 == 0 && model.Counts.Count > 0) await AssertStatementAgreesAsync(a, $"statement at transaction {model.Transactions}");
            if (mid is null && model.Transactions >= 30)
            {
                var preview = await Bill(s => s.PreviewDocumentAsync(Ann, new DocumentPreviewInput("Statement", null), default));
                midHash = preview.ContentHash;
                mid = await Bill(s => s.IssueDocumentAsync(Ann, new IssueDocumentInput(preview.PreviewToken, "mid-statement"), S.Actor, default));
            }
        }

        Assert.True(model.Transactions >= 60, $"only {model.Transactions} transactions ran");
        foreach (var kind in new[] { "pay", "refund", "move", "correct-payment", "correct-adjustment", "reverse-payment", "reverse-adjustment", "adjust" })
            Assert.True(model.Counts.GetValueOrDefault(kind) >= 2, $"{kind} ran {model.Counts.GetValueOrDefault(kind)} times: {string.Join(", ", model.Counts.Select(c => $"{c.Key}={c.Value}"))}");
        Assert.True(a.Allocations!.Count(x => (x.ReleasedAmount ?? 0m) > 0m) >= 3, "too few releases were exercised");

        await AssertStatementAgreesAsync(a, "end");
        var reprint = await Bill(s => s.GetDocumentAsync(Ann, mid!.Id, S.Actor, default));                          // issued mid-walk, rebuilt at the end from the same cut-offs
        Assert.Equal(midHash, FinancialDocumentBuilder.HashOf(reprint.Statement!));
        Assert.True(reprint.CurrentStatus.Count > 0);
        foreach (var p in Live(a, BillingKinds.Payment).Take(5))                                           // a receipt agrees with the payment's own standing
        {
            var r = (await Bill(s => s.PreviewDocumentAsync(Ann, new DocumentPreviewInput("Receipt", p.EntryNumber), default))).Receipt!;
            var st = St(a, p.EntryNumber);
            Assert.Equal((st.UnappliedAmount!.Value.ToString("F2", CultureInfo.InvariantCulture), st.RefundedAmount!.Value.ToString("F2", CultureInfo.InvariantCulture)), (r.Unapplied, r.RefundedToDate));
        }
        var timeline = await Bill(s => s.GetTimelineAsync(Ann, default));
        var releases = await WithDb(db => Task.FromResult(db.BillingAllocationReleases.Count()));
        Assert.Equal(a.Entries.Count + a.Allocations.Count + releases + 1, timeline.Count);                 // every entry, application, release and the one issued document
    }
}
