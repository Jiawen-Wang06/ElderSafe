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
/// The fall uses values measured from recorded data rather than values chosen
/// to clear a detector's thresholds. Across the falls in the UCI Simulated
/// Falls and UMAFall datasets (waist sensor), the median lowest magnitude is
/// 0.20 to 0.33 g and the median peak is 3.8 to 4.2 g, and the wearer ends
/// lying down, so gravity moves to a different axis. An earlier version
/// dipped only to 0.4 g, peaked at 3.0 g and never changed orientation:
/// milder than nearly every recorded fall, which made the demo describe a
/// fall no real resident produces.
/// </para>
/// <para>
/// Every scenario lasts the full detection window (4 s at 20 Hz, matching
/// <c>Alerting:DetectionWindow</c>), so a detector sees as much data as it
/// would from a wearable that had been streaming all along.
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

        // The first reading lands exactly at the start of the detection
        // window and the last one just before its end, so the whole sequence
        // sits inside the detector's look-back.
        var start = endingAt
            - TimeSpan.FromSeconds(shape.Count * SampleIntervalSeconds);

        readings = shape
            .Select((sample, index) => new IncomingReading(
                At: start + TimeSpan.FromSeconds(index * SampleIntervalSeconds),
                AccelerationX: sample.X,
                AccelerationY: sample.Y,
                AccelerationZ: sample.Z,
                Latitude: null,
                Longitude: null,
                AccuracyMeters: null))
            .ToList();

        return true;
    }

    private static List<Axes> BuildFall()
    {
        var shape = new List<Axes>();

        // Standing: gravity along the body's vertical axis.
        Hold(shape, seconds: 1.0, new Axes(0, 0, 1.0));

        // Free fall, then the impact.
        Hold(shape, seconds: 0.4, new Axes(0, 0, 0.2));
        shape.Add(new Axes(0, 0, 4.0));

        // Lying still on the floor: gravity now along a horizontal axis.
        Hold(shape, seconds: 2.55, new Axes(1.0, 0, 0));

        return shape;
    }

    private static List<Axes> BuildOrdinaryMovement()
    {
        var shape = new List<Axes>();

        Hold(shape, seconds: 4.0, new Axes(0, 0, 1.3));

        return shape;
    }

    private static void Hold(List<Axes> shape, double seconds, Axes sample)
    {
        var count = (int)Math.Round(seconds / SampleIntervalSeconds);

        for (var i = 0; i < count; i++)
        {
            shape.Add(sample);
        }
    }

    /// <summary>One accelerometer sample, in g.</summary>
    private readonly record struct Axes(double X, double Y, double Z);
}
