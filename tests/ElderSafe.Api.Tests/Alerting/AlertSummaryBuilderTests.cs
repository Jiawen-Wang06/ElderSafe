using ElderSafe.Api.Alerting;

namespace ElderSafe.Api.Tests.Alerting;

public class AlertSummaryBuilderTests
{
    private static readonly Guid ResidentId = Guid.NewGuid();
    private static readonly TimeSpan Timeout = TimeSpan.FromMinutes(2);

    private static AlertSnapshot Alert(
        DateTimeOffset raisedAt,
        AlertKind kind = AlertKind.Fall,
        int escalationLevel = 0,
        DateTimeOffset? acknowledgedAt = null)
    {
        return new AlertSnapshot(
            Guid.NewGuid(), ResidentId, kind, raisedAt, raisedAt, escalationLevel, acknowledgedAt);
    }

    [Fact(DisplayName = "REQ-FALL-09 a week-long range buckets by day")]
    [Trait("Requirement", "REQ-FALL-09")]
    public void A_week_long_range_buckets_by_day()
    {
        var from = new DateTimeOffset(2026, 11, 1, 0, 0, 0, TimeSpan.Zero);
        var to = from.AddDays(7);

        Assert.Equal("day", AlertSummaryBuilder.GranularityFor(from, to));

        var buckets = AlertSummaryBuilder.Build([], from, to, "day", Timeout);

        Assert.Equal(7, buckets.Count);
        Assert.Equal(from, buckets[0].PeriodStart);
        Assert.Equal(to, buckets[^1].PeriodEnd);
    }

    [Fact(DisplayName = "REQ-FALL-09 a year-long range buckets by month")]
    [Trait("Requirement", "REQ-FALL-09")]
    public void A_year_long_range_buckets_by_month()
    {
        var from = new DateTimeOffset(2026, 1, 1, 0, 0, 0, TimeSpan.Zero);
        var to = from.AddYears(1);

        Assert.Equal("month", AlertSummaryBuilder.GranularityFor(from, to));

        var buckets = AlertSummaryBuilder.Build([], from, to, "month", Timeout);

        Assert.Equal(12, buckets.Count);
    }

    [Fact(DisplayName = "REQ-FALL-09 a three-month custom range buckets by week")]
    [Trait("Requirement", "REQ-FALL-09")]
    public void A_three_month_custom_range_buckets_by_week()
    {
        var from = new DateTimeOffset(2026, 1, 1, 0, 0, 0, TimeSpan.Zero);
        var to = from.AddDays(90);

        Assert.Equal("week", AlertSummaryBuilder.GranularityFor(from, to));
    }

    [Fact(DisplayName = "REQ-FALL-09 the final bucket is clipped rather than overshooting the range")]
    [Trait("Requirement", "REQ-FALL-09")]
    public void The_final_bucket_is_clipped_rather_than_overshooting_the_range()
    {
        var from = new DateTimeOffset(2026, 11, 1, 0, 0, 0, TimeSpan.Zero);
        var to = from.AddDays(10).AddHours(5);

        var buckets = AlertSummaryBuilder.Build([], from, to, "day", Timeout);

        Assert.Equal(11, buckets.Count);
        Assert.Equal(to, buckets[^1].PeriodEnd);
        Assert.True(buckets[^1].PeriodEnd - buckets[^1].PeriodStart < TimeSpan.FromDays(1));
    }

    [Fact(DisplayName = "REQ-FALL-09 an alert at a bucket boundary belongs to the later bucket")]
    [Trait("Requirement", "REQ-FALL-09")]
    public void An_alert_at_a_bucket_boundary_belongs_to_the_later_bucket()
    {
        var from = new DateTimeOffset(2026, 11, 1, 0, 0, 0, TimeSpan.Zero);
        var to = from.AddDays(2);
        var boundary = from.AddDays(1);

        var alerts = new[] { Alert(boundary) };

        var buckets = AlertSummaryBuilder.Build(alerts, from, to, "day", Timeout);

        Assert.All(buckets[0].Counts, count => Assert.Equal(0, count.Total));
        Assert.Equal(1, buckets[1].Counts.Single(c => c.Kind == "Fall").Total);
    }

    [Fact(DisplayName = "REQ-FALL-09 every bucket carries an entry for every kind, even at zero")]
    [Trait("Requirement", "REQ-FALL-09")]
    public void Every_bucket_carries_an_entry_for_every_kind_even_at_zero()
    {
        var from = new DateTimeOffset(2026, 11, 1, 0, 0, 0, TimeSpan.Zero);
        var to = from.AddDays(1);

        var bucket = AlertSummaryBuilder.Build([], from, to, "day", Timeout).Single();

        Assert.Equal(Enum.GetValues<AlertKind>().Length, bucket.Counts.Count);
        Assert.All(bucket.Counts, count => Assert.Equal(0, count.Total));
    }

    [Fact(DisplayName = "REQ-FALL-09 counts are broken down by kind")]
    [Trait("Requirement", "REQ-FALL-09")]
    public void Counts_are_broken_down_by_kind()
    {
        var from = new DateTimeOffset(2026, 11, 1, 0, 0, 0, TimeSpan.Zero);
        var to = from.AddDays(1);

        var alerts = new[]
        {
            Alert(from.AddHours(1), AlertKind.Fall),
            Alert(from.AddHours(2), AlertKind.Fall),
            Alert(from.AddHours(3), AlertKind.NoData)
        };

        var bucket = AlertSummaryBuilder.Build(alerts, from, to, "day", Timeout).Single();

        Assert.Equal(2, bucket.Counts.Single(c => c.Kind == "Fall").Total);
        Assert.Equal(1, bucket.Counts.Single(c => c.Kind == "NoData").Total);
        Assert.Equal(0, bucket.Counts.Single(c => c.Kind == "Wander").Total);
    }

    [Fact(DisplayName = "REQ-FALL-09 an alert acknowledged in time at level zero is responded in time")]
    [Trait("Requirement", "REQ-FALL-09")]
    public void An_alert_acknowledged_in_time_at_level_zero_is_responded_in_time()
    {
        var from = new DateTimeOffset(2026, 11, 1, 0, 0, 0, TimeSpan.Zero);
        var to = from.AddDays(1);
        var raisedAt = from.AddHours(1);

        var alerts = new[] { Alert(raisedAt, escalationLevel: 0, acknowledgedAt: raisedAt.AddSeconds(30)) };

        var count = AlertSummaryBuilder.Build(alerts, from, to, "day", Timeout).Single().Counts
            .Single(c => c.Kind == "Fall");

        Assert.Equal(1, count.Total);
        Assert.Equal(1, count.RespondedInTime);
        Assert.Equal(0, count.Escalated);
    }

    [Fact(DisplayName = "REQ-FALL-09 an alert acknowledged after the timeout is not responded in time")]
    [Trait("Requirement", "REQ-FALL-09")]
    public void An_alert_acknowledged_after_the_timeout_is_not_responded_in_time()
    {
        var from = new DateTimeOffset(2026, 11, 1, 0, 0, 0, TimeSpan.Zero);
        var to = from.AddDays(1);
        var raisedAt = from.AddHours(1);

        var alerts = new[]
        {
            Alert(raisedAt, escalationLevel: 0, acknowledgedAt: raisedAt + Timeout + TimeSpan.FromSeconds(1))
        };

        var count = AlertSummaryBuilder.Build(alerts, from, to, "day", Timeout).Single().Counts
            .Single(c => c.Kind == "Fall");

        Assert.Equal(0, count.RespondedInTime);
    }

    [Fact(DisplayName = "REQ-FALL-09 an alert that escalated is still counted as escalated once acknowledged")]
    [Trait("Requirement", "REQ-FALL-09")]
    public void An_alert_that_escalated_is_still_counted_as_escalated_once_acknowledged()
    {
        var from = new DateTimeOffset(2026, 11, 1, 0, 0, 0, TimeSpan.Zero);
        var to = from.AddDays(1);
        var raisedAt = from.AddHours(1);

        var alerts = new[]
        {
            Alert(raisedAt, escalationLevel: 1, acknowledgedAt: raisedAt.AddMinutes(3))
        };

        var count = AlertSummaryBuilder.Build(alerts, from, to, "day", Timeout).Single().Counts
            .Single(c => c.Kind == "Fall");

        Assert.Equal(1, count.Escalated);
        Assert.Equal(0, count.RespondedInTime);
    }

    [Fact(DisplayName = "REQ-FALL-09 an alert still open within its timeout is neither responded nor escalated")]
    [Trait("Requirement", "REQ-FALL-09")]
    public void An_alert_still_open_within_its_timeout_is_neither_responded_nor_escalated()
    {
        var from = new DateTimeOffset(2026, 11, 1, 0, 0, 0, TimeSpan.Zero);
        var to = from.AddDays(1);

        var alerts = new[] { Alert(from.AddHours(1), escalationLevel: 0, acknowledgedAt: null) };

        var count = AlertSummaryBuilder.Build(alerts, from, to, "day", Timeout).Single().Counts
            .Single(c => c.Kind == "Fall");

        Assert.Equal(1, count.Total);
        Assert.Equal(0, count.RespondedInTime);
        Assert.Equal(0, count.Escalated);
    }

    [Fact(DisplayName = "REQ-FALL-09 an alert outside the requested range is excluded")]
    [Trait("Requirement", "REQ-FALL-09")]
    public void An_alert_outside_the_requested_range_is_excluded()
    {
        var from = new DateTimeOffset(2026, 11, 1, 0, 0, 0, TimeSpan.Zero);
        var to = from.AddDays(1);

        var alerts = new[] { Alert(from.AddDays(-1)), Alert(to.AddHours(1)) };

        var bucket = AlertSummaryBuilder.Build(alerts, from, to, "day", Timeout).Single();

        Assert.All(bucket.Counts, count => Assert.Equal(0, count.Total));
    }

    [Fact(DisplayName = "REQ-FALL-09 a range where 'from' is not before 'to' produces no buckets")]
    [Trait("Requirement", "REQ-FALL-09")]
    public void A_range_where_from_is_not_before_to_produces_no_buckets()
    {
        var from = new DateTimeOffset(2026, 11, 1, 0, 0, 0, TimeSpan.Zero);

        var buckets = AlertSummaryBuilder.Build([], from, from, "day", Timeout);

        Assert.Empty(buckets);
    }
}
