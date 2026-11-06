namespace ElderSafe.Api.Alerting;

/// <summary>
/// An alert as a caregiver sees it.
/// </summary>
/// <remarks>
/// A read model rather than the <c>Alert</c> entity, so a change to the
/// database schema does not silently change the API, and so no navigation
/// property can drag unrelated data into a response.
/// </remarks>
public sealed record AlertView(
    Guid Id,
    Guid ResidentId,
    string ResidentDisplayName,
    string Kind,
    DateTimeOffset RaisedAt,
    int EscalationLevel,
    DateTimeOffset? AcknowledgedAt,
    DateTimeOffset? DispatchedAt);
