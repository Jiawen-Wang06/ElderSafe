import { useState } from "react";
import type { AlertSummary } from "@/lib/api";
import { chartDataFor, countTicks } from "@/lib/summary";
import { linearScale } from "@/lib/trace";

const width = 720;
const height = 260;
const padding = { top: 16, right: 16, bottom: 32, left: 36 };

const plot = {
  left: padding.left,
  right: width - padding.right,
  top: padding.top,
  bottom: height - padding.bottom,
};

const maxBarWidth = 24;
const barGap = 2;

/** One color per alert kind, validated against both app surfaces. */
const kindColor: Record<string, string> = {
  Fall: "var(--viz-fall)",
  Wander: "var(--viz-wander)",
  NoData: "var(--viz-nodata)",
};

/**
 * Alert counts for a resident over a chosen date range, as a grouped bar
 * chart (REQ-FALL-09).
 *
 * The NoData slot's color does not clear 3:1 contrast against the light
 * surface on its own (checked with the dataviz skill's palette validator),
 * so this chart never relies on color alone: every bar has a focusable hit
 * target with a text tooltip, and the collapsed table below repeats every
 * value as text regardless of color.
 */
export function AlertSummaryChart({ summary }: { summary: AlertSummary }) {
  const [active, setActive] = useState<{ bucketIndex: number; kind: string } | null>(null);

  const { bucketLabels, series } = chartDataFor(summary);

  if (bucketLabels.length === 0 || series.length === 0) {
    return (
      <p className="text-sm text-muted-foreground">
        No alerts were raised for this resident in the chosen range.
      </p>
    );
  }

  const maxCount = Math.max(1, ...series.flatMap((s) => s.values));
  const ticks = countTicks(maxCount);
  const topTick = ticks[ticks.length - 1];

  const y = linearScale([0, topTick], [plot.bottom, plot.top]);
  const groupWidth = (plot.right - plot.left) / bucketLabels.length;
  const barWidth = Math.min(
    maxBarWidth,
    (groupWidth - barGap * (series.length + 1)) / series.length,
  );

  function barX(bucketIndex: number, seriesIndex: number): number {
    const groupStart = plot.left + bucketIndex * groupWidth;
    const barsWidth = barWidth * series.length + barGap * (series.length - 1);
    const groupCenter = groupStart + groupWidth / 2;

    return groupCenter - barsWidth / 2 + seriesIndex * (barWidth + barGap);
  }

  const activeValue =
    active === null
      ? null
      : series.find((s) => s.kind === active.kind)?.values[active.bucketIndex] ?? null;

  const activeEmergencyCalled =
    active === null
      ? null
      : series.find((s) => s.kind === active.kind)?.emergencyCalled[active.bucketIndex] ?? null;

  const hasEmergencyCalled = series.some((s) => s.emergencyCalled.some((count) => count > 0));

  return (
    <div className="viz-root flex flex-col gap-3">
      <style>{`
        .viz-root {
          --viz-fall: #2a78d6;
          --viz-wander: #eb6834;
          --viz-nodata: #1baf7a;
          --viz-emergency: #c22525;
          --viz-grid: #e1e0d9;
          --viz-axis: #c3c2b7;
          --viz-muted: #898781;
          --viz-surface: #ffffff;
        }
        .dark .viz-root {
          --viz-fall: #3987e5;
          --viz-wander: #d95926;
          --viz-nodata: #199e70;
          --viz-emergency: #e14545;
          --viz-grid: #2c2c2a;
          --viz-axis: #383835;
          --viz-muted: #898781;
          --viz-surface: #171717;
        }
      `}</style>

      {(series.length > 1 || hasEmergencyCalled) && (
        <div className="flex flex-wrap gap-3 text-sm text-muted-foreground">
          {series.map((s) => (
            <span key={s.kind} className="inline-flex items-center gap-1.5">
              <span
                aria-hidden
                className="inline-block size-2.5 rounded-full"
                style={{ backgroundColor: kindColor[s.kind] ?? "var(--viz-muted)" }}
              />
              {s.kind}
            </span>
          ))}
          {hasEmergencyCalled && (
            <span className="inline-flex items-center gap-1.5">
              <span
                aria-hidden
                className="inline-block size-2.5 rounded-full"
                style={{ backgroundColor: "var(--viz-emergency)" }}
              />
              Reached emergency call
            </span>
          )}
        </div>
      )}

      <svg
        viewBox={`0 0 ${width} ${height}`}
        className="w-full"
        role="img"
        aria-label={`Alert counts by period. ${series
          .map((s) => {
            const total = s.values.reduce((a, b) => a + b, 0);
            const emergencyCalled = s.emergencyCalled.reduce((a, b) => a + b, 0);

            return emergencyCalled > 0
              ? `${s.kind}: ${total} total, ${emergencyCalled} reached emergency call`
              : `${s.kind}: ${total} total`;
          })
          .join(". ")}`}
      >
        {ticks.map((tick) => (
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
          y1={plot.bottom}
          y2={plot.bottom}
          stroke="var(--viz-axis)"
          strokeWidth={1}
        />

        {bucketLabels.map((label, bucketIndex) => (
          <text
            key={label + bucketIndex}
            x={plot.left + bucketIndex * groupWidth + groupWidth / 2}
            y={plot.bottom + 18}
            textAnchor="middle"
            fontSize={11}
            fill="var(--viz-muted)"
          >
            {label}
          </text>
        ))}

        {series.map((s) =>
          s.values.map((value, bucketIndex) => {
            const barHeight = plot.bottom - y(value);
            const isActive = active?.bucketIndex === bucketIndex && active.kind === s.kind;
            const emergencyCalled = s.emergencyCalled[bucketIndex] ?? 0;

            if (value === 0) {
              return null;
            }

            // The emergency-called portion of a bar is drawn as a flat-top
            // red cap stacked on the bar's own top, sized to its share of
            // the bar's height — never a separate bar, since it is a subset
            // of this bar's total rather than a count of its own.
            const emergencyCapHeight =
              emergencyCalled > 0 ? barHeight * (emergencyCalled / value) : 0;

            const label =
              emergencyCalled > 0
                ? `${s.kind}, ${bucketLabels[bucketIndex]}: ${value}, ${emergencyCalled} reached emergency call`
                : `${s.kind}, ${bucketLabels[bucketIndex]}: ${value}`;

            return (
              <g key={`${s.kind}-${bucketIndex}`}>
                <rect
                  x={barX(bucketIndex, series.indexOf(s))}
                  y={y(value)}
                  width={barWidth}
                  height={barHeight}
                  rx={4}
                  fill={kindColor[s.kind] ?? "var(--viz-muted)"}
                  opacity={isActive ? 1 : 0.9}
                  tabIndex={0}
                  role="button"
                  aria-label={label}
                  onPointerEnter={() => setActive({ bucketIndex, kind: s.kind })}
                  onPointerLeave={() => setActive(null)}
                  onFocus={() => setActive({ bucketIndex, kind: s.kind })}
                  onBlur={() => setActive(null)}
                  className="outline-none focus-visible:ring-2 focus-visible:ring-ring"
                />
                {emergencyCapHeight > 0 && (
                  <rect
                    aria-hidden
                    x={barX(bucketIndex, series.indexOf(s))}
                    y={y(value)}
                    width={barWidth}
                    height={emergencyCapHeight}
                    fill="var(--viz-emergency)"
                    opacity={isActive ? 1 : 0.9}
                    className="pointer-events-none"
                  />
                )}
              </g>
            );
          }),
        )}
      </svg>

      <p className="min-h-5 text-sm" aria-live="polite">
        {active !== null && activeValue !== null ? (
          <>
            <span className="font-medium tabular-nums">{activeValue}</span>
            <span className="text-muted-foreground">
              {" "}
              {active.kind} alerts, {bucketLabels[active.bucketIndex]}
              {activeEmergencyCalled !== null && activeEmergencyCalled > 0 && (
                <>
                  {" "}
                  (
                  <span className="font-medium text-destructive">
                    {activeEmergencyCalled} reached emergency call
                  </span>
                  )
                </>
              )}
            </span>
          </>
        ) : (
          <span className="text-muted-foreground">
            Hover or focus a bar to read its count.
          </span>
        )}
      </p>

      <details className="text-sm">
        <summary className="cursor-pointer text-muted-foreground">
          Counts as a table
        </summary>

        <div className="mt-2 max-h-64 overflow-auto">
          <table className="w-full text-left tabular-nums">
            <thead className="sticky top-0 bg-card">
              <tr className="text-muted-foreground">
                <th className="py-1 pr-4 font-medium">Period</th>
                {series.map((s) => (
                  <th key={s.kind} className="py-1 pr-4 font-medium">
                    {s.kind}
                  </th>
                ))}
                {hasEmergencyCalled && (
                  <th className="py-1 pr-4 font-medium">Reached emergency call</th>
                )}
              </tr>
            </thead>
            <tbody>
              {bucketLabels.map((label, bucketIndex) => (
                <tr key={label + bucketIndex}>
                  <td className="py-0.5 pr-4">{label}</td>
                  {series.map((s) => (
                    <td key={s.kind} className="py-0.5 pr-4">
                      {s.values[bucketIndex]}
                    </td>
                  ))}
                  {hasEmergencyCalled && (
                    <td className="py-0.5 pr-4">
                      {series.reduce(
                        (sum, s) => sum + (s.emergencyCalled[bucketIndex] ?? 0),
                        0,
                      )}
                    </td>
                  )}
                </tr>
              ))}
            </tbody>
          </table>
        </div>
      </details>
    </div>
  );
}
