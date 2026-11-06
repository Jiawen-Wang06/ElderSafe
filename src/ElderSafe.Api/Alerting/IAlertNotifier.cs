namespace ElderSafe.Api.Alerting;

/// <summary>
/// Tells connected caregivers that something has happened to an alert.
/// </summary>
/// <remarks>
/// <para>
/// An interface rather than a direct dependency on SignalR, so that raising an
/// alert can be tested without a hub, and so the transport can change without
/// touching the alerting rules.
/// </para>
/// <para>
/// Delivery is best-effort by design. The alert is already stored before any
/// notification is attempted, and a caregiver signing in is shown the open
/// alerts, so a push that never arrives is recoverable rather than lost
/// (RISK-08). What it is not is a substitute for being told at all: a
/// caregiver with no active session is not reached by any of this, which is
/// RISK-16 and why ADR-007 exists.
/// </para>
/// </remarks>
public interface IAlertNotifier
{
    /// <summary>A new alert has been raised.</summary>
    Task AlertRaisedAsync(AlertView alert, CancellationToken cancellationToken = default);

    /// <summary>An existing alert has been escalated or acknowledged.</summary>
    Task AlertUpdatedAsync(AlertView alert, CancellationToken cancellationToken = default);
}
