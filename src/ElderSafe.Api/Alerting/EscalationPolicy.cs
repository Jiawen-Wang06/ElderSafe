namespace ElderSafe.Api.Alerting;

/// <summary>
/// Escalates an unacknowledged alert through a resident's caregivers, and
/// falls back to an emergency call when the chain is exhausted.
/// </summary>
/// <remarks>
/// <para>
/// The order of the checks below is the safety argument. Acknowledgement and
/// an already-placed call are both absorbing states: once either is true,
/// nothing further happens to the alert no matter how much time passes. They
/// are therefore tested first, before any arithmetic on the clock, so that no
/// later branch can undo them.
/// </para>
/// <para>
/// Exactly-once dispatch (REQ-FALL-06, RISK-10) comes from the alert's own
/// recorded state rather than from a flag held in memory or a lock. A process
/// that restarts mid-incident, or a second worker evaluating the same alert,
/// reads the same timestamp and reaches the same conclusion.
/// </para>
/// </remarks>
public sealed class EscalationPolicy : IEscalationPolicy
{
    private readonly EscalationOptions _options;

    public EscalationPolicy(EscalationOptions options)
    {
        _options = options ?? throw new ArgumentNullException(nameof(options));
    }

    public EscalationDecision Decide(
        AlertSnapshot alert,
        ResidentAlertSettings settings,
        DateTimeOffset now)
    {
        ArgumentNullException.ThrowIfNull(alert);
        ArgumentNullException.ThrowIfNull(settings);

        if (alert.AcknowledgedAt is not null)
        {
            return EscalationDecision.None("the alert has been acknowledged");
        }

        if (alert.DispatchedAt is not null)
        {
            return EscalationDecision.None("an emergency call has already been placed for this alert");
        }

        if (now < alert.LastNotifiedAt + _options.ResponseTimeout)
        {
            return EscalationDecision.None("the current caregiver still has time to respond");
        }

        var nextLevel = alert.EscalationLevel + 1;

        if (nextLevel < settings.EffectiveEscalationLimit)
        {
            return EscalationDecision.NotifyCaregiver(nextLevel);
        }

        // The chain is exhausted. Whether anything happens now is the
        // resident's own choice, and its default is off: an unwanted emergency
        // call is RISK-09, and a product that places them against a family's
        // wishes gets the feature switched off entirely.
        if (settings.EmergencyCallEnabled)
        {
            return EscalationDecision.PlaceEmergencyCall();
        }

        return EscalationDecision.None(
            "the escalation chain is exhausted and emergency calling is off for this resident");
    }
}
