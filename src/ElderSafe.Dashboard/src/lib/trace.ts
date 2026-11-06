import type { AlertTrace, TraceSample } from "@/lib/api";

/** Seconds of a sample relative to the moment the alert was raised. */
export function offsetSeconds(sample: TraceSample, raisedAt: string): number {
  return (Date.parse(sample.at) - Date.parse(raisedAt)) / 1000;
}

/** The largest magnitude in the trace, or null for an empty trace. */
export function peakOf(samples: readonly TraceSample[]): TraceSample | null {
  return samples.reduce<TraceSample | null>(
    (highest, sample) =>
      highest === null || sample.magnitudeG > highest.magnitudeG ? sample : highest,
    null,
  );
}

/** The first sample at or below a threshold. */
export function firstAtOrBelow(
  samples: readonly TraceSample[],
  threshold: number,
): TraceSample | null {
  return samples.find((sample) => sample.magnitudeG <= threshold) ?? null;
}

/** The first sample at or above a threshold. */
export function firstAtOrAbove(
  samples: readonly TraceSample[],
  threshold: number,
): TraceSample | null {
  return samples.find((sample) => sample.magnitudeG >= threshold) ?? null;
}

/**
 * What the trace shows against the thresholds that were applied to it.
 *
 * This describes rather than decides. The detector has already run on the
 * server, and re-implementing its rules here would create a second opinion
 * that can disagree with the first — a chart confidently annotating "no
 * impact" on an alert the detector raised would be worse than no annotation.
 * So this reports the first crossing of each threshold in order and nothing
 * more: free fall must precede impact for the labels to describe one event,
 * and when they do not, both are still shown and neither is called a fall.
 */
export interface TraceFindings {
  peak: TraceSample | null;
  freeFall: TraceSample | null;
  impact: TraceSample | null;
  inExpectedOrder: boolean;
}

export function findingsFor(trace: AlertTrace): TraceFindings {
  const freeFall = firstAtOrBelow(trace.samples, trace.thresholds.freeFallG);

  // Looked for after free fall, not from the start: an impact spike before
  // the fall began belongs to whatever happened earlier, and pairing it with
  // this free fall would describe an event that did not happen.
  const afterFreeFall =
    freeFall === null
      ? []
      : trace.samples.filter((sample) => Date.parse(sample.at) > Date.parse(freeFall.at));

  const impact = firstAtOrAbove(afterFreeFall, trace.thresholds.impactG);

  return {
    peak: peakOf(trace.samples),
    freeFall,
    impact,
    inExpectedOrder: freeFall !== null && impact !== null,
  };
}

/** Maps a value in a domain onto a pixel range. */
export function linearScale(
  domain: readonly [number, number],
  range: readonly [number, number],
): (value: number) => number {
  const [domainStart, domainEnd] = domain;
  const [rangeStart, rangeEnd] = range;
  const span = domainEnd - domainStart;

  if (span === 0) {
    return () => (rangeStart + rangeEnd) / 2;
  }

  return (value) => rangeStart + ((value - domainStart) / span) * (rangeEnd - rangeStart);
}

/**
 * Round tick values covering a range.
 *
 * Deliberately simple: this axis is acceleration in g, which spans single
 * digits, so whole and half units are the only steps worth having. A general
 * nice-number algorithm would be more code for a range that never needs it.
 */
export function gTicks(maxG: number): number[] {
  const step = maxG > 6 ? 2 : maxG > 3 ? 1 : 0.5;
  const ticks: number[] = [];

  for (let value = 0; value <= maxG + 1e-9; value += step) {
    ticks.push(Number(value.toFixed(1)));
  }

  return ticks;
}

/**
 * The upper end of the y axis.
 *
 * Never below the impact threshold: an axis that stopped at the data would
 * hide the line the reader is meant to compare against whenever a trace
 * never reached it, which is exactly the case where the comparison matters.
 */
export function upperBoundG(samples: readonly TraceSample[], impactG: number): number {
  const peak = peakOf(samples);
  const highest = Math.max(peak?.magnitudeG ?? 0, impactG);

  return Math.ceil((highest + 0.3) * 2) / 2;
}

/** The sample nearest a given offset in seconds, for the crosshair. */
export function nearestSample(
  trace: AlertTrace,
  seconds: number,
): { sample: TraceSample; index: number } | null {
  if (trace.samples.length === 0) {
    return null;
  }

  let bestIndex = 0;
  let bestDistance = Number.POSITIVE_INFINITY;

  trace.samples.forEach((sample, index) => {
    const distance = Math.abs(offsetSeconds(sample, trace.raisedAt) - seconds);

    if (distance < bestDistance) {
      bestDistance = distance;
      bestIndex = index;
    }
  });

  return { sample: trace.samples[bestIndex], index: bestIndex };
}
