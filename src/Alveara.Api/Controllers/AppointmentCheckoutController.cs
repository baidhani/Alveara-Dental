using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Alveara.Api.Architecture.Identity;
using Alveara.Api.Architecture.Scheduling.Checkout;

namespace Alveara.Api.Controllers;

/// <summary>
/// ALV-N021 A2 (briefing 6.3): a visit's checkout. <c>GET</c> returns every item, whether it applies, its current outcome, the next action and who does it (needs <see cref="Permission.ViewSchedule"/>).
/// <c>POST {item}</c> records one decision (Idempotency-Key and CSRF token required); WHO may record which outcome is decided by <see cref="CheckoutRecorder"/>, which reads the ledger but never writes it.
/// Completion itself is not here: it runs the same <see cref="CheckoutEvaluator"/> inside the completion transaction (A2 step 3).
/// </summary>
[ApiController]
[Route("api/appointments/{id:guid}/checkout")]
[Authorize]
public class AppointmentCheckoutController(CheckoutEvaluator evaluator, CheckoutRecorder recorder) : ControllerBase
{
    private Guid Actor => Guid.Parse(User.FindFirstValue(ClaimTypes.NameIdentifier)!);

    private IActionResult Refusal(CheckoutException ex) => StatusCode(ex.StatusCode, new { error = ex.Code, message = ex.Message, openItems = ex.OpenItems });

    [HttpGet]
    [RequirePermission(Permission.ViewSchedule)]
    public async Task<IActionResult> Get(Guid id, CancellationToken ct)
    {
        try { return Ok(await evaluator.EvaluateAsync(id, ct)); }
        catch (CheckoutException ex) { return Refusal(ex); }
    }

    [HttpPost("{item}")]
    [RequireCsrfToken]
    public async Task<IActionResult> Record(Guid id, string item, [FromBody] RecordCheckoutOutcomeRequest? body, [FromHeader(Name = "Idempotency-Key")] string? idempotencyKey, CancellationToken ct)
    {
        if (!Enum.TryParse<Role>(User.FindFirstValue(ClaimTypes.Role), out var role)) return Unauthorized();
        try { return Ok(await recorder.RecordAsync(id, item, body ?? new RecordCheckoutOutcomeRequest(null, null, null), Actor, role, idempotencyKey, ct)); }
        catch (CheckoutException ex) { return Refusal(ex); }
    }
}
