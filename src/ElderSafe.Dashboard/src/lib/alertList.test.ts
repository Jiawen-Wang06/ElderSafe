import { describe, expect, it } from "vitest";
import {
  byMostRecentFirst,
  describeWait,
  isOpen,
  mergeAlert,
  mergeAlerts,
  secondsWaiting,
} from "@/lib/alertList";
import type { AlertView } from "@/lib/api";

function alert(overrides: Partial<AlertView> = {}): AlertView {
  return {
    id: "00000000-0000-0000-0000-000000000001",
    residentId: "11111111-1111-1111-1111-111111111111",
    residentDisplayName: "Resident A",
    kind: "Fall",
    raisedAt: "2026-11-06T12:00:00.000Z",
    escalationLevel: 0,
    acknowledgedAt: null,
    dispatchedAt: null,
    ...overrides,
  };
}

describe("isOpen", () => {
  it("treats an unacknowledged alert as open", () => {
    expect(isOpen(alert())).toBe(true);
  });

  it("treats an acknowledged alert as closed", () => {
    expect(isOpen(alert({ acknowledgedAt: "2026-11-06T12:00:30.000Z" }))).toBe(false);
  });

  it("treats a dispatched but unacknowledged alert as still open", () => {
    expect(isOpen(alert({ dispatchedAt: "2026-11-06T12:02:00.000Z" }))).toBe(true);
  });
});

describe("byMostRecentFirst", () => {
  it("puts the later alert first", () => {
    const older = alert({ id: "a", raisedAt: "2026-11-06T12:00:00.000Z" });
    const newer = alert({ id: "b", raisedAt: "2026-11-06T12:05:00.000Z" });

    expect([older, newer].sort(byMostRecentFirst)).toEqual([newer, older]);
  });

  it("orders alerts raised at the same instant by identifier", () => {
    const first = alert({ id: "aaa" });
    const second = alert({ id: "bbb" });

    expect([second, first].sort(byMostRecentFirst)).toEqual([first, second]);
  });
});

describe("mergeAlert", () => {
  it("adds an alert the list has not seen", () => {
    const existing = alert({ id: "a" });
    const raised = alert({ id: "b", raisedAt: "2026-11-06T12:05:00.000Z" });

    expect(mergeAlert([existing], raised).map((entry) => entry.id)).toEqual(["b", "a"]);
  });

  it("replaces an alert it already holds rather than duplicating it", () => {
    const original = alert({ id: "a", escalationLevel: 0 });
    const escalated = alert({ id: "a", escalationLevel: 1 });

    const merged = mergeAlert([original], escalated);

    expect(merged).toHaveLength(1);
    expect(merged[0].escalationLevel).toBe(1);
  });

  it("inserts an update for an alert that was never in the list", () => {
    const acknowledged = alert({ id: "unseen", acknowledgedAt: "2026-11-06T12:00:30.000Z" });

    expect(mergeAlert([], acknowledged)).toEqual([acknowledged]);
  });

  it("does not mutate the list it was given", () => {
    const existing = alert({ id: "a" });
    const list = [existing];

    mergeAlert(list, alert({ id: "b" }));

    expect(list).toEqual([existing]);
  });

  it("keeps the newest alert first after a merge", () => {
    const middle = alert({ id: "b", raisedAt: "2026-11-06T12:02:00.000Z" });
    const oldest = alert({ id: "a", raisedAt: "2026-11-06T12:00:00.000Z" });
    const newest = alert({ id: "c", raisedAt: "2026-11-06T12:09:00.000Z" });

    expect(mergeAlert([middle, oldest], newest).map((entry) => entry.id)).toEqual([
      "c",
      "b",
      "a",
    ]);
  });
});

describe("mergeAlerts", () => {
  it("combines per-resident batches newest first", () => {
    const first = [alert({ id: "a", raisedAt: "2026-11-06T12:00:00.000Z" })];
    const second = [alert({ id: "b", raisedAt: "2026-11-06T12:05:00.000Z" })];

    expect(mergeAlerts(first, second).map((entry) => entry.id)).toEqual(["b", "a"]);
  });

  it("keeps one row when the same alert arrives in two batches", () => {
    const duplicated = alert({ id: "a" });

    expect(mergeAlerts([duplicated], [duplicated])).toHaveLength(1);
  });

  it("prefers the later batch when the same alert differs between them", () => {
    const stale = alert({ id: "a", escalationLevel: 0 });
    const fresh = alert({ id: "a", escalationLevel: 2 });

    expect(mergeAlerts([stale], [fresh])[0].escalationLevel).toBe(2);
  });

  it("returns an empty list when no resident has alerts", () => {
    expect(mergeAlerts([], [])).toEqual([]);
  });
});

describe("secondsWaiting", () => {
  it("counts whole seconds since the alert was raised", () => {
    const now = Date.parse("2026-11-06T12:00:04.900Z");

    expect(secondsWaiting(alert(), now)).toBe(4);
  });

  it("never reports a negative wait when a clock disagrees", () => {
    const now = Date.parse("2026-11-06T11:59:55.000Z");

    expect(secondsWaiting(alert(), now)).toBe(0);
  });
});

describe("describeWait", () => {
  it("reports seconds under a minute", () => {
    expect(describeWait(alert(), Date.parse("2026-11-06T12:00:03.000Z"))).toBe("3s ago");
  });

  it("reports minutes under an hour", () => {
    expect(describeWait(alert(), Date.parse("2026-11-06T12:20:00.000Z"))).toBe("20m ago");
  });

  it("reports hours under a day", () => {
    expect(describeWait(alert(), Date.parse("2026-11-06T15:00:00.000Z"))).toBe("3h ago");
  });

  it("reports days beyond that", () => {
    expect(describeWait(alert(), Date.parse("2026-11-08T12:00:00.000Z"))).toBe("2d ago");
  });
});
