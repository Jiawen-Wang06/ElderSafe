import { useEffect, useState } from "react";
import { Check, Loader2, Search, Trash2, X } from "lucide-react";
import { useAuth } from "@/auth/useAuth";
import { api, ApiError } from "@/lib/api";
import type { AssignedCaregiverView, CaregiverLookupResult, ResidentSettingsView } from "@/lib/api";
import { Button } from "@/components/ui/button";
import { Card, CardContent, CardDescription, CardHeader, CardTitle } from "@/components/ui/card";
import { Input } from "@/components/ui/input";
import {
  Table,
  TableBody,
  TableCell,
  TableHead,
  TableHeader,
  TableRow,
} from "@/components/ui/table";

const escalationOptions = Array.from({ length: 10 }, (_, index) => index + 1);

/**
 * The shown-when-nothing-is-configured placeholder (REQ-CFG-06).
 *
 * Deliberately not a real phone number: a resident who has not set a
 * contact should see that a fallback exists, not a number that looks like
 * it is already dialling somewhere.
 */
const DEFAULT_EMERGENCY_CONTACT_PLACEHOLDER = "911 (default — not configured by you)";

/**
 * Configuration for a resident account: who is assigned to be notified, how
 * far an unanswered alert escalates, and what happens if nobody answers
 * (REQ-CFG-01, REQ-CFG-06, REQ-CFG-07).
 *
 * Every mutation here goes through the API's own checks (the last caregiver
 * cannot be removed, the limit is clamped to 1–10), so this page does not
 * duplicate that logic — it reflects the server's refusal back as a notice
 * instead of trying to predict it.
 */
export function ResidentSettingsPage() {
  const { account } = useAuth();
  const resident = account?.residents[0] ?? null;

  const [settings, setSettings] = useState<ResidentSettingsView | null>(null);
  const [caregivers, setCaregivers] = useState<AssignedCaregiverView[]>([]);
  const [loading, setLoading] = useState(true);
  const [loadError, setLoadError] = useState<string | null>(null);

  const [escalationLimit, setEscalationLimit] = useState<number | null>(null);
  const [emergencyCallEnabled, setEmergencyCallEnabled] = useState(false);
  const [contactNumber, setContactNumber] = useState("");
  const [saving, setSaving] = useState(false);
  const [saveNotice, setSaveNotice] = useState<string | null>(null);
  const [saveError, setSaveError] = useState<string | null>(null);

  const [removing, setRemoving] = useState<string | null>(null);
  const [removeError, setRemoveError] = useState<string | null>(null);

  const [addEmail, setAddEmail] = useState("");
  const [searchedEmail, setSearchedEmail] = useState<string | null>(null);
  const [lookup, setLookup] = useState<CaregiverLookupResult | null>(null);
  const [looking, setLooking] = useState(false);
  const [lookupError, setLookupError] = useState<string | null>(null);
  const [adding, setAdding] = useState(false);
  const [addError, setAddError] = useState<string | null>(null);

  // Loaded once per resident. The settings form's own fields are seeded from
  // the result but then live independently of it, so refreshing the
  // caregiver list elsewhere does not clobber whatever the resident is
  // mid-typing.
  useEffect(() => {
    if (resident === null) {
      return;
    }

    let cancelled = false;

    // As in SummaryPanel, every setState call here runs as a callback from
    // the fetch rather than synchronously in the effect body
    // (react-hooks/set-state-in-effect).
    Promise.resolve()
      .then(() => {
        if (cancelled) {
          return undefined;
        }

        setLoading(true);
        setLoadError(null);

        return Promise.all([api.residentSettings(resident.id), api.caregivers(resident.id)]);
      })
      .then((result) => {
        if (cancelled || result === undefined) {
          return;
        }

        const [settingsResult, caregiversResult] = result;

        setSettings(settingsResult);
        setEscalationLimit(settingsResult.escalationLimit);
        setEmergencyCallEnabled(settingsResult.emergencyCallEnabled);
        setContactNumber(settingsResult.emergencyContactPhoneNumber ?? "");
        setCaregivers(caregiversResult);
      })
      .catch(() => {
        if (!cancelled) {
          setLoadError("Could not load this resident's configuration.");
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
  }, [resident]);

  if (resident === null) {
    return (
      <Card>
        <CardHeader>
          <CardTitle>Settings</CardTitle>
          <CardDescription>This account does not hold a resident's data.</CardDescription>
        </CardHeader>
      </Card>
    );
  }

  async function saveSettings() {
    if (resident === null) {
      return;
    }

    setSaving(true);
    setSaveNotice(null);
    setSaveError(null);

    const normalizedContact = contactNumber.trim().length === 0 ? null : contactNumber.trim();

    try {
      await api.updateResidentSettings(resident.id, {
        escalationLimit,
        emergencyCallEnabled,
        emergencyContactPhoneNumber: normalizedContact,
      });

      setSettings((current) =>
        current === null
          ? current
          : {
            ...current,
            escalationLimit,
            emergencyCallEnabled,
            emergencyContactPhoneNumber: normalizedContact,
          },
      );
      setSaveNotice("Saved.");
    } catch (error) {
      setSaveError(
        error instanceof ApiError && error.details.length > 0
          ? error.details.join(" ")
          : "Could not save these settings.",
      );
    } finally {
      setSaving(false);
    }
  }

  async function removeCaregiver(caregiver: AssignedCaregiverView) {
    if (resident === null) {
      return;
    }

    setRemoving(caregiver.caregiverId);
    setRemoveError(null);

    try {
      await api.removeCaregiver(resident.id, caregiver.caregiverId);
      setCaregivers((current) => current.filter((c) => c.caregiverId !== caregiver.caregiverId));
      setSettings((current) =>
        current === null
          ? current
          : { ...current, assignedCaregiverCount: current.assignedCaregiverCount - 1 },
      );
    } catch (error) {
      setRemoveError(
        error instanceof ApiError && error.details.length > 0
          ? error.details.join(" ")
          : "That caregiver could not be removed.",
      );
    } finally {
      setRemoving(null);
    }
  }

  // A deliberate click rather than search-as-you-type (REQ-CFG-07): typing
  // triggers a lookup for every prefix the resident happens to pause on,
  // and even though each one only answers "does this exact string match,"
  // a result appearing after every keystroke reads like the system is
  // narrowing in on someone else's address. One lookup per explicit
  // request is the simpler thing to reason about.
  async function searchCaregiver() {
    if (resident === null) {
      return;
    }

    const trimmed = addEmail.trim();

    if (trimmed.length === 0) {
      return;
    }

    setLooking(true);
    setLookupError(null);

    try {
      const result = await api.lookupCaregiver(resident.id, trimmed);

      setLookup(result);
      setSearchedEmail(trimmed);
    } catch {
      setLookup(null);
      setSearchedEmail(null);
      setLookupError("Could not look up that email.");
    } finally {
      setLooking(false);
    }
  }

  async function addCaregiver() {
    if (resident === null) {
      return;
    }

    const trimmed = addEmail.trim();

    if (trimmed.length === 0 || lookup?.found !== true || searchedEmail !== trimmed) {
      return;
    }

    setAdding(true);
    setAddError(null);

    try {
      await api.assignCaregiver(resident.id, trimmed);
      const refreshed = await api.caregivers(resident.id);

      setCaregivers(refreshed);
      setSettings((current) =>
        current === null ? current : { ...current, assignedCaregiverCount: refreshed.length },
      );
      setAddEmail("");
      setSearchedEmail(null);
      setLookup(null);
    } catch (error) {
      setAddError(
        error instanceof ApiError && error.details.length > 0
          ? error.details.join(" ")
          : "That caregiver could not be added.",
      );
    } finally {
      setAdding(false);
    }
  }

  return (
    <div className="flex flex-col gap-4">
      <Card>
        <CardHeader>
          <CardTitle>Settings</CardTitle>
          <CardDescription>Configuration for {resident.displayName}</CardDescription>
        </CardHeader>

        <CardContent className="flex flex-col gap-4">
          {loadError !== null && (
            <p role="alert" className="text-sm text-destructive">
              {loadError}
            </p>
          )}

          {loading ? (
            <p className="flex items-center gap-2 text-sm text-muted-foreground">
              <Loader2 className="size-4 animate-spin" aria-hidden="true" />
              Loading…
            </p>
          ) : (
            settings !== null && (
              <>
                <div className="flex flex-col gap-1.5">
                  <label htmlFor="escalation-limit" className="text-sm font-medium">
                    Escalation limit
                  </label>
                  <p className="text-xs text-muted-foreground">
                    How many assigned caregivers an unanswered alert travels
                    through before it reaches the emergency-call step.
                  </p>
                  <select
                    id="escalation-limit"
                    className="h-8 w-full max-w-60 rounded-lg border border-input bg-transparent px-2.5 text-sm outline-none focus-visible:border-ring focus-visible:ring-3 focus-visible:ring-ring/50 dark:bg-input/30"
                    value={escalationLimit === null ? "all" : escalationLimit}
                    onChange={(event) =>
                      setEscalationLimit(
                        event.target.value === "all" ? null : Number(event.target.value),
                      )
                    }
                  >
                    <option value="all">All assigned caregivers</option>
                    {escalationOptions.map((value) => (
                      <option key={value} value={value}>
                        {value}
                      </option>
                    ))}
                  </select>
                </div>

                <div className="flex flex-col gap-1.5">
                  <label className="flex items-center gap-2 text-sm font-medium">
                    <input
                      type="checkbox"
                      className="size-4 rounded border border-input accent-primary"
                      checked={emergencyCallEnabled}
                      onChange={(event) => setEmergencyCallEnabled(event.target.checked)}
                    />
                    Place an emergency call if escalation is exhausted
                  </label>
                  <p className="text-xs text-muted-foreground">
                    This demo only ever logs a simulated call. Nothing in
                    this project can dial a real number, in this environment
                    or any other.
                  </p>
                </div>

                <div className="flex flex-col gap-1.5">
                  <label htmlFor="contact-number" className="text-sm font-medium">
                    Emergency contact number
                  </label>
                  <Input
                    id="contact-number"
                    type="tel"
                    className="max-w-60"
                    placeholder={DEFAULT_EMERGENCY_CONTACT_PLACEHOLDER}
                    value={contactNumber}
                    onChange={(event) => setContactNumber(event.target.value)}
                  />
                  <p className="text-xs text-muted-foreground">
                    {contactNumber.trim().length === 0
                      ? "Not configured — falls back to the default emergency contact."
                      : "Shown back to you and recorded in the simulated call log, never dialled."}
                  </p>
                </div>

                <div>
                  <Button onClick={() => void saveSettings()} disabled={saving}>
                    {saving ? "Saving…" : "Save settings"}
                  </Button>
                </div>

                {saveNotice !== null && (
                  <p role="status" className="text-sm text-muted-foreground">
                    {saveNotice}
                  </p>
                )}

                {saveError !== null && (
                  <p role="alert" className="text-sm text-destructive">
                    {saveError}
                  </p>
                )}
              </>
            )
          )}
        </CardContent>
      </Card>

      <Card>
        <CardHeader>
          <CardTitle>Caregivers</CardTitle>
          <CardDescription>
            {settings === null
              ? "Who is notified when an alert is raised."
              : `${settings.assignedCaregiverCount} assigned.`}
          </CardDescription>
        </CardHeader>

        <CardContent className="flex flex-col gap-4">
          {removeError !== null && (
            <p role="alert" className="text-sm text-destructive">
              {removeError}
            </p>
          )}

          {caregivers.length === 0 ? (
            <p className="text-sm text-muted-foreground">No caregivers assigned yet.</p>
          ) : (
            <Table>
              <TableHeader>
                <TableRow>
                  <TableHead>Order</TableHead>
                  <TableHead>Name</TableHead>
                  <TableHead>Email</TableHead>
                  <TableHead className="text-right">Action</TableHead>
                </TableRow>
              </TableHeader>

              <TableBody>
                {caregivers.map((caregiver) => (
                  <TableRow key={caregiver.caregiverId}>
                    <TableCell>{caregiver.order + 1}</TableCell>
                    <TableCell className="font-medium">{caregiver.displayName}</TableCell>
                    <TableCell className="text-muted-foreground">{caregiver.userName}</TableCell>
                    <TableCell className="text-right">
                      <Button
                        variant="ghost"
                        size="icon-sm"
                        aria-label={`Remove ${caregiver.displayName}`}
                        onClick={() => void removeCaregiver(caregiver)}
                        disabled={removing === caregiver.caregiverId || caregivers.length <= 1}
                        title={
                          caregivers.length <= 1
                            ? "A resident must keep at least one caregiver."
                            : undefined
                        }
                      >
                        {removing === caregiver.caregiverId ? (
                          <Loader2 className="size-4 animate-spin" aria-hidden="true" />
                        ) : (
                          <Trash2 className="size-4" aria-hidden="true" />
                        )}
                      </Button>
                    </TableCell>
                  </TableRow>
                ))}
              </TableBody>
            </Table>
          )}

          <div className="flex flex-col gap-1.5 border-t pt-4">
            <label htmlFor="add-caregiver-email" className="text-sm font-medium">
              Add a caregiver
            </label>
            <p className="text-xs text-muted-foreground">
              Enter the full email a caregiver registered with, then search.
              An account must already exist — this does not invite or
              create one.
            </p>

            <div className="flex max-w-96 items-center gap-2">
              <Input
                id="add-caregiver-email"
                type="email"
                placeholder="caregiver@example.com"
                value={addEmail}
                onChange={(event) => {
                  setAddEmail(event.target.value);

                  // Any edit invalidates the last search: a result that
                  // matched a different string must not silently keep
                  // authorizing Add once the text no longer says that.
                  setSearchedEmail(null);
                  setLookup(null);
                }}
                onKeyDown={(event) => {
                  if (event.key === "Enter") {
                    event.preventDefault();
                    void searchCaregiver();
                  }
                }}
              />
              <Button
                type="button"
                variant="outline"
                onClick={() => void searchCaregiver()}
                disabled={looking || addEmail.trim().length === 0}
              >
                {looking ? (
                  <Loader2 className="size-4 animate-spin" aria-hidden="true" />
                ) : (
                  <Search className="size-4" aria-hidden="true" />
                )}
                Search
              </Button>
              <Button
                onClick={() => void addCaregiver()}
                disabled={adding || lookup?.found !== true || searchedEmail !== addEmail.trim()}
              >
                {adding ? "Adding…" : "Add"}
              </Button>
            </div>

            {lookupError !== null && (
              <p role="alert" className="text-sm text-destructive">
                {lookupError}
              </p>
            )}

            {lookup !== null && searchedEmail === addEmail.trim() && (
              <p
                className={
                  "flex items-center gap-1.5 text-xs " +
                  (lookup.found ? "text-emerald-600" : "text-muted-foreground")
                }
                role="status"
              >
                {lookup.found ? (
                  <>
                    <Check className="size-3.5" aria-hidden="true" />
                    Matched: {lookup.displayName}
                  </>
                ) : (
                  <>
                    <X className="size-3.5" aria-hidden="true" />
                    No caregiver is registered with that email.
                  </>
                )}
              </p>
            )}

            {addError !== null && (
              <p role="alert" className="text-sm text-destructive">
                {addError}
              </p>
            )}
          </div>
        </CardContent>
      </Card>
    </div>
  );
}
