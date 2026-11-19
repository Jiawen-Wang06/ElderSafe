import { useState } from "react";
import { Loader2, NotebookPen, ShieldAlert } from "lucide-react";
import { api } from "@/lib/api";
import type { ShiftSummary, ShiftSummaryAlert } from "@/lib/api";
import { browserTimeZone, splitCitations } from "@/lib/shiftSummary";
import { Button } from "@/components/ui/button";
import { Card, CardContent, CardDescription, CardHeader, CardTitle } from "@/components/ui/card";

interface ShiftSummaryCardProps {
  residentId: string;
  residentDisplayName: string;
}

/**
 * A handover summary of a resident's last shift, written on request
 * (REQ-AI-01, REQ-AI-02).
 *
 * Generated only when the caregiver asks: each request may call a language
 * model, which costs money and is rate limited. The stored alerts are always
 * listed under the text, so the caregiver can check it against them, and a
 * summary the server rejected is never shown — only the reason and the
 * alerts.
 */
export function ShiftSummaryCard({ residentId, residentDisplayName }: ShiftSummaryCardProps) {
  const [summary, setSummary] = useState<ShiftSummary | null>(null);
  const [loading, setLoading] = useState(false);
  const [error, setError] = useState<string | null>(null);

  async function summarize() {
    setLoading(true);
    setError(null);

    try {
      setSummary(await api.shiftSummary(residentId, browserTimeZone()));
    } catch {
      setError("The shift summary could not be created.");
    } finally {
      setLoading(false);
    }
  }

  return (
    <Card>
      <CardHeader>
        <CardTitle className="flex items-center gap-2">
          <NotebookPen className="size-4 text-primary" aria-hidden="true" />
          Shift handover
        </CardTitle>
        <CardDescription>
          A short summary of {residentDisplayName}'s alerts over the last shift, written from the
          stored alerts only.
        </CardDescription>
      </CardHeader>

      <CardContent className="flex flex-col gap-3">
        <div>
          <Button size="sm" onClick={() => void summarize()} disabled={loading}>
            {loading && <Loader2 className="size-4 animate-spin" aria-hidden="true" />}
            {summary === null ? "Summarize last shift" : "Summarize again"}
          </Button>
        </div>

        {error !== null && (
          <p role="alert" className="text-sm text-destructive">
            {error}
          </p>
        )}

        {summary !== null && <ShiftSummaryResult summary={summary} />}
      </CardContent>
    </Card>
  );
}

interface ShiftSummaryResultProps {
  summary: ShiftSummary;
}

function ShiftSummaryResult({ summary }: ShiftSummaryResultProps) {
  const refs = summary.alerts.map((alert) => alert.ref);
  const range = `${new Date(summary.from).toLocaleString()} – ${new Date(summary.to).toLocaleString()}`;

  return (
    <div className="flex max-w-3xl flex-col gap-3" aria-live="polite">
      {summary.status === "Generated" && summary.text !== null ? (
        <p className="rounded-lg border border-border bg-muted/40 p-3 text-sm leading-relaxed">
          {splitCitations(summary.text, refs).map((segment, index) =>
            segment.kind === "text" ? (
              <span key={index}>{segment.text}</span>
            ) : (
              <a
                key={index}
                href={`#shift-alert-${segment.ref}`}
                className="mx-0.5 rounded bg-primary/10 px-1 font-mono text-xs font-medium text-primary hover:underline"
              >
                {segment.ref}
              </a>
            ),
          )}
        </p>
      ) : (
        <div role="alert" className="flex gap-2 rounded-lg border border-destructive/40 p-3 text-sm">
          <ShieldAlert className="mt-0.5 size-4 shrink-0 text-destructive" aria-hidden="true" />
          <div className="flex flex-col gap-1">
            <p className="font-medium">
              The generated summary was withheld because it did not match the stored alerts.
            </p>
            <ul className="list-disc pl-4 text-muted-foreground">
              {summary.problems.map((problem) => (
                <li key={problem}>{problem}</li>
              ))}
            </ul>
            <p className="text-muted-foreground">The alerts it was written from are listed below.</p>
          </div>
        </div>
      )}

      <p className="text-xs text-muted-foreground">
        Written by {summary.generator}. Covers {range}.
        {summary.notice !== null && <> {summary.notice}</>} Check it against the alerts below.
      </p>

      <ShiftAlertList alerts={summary.alerts} />
    </div>
  );
}

interface ShiftAlertListProps {
  alerts: ShiftSummaryAlert[];
}

function ShiftAlertList({ alerts }: ShiftAlertListProps) {
  if (alerts.length === 0) {
    return <p className="text-sm text-muted-foreground">No alerts were raised in this shift.</p>;
  }

  return (
    <ul className="flex flex-col divide-y divide-border rounded-lg border border-border">
      {alerts.map((alert) => (
        <li
          key={alert.id}
          id={`shift-alert-${alert.ref}`}
          className="flex items-center justify-between gap-3 px-3 py-2 text-sm target:bg-primary/10"
        >
          <div className="flex items-center gap-2">
            <span className="rounded bg-primary/10 px-1 font-mono text-xs font-medium text-primary">
              {alert.ref}
            </span>
            <span>{alert.kind}</span>
            <span className="text-xs text-muted-foreground">
              {new Date(alert.raisedAt).toLocaleTimeString()}
            </span>
          </div>

          <div className="flex flex-wrap items-center justify-end gap-2 text-right text-xs">
            {alert.emergencyCalled && <span className="font-medium text-destructive">Emergency call</span>}
            {alert.escalated && !alert.emergencyCalled && (
              <span className="text-destructive">Escalated</span>
            )}
            {alert.acknowledgedAt !== null ? (
              <span className="text-muted-foreground">
                Acknowledged {new Date(alert.acknowledgedAt).toLocaleTimeString()}
                {alert.acknowledgedByDisplayName !== null && <> by {alert.acknowledgedByDisplayName}</>}
              </span>
            ) : (
              <span className="text-destructive">Not yet acknowledged</span>
            )}
          </div>
        </li>
      ))}
    </ul>
  );
}
