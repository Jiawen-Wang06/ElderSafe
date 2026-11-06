namespace ElderSafe.Api.Alerting;

/// <summary>
/// A request to place an emergency call for an alert.
/// </summary>
/// <param name="AlertId">The alert the call is about.</param>
/// <param name="ResidentId">
/// The resident's identifier. Deliberately not their name: a dispatch
/// integration is an outside system, and nothing identifying is sent to one
/// from this project's synthetic data.
/// </param>
/// <param name="Latitude">Last known latitude, if any reading carried one.</param>
/// <param name="Longitude">Last known longitude, if any reading carried one.</param>
/// <param name="RaisedAt">When the alert was raised.</param>
/// <param name="ContactPhoneNumber">
/// The resident's configured emergency contact number (REQ-CFG-06), or null
/// if they have not set one. Carried here only so the simulated log entry
/// can show it; <see cref="IEmergencyDispatch"/> has no implementation that
/// does anything with it beyond logging it.
/// </param>
public sealed record EmergencyCallRequest(
    Guid AlertId,
    Guid ResidentId,
    double? Latitude,
    double? Longitude,
    DateTimeOffset RaisedAt,
    string? ContactPhoneNumber = null);

/// <summary>
/// Places an emergency call.
/// </summary>
/// <remarks>
/// An interface with no real implementation in this project. Automatically
/// dialling 911 cannot be done responsibly here: telephony providers restrict
/// it, a false call has real consequences for real people, and a portfolio
/// project must never place one. The design is therefore real and the adapter
/// is simulated, which is the honest way round.
/// </remarks>
public interface IEmergencyDispatch
{
    /// <summary>
    /// Attempts to place the call.
    /// </summary>
    /// <returns>
    /// True if the call was placed. False means it was not, and the caller
    /// must not record a dispatch that did not happen (REQ-FALL-06).
    /// </returns>
    Task<bool> PlaceCallAsync(EmergencyCallRequest request, CancellationToken cancellationToken = default);
}
