namespace ElderSafe.Api.Alerting;

/// <summary>
/// The simulated dispatch service: records that a call would have been placed
/// and places none.
/// </summary>
/// <remarks>
/// This is the only implementation in the project, and it must stay that way
/// unless someone has thought very hard about the consequences. The log line
/// is deliberately loud, so that a reviewer watching the demo can see the
/// decision being taken.
/// </remarks>
public sealed class LoggingEmergencyDispatch : IEmergencyDispatch
{
    private readonly ILogger<LoggingEmergencyDispatch> _logger;

    public LoggingEmergencyDispatch(ILogger<LoggingEmergencyDispatch> logger)
    {
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    public Task<bool> PlaceCallAsync(
        EmergencyCallRequest request,
        CancellationToken cancellationToken = default)
    {
        _logger.LogWarning(
            "SIMULATED emergency call for alert {AlertId}, resident {ResidentId}, " +
            "raised at {RaisedAt}, last known position {Latitude},{Longitude}. " +
            "No call was placed.",
            request.AlertId,
            request.ResidentId,
            request.RaisedAt,
            request.Latitude,
            request.Longitude);

        return Task.FromResult(true);
    }
}
