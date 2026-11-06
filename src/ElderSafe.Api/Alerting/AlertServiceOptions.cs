namespace ElderSafe.Api.Alerting;

/// <summary>
/// Tuning values for <see cref="AlertService"/> and the escalation worker.
/// </summary>
public sealed class AlertServiceOptions
{
    public const string SectionName = "Alerting";

    /// <summary>
    /// How much recent data the fall detector is given on each evaluation.
    /// </summary>
    /// <remarks>
    /// Must comfortably exceed free fall plus impact plus the stillness
    /// period, or a real fall is cut in half by the window boundary and the
    /// detector returns "not enough data" forever.
    /// </remarks>
    public TimeSpan DetectionWindow { get; set; } = TimeSpan.FromSeconds(4);

    /// <summary>
    /// How often open alerts are re-examined for escalation.
    /// </summary>
    /// <remarks>
    /// This interval is added to the worst-case escalation delay: with a
    /// two-minute timeout and a ten-second sweep, a caregiver may be notified
    /// up to two minutes ten seconds after the previous one was.
    /// </remarks>
    public TimeSpan EscalationSweepInterval { get; set; } = TimeSpan.FromSeconds(10);
}
