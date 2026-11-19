namespace ElderSafe.Api.Alerting;

/// <summary>
/// One point on an alert's acceleration trace.
/// </summary>
/// <remarks>
/// Magnitude rather than the three axes, because magnitude is what the
/// detector actually decides on. Showing the axes would look like more
/// evidence while showing less of the reasoning: a reader could not tell from
/// them which number crossed which threshold.
/// </remarks>
public sealed record TraceSample(DateTimeOffset At, double MagnitudeG);

/// <summary>
/// The rule-based detector's thresholds, sent alongside the trace.
/// </summary>
/// <remarks>
/// <para>
/// These are the thresholds <see cref="Detection.ThresholdFallDetector"/>
/// applies. When the trained model is the detector, they were not applied to
/// the alert; the chart still draws them as a reference a person can read,
/// and <see cref="AlertTrace.Detector"/> says which case this is.
/// </para>
/// <para>
/// Reported rather than assumed by the client. The values live in
/// configuration and can be retuned without a rebuild (ADR-002), so a chart
/// that drew 2.5 g from a constant in its own source would, after the first
/// retune, confidently draw a line the detector no longer uses.
/// </para>
/// </remarks>
public sealed record DetectorThresholds(
    double FreeFallG,
    double ImpactG,
    double StillnessCentreG,
    double StillnessToleranceG,
    double SettleAfterImpactSeconds,
    double StillnessDurationSeconds);

/// <summary>
/// The sensor data behind one alert, with the thresholds applied to it
/// (REQ-FALL-08).
/// </summary>
/// <remarks>
/// This is the evidence for a decision the resident cannot contest and the
/// caregiver cannot otherwise inspect. A caregiver who believes an alert was
/// wrong has, without this, no way to tell a genuine fall from a dropped
/// device, and no way to report anything useful about it. Being able to see
/// which sample crossed which threshold is what turns "the system said so"
/// into something reviewable, which is what RISK-02 needs: alerts stop being
/// ignored when they can be understood.
/// </remarks>
/// <param name="Detector">
/// The detector configured now: <c>Threshold</c> or <c>Onnx</c>.
/// </param>
/// <param name="DecisionReason">
/// The reason the detector recorded in the audit log when it raised this
/// alert, such as the model version and its score. This is the record of
/// what actually decided, and stays true if the configuration has changed
/// since. Null only if no such entry exists.
/// </param>
public sealed record AlertTrace(
    Guid AlertId,
    Guid ResidentId,
    string ResidentDisplayName,
    string Kind,
    DateTimeOffset RaisedAt,
    DateTimeOffset WindowStart,
    DateTimeOffset WindowEnd,
    string Detector,
    string? DecisionReason,
    DetectorThresholds Thresholds,
    IReadOnlyList<TraceSample> Samples);
