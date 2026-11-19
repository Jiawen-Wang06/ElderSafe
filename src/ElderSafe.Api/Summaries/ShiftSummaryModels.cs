using System.Globalization;
using ElderSafe.Api.Alerting;

namespace ElderSafe.Api.Summaries;

/// <summary>
/// One stored alert, as a summary generator sees it.
/// </summary>
/// <remarks>
/// Deliberately carries no names and no identifiers that mean anything
/// outside this summary. <see cref="Ref"/> is a short label such as
/// <c>A1</c>, which a language model copies reliably and a validator can
/// check; the alert's real ID stays on the server. Nothing here identifies
/// the resident or the caregivers, so nothing identifying is sent to an
/// external model.
/// </remarks>
public sealed record ShiftEvent(
    string Ref,
    AlertKind Kind,
    DateTimeOffset RaisedAt,
    DateTimeOffset? AcknowledgedAt,
    int EscalationLevel,
    DateTimeOffset? EmergencyCallAt)
{
    /// <summary>
    /// Escalated past the first caregiver, or all the way to an emergency
    /// call. These are the alerts a summary must never leave out (REQ-AI-01).
    /// </summary>
    public bool Escalated => EscalationLevel > 0 || EmergencyCallAt is not null;
}

/// <summary>
/// Everything a generator is given: the shift and its alerts, nothing else.
/// </summary>
public sealed record ShiftSummaryInput(
    DateTimeOffset ShiftStart,
    DateTimeOffset ShiftEnd,
    TimeZoneInfo TimeZone,
    TimeSpan ResponseTimeout,
    IReadOnlyList<ShiftEvent> Alerts)
{
    /// <summary>A time as the caregiver reads it, e.g. <c>03:12</c>.</summary>
    public string Clock(DateTimeOffset at) =>
        TimeZoneInfo.ConvertTime(at, TimeZone).ToString("HH:mm", CultureInfo.InvariantCulture);

    /// <summary>A time with its weekday, for the shift's bounds, e.g. <c>Tue 19:00</c>.</summary>
    public string DayAndClock(DateTimeOffset at) =>
        TimeZoneInfo.ConvertTime(at, TimeZone).ToString("ddd HH:mm", CultureInfo.InvariantCulture);
}

/// <summary>A summary as a generator wrote it, before validation.</summary>
/// <param name="Text">The summary.</param>
/// <param name="Generator">Who wrote it, including the model version where there is one.</param>
public sealed record SummaryDraft(string Text, string Generator);

/// <summary>Whether a summary can be shown.</summary>
public enum ShiftSummaryStatus
{
    /// <summary>Written and validated against the stored alerts.</summary>
    Generated,

    /// <summary>
    /// Written, but failed validation, so it is withheld and the caregiver
    /// is shown the stored alerts instead (REQ-AI-02).
    /// </summary>
    Rejected
}

/// <summary>The body of a shift-summary request.</summary>
/// <param name="TimeZone">
/// The caregiver's IANA time zone, such as <c>America/Toronto</c>, so times
/// in the summary read as the caregiver's clock. Unknown or missing means UTC.
/// </param>
public sealed record ShiftSummaryRequest(string? TimeZone);

/// <summary>One alert behind a summary, as the dashboard lists it.</summary>
/// <remarks>
/// Unlike <see cref="ShiftEvent"/>, this carries the alert's ID and the
/// acknowledging caregiver's name. It goes only to a caregiver already
/// assigned to the resident, never to the model.
/// </remarks>
public sealed record ShiftSummaryAlert(
    string Ref,
    Guid Id,
    string Kind,
    DateTimeOffset RaisedAt,
    DateTimeOffset? AcknowledgedAt,
    string? AcknowledgedByDisplayName,
    int EscalationLevel,
    bool Escalated,
    bool EmergencyCalled);

/// <summary>
/// A shift summary, with the stored alerts it was built from (REQ-AI-01,
/// REQ-AI-02).
/// </summary>
/// <param name="Text">The summary, or null when it was rejected.</param>
/// <param name="Generator">Who wrote the text, shown to the caregiver.</param>
/// <param name="Notice">Why the template was used instead of the model, if it was.</param>
/// <param name="Problems">Why the summary was rejected. Empty when it was not.</param>
/// <param name="Alerts">The stored alerts, always present, so they can be checked against the text.</param>
public sealed record ShiftSummaryView(
    Guid ResidentId,
    DateTimeOffset From,
    DateTimeOffset To,
    ShiftSummaryStatus Status,
    string? Text,
    string Generator,
    string? Notice,
    IReadOnlyList<string> Problems,
    IReadOnlyList<ShiftSummaryAlert> Alerts);
