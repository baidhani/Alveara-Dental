using Microsoft.EntityFrameworkCore;
using Alveara.Api.Architecture.Odontogram;
using Alveara.Api.Data;

namespace Alveara.Api.Architecture.Procedures;

/// <summary>
/// ALV-N005: a module that stores a reference to a procedure (a finding's link, later a treatment plan item, a completed procedure, a charge) reports how many such references there are, so the
/// catalog can warn before a procedure is inactivated. A new consumer adds one implementation and registers it; the catalog does not change. Counting must be read-only and must not
/// expose any patient detail: only a number per source leaves it.
/// </summary>
public interface IProcedureUsageSource
{
    /// <summary>A short, stable name shown in the warning (for example "Odontogram links").</summary>
    string Name { get; }

    Task<int> CountAsync(Guid procedureId, CancellationToken ct);
}

/// <summary>Odontogram findings can already be linked to a procedure by reference (ALV-006-C01); a link whose reference is the procedure's id counts as a use.</summary>
public sealed class FindingLinkProcedureUsageSource(AlveraDbContext db) : IProcedureUsageSource
{
    public string Name => "Odontogram links";

    public Task<int> CountAsync(Guid procedureId, CancellationToken ct)
    {
        var reference = procedureId.ToString();
        return db.ToothFindingLinks.AsNoTracking().CountAsync(l => l.LinkType == LinkTypes.Procedure && l.Reference == reference, ct);
    }
}
