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

  acknowledge(alertId: string): Promise<void> {
    return request<void>(`/api/alerts/${alertId}/acknowledge`, { method: "POST" });
  },

  simulate(residentId: string, scenario: string): Promise<void> {
    return request<void>(`/api/residents/${residentId}/simulate/${scenario}`, {
      method: "POST",
    });
  },
};
