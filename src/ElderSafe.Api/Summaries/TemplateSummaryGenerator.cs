using System.Text;
using ElderSafe.Api.Alerting;

namespace ElderSafe.Api.Summaries;

/// <summary>
/// Writes a shift summary from fixed sentences, with no model and no network.
/// </summary>
/// <remarks>
/// The default when no model is configured, the fallback when the model is
/// unavailable, and what CI exercises. It cites every alert, not only the
/// escalated ones, so its output always passes <see cref="SummaryValidator"/>.
/// It says "alert", never "fall": the system knows what its detector
/// reported, not what happened to the resident.
/// </remarks>
public sealed class TemplateSummaryGenerator : ISummaryGenerator
{
    /// <summary>The name shown to the caregiver as the summary's author.</summary>
    public const string Name = "Template (no AI)";

    public Task<SummaryDraft> GenerateAsync(ShiftSummaryInput input, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(input);

        return Task.FromResult(new SummaryDraft(Write(input), Name));
    }

    private static string Write(ShiftSummaryInput input)
    {
        var shift = $"between {input.DayAndClock(input.ShiftStart)} and {input.DayAndClock(input.ShiftEnd)}";

        if (input.Alerts.Count == 0)
        {
            return $"No alerts were raised {shift}.";
        }

        var text = new StringBuilder();
        var escalated = input.Alerts.Count(a => a.Escalated);

        text.Append(input.Alerts.Count == 1 ? "1 alert was" : $"{input.Alerts.Count} alerts were")
            .Append($" raised {shift}");
        text.Append(escalated switch
        {
            0 => ".",
            1 => ", and 1 escalated.",
            _ => $", and {escalated} escalated."
        });

        foreach (var alert in input.Alerts.OrderBy(a => a.RaisedAt))
        {
            text.Append(' ').Append(Describe(alert, input));
        }

        return text.ToString();
    }

    private static string Describe(ShiftEvent alert, ShiftSummaryInput input)
    {
        var kind = alert.Kind == AlertKind.Fall ? "Fall alert" : $"{alert.Kind} alert";
        var opening = $"[{alert.Ref}] {kind} at {input.Clock(alert.RaisedAt)}";
        var minutes = $"{input.ResponseTimeout.TotalMinutes:0}-minute";

        if (!alert.Escalated)
        {
            return alert.AcknowledgedAt is { } acknowledged
                ? $"{opening}: acknowledged at {input.Clock(acknowledged)}, within the {minutes} response window."
                : $"{opening}: not yet acknowledged.";
        }

        // Level 0 with a call happens when a resident has no caregiver to
        // escalate to: the chain is exhausted before it starts.
        var escalation = alert.EscalationLevel switch
        {
            0 => string.Empty,
            1 => ", so it escalated once",
            _ => $", so it escalated {alert.EscalationLevel} times"
        };
        var call = alert.EmergencyCallAt is { } calledAt
            ? $"{(alert.EscalationLevel == 0 ? ", so" : ", and")} an emergency call was placed at {input.Clock(calledAt)}"
            : string.Empty;
        var outcome = alert.AcknowledgedAt is { } acknowledgedAt
            ? $"It was acknowledged at {input.Clock(acknowledgedAt)}."
            : "It has not been acknowledged.";

        return $"{opening}: not acknowledged within the {minutes} response window{escalation}{call}. {outcome}";
    }
}
