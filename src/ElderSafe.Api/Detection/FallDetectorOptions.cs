namespace ElderSafe.Api.Detection;

/// <summary>
/// Tuning values for <see cref="ThresholdFallDetector"/>.
/// </summary>
/// <remarks>
/// These live in configuration rather than in code (ADR-002) so they can be
/// changed without a rebuild. The defaults are starting points from the
/// fall-detection literature, not validated values. See
/// docs/adr/ADR-002-rule-based-detector.md for why no accuracy figure is
/// claimed for them.
/// </remarks>
public sealed class FallDetectorOptions
{
    /// <summary>Configuration section name.</summary>
    public const string SectionName = "FallDetection";

    /// <summary>
    /// Magnitude below which the device is treated as falling freely, in g.
    /// In free fall the sensor reads close to 0 g, because it is no longer
    /// being held up against gravity.
    /// </summary>
    public double FreeFallThresholdG { get; set; } = 0.6;

    /// <summary>
    /// Magnitude at or above which a sample counts as an impact, in g.
    /// </summary>
    public double ImpactThresholdG { get; set; } = 2.5;

    /// <summary>
    /// How long after the start of free fall an impact may arrive and still
    /// be treated as part of the same event.
    /// </summary>
    public TimeSpan MaxTimeFromFreeFallToImpact { get; set; } = TimeSpan.FromSeconds(1);

    /// <summary>
    /// How long to wait after an impact before judging stillness. A body and a
    /// strapped-on device keep moving briefly after hitting the floor, so
    /// measuring immediately would see that settling and call it movement.
    /// </summary>
    public TimeSpan SettleAfterImpact { get; set; } = TimeSpan.FromSeconds(0.5);

    /// <summary>
    /// How long the device must stay still after settling for the event to
    /// count as a fall.
    /// </summary>
    public TimeSpan StillnessDuration { get; set; } = TimeSpan.FromSeconds(2);

    /// <summary>
    /// How far the magnitude may stray from 1 g during the stillness period
    /// before it counts as movement, in g.
    /// </summary>
    public double StillnessToleranceG { get; set; } = 0.25;
}
