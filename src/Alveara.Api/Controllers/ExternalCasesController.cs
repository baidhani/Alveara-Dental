using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Alveara.Api.Architecture.ExternalCases;
using Alveara.Api.Architecture.Identity;
using Alveara.Api.Architecture.Permissions;

namespace Alveara.Api.Controllers;

/// <summary>
/// ALV-009-C02: referral and dental-laboratory cases and the outside parties they go to. Reading cases, the party list and a patient's timeline needs <see cref="Permission.ViewExternalCases"/> (a case's linked documents
/// also need <see cref="Permission.ViewDocuments"/>, since they are the documents themselves); creating, changing, moving and linking needs <see cref="Permission.ManageExternalCases"/> (linking also needs ViewDocuments);
/// the party list is maintained with <see cref="Permission.ManageExternalParties"/>. Writes need a CSRF token; a create needs an <c>Idempotency-Key</c>; every change carries the <c>rowVersion</c> the screen showed (a stale
/// one is 409 and nothing is stored). Refusals carry a stable <c>error</c> code (see <see cref="ExternalCaseException"/>). Cases are addressed as <c>referrals</c> or <c>lab-cases</c>.
/// </summary>
[ApiController]
[Authorize]
public class ExternalCasesController(ExternalCaseService cases, ExternalPartyService parties, IEffectivePermissions permissions) : ControllerBase
{
    private const string KindRoute = "{route:regex(^(referrals|lab-cases)$)}";

    private Guid Actor => Guid.Parse(User.FindFirstValue(ClaimTypes.NameIdentifier)!);
    private static string KindOf(string route) => route == "referrals" ? ExternalCaseKinds.Referral : ExternalCaseKinds.Lab;

    private async Task<IActionResult> Run(Func<Task<object>> action)
    {
        try { return Ok(await action()); }
        catch (ExternalCaseException ex) { return StatusCode(ex.StatusCode, new { error = ex.Code, message = ex.Message }); }
    }

    /// <summary>The second permission an action needs besides the one on its attribute; null when held.</summary>
    private async Task<IActionResult?> NeedsAsync(Permission permission, CancellationToken ct)
    {
        Enum.TryParse<Role>(User.FindFirstValue(ClaimTypes.Role), out var role);
        return PermissionResults.Refusal(await permissions.CheckAsync(role, permission, ct), permission.ToString());
    }

    // ---------- outside parties ----------

    [HttpGet("api/external-parties")]
    [RequirePermission(Permission.ViewExternalCases)]
    public Task<IActionResult> Parties([FromQuery] string? kind, [FromQuery] bool? includeInactive, CancellationToken ct) => Run(async () => await parties.ListAsync(kind, includeInactive == true, ct));

    [HttpPost("api/external-parties")]
    [RequirePermission(Permission.ManageExternalParties)]
    [RequireCsrfToken]
    public Task<IActionResult> CreateParty([FromBody] CreatePartyRequest request, CancellationToken ct) => Run(async () => await parties.CreateAsync(Actor, request, ct));

    [HttpPut("api/external-parties/{id:guid}")]
    [RequirePermission(Permission.ManageExternalParties)]
    [RequireCsrfToken]
    public Task<IActionResult> UpdateParty(Guid id, [FromBody] UpdatePartyRequest request, CancellationToken ct) => Run(async () => await parties.UpdateAsync(Actor, id, request, ct));

    [HttpPost("api/external-parties/{id:guid}/active")]
    [RequirePermission(Permission.ManageExternalParties)]
    [RequireCsrfToken]
    public Task<IActionResult> SetPartyActive(Guid id, [FromBody] SetPartyActiveRequest request, CancellationToken ct) => Run(async () => await parties.SetActiveAsync(Actor, id, request, ct));

    // ---------- lists and timelines ----------

    [HttpGet("api/external-cases")]
    [RequirePermission(Permission.ViewExternalCases)]
    public Task<IActionResult> List([FromQuery] string? kind, [FromQuery] string? status, [FromQuery] bool? overdue, [FromQuery] Guid? patientId, [FromQuery] Guid? partyId, [FromQuery] int? page, [FromQuery] int? pageSize, CancellationToken ct) =>
        Run(async () => await cases.ListAsync(new ExternalCaseQuery(kind, status, overdue, patientId, partyId, page, pageSize), ct));

    [HttpGet("api/patients/{patientId:guid}/external-cases/timeline")]
    [RequirePermission(Permission.ViewExternalCases)]
    public Task<IActionResult> Timeline(Guid patientId, CancellationToken ct) => Run(async () => await cases.PatientTimelineAsync(patientId, ct));

    // ---------- creating ----------

    [HttpPost("api/patients/{patientId:guid}/referrals")]
    [RequirePermission(Permission.ManageExternalCases)]
    [RequireCsrfToken]
    public Task<IActionResult> CreateReferral(Guid patientId, [FromBody] CreateReferralRequest request, [FromHeader(Name = "Idempotency-Key")] string? key, CancellationToken ct) =>
        Run(async () => await cases.CreateReferralAsync(Actor, patientId, request, key, ct));

    [HttpPost("api/patients/{patientId:guid}/lab-cases")]
    [RequirePermission(Permission.ManageExternalCases)]
    [RequireCsrfToken]
    public Task<IActionResult> CreateLab(Guid patientId, [FromBody] CreateLabRequest request, [FromHeader(Name = "Idempotency-Key")] string? key, CancellationToken ct) =>
        Run(async () => await cases.CreateLabAsync(Actor, patientId, request, key, ct));

    // ---------- one case ----------

    [HttpGet($"api/{KindRoute}/{{id:guid}}")]
    [RequirePermission(Permission.ViewExternalCases)]
    public Task<IActionResult> Get(string route, Guid id, CancellationToken ct) => Run(async () => await cases.GetAsync(KindOf(route), id, ct));

    [HttpGet($"api/{KindRoute}/{{id:guid}}/history")]
    [RequirePermission(Permission.ViewExternalCases)]
    public Task<IActionResult> History(string route, Guid id, CancellationToken ct) => Run(async () => await cases.EventsAsync(KindOf(route), id, ct));

    [HttpPut("api/referrals/{id:guid}")]
    [RequirePermission(Permission.ManageExternalCases)]
    [RequireCsrfToken]
    public Task<IActionResult> UpdateReferral(Guid id, [FromBody] UpdateReferralRequest request, CancellationToken ct) => Run(async () => await cases.UpdateReferralAsync(Actor, id, request, ct));

    [HttpPut("api/lab-cases/{id:guid}")]
    [RequirePermission(Permission.ManageExternalCases)]
    [RequireCsrfToken]
    public Task<IActionResult> UpdateLab(Guid id, [FromBody] UpdateLabRequest request, CancellationToken ct) => Run(async () => await cases.UpdateLabAsync(Actor, id, request, ct));

    [HttpPost($"api/{KindRoute}/{{id:guid}}/status")]
    [RequirePermission(Permission.ManageExternalCases)]
    [RequireCsrfToken]
    public Task<IActionResult> Status(string route, Guid id, [FromBody] ChangeStatusRequest request, CancellationToken ct) => Run(async () => await cases.ChangeStatusAsync(Actor, KindOf(route), id, request, ct));

    // ---------- documents (linked, never copied) ----------

    [HttpGet($"api/{KindRoute}/{{id:guid}}/documents")]
    [RequirePermission(Permission.ViewExternalCases)]
    public async Task<IActionResult> Documents(string route, Guid id, CancellationToken ct) =>
        await NeedsAsync(Permission.ViewDocuments, ct) ?? await Run(async () => await cases.DocumentsAsync(KindOf(route), id, ct));

    [HttpPut($"api/{KindRoute}/{{id:guid}}/documents/{{documentId:guid}}")]
    [RequirePermission(Permission.ManageExternalCases)]
    [RequireCsrfToken]
    public async Task<IActionResult> LinkDocument(string route, Guid id, Guid documentId, CancellationToken ct) =>
        await NeedsAsync(Permission.ViewDocuments, ct) ?? await Run(async () => await cases.LinkDocumentAsync(Actor, KindOf(route), id, documentId, ct));

    [HttpDelete($"api/{KindRoute}/{{id:guid}}/documents/{{documentId:guid}}")]
    [RequirePermission(Permission.ManageExternalCases)]
    [RequireCsrfToken]
    public async Task<IActionResult> UnlinkDocument(string route, Guid id, Guid documentId, CancellationToken ct) =>
        await NeedsAsync(Permission.ViewDocuments, ct) ?? await Run(async () => await cases.UnlinkDocumentAsync(Actor, KindOf(route), id, documentId, ct));
}
