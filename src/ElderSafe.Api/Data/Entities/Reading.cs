namespace ElderSafe.Api.Data.Entities;

/// <summary>
/// One reading from a resident's wearable.
/// </summary>
/// <remarks>
/// Acceleration is in g, matching <c>AccelerationSample</c>. Location is
/// optional because a reading may arrive without a usable fix.
/// </remarks>
public class Reading
{
    public Guid Id { get; set; }

    public Guid ResidentId { get; set; }

    public Resident Resident { get; set; } = null!;

    public DateTimeOffset At { get; set; }

    public double AccelerationX { get; set; }

    public double AccelerationY { get; set; }

    public double AccelerationZ { get; set; }

    public double? Latitude { get; set; }

    public double? Longitude { get; set; }

    /// <summary>
    /// Reported accuracy of the fix, in metres. A reading less accurate than
    /// the configured threshold is skipped by the geofence evaluator rather
    /// than counted either way (RISK-03, RISK-04).
    /// </summary>
    public double? AccuracyMeters { get; set; }
}
