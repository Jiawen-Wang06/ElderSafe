namespace ElderSafe.Api.Alerting;

/// <summary>
/// Decides what should happen to an open alert.
/// </summary>
/// <remarks>
/// Implementations must be pure: the decision depends only on the arguments,
/// and making one changes nothing. The current time is passed in rather than
/// read inside, which is what lets a two-minute timeout be tested in
/// microseconds and what makes a past decision reproducible when someone asks
/// why an alert escalated when it did.
/// </remarks>
public interface IEscalationPolicy
{
    /// <summary>
    /// Decides what to do with <paramref name="alert"/> as at <paramref name="now"/>.
    /// </summary>
    EscalationDecision Decide(
        AlertSnapshot alert,
        ResidentAlertSettings settings,
        DateTimeOffset now);
}
