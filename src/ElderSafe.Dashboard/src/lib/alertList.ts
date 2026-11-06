import type { AlertView } from "@/lib/api";

/**
 * An alert nobody has acknowledged yet.
 *
 * Acknowledgement is the thing that stops escalation (REQ-FALL-03), so it is
 * what "still needs someone" means here. Dispatch is not: an alert that
 * reached the emergency-call stage without being acknowledged is the most
 * open an alert can be.
 */
export function isOpen(alert: AlertView): boolean {
  return alert.acknowledgedAt === null;
}

/**
 * Most recently raised first.
 *
 * Deliberately by <c>raisedAt</c> and not by escalation level: a caregiver
 * opening the dashboard wants the newest incident at the top, not the one
 * that has been shouting longest. Ties fall back to the identifier so the
 * order is stable between renders rather than depending on how the server
 * happened to return them.
 */
export function byMostRecentFirst(first: AlertView, second: AlertView): number {
  const difference = Date.parse(second.raisedAt) - Date.parse(first.raisedAt);

  if (difference !== 0) {
    return difference;
  }

  return first.id.localeCompare(second.id);
}

/**
 * Folds one pushed alert into the list already on screen.
 *
 * Both hub events go through here. An <c>alertUpdated</c> for something the
 * list has never seen is inserted rather than dropped, because that is what
 * arrives when an alert is raised and escalated in the gap between the fetch
 * and the subscription being established — dropping it would lose an alert
 * that the server believes was delivered.
 *
 * Last write wins, which means a late-arriving stale update can briefly
 * overwrite a newer one. The push is not the system of record: the list is
 * refetched after every reconnect, and the alert is stored before any
 * notification is attempted (RISK-08). A version or sequence number on
 * AlertView would be the real fix if this ever matters.
 */
export function mergeAlert(alerts: readonly AlertView[], incoming: AlertView): AlertView[] {
  const known = alerts.some((alert) => alert.id === incoming.id);

  const merged = known
    ? alerts.map((alert) => (alert.id === incoming.id ? incoming : alert))
    : [...alerts, incoming];

  return [...merged].sort(byMostRecentFirst);
}

/**
 * Combines the per-resident fetches into one list.
 *
 * Alerts are fetched one resident at a time, because that is the shape of the
 * endpoint and of the authorization check behind it. Deduplicating by
 * identifier keeps a resident appearing under two concurrent requests from
 * producing two rows.
 */
export function mergeAlerts(...batches: readonly AlertView[][]): AlertView[] {
  const byId = new Map<string, AlertView>();

  for (const batch of batches) {
    for (const alert of batch) {
      byId.set(alert.id, alert);
    }
  }

  return [...byId.values()].sort(byMostRecentFirst);
}

/**
 * How long an alert has been waiting, in whole seconds.
 *
 * Takes the current time as an argument rather than reading the clock, so a
 * test can assert on it. The dashboard passes a value that ticks once a
 * second, which is what makes REQ-FALL-02's five-second criterion something a
 * person can watch rather than something only a test knows about.
 */
export function secondsWaiting(alert: AlertView, now: number): number {
  return Math.max(0, Math.floor((now - Date.parse(alert.raisedAt)) / 1000));
}

/** A short human reading of how long an alert has been waiting. */
export function describeWait(alert: AlertView, now: number): string {
  const seconds = secondsWaiting(alert, now);

  if (seconds < 60) {
    return `${seconds}s ago`;
  }

  const minutes = Math.floor(seconds / 60);

  if (minutes < 60) {
    return `${minutes}m ago`;
  }

  const hours = Math.floor(minutes / 60);

  if (hours < 24) {
    return `${hours}h ago`;
  }

  return `${Math.floor(hours / 24)}d ago`;
}
