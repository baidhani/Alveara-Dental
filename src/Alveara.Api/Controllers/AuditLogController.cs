using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Alveara.Api.Architecture.Identity;
using Alveara.Api.Architecture.Permissions;
using Alveara.Api.Data;

namespace Alveara.Api.Controllers;

/// <summary>
/// ALV-N025: the audit-log reads, moved out of <see cref="AuthController"/> (which had passed the 500-line ceiling) with their URLs unchanged
/// (<c>GET /api/auth/audit-log</c>, <c>GET /api/auth/audit-log/event-types</c>). Both need <see cref="Permission.ViewAuditLog"/>.
/// </summary>
[ApiController]
[Route("api/auth")]
public class AuditLogController(AlveraDbContext db) : ControllerBase
{
    /// <summary>STORY-002: audit log visibility, gated on ViewAuditLog so only a system auditor or
    /// admin can read it - "when they do not have permission, then the action is denied" applies to
    /// reading as much as to any attempt to alter it (see AlveraDbContext's SaveChanges guard for
    /// the immutability side of that Trust requirement; there is no update/delete endpoint at all).
    /// Most-recent-first, bounded to at most 500 rows per call so a growing audit trail can never
    /// produce an unbounded response.</summary>
    [HttpGet("audit-log")]
    [Authorize]
    [RequirePermission(Permission.ViewAuditLog)]
    public async Task<IActionResult> GetAuditLog([FromQuery] int take = 100, [FromQuery] string? eventType = null, [FromQuery] DateTimeOffset? fromUtc = null,
        [FromQuery] DateTimeOffset? toUtc = null, CancellationToken cancellationToken = default) =>
        // ALV-N025: names, filters and the opaque History reference come from AuditLogReader (rows stay PHI-free; no patient value is ever returned here)
        Ok(await new Alveara.Api.Architecture.Auditing.AuditLogReader(db).ReadAsync(new(take, eventType, fromUtc, toUtc), cancellationToken));

    /// <summary>ALV-N025 (F-150): the event types present in the audit log, for the page's filter.</summary>
    [HttpGet("audit-log/event-types")]
    [Authorize]
    [RequirePermission(Permission.ViewAuditLog)]
    public async Task<IActionResult> GetAuditLogEventTypes(CancellationToken cancellationToken = default) =>
        Ok(await new Alveara.Api.Architecture.Auditing.AuditLogReader(db).EventTypesAsync(cancellationToken));
}
