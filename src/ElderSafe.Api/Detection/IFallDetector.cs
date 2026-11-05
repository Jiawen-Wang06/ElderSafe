namespace ElderSafe.Api.Detection;

/// <summary>
/// Decides whether a window of accelerometer readings contains a fall.
/// </summary>
/// <remarks>
/// The interface exists so that a trained model can replace the threshold
/// rules later without any other component changing (ADR-002). Implementations
/// must be free of side effects: no database access, no clock reading, no
/// logging of resident data. That keeps them cheap to test and keeps the
/// decision reproducible from its inputs alone, which matters when someone
/// asks why a given event was or was not treated as a fall.
/// </remarks>
public interface IFallDetector
{
    /// <summary>
    /// Evaluates one window of readings for a single resident.
    /// </summary>
    /// <param name="window">
    /// Readings from one device. They need not be sorted; the implementation
    /// orders them by timestamp.
    /// </param>
    FallVerdict Evaluate(IReadOnlyList<AccelerationSample> window);
}
