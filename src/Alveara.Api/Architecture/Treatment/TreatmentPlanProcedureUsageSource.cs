using Microsoft.EntityFrameworkCore;
using Alveara.Api.Architecture.Procedures;
using Alveara.Api.Data;

namespace Alveara.Api.Architecture.Treatment;

/// <summary>
/// ALV-015-C01 R02: a treatment plan refers to a catalog procedure (every plan procedure stores the procedure's id and the exact catalog version it was proposed under), so the catalog must count those
/// references before a procedure is inactivated (the contract ALV-N005 built in <see cref="IProcedureUsageSource"/>). Every persisted plan procedure counts, withdrawn and completed ones included, because the
/// reference is historical: a plan keeps showing the procedure and the fee it copied however the catalog changes. Counting is read-only and returns a number only (no patient, plan or procedure detail leaves it).
/// </summary>
public sealed class TreatmentPlanProcedureUsageSource(AlveraDbContext db) : IProcedureUsageSource
{
    public string Name => "Treatment plans";

    public Task<int> CountAsync(Guid procedureId, CancellationToken ct) => db.TreatmentPlanItems.AsNoTracking().CountAsync(i => i.ProcedureId == procedureId, ct);
}
