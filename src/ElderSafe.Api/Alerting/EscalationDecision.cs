namespace ElderSafe.Api.Alerting;

/// <summary>
/// What should happen to an alert now.
/// </summary>
public enum EscalationAction
{
    /// <summary>Nothing. The reason on the decision says why.</summary>
    None,

    /// <summary>Notify the caregiver at the given escalation level.</summary>
    NotifyCaregiver,

    /// <summary>Place an emergency call for this alert.</summary>
    PlaceEmergencyCall
}

/// <summary>
/// The policy's decision about one alert, with the reason for it.
/// </summary>
/// <remarks>
/// As with <c>FallVerdict</c>, the reason is part of the answer. "The
/// escalation chain is exhausted and emergency calling is off" explains an
/// alert that sat open without escalating; an enum value on its own does not.
/// </remarks>
public readonly record struct EscalationDecision(
    EscalationAction Action,
    int? CaregiverLevel,
    string Reason)
{
    public static EscalationDecision None(string reason)
    {
        return new EscalationDecision(EscalationAction.None, null, reason);
    }

    public static EscalationDecision NotifyCaregiver(int level)
    {
        return new EscalationDecision(
            EscalationAction.NotifyCaregiver,
            level,
            $"the caregiver at level {level - 1} did not respond in time");
    }

    public static EscalationDecision PlaceEmergencyCall()
    {
        return new EscalationDecision(
            EscalationAction.PlaceEmergencyCall,
            null,
            "every configured caregiver failed to respond");
    }
}
