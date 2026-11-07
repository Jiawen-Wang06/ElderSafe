export type AccountType = "Resident" | "Caregiver";

export interface ResidentSummary {
  id: string;
  displayName: string;
}

export interface CurrentAccount {
  id: string;
  displayName: string;
  userName: string;
  accountType: AccountType;
  residents: ResidentSummary[];
}

export interface AlertView {
  id: string;
  residentId: string;
  residentDisplayName: string;
  kind: string;
  raisedAt: string;
  escalationLevel: number;
  acknowledgedAt: string | null;
  dispatchedAt: string | null;
}

/** One point on an alert's acceleration trace. */
export interface TraceSample {
  at: string;
  magnitudeG: number;
}

/**
 * The thresholds the detector was using when it judged this trace.
 *
 * Reported by the API rather than fixed here. They live in server
 * configuration and can be retuned without a rebuild (ADR-002), so a chart
 * drawing them from its own constants would, after the first retune,
 * confidently draw a line the detector no longer uses.
 */
export interface DetectorThresholds {
  freeFallG: number;
  impactG: number;
  stillnessCentreG: number;
  stillnessToleranceG: number;
  settleAfterImpactSeconds: number;
  stillnessDurationSeconds: number;
}

/** The sensor data behind one alert (REQ-FALL-08). */
export interface AlertTrace {
  alertId: string;
  residentId: string;
  residentDisplayName: string;
  kind: string;
  raisedAt: string;
  windowStart: string;
  windowEnd: string;
  thresholds: DetectorThresholds;
  samples: TraceSample[];
}

/** Counts for one alert kind within one bucket (REQ-FALL-09, REQ-FALL-10). */
export interface AlertKindCount {
  kind: string;
  total: number;
  respondedInTime: number;
  escalated: number;
  emergencyCalled: number;
}

/** One period's counts in an alert summary. */
export interface SummaryBucket {
  periodStart: string;
  periodEnd: string;
  counts: AlertKindCount[];
}

/**
 * One alert in a summary's range, for the list behind the bucket counts
 * (REQ-FALL-10).
 *
 * `acknowledgedByDisplayName` is null until someone has — there is no "no
 * one" placeholder, since a reader should not have to tell "not yet" apart
 * from "by someone unnamed."
 */
export interface SummaryAlertEntry {
  id: string;
  kind: string;
  raisedAt: string;
  acknowledgedAt: string | null;
  acknowledgedByDisplayName: string | null;
  emergencyCalled: boolean;
}

/** Alert counts for a resident over a date range (REQ-FALL-09, REQ-FALL-10). */
export interface AlertSummary {
  residentId: string;
  residentDisplayName: string;
  from: string;
  to: string;
  granularity: "day" | "week" | "month";
  buckets: SummaryBucket[];
  alerts: SummaryAlertEntry[];
}

/** A resident's alerting configuration, as shown and edited (REQ-CFG-01, REQ-CFG-06). */
export interface ResidentSettingsView {
  residentId: string;
  displayName: string;
  escalationLimit: number | null;
  emergencyCallEnabled: boolean;
  emergencyContactPhoneNumber: string | null;
  assignedCaregiverCount: number;
}

/** A caregiver assigned to a resident. */
export interface AssignedCaregiverView {
  caregiverId: string;
  displayName: string;
  userName: string;
  order: number;
}

/** Whether an exact-match caregiver email lookup found an account (REQ-CFG-07). */
export interface CaregiverLookupResult {
  found: boolean;
  displayName: string | null;
}

/**
 * A request the API refused, carrying enough to show the person why.
 *
 * The fields are declared and then assigned rather than written as
 * constructor parameter properties. Parameter properties are the one piece
 * of TypeScript that emits real code instead of vanishing at compile time,
 * and Vite strips types without type-checking them, so the project turns on
 * erasableSyntaxOnly to keep that kind of syntax out.
 */
export class ApiError extends Error {
  readonly status: number;
  readonly details: string[];

  constructor(status: number, message: string, details: string[] = []) {
    super(message);
    this.name = "ApiError";
    this.status = status;
    this.details = details;
  }
}

/**
 * Calls the API with the session cookie attached.
 *
 * Relative paths only. In development the Vite dev server proxies these, so
 * the browser only ever talks to one origin — which is what lets the auth
 * cookie stay SameSite=Strict instead of being relaxed to suit the tooling.
 */
async function request<T>(path: string, init?: RequestInit): Promise<T> {
  const response = await fetch(path, {
    ...init,
    credentials: "include",
    headers: {
      "Content-Type": "application/json",
      ...(init?.headers ?? {}),
    },
  });

  if (!response.ok) {
    throw new ApiError(response.status, await describe(response), await detailsOf(response));
  }

  if (response.status === 204) {
    return undefined as T;
  }

  return (await response.json()) as T;
}

async function describe(response: Response): Promise<string> {
  if (response.status === 401) {
    return "Sign-in failed.";
  }

  if (response.status === 404) {
    return "Not found.";
  }

  return `The request failed (${response.status}).`;
}

async function detailsOf(response: Response): Promise<string[]> {
  try {
    const body = await response.clone().json();

    // Our own handlers return a flat array of messages.
    if (Array.isArray(body?.errors)) {
      return body.errors as string[];
    }

    // ASP.NET model binding returns ValidationProblemDetails instead, where
    // errors is an object of field name to messages. Flattening it means a
    // binding failure shows its reason rather than a bare 400.
    if (body?.errors !== null && typeof body?.errors === "object") {
      return Object.entries(body.errors as Record<string, string[]>).flatMap(
        ([field, messages]) =>
          messages.map((message) => (field.length > 0 ? `${field}: ${message}` : message)),
      );
    }

    if (typeof body?.message === "string") {
      return [body.message];
    }

    if (typeof body?.title === "string") {
      return [body.title];
    }
  } catch {
    // A response without a JSON body is normal; there is simply no detail.
  }

  return [];
}

export const api = {
  signIn(userName: string, password: string): Promise<void> {
    return request<void>("/api/auth/sign-in", {
      method: "POST",
      body: JSON.stringify({ userName, password }),
    });
  },

  signOut(): Promise<void> {
    return request<void>("/api/auth/sign-out", { method: "POST" });
  },

  register(
    userName: string,
    password: string,
    displayName: string,
    accountType: AccountType,
  ): Promise<void> {
    return request<void>("/api/auth/register", {
      method: "POST",
      body: JSON.stringify({ userName, password, displayName, accountType }),
    });
  },

  me(): Promise<CurrentAccount> {
    return request<CurrentAccount>("/api/auth/me");
  },

  alerts(residentId: string, openOnly = true): Promise<AlertView[]> {
    return request<AlertView[]>(
      `/api/residents/${residentId}/alerts?openOnly=${openOnly}`,
    );
  },

  trace(alertId: string): Promise<AlertTrace> {
    return request<AlertTrace>(`/api/alerts/${alertId}/trace`);
  },

  summary(residentId: string, from: Date, to: Date): Promise<AlertSummary> {
    const query = new URLSearchParams({
      from: from.toISOString(),
      to: to.toISOString(),
    });

    return request<AlertSummary>(`/api/residents/${residentId}/alerts/summary?${query}`);
  },

  acknowledge(alertId: string): Promise<void> {
    return request<void>(`/api/alerts/${alertId}/acknowledge`, { method: "POST" });
  },

  simulate(residentId: string, scenario: string): Promise<void> {
    return request<void>(`/api/residents/${residentId}/simulate/${scenario}`, {
      method: "POST",
    });
  },

  residentSettings(residentId: string): Promise<ResidentSettingsView> {
    return request<ResidentSettingsView>(`/api/residents/${residentId}/settings`);
  },

  updateResidentSettings(
    residentId: string,
    settings: {
      escalationLimit: number | null;
      emergencyCallEnabled: boolean;
      emergencyContactPhoneNumber: string | null;
    },
  ): Promise<void> {
    return request<void>(`/api/residents/${residentId}/settings`, {
      method: "PUT",
      body: JSON.stringify({
        escalationLimit: settings.escalationLimit,
        emergencyCallEnabled: settings.emergencyCallEnabled,
        emergencyContactPhoneNumber: settings.emergencyContactPhoneNumber,
      }),
    });
  },

  caregivers(residentId: string): Promise<AssignedCaregiverView[]> {
    return request<AssignedCaregiverView[]>(`/api/residents/${residentId}/caregivers`);
  },

  assignCaregiver(residentId: string, userName: string): Promise<void> {
    return request<void>(`/api/residents/${residentId}/caregivers`, {
      method: "POST",
      body: JSON.stringify({ userName }),
    });
  },

  removeCaregiver(residentId: string, caregiverId: string): Promise<void> {
    return request<void>(`/api/residents/${residentId}/caregivers/${caregiverId}`, {
      method: "DELETE",
    });
  },

  /** Rearranges existing assignments (REQ-CFG-08); never adds or removes one. */
  reorderCaregivers(residentId: string, caregiverIds: string[]): Promise<void> {
    return request<void>(`/api/residents/${residentId}/caregivers/order`, {
      method: "PUT",
      body: JSON.stringify({ caregiverIds }),
    });
  },

  lookupCaregiver(residentId: string, email: string): Promise<CaregiverLookupResult> {
    const query = new URLSearchParams({ email });
    return request<CaregiverLookupResult>(
      `/api/residents/${residentId}/caregivers/lookup?${query}`,
    );
  },
};
