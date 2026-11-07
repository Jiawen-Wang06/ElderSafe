import type { AlertSummary, SummaryBucket } from "@/lib/api";

export type SummaryPreset = "week" | "month" | "year";

/**
 * The [from, to) range a preset covers, trailing back from now.
 *
 * Stepped with the UTC setters rather than the local ones. The local
 * setters (setDate, setMonth, setFullYear) hold the wall-clock hour fixed
 * and let the UTC instant drift, so a range that crosses a daylight-saving
 * change comes out an hour short or long depending on the caregiver's time
 * zone. The UTC setters have no daylight saving to cross, so the same
 * preset always covers the same span regardless of when or where it is
 * computed.
 */
export function presetRange(preset: SummaryPreset, now: Date): { from: Date; to: Date } {
  const to = now;
  const from = new Date(to);

  if (preset === "week") {
    from.setUTCDate(from.getUTCDate() - 7);
  } else if (preset === "month") {
    from.setUTCMonth(from.getUTCMonth() - 1);
  } else {
    from.setUTCFullYear(from.getUTCFullYear() - 1);
  }

  return { from, to };
}

/** One bar's worth of counts for one alert kind, across all buckets. */
export interface ChartSeries {
  kind: string;
  values: number[];
  /**
   * How many of each bucket's `values` entry reached an emergency call
   * (REQ-FALL-10) — a subset of that bucket's total, never larger than it.
   */
  emergencyCalled: number[];
}

/** The bars, their labels, and the kinds actually worth charting. */
export interface ChartData {
  bucketLabels: string[];
  series: ChartSeries[];
}

/** A label for a bucket, shaped by the summary's granularity. */
function labelFor(bucket: SummaryBucket, granularity: AlertSummary["granularity"]): string {
  const start = new Date(bucket.periodStart);

  if (granularity === "month") {
    return start.toLocaleDateString(undefined, { month: "short", year: "2-digit" });
  }

  if (granularity === "week") {
    return start.toLocaleDateString(undefined, { month: "short", day: "numeric" });
  }

  return start.toLocaleDateString(undefined, { weekday: "short", day: "numeric" });
}

/**
 * Builds chart series from a summary.
 *
 * Only kinds with at least one nonzero bucket are charted, so a dashboard
 * with falls alone does not show two empty legend entries for wandering and
 * no-data alerts that have never happened. If every kind is zero throughout
 * — nothing to report — "Fall" is kept as the one series, since it is the
 * only kind the app actually raises today, and an empty chart with no
 * series at all would look broken rather than quiet.
 */
export function chartDataFor(summary: AlertSummary): ChartData {
  const bucketLabels = summary.buckets.map((bucket) => labelFor(bucket, summary.granularity));

  const allKinds = summary.buckets[0]?.counts.map((count) => count.kind) ?? [];

  const kindsWithData = allKinds.filter((kind) =>
    summary.buckets.some(
      (bucket) => (bucket.counts.find((c) => c.kind === kind)?.total ?? 0) > 0,
    ),
  );

  const kindsToChart = kindsWithData.length > 0 ? kindsWithData : allKinds.slice(0, 1);

  const series = kindsToChart.map((kind) => ({
    kind,
    values: summary.buckets.map(
      (bucket) => bucket.counts.find((c) => c.kind === kind)?.total ?? 0,
    ),
    emergencyCalled: summary.buckets.map(
      (bucket) => bucket.counts.find((c) => c.kind === kind)?.emergencyCalled ?? 0,
    ),
  }));

  return { bucketLabels, series };
}

/** Grand totals across every bucket and kind, for the stat row. */
export interface SummaryTotals {
  total: number;
  respondedInTime: number;
  escalated: number;
  emergencyCalled: number;
}

export function totalsFor(summary: AlertSummary): SummaryTotals {
  return summary.buckets
    .flatMap((bucket) => bucket.counts)
    .reduce<SummaryTotals>(
      (acc, count) => ({
        total: acc.total + count.total,
        respondedInTime: acc.respondedInTime + count.respondedInTime,
        escalated: acc.escalated + count.escalated,
        emergencyCalled: acc.emergencyCalled + count.emergencyCalled,
      }),
      { total: 0, respondedInTime: 0, escalated: 0, emergencyCalled: 0 },
    );
}

/**
 * Round tick values covering a count axis.
 *
 * Separate from trace.ts's gTicks, which steps in g-unit fractions: this
 * axis is a whole-number count, so the step is always a whole number, chosen
 * so there are never more than about five ticks.
 */
export function countTicks(maxCount: number): number[] {
  if (maxCount <= 0) {
    return [0];
  }

  const rawStep = maxCount / 4;
  const magnitude = Math.pow(10, Math.floor(Math.log10(rawStep)));
  const normalized = rawStep / magnitude;
  const niceNormalized = normalized <= 1 ? 1 : normalized <= 2 ? 2 : normalized <= 5 ? 5 : 10;
  const step = Math.max(1, Math.round(niceNormalized * magnitude));

  const ticks: number[] = [];
  for (let value = 0; value <= maxCount + 1e-9; value += step) {
    ticks.push(value);
  }

  if (ticks[ticks.length - 1] < maxCount) {
    ticks.push(ticks[ticks.length - 1] + step);
  }

  return ticks;
}
