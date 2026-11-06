using ElderSafe.Api.Alerting;
using Microsoft.AspNetCore.Mvc;

namespace ElderSafe.Api.Controllers;

/// <summary>Who is acknowledging.</summary>
/// <remarks>
/// The caregiver is named in the body only because there is no authentication
/// yet. Once there is, the acting caregiver comes from the signed-in principal
/// and this field disappears: letting a caller name any caregiver makes the
/// audit log worthless as evidence of who responded.
/// </remarks>
public sealed record AcknowledgeRequest(Guid CaregiverId);

/// <summary>
/// Lists and acknowledges alerts.
/// </summary>
/// <remarks>
/// <b>Unauthenticated in this version.</b> REQ-SEC-01 and REQ-SEC-03 are not
/// met until the authentication work lands.
/// </remarks>
[ApiController]
[Route("api")]
public sealed class AlertsController : ControllerBase
{
    private readonly AlertService _alertService;

    public AlertsController(AlertService alertService)
    {
        _alertService = alertService;
    }

    [HttpGet("residents/{residentId:guid}/alerts")]
    public async Task<ActionResult<IReadOnlyList<AlertView>>> GetForResident(
        Guid residentId,
        [FromQuery] bool openOnly = true,
        CancellationToken cancellationToken = default)
    {
        var alerts = await _alertService.GetForResidentAsync(residentId, openOnly, cancellationToken);

        return Ok(alerts);
    }

    [HttpPost("alerts/{alertId:guid}/acknowledge")]
    public async Task<IActionResult> Acknowledge(
        Guid alertId,
        [FromBody] AcknowledgeRequest request,
        CancellationToken cancellationToken)
    {
        var acknowledged = await _alertService.AcknowledgeAsync(
            alertId, request.CaregiverId, cancellationToken);

        if (!acknowledged)
        {
            return NotFound();
        }

        return NoContent();
    }
}
