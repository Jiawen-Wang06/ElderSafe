using ElderSafe.Api.Alerting;
using ElderSafe.Api.Security;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace ElderSafe.Api.Controllers;

/// <summary>
/// Lists and acknowledges alerts.
/// </summary>
/// <remarks>
/// The acting caregiver comes from the signed-in principal and never from the
/// request, so the audit log records what happened rather than what a caller
/// claimed (REQ-SEC-02).
/// </remarks>
[ApiController]
[Authorize]
[Route("api")]
public sealed class AlertsController : ControllerBase
{
    private readonly AlertService _alertService;
    private readonly AlertTraceService _traceService;
    private readonly AlertSummaryService _summaryService;
    private readonly ICurrentCaregiver _currentCaregiver;

    public AlertsController(
        AlertService alertService,
        AlertTraceService traceService,
        AlertSummaryService summaryService,
        ICurrentCaregiver currentCaregiver)
    {
        _alertService = alertService;
        _traceService = traceService;
        _summaryService = summaryService;
        _currentCaregiver = currentCaregiver;
    }

    [HttpGet("residents/{residentId:guid}/alerts")]
    public async Task<ActionResult<IReadOnlyList<AlertView>>> GetForResident(
        Guid residentId,
        [FromQuery] bool openOnly = true,
        CancellationToken cancellationToken = default)
    {
        var caregiverId = _currentCaregiver.Id;

        if (caregiverId is null)
        {
            return Unauthorized();
        }

        var alerts = await _alertService.GetForResidentAsync(
            caregiverId.Value, residentId, openOnly, cancellationToken);

        // Null means either no such resident or not one of theirs. Answering
        // 404 for both means the API never confirms that a resident exists to
        // someone with no business knowing (RISK-12).
        if (alerts is null)
        {
            return NotFound();
        }

        return Ok(alerts);
    }

    /// <summary>
    /// Alert counts for a resident over a date range (REQ-FALL-09).
    /// </summary>
    /// <remarks>
    /// Range validity is checked here, before the resident is looked up, so
    /// a malformed range gets a 400 that says what is wrong rather than being
    /// folded into the 404 that authorization failures use. The two refusals
    /// mean different things to a caller: a 400 is something the caller can
    /// fix by asking differently, a 404 is not.
    /// </remarks>
    [HttpGet("residents/{residentId:guid}/alerts/summary")]
    public async Task<ActionResult<AlertSummary>> GetSummary(
        Guid residentId,
        [FromQuery] DateTimeOffset from,
        [FromQuery] DateTimeOffset to,
        CancellationToken cancellationToken)
    {
        var caregiverId = _currentCaregiver.Id;

        if (caregiverId is null)
        {
            return Unauthorized();
        }

        if (from >= to)
        {
            return BadRequest("'from' must be earlier than 'to'.");
        }

        if (to - from > AlertSummaryService.MaxRange)
        {
            return BadRequest($"The range cannot exceed {AlertSummaryService.MaxRange.TotalDays:0} days.");
        }

        var summary = await _summaryService.GetAsync(
            caregiverId.Value, residentId, from, to, cancellationToken);

        if (summary is null)
        {
            return NotFound();
        }

        return Ok(summary);
    }

    /// <summary>
    /// The sensor data behind one alert, with the thresholds applied to it
    /// (REQ-FALL-08).
    /// </summary>
    [HttpGet("alerts/{alertId:guid}/trace")]
    public async Task<ActionResult<AlertTrace>> GetTrace(
        Guid alertId,
        CancellationToken cancellationToken)
    {
        var caregiverId = _currentCaregiver.Id;

        if (caregiverId is null)
        {
            return Unauthorized();
        }

        var trace = await _traceService.GetAsync(caregiverId.Value, alertId, cancellationToken);

        if (trace is null)
        {
            return NotFound();
        }

        return Ok(trace);
    }

    [HttpPost("alerts/{alertId:guid}/acknowledge")]
    public async Task<IActionResult> Acknowledge(
        Guid alertId,
        CancellationToken cancellationToken)
    {
        var caregiverId = _currentCaregiver.Id;

        if (caregiverId is null)
        {
            return Unauthorized();
        }

        var acknowledged = await _alertService.AcknowledgeAsync(
            alertId, caregiverId.Value, cancellationToken);

        if (!acknowledged)
        {
            return NotFound();
        }

        return NoContent();
    }
}
