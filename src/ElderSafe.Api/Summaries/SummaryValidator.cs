using System.Text.RegularExpressions;

namespace ElderSafe.Api.Summaries;

/// <summary>
/// Checks a generated summary against the alerts it was written from
/// (REQ-AI-01, REQ-AI-02).
/// </summary>
/// <remarks>
/// <para>
/// Model output is treated as untrusted input. A summary that invents an
/// alert, or leaves out one that escalated, gives the incoming shift a wrong
/// picture of the night (RISK-13), so it is rejected rather than shown.
/// </para>
/// <para>
/// A pure function over the text and the alerts, with no model and no I/O,
/// so it is tested in CI for free and runs on every summary, whichever
/// generator wrote it.
/// </para>
/// <para>
/// What it cannot catch: a summary that cites only real alerts but describes
/// them wrongly, such as an escalated alert called "answered promptly". That
/// residual risk is recorded against RISK-13, and is why the dashboard
/// always shows the stored alerts under the text.
/// </para>
/// </remarks>
public static partial class SummaryValidator
{
    /// <summary>Longest summary accepted. A handover note is a few sentences.</summary>
    public const int MaxLength = 1500;

    /// <summary>
    /// Anything shaped like an alert reference, bracketed or not. Matching
    /// the bare form too means "alert A7" cannot slip past just because the
    /// model forgot the brackets.
    /// </summary>
    [GeneratedRegex(@"\bA\d+\b")]
    private static partial Regex Reference();

    /// <summary>
    /// The problems found. An empty list means the summary may be shown.
    /// </summary>
    public static IReadOnlyList<string> Validate(string? text, IReadOnlyList<ShiftEvent> alerts)
    {
        ArgumentNullException.ThrowIfNull(alerts);

        if (string.IsNullOrWhiteSpace(text))
        {
            return ["The summary is empty."];
        }

        var problems = new List<string>();

        if (text.Length > MaxLength)
        {
            problems.Add($"The summary is {text.Length} characters long; the limit is {MaxLength}.");
        }

        var known = alerts.Select(a => a.Ref).ToHashSet(StringComparer.Ordinal);
        var cited = Reference().Matches(text).Select(m => m.Value).ToHashSet(StringComparer.Ordinal);

        foreach (var reference in cited.Where(r => !known.Contains(r)).Order(StringComparer.Ordinal))
        {
            problems.Add($"It cites {reference}, which is not one of this shift's alerts.");
        }

        foreach (var alert in alerts.Where(a => a.Escalated && !cited.Contains(a.Ref)))
        {
            problems.Add($"It does not mention {alert.Ref}, which escalated.");
        }

        return problems;
    }
}
