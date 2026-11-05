namespace ElderSafe.Api.Alerting;

/// <summary>
/// Everything about an alert that the escalation policy needs in order to
/// decide what should happen to it next.
/// </summary>
/// <remarks>
/// A snapshot rather than the stored entity, so the policy cannot reach into
/// the database, lazily load a navigation property, or be changed by the time
/// it finishes deciding. The decision depends only on the values here and the
/// current time.
/// </remarks>
/// <param name="Id">The alert.</param>
/// <param name="ResidentId">Who the alert is about.</param>
/// <param name="Kind">What the alert is about.</param>
/// <param name="RaisedAt">When the alert was first raised.</param>
/// <param name="LastNotifiedAt">
/// When the caregiver at the current escalation level was notified. The
/// response timeout runs from here rather than from <paramref name="RaisedAt"/>,
/// so each caregiver gets the full window to respond.
/// </param>
/// <param name="EscalationLevel">
/// Zero-based index of the caregiver currently notified. A newly raised alert
/// is at level 0, with the resident's first caregiver notified.
/// </param>
/// <param name="AcknowledgedAt">When a caregiver acknowledged it, if they have.</param>
/// <param name="DispatchedAt">
/// When an emergency call was placed for this alert, if one was. Its presence
/// is what makes a second call impossible (REQ-FALL-06, RISK-10).
/// </param>
public sealed record AlertSnapshot(
    Guid Id,
    Guid ResidentId,
    AlertKind Kind,
    DateTimeOffset RaisedAt,
    DateTimeOffset LastNotifiedAt,
    int EscalationLevel,
    DateTimeOffset? AcknowledgedAt = null,
    DateTimeOffset? DispatchedAt = null);
