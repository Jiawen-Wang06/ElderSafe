using System.Text.Json;
using ElderSafe.Api.Detection;
using ElderSafe.Api.Simulation;

namespace ElderSafe.Api.Tests.Detection;

public class OnnxFallDetectorTests
{
    private static readonly DateTimeOffset Origin = new(2026, 11, 18, 3, 0, 0, TimeSpan.Zero);

    private static readonly JsonSerializerOptions MetadataJson = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower
    };

    // The model ships with the API, so the copy in the API's build output is
    // the one under test.
    private static OnnxFallDetector CreateDetector() =>
        OnnxFallDetector.Load(AppContext.BaseDirectory, new FallModelOptions());

    private static byte[] ModelBytes() =>
        File.ReadAllBytes(Path.Combine(AppContext.BaseDirectory, new FallModelOptions().ModelPath));

    private static FallModelMetadata Metadata() =>
        JsonSerializer.Deserialize<FallModelMetadata>(
            File.ReadAllText(Path.Combine(AppContext.BaseDirectory, new FallModelOptions().MetadataPath)),
            MetadataJson)!;

    private static List<AccelerationSample> Scenario(string name)
    {
        Assert.True(ScenarioLibrary.TryBuild(name, Origin, out var readings));

        return readings
            .Select(r => new AccelerationSample(r.At, r.AccelerationX, r.AccelerationY, r.AccelerationZ))
            .ToList();
    }

    private static List<AccelerationSample> Steady(double seconds, double magnitudeG)
    {
        var count = (int)Math.Round(seconds * 20);

        return Enumerable.Range(0, count)
            .Select(i => new AccelerationSample(Origin.AddSeconds(i * 0.05), 0, 0, magnitudeG))
            .ToList();
    }

    [Fact(DisplayName = "REQ-FALL-01 computes the same features and score as the training pipeline")]
    [Trait("Requirement", "REQ-FALL-01")]
    public void Computes_the_same_features_and_score_as_the_training_pipeline()
    {
        using var detector = CreateDetector();
        var path = Path.Combine(AppContext.BaseDirectory, "Detection", "Fixtures", "fall_model_fixture.json");
        using var fixture = JsonDocument.Parse(File.ReadAllText(path));
        var windows = fixture.RootElement.GetProperty("windows").EnumerateArray().ToList();

        Assert.NotEmpty(windows);

        foreach (var window in windows)
        {
            var samples = window.GetProperty("samples").EnumerateArray()
                .Select(row => new AccelerationSample(
                    Origin.AddSeconds(row[0].GetDouble()),
                    row[1].GetDouble(),
                    row[2].GetDouble(),
                    row[3].GetDouble()))
                .ToList();
            var expected = window.GetProperty("features").EnumerateArray().Select(e => e.GetDouble()).ToArray();

            var actual = FallFeatures.Compute(samples);

            Assert.Equal(expected.Length, actual.Length);

            for (var i = 0; i < expected.Length; i++)
            {
                Assert.Equal(expected[i], actual[i], 3);
            }

            Assert.Equal(window.GetProperty("probability").GetDouble(), detector.Score(samples), 4);
        }
    }

    [Fact(DisplayName = "REQ-FALL-01 the model reports the simulator's fall as a fall")]
    [Trait("Requirement", "REQ-FALL-01")]
    public void Reports_the_simulators_fall_as_a_fall()
    {
        using var detector = CreateDetector();

        var verdict = detector.Evaluate(Scenario(ScenarioLibrary.Fall));

        Assert.Equal(FallOutcome.Fall, verdict.Outcome);
        Assert.NotNull(verdict.DetectedAt);
        Assert.Contains(detector.ModelVersion, verdict.Reason);
    }

    [Fact(DisplayName = "REQ-FALL-01 the model still reports a fall when the window arrives slightly short")]
    [Trait("Requirement", "REQ-FALL-01")]
    public void Still_reports_a_fall_when_the_window_arrives_slightly_short()
    {
        using var detector = CreateDetector();

        // The window is cut from the server clock, so latency trims its
        // oldest readings. Half a second of that must not hide a fall.
        var window = Scenario(ScenarioLibrary.Fall).Skip(10).ToList();

        var verdict = detector.Evaluate(window);

        Assert.Equal(FallOutcome.Fall, verdict.Outcome);
    }

    [Fact(DisplayName = "REQ-FALL-01 the model does not report the simulator's ordinary movement as a fall")]
    [Trait("Requirement", "REQ-FALL-01")]
    public void Does_not_report_ordinary_movement_as_a_fall()
    {
        using var detector = CreateDetector();

        var verdict = detector.Evaluate(Scenario(ScenarioLibrary.OrdinaryMovement));

        Assert.Equal(FallOutcome.NoFall, verdict.Outcome);
    }

    [Fact(DisplayName = "REQ-FALL-01 the rule still reports the simulator's fall as a fall")]
    [Trait("Requirement", "REQ-FALL-01")]
    public void The_rule_still_reports_the_simulators_fall_as_a_fall()
    {
        var verdict = new ThresholdFallDetector(new FallDetectorOptions()).Evaluate(Scenario(ScenarioLibrary.Fall));

        Assert.Equal(FallOutcome.Fall, verdict.Outcome);
    }

    [Fact(DisplayName = "REQ-FALL-01 the model does not score a window shorter than it was trained on")]
    [Trait("Requirement", "REQ-FALL-01")]
    public void Does_not_score_a_window_shorter_than_it_was_trained_on()
    {
        using var detector = CreateDetector();

        var verdict = detector.Evaluate(Steady(seconds: 2.0, magnitudeG: 1.0));

        Assert.Equal(FallOutcome.NotEnoughData, verdict.Outcome);
    }

    [Fact(DisplayName = "REQ-FALL-01 the model does not score a window with too many readings missing")]
    [Trait("Requirement", "REQ-FALL-01")]
    public void Does_not_score_a_window_with_too_many_readings_missing()
    {
        using var detector = CreateDetector();

        // Full 4 s span, but only every second reading arrived.
        var gappy = Steady(seconds: 4.0, magnitudeG: 1.0).Where((_, i) => i % 2 == 0).ToList();
        gappy.Add(new AccelerationSample(Origin.AddSeconds(3.95), 0, 0, 1.0));

        var verdict = detector.Evaluate(gappy);

        Assert.Equal(FallOutcome.NotEnoughData, verdict.Outcome);
    }

    [Fact(DisplayName = "REQ-FALL-01 an altered model file is refused at load")]
    [Trait("Requirement", "REQ-FALL-01")]
    public void An_altered_model_file_is_refused_at_load()
    {
        var model = ModelBytes();
        model[^1] ^= 0xFF;

        Assert.Throws<InvalidOperationException>(() =>
            new OnnxFallDetector(model, Metadata(), new FallModelOptions()));
    }

    [Fact(DisplayName = "REQ-FALL-01 a model expecting different features is refused at load")]
    [Trait("Requirement", "REQ-FALL-01")]
    public void A_model_expecting_different_features_is_refused_at_load()
    {
        var metadata = Metadata() with { FeatureNames = ["mag_max", "mag_min"] };

        Assert.Throws<InvalidOperationException>(() =>
            new OnnxFallDetector(ModelBytes(), metadata, new FallModelOptions()));
    }
}
