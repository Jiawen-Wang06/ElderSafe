import { Fragment, useCallback, useEffect, useMemo, useState } from "react";
import {
  AlertTriangle,
  Check,
  ChevronDown,
  ChevronRight,
  FlaskConical,
  Loader2,
  Siren,
} from "lucide-react";
import { useAuth } from "@/auth/useAuth";
import { api, ApiError } from "@/lib/api";
import type { AlertTrace, AlertView, AssignedCaregiverView } from "@/lib/api";
import { AccelerationTrace } from "@/components/AccelerationTrace";
import { describeWait, isOpen, mergeAlert, mergeAlerts, secondsWaiting } from "@/lib/alertList";
import { createAlertsConnection } from "@/lib/signalr";
import { Button } from "@/components/ui/button";
import { Card, CardContent, CardDescription, CardHeader, CardTitle } from "@/components/ui/card";
import {
  Table,
  TableBody,
  TableCell,
  TableHead,
  TableHeader,
  TableRow,
} from "@/components/ui/table";

type ConnectionState = "connecting" | "connected" | "reconnecting" | "disconnected";

const connectionLabels: Record<ConnectionState, string> = {
  connecting: "Connecting…",
  connected: "Live",
  reconnecting: "Reconnecting…",
  disconnected: "Not receiving alerts",
};

/**
 * The live alert list and acknowledgement flow (REQ-FALL-02, REQ-FALL-07,
 * REQ-FALL-08).
 *
 * Extracted out of what used to be the whole caregiver alerts page, so it
 * can sit in one tab beside a summary view (REQ-FALL-09) without either
 * competing for the page. Rendered with `keepMounted` from its parent, so
 * switching tabs never tears down the live SignalR connection or the
 * in-memory alert list built up here.
 */
export function AlertsPanel() {
  const { account } = useAuth();

  const [alerts, setAlerts] = useState<AlertView[]>([]);
  const [connection, setConnection] = useState<ConnectionState>("connecting");
  const [loading, setLoading] = useState(true);
  const [loadError, setLoadError] = useState<string | null>(null);
  const [acknowledging, setAcknowledging] = useState<string | null>(null);
  const [simulating, setSimulating] = useState(false);
  const [notice, setNotice] = useState<string | null>(null);
  const [now, setNow] = useState(() => Date.now());
  const [expanded, setExpanded] = useState<string | null>(null);
  const [traces, setTraces] = useState<Record<string, AlertTrace>>({});
  const [traceError, setTraceError] = useState<string | null>(null);
  const [caregiversByResident, setCaregiversByResident] = useState<
    Record<string, AssignedCaregiverView[]>
  >({});

  // Memoised on the account rather than on a joined list of identifiers. The
  // account object is replaced only when "who am I" is re-asked, so this is
  // already stable between renders, and an effect that did restart would
  // refetch and resubscribe — which is correct behaviour, not a bug to design
  // around.
  const residents = useMemo(() => account?.residents ?? [], [account]);

  const load = useCallback(async () => {
    // Nothing to fetch, and nothing to report: this account renders the
    // "no residents" card instead of a list, so no state needs setting.
    if (residents.length === 0) {
      return;
    }

    try {
      const [alertBatches, caregiverBatches] = await Promise.all([
        Promise.all(residents.map((resident) => api.alerts(resident.id, true))),
        Promise.all(residents.map((resident) => api.caregivers(resident.id))),
      ]);

      setAlerts(mergeAlerts(...alertBatches));
      setCaregiversByResident(
        Object.fromEntries(
          residents.map((resident, index) => [resident.id, caregiverBatches[index]]),
        ),
      );
      setLoadError(null);
    } catch {
      // Deliberately not cleared: showing a stale list under an error notice
      // is safer than blanking the screen, which would read as "no alerts".
      setLoadError("Could not load alerts. The list below may be out of date.");
    } finally {
      setLoading(false);
    }
  }, [residents]);

  useEffect(() => {
    async function run() {
      await load();
    }

    void run();
  }, [load]);

  // A ticking clock so the wait column counts up. One second, because
  // REQ-FALL-02 is specified in seconds.
  useEffect(() => {
    const timer = window.setInterval(() => setNow(Date.now()), 1000);

    return () => window.clearInterval(timer);
  }, []);

  useEffect(() => {
    // No subscription to make. The connection state is only rendered beside a
    // list, and this account has none, so it is left alone rather than
    // written to from an effect body.
    if (residents.length === 0) {
      return;
    }

    const hub = createAlertsConnection();
    let stopped = false;

    hub.on("alertRaised", (alert: AlertView) => {
      setAlerts((current) => mergeAlert(current, alert));
    });

    hub.on("alertUpdated", (alert: AlertView) => {
      setAlerts((current) => mergeAlert(current, alert));
    });

    async function subscribe() {
      for (const resident of residents) {
        const allowed = await hub.invoke<boolean>("SubscribeToResident", resident.id);

        if (!allowed) {
          setNotice(
            `Not subscribed to ${resident.displayName}: the server did not accept the subscription.`,
          );
        }
      }
    }

    hub.onreconnecting(() => setConnection("reconnecting"));

    hub.onreconnected(() => {
      setConnection("connected");

      // Group membership does not survive a reconnect, and anything raised
      // while the socket was down was never pushed. Resubscribing and
      // refetching is what keeps a dropped connection from silently becoming
      // a caregiver who stops being told about falls (RISK-08).
      void subscribe();
      void load();
    });

    hub.onclose(() => setConnection("disconnected"));

    hub
      .start()
      .then(async () => {
        if (stopped) {
          return;
        }

        setConnection("connected");
        await subscribe();
      })
      .catch(() => setConnection("disconnected"));

    return () => {
      stopped = true;
      void hub.stop();
    };
  }, [residents, load]);

  // The caregiver currently at this alert's escalation level, by their
  // registered email. Every subscribed caregiver is actually notified
  // regardless of level (the server broadcasts to the resident's whole
  // group, not to one caregiver at a time), so this names whose turn it
  // is in the chain, not who received a push and who didn't. Falls back
  // to a bare position number if that caregiver was since removed.
  function caregiverAtLevel(alert: AlertView): string {
    const caregiver = caregiversByResident[alert.residentId]?.[alert.escalationLevel];

    return caregiver?.userName ?? `Caregiver ${alert.escalationLevel + 1}`;
  }

  async function acknowledge(alert: AlertView) {
    setAcknowledging(alert.id);
    setNotice(null);

    try {
      await api.acknowledge(alert.id);

      // Applied locally rather than waiting for the hub to echo it back, so
      // the button does not appear to do nothing if the push is slow or the
      // connection is down.
      setAlerts((current) =>
        mergeAlert(current, { ...alert, acknowledgedAt: new Date().toISOString() }),
      );
    } catch {
      setNotice("That alert could not be acknowledged. It may already be closed.");
      void load();
    } finally {
      setAcknowledging(null);
    }
  }

  async function toggleTrace(alertId: string) {
    if (expanded === alertId) {
      setExpanded(null);

      return;
    }

    setExpanded(alertId);
    setTraceError(null);

    // Cached after the first fetch: the readings behind an alert cannot change
    // once it has been raised, so refetching on every expand would be a
    // request whose answer is already known.
    if (traces[alertId] !== undefined) {
      return;
    }

    try {
      const trace = await api.trace(alertId);

      setTraces((current) => ({ ...current, [alertId]: trace }));
    } catch {
      setTraceError("The readings behind that alert could not be loaded.");
    }
  }

  async function simulateFall(residentId: string) {
    setSimulating(true);
    setNotice(null);

    try {
      await api.simulate(residentId, "fall");
    } catch (error) {
      if (error instanceof ApiError && error.status === 404) {
        setNotice(
          "The simulator is switched off in this environment. It is enabled in development only, so fabricated readings cannot reach a real deployment.",
        );
      } else {
        setNotice("The simulation could not be started.");
      }
    } finally {
      setSimulating(false);
    }
  }

  if (residents.length === 0) {
    return (
      <Card>
        <CardHeader>
          <CardTitle>No residents assigned</CardTitle>
          <CardDescription>
            You will not receive alerts until a resident assigns you.
          </CardDescription>
        </CardHeader>

        <CardContent className="text-sm text-muted-foreground">
          <p>
            This is not an empty alert list. Nobody has given this account
            access to a resident, so there is nothing it could be told about.
            Someone holding the resident account has to add you from their
            settings page.
          </p>
        </CardContent>
      </Card>
    );
  }

  const open = alerts.filter(isOpen);

  return (
    <div className="flex flex-col gap-4">
      <Card>
        <CardHeader
          style={{ marginTop: "calc(var(--card-spacing) * -1)" }}
          className={`flex flex-row items-start justify-between gap-4 rounded-t-xl border-b pt-(--card-spacing) pb-(--card-spacing) ${
            open.length > 0 || connection === "disconnected"
              ? "border-accent-foreground/10 bg-accent"
              : "border-border bg-secondary/40"
          }`}
        >
          <div className="flex flex-col gap-1.5">
            <CardTitle className="flex items-center gap-2">
              <AlertTriangle
                className={`size-4 ${
                  open.length > 0 || connection === "disconnected"
                    ? "text-accent-foreground"
                    : "text-muted-foreground"
                }`}
                aria-hidden="true"
              />
              Alerts
            </CardTitle>
            <CardDescription>
              {open.length === 0
                ? `No open alerts across ${residents.length} resident${residents.length === 1 ? "" : "s"}.`
                : `${open.length} open alert${open.length === 1 ? "" : "s"}.`}
            </CardDescription>
            <p className="text-xs text-muted-foreground">
              Showing open alerts only. Once acknowledged, an alert moves to
              the Summary tab instead of disappearing.
            </p>
          </div>

          <span
            className="flex items-center gap-2 text-sm text-muted-foreground"
            aria-live="polite"
          >
            <span
              aria-hidden="true"
              className={
                connection === "connected"
                  ? "size-2 rounded-full bg-emerald-500"
                  : connection === "disconnected"
                    ? "size-2 rounded-full bg-destructive"
                    : "size-2 animate-pulse rounded-full bg-amber-500"
              }
            />
            {connectionLabels[connection]}
          </span>
        </CardHeader>

        <CardContent className="flex flex-col gap-4">
          {connection === "disconnected" && (
            <p role="alert" className="text-sm text-destructive">
              The live connection is down, so new alerts will not appear on
              their own. Reload to check for alerts raised in the meantime.
            </p>
          )}

          {loadError !== null && (
            <p role="alert" className="text-sm text-destructive">
              {loadError}
            </p>
          )}

          {notice !== null && (
            <p role="status" className="text-sm text-muted-foreground">
              {notice}
            </p>
          )}

          {loading ? (
            <p className="flex items-center gap-2 text-sm text-muted-foreground">
              <Loader2 className="size-4 animate-spin" aria-hidden="true" />
              Loading alerts…
            </p>
          ) : alerts.length === 0 ? (
            <p className="text-sm text-muted-foreground">
              Nothing to show. Raise a simulated fall below to watch an alert
              arrive.
            </p>
          ) : (
            <Table>
              <TableHeader>
                <TableRow>
                  <TableHead>Resident</TableHead>
                  <TableHead>Kind</TableHead>
                  <TableHead>Raised</TableHead>
                  <TableHead>Escalation</TableHead>
                  <TableHead>State</TableHead>
                  <TableHead className="text-right">Action</TableHead>
                </TableRow>
              </TableHeader>

              <TableBody>
                {alerts.map((alert) => (
                  <Fragment key={alert.id}>
                  <TableRow>
                    <TableCell className="font-medium">
                      <button
                        type="button"
                        onClick={() => void toggleTrace(alert.id)}
                        className="flex items-center gap-1.5 text-left hover:underline"
                        aria-expanded={expanded === alert.id}
                        aria-controls={`trace-${alert.id}`}
                      >
                        {expanded === alert.id ? (
                          <ChevronDown className="size-4 shrink-0" aria-hidden="true" />
                        ) : (
                          <ChevronRight className="size-4 shrink-0" aria-hidden="true" />
                        )}
                        {alert.residentDisplayName}
                      </button>
                    </TableCell>

                    <TableCell>
                      <span className="flex items-center gap-1.5">
                        {isOpen(alert) && (
                          <AlertTriangle
                            className="size-4 text-destructive"
                            aria-hidden="true"
                          />
                        )}
                        {alert.kind}
                      </span>
                    </TableCell>

                    <TableCell
                      title={new Date(alert.raisedAt).toLocaleString()}
                      className={
                        isOpen(alert) && secondsWaiting(alert, now) >= 5
                          ? "text-destructive"
                          : undefined
                      }
                    >
                      {describeWait(alert, now)}
                    </TableCell>

                    <TableCell>
                      {alert.dispatchedAt !== null ? (
                        <span className="flex items-center gap-1.5 text-destructive">
                          <Siren className="size-4" aria-hidden="true" />
                          Emergency call
                        </span>
                      ) : (
                        caregiverAtLevel(alert)
                      )}
                    </TableCell>

                    <TableCell>
                      {alert.acknowledgedAt === null ? (
                        <span className="text-destructive">Waiting</span>
                      ) : (
                        <span className="flex items-center gap-1.5 text-muted-foreground">
                          <Check className="size-4" aria-hidden="true" />
                          Acknowledged
                        </span>
                      )}
                    </TableCell>

                    <TableCell className="text-right">
                      {alert.acknowledgedAt === null && (
                        <Button
                          size="sm"
                          onClick={() => void acknowledge(alert)}
                          disabled={acknowledging === alert.id}
                        >
                          {acknowledging === alert.id ? "Acknowledging…" : "Acknowledge"}
                        </Button>
                      )}
                    </TableCell>
                  </TableRow>

                  {expanded === alert.id && (
                    <TableRow>
                      <TableCell colSpan={6} id={`trace-${alert.id}`}>
                        {traceError !== null ? (
                          <p role="alert" className="text-sm text-destructive">
                            {traceError}
                          </p>
                        ) : traces[alert.id] === undefined ? (
                          <p className="flex items-center gap-2 text-sm text-muted-foreground">
                            <Loader2 className="size-4 animate-spin" aria-hidden="true" />
                            Loading readings…
                          </p>
                        ) : (
                          <AccelerationTrace trace={traces[alert.id]} />
                        )}
                      </TableCell>
                    </TableRow>
                  )}
                  </Fragment>
                ))}
              </TableBody>
            </Table>
          )}
        </CardContent>
      </Card>

      <Card>
        <CardHeader
          style={{ marginTop: "calc(var(--card-spacing) * -1)" }}
          className="rounded-t-xl border-b border-primary/15 bg-primary/8 pt-(--card-spacing) pb-(--card-spacing)"
        >
          <CardTitle className="flex items-center gap-2">
            <FlaskConical className="size-4 text-primary" aria-hidden="true" />
            Simulate
          </CardTitle>
          <CardDescription>
            Raises a fall from recorded sample readings, so detection and
            escalation can be watched end to end.
          </CardDescription>
        </CardHeader>

        <CardContent className="flex flex-col gap-3">
          <div className="flex flex-wrap gap-2">
            {residents.map((resident) => (
              <Button
                key={resident.id}
                variant="outline"
                size="sm"
                onClick={() => void simulateFall(resident.id)}
                disabled={simulating}
              >
                Fall — {resident.displayName}
              </Button>
            ))}
          </div>

          <p className="text-xs text-muted-foreground">
            Synthetic data only. The simulator is enabled in development and
            off by default everywhere else, so a deployment cannot be fed
            fabricated readings.
          </p>
        </CardContent>
      </Card>
    </div>
  );
}
