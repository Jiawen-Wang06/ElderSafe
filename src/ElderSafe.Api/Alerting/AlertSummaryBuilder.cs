namespace ElderSafe.Api.Alerting;

/// <summary>
/// Buckets a resident's alerts into periods and counts them (REQ-FALL-09).
/// </summary>
/// <remarks>
/// Pure, and given its input rather than a database connection, so the rules
/// — what period length a range gets, which bucket an alert at a period
/// boundary falls into, how an alert that escalated and was later
/// acknowledged is counted — can be driven directly by a test rather than
/// through a query provider. The aggregation runs in memory rather than as a
/// SQL <c>GROUP BY</c> because the volume here, one resident's alerts over at
/// most a year, never approaches the size where that would matter.
/// </remarks>
public static class AlertSummaryBuilder
{
    /// <summary>
    /// Picks a bucket size from the width of the range.
    /// </summary>
    /// <remarks>
    /// A week or a month of daily bars is legible; a year of daily bars is
    /// 365 bars nobody can read, so the bucket widens as the range does. The
    /// thresholds land on the natural presets: a week and a month both fall
    /// under 31 days and get daily bars, a year gets monthly ones, and
    /// anything in between — a custom range a caregiver picked by hand — gets
    /// weekly ones rather than snapping straight from day to month.
    /// </remarks>
    public static string GranularityFor(DateTimeOffset from, DateTimeOffset to)
    {
        var span = to - from;

        if (span <= TimeSpan.FromDays(31))
        {
            return "day";
        }

        if (span <= TimeSpan.FromDays(92))
        {
            return "week";
        }

        return "month";
    }

    /// <summary>
    /// Builds the buckets for a range.
    /// </summary>
    /// <remarks>
    /// Tolerates <paramref name="from"/> not being earlier than
    /// <paramref name="to"/> by returning no buckets rather than throwing:
    /// validating that the range makes sense is the caller's job, and a
    /// request the caller already rejected should never reach here in
    /// practice, but a pure function should not crash on a degenerate input
    /// it can answer safely.
    /// </remarks>
    public static IReadOnlyList<SummaryBucket> Build(
        IReadOnlyList<AlertSnapshot> alerts,
        DateTimeOffset from,
        DateTimeOffset to,
        string granularity,
        TimeSpan responseTimeout)
    {
        return PeriodsBetween(from, to, granularity)
            .Select(period => BuildBucket(alerts, period.Start, period.End, responseTimeout))
            .ToList();
    }

    private static IEnumerable<(DateTimeOffset Start, DateTimeOffset End)> PeriodsBetween(
        DateTimeOffset from,
        DateTimeOffset to,
        string granularity)
    {
        Func<DateTimeOffset, DateTimeOffset> step = granularity switch
        {
            "day" => d => d.AddDays(1),
            "week" => d => d.AddDays(7),
            "month" => d => d.AddMonths(1),
            _ => throw new ArgumentOutOfRangeException(
                nameof(granularity), granularity, "Expected 'day', 'week', or 'month'.")
        };

        var cursor = from;

        // The final period is clipped to "to" rather than running past it, so
        // a range that does not divide evenly (a 10-day custom range at daily
        // granularity) still ends exactly where it was asked to, with a
        // shorter last bucket rather than one that overshoots.
        while (cursor < to)
        {
            var next = step(cursor);
            var end = next < to ? next : to;

            yield return (cursor, end);

            cursor = next;
        }
    }

    private static SummaryBucket BuildBucket(
        IReadOnlyList<AlertSnapshot> alerts,
        DateTimeOffset start,
        DateTimeOffset end,
        TimeSpan responseTimeout)
    {
        // Half-open: an alert raised exactly at a boundary belongs to the
        // period it opens, not the one it closes, so no alert is double
        // counted and none falls through a gap between two adjacent buckets.
        var inPeriod = alerts.Where(a => a.RaisedAt >= start && a.RaisedAt < end).ToList();

        var counts = Enum.GetValues<AlertKind>()
            .Select(kind =>
            {
                var ofKind = inPeriod.Where(a => a.Kind == kind).ToList();

                var respondedInTime = ofKind.Count(a =>
                    a.AcknowledgedAt is not null
                    && a.EscalationLevel == 0
                    && a.AcknowledgedAt.Value - a.RaisedAt <= responseTimeout);

                // Escalated looks at EscalationLevel rather than at whether
                // the alert is still open, so an alert that moved to a second
                // caregiver and was then acknowledged still counts as having
                // escalated. The count describes what happened to the alert,
                // not just where it ended up.
                var escalated = ofKind.Count(a => a.EscalationLevel > 0);

                return new AlertKindCount(kind.ToString(), ofKind.Count, respondedInTime, escalated);
            })
            .ToList();

        return new SummaryBucket(start, end, counts);
    }
}
