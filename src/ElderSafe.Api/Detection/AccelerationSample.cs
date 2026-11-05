namespace ElderSafe.Api.Detection;

/// <summary>
/// One accelerometer reading from a wearable.
/// </summary>
/// <remarks>
/// Axis values are in <b>g</b> (1 g = 9.81 m/s²), not in m/s². A device lying
/// still on a table reads a magnitude of about 1 g, because gravity is still
/// acting on it. Mixing the two units is the classic bug in this kind of code:
/// a threshold of 2.5 compared against m/s² values would fire on almost
/// everything, so the unit is part of the type's contract and is asserted in
/// the detector's tests.
/// </remarks>
/// <param name="At">When the reading was taken.</param>
/// <param name="X">Acceleration on the X axis, in g.</param>
/// <param name="Y">Acceleration on the Y axis, in g.</param>
/// <param name="Z">Acceleration on the Z axis, in g.</param>
public readonly record struct AccelerationSample(
    DateTimeOffset At,
    double X,
    double Y,
    double Z)
{
    /// <summary>
    /// The combined strength of the acceleration, in g, regardless of direction.
    /// </summary>
    /// <remarks>
    /// Orientation is deliberately discarded. A person can fall in any
    /// direction and a wearable can be worn at any angle, so a rule written
    /// against a single axis would work only for one of them.
    /// </remarks>
    public double MagnitudeG => Math.Sqrt(X * X + Y * Y + Z * Z);
}
