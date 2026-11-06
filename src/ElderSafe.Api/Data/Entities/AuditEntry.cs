namespace ElderSafe.Api.Data.Entities;

/// <summary>
/// One recorded access to, or action on, resident data (REQ-SEC-02).
/// </summary>
/// <remarks>
/// Append-only. <see cref="ElderSafeDbContext"/> refuses to save a modified or
/// deleted entry, so the rule is enforced by the context rather than by every
/// caller remembering it (RISK-12).
/// </remarks>
public class AuditEntry
{
    public Guid Id { get; set; }

    public DateTimeOffset At { get; set; }

    /// <summary>The signed-in caregiver, or null for an action by the system.</summary>
    public Guid? ActorCaregiverId { get; set; }

    /// <summary>What happened, for example "ViewedResident" or "AcknowledgedAlert".</summary>
    public string Action { get; set; } = string.Empty;

    public Guid? ResidentId { get; set; }

    /// <summary>Anything else worth recording, such as the alert involved.</summary>
    public string? Detail { get; set; }
}
