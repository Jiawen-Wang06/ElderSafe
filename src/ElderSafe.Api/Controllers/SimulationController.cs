using ElderSafe.Api.Alerting;
using ElderSafe.Api.Security;
using ElderSafe.Api.Simulation;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace ElderSafe.Api.Controllers;

/// <summary>What a triggered scenario produced.</summary>
public sealed record SimulationResponse(string Scenario, int ReadingsAccepted, Guid? AlertId);

/// <summary>
/// Triggers a named scenario for a resident, so the system can be watched
/// working.
/// </summary>
/// <remarks>
/// Guarded three ways, because this is a sanctioned route to fabricated
/// readings (RISK-15): the feature is off by default and the endpoint then
/// does not answer at all; the caller must be signed in; and they must be
/// assigned to the resident, like every other caregiver-facing path.
/// </remarks>
[ApiController]
[Authorize]
[Route("api/residents/{residentId:guid}/simulate")]
public sealed class SimulationController : ControllerBase
{
    private readonly SimulationOptions _options;
    private readonly IResidentAccess _residentAccess;
    private readonly ICurrentCaregiver _currentCaregiver;
    private readonly ReadingIngestionService _ingestion;
    private readonly TimeProvider _timeProvider;

    public SimulationController(
        SimulationOptions options,
        IResidentAccess residentAccess,
        ICurrentCaregiver currentCaregiver,
        ReadingIngestionService ingestion,
        TimeProvider timeProvider)
    {
        _options = options;
        _residentAccess = residentAccess;
        _currentCaregiver = currentCaregiver;
        _ingestion = ingestion;
        _timeProvider = timeProvider;
    }

    [HttpPost("{scenario}")]
    public async Task<ActionResult<SimulationResponse>> Trigger(
        Guid residentId,
        string scenario,
        CancellationToken cancellationToken)
    {
        if (!_options.Enabled)
        {
            return NotFound();
        }

        var caregiverId = _currentCaregiver.Id;

        if (caregiverId is null)
        {
            return Unauthorized();
        }

        if (!await _residentAccess.IsAssignedAsync(caregiverId.Value, residentId, cancellationToken))
        {
            return NotFound();
        }

        if (!ScenarioLibrary.TryBuild(scenario, _timeProvider.GetUtcNow(), out var readings))
        {
            return BadRequest($"Unknown scenario. Available: {string.Join(", ", ScenarioLibrary.Names)}.");
        }

        var outcome = await _ingestion.IngestAsync(residentId, readings, cancellationToken);

        if (!outcome.ResidentFound)
        {
            return NotFound();
        }

        return Ok(new SimulationResponse(scenario, outcome.Accepted, outcome.RaisedAlertId));
    }
}
