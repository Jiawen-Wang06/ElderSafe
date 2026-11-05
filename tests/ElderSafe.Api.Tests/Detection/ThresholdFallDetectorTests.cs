using ElderSafe.Api.Detection;

namespace ElderSafe.Api.Tests.Detection;

public class ThresholdFallDetectorTests
{
    private static readonly DateTimeOffset Origin = new(2026, 11, 5, 3, 0, 0, TimeSpan.Zero);

    private static ThresholdFallDetector CreateDetector() => new(new FallDetectorOptions());

    [Fact]
    [Trait("Requirement", "REQ-FALL-01")]
    public void Reports_a_fall_when_free_fall_impact_and_stillness_occur_in_order()
    {
        var window = new Window()
            .Steady(seconds: 1.0, magnitudeG: 1.0)
            .Steady(seconds: 0.3, magnitudeG: 0.4)
            .Spike(magnitudeG: 3.0)
            .Steady(seconds: 2.6, magnitudeG: 1.0)
            .Build();

        var verdict = CreateDetector().Evaluate(window);

        Assert.Equal(FallOutcome.Fall, verdict.Outcome);
        Assert.NotNull(verdict.DetectedAt);
    }

    [Fact]
    [Trait("Requirement", "REQ-FALL-01")]
    public void Does_not_report_a_fall_during_ordinary_movement()
    {
        var window = new Window()
            .Steady(seconds: 4.0, magnitudeG: 1.3)
            .Build();

        var verdict = CreateDetector().Evaluate(window);

        Assert.Equal(FallOutcome.NoFall, verdict.Outcome);
    }

    [Fact]
    [Trait("Requirement", "REQ-FALL-01")]
    public void Does_not_report_a_fall_when_an_impact_has_no_free_fall_before_it()
    {
        var window = new Window()
            .Steady(seconds: 1.0, magnitudeG: 1.0)
            .Spike(magnitudeG: 3.0)
            .Steady(seconds: 2.6, magnitudeG: 1.0)
            .Build();

        var verdict = CreateDetector().Evaluate(window);

        Assert.Equal(FallOutcome.NoFall, verdict.Outcome);
    }

    [Fact]
    [Trait("Requirement", "REQ-FALL-01")]
    public void Does_not_report_a_fall_when_the_resident_moves_again_afterwards()
    {
        var window = new Window()
            .Steady(seconds: 1.0, magnitudeG: 1.0)
            .Steady(seconds: 0.3, magnitudeG: 0.4)
            .Spike(magnitudeG: 3.0)
            .Steady(seconds: 1.0, magnitudeG: 1.0)
            .Steady(seconds: 1.6, magnitudeG: 1.6)
            .Build();

        var verdict = CreateDetector().Evaluate(window);

        Assert.Equal(FallOutcome.NoFall, verdict.Outcome);
    }

    [Fact]
    [Trait("Requirement", "REQ-FALL-01")]
    public void Does_not_report_a_fall_when_the_impact_arrives_too_long_after_the_free_fall()
    {
        var window = new Window()
            .Steady(seconds: 1.0, magnitudeG: 1.0)
            .Steady(seconds: 0.3, magnitudeG: 0.4)
            .Steady(seconds: 1.2, magnitudeG: 1.0)
            .Spike(magnitudeG: 3.0)
            .Steady(seconds: 2.6, magnitudeG: 1.0)
            .Build();

        var verdict = CreateDetector().Evaluate(window);

        Assert.Equal(FallOutcome.NoFall, verdict.Outcome);
    }

    [Fact]
    [Trait("Requirement", "REQ-FALL-01")]
    public void Reports_a_fall_even_when_an_unrelated_dip_came_earlier_in_the_window()
    {
        var window = new Window()
            .Steady(seconds: 0.5, magnitudeG: 1.0)
            .Steady(seconds: 0.2, magnitudeG: 0.5)
            .Steady(seconds: 1.5, magnitudeG: 1.0)
            .Steady(seconds: 0.3, magnitudeG: 0.4)
            .Spike(magnitudeG: 3.0)
            .Steady(seconds: 2.6, magnitudeG: 1.0)
            .Build();

        var verdict = CreateDetector().Evaluate(window);

        Assert.Equal(FallOutcome.Fall, verdict.Outcome);
    }

    [Fact]
    [Trait("Requirement", "REQ-FALL-01")]
    public void Says_not_enough_data_when_the_stillness_period_is_incomplete()
    {
        var window = new Window()
            .Steady(seconds: 1.0, magnitudeG: 1.0)
            .Steady(seconds: 0.3, magnitudeG: 0.4)
            .Spike(magnitudeG: 3.0)
            .Steady(seconds: 0.5, magnitudeG: 1.0)
            .Build();

        var verdict = CreateDetector().Evaluate(window);

        Assert.Equal(FallOutcome.NotEnoughData, verdict.Outcome);
    }

    [Fact]
    [Trait("Requirement", "REQ-FALL-01")]
    public void Says_not_enough_data_for_an_empty_window()
    {
        var verdict = CreateDetector().Evaluate(Array.Empty<AccelerationSample>());

        Assert.Equal(FallOutcome.NotEnoughData, verdict.Outcome);
    }

    [Fact]
    [Trait("Requirement", "REQ-FALL-01")]
    public void Reports_a_fall_when_the_readings_arrive_out_of_order()
    {
        var window = new Window()
            .Steady(seconds: 1.0, magnitudeG: 1.0)
            .Steady(seconds: 0.3, magnitudeG: 0.4)
            .Spike(magnitudeG: 3.0)
            .Steady(seconds: 2.6, magnitudeG: 1.0)
            .Build()
            .Reverse()
            .ToArray();

        var verdict = CreateDetector().Evaluate(window);

        Assert.Equal(FallOutcome.Fall, verdict.Outcome);
    }

    [Fact]
    [Trait("Requirement", "REQ-FALL-01")]
    public void Measures_magnitude_in_g_regardless_of_device_orientation()
    {
        var flat = new AccelerationSample(Origin, 0, 0, 1.0);
        var tilted = new AccelerationSample(Origin, 0.6, 0, 0.8);

        Assert.Equal(1.0, flat.MagnitudeG, precision: 6);
        Assert.Equal(1.0, tilted.MagnitudeG, precision: 6);
    }

    private sealed class Window
    {
        private const double SampleIntervalSeconds = 0.05;

        private readonly List<AccelerationSample> _samples = [];
        private double _seconds;

        public Window Steady(double seconds, double magnitudeG)
        {
            var count = (int)Math.Round(seconds / SampleIntervalSeconds);

            for (var i = 0; i < count; i++)
            {
                Add(magnitudeG);
            }

            return this;
        }

        public Window Spike(double magnitudeG)
        {
            Add(magnitudeG);
            return this;
        }

        public AccelerationSample[] Build() => [.. _samples];

        private void Add(double magnitudeG)
        {
            _samples.Add(new AccelerationSample(
                Origin.AddSeconds(_seconds), 0, 0, magnitudeG));

            _seconds += SampleIntervalSeconds;
        }
    }
}
