using Microsoft.EntityFrameworkCore;
using Alveara.Api.Architecture.Auditing;

namespace Alveara.Api.Architecture.Billing;

/// <summary>
/// ALV-007-C01: the payment methods payments are recorded under, owned by the billing domain (not generic practice configuration). Four are built in and always active (Cash, Check, Card, Other); billing adds named
/// methods that each map to one of those kinds (the kind is what the ledger reports on), and can inactivate a method it no longer uses, which stops it being chosen for new payments but leaves every old payment's
/// method readable. A method's name and kind never change and a method is never deleted. Each change is audited in the same save.
/// </summary>
public partial class BillingService
{
    private static PaymentMethodView ViewOf(BillingPaymentMethod m) => new(m.Id, m.Name, m.Kind, m.IsBuiltIn, m.IsActive, Convert.ToBase64String(m.RowVersion));

    /// <summary>The methods that can be chosen (active ones), built-ins first, or all of them including those no longer used.</summary>
    public async Task<IReadOnlyList<PaymentMethodView>> ListPaymentMethodsAsync(bool includeInactive, CancellationToken ct)
    {
        var methods = await db.BillingPaymentMethods.AsNoTracking().Where(m => includeInactive || m.IsActive).ToListAsync(ct);
        return methods.OrderByDescending(m => m.IsBuiltIn).ThenBy(m => m.Kind == m.Name ? 0 : 1).ThenBy(m => m.Name, StringComparer.OrdinalIgnoreCase).Select(ViewOf).ToList();
    }

    /// <summary>Adds a named method that maps to one of the four kinds. The name is unique whatever its case; adding the same name and kind again returns the existing method.</summary>
    public async Task<PaymentMethodView> AddPaymentMethodAsync(PaymentMethodInput input, Guid actor, CancellationToken ct)
    {
        var errors = new Dictionary<string, string>();
        var name = BillingRules.CleanMethodName(errors, input.Name);
        var kind = BillingPaymentMethods.All.FirstOrDefault(k => string.Equals(k, input.Kind?.Trim(), StringComparison.OrdinalIgnoreCase));
        if (kind is null) errors["kind"] = $"Choose the kind of payment this is: {string.Join(", ", BillingPaymentMethods.All)}.";
        if (errors.Count > 0) throw BillingRules.Invalid(errors);

        var existing = await db.BillingPaymentMethods.AsNoTracking().FirstOrDefaultAsync(m => m.Name == name, ct);                    // the column's collation makes this match whatever the case
        if (existing is not null)
            return existing.Kind == kind ? ViewOf(existing) : throw new BillingException("method_exists", $"A payment method named {existing.Name} already exists (as {existing.Kind}).", 409);

        var method = new BillingPaymentMethod { Id = Guid.NewGuid(), Name = name!, Kind = kind!, IsBuiltIn = false, IsActive = true, CreatedAtUtc = clock.UtcNow, CreatedByUserId = actor };
        db.BillingPaymentMethods.Add(method);
        AuditService.Record(db, "BillingPaymentMethodAdded", nameof(BillingPaymentMethod), method.Id, actor, $"Payment method {name} ({kind}) added.");
        try
        {
            await SaveAsync(method.Id, ct, nameof(BillingPaymentMethod));
        }
        catch (Concurrency.ConcurrencyConflictException)
        {
            // two people added the same name at the same moment (the unique name): the first stands, so this is a repeat of it when it is the same kind
            db.ChangeTracker.Clear();
            var winner = await db.BillingPaymentMethods.AsNoTracking().FirstOrDefaultAsync(m => m.Name == name, ct);
            return winner is not null && winner.Kind == kind ? ViewOf(winner) : throw new BillingException("method_exists", $"A payment method named {name} already exists.", 409);
        }
        return ViewOf(await db.BillingPaymentMethods.AsNoTracking().FirstAsync(m => m.Id == method.Id, ct));
    }

    /// <summary>Inactivates or reactivates a method, with the version the caller read. A built-in method always stays active; setting the state it is already in changes nothing.</summary>
    public async Task<PaymentMethodView> SetPaymentMethodActiveAsync(Guid methodId, bool active, string? rowVersion, Guid actor, CancellationToken ct)
    {
        var version = BillingRules.ParseVersion(rowVersion);
        var method = await db.BillingPaymentMethods.FirstOrDefaultAsync(m => m.Id == methodId, ct) ?? throw new BillingException("method_not_found", "That payment method was not found.", 404);
        if (method.IsActive == active) return ViewOf(method);
        if (!active && method.IsBuiltIn) throw new BillingException("built_in_method", $"{method.Name} is one of the four built-in methods and stays available.", 409);

        db.Entry(method).Property(m => m.RowVersion).OriginalValue = version;
        method.IsActive = active;
        method.UpdatedAtUtc = clock.UtcNow;
        method.UpdatedByUserId = actor;
        AuditService.Record(db, "BillingPaymentMethodChanged", nameof(BillingPaymentMethod), method.Id, actor, $"Payment method {method.Name} {(active ? "reactivated" : "inactivated")}.");
        await SaveAsync(method.Id, ct, nameof(BillingPaymentMethod));
        return ViewOf(await db.BillingPaymentMethods.AsNoTracking().FirstAsync(m => m.Id == method.Id, ct));
    }
}
