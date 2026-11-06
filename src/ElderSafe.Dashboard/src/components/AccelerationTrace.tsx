import { useMemo, useState } from "react";
import type { AlertTrace } from "@/lib/api";
import {
  findingsFor,
  gTicks,
  linearScale,
  nearestSample,
  offsetSeconds,
  upperBoundG,
} from "@/lib/trace";

const width = 720;
const height = 260;
const padding = { top: 16, right: 64, bottom: 28, left: 40 };

const plot = {
  left: padding.left,
  right: width - padding.right,
  top: padding.top,
  bottom: height - padding.bottom,
};

/**
 * The acceleration behind one alert, against the thresholds applied to it
 * (REQ-FALL-08).
 *
 * Built as SVG rather than with a charting library. The chart has to draw
 * things a general-purpose library makes awkward — reference lines carrying
 * the detector's own configured values, a shaded stillness band, annotations
 * on the two samples that crossed a threshold — and all of it is one path and
 * a handful of lines. A dependency would be more code to read, not less.
 *
 * One series, so there is no legend: the title says what is plotted. Values
 * are not printed on every point; the crosshair and the table below carry
 * them, and only the peak is labelled directly.
 */
export function AccelerationTrace({ trace }: { trace: AlertTrace }) {
  const [cursor, setCursor] = useState<number | null>(null);

  const findings = useMemo(() => findingsFor(trace), [trace]);

  const maxG = upperBoundG(trace.samples, trace.thresholds.impactG);

  const firstOffset = trace.samples.length > 0
    ? offsetSeconds(trace.samples[0], trace.raisedAt)
    : -1;

  const lastOffset = trace.samples.length > 0
    ? offsetSeconds(trace.samples[trace.samples.length - 1], trace.raisedAt)
    : 0;

  const x = linearScale([firstOffset, lastOffset], [plot.left, plot.right]);
  const y = linearScale([0, maxG], [plot.bottom, plot.top]);

  const path = trace.samples
    .map((sample, index) => {
      const command = index === 0 ? "M" : "L";

      return `${command}${x(offsetSeconds(sample, trace.raisedAt)).toFixed(2)} ${y(sample.magnitudeG).toFixed(2)}`;
    })
    .join(" ");

  const active = cursor === null ? null : trace.samples[cursor];

  function moveCursorTo(clientX: number, target: SVGSVGElement) {
    const bounds = target.getBoundingClientRect();
    const scaled = ((clientX - bounds.left) / bounds.width) * width;
    const seconds =
      firstOffset + ((scaled - plot.left) / (plot.right - plot.left)) * (lastOffset - firstOffset);

    setCursor(nearestSample(trace, seconds)?.index ?? null);
  }

  function onKeyDown(event: React.KeyboardEvent<SVGSVGElement>) {
    if (event.key !== "ArrowLeft" && event.key !== "ArrowRight") {
      return;
    }

    event.preventDefault();

    const step = event.key === "ArrowLeft" ? -1 : 1;
    const from = cursor ?? 0;

    setCursor(Math.min(trace.samples.length - 1, Math.max(0, from + step)));
  }

  if (trace.samples.length === 0) {
    return (
      <p className="text-sm text-muted-foreground">
        No readings were stored for this window, so there is nothing to show.
        An alert can outlive its readings if they were pruned.
      </p>
    );
  }

  return (
    <div className="viz-root flex flex-col gap-3">
      <style>{`
        .viz-root {
          --viz-series: #2a78d6;
          --viz-grid: #e1e0d9;
          --viz-axis: #c3c2b7;
          --viz-muted: #898781;
          --viz-surface: #ffffff;
          --viz-critical: #d03b3b;
        }
        .dark .viz-root {
          --viz-series: #3987e5;
          --viz-grid: #2c2c2a;
          --viz-axis: #383835;
          --viz-muted: #898781;
          --viz-surface: #171717;
          --viz-critical: #d03b3b;
        }
      `}</style>

      <svg
        viewBox={`0 0 ${width} ${height}`}
        className="w-full touch-none outline-none focus-visible:ring-2 focus-visible:ring-ring"
        role="img"
        tabIndex={0}
        aria-label={`Acceleration for this alert, peaking at ${findings.peak?.magnitudeG.toFixed(2)} g. Free fall threshold ${trace.thresholds.freeFallG} g, impact threshold ${trace.thresholds.impactG} g. Values are listed in the table below the chart.`}
        onPointerMove={(event) => moveCursorTo(event.clientX, event.currentTarget)}
        onPointerLeave={() => setCursor(null)}
        onKeyDown={onKeyDown}
        onBlur={() => setCursor(null)}
      >
        <rect
          x={plot.left}
          y={y(trace.thresholds.stillnessCentreG + trace.thresholds.stillnessToleranceG)}
          width={plot.right - plot.left}
          height={
            y(trace.thresholds.stillnessCentreG - trace.thresholds.stillnessToleranceG)
            - y(trace.thresholds.stillnessCentreG + trace.thresholds.stillnessToleranceG)
          }
          fill="var(--viz-series)"
          opacity={0.08}
        />

        {gTicks(maxG).map((tick) => (
          <g key={tick}>
            <line
              x1={plot.left}
              x2={plot.right}
              y1={y(tick)}
              y2={y(tick)}
              stroke="var(--viz-grid)"
              strokeWidth={1}
            />
            <text
              x={plot.left - 8}
              y={y(tick) + 4}
              textAnchor="end"
              fontSize={11}
              fill="var(--viz-muted)"
              style={{ fontVariantNumeric: "tabular-nums" }}
            >
              {tick}
            </text>
          </g>
        ))}

        <line
          x1={plot.left}
          x2={plot.right}
          y1={y(trace.thresholds.freeFallG)}
          y2={y(trace.thresholds.freeFallG)}
          stroke="var(--viz-axis)"
          strokeWidth={1}
        />
        <text
          x={plot.right + 6}
          y={y(trace.thresholds.freeFallG) + 4}
          fontSize={11}
          fill="var(--viz-muted)"
        >
          free fall
        </text>

        <line
          x1={plot.left}
          x2={plot.right}
          y1={y(trace.thresholds.impactG)}
          y2={y(trace.thresholds.impactG)}
          stroke="var(--viz-axis)"
          strokeWidth={1}
        />
        <text
          x={plot.right + 6}
          y={y(trace.thresholds.impactG) + 4}
          fontSize={11}
          fill="var(--viz-muted)"
        >
          impact
        </text>

        <line
          x1={plot.left}
          x2={plot.right}
          y1={plot.bottom}
          y2={plot.bottom}
          stroke="var(--viz-axis)"
          strokeWidth={1}
        />

        {[firstOffset, 0].map((tick) => (
          <text
            key={tick}
            x={x(tick)}
            y={plot.bottom + 18}
            textAnchor={tick === 0 ? "end" : "start"}
            fontSize={11}
            fill="var(--viz-muted)"
            style={{ fontVariantNumeric: "tabular-nums" }}
          >
            {tick === 0 ? "alert raised" : `${tick.toFixed(1)}s`}
          </text>
        ))}

        <path
          d={path}
          fill="none"
          stroke="var(--viz-series)"
          strokeWidth={2}
          strokeLinejoin="round"
          strokeLinecap="round"
        />

        {findings.freeFall !== null && (
          <circle
            cx={x(offsetSeconds(findings.freeFall, trace.raisedAt))}
            cy={y(findings.freeFall.magnitudeG)}
            r={4}
            fill="var(--viz-series)"
            stroke="var(--viz-surface)"
            strokeWidth={2}
          />
        )}

        {findings.peak !== null && (
          <>
            <circle
              cx={x(offsetSeconds(findings.peak, trace.raisedAt))}
              cy={y(findings.peak.magnitudeG)}
              r={4}
              fill={
                findings.peak.magnitudeG >= trace.thresholds.impactG
                  ? "var(--viz-critical)"
                  : "var(--viz-series)"
              }
              stroke="var(--viz-surface)"
              strokeWidth={2}
            />
            <text
              x={x(offsetSeconds(findings.peak, trace.raisedAt))}
              y={y(findings.peak.magnitudeG) - 10}
              textAnchor="middle"
              fontSize={11}
              fill="var(--viz-muted)"
              style={{ fontVariantNumeric: "tabular-nums" }}
            >
              {findings.peak.magnitudeG.toFixed(2)} g peak
            </text>
          </>
        )}

        {active !== undefined && active !== null && (
          <g>
            <line
              x1={x(offsetSeconds(active, trace.raisedAt))}
              x2={x(offsetSeconds(active, trace.raisedAt))}
              y1={plot.top}
              y2={plot.bottom}
              stroke="var(--viz-axis)"
              strokeWidth={1}
            />
            <circle
              cx={x(offsetSeconds(active, trace.raisedAt))}
              cy={y(active.magnitudeG)}
              r={4}
              fill="var(--viz-series)"
              stroke="var(--viz-surface)"
              strokeWidth={2}
            />
          </g>
        )}
      </svg>

      <p className="min-h-5 text-sm" aria-live="polite">
        {active !== undefined && active !== null ? (
          <>
            <span className="font-medium tabular-nums">{active.magnitudeG.toFixed(2)} g</span>
            <span className="text-muted-foreground">
              {" "}
              at {offsetSeconds(active, trace.raisedAt).toFixed(2)}s
            </span>
          </>
        ) : (
          <span className="text-muted-foreground">
            Hover the chart, or focus it and use the arrow keys, to read a value.
          </span>
        )}
      </p>

      <p className="text-sm text-muted-foreground">
        {findings.inExpectedOrder
          ? `Free fall below ${trace.thresholds.freeFallG} g, then an impact of at least ${trace.thresholds.impactG} g, then stillness — the three conditions the detector requires.`
          : `This trace does not show free fall followed by an impact in that order. The alert may have been raised from readings outside this window, or the thresholds may have been retuned since.`}
      </p>

      <details className="text-sm">
        <summary className="cursor-pointer text-muted-foreground">
          Readings as a table ({trace.samples.length} samples)
        </summary>

        <div className="mt-2 max-h-64 overflow-auto">
          <table className="w-full text-left tabular-nums">
            <thead className="sticky top-0 bg-card">
              <tr className="text-muted-foreground">
                <th className="py-1 pr-4 font-medium">Offset (s)</th>
                <th className="py-1 font-medium">Magnitude (g)</th>
              </tr>
            </thead>
            <tbody>
              {trace.samples.map((sample) => (
                <tr key={sample.at}>
                  <td className="py-0.5 pr-4">
                    {offsetSeconds(sample, trace.raisedAt).toFixed(2)}
                  </td>
                  <td className="py-0.5">{sample.magnitudeG.toFixed(3)}</td>
                </tr>
              ))}
            </tbody>
          </table>
        </div>
      </details>
    </div>
  );
}
