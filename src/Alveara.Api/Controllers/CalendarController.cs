using System.Globalization;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Alveara.Api.Architecture.Identity;
using Alveara.Api.Architecture.Scheduling;
using Alveara.Api.Architecture.Time;

namespace Alveara.Api.Controllers;

/// <summary>
/// ALV-N021 A2 (F-052, owner decision 2026-11-15): what the calendar draws besides appointments. GET <c>api/calendar/blocked-time?from=yyyy-MM-dd[&amp;to=yyyy-MM-dd][&amp;providerId=]</c>
/// returns the blocked time overlapping those practice-local days (at most 31) with a controlled display label and never the private reason. Needs <see cref="Permission.ViewSchedule"/>,
/// like the calendar itself. A malformed or too long range is 400 <c>invalid_range</c>, exactly like the appointments list.
/// </summary>
[ApiController]
[Route("api/calendar")]
[Authorize]
public class CalendarController(CalendarBlockedTimeReader blocks, IPracticeClock clock) : ControllerBase
{
    private const int MaxRangeDays = 31;

    [HttpGet("blocked-time")]
    [RequirePermission(Permission.ViewSchedule)]
    public async Task<IActionResult> BlockedTime([FromQuery] string? from, [FromQuery] string? to, [FromQuery] Guid? providerId, CancellationToken ct = default)
    {
        if (!DateOnly.TryParseExact(from, "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var fromDay)
            || !DateOnly.TryParseExact(to ?? from, "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var toDay))
            return BadRequest(new { error = "invalid_range", message = "Give from (and optionally to) as yyyy-MM-dd." });
        if (toDay < fromDay || toDay.DayNumber - fromDay.DayNumber >= MaxRangeDays)
            return BadRequest(new { error = "invalid_range", message = $"The range must be 1 to {MaxRangeDays} days, ending on or after it starts." });
        try
        {
            return Ok(await blocks.ListAsync(clock.StartOfPracticeDayUtc(fromDay), clock.StartOfPracticeDayUtc(toDay.AddDays(1)), providerId, ct));
        }
        catch (LocalTimeConversionException ex)
        {
            return BadRequest(new { error = "invalid_range", message = ex.Message });
        }
    }
}
