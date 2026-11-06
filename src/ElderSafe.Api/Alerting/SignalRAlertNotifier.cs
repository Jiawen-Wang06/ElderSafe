using Microsoft.AspNetCore.SignalR;

namespace ElderSafe.Api.Alerting;

/// <summary>
/// Sends alerts to the caregivers subscribed to that resident.
/// </summary>
public sealed class SignalRAlertNotifier : IAlertNotifier
{
    /// <summary>Client method invoked when an alert is first raised.</summary>
    public const string AlertRaisedMethod = "alertRaised";

    /// <summary>Client method invoked when an alert changes.</summary>
    public const string AlertUpdatedMethod = "alertUpdated";

    private readonly IHubContext<AlertsHub> _hub;

    public SignalRAlertNotifier(IHubContext<AlertsHub> hub)
    {
        _hub = hub ?? throw new ArgumentNullException(nameof(hub));
    }

    public Task AlertRaisedAsync(AlertView alert, CancellationToken cancellationToken = default)
    {
        return Send(AlertRaisedMethod, alert, cancellationToken);
    }

    public Task AlertUpdatedAsync(AlertView alert, CancellationToken cancellationToken = default)
    {
        return Send(AlertUpdatedMethod, alert, cancellationToken);
    }

    private Task Send(string method, AlertView alert, CancellationToken cancellationToken)
    {
        // Sent to the resident's group rather than to all clients, so a
        // connection only ever receives alerts for residents it was allowed to
        // subscribe to (RISK-12).
        return _hub.Clients
            .Group(AlertsHub.ResidentGroup(alert.ResidentId))
            .SendAsync(method, alert, cancellationToken);
    }
}
