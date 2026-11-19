using ElderSafe.Api.Summaries;

namespace ElderSafe.Api.Tests.Summaries;

public class TemplateSummaryGeneratorTests
{
    private static async Task<string> Write(ShiftSummaryInput input) =>
        (await new TemplateSummaryGenerator().GenerateAsync(input, CancellationToken.None)).Text;

    [Theory(DisplayName = "REQ-AI-01 the template's summary passes validation on every evaluation scenario")]
    [MemberData(nameof(SummaryEvaluationSet.Names), MemberType = typeof(SummaryEvaluationSet))]
    [Trait("Requirement", "REQ-AI-01")]
    public async Task The_templates_summary_passes_validation_on_every_evaluation_scenario(string scenario)
    {
        var input = SummaryEvaluationSet.Scenarios[scenario];

        var text = await Write(input);

        Assert.Empty(SummaryValidator.Validate(text, input.Alerts));
    }

    [Theory(DisplayName = "REQ-AI-01 the template cites every alert in the shift, not only the escalated ones")]
    [MemberData(nameof(SummaryEvaluationSet.Names), MemberType = typeof(SummaryEvaluationSet))]
    [Trait("Requirement", "REQ-AI-01")]
    public async Task The_template_cites_every_alert_in_the_shift(string scenario)
    {
        var input = SummaryEvaluationSet.Scenarios[scenario];

        var text = await Write(input);

        Assert.All(input.Alerts, alert => Assert.Contains($"[{alert.Ref}]", text));
    }

    [Fact(DisplayName = "REQ-AI-01 the template says a quiet shift had no alerts")]
    [Trait("Requirement", "REQ-AI-01")]
    public async Task The_template_says_a_quiet_shift_had_no_alerts()
    {
        var text = await Write(SummaryEvaluationSet.Scenarios["a quiet shift"]);

        Assert.StartsWith("No alerts were raised", text);
    }

    [Fact(DisplayName = "REQ-AI-01 the template describes an escalated alert and its emergency call")]
    [Trait("Requirement", "REQ-AI-01")]
    public async Task The_template_describes_an_escalated_alert_and_its_emergency_call()
    {
        var text = await Write(SummaryEvaluationSet.Scenarios["one alert that reached an emergency call"]);

        Assert.Contains("escalated 2 times", text);
        Assert.Contains("emergency call was placed at 01:11", text);
        Assert.Contains("has not been acknowledged", text);
    }

    [Fact(DisplayName = "REQ-AI-01 the template writes times in the caregiver's time zone")]
    [Trait("Requirement", "REQ-AI-01")]
    public async Task The_template_writes_times_in_the_caregivers_time_zone()
    {
        var utc = SummaryEvaluationSet.Scenarios["one alert answered in time"];
        var toronto = utc with { TimeZone = TimeZoneInfo.FindSystemTimeZoneById("America/Toronto") };

        var text = await Write(toronto);

        // 03:12 UTC on 18 November is 22:12 the evening before in Toronto.
        Assert.Contains("at 22:12", text);
    }

    [Fact(DisplayName = "REQ-AI-01 the template never calls an alert a fall that happened")]
    [Trait("Requirement", "REQ-AI-01")]
    public async Task The_template_never_calls_an_alert_a_fall_that_happened()
    {
        var text = await Write(SummaryEvaluationSet.Scenarios["a busy shift with every outcome"]);

        Assert.DoesNotContain("fell", text, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("injur", text, StringComparison.OrdinalIgnoreCase);
    }
}
