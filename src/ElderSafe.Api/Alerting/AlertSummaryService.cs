using ElderSafe.Api.Data;
using ElderSafe.Api.Security;
using Microsoft.EntityFrameworkCore;

namespace ElderSafe.Api.Alerting;

/// <summary>
/// Reads alert counts for one resident over a date range (REQ-FALL-09).
/// </summary>
/// <remarks>
/// Separate from <see cref="AlertService"/> for the same reason
/// <see cref="AlertTraceService"/> is: this only reads, and the aggregation
/// rules in <see cref="AlertSummaryBuilder"/> have no business depending on
/// the service that raises and escalates alerts.
/// </remarks>
public sealed class AlertSummaryService
{
    /// <summary>Longest range the endpoint will aggregate in one request.</summary>
    /// <remarks>
    /// A caller who wants more has to ask for it in separate requests.
    /// Without a cap, "custom range" means an open-ended query over a
    /// resident's entire history, which is a cost nobody asked for and, for a
    /// resident monitored for years, a slow answer to a screen a caregiver
    /// expects to load quickly. The controller checks the range against this
    /// before calling in, so the check is enforced once rather than twice.
    /// </remarks>
    public static readonly TimeSpan MaxRange = TimeSpan.FromDays(400);

    private readonly ElderSafeDbContext _context;
    private readonly IResidentAccess _residentAccess;
    private readonly EscalationOptions _escalationOptions;

    public AlertSummaryService(
        ElderSafeDbContext context,
        IResidentAccess residentAccess,
        EscalationOptions escalationOptions)
    {
        _context = context ?? throw new ArgumentNullException(nameof(context));
        _residentAccess = residentAccess ?? throw new ArgumentNullException(nameof(residentAccess));
        _escalationOptions = escalationOptions ?? throw new ArgumentNullException(nameof(escalationOptions));
    }

    /// <summary>
    /// Gets the summary for one resident.
    /// </summary>
    /// <returns>
    /// Null if no such resident exists, or if the caregiver is not assigned
    /// to it. One answer for both, so the endpoint never confirms that a
    /// resident exists to someone with no business knowing (RISK-12,
    /// RISK-18).
    /// </returns>
    public async Task<AlertSummary?> GetAsync(
        Guid caregiverId,
        Guid residentId,
        DateTimeOffset from,
        DateTimeOffset to,
        CancellationToken cancellationToken = default)
    {
        var resident = await _context.Residents
            .Where(r => r.Id == residentId)
            .Select(r => new { r.Id, r.DisplayName })
            .SingleOrDefaultAsync(cancellationToken);

        if (resident is null)
        {
            return null;
        }

        if (!await _residentAccess.CanViewAsync(caregiverId, residentId, cancellationToken))
        {
            return null;
        }

        var alerts = await _context.Alerts
            .Where(a => a.ResidentId == residentId && a.RaisedAt >= from && a.RaisedAt < to)
            .Select(a => new AlertSnapshot(
                a.Id,
                a.ResidentId,
                a.Kind,
                a.RaisedAt,
                a.LastNotifiedAt,
                a.EscalationLevel,
                a.AcknowledgedAt,
                a.DispatchedAt))
            .ToListAsync(cancellationToken);

        var granularity = AlertSummaryBuilder.GranularityFor(from, to);

        var buckets = AlertSummaryBuilder.Build(
            alerts, from, to, granularity, _escalationOptions.ResponseTimeout);

        // A separate projection rather than building this from the snapshots
        // above: AlertSnapshot is deliberately kept to only what the
        // escalation policy needs (see its own remarks), and an
        // acknowledger's display name is not that. Pulling it here, once,
        // keeps that contract intact instead of growing it for a reader that
        // is not the policy.
        var alertEntries = await _context.Alerts
            .Where(a => a.ResidentId == residentId && a.RaisedAt >= from && a.RaisedAt < to)
            .OrderByDescending(a => a.RaisedAt)
            .Select(a => new SummaryAlertEntry(
                a.Id,
                a.Kind,
                a.RaisedAt,
                a.AcknowledgedAt,
                a.AcknowledgedByCaregiver == null ? null : a.AcknowledgedByCaregiver.DisplayName,
                a.DispatchedAt != null))
            .ToListAsync(cancellationToken);

        return new AlertSummary(
            resident.Id, resident.DisplayName, from, to, granularity, buckets, alertEntries);
    }
}
