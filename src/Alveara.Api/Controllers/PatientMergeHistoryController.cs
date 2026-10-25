using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Alveara.Api.Architecture.Concurrency;
using Alveara.Api.Architecture.Identity;
using Alveara.Api.Architecture.Patients;
using Alveara.Api.Architecture.Patients.Merge;

namespace Alveara.Api.Controllers;

/// <summary>
/// ALV-017-C01: where a patient stands in the merge lineage, read from the patient's own page: the record this one was merged into (so a retired record says so and points on), and the records merged into this one.
/// Anyone who may view patient records may read it (<see cref="Permission.ViewPatientRecords"/>): it names the other record and says when, by whom and why, and shows nothing of either chart.
/// </summary>
[ApiController]
[Route("api/patients/{patientId:guid}/merge-history")]
[Authorize]
public class PatientMergeHistoryController(PatientMergeService merges) : ControllerBase
{
    [HttpGet]
    [RequirePermission(Permission.ViewPatientRecords)]
    public async Task<IActionResult> Get(Guid patientId, CancellationToken ct)
    {
        try { return Ok(await merges.HistoryAsync(patientId, ct)); }
        catch (PatientException ex) { return StatusCode(ex.StatusCode, new { error = ex.Code, message = ex.Message }); }
        catch (ConcurrencyConflictException ex) { return Conflict(ex.ToProblem()); }
    }
}
