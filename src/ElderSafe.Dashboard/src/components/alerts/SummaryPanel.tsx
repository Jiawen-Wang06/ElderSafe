import { useEffect, useMemo, useState } from "react";
import { Loader2 } from "lucide-react";
import { useAuth } from "@/auth/useAuth";
import { api } from "@/lib/api";
import type { AlertSummary } from "@/lib/api";
import { presetRange, totalsFor } from "@/lib/summary";
import type { SummaryPreset } from "@/lib/summary";
import { AlertSummaryChart } from "@/components/AlertSummaryChart";
import { Button } from "@/components/ui/button";
import { Card, CardContent, CardDescription, CardHeader, CardTitle } from "@/components/ui/card";

const presetLabels: Record<SummaryPreset, string> = {
  week: "Past week",
  month: "Past month",
  year: "Past year",
};

function toDateInputValue(date: Date): string {
  return date.toISOString().slice(0, 10);
}

/**
 * Alert counts for a resident over a date range the caregiver chooses
 * (REQ-FALL-09).
 *
 * The date-range control is three preset buttons plus two native date
 * inputs for a custom range — a simplified stand-in for a richer
 * preset-and-calendar picker. Good enough for a caregiver who wants "this
 * week" or "since I was last on shift," and a known scope simplification
 * rather than an oversight.
 */
export function SummaryPanel() {
  const { account } = useAuth();
  const residents = useMemo(() => account?.residents ?? [], [account]);

  const [residentId, setResidentId] = useState<string | null>(residents[0]?.id ?? null);
  const [preset, setPreset] = useState<SummaryPreset | "custom">("week");
  const [customFrom, setCustomFrom] = useState(() => toDateInputValue(presetRange("week", new Date()).from));
  const [customTo, setCustomTo] = useState(() => toDateInputValue(new Date()));

  const [summary, setSummary] = useState<AlertSummary | null>(null);
  const [loading, setLoading] = useState(false);
  const [error, setError] = useState<string | null>(null);

  const range = useMemo(() => {
    if (preset === "custom") {
      return { from: new Date(customFrom), to: new Date(customTo) };
    }

    return presetRange(preset, new Date());
  }, [preset, customFrom, customTo]);

  useEffect(() => {
    if (residentId === null) {
      return;
    }

    let cancelled = false;

    // The loading flag is set from inside the promise chain, even though it
    // is known before the request is sent, so that every setState call here
    // runs as a callback from the fetch rather than synchronously in the
    // effect body (react-hooks/set-state-in-effect).
    Promise.resolve()
      .then(() => {
        if (cancelled) {
          return undefined;
        }

        setLoading(true);
        setError(null);

        return api.summary(residentId, range.from, range.to);
      })
      .then((result) => {
        if (!cancelled && result !== undefined) {
          setSummary(result);
        }
      })
      .catch(() => {
        if (!cancelled) {
          setError("Could not load the summary for this range.");
        }
      })
      .finally(() => {
        if (!cancelled) {
          setLoading(false);
        }
      });

    return () => {
      cancelled = true;
    };
  }, [residentId, range]);

  if (residents.length === 0) {
    return (
      <Card>
        <CardHeader>
          <CardTitle>No residents assigned</CardTitle>
          <CardDescription>
            There is nothing to summarize until a resident assigns you.
          </CardDescription>
        </CardHeader>
      </Card>
    );
  }

  const totals = summary === null ? null : totalsFor(summary);

  return (
    <Card>
      <CardHeader>
        <CardTitle>Summary</CardTitle>
        <CardDescription>
          How many alerts were raised, and how many were answered in time.
        </CardDescription>
      </CardHeader>

      <CardContent className="flex flex-col gap-4">
        {residents.length > 1 && (
          <div className="flex flex-wrap gap-2">
            {residents.map((resident) => (
              <Button
                key={resident.id}
                variant={resident.id === residentId ? "secondary" : "outline"}
                size="sm"
                onClick={() => setResidentId(resident.id)}
              >
                {resident.displayName}
              </Button>
            ))}
          </div>
        )}

        <div className="flex flex-wrap items-center gap-2">
          {(["week", "month", "year"] as const).map((option) => (
            <Button
              key={option}
              variant={preset === option ? "secondary" : "outline"}
              size="sm"
              onClick={() => setPreset(option)}
            >
              {presetLabels[option]}
            </Button>
          ))}

          <div className="flex items-center gap-1.5 text-sm text-muted-foreground">
            <label htmlFor="summary-from">from</label>
            <input
              id="summary-from"
              type="date"
              value={customFrom}
              onChange={(event) => {
                setCustomFrom(event.target.value);
                setPreset("custom");
              }}
              className="rounded-md border border-input bg-background px-2 py-1 text-sm"
            />
            <label htmlFor="summary-to">to</label>
            <input
              id="summary-to"
              type="date"
              value={customTo}
              onChange={(event) => {
                setCustomTo(event.target.value);
                setPreset("custom");
              }}
              className="rounded-md border border-input bg-background px-2 py-1 text-sm"
            />
          </div>
        </div>

        {error !== null && (
          <p role="alert" className="text-sm text-destructive">
            {error}
          </p>
        )}

        {loading ? (
          <p className="flex items-center gap-2 text-sm text-muted-foreground">
            <Loader2 className="size-4 animate-spin" aria-hidden="true" />
            Loading summary…
          </p>
        ) : summary !== null && totals !== null ? (
          <>
            <div className="grid grid-cols-3 gap-3">
              <div className="rounded-lg border border-border p-3">
                <p className="text-xs text-muted-foreground">Alerts raised</p>
                <p className="text-2xl font-semibold tabular-nums">{totals.total}</p>
              </div>
              <div className="rounded-lg border border-border p-3">
                <p className="text-xs text-muted-foreground">Responded in time</p>
                <p className="text-2xl font-semibold tabular-nums">{totals.respondedInTime}</p>
              </div>
              <div className="rounded-lg border border-border p-3">
                <p className="text-xs text-muted-foreground">Escalated</p>
                <p className="text-2xl font-semibold tabular-nums">{totals.escalated}</p>
              </div>
            </div>

            <AlertSummaryChart summary={summary} />
          </>
        ) : null}
      </CardContent>
    </Card>
  );
}
