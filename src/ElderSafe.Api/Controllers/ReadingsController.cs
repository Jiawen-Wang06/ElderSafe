using System.Security.Claims;
using ElderSafe.Api.Alerting;
using ElderSafe.Api.Security;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace ElderSafe.Api.Controllers;

/// <summary>What the ingestion endpoint reports back.</summary>
public sealed record IngestionResponse(int Accepted, bool AlertRaised, Guid? AlertId);

/// <summary>
/// Accepts readings from a paired wearable.
/// </summary>
/// <remarks>
/// The route carries no resident. The resident is read from the device's
/// pairing, so a device cannot post readings for anyone else even if it tries
/// — the opportunity does not exist in the API's shape rather than being
/// checked for and rejected (REQ-SEC-04, RISK-15).
/// </remarks>
[ApiController]
[Route("api/readings")]
[Authorize(AuthenticationSchemes = DeviceAuthenticationHandler.SchemeName)]
public sealed class ReadingsController : ControllerBase
{
    private readonly ReadingIngestionService _ingestion;

    public ReadingsController(ReadingIngestionService ingestion)
    {
        _ingestion = ingestion;
    }

    [HttpPost]
    public async Task<ActionResult<IngestionResponse>> Post(
        [FromBody] IReadOnlyList<IncomingReading> readings,
        CancellationToken cancellationToken)
    {
        if (readings is null || readings.Count == 0)
        {
            return BadRequest("At least one reading is required.");
        }

        var residentClaim = User.FindFirstValue(DeviceAuthenticationHandler.ResidentIdClaimType);

        if (!Guid.TryParse(residentClaim, out var residentId))
        {
            return Unauthorized();
        }

        var outcome = await _ingestion.IngestAsync(residentId, readings, cancellationToken);

        if (!outcome.ResidentFound)
        {
            // The device is paired to a resident that no longer exists. Not
            // the caller's doing, and not something to leak back to it.
            return StatusCode(StatusCodes.Status500InternalServerError);
        }

        return Ok(new IngestionResponse(
            outcome.Accepted,
            outcome.RaisedAlertId is not null,
            outcome.RaisedAlertId));
    }
}
