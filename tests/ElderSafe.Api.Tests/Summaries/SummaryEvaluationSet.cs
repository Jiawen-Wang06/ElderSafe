using ElderSafe.Api.Alerting;
using ElderSafe.Api.Summaries;

namespace ElderSafe.Api.Tests.Summaries;

/// <summary>
/// The evaluation set REQ-AI-01 refers to: shifts covering each way an alert
/// can end, which every generator's output must survive validation on.
/// </summary>
/// <remarks>
/// The template runs over these in CI. A real model is run over the same
/// shapes by hand, through the API, with the results recorded in ADR-005.
/// </remarks>
public static class SummaryEvaluationSet
{
    public static readonly DateTimeOffset ShiftEnd = new(2026, 11, 18, 7, 0, 0, TimeSpan.Zero);

    public static readonly DateTimeOffset ShiftStart = ShiftEnd.AddHours(-12);

    private static DateTimeOffset At(int hour, int minute) =>
        new DateTimeOffset(2026, 11, 18, hour, minute, 0, TimeSpan.Zero).AddDays(hour >= 19 ? -1 : 0);

    public static ShiftSummaryInput Input(params ShiftEvent[] alerts) =>
        new(ShiftStart, ShiftEnd, TimeZoneInfo.Utc, TimeSpan.FromMinutes(2), alerts);

    public static TheoryData<string> Names => new(Scenarios.Keys);

    public static IReadOnlyDictionary<string, ShiftSummaryInput> Scenarios { get; } =
        new Dictionary<string, ShiftSummaryInput>
        {
            ["a quiet shift"] = Input(),

            ["one alert answered in time"] = Input(
                new ShiftEvent("A1", AlertKind.Fall, At(3, 12), At(3, 13), 0, null)),

            ["one alert that escalated, then was acknowledged"] = Input(
                new ShiftEvent("A1", AlertKind.Fall, At(5, 40), At(5, 44), 1, null)),

            ["one alert that reached an emergency call"] = Input(
                new ShiftEvent("A1", AlertKind.Fall, At(1, 5), null, 2, At(1, 11))),

            ["an emergency call with no caregiver to escalate to"] = Input(
                new ShiftEvent("A1", AlertKind.Fall, At(23, 30), null, 0, At(23, 32))),

            ["one alert still open"] = Input(
                new ShiftEvent("A1", AlertKind.Fall, At(6, 50), null, 0, null)),

            ["a busy shift with every outcome"] = Input(
                new ShiftEvent("A1", AlertKind.Fall, At(20, 2), At(20, 3), 0, null),
                new ShiftEvent("A2", AlertKind.Fall, At(22, 47), At(22, 53), 1, null),
                new ShiftEvent("A3", AlertKind.Fall, At(2, 15), null, 2, At(2, 21)),
                new ShiftEvent("A4", AlertKind.Fall, At(4, 0), At(4, 1), 0, null),
                new ShiftEvent("A5", AlertKind.Fall, At(6, 58), null, 0, null))
        };
}
