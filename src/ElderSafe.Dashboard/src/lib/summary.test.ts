import { describe, expect, it } from "vitest";
import type { AlertSummary, SummaryBucket } from "@/lib/api";
import { chartDataFor, countTicks, presetRange, totalsFor } from "@/lib/summary";

function bucket(
  periodStart: string,
  periodEnd: string,
  counts: {
    kind: string;
    total: number;
    respondedInTime?: number;
    escalated?: number;
    emergencyCalled?: number;
  }[],
): SummaryBucket {
  return {
    periodStart,
    periodEnd,
    counts: counts.map((c) => ({
      kind: c.kind,
      total: c.total,
      respondedInTime: c.respondedInTime ?? 0,
      escalated: c.escalated ?? 0,
      emergencyCalled: c.emergencyCalled ?? 0,
    })),
  };
}

function summary(buckets: SummaryBucket[], granularity: AlertSummary["granularity"] = "day"): AlertSummary {
  return {
    residentId: "r1",
    residentDisplayName: "Test Resident",
    from: buckets[0]?.periodStart ?? "2026-11-01T00:00:00Z",
    to: buckets[buckets.length - 1]?.periodEnd ?? "2026-11-02T00:00:00Z",
    granularity,
    buckets,
    alerts: [],
  };
}

describe("presetRange", () => {
  it("covers the trailing week", () => {
    const now = new Date("2026-11-08T00:00:00Z");
    const { from, to } = presetRange("week", now);

    expect(to).toEqual(now);
    expect(from.toISOString()).toBe("2026-11-01T00:00:00.000Z");
  });

  it("covers the trailing month", () => {
    const now = new Date("2026-11-08T00:00:00Z");
    const { from, to } = presetRange("month", now);

    expect(to).toEqual(now);
    expect(from.toISOString()).toBe("2026-10-08T00:00:00.000Z");
  });

  it("covers the trailing year", () => {
    const now = new Date("2026-11-08T00:00:00Z");
    const { from, to } = presetRange("year", now);

    expect(to).toEqual(now);
    expect(from.toISOString()).toBe("2025-11-08T00:00:00.000Z");
  });
});

describe("chartDataFor", () => {
  it("includes only kinds with at least one nonzero bucket", () => {
    const s = summary([
      bucket("2026-11-01T00:00:00Z", "2026-11-02T00:00:00Z", [
        { kind: "Fall", total: 2 },
        { kind: "Wander", total: 0 },
        { kind: "NoData", total: 0 },
      ]),
    ]);

    const data = chartDataFor(s);

    expect(data.series.map((series) => series.kind)).toEqual(["Fall"]);
  });

  it("falls back to the first kind when every bucket is all zero", () => {
    const s = summary([
      bucket("2026-11-01T00:00:00Z", "2026-11-02T00:00:00Z", [
        { kind: "Fall", total: 0 },
        { kind: "Wander", total: 0 },
      ]),
    ]);

    const data = chartDataFor(s);

    expect(data.series.map((series) => series.kind)).toEqual(["Fall"]);
    expect(data.series[0].values).toEqual([0]);
  });

  it("builds one label per bucket and one value per bucket per series", () => {
    const s = summary(
      [
        bucket("2026-11-01T00:00:00Z", "2026-11-02T00:00:00Z", [{ kind: "Fall", total: 1 }]),
        bucket("2026-11-02T00:00:00Z", "2026-11-03T00:00:00Z", [{ kind: "Fall", total: 3 }]),
      ],
      "day",
    );

    const data = chartDataFor(s);

    expect(data.bucketLabels).toHaveLength(2);
    expect(data.series[0].values).toEqual([1, 3]);
  });

  it("carries each bucket's emergency-called count alongside its total", () => {
    const s = summary([
      bucket("2026-11-01T00:00:00Z", "2026-11-02T00:00:00Z", [
        { kind: "Fall", total: 3, emergencyCalled: 1 },
      ]),
    ]);

    const data = chartDataFor(s);

    expect(data.series[0].emergencyCalled).toEqual([1]);
  });

  it("returns no series for an empty summary", () => {
    const s = summary([]);

    const data = chartDataFor(s);

    expect(data.series).toEqual([]);
    expect(data.bucketLabels).toEqual([]);
  });
});

describe("totalsFor", () => {
  it("sums totals, responded-in-time counts, and escalations across buckets and kinds", () => {
    const s = summary([
      bucket("2026-11-01T00:00:00Z", "2026-11-02T00:00:00Z", [
        { kind: "Fall", total: 2, respondedInTime: 1, escalated: 1, emergencyCalled: 1 },
        { kind: "Wander", total: 1, respondedInTime: 1, escalated: 0 },
      ]),
      bucket("2026-11-02T00:00:00Z", "2026-11-03T00:00:00Z", [
        { kind: "Fall", total: 1, respondedInTime: 0, escalated: 1 },
      ]),
    ]);

    expect(totalsFor(s)).toEqual({ total: 4, respondedInTime: 2, escalated: 2, emergencyCalled: 1 });
  });

  it("returns zero totals for an empty summary", () => {
    expect(totalsFor(summary([]))).toEqual({
      total: 0,
      respondedInTime: 0,
      escalated: 0,
      emergencyCalled: 0,
    });
  });
});

describe("countTicks", () => {
  it("returns a single zero tick when the max count is zero", () => {
    expect(countTicks(0)).toEqual([0]);
  });

  it("produces whole-number steps that cover the max count", () => {
    const ticks = countTicks(9);

    expect(ticks.every((tick) => Number.isInteger(tick))).toBe(true);
    expect(ticks[ticks.length - 1]).toBeGreaterThanOrEqual(9);
    expect(ticks[0]).toBe(0);
  });

  it("never produces more than about five ticks for a large max", () => {
    const ticks = countTicks(1000);

    expect(ticks.length).toBeLessThanOrEqual(6);
  });
});
