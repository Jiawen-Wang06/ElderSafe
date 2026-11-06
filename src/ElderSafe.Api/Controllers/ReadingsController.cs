using ElderSafe.Api.Alerting;
using Microsoft.AspNetCore.Mvc;

namespace ElderSafe.Api.Controllers;

/// <summary>What the ingestion endpoint reports back.</summary>
public sealed record IngestionResponse(int Accepted, bool AlertRaised, Guid? AlertId);

/// <summary>
/// Accepts readings from a resident's wearable.
/// </summary>
/// <remarks>
/// <b>Unauthenticated in this version.</b> REQ-SEC-01 and REQ-SEC-03 are not
/// met until the authentication work lands, and the traceability matrix says
/// so rather than implying otherwise.
/// </remarks>
[ApiController]
[Route("api/residents/{residentId:guid}/readings")]
public sealed class ReadingsController : ControllerBase
{
    private readonly ReadingIngestionService _ingestion;

    public ReadingsController(ReadingIngestionService ingestion)
    {
        _ingestion = ingestion;
    }

    [HttpPost]
    public async Task<ActionResult<IngestionResponse>> Post(
        Guid residentId,
        [FromBody] IReadOnlyList<IncomingReading> readings,
        CancellationToken cancellationToken)
    {
        if (readings is null || readings.Count == 0)
        {
            return BadRequest("At least one reading is required.");
        }

        var outcome = await _ingestion.IngestAsync(residentId, readings, cancellationToken);

        if (!outcome.ResidentFound)
        {
            return NotFound();
        }

        return Ok(new IngestionResponse(
            outcome.Accepted,
            outcome.RaisedAlertId is not null,
            outcome.RaisedAlertId));
    }
}
