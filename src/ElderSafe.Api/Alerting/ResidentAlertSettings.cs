namespace ElderSafe.Api.Alerting;

/// <summary>
/// A resident's alerting configuration (REQ-FALL-04, REQ-FALL-05).
/// </summary>
/// <param name="AssignedCaregiverCount">How many caregivers are assigned, in escalation order.</param>
/// <param name="EscalationLimit">
/// How many caregivers an alert may travel through. Null means every assigned
/// caregiver, which is the default.
/// </param>
/// <param name="EmergencyCallEnabled">
/// Whether an emergency call is placed once the chain is exhausted. Off by
/// default: a call nobody asked for is RISK-09.
/// </param>
public sealed record ResidentAlertSettings(
    int AssignedCaregiverCount,
    int? EscalationLimit = null,
    bool EmergencyCallEnabled = false)
{
    /// <summary>
    /// The limit actually applied, after clamping to something achievable.
    /// </summary>
    /// <remarks>
    /// Clamped here as well as validated when the setting is saved. A stored
    /// value could be out of range because it was written before a caregiver
    /// was unassigned, or by an older version of the application, and a limit
    /// of zero would silently mean "notify nobody" (RISK-07). Deciding what an
    /// impossible configuration means is the policy's job, not the caller's.
    /// </remarks>
    public int EffectiveEscalationLimit
    {
        get
        {
            if (AssignedCaregiverCount <= 0)
            {
                return 0;
            }

            var requested = EscalationLimit ?? AssignedCaregiverCount;

            return Math.Clamp(requested, 1, AssignedCaregiverCount);
        }
    }
}
