using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Alveara.Api.Architecture.Identity;
using Alveara.Api.Architecture.Periodontal;

namespace Alveara.Api.Controllers;

public record SaveEntriesRequest(string? RowVersion, List<PerioReadingRequest?>? Readings, List<PerioToothRequest?>? Teeth, List<PerioSiteRefRequest?>? ClearSites, List<string>? ClearTeeth);
public record CloseSessionRequest(string? RowVersion);
public record LinkChartRequest(string? LinkType, string? Reference);

/// <summary>
/// ALV-012-C01: periodontal chart sessions, finalized charts and comparison. Reading needs <see cref="Permission.ViewClinicalDocumentation"/>; every write needs <see cref="Permission.ManageClinicalNotes"/> plus
/// a CSRF token - the same permissions as STORY-012's charts. Starting a session returns the patient's open draft if there is one (200 either way). Saving entries, finalizing and abandoning echo the
/// session's <c>rowVersion</c>: a stale one is the shared 409 concurrency conflict and nothing is merged. A save is applied together or not at all; a refusal returns <c>problems</c> naming every entry to
/// correct and leaves the draft exactly as it was. Finalizing a finalized session returns its chart (200). A closed session answers <c>session_closed</c> (409).
/// </summary>
[ApiController]
[Authorize]
public class PerioSessionController(PerioSessionService sessions, PerioComparisonService comparison) : PerioControllerBase
{
    /// <summary>The patient's open draft as <c>{ session }</c>, or <c>{ session: null }</c> when none is open.</summary>
    [HttpGet("api/patients/{patientId:guid}/periodontal/session")]
    [RequirePermission(Permission.ViewClinicalDocumentation)]
    public Task<IActionResult> Current(Guid patientId, CancellationToken ct) => Run(async () => Ok(new { session = await sessions.CurrentAsync(patientId, ct) }));

    [HttpPost("api/patients/{patientId:guid}/periodontal/sessions")]
    [RequirePermission(Permission.ManageClinicalNotes)]
    [RequireCsrfToken]
    public Task<IActionResult> Start(Guid patientId, CancellationToken ct) => Run(async () => Ok(await sessions.StartAsync(patientId, Actor, ct)));

    [HttpGet("api/periodontal/sessions/{sessionId:guid}")]
    [RequirePermission(Permission.ViewClinicalDocumentation)]
    public Task<IActionResult> Get(Guid sessionId, CancellationToken ct) => Run(async () => Ok(await sessions.GetAsync(sessionId, ct)));

    [HttpPost("api/periodontal/sessions/{sessionId:guid}/entries")]
    [RequirePermission(Permission.ManageClinicalNotes)]
    [RequireCsrfToken]
    public Task<IActionResult> SaveEntries(Guid sessionId, [FromBody] SaveEntriesRequest request, CancellationToken ct) =>
        Run(async () => Ok(await sessions.SaveEntriesAsync(sessionId, request.RowVersion,
            new PerioEntryBatch(ToInputs(request.Readings), ToTeeth(request.Teeth), ToSiteRefs(request.ClearSites), request.ClearTeeth), Actor, ct)));

    [HttpPost("api/periodontal/sessions/{sessionId:guid}/finalize")]
    [RequirePermission(Permission.ManageClinicalNotes)]
    [RequireCsrfToken]
    public Task<IActionResult> Finalize(Guid sessionId, [FromBody] CloseSessionRequest request, CancellationToken ct) =>
        Run(async () => Ok(await sessions.FinalizeAsync(sessionId, request.RowVersion, Actor, ct)));

    [HttpPost("api/periodontal/sessions/{sessionId:guid}/abandon")]
    [RequirePermission(Permission.ManageClinicalNotes)]
    [RequireCsrfToken]
    public Task<IActionResult> Abandon(Guid sessionId, [FromBody] CloseSessionRequest request, CancellationToken ct) =>
        Run(async () => Ok(await sessions.AbandonAsync(sessionId, request.RowVersion, Actor, ct)));

    [HttpGet("api/periodontal/charts/{examId:guid}")]
    [RequirePermission(Permission.ViewClinicalDocumentation)]
    public Task<IActionResult> Chart(Guid examId, CancellationToken ct) => Run(async () => Ok(await sessions.ExamAsync(examId, ct)));

    [HttpPost("api/periodontal/charts/{examId:guid}/links")]
    [RequirePermission(Permission.ManageClinicalNotes)]
    [RequireCsrfToken]
    public Task<IActionResult> Link(Guid examId, [FromBody] LinkChartRequest request, CancellationToken ct) =>
        Run(async () => Ok(await sessions.LinkAsync(examId, request.LinkType, request.Reference, Actor, ct)));

    /// <summary>Compares a saved chart (<c>currentExamId</c>) or the draft being entered (<c>currentSessionId</c>) with an earlier chart (<c>previousExamId</c>, or by default the latest finalized one before it).</summary>
    [HttpGet("api/patients/{patientId:guid}/periodontal/comparison")]
    [RequirePermission(Permission.ViewClinicalDocumentation)]
    public Task<IActionResult> Compare(Guid patientId, [FromQuery] Guid? currentExamId, [FromQuery] Guid? currentSessionId, [FromQuery] Guid? previousExamId, CancellationToken ct) =>
        Run(async () => Ok(await comparison.CompareAsync(patientId, currentExamId, currentSessionId, previousExamId, ct)));
}
