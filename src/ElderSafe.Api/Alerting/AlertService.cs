using ElderSafe.Api.Data;
using ElderSafe.Api.Data.Entities;
using ElderSafe.Api.Detection;
using Microsoft.EntityFrameworkCore;

namespace ElderSafe.Api.Alerting;

/// <summary>
/// Raises alerts from incoming readings, records acknowledgement, and applies
/// the escalation policy's decisions to stored alerts.
/// </summary>
/// <remarks>
/// The decisions themselves live in <see cref="IFallDetector"/> and
/// <see cref="IEscalationPolicy"/>, which are pure. This class is the part
/// that talks to the database and to the outside world, and it deliberately
/// contains no rules of its own: if a question like "should this escalate?"
/// starts being answered here as well, the two answers will eventually
/// disagree.
/// </remarks>
public sealed class AlertService
{
    private readonly ElderSafeDbContext _context;
    private readonly IFallDetector _detector;
    private readonly IEscalationPolicy _policy;
    private readonly IEmergencyDispatch _dispatch;
    private readonly TimeProvider _timeProvider;
    private readonly AlertServiceOptions _options;
    private readonly ILogger<AlertService> _logger;

    public AlertService(
        ElderSafeDbContext context,
        IFallDetector detector,
        IEscalationPolicy policy,
        IEmergencyDispatch dispatch,
        TimeProvider timeProvider,
        AlertServiceOptions options,
        ILogger<AlertService> logger)
    {
        _context = context ?? throw new ArgumentNullException(nameof(context));
        _detector = detector ?? throw new ArgumentNullException(nameof(detector));
        _policy = policy ?? throw new ArgumentNullException(nameof(policy));
        _dispatch = dispatch ?? throw new ArgumentNullException(nameof(dispatch));
        _timeProvider = timeProvider ?? throw new ArgumentNullException(nameof(timeProvider));
        _options = options ?? throw new ArgumentNullException(nameof(options));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    /// <summary>
    /// Evaluates a resident's recent readings and raises a fall alert if the
    /// detector finds one (REQ-FALL-01, REQ-FALL-02).
    /// </summary>
    /// <returns>The alert raised, or null if none was.</returns>
    public async Task<Alert?> EvaluateFallAsync(
        Guid residentId,
        CancellationToken cancellationToken = default)
    {
        var now = _timeProvider.GetUtcNow();

        // One fall produces many overlapping windows that each look like a
        // fall. Without this, a single incident becomes a stream of alerts,
        // which is the fastest route to caregivers ignoring them (RISK-02).
        var alreadyOpen = await _context.Alerts.AnyAsync(
            a => a.ResidentId == residentId
                && a.Kind == AlertKind.Fall
                && a.AcknowledgedAt == null,
            cancellationToken);

        if (alreadyOpen)
        {
            return null;
        }

        var from = now - _options.DetectionWindow;

        var readings = await _context.Readings
            .Where(r => r.ResidentId == residentId && r.At >= from)
            .OrderBy(r => r.At)
            .ToListAsync(cancellationToken);

        var window = readings
            .Select(r => new AccelerationSample(r.At, r.AccelerationX, r.AccelerationY, r.AccelerationZ))
            .ToList();

        var verdict = _detector.Evaluate(window);

        if (!verdict.IsFall)
        {
            return null;
        }

        var alert = new Alert
        {
            Id = Guid.NewGuid(),
            ResidentId = residentId,
            Kind = AlertKind.Fall,
            RaisedAt = verdict.DetectedAt ?? now,
            LastNotifiedAt = now,
            EscalationLevel = 0
        };

        _context.Alerts.Add(alert);

        AddAudit(now, "RaisedAlert", residentId, $"Fall alert {alert.Id}: {verdict.Reason}");

        await _context.SaveChangesAsync(cancellationToken);

        _logger.LogInformation(
            "Fall alert {AlertId} raised for resident {ResidentId}", alert.Id, residentId);

        return alert;
    }

    /// <summary>
    /// Records that a caregiver has acknowledged an alert (REQ-FALL-03,
    /// REQ-SEC-01, REQ-SEC-02).
    /// </summary>
    /// <returns>
    /// False if no such alert exists, or the caregiver is not assigned to its
    /// resident. The caller cannot tell the two apart.
    /// </returns>
    public async Task<bool> AcknowledgeAsync(
        Guid alertId,
        Guid caregiverId,
        CancellationToken cancellationToken = default)
    {
        var alert = await _context.Alerts
            .FirstOrDefaultAsync(a => a.Id == alertId, cancellationToken);

        if (alert is null)
        {
            return false;
        }

        // Indistinguishable from "no such alert" on purpose: telling an
        // unassigned caregiver that the alert exists confirms a resident they
        // have no business knowing about (RISK-12).
        if (!await IsAssignedAsync(caregiverId, alert.ResidentId, cancellationToken))
        {
            return false;
        }

        // Idempotent: a caregiver tapping twice, or two caregivers responding
        // at once, must not overwrite who answered first.
        if (alert.AcknowledgedAt is not null)
        {
            return true;
        }

        var now = _timeProvider.GetUtcNow();

        alert.AcknowledgedAt = now;
        alert.AcknowledgedByCaregiverId = caregiverId;

        AddAudit(now, "AcknowledgedAlert", alert.ResidentId, $"Alert {alert.Id} by caregiver {caregiverId}", caregiverId);

        await _context.SaveChangesAsync(cancellationToken);

        return true;
    }

    /// <summary>
    /// Returns a resident's alerts, newest first.
    /// </summary>
    /// <remarks>
    /// Queried here rather than in the controller because this is where the
    /// per-resident authorization check belongs once REQ-SEC-01 is
    /// implemented. A query written directly in a controller is a query that
    /// has to remember the filter on its own, and the one that forgets is
    /// RISK-12.
    /// </remarks>
    public async Task<IReadOnlyList<AlertView>?> GetForResidentAsync(
        Guid caregiverId,
        Guid residentId,
        bool openOnly = true,
        CancellationToken cancellationToken = default)
    {
        if (!await IsAssignedAsync(caregiverId, residentId, cancellationToken))
        {
            return null;
        }

        var query = _context.Alerts
            .Where(a => a.ResidentId == residentId);

        if (openOnly)
        {
            query = query.Where(a => a.AcknowledgedAt == null);
        }

        return await query
            .OrderByDescending(a => a.RaisedAt)
            .Select(a => new AlertView(
                a.Id,
                a.ResidentId,
                a.Resident.DisplayName,
                a.Kind.ToString(),
                a.RaisedAt,
                a.EscalationLevel,
                a.AcknowledgedAt,
                a.DispatchedAt))
            .ToListAsync(cancellationToken);
    }

    /// <summary>
    /// Applies the escalation policy to every open alert (REQ-FALL-03 to 06).
    /// </summary>
    public async Task ApplyEscalationAsync(CancellationToken cancellationToken = default)
    {
        var now = _timeProvider.GetUtcNow();

        var openAlerts = await _context.Alerts
            .Where(a => a.AcknowledgedAt == null)
            .ToListAsync(cancellationToken);

        foreach (var alert in openAlerts)
        {
            var settings = await BuildSettingsAsync(alert.ResidentId, cancellationToken);
            var decision = _policy.Decide(alert.ToSnapshot(), settings, now);

            if (decision.Action == EscalationAction.NotifyCaregiver)
            {
                alert.EscalationLevel = decision.CaregiverLevel!.Value;
                alert.LastNotifiedAt = now;

                AddAudit(now, "EscalatedAlert", alert.ResidentId,
                    $"Alert {alert.Id} to level {alert.EscalationLevel}: {decision.Reason}");
            }
            else if (decision.Action == EscalationAction.PlaceEmergencyCall)
            {
                await TryDispatchAsync(alert, now, cancellationToken);
            }
        }

        await _context.SaveChangesAsync(cancellationToken);
    }

    private async Task TryDispatchAsync(Alert alert, DateTimeOffset now, CancellationToken cancellationToken)
    {
        // The policy will not ask twice, but re-checking here means the stored
        // state is the final guard rather than the worker's schedule
        // (REQ-FALL-06, RISK-10).
        if (alert.DispatchedAt is not null)
        {
            return;
        }

        var lastKnownPosition = await _context.Readings
            .Where(r => r.ResidentId == alert.ResidentId && r.Latitude != null)
            .OrderByDescending(r => r.At)
            .FirstOrDefaultAsync(cancellationToken);

        var request = new EmergencyCallRequest(
            alert.Id,
            alert.ResidentId,
            lastKnownPosition?.Latitude,
            lastKnownPosition?.Longitude,
            alert.RaisedAt);

        var placed = await _dispatch.PlaceCallAsync(request, cancellationToken);

        if (placed)
        {
            alert.DispatchedAt = now;
            AddAudit(now, "EmergencyCallPlaced", alert.ResidentId, $"Alert {alert.Id}");

            return;
        }

        // DispatchedAt stays null, so the next sweep tries again. The risk
        // file calls a call that never happens RISK-11, and silence is the
        // worse failure here, but a provider that reports failure after
        // connecting would be called twice. Noted as a limitation.
        _logger.LogError("Emergency call for alert {AlertId} could not be placed", alert.Id);

        AddAudit(now, "EmergencyCallFailed", alert.ResidentId, $"Alert {alert.Id}");
    }

    /// <summary>
    /// Whether this caregiver is assigned to this resident (REQ-SEC-01).
    /// </summary>
    /// <remarks>
    /// Signing in proves identity; this is what grants access. The check lives
    /// beside the queries so that every caregiver-facing path passes through
    /// it, rather than each controller remembering to filter (RISK-12).
    /// </remarks>
    private Task<bool> IsAssignedAsync(
        Guid caregiverId,
        Guid residentId,
        CancellationToken cancellationToken)
    {
        return _context.ResidentCaregivers.AnyAsync(
            rc => rc.CaregiverId == caregiverId && rc.ResidentId == residentId,
            cancellationToken);
    }

    private async Task<ResidentAlertSettings> BuildSettingsAsync(
        Guid residentId,
        CancellationToken cancellationToken)
    {
        var resident = await _context.Residents
            .FirstOrDefaultAsync(r => r.Id == residentId, cancellationToken);

        var caregiverCount = await _context.ResidentCaregivers
            .CountAsync(rc => rc.ResidentId == residentId, cancellationToken);

        return new ResidentAlertSettings(
            AssignedCaregiverCount: caregiverCount,
            EscalationLimit: resident?.EscalationLimit,
            EmergencyCallEnabled: resident?.EmergencyCallEnabled ?? false);
    }

    private void AddAudit(
        DateTimeOffset at,
        string action,
        Guid residentId,
        string detail,
        Guid? actorCaregiverId = null)
    {
        _context.AuditEntries.Add(new AuditEntry
        {
            Id = Guid.NewGuid(),
            At = at,
            ActorCaregiverId = actorCaregiverId,
            Action = action,
            ResidentId = residentId,
            Detail = detail
        });
    }
}
