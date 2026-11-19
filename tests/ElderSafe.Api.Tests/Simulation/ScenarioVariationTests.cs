using ElderSafe.Api.Detection;
using ElderSafe.Api.Simulation;

namespace ElderSafe.Api.Tests.Simulation;

/// <summary>
/// The dashboard's simulator varies every scenario. These tests check that
/// the variations still mean what their name says, for both detectors, so a
/// demo press cannot land on a fall nothing detects or on movement that
/// raises an alert.
/// </summary>
/// <remarks>
/// Seeded, so a failure names the seed that produced it and can be replayed.
/// </remarks>
public class ScenarioVariationTests
{
    private const int Seeds = 500;

    private static readonly DateTimeOffset Origin = new(2026, 11, 18, 3, 0, 0, TimeSpan.Zero);

    private static readonly ThresholdFallDetector Rule = new(new FallDetectorOptions());

    private static List<AccelerationSample> Varied(string name, int seed)
    {
        Assert.True(ScenarioLibrary.TryBuild(name, Origin, new Random(seed), out var readings));

        return readings
            .Select(r => new AccelerationSample(r.At, r.AccelerationX, r.AccelerationY, r.AccelerationZ))
            .ToList();
    }

    private static OnnxFallDetector Model() =>
        OnnxFallDetector.Load(AppContext.BaseDirectory, new FallModelOptions());

    [Fact(DisplayName = "REQ-FALL-01 every varied fall is reported as a fall by both detectors")]
    [Trait("Requirement", "REQ-FALL-01")]
    public void Every_varied_fall_is_reported_as_a_fall_by_both_detectors()
    {
        using var model = Model();

        for (var seed = 0; seed < Seeds; seed++)
        {
            var fall = Varied(ScenarioLibrary.Fall, seed);

            // Also with the oldest half second trimmed, as server-side
            // latency does to the real window.
            foreach (var window in new[] { fall, fall.Skip(10).ToList() })
            {
                Assert.True(
                    Rule.Evaluate(window).Outcome == FallOutcome.Fall,
                    $"The rule missed the fall from seed {seed}.");
                Assert.True(
                    model.Evaluate(window).Outcome == FallOutcome.Fall,
                    $"The model missed the fall from seed {seed}.");
            }
        }
    }

    [Fact(DisplayName = "REQ-FALL-01 no varied ordinary movement is reported as a fall by either detector")]
    [Trait("Requirement", "REQ-FALL-01")]
    public void No_varied_ordinary_movement_is_reported_as_a_fall_by_either_detector()
    {
        using var model = Model();

        for (var seed = 0; seed < Seeds; seed++)
        {
            var movement = Varied(ScenarioLibrary.OrdinaryMovement, seed);

            Assert.True(
                Rule.Evaluate(movement).Outcome != FallOutcome.Fall,
                $"The rule reported movement from seed {seed} as a fall.");
            Assert.True(
                model.Evaluate(movement).Outcome != FallOutcome.Fall,
                $"The model reported movement from seed {seed} as a fall.");
        }
    }

    [Fact(DisplayName = "REQ-FALL-01 two simulated falls do not replay the same readings")]
    [Trait("Requirement", "REQ-FALL-01")]
    public void Two_simulated_falls_do_not_replay_the_same_readings()
    {
        var first = Varied(ScenarioLibrary.Fall, seed: 1);
        var second = Varied(ScenarioLibrary.Fall, seed: 2);

        Assert.Equal(80, first.Count);
        Assert.Equal(80, second.Count);
        Assert.NotEqual(first.Select(s => s.MagnitudeG), second.Select(s => s.MagnitudeG));
    }
}
