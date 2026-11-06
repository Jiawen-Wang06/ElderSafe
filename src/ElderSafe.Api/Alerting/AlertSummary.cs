namespace ElderSafe.Api.Alerting;

/// <summary>
/// How many alerts of one kind fell into one bucket, and how they resolved.
/// </summary>
/// <remarks>
/// <see cref="RespondedInTime"/> and <see cref="Escalated"/> are not
/// complementary, so they do not have to add up to <see cref="Total"/>. An
/// alert that is still open and still inside its response window when the
/// range was queried is neither: it has not been answered, but it also has
/// not yet failed to be. Collapsing that into one of the two counts would
/// state something about an outcome that has not happened yet.
/// </remarks>
public sealed record AlertKindCount(string Kind, int Total, int RespondedInTime, int Escalated);

/// <summary>One period of a summary, such as one day of a week-long view.</summary>
/// <remarks>
/// Always carries one entry per <see cref="AlertKind"/>, in the enum's
/// declared order, even when a kind had no alerts in this bucket — a kind
/// with nothing to report gets a zero rather than being left out. A chart
/// built from a variable set of keys per bucket would need to reconcile that
/// on the client; a fixed shape does not.
/// </remarks>
public sealed record SummaryBucket(
    DateTimeOffset PeriodStart,
    DateTimeOffset PeriodEnd,
    IReadOnlyList<AlertKindCount> Counts);

/// <summary>
/// Alert counts for one resident over a date range (REQ-FALL-09).
/// </summary>
/// <remarks>
/// Named and described as alerts raised, deliberately never as falls that
/// happened. The system has no way to know the second number — only what its
/// own detector reported — and a screen that presented the first as if it
/// were the second would state detector output as a clinical fact to a
/// caregiver or family member who may not be positioned to tell the
/// difference.
/// </remarks>
public sealed record AlertSummary(
    Guid ResidentId,
    string ResidentDisplayName,
    DateTimeOffset From,
    DateTimeOffset To,
    string Granularity,
    IReadOnlyList<SummaryBucket> Buckets);
