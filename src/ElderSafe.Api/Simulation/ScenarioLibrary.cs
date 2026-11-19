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
/// Each scenario comes in two forms. Without a <see cref="Random"/> it is the
/// fixed, canonical sequence the tests pin down. With one, it is a variation
/// drawn from ranges measured in the UCI Simulated Falls and UMAFall datasets
/// (waist sensor), so pressing the same button twice does not replay
/// identical numbers. For falls, every drawn value lies inside the middle half
/// of the recorded falls (25th to 75th percentile): lowest magnitude
/// 0.16 to 0.38 g, peak 3.2 to 4.3 g, 0.15 to 0.25 s below 0.6 g, and a
/// change in orientation of 75 to 90 degrees.
/// </para>
/// <para>
/// That choice keeps the demo to typical falls. A quarter of recorded falls
/// are milder than this on each measure, and those are the ones a detector
/// misses. The variations are therefore not evidence of accuracy: the
/// measured sensitivity on held-out data is in the model card, and it is
/// below 100%.
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
    private const int SampleCount = 80;

    /// <summary>Every scenario that can be triggered.</summary>
    public static IReadOnlyList<string> Names { get; } = [Fall, OrdinaryMovement];

    /// <summary>
    /// Builds the canonical form of a scenario ending at
    /// <paramref name="endingAt"/>.
    /// </summary>
    /// <returns>False if no scenario has that name.</returns>
    public static bool TryBuild(
        string name,
        DateTimeOffset endingAt,
        out IReadOnlyList<IncomingReading> readings) =>
        TryBuild(name, endingAt, random: null, out readings);

    /// <summary>
    /// Builds a scenario ending at <paramref name="endingAt"/>, varied by
    /// <paramref name="random"/>, or the canonical form when it is null.
    /// </summary>
    /// <returns>False if no scenario has that name.</returns>
    public static bool TryBuild(
        string name,
        DateTimeOffset endingAt,
        Random? random,
        out IReadOnlyList<IncomingReading> readings)
    {
        var shape = (name, random) switch
        {
            (Fall, null) => BuildFall(),
            (Fall, _) => BuildVariedFall(random),
            (OrdinaryMovement, null) => BuildOrdinaryMovement(),
            (OrdinaryMovement, _) => BuildVariedOrdinaryMovement(random),
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

    private static List<Axes> BuildVariedFall(Random random)
    {
        var shape = new List<Axes>();

        // The wearable never sits perfectly upright on the body.
        var upright = Tilt(new Axes(0, 0, 1), Uniform(random, 0, 10), random);
        var lying = Tilt(upright, Uniform(random, 75, 90), random);

        // Standing, 0.9 to 1.1 s. Short enough that the impact, the half
        // second of settling and the two seconds lying still all fit inside
        // the window.
        var standing = random.Next(18, 23);

        for (var i = 0; i < standing; i++)
        {
            shape.Add(Noisy(upright, 0.02, random));
        }

        // Free fall: 3 to 5 readings (0.15 to 0.25 s) well below 1 g.
        var dip = Uniform(random, 0.16, 0.38);
        var freeFall = random.Next(3, 6);

        for (var i = 0; i < freeFall; i++)
        {
            shape.Add(Noisy(upright * dip, 0.02, random));
        }

        // Impact, mostly along the direction the body hits the floor.
        shape.Add((upright + lying).Normalized() * Uniform(random, 3.2, 4.3));

        // A short rebound inside the half second the detector lets settle.
        var rebound = random.Next(2, 5);

        for (var i = 0; i < rebound; i++)
        {
            shape.Add(Noisy(lying * Uniform(random, 1.1, 1.8), 0.05, random));
        }

        // Lying still, with the small sensor noise recorded falls show.
        var noise = Uniform(random, 0.01, 0.03);

        while (shape.Count < SampleCount)
        {
            shape.Add(Noisy(lying, noise, random));
        }

        return shape;
    }

    private static List<Axes> BuildVariedOrdinaryMovement(Random random) =>
        random.Next(3) switch
        {
            0 => BuildWalking(random),
            1 => BuildSittingDown(random),
            _ => BuildStanding(random)
        };

    private static List<Axes> BuildWalking(Random random)
    {
        var shape = new List<Axes>();
        var upright = Tilt(new Axes(0, 0, 1), Uniform(random, 0, 10), random);
        var forward = Tilt(upright, 90, random);
        var stepHz = Uniform(random, 1.6, 2.1);
        var bounce = Uniform(random, 0.15, 0.35);
        var phase = Uniform(random, 0, 2 * Math.PI);

        for (var i = 0; i < SampleCount; i++)
        {
            var angle = (2 * Math.PI * stepHz * i * SampleIntervalSeconds) + phase;
            var sample = (upright * (1 + (bounce * Math.Sin(angle))))
                + (forward * (0.1 * Math.Cos(angle)));

            shape.Add(Noisy(sample, 0.03, random));
        }

        return shape;
    }

    private static List<Axes> BuildSittingDown(Random random)
    {
        var shape = new List<Axes>();
        var upright = Tilt(new Axes(0, 0, 1), Uniform(random, 0, 10), random);
        var seated = Tilt(upright, Uniform(random, 10, 30), random);

        var standing = random.Next(16, 30);

        for (var i = 0; i < standing; i++)
        {
            shape.Add(Noisy(upright, 0.02, random));
        }

        // Lowering into the chair: a shallow dip, nothing like free fall.
        var dip = Uniform(random, 0.7, 0.85);

        for (var i = 0; i < 8; i++)
        {
            shape.Add(Noisy(upright * dip, 0.03, random));
        }

        // Landing on the seat.
        shape.Add(seated * Uniform(random, 1.3, 1.7));

        while (shape.Count < SampleCount)
        {
            shape.Add(Noisy(seated, 0.02, random));
        }

        return shape;
    }

    private static List<Axes> BuildStanding(Random random)
    {
        var shape = new List<Axes>();
        var upright = Tilt(new Axes(0, 0, 1), Uniform(random, 0, 10), random);

        while (shape.Count < SampleCount)
        {
            shape.Add(Noisy(upright, 0.03, random));
        }

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

    private static double Uniform(Random random, double min, double max) =>
        min + (random.NextDouble() * (max - min));

    private static double Gaussian(Random random)
    {
        // Box-Muller. 1 - NextDouble() is never zero, so the log is defined.
        var u1 = 1.0 - random.NextDouble();
        var u2 = random.NextDouble();

        return Math.Sqrt(-2.0 * Math.Log(u1)) * Math.Cos(2.0 * Math.PI * u2);
    }

    private static Axes Noisy(Axes sample, double sigma, Random random) =>
        new(
            sample.X + (sigma * Gaussian(random)),
            sample.Y + (sigma * Gaussian(random)),
            sample.Z + (sigma * Gaussian(random)));

    /// <summary>
    /// Rotates a unit vector by <paramref name="degrees"/> towards a random
    /// direction perpendicular to it.
    /// </summary>
    private static Axes Tilt(Axes from, double degrees, Random random)
    {
        Axes perpendicular;

        do
        {
            var candidate = new Axes(Gaussian(random), Gaussian(random), Gaussian(random));
            perpendicular = candidate - (from * candidate.Dot(from));
        }
        while (perpendicular.Length < 1e-3);

        var radians = degrees * Math.PI / 180.0;

        return (from * Math.Cos(radians)) + (perpendicular.Normalized() * Math.Sin(radians));
    }

    /// <summary>One accelerometer sample, in g.</summary>
    private readonly record struct Axes(double X, double Y, double Z)
    {
        public double Length => Math.Sqrt((X * X) + (Y * Y) + (Z * Z));

        public double Dot(Axes other) => (X * other.X) + (Y * other.Y) + (Z * other.Z);

        public Axes Normalized() => this * (1.0 / Length);

        public static Axes operator +(Axes a, Axes b) => new(a.X + b.X, a.Y + b.Y, a.Z + b.Z);

        public static Axes operator -(Axes a, Axes b) => new(a.X - b.X, a.Y - b.Y, a.Z - b.Z);

        public static Axes operator *(Axes a, double factor) => new(a.X * factor, a.Y * factor, a.Z * factor);
    }
}
