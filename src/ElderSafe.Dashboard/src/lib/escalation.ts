/**
 * The escalation-limit values worth offering a resident, given how many
 * caregivers they currently have assigned (REQ-CFG-09).
 *
 * Capped at the assigned count rather than a flat ten: a limit higher than
 * that is already clamped down to the assigned count the moment an alert
 * escalates (`ResidentAlertSettings.EffectiveEscalationLimit` on the
 * server), so offering it here would just be an option that quietly does
 * something other than what it says. The cap tracks the assigned count
 * exactly and nothing else — the caller is responsible for also clamping
 * whatever value it is about to display or save down to this same cap, so
 * a stale higher number from before a caregiver was removed is corrected
 * rather than shown as an extra, unexplained option.
 */
export function selectableEscalationLimits(assignedCaregiverCount: number): number[] {
  const cap = Math.max(1, Math.min(10, assignedCaregiverCount));

  return Array.from({ length: cap }, (_, index) => index + 1);
}
