namespace ElderSafe.Api.Alerting;

/// <summary>
/// Tuning values for <see cref="EscalationPolicy"/>.
/// </summary>
public sealed class EscalationOptions
{
    /// <summary>Configuration section name.</summary>
    public const string SectionName = "Escalation";

    /// <summary>
    /// How long a caregiver has to acknowledge before the alert moves on.
    /// </summary>
    /// <remarks>
    /// A placeholder until a clinician gives a real figure. The open question
    /// is in docs/03-user-needs.md section 5: two minutes per caregiver means
    /// a three-caregiver chain takes six minutes to exhaust, which is a long
    /// time for someone on the floor.
    /// </remarks>
    public TimeSpan ResponseTimeout { get; set; } = TimeSpan.FromMinutes(2);
}
