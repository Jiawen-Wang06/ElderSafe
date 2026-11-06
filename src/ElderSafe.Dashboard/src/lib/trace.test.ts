import { describe, expect, it } from "vitest";
import {
  findingsFor,
  firstAtOrAbove,
  firstAtOrBelow,
  gTicks,
  linearScale,
  nearestSample,
  offsetSeconds,
  peakOf,
  upperBoundG,
} from "@/lib/trace";
import type { AlertTrace, TraceSample } from "@/lib/api";

const raisedAt = "2026-11-06T12:00:04.000Z";

function sample(secondsFromRaised: number, magnitudeG: number): TraceSample {
  return {
    at: new Date(Date.parse(raisedAt) + secondsFromRaised * 1000).toISOString(),
    magnitudeG,
  };
}

function trace(samples: TraceSample[], overrides: Partial<AlertTrace> = {}): AlertTrace {
  return {
    alertId: "00000000-0000-0000-0000-000000000001",
    residentId: "11111111-1111-1111-1111-111111111111",
    residentDisplayName: "Resident A",
    kind: "Fall",
    raisedAt,
    windowStart: new Date(Date.parse(raisedAt) - 5500).toISOString(),
    windowEnd: new Date(Date.parse(raisedAt) + 1500).toISOString(),
    thresholds: {
      freeFallG: 0.6,
      impactG: 2.5,
      stillnessCentreG: 1,
      stillnessToleranceG: 0.25,
      settleAfterImpactSeconds: 0.5,
      stillnessDurationSeconds: 2,
    },
    samples,
    ...overrides,
  };
}

describe("offsetSeconds", () => {
  it("reports a sample before the alert as negative", () => {
    expect(offsetSeconds(sample(-3, 1), raisedAt)).toBe(-3);
  });

  it("reports a sample after the alert as positive", () => {
    expect(offsetSeconds(sample(1.5, 1), raisedAt)).toBe(1.5);
  });
});

describe("peakOf", () => {
  it("finds the largest magnitude", () => {
    expect(peakOf([sample(-1, 1), sample(0, 3.4), sample(1, 0.9)])?.magnitudeG).toBe(3.4);
  });

  it("returns null for an empty trace", () => {
    expect(peakOf([])).toBeNull();
  });

  it("keeps the earliest of two equal peaks", () => {
    const earlier = sample(-2, 2.5);

    expect(peakOf([earlier, sample(0, 2.5)])).toBe(earlier);
  });
});

describe("firstAtOrBelow and firstAtOrAbove", () => {
  it("treats a sample exactly on the threshold as a crossing", () => {
    expect(firstAtOrBelow([sample(0, 0.6)], 0.6)).not.toBeNull();
    expect(firstAtOrAbove([sample(0, 2.5)], 2.5)).not.toBeNull();
  });

  it("returns null when nothing crosses", () => {
    expect(firstAtOrBelow([sample(0, 1)], 0.6)).toBeNull();
    expect(firstAtOrAbove([sample(0, 1)], 2.5)).toBeNull();
  });
});

describe("findingsFor", () => {
  it("reports free fall followed by impact in the expected order", () => {
    const findings = findingsFor(
      trace([sample(-3, 1), sample(-2.5, 0.2), sample(-2.2, 3.1), sample(-1, 1.02)]),
    );

    expect(offsetSeconds(findings.freeFall!, raisedAt)).toBe(-2.5);
    expect(offsetSeconds(findings.impact!, raisedAt)).toBe(-2.2);
    expect(findings.inExpectedOrder).toBe(true);
  });

  it("ignores an impact that happened before the free fall", () => {
    const findings = findingsFor(
      trace([sample(-4, 3.2), sample(-2, 0.3), sample(-1, 1.01)]),
    );

    expect(offsetSeconds(findings.freeFall!, raisedAt)).toBe(-2);
    expect(findings.impact).toBeNull();
    expect(findings.inExpectedOrder).toBe(false);
  });

  it("does not claim an order when there was no free fall", () => {
    const findings = findingsFor(trace([sample(-2, 1), sample(-1, 2.8)]));

    expect(findings.freeFall).toBeNull();
    expect(findings.inExpectedOrder).toBe(false);
  });

  it("still reports the peak when no threshold was crossed", () => {
    const findings = findingsFor(trace([sample(-2, 1.1), sample(-1, 1.4)]));

    expect(findings.peak?.magnitudeG).toBe(1.4);
    expect(findings.inExpectedOrder).toBe(false);
  });
});

describe("linearScale", () => {
  it("maps the domain onto the range", () => {
    const scale = linearScale([0, 10], [0, 100]);

    expect(scale(0)).toBe(0);
    expect(scale(5)).toBe(50);
    expect(scale(10)).toBe(100);
  });

  it("supports an inverted range, as a y axis needs", () => {
    const scale = linearScale([0, 4], [200, 0]);

    expect(scale(0)).toBe(200);
    expect(scale(4)).toBe(0);
  });

  it("centres everything when the domain has no width", () => {
    const scale = linearScale([3, 3], [0, 100]);

    expect(scale(3)).toBe(50);
  });
});

describe("gTicks", () => {
  it("steps by half a g over a small range", () => {
    expect(gTicks(3)).toEqual([0, 0.5, 1, 1.5, 2, 2.5, 3]);
  });

  it("steps by one g over a wider range", () => {
    expect(gTicks(5)).toEqual([0, 1, 2, 3, 4, 5]);
  });

  it("includes the upper bound when it falls on a step", () => {
    expect(gTicks(2)).toContain(2);
  });
});

describe("upperBoundG", () => {
  it("never falls below the impact threshold", () => {
    expect(upperBoundG([sample(0, 1.05)], 2.5)).toBeGreaterThanOrEqual(2.5);
  });

  it("leaves headroom above a peak that exceeds the threshold", () => {
    expect(upperBoundG([sample(0, 4.1)], 2.5)).toBeGreaterThan(4.1);
  });

  it("handles an empty trace", () => {
    expect(upperBoundG([], 2.5)).toBeGreaterThanOrEqual(2.5);
  });
});

describe("nearestSample", () => {
  it("snaps to the closest sample", () => {
    const subject = trace([sample(-2, 1), sample(-1, 2), sample(0, 3)]);

    expect(nearestSample(subject, -1.1)?.index).toBe(1);
  });

  it("clamps to the ends", () => {
    const subject = trace([sample(-2, 1), sample(0, 3)]);

    expect(nearestSample(subject, -99)?.index).toBe(0);
    expect(nearestSample(subject, 99)?.index).toBe(1);
  });

  it("returns null for an empty trace", () => {
    expect(nearestSample(trace([]), 0)).toBeNull();
  });
});
