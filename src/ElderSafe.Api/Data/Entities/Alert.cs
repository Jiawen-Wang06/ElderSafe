using ElderSafe.Api.Alerting;

namespace ElderSafe.Api.Data.Entities;

/// <summary>
/// A raised alert and everything that has happened to it.
/// </summary>
/// <remarks>
/// There is no separate state column. Whether an alert is acknowledged is
/// decided by <see cref="AcknowledgedAt"/> being set, and whether a call was
/// placed by <see cref="DispatchedAt"/>. A status column alongside those
/// timestamps would be a second source of truth that can disagree with the
/// first, and the escalation policy reads the timestamps.
/// </remarks>
public class Alert
{
    public Guid Id { get; set; }

    public Guid ResidentId { get; set; }

    public Resident Resident { get; set; } = null!;

    public AlertKind Kind { get; set; }

    public DateTimeOffset RaisedAt { get; set; }

    /// <summary>
    /// When the caregiver at the current escalation level was notified. The
    /// response timeout runs from here (REQ-FALL-03).
    /// </summary>
    public DateTimeOffset LastNotifiedAt { get; set; }

    /// <summary>Zero-based index of the caregiver currently notified.</summary>
    public int EscalationLevel { get; set; }

    public DateTimeOffset? AcknowledgedAt { get; set; }

    public Guid? AcknowledgedByCaregiverId { get; set; }

    public Caregiver? AcknowledgedByCaregiver { get; set; }

    /// <summary>
    /// When an emergency call was placed for this alert, if one was. Its
    /// presence is what makes a second call impossible, which is why it is
    /// stored rather than held in memory (REQ-FALL-06, RISK-10).
    /// </summary>
    public DateTimeOffset? DispatchedAt { get; set; }

    /// <summary>
    /// Builds the snapshot the escalation policy works from.
    /// </summary>
    public AlertSnapshot ToSnapshot()
    {
        return new AlertSnapshot(
            Id: Id,
            ResidentId: ResidentId,
            Kind: Kind,
            RaisedAt: RaisedAt,
            LastNotifiedAt: LastNotifiedAt,
            EscalationLevel: EscalationLevel,
            AcknowledgedAt: AcknowledgedAt,
            DispatchedAt: DispatchedAt);
    }
}
