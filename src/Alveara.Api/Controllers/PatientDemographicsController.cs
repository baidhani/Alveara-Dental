using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Alveara.Api.Architecture.Identity;
using Alveara.Api.Architecture.Patients;
using Alveara.Api.Data;

namespace Alveara.Api.Controllers;

/// <summary>
/// ALV-N025 (F-032, F-033): the demographic value lists for the patient form (anyone who may read patients) and the staff review list of stored values that are not on
/// their list (<see cref="Permission.EditPatients"/>, the people who can correct them). Both are reads; nothing here changes a patient.
/// </summary>
[ApiController]
[Route("api/patients")]
[Authorize]
public class PatientDemographicsController(AlveraDbContext db) : ControllerBase
{
    [HttpGet("demographic-options")]
    [RequirePermission(Permission.ViewPatientRecords)]
    public async Task<IActionResult> Options(CancellationToken ct) => Ok(await new PatientDemographicsReader(db).OptionsAsync(ct));

    [HttpGet("demographic-review")]
    [RequirePermission(Permission.EditPatients)]
    public async Task<IActionResult> Review(CancellationToken ct) => Ok(await new PatientDemographicsReader(db).ReviewAsync(ct));
}
