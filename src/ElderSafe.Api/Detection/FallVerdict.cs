namespace ElderSafe.Api.Detection;

/// <summary>
/// What the detector concluded about a window of readings.
/// </summary>
public enum FallOutcome
{
    NoFall,
    Fall,
    NotEnoughData
}

/// <summary>
/// The detector's decision about one window of readings, with the reason for it.
/// </summary>
/// <remarks>
/// The reason is not decoration. ElderSafe is Class C software, and when a
/// caregiver or a reviewer asks why an alert did or did not fire, "the free
/// fall was not followed by an impact in time" is an answer. A bare boolean
/// is not.
/// </remarks>
public readonly record struct FallVerdict(
    FallOutcome Outcome,
    string Reason,
    DateTimeOffset? DetectedAt)
{
    public bool IsFall => Outcome == FallOutcome.Fall;

    public static FallVerdict Fall(DateTimeOffset at) =>
        new(FallOutcome.Fall, "free fall, impact and stillness occurred in order", at);

    public static FallVerdict NoFall(string reason) =>
        new(FallOutcome.NoFall, reason, null);

    public static FallVerdict NotEnoughData(string reason) =>
        new(FallOutcome.NotEnoughData, reason, null);
}
