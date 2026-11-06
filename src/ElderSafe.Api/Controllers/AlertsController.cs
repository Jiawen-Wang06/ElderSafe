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
    private readonly ICurrentCaregiver _currentCaregiver;

    public AlertsController(AlertService alertService, ICurrentCaregiver currentCaregiver)
    {
        _alertService = alertService;
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
