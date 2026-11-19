using ElderSafe.Api.Alerting;
using ElderSafe.Api.Summaries;

namespace ElderSafe.Api.Tests.Summaries;

public class SummaryValidatorTests
{
    private static readonly DateTimeOffset Raised = SummaryEvaluationSet.ShiftStart.AddHours(3);

    private static readonly ShiftEvent Answered = new("A1", AlertKind.Fall, Raised, Raised.AddMinutes(1), 0, null);

    private static readonly ShiftEvent Escalated = new("A2", AlertKind.Fall, Raised.AddHours(1), Raised.AddHours(1).AddMinutes(5), 1, null);

    private static readonly ShiftEvent Called = new("A3", AlertKind.Fall, Raised.AddHours(2), null, 0, Raised.AddHours(2).AddMinutes(2));

    [Fact(DisplayName = "REQ-AI-02 accepts a summary that cites only this shift's alerts and every escalated one")]
    [Trait("Requirement", "REQ-AI-02")]
    public void Accepts_a_summary_that_cites_only_this_shifts_alerts_and_every_escalated_one()
    {
        var problems = SummaryValidator.Validate(
            "Two alerts overnight. [A1] was answered in a minute; [A2] escalated before it was acknowledged.",
            [Answered, Escalated]);

        Assert.Empty(problems);
    }

    [Fact(DisplayName = "REQ-AI-02 rejects a summary citing an alert that is not in the input")]
    [Trait("Requirement", "REQ-AI-02")]
    public void Rejects_a_summary_citing_an_alert_that_is_not_in_the_input()
    {
        var problems = SummaryValidator.Validate(
            "[A1] was answered quickly. [A7] was a second alert later in the night.",
            [Answered]);

        Assert.Contains(problems, p => p.Contains("A7"));
    }

    [Fact(DisplayName = "REQ-AI-02 rejects an invented alert reference even without brackets")]
    [Trait("Requirement", "REQ-AI-02")]
    public void Rejects_an_invented_alert_reference_even_without_brackets()
    {
        var problems = SummaryValidator.Validate("[A1] was answered, and alert A4 was a false alarm.", [Answered]);

        Assert.Contains(problems, p => p.Contains("A4"));
    }

    [Fact(DisplayName = "REQ-AI-02 rejects a summary omitting an escalated alert")]
    [Trait("Requirement", "REQ-AI-02")]
    public void Rejects_a_summary_omitting_an_escalated_alert()
    {
        var problems = SummaryValidator.Validate("A quiet night: [A1] was answered at once.", [Answered, Escalated]);

        Assert.Contains(problems, p => p.Contains("A2"));
    }

    [Fact(DisplayName = "REQ-AI-02 rejects a summary omitting an alert that reached an emergency call")]
    [Trait("Requirement", "REQ-AI-02")]
    public void Rejects_a_summary_omitting_an_alert_that_reached_an_emergency_call()
    {
        // Escalation level 0, but a call was placed: still escalated.
        var problems = SummaryValidator.Validate("Nothing of note happened.", [Called]);

        Assert.Contains(problems, p => p.Contains("A3"));
    }

    [Fact(DisplayName = "REQ-AI-02 rejects an alert reference in a shift that had no alerts")]
    [Trait("Requirement", "REQ-AI-02")]
    public void Rejects_an_alert_reference_in_a_shift_that_had_no_alerts()
    {
        var problems = SummaryValidator.Validate("One alert, [A1], was answered quickly.", []);

        Assert.NotEmpty(problems);
    }

    [Fact(DisplayName = "REQ-AI-02 accepts a quiet shift's summary that cites nothing")]
    [Trait("Requirement", "REQ-AI-02")]
    public void Accepts_a_quiet_shifts_summary_that_cites_nothing()
    {
        Assert.Empty(SummaryValidator.Validate("No alerts were raised during the shift.", []));
    }

    [Fact(DisplayName = "REQ-AI-02 rejects an empty summary")]
    [Trait("Requirement", "REQ-AI-02")]
    public void Rejects_an_empty_summary()
    {
        Assert.NotEmpty(SummaryValidator.Validate("   ", []));
    }

    [Fact(DisplayName = "REQ-AI-02 rejects a summary longer than a handover note")]
    [Trait("Requirement", "REQ-AI-02")]
    public void Rejects_a_summary_longer_than_a_handover_note()
    {
        var rambling = "[A1] was answered. " + new string('x', SummaryValidator.MaxLength);

        Assert.NotEmpty(SummaryValidator.Validate(rambling, [Answered]));
    }
}
