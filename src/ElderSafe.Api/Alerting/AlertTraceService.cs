using ElderSafe.Api.Data;
using ElderSafe.Api.Detection;
using ElderSafe.Api.Security;
using Microsoft.EntityFrameworkCore;

namespace ElderSafe.Api.Alerting;

/// <summary>
/// Reads the sensor data behind an alert, so a caregiver can see why it was
/// raised (REQ-FALL-08).
/// </summary>
/// <remarks>
/// <para>
/// Separate from <see cref="AlertService"/> rather than another method on it.
/// AlertService raises and escalates; this only reads. Keeping them apart
/// means the read model can take the detector's options without the
/// alert-raising path gaining a dependency it has no use for.
/// </para>
/// <para>
/// The same per-resident authorization rule as every other caregiver-facing
/// path, and for the same reason: an alert identifier is a guess away, and
/// without the check this endpoint would hand a stranger a resident's
/// movement data (RISK-12, RISK-14).
/// </para>
/// </remarks>
public sealed class AlertTraceService
{
    /// <summary>
    /// How much data to show either side of the window the detector judged.
    /// </summary>
    /// <remarks>
    /// Context, not evidence. A trace cut exactly at the window boundary
    /// gives no sense of what ordinary movement looked like just before, and
    /// a reader cannot judge an impact spike without seeing the quiet either
    /// side of it.
    /// </remarks>
    private static readonly TimeSpan Margin = TimeSpan.FromSeconds(1.5);

    private readonly ElderSafeDbContext _context;
    private readonly IResidentAccess _residentAccess;
    private readonly FallDetectorOptions _detectorOptions;
    private readonly AlertServiceOptions _alertOptions;

    public AlertTraceService(
        ElderSafeDbContext context,
        IResidentAccess residentAccess,
        FallDetectorOptions detectorOptions,
        AlertServiceOptions alertOptions)
    {
        _context = context ?? throw new ArgumentNullException(nameof(context));
        _residentAccess = residentAccess ?? throw new ArgumentNullException(nameof(residentAccess));
        _detectorOptions = detectorOptions ?? throw new ArgumentNullException(nameof(detectorOptions));
        _alertOptions = alertOptions ?? throw new ArgumentNullException(nameof(alertOptions));
    }

    /// <summary>
    /// Gets the trace for one alert.
    /// </summary>
    /// <returns>
    /// Null if no such alert exists, or if the caregiver is not assigned to
    /// its resident. One answer for both, so the endpoint never confirms that
    /// an alert exists to someone with no business knowing (RISK-12).
    /// </returns>
    public async Task<AlertTrace?> GetAsync(
        Guid caregiverId,
        Guid alertId,
        CancellationToken cancellationToken = default)
    {
        var alert = await _context.Alerts
            .Where(a => a.Id == alertId)
            .Select(a => new
            {
                a.Id,
                a.ResidentId,
                ResidentDisplayName = a.Resident.DisplayName,
                a.Kind,
                a.RaisedAt
            })
            .SingleOrDefaultAsync(cancellationToken);

        if (alert is null)
        {
            return null;
        }

        if (!await _residentAccess.CanViewAsync(caregiverId, alert.ResidentId, cancellationToken))
        {
            return null;
        }

        // The detector judged the window ending when the alert was raised.
        var windowStart = alert.RaisedAt - _alertOptions.DetectionWindow - Margin;
        var windowEnd = alert.RaisedAt + Margin;

        // The three axes are fetched and combined here rather than in the
        // query: a square root inside the WHERE clause is at the mercy of the
        // provider, and this one runs on SQLite.
        var rows = await _context.Readings
            .Where(r => r.ResidentId == alert.ResidentId
                && r.At >= windowStart
                && r.At <= windowEnd)
            .OrderBy(r => r.At)
            .Select(r => new
            {
                r.At,
                r.AccelerationX,
                r.AccelerationY,
                r.AccelerationZ
            })
            .ToListAsync(cancellationToken);

        var samples = rows
            .Select(r => new TraceSample(
                r.At,
                Math.Sqrt(
                    (r.AccelerationX * r.AccelerationX)
                    + (r.AccelerationY * r.AccelerationY)
                    + (r.AccelerationZ * r.AccelerationZ))))
            .ToList();

        var thresholds = new DetectorThresholds(
            _detectorOptions.FreeFallThresholdG,
            _detectorOptions.ImpactThresholdG,
            1.0,
            _detectorOptions.StillnessToleranceG,
            _detectorOptions.SettleAfterImpact.TotalSeconds,
            _detectorOptions.StillnessDuration.TotalSeconds);

        return new AlertTrace(
            alert.Id,
            alert.ResidentId,
            alert.ResidentDisplayName,
            alert.Kind.ToString(),
            alert.RaisedAt,
            windowStart,
            windowEnd,
            thresholds,
            samples);
    }
}
