namespace ElderSafe.Api.Detection;

/// <summary>
/// Detects a fall by looking for three stages in order: free fall, impact,
/// then stillness.
/// </summary>
/// <remarks>
/// <para>
/// The three stages must all occur inside about three seconds, which comes
/// from interview finding F-01: a fall is over in under three seconds.
/// </para>
/// <para>
/// The stillness stage is what makes the rule useful. Free fall followed by
/// impact also describes sitting down heavily or a device being knocked off a
/// table. Requiring the device to then lie still is also what ties the rule to
/// the hazard: RISK-01 is about a resident who stays on the floor, and someone
/// who gets straight back up is not in that situation.
/// </para>
/// <para>
/// Known limitation: a device dropped on the floor while not being worn
/// produces the same three stages and will be reported as a fall. Real
/// products use a worn-detection signal to rule that out, which this project
/// does not have.
/// </para>
/// </remarks>
public sealed class ThresholdFallDetector : IFallDetector
{
    /// <summary>Magnitude read by a device at rest, in g.</summary>
    private const double RestingMagnitudeG = 1.0;

    private readonly FallDetectorOptions _options;

    public ThresholdFallDetector(FallDetectorOptions options)
    {
        _options = options ?? throw new ArgumentNullException(nameof(options));
    }

    public FallVerdict Evaluate(IReadOnlyList<AccelerationSample> window)
    {
        if (window is null || window.Count < 2)
        {
            return FallVerdict.NotEnoughData("fewer than two readings");
        }

        // Sorting defensively costs almost nothing at this size, and readings
        // arriving out of order would otherwise produce a wrong answer in
        // silence, which is the worst failure mode available to us here.
        var samples = window.OrderBy(s => s.At).ToArray();

        // A window may contain more than one dip below the free-fall
        // threshold: a device knocked about, then a real fall. Checking only
        // the first would let an earlier harmless dip mask a later fall, so
        // every candidate is tried.
        for (var i = 0; i < samples.Length; i++)
        {
            if (samples[i].MagnitudeG > _options.FreeFallThresholdG)
            {
                continue;
            }

            var verdict = EvaluateFrom(samples, i);

            // A fall is conclusive. So is "not enough data": every later
            // candidate starts later still, so none of them can have a
            // complete stillness period either.
            if (verdict.Outcome is FallOutcome.Fall or FallOutcome.NotEnoughData)
            {
                return verdict;
            }
        }

        return FallVerdict.NoFall("no free fall was followed by an impact and stillness");
    }

    private FallVerdict EvaluateFrom(AccelerationSample[] samples, int freeFallIndex)
    {
        var impactIndex = FindImpactAfter(samples, freeFallIndex);

        if (impactIndex is null)
        {
            return FallVerdict.NoFall("free fall was not followed by an impact in time");
        }

        var impact = samples[impactIndex.Value];
        var from = impact.At + _options.SettleAfterImpact;
        var until = from + _options.StillnessDuration;

        // The window ends before the stillness period does, so we genuinely do
        // not know yet. Saying "no fall" here would quietly discard a fall
        // whose window had not finished arriving.
        if (samples[^1].At < until)
        {
            return FallVerdict.NotEnoughData("the stillness period is not yet complete");
        }

        var observed = 0;

        foreach (var sample in samples)
        {
            if (sample.At < from || sample.At > until)
            {
                continue;
            }

            observed++;

            if (Math.Abs(sample.MagnitudeG - RestingMagnitudeG) > _options.StillnessToleranceG)
            {
                return FallVerdict.NoFall("there was movement after the impact");
            }
        }

        // Guard against a gap in the data: with no readings inside the
        // stillness period the loop above proves nothing, and declaring a fall
        // on the strength of missing data would be inventing evidence.
        if (observed == 0)
        {
            return FallVerdict.NotEnoughData("no readings during the stillness period");
        }

        return FallVerdict.Fall(impact.At);
    }

    private int? FindImpactAfter(AccelerationSample[] samples, int freeFallIndex)
    {
        var deadline = samples[freeFallIndex].At + _options.MaxTimeFromFreeFallToImpact;

        for (var j = freeFallIndex + 1; j < samples.Length; j++)
        {
            if (samples[j].At > deadline)
            {
                return null;
            }

            if (samples[j].MagnitudeG >= _options.ImpactThresholdG)
            {
                return j;
            }
        }

        return null;
    }
}
