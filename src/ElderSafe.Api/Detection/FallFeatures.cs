namespace ElderSafe.Api.Detection;

/// <summary>
/// The features the fall model is given for one window of readings.
/// </summary>
/// <remarks>
/// <para>
/// This class and <c>tools/ml/features.py</c> must compute the same values in
/// the same order. The model was trained on the Python version, so any
/// difference here means the model is scoring inputs it has never seen, and
/// nothing would fail loudly. The parity test in <c>OnnxFallDetectorTests</c>
/// compares the two on windows exported by the training run.
/// </para>
/// <para>
/// Time-based segments (the first second, the last 1.5 seconds) are measured
/// from timestamps, not sample counts, so a window with uneven spacing is
/// described the same way it would be in training.
/// </para>
/// </remarks>
public static class FallFeatures
{
    /// <summary>Feature names in model input order, as written to model.json.</summary>
    public static readonly IReadOnlyList<string> Names =
    [
        "mag_min",
        "mag_max",
        "mag_mean",
        "mag_std",
        "peak_minus_dip_s",
        "tail_mag_mean",
        "tail_mag_std",
        "orientation_change_deg",
    ];

    /// <summary>Length of the "before" posture segment, in seconds.</summary>
    private const double HeadSeconds = 1.0;

    /// <summary>Length of the "after" stillness segment, in seconds.</summary>
    private const double TailSeconds = 1.5;

    /// <summary>
    /// Boundary tolerance. Timestamps are stored in 100 ns ticks, so a sample
    /// meant to sit exactly on a segment edge can land a hair either side of
    /// it. Python uses the same tolerance.
    /// </summary>
    private const double Epsilon = 1e-6;

    /// <param name="samples">At least two samples, sorted by timestamp.</param>
    public static float[] Compute(IReadOnlyList<AccelerationSample> samples)
    {
        ArgumentNullException.ThrowIfNull(samples);

        if (samples.Count < 2)
        {
            throw new ArgumentException("At least two samples are needed.", nameof(samples));
        }

        var n = samples.Count;
        var t = new double[n];
        var mag = new double[n];

        for (var i = 0; i < n; i++)
        {
            t[i] = (samples[i].At - samples[0].At).TotalSeconds;
            mag[i] = samples[i].MagnitudeG;
        }

        // Ties resolve to the first occurrence, as numpy's argmin and argmax do.
        var iMin = 0;
        var iMax = 0;

        for (var i = 1; i < n; i++)
        {
            if (mag[i] < mag[iMin])
            {
                iMin = i;
            }

            if (mag[i] > mag[iMax])
            {
                iMax = i;
            }
        }

        var head = Enumerable.Range(0, n).Where(i => t[i] <= HeadSeconds + Epsilon).ToArray();
        var tail = Enumerable.Range(0, n).Where(i => t[i] >= t[^1] - TailSeconds - Epsilon).ToArray();
        var tailMag = tail.Select(i => mag[i]).ToArray();

        return
        [
            (float)mag[iMin],
            (float)mag[iMax],
            (float)mag.Average(),
            (float)PopulationStandardDeviation(mag),
            (float)(t[iMax] - t[iMin]),
            (float)tailMag.Average(),
            (float)PopulationStandardDeviation(tailMag),
            (float)AngleDegrees(Mean(samples, head), Mean(samples, tail)),
        ];
    }

    private static double PopulationStandardDeviation(double[] values)
    {
        var mean = values.Average();

        return Math.Sqrt(values.Average(v => (v - mean) * (v - mean)));
    }

    private static (double X, double Y, double Z) Mean(IReadOnlyList<AccelerationSample> samples, int[] indexes) =>
        (indexes.Average(i => samples[i].X), indexes.Average(i => samples[i].Y), indexes.Average(i => samples[i].Z));

    private static double AngleDegrees((double X, double Y, double Z) a, (double X, double Y, double Z) b)
    {
        var dot = (a.X * b.X) + (a.Y * b.Y) + (a.Z * b.Z);
        var norms = Math.Sqrt((a.X * a.X) + (a.Y * a.Y) + (a.Z * a.Z))
            * Math.Sqrt((b.X * b.X) + (b.Y * b.Y) + (b.Z * b.Z));

        return Math.Acos(Math.Clamp(dot / (norms + 1e-9), -1.0, 1.0)) * 180.0 / Math.PI;
    }
}
