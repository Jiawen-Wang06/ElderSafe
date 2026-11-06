using ElderSafe.Api.Alerting;

namespace ElderSafe.Api.Simulation;

/// <summary>
/// Named sequences of readings, shared by the simulator and the tests.
/// </summary>
/// <remarks>
/// <para>
/// One definition on purpose. If the demo had its own hand-tuned fall, it
/// could be easier to detect than anything the tests verify, and the
/// demonstration would quietly look better than the detector is (RISK-02).
/// Sharing the fixtures means the two cannot drift.
/// </para>
/// <para>
/// Readings carry realistic timestamps and are produced in one batch. The
/// detector works from the timestamps rather than from when data arrives, so
/// pacing the replay in real time would change how the demo looks and not
/// what it concludes.
/// </para>
/// </remarks>
public static class ScenarioLibrary
{
    /// <summary>A resident falling: free fall, impact, then lying still.</summary>
    public const string Fall = "fall";

    /// <summary>Ordinary movement that must not be mistaken for a fall.</summary>
    public const string OrdinaryMovement = "ordinary-movement";

    private const double SampleIntervalSeconds = 0.05;

    /// <summary>Every scenario that can be triggered.</summary>
    public static IReadOnlyList<string> Names { get; } = [Fall, OrdinaryMovement];

    /// <summary>
    /// Builds a scenario ending at <paramref name="endingAt"/>.
    /// </summary>
    /// <returns>False if no scenario has that name.</returns>
    public static bool TryBuild(
        string name,
        DateTimeOffset endingAt,
        out IReadOnlyList<IncomingReading> readings)
    {
        var shape = name switch
        {
            Fall => BuildFall(),
            OrdinaryMovement => BuildOrdinaryMovement(),
            _ => null
        };

        if (shape is null)
        {
            readings = [];

            return false;
        }

        // The last reading lands just before the end of the window, so the
        // whole sequence sits inside the detector's look-back.
        var start = endingAt
            - TimeSpan.FromSeconds(shape.Count * SampleIntervalSeconds);

        readings = shape
            .Select((magnitudeG, index) => new IncomingReading(
                At: start + TimeSpan.FromSeconds(index * SampleIntervalSeconds),
                AccelerationX: 0,
                AccelerationY: 0,
                AccelerationZ: magnitudeG,
                Latitude: null,
                Longitude: null,
                AccuracyMeters: null))
            .ToList();

        return true;
    }

    private static List<double> BuildFall()
    {
        var shape = new List<double>();

        Hold(shape, seconds: 0.5, magnitudeG: 1.0);
        Hold(shape, seconds: 0.3, magnitudeG: 0.4);
        shape.Add(3.0);
        Hold(shape, seconds: 2.6, magnitudeG: 1.0);

        return shape;
    }

    private static List<double> BuildOrdinaryMovement()
    {
        var shape = new List<double>();

        Hold(shape, seconds: 3.9, magnitudeG: 1.3);

        return shape;
    }

    private static void Hold(List<double> shape, double seconds, double magnitudeG)
    {
        var count = (int)Math.Round(seconds / SampleIntervalSeconds);

        for (var i = 0; i < count; i++)
        {
            shape.Add(magnitudeG);
        }
    }
}
